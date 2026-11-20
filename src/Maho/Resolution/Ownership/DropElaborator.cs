using System.Collections.Generic;

namespace Maho.Resolution.Ownership;

/// <summary>
/// Adds explicit cleanup operations before storage dies and before an owned value is replaced.
/// A conditional drop is the IR form of Maho's hidden stack initialization flag: code generation
/// materializes a flag only when control flow left the place maybe-initialized.
/// </summary>
internal sealed class DropElaborator
{
    private readonly OwnershipBody body;
    private readonly OwnershipFacts facts;
    private readonly OwnershipTypeClassifier ownership;

    public DropElaborator(ResolutionContext context, OwnershipFacts facts)
    {
        body = facts.Body;
        this.facts = facts;
        ownership = new OwnershipTypeClassifier(context);
    }

    /// <summary>
    /// Inserts explicit cleanup before whole-place replacement and scope death using converged move
    /// facts captured before elaboration mutates the body.
    /// </summary>
    /// <remarks>
    /// Each block is rebuilt rather than edited during enumeration. This keeps original statement
    /// ordering stable and lets the inserted Drop participate in the local replay state.
    /// </remarks>
    public void Elaborate()
    {
        foreach (var block in body.Blocks)
        {
            var input = facts.Incoming.TryGetValue(block, out var known)
                ? known
                : new Dictionary<Symbol, InitializationState>();
            var state = new Dictionary<Symbol, InitializationState>(input);
            var elaborated = new List<OwnershipStatement>(block.Statements.Count);
            foreach (var statement in block.Statements)
            {
                if (RequiresDropBefore(statement, state, out bool conditional))
                {
                    elaborated.Add(new OwnershipStatement(
                        OwnershipStatementKind.Drop,
                        statement.Place,
                        statement.Span,
                        statement.Source,
                        Conditional: conditional,
                        Origin: statement.Origin));
                    state[statement.Place.Root] = InitializationState.Uninitialized;
                }

                elaborated.Add(statement);
                Apply(state, statement);
            }
            block.Statements.Clear();
            block.Statements.AddRange(elaborated);
        }
    }

    /// <summary>
    /// Determines whether cleanup must be inserted immediately before a statement.
    /// </summary>
    /// <remarks>
    /// Only whole-place assignment/death is handled in this slice. A MaybeInitialized fact yields
    /// <paramref name="conditional"/>, which is the ownership-IR representation of a future hidden
    /// stack initialization flag.
    /// </remarks>
    private bool RequiresDropBefore(OwnershipStatement statement, IReadOnlyDictionary<Symbol, InitializationState> state, out bool conditional)
    {
        conditional = false;
        if (statement.Place.Projections.Count != 0 || statement.Kind is not (OwnershipStatementKind.Assign or OwnershipStatementKind.StorageDead))
            return false;
        if (!body.PlaceTypes.TryGetValue(statement.Place.Root, out var type) || !ownership.RequiresDrop(type))
            return false;

        var initialization = state.GetValueOrDefault(statement.Place.Root, InitializationState.Uninitialized);
        conditional = initialization == InitializationState.MaybeInitialized;
        return initialization is InitializationState.Initialized or InitializationState.MaybeInitialized;
    }

    /// <summary>Replays root-level effects while building a block containing explicit drops.</summary>
    private static void Apply(Dictionary<Symbol, InitializationState> state, OwnershipStatement statement)
    {
        switch (statement.Kind)
        {
            case OwnershipStatementKind.StorageLive:
            case OwnershipStatementKind.StorageDead:
            case OwnershipStatementKind.Drop:
                state[statement.Place.Root] = InitializationState.Uninitialized;
                break;
            case OwnershipStatementKind.Assign:
                state[statement.Place.Root] = InitializationState.Initialized;
                break;
            case OwnershipStatementKind.Use when statement.UseKind == OwnershipUseKind.Move:
                state[statement.Place.Root] = InitializationState.Moved;
                break;
        }
    }
}
