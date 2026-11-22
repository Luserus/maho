using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Maho.Resolution.Ownership;

/// <summary>
/// Forward data-flow analysis for definite initialization, field-by-field partial moves,
/// and whole-object move invariants.
/// </summary>
internal sealed class MoveAnalysis
{
    private readonly ResolutionContext context;
    private readonly OwnershipBody body;
    private readonly OwnershipTypeClassifier ownership;
    private readonly List<Place> allTrackedPlaces;
    private readonly Dictionary<OwnershipBlock, Dictionary<Place, InitializationState>> incoming = [];
    private readonly Dictionary<OwnershipBlock, Dictionary<Place, InitializationState>> outgoing = [];

    public MoveAnalysis(ResolutionContext context, OwnershipBody body)
    {
        this.context = context;
        this.body = body;
        ownership = new OwnershipTypeClassifier(context);
        allTrackedPlaces = DiscoverTrackedPlaces();
    }

    /// <summary>
    /// Solves forward initialization state to a fixed point, reports invalid post-move uses, and
    /// returns an immutable snapshot for later analyses.
    /// </summary>
    public OwnershipFacts Analyze()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var block in body.Blocks)
            {
                var input = JoinPredecessors(block);
                if (!incoming.TryGetValue(block, out var currentInput) || !StatesEqual(currentInput, input))
                {
                    incoming[block] = input;
                    changed = true;
                }

                var output = Transfer(input, block.Statements);
                if (!outgoing.TryGetValue(block, out var currentOutput) || !StatesEqual(currentOutput, output))
                {
                    outgoing[block] = output;
                    changed = true;
                }
            }
        }
        while (changed);

        ReportInvalidUses();
        return new OwnershipFacts(body, CloneMap(incoming), CloneMap(outgoing));
    }

    private List<Place> DiscoverTrackedPlaces()
    {
        var result = new List<Place>();
        var visited = new HashSet<SymbolHandle>();
        foreach (var (root, type) in body.PlaceTypes)
        {
            var rootPlace = new Place(root);
            result.Add(rootPlace);
            visited.Clear();
            CollectFieldPlaces(rootPlace, type, result, visited);
        }
        return result;
    }

    private void CollectFieldPlaces(Place parent, TypeRef type, List<Place> result, HashSet<SymbolHandle> visited)
    {
        if (type.Handle is not { } h)
            return;

        var unwrap = context.UnwrapAlias(h);
        if (!visited.Add(unwrap))
            return;

        if (ownership.IsOwningIndirection(type, out var pointeeType))
        {
            var derefPlace = parent.Dereference();
            result.Add(derefPlace);
            CollectFieldPlaces(derefPlace, pointeeType, result, visited);
        }

        foreach (var fieldHandle in GetTypeFields(type))
        {
            if (context.GetFieldSymbol(fieldHandle) is { } fieldSym)
            {
                var fieldPlace = parent.ProjectField(fieldHandle);
                result.Add(fieldPlace);

                // Only recurse into by-value structs that do not have custom Move/Destructor
                if (fieldSym.Type.SpecialType is null && !ownership.HasExplicitLifecycle(fieldSym.Type))
                {
                    CollectFieldPlaces(fieldPlace, fieldSym.Type, result, visited);
                }
            }
        }

        visited.Remove(unwrap);
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

    private Dictionary<Place, InitializationState> JoinPredecessors(OwnershipBlock block)
    {
        if (ReferenceEquals(block, body.Entry) || block.Predecessors.Count == 0)
            return NewDefaultState();

        var result = NewDefaultState();
        bool first = true;
        foreach (var predecessor in block.Predecessors)
        {
            var state = GetOrEmpty(outgoing, predecessor);
            if (first)
            {
                result = CopyState(state);
                first = false;
                continue;
            }

            foreach (var place in allTrackedPlaces)
                result[place] = Join(result.GetValueOrDefault(place), state.GetValueOrDefault(place));
        }
        return result;
    }

    private Dictionary<Place, InitializationState> Transfer(
        IReadOnlyDictionary<Place, InitializationState> input,
        IReadOnlyList<OwnershipStatement> statements)
    {
        var result = CopyState(input);
        foreach (var statement in statements)
            Apply(result, statement);
        return result;
    }

    private void Apply(Dictionary<Place, InitializationState> state, OwnershipStatement statement)
    {
        var targetPlace = statement.Place;
        switch (statement.Kind)
        {
            case OwnershipStatementKind.StorageLive:
            case OwnershipStatementKind.StorageDead:
            case OwnershipStatementKind.Drop:
                SetPlaceAndDescendants(state, targetPlace, InitializationState.Uninitialized);
                break;

            case OwnershipStatementKind.Assign:
                SetPlaceAndDescendants(state, targetPlace, InitializationState.Initialized);
                UpdateAncestorStates(state, targetPlace);
                break;

            case OwnershipStatementKind.Use when statement.UseKind == OwnershipUseKind.Move:
                SetPlaceAndDescendants(state, targetPlace, InitializationState.Moved);
                break;
        }
    }

    private void SetPlaceAndDescendants(Dictionary<Place, InitializationState> state, Place target, InitializationState newState)
    {
        state[target] = newState;
        foreach (var place in allTrackedPlaces)
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

    private void UpdateAncestorStates(Dictionary<Place, InitializationState> state, Place target)
    {
        var rootPlace = new Place(target.Root);
        if (body.PlaceTypes.TryGetValue(target.Root, out var rootType))
        {
            var fields = GetTypeFields(rootType);
            if (fields.Count > 0)
            {
                bool allInit = true;
                foreach (var f in fields)
                {
                    var fPlace = rootPlace.ProjectField(f);
                    if (state.GetValueOrDefault(fPlace) != InitializationState.Initialized)
                    {
                        allInit = false;
                        break;
                    }
                }
                if (allInit)
                    state[rootPlace] = InitializationState.Initialized;
            }
        }
    }

    private void ReportInvalidUses()
    {
        foreach (var block in body.Blocks)
        {
            var state = CopyState(GetOrEmpty(incoming, block));
            foreach (var statement in block.Statements)
            {
                if (statement.Kind == OwnershipStatementKind.Use && statement.UseKind == OwnershipUseKind.Move)
                {
                    // Check 1: If moving a field projection out of a type with custom Move or Destructor
                    if (statement.Place.Projections.Count > 0 &&
                        body.PlaceTypes.TryGetValue(statement.Place.Root, out var rootType) &&
                        ownership.HasExplicitLifecycle(rootType))
                    {
                        bool isAllowedOwningMove = false;
                        if (ownership.IsOwningIndirection(rootType, out var pointeeType))
                        {
                            if (!ownership.HasExplicitLifecycle(pointeeType))
                                isAllowedOwningMove = true;
                        }

                        if (!isAllowedOwningMove)
                        {
                            context.Diagnostics.ReportPartialMoveDisallowed(
                                statement.Span,
                                GetPlaceName(statement.Place),
                                rootType.ToQualifiedString(context),
                                statement.Source);
                        }
                    }
                    // Check 2: If moving the whole struct, but some fields were partially moved
                    else if (statement.Place.Projections.Count == 0 &&
                             body.PlaceTypes.TryGetValue(statement.Place.Root, out var wholeType))
                    {
                        var fields = GetTypeFields(wholeType);
                        bool isOwning = false;
                        if (ownership.IsOwningIndirection(wholeType, out var pType))
                        {
                            fields = GetTypeFields(pType);
                            isOwning = true;
                        }

                        if (fields.Count > 0)
                        {
                            bool hasPartialMove = false;
                            foreach (var f in fields)
                            {
                                var fPlace = isOwning
                                    ? statement.Place.Dereference().ProjectField(f)
                                    : statement.Place.ProjectField(f);
                                var fState = state.GetValueOrDefault(fPlace, InitializationState.Uninitialized);
                                if (fState is InitializationState.Moved or InitializationState.MaybeInitialized)
                                {
                                    hasPartialMove = true;
                                    break;
                                }
                            }
                            if (hasPartialMove)
                            {
                                context.Diagnostics.ReportCannotMovePartiallyMoved(
                                    statement.Span,
                                    statement.Place.Root.Name.Text,
                                    statement.Source);
                            }
                        }
                    }
                }

                if (statement.Kind is OwnershipStatementKind.Use or OwnershipStatementKind.Borrow)
                {
                    var current = state.GetValueOrDefault(statement.Place, InitializationState.Uninitialized);
                    if (current is InitializationState.Moved or InitializationState.MaybeInitialized)
                    {
                        context.Diagnostics.ReportUseAfterMove(statement.Span, GetPlaceName(statement.Place), statement.Source);
                    }
                }

                Apply(state, statement);
            }
        }
    }

    private string GetPlaceName(Place place)
    {
        if (place.Projections.Count == 0)
            return place.Root.Name.Text;

        var sb = new StringBuilder(place.Root.Name.Text);
        foreach (var proj in place.Projections)
        {
            if (proj.Kind == PlaceProjectionKind.Field && proj.Field is { } f)
            {
                var fName = context.GetFieldSymbol(f)?.Name.Text ?? f.ToString();
                sb.Append('.').Append(fName);
            }
            else if (proj.Kind == PlaceProjectionKind.Index)
                sb.Append("[]");
            else if (proj.Kind == PlaceProjectionKind.Dereference)
                sb.Append('*');
        }
        return sb.ToString();
    }

    private Dictionary<Place, InitializationState> NewDefaultState()
    {
        var dict = new Dictionary<Place, InitializationState>(allTrackedPlaces.Count);
        foreach (var place in allTrackedPlaces)
            dict[place] = InitializationState.Uninitialized;
        return dict;
    }

    private static Dictionary<Place, InitializationState> CopyState(IReadOnlyDictionary<Place, InitializationState> state) =>
        new(state);

    private IReadOnlyDictionary<Place, InitializationState> GetOrEmpty(
        IReadOnlyDictionary<OwnershipBlock, Dictionary<Place, InitializationState>> map,
        OwnershipBlock block) => map.TryGetValue(block, out var state) ? state : NewDefaultState();

    private static bool StatesEqual(IReadOnlyDictionary<Place, InitializationState> left, IReadOnlyDictionary<Place, InitializationState> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static InitializationState Join(InitializationState left, InitializationState right)
    {
        if (left == right)
            return left;
        if (left == InitializationState.Moved && right == InitializationState.Moved)
            return InitializationState.Moved;
        return InitializationState.MaybeInitialized;
    }

    private static Dictionary<OwnershipBlock, IReadOnlyDictionary<Place, InitializationState>> CloneMap(
        IReadOnlyDictionary<OwnershipBlock, Dictionary<Place, InitializationState>> map) =>
        map.ToDictionary(static pair => pair.Key, static pair => (IReadOnlyDictionary<Place, InitializationState>)new Dictionary<Place, InitializationState>(pair.Value));
}

/// <summary>Fixed-point ownership states retained for later borrow checking and drop elaboration.</summary>
internal sealed class OwnershipFacts
{
    public OwnershipBody Body { get; }
    public IReadOnlyDictionary<OwnershipBlock, IReadOnlyDictionary<Place, InitializationState>> Incoming { get; }
    public IReadOnlyDictionary<OwnershipBlock, IReadOnlyDictionary<Place, InitializationState>> Outgoing { get; }

    internal OwnershipFacts(
        OwnershipBody body,
        IReadOnlyDictionary<OwnershipBlock, IReadOnlyDictionary<Place, InitializationState>> incoming,
        IReadOnlyDictionary<OwnershipBlock, IReadOnlyDictionary<Place, InitializationState>> outgoing)
    {
        Body = body;
        Incoming = incoming;
        Outgoing = outgoing;
    }
}
