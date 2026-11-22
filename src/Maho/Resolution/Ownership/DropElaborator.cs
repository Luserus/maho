using System.Collections.Generic;

namespace Maho.Resolution.Ownership;

/// <summary>
/// Adds explicit cleanup operations before storage dies and before an owned value is replaced.
/// For composite types without custom destructors, cleanup is elaborated field-by-field, omitting
/// moved-out fields and generating conditional drops for maybe-initialized fields.
/// </summary>
internal sealed class DropElaborator
{
    private readonly ResolutionContext context;
    private readonly OwnershipBody body;
    private readonly OwnershipFacts facts;
    private readonly OwnershipTypeClassifier ownership;

    public DropElaborator(ResolutionContext context, OwnershipFacts facts)
    {
        this.context = context;
        body = facts.Body;
        this.facts = facts;
        ownership = new OwnershipTypeClassifier(context);
    }

    /// <summary>
    /// Inserts explicit cleanup before whole-place replacement and scope death using converged move
    /// facts captured before elaboration mutates the body.
    /// </summary>
    public void Elaborate()
    {
        foreach (var block in body.Blocks)
        {
            var input = facts.Incoming.TryGetValue(block, out var known)
                ? known
                : new Dictionary<Place, InitializationState>();
            var state = new Dictionary<Place, InitializationState>(input);
            var elaborated = new List<OwnershipStatement>(block.Statements.Count);
            foreach (var statement in block.Statements)
            {
                if (statement.Place.Projections.Count == 0 &&
                    statement.Kind is OwnershipStatementKind.Assign or OwnershipStatementKind.StorageDead &&
                    body.PlaceTypes.TryGetValue(statement.Place.Root, out var type) &&
                    (ownership.RequiresDrop(type) || (ownership.IsOwningIndirection(type, out var pDropType) && ownership.RequiresDrop(pDropType))))
                {
                    // Case 0: Owning indirection (e.g. Box<T>)
                    if (ownership.IsOwningIndirection(type, out var pointeeType))
                    {
                        if (ownership.RequiresDrop(pointeeType))
                        {
                            var pointeeFields = GetTypeFields(pointeeType);
                            if (pointeeFields.Count > 0)
                            {
                                foreach (var f in pointeeFields)
                                {
                                    if (context.GetFieldSymbol(f) is { } fSym && ownership.RequiresDrop(fSym.Type))
                                    {
                                        var fPlace = statement.Place.Dereference().ProjectField(f);
                                        var fInit = state.GetValueOrDefault(fPlace, InitializationState.Uninitialized);
                                        if (fInit is InitializationState.Initialized or InitializationState.MaybeInitialized)
                                        {
                                            elaborated.Add(new OwnershipStatement(
                                                OwnershipStatementKind.Drop,
                                                fPlace,
                                                statement.Span,
                                                statement.Source,
                                                Conditional: fInit == InitializationState.MaybeInitialized,
                                                Origin: statement.Origin));
                                            state[fPlace] = InitializationState.Uninitialized;
                                        }
                                    }
                                }
                            }
                            else if (ownership.HasExplicitDestructor(pointeeType))
                            {
                                var derefPlace = statement.Place.Dereference();
                                elaborated.Add(new OwnershipStatement(
                                    OwnershipStatementKind.Drop,
                                    derefPlace,
                                    statement.Span,
                                    statement.Source,
                                    Origin: statement.Origin));
                            }
                        }

                        // Then drop the container itself
                        if (ownership.RequiresDrop(type))
                        {
                            var init = state.GetValueOrDefault(statement.Place, InitializationState.Uninitialized);
                            if (init is InitializationState.Initialized or InitializationState.MaybeInitialized)
                            {
                                elaborated.Add(new OwnershipStatement(
                                    OwnershipStatementKind.Drop,
                                    statement.Place,
                                    statement.Span,
                                    statement.Source,
                                    Conditional: init == InitializationState.MaybeInitialized,
                                    Origin: statement.Origin));
                                state[statement.Place] = InitializationState.Uninitialized;
                            }
                        }
                    }
                    // Case 1: Monolithic drop if type has explicit Destructor
                    else if (ownership.HasExplicitDestructor(type))
                    {
                        var init = state.GetValueOrDefault(statement.Place, InitializationState.Uninitialized);
                        if (init is InitializationState.Initialized or InitializationState.MaybeInitialized)
                        {
                            elaborated.Add(new OwnershipStatement(
                                OwnershipStatementKind.Drop,
                                statement.Place,
                                statement.Span,
                                statement.Source,
                                Conditional: init == InitializationState.MaybeInitialized,
                                Origin: statement.Origin));
                            state[statement.Place] = InitializationState.Uninitialized;
                        }
                    }
                    // Case 2: Composite struct without explicit Destructor: elaborate field-by-field drops
                    else
                    {
                        var fields = GetTypeFields(type);
                        if (fields.Count > 0)
                        {
                            foreach (var f in fields)
                            {
                                if (context.GetFieldSymbol(f) is { } fSym && ownership.RequiresDrop(fSym.Type))
                                {
                                    var fPlace = statement.Place.ProjectField(f);
                                    var fInit = state.GetValueOrDefault(fPlace, InitializationState.Uninitialized);
                                    if (fInit is InitializationState.Initialized or InitializationState.MaybeInitialized)
                                    {
                                        elaborated.Add(new OwnershipStatement(
                                            OwnershipStatementKind.Drop,
                                            fPlace,
                                            statement.Span,
                                            statement.Source,
                                            Conditional: fInit == InitializationState.MaybeInitialized,
                                            Origin: statement.Origin));
                                        state[fPlace] = InitializationState.Uninitialized;
                                    }
                                }
                            }
                        }
                        else
                        {
                            var init = state.GetValueOrDefault(statement.Place, InitializationState.Uninitialized);
                            if (init is InitializationState.Initialized or InitializationState.MaybeInitialized)
                            {
                                elaborated.Add(new OwnershipStatement(
                                    OwnershipStatementKind.Drop,
                                    statement.Place,
                                    statement.Span,
                                    statement.Source,
                                    Conditional: init == InitializationState.MaybeInitialized,
                                    Origin: statement.Origin));
                                state[statement.Place] = InitializationState.Uninitialized;
                            }
                        }
                    }
                }

                elaborated.Add(statement);
                Apply(state, statement);
            }
            block.Statements.Clear();
            block.Statements.AddRange(elaborated);
        }
    }

    private IReadOnlyList<SymbolHandle> GetTypeFields(TypeRef type)
    {
        if (type.Handle is { } h)
        {
            var unwrap = context.UnwrapAlias(h);
            var sym = context.GetSymbol(unwrap);
            if (sym is ProductTypeSymbol p) return p.Fields;
            if (sym is MemberProductTypeSymbol mp) return mp.Fields;
        }
        return System.Array.Empty<SymbolHandle>();
    }

    private static void Apply(Dictionary<Place, InitializationState> state, OwnershipStatement statement)
    {
        switch (statement.Kind)
        {
            case OwnershipStatementKind.StorageLive:
            case OwnershipStatementKind.StorageDead:
            case OwnershipStatementKind.Drop:
                SetPlaceAndDescendants(state, statement.Place, InitializationState.Uninitialized);
                break;
            case OwnershipStatementKind.Assign:
                SetPlaceAndDescendants(state, statement.Place, InitializationState.Initialized);
                break;
            case OwnershipStatementKind.Use when statement.UseKind == OwnershipUseKind.Move:
                SetPlaceAndDescendants(state, statement.Place, InitializationState.Moved);
                break;
        }
    }

    private static void SetPlaceAndDescendants(Dictionary<Place, InitializationState> state, Place target, InitializationState newState)
    {
        state[target] = newState;
        foreach (var place in System.Linq.Enumerable.ToList(state.Keys))
        {
            if (IsStrictDescendant(place, target))
                state[place] = newState;
        }
    }

    private static bool IsStrictDescendant(Place candidate, Place ancestor)
    {
        if (!ReferenceEquals(candidate.Root, ancestor.Root))
            return false;
        if (candidate.Projections.Count <= ancestor.Projections.Count)
            return false;

        for (int i = 0; i < ancestor.Projections.Count; i++)
        {
            if (!candidate.Projections[i].Equals(ancestor.Projections[i]))
                return false;
        }
        return true;
    }
}
