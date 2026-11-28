using System.Collections.Generic;
using System.Linq;
using System.Text;
using Maho.Resolution.Passes;

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
    private readonly Dictionary<Place, TypeRef> placeTypes = [];
    private readonly SymbolHandle? callerType;
    private readonly ExtensionSymbol? callerExtension;
    private readonly Dictionary<OwnershipBlock, Dictionary<Place, InitializationState>> incoming = [];
    private readonly Dictionary<OwnershipBlock, Dictionary<Place, InitializationState>> outgoing = [];
    private readonly Dictionary<OwnershipBlock, HashSet<Place>> incomingMoved = [];
    private readonly Dictionary<OwnershipBlock, HashSet<Place>> outgoingMoved = [];

    public MoveAnalysis(ResolutionContext context, OwnershipBody body)
    {
        this.context = context;
        this.body = body;
        ownership = new OwnershipTypeClassifier(context);
        VisibilityAnalysisPass.ResolveEnclosingContext(context, body.ContainingSymbol, out callerType, out callerExtension);
        allTrackedPlaces = DiscoverTrackedPlaces();
    }

    /// <summary>
    /// Solves forward initialization state to a fixed point, reports invalid uninitialized or
    /// post-move uses, and returns an immutable snapshot for later analyses.
    /// </summary>
    public OwnershipFacts Analyze()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var block in body.Blocks)
            {
                var (input, inMoved) = JoinPredecessors(block);
                if (!incoming.TryGetValue(block, out var currentInput) || !StatesEqual(currentInput, input) ||
                    !incomingMoved.TryGetValue(block, out var currentInMoved) || !currentInMoved.SetEquals(inMoved))
                {
                    incoming[block] = input;
                    incomingMoved[block] = inMoved;
                    changed = true;
                }

                var (output, outMoved) = Transfer(input, inMoved, block.Statements);
                if (!outgoing.TryGetValue(block, out var currentOutput) || !StatesEqual(currentOutput, output) ||
                    !outgoingMoved.TryGetValue(block, out var currentOutMoved) || !currentOutMoved.SetEquals(outMoved))
                {
                    outgoing[block] = output;
                    outgoingMoved[block] = outMoved;
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
            placeTypes[rootPlace] = type;
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
            placeTypes[derefPlace] = pointeeType;
            CollectFieldPlaces(derefPlace, pointeeType, result, visited);
        }

        Dictionary<SymbolHandle, TypeRef> genericSubs = BuildGenericSubstitutionMap(type);
        foreach (var fieldHandle in GetTypeFields(type))
        {
            if (context.GetFieldSymbol(fieldHandle) is { } fieldSym)
            {
                var fieldPlace = parent.ProjectField(fieldHandle);
                var fieldType = genericSubs.Count > 0 ? fieldSym.Type.Substitute(genericSubs) : fieldSym.Type;
                result.Add(fieldPlace);
                placeTypes[fieldPlace] = fieldType;

                // Only recurse into by-value structs that do not have custom Move/Destructor
                if ((fieldType.SpecialType is null or GenericSemanticType) && !ownership.HasExplicitLifecycle(fieldType))
                {
                    CollectFieldPlaces(fieldPlace, fieldType, result, visited);
                }
            }
        }

        visited.Remove(unwrap);
    }

    private Dictionary<SymbolHandle, TypeRef> BuildGenericSubstitutionMap(TypeRef targetType)
    {
        var subs = new Dictionary<SymbolHandle, TypeRef>();
        if (targetType.SpecialType is GenericSemanticType gst && targetType.Handle is { } h)
        {
            var sym = context.GetSymbol(context.UnwrapAlias(h));
            IReadOnlyList<SymbolHandle> gpHandles = sym switch
            {
                TypeSymbol ts => ts.GenericParameters,
                NestedTypeSymbol nts => nts.GenericParameters,
                _ => []
            };
            for (int i = 0; i < gpHandles.Count && i < gst.TypeArguments.Count; i++)
                subs[gpHandles[i]] = gst.TypeArguments[i];
        }
        return subs;
    }

    private IReadOnlyList<SymbolHandle> GetTypeFields(TypeRef type) =>
        ownership.GetAllStructFields(type);

    private bool IsPlaceAlwaysInitialized(Place place) =>
        placeTypes.TryGetValue(place, out var type) && ownership.IsZeroSizedEmptyStruct(type);

    private static InitializationState GetEffectiveState(
        IReadOnlyDictionary<Place, InitializationState> state,
        IReadOnlySet<Place> mayBeMoved,
        Place place,
        out bool placeMayBeMoved)
    {
        if (state.TryGetValue(place, out var exact))
        {
            placeMayBeMoved = mayBeMoved.Contains(place);
            return exact;
        }

        for (int depth = place.Projections.Count - 1; depth >= 0; depth--)
        {
            var prefix = depth == 0
                ? new Place(place.Root)
                : new Place(place.Root, place.Projections.Take(depth).ToArray());

            if (state.TryGetValue(prefix, out var prefixState))
            {
                placeMayBeMoved = mayBeMoved.Contains(prefix);
                return prefixState;
            }
        }

        placeMayBeMoved = false;
        return InitializationState.Uninitialized;
    }

    private (Dictionary<Place, InitializationState> Init, HashSet<Place> MayBeMoved) JoinPredecessors(OwnershipBlock block)
    {
        if (ReferenceEquals(block, body.Entry) || block.Predecessors.Count == 0)
            return (NewDefaultState(), []);

        var result = NewDefaultState();
        var movedResult = new HashSet<Place>();
        bool first = true;
        foreach (var predecessor in block.Predecessors)
        {
            if (!outgoing.TryGetValue(predecessor, out var state))
                continue;

            var predMoved = outgoingMoved.GetValueOrDefault(predecessor);
            if (predMoved is not null)
                movedResult.UnionWith(predMoved);

            if (first)
            {
                result = CopyState(state);
                first = false;
                continue;
            }

            foreach (var place in allTrackedPlaces)
                result[place] = Join(result.GetValueOrDefault(place), state.GetValueOrDefault(place));
        }
        return (result, movedResult);
    }

    private (Dictionary<Place, InitializationState> Init, HashSet<Place> MayBeMoved) Transfer(
        IReadOnlyDictionary<Place, InitializationState> input,
        IReadOnlyCollection<Place> inMoved,
        IReadOnlyList<OwnershipStatement> statements)
    {
        var result = CopyState(input);
        var moved = new HashSet<Place>(inMoved);
        foreach (var statement in statements)
            Apply(result, moved, statement);
        return (result, moved);
    }

    private void Apply(Dictionary<Place, InitializationState> state, HashSet<Place> mayBeMoved, OwnershipStatement statement)
    {
        var targetPlace = statement.Place;
        switch (statement.Kind)
        {
            case OwnershipStatementKind.StorageLive:
                SetPlaceAndDescendantsForStorageLive(state, mayBeMoved, targetPlace);
                break;

            case OwnershipStatementKind.StorageDead:
            case OwnershipStatementKind.Drop:
                SetPlaceAndDescendants(state, mayBeMoved, targetPlace, InitializationState.Uninitialized, markMoved: false);
                break;

            case OwnershipStatementKind.Assign:
                SetPlaceAndDescendants(state, mayBeMoved, targetPlace, InitializationState.Initialized, markMoved: false);
                UpdateAncestorStates(state, mayBeMoved, targetPlace);
                break;

            case OwnershipStatementKind.Use when statement.UseKind == OwnershipUseKind.Move:
                SetPlaceAndDescendants(state, mayBeMoved, targetPlace, InitializationState.Moved, markMoved: true);
                break;
        }
    }

    private void SetPlaceAndDescendantsForStorageLive(
        Dictionary<Place, InitializationState> state,
        HashSet<Place> mayBeMoved,
        Place target)
    {
        state[target] = IsPlaceAlwaysInitialized(target) ? InitializationState.Initialized : InitializationState.Uninitialized;
        mayBeMoved.Remove(target);
        foreach (var place in allTrackedPlaces)
        {
            if (IsStrictDescendant(place, target))
            {
                state[place] = IsPlaceAlwaysInitialized(place) ? InitializationState.Initialized : InitializationState.Uninitialized;
                mayBeMoved.Remove(place);
            }
        }
    }

    private void SetPlaceAndDescendants(
        Dictionary<Place, InitializationState> state,
        HashSet<Place> mayBeMoved,
        Place target,
        InitializationState newState,
        bool markMoved)
    {
        state[target] = newState;
        if (markMoved) mayBeMoved.Add(target);
        else mayBeMoved.Remove(target);

        foreach (var place in allTrackedPlaces)
        {
            if (IsStrictDescendant(place, target))
            {
                state[place] = newState;
                if (markMoved) mayBeMoved.Add(place);
                else mayBeMoved.Remove(place);
            }
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

    private void UpdateAncestorStates(Dictionary<Place, InitializationState> state, HashSet<Place> mayBeMoved, Place target)
    {
        for (int depth = target.Projections.Count - 1; depth >= 0; depth--)
        {
            var ancestor = depth == 0
                ? new Place(target.Root)
                : new Place(target.Root, target.Projections.Take(depth).ToArray());

            if (!placeTypes.TryGetValue(ancestor, out var ancestorType))
                continue;

            var fields = GetTypeFields(ancestorType);
            if (fields.Count == 0)
                continue;

            bool allInit = true;
            foreach (var f in fields)
            {
                var fPlace = ancestor.ProjectField(f);
                if (state.GetValueOrDefault(fPlace) != InitializationState.Initialized)
                {
                    allInit = false;
                    break;
                }
            }
            if (allInit)
            {
                state[ancestor] = InitializationState.Initialized;
                mayBeMoved.Remove(ancestor);
            }
        }
    }

    private void ReportInvalidUses()
    {
        foreach (var block in body.Blocks)
        {
            var state = CopyState(GetOrEmpty(incoming, block));
            var mayBeMoved = new HashSet<Place>(incomingMoved.GetValueOrDefault(block) ?? []);
            foreach (var statement in block.Statements)
            {
                if (statement.Kind == OwnershipStatementKind.Assign && statement.Place.Projections.Count > 0)
                {
                    for (int depth = 0; depth < statement.Place.Projections.Count; depth++)
                    {
                        if (statement.Place.Projections[depth].Kind != PlaceProjectionKind.Field)
                            continue;

                        var containerPlace = depth == 0
                            ? new Place(statement.Place.Root)
                            : new Place(statement.Place.Root, statement.Place.Projections.Take(depth).ToArray());

                        if (GetEffectiveState(state, mayBeMoved, containerPlace, out _) != InitializationState.Initialized &&
                            placeTypes.TryGetValue(containerPlace, out var containerType) &&
                            VisibilityAnalysisPass.TryFindInaccessibleField(context, containerType, callerType, callerExtension, out var inaccessibleField))
                        {
                            context.Diagnostics.ReportStructCannotBePiecewiseInitialized(
                                statement.Span,
                                containerType.ToDisplayString(context),
                                inaccessibleField!.Name.Text,
                                statement.Source);
                            break;
                        }
                    }
                }

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
                                if (fState is InitializationState.Moved ||
                                    (fState == InitializationState.MaybeInitialized && mayBeMoved.Contains(fPlace)))
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
                    var current = GetEffectiveState(state, mayBeMoved, statement.Place, out var placeMoved);
                    if (statement.IsReturnUse &&
                        ownership.IsAllocationOperatorMethod(body.ContainingSymbol, out var allocTargetType) &&
                        TryHandleAllocationOperatorReturnUse(state, mayBeMoved, statement, current, placeMoved, allocTargetType))
                    {
                        Apply(state, mayBeMoved, statement);
                        continue;
                    }

                    if (current == InitializationState.Moved)
                    {
                        context.Diagnostics.ReportUseAfterMove(statement.Span, GetPlaceName(statement.Place), statement.Source);
                    }
                    else if (current == InitializationState.MaybeInitialized)
                    {
                        if (placeMoved)
                            context.Diagnostics.ReportUseAfterMove(statement.Span, GetPlaceName(statement.Place), statement.Source);
                        else
                            context.Diagnostics.ReportUseOfPossiblyUninitializedValue(statement.Span, GetPlaceName(statement.Place), statement.Source);
                    }
                    else if (current == InitializationState.Uninitialized && !IsPlaceAlwaysInitialized(statement.Place))
                    {
                        context.Diagnostics.ReportUseOfUninitializedValue(statement.Span, GetPlaceName(statement.Place), statement.Source);
                    }
                }

                Apply(state, mayBeMoved, statement);
            }
        }
    }

    private bool TryHandleAllocationOperatorReturnUse(
        Dictionary<Place, InitializationState> state,
        HashSet<Place> mayBeMoved,
        OwnershipStatement statement,
        InitializationState current,
        bool placeMoved,
        TypeRef allocTargetType)
    {
        if (current == InitializationState.Moved || placeMoved || HasMovedDescendant(state, mayBeMoved, statement.Place))
            return false;

        if (current == InitializationState.Initialized)
            return false;

        if (!placeTypes.TryGetValue(statement.Place, out var placeType))
            return false;

        return MatchesAllocationTargetType(placeType, allocTargetType);
    }

    private bool HasMovedDescendant(
        Dictionary<Place, InitializationState> state,
        HashSet<Place> mayBeMoved,
        Place ancestor)
    {
        foreach (var candidate in allTrackedPlaces)
        {
            if (!IsStrictDescendant(candidate, ancestor))
                continue;
            if (GetEffectiveState(state, mayBeMoved, candidate, out var moved) == InitializationState.Moved || moved)
                return true;
        }
        return false;
    }

    private bool MatchesAllocationTargetType(TypeRef actual, TypeRef target)
    {
        if (actual.Equals(target))
            return true;
        if (actual.Handle is { } ah && target.Handle is { } th)
            return context.UnwrapAlias(ah) == context.UnwrapAlias(th);
        return false;
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
        {
            dict[place] = IsPlaceAlwaysInitialized(place)
                ? InitializationState.Initialized
                : InitializationState.Uninitialized;
        }
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
