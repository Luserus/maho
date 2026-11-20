using System.Collections.Generic;
using System.Linq;

namespace Maho.Resolution.Ownership;

/// <summary>
/// Forward data-flow analysis for definite initialization and implicit moves.  Borrow regions are
/// represented in the IR already; this first analysis establishes the move-state invariant on
/// which region inference and drop elaboration build.
/// </summary>
internal sealed class MoveAnalysis
{
    private readonly ResolutionContext context;
    private readonly OwnershipBody body;
    private readonly Dictionary<OwnershipBlock, Dictionary<Symbol, InitializationState>> incoming = [];
    private readonly Dictionary<OwnershipBlock, Dictionary<Symbol, InitializationState>> outgoing = [];

    public MoveAnalysis(ResolutionContext context, OwnershipBody body)
    {
        this.context = context;
        this.body = body;
    }

    /// <summary>
    /// Solves forward initialization state to a fixed point, reports invalid post-move uses, and
    /// returns an immutable snapshot for later analyses.
    /// </summary>
    /// <remarks>
    /// The first traversal has no diagnostics because loop/back-edge facts can still change. Only
    /// after convergence are uses replayed against stable incoming states.
    /// </remarks>
    public OwnershipFacts Analyze()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var block in body.Blocks)
            {
                var input = JoinPredecessors(block);
                if (!StatesEqual(GetOrEmpty(incoming, block), input))
                {
                    incoming[block] = input;
                    changed = true;
                }

                var output = Transfer(input, block.Statements);
                if (!StatesEqual(GetOrEmpty(outgoing, block), output))
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

    /// <summary>
    /// Computes the state on entry to <paramref name="block"/> by joining all predecessor exits.
    /// </summary>
    /// <remarks>
    /// Entry/unreachable blocks begin with every known root uninitialized. A disagreement at a CFG
    /// merge becomes <see cref="InitializationState.MaybeInitialized"/>, which represents a
    /// conditional move or initialization and later drives conditional-drop lowering.
    /// </remarks>
    private Dictionary<Symbol, InitializationState> JoinPredecessors(OwnershipBlock block)
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

            foreach (var place in body.PlaceTypes.Keys)
                result[place] = Join(result.GetValueOrDefault(place), state.GetValueOrDefault(place));
        }
        return result;
    }

    /// <summary>Replays ordered storage effects to derive a block's outgoing state.</summary>
    private Dictionary<Symbol, InitializationState> Transfer(
        IReadOnlyDictionary<Symbol, InitializationState> input,
        IReadOnlyList<OwnershipStatement> statements)
    {
        var result = CopyState(input);
        foreach (var statement in statements)
            Apply(result, statement);
        return result;
    }

    /// <summary>
    /// Applies the root-level initialization transfer function for one ownership operation.
    /// </summary>
    /// <remarks>
    /// Projection effects are deliberately not distinguished yet; the partial-move pass will
    /// refine this root-state transfer into per-projection state.
    /// </remarks>
    private static void Apply(Dictionary<Symbol, InitializationState> state, OwnershipStatement statement)
    {
        Symbol root = statement.Place.Root;
        switch (statement.Kind)
        {
            case OwnershipStatementKind.StorageLive:
            case OwnershipStatementKind.StorageDead:
                state[root] = InitializationState.Uninitialized;
                break;
            case OwnershipStatementKind.Assign:
                state[root] = InitializationState.Initialized;
                break;
            case OwnershipStatementKind.Drop:
                state[root] = InitializationState.Uninitialized;
                break;
            case OwnershipStatementKind.Use when statement.UseKind == OwnershipUseKind.Move:
                state[root] = InitializationState.Moved;
                break;
        }
    }

    /// <summary>
    /// Replays converged block inputs and reports reads, moves, or borrows after a definite or
    /// conditional move.
    /// </summary>
    /// <remarks>
    /// This intentionally does not issue uninitialized-value diagnostics yet. Field-by-field
    /// construction is legal, and reporting before projection-aware state exists would be wrong.
    /// </remarks>
    private void ReportInvalidUses()
    {
        foreach (var block in body.Blocks)
        {
            var state = CopyState(GetOrEmpty(incoming, block));
            foreach (var statement in block.Statements)
            {
                if (statement.Kind is OwnershipStatementKind.Use or OwnershipStatementKind.Borrow)
                {
                    var current = state.GetValueOrDefault(statement.Place.Root, InitializationState.Uninitialized);
                    if (current is InitializationState.Moved or InitializationState.MaybeInitialized)
                        context.Diagnostics.ReportUseAfterMove(statement.Span, GetPlaceName(statement.Place), statement.Source);
                    // Definite-initialization reporting follows the construction/field/drop
                    // elaboration slice.  The language currently accepts field-by-field
                    // initialization of an otherwise uninitialized aggregate, so emitting this
                    // diagnostic before projection-aware move paths exist would be unsound.
                }
                Apply(state, statement);
            }
        }
    }

    private static string GetPlaceName(Place place) => place.Root.Name.Text;

    private Dictionary<Symbol, InitializationState> NewDefaultState() =>
        body.PlaceTypes.Keys.ToDictionary(static key => key, static _ => InitializationState.Uninitialized);

    private static Dictionary<Symbol, InitializationState> CopyState(IReadOnlyDictionary<Symbol, InitializationState> state) =>
        new(state);

    private static IReadOnlyDictionary<Symbol, InitializationState> GetOrEmpty(
        IReadOnlyDictionary<OwnershipBlock, Dictionary<Symbol, InitializationState>> map,
        OwnershipBlock block) => map.TryGetValue(block, out var state) ? state : EmptyState.Instance;

    private static bool StatesEqual(IReadOnlyDictionary<Symbol, InitializationState> left, IReadOnlyDictionary<Symbol, InitializationState> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    /// <summary>
    /// Merges one root's facts from two feasible incoming paths.
    /// </summary>
    /// <returns>
    /// The common state when both paths agree; otherwise <see cref="InitializationState.MaybeInitialized"/>.
    /// </returns>
    private static InitializationState Join(InitializationState left, InitializationState right)
    {
        if (left == right)
            return left;
        if (left == InitializationState.Moved && right == InitializationState.Moved)
            return InitializationState.Moved;
        return InitializationState.MaybeInitialized;
    }

    private static Dictionary<OwnershipBlock, IReadOnlyDictionary<Symbol, InitializationState>> CloneMap(
        IReadOnlyDictionary<OwnershipBlock, Dictionary<Symbol, InitializationState>> map) =>
        map.ToDictionary(static pair => pair.Key, static pair => (IReadOnlyDictionary<Symbol, InitializationState>)new Dictionary<Symbol, InitializationState>(pair.Value));

    private sealed class EmptyState : IReadOnlyDictionary<Symbol, InitializationState>
    {
        public static EmptyState Instance { get; } = new();
        public int Count => 0;
        public IEnumerable<Symbol> Keys => [];
        public IEnumerable<InitializationState> Values => [];
        public InitializationState this[Symbol key] => InitializationState.Uninitialized;
        public bool ContainsKey(Symbol key) => false;
        public IEnumerator<KeyValuePair<Symbol, InitializationState>> GetEnumerator() => Enumerable.Empty<KeyValuePair<Symbol, InitializationState>>().GetEnumerator();
        public bool TryGetValue(Symbol key, out InitializationState value) { value = InitializationState.Uninitialized; return false; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>Fixed-point ownership states retained for later borrow checking and drop elaboration.</summary>
internal sealed class OwnershipFacts
{
    public OwnershipBody Body { get; }
    public IReadOnlyDictionary<OwnershipBlock, IReadOnlyDictionary<Symbol, InitializationState>> Incoming { get; }
    public IReadOnlyDictionary<OwnershipBlock, IReadOnlyDictionary<Symbol, InitializationState>> Outgoing { get; }

    internal OwnershipFacts(
        OwnershipBody body,
        IReadOnlyDictionary<OwnershipBlock, IReadOnlyDictionary<Symbol, InitializationState>> incoming,
        IReadOnlyDictionary<OwnershipBlock, IReadOnlyDictionary<Symbol, InitializationState>> outgoing)
    {
        Body = body;
        Incoming = incoming;
        Outgoing = outgoing;
    }
}
