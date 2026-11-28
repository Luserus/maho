using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Text;

namespace Maho.Resolution.Ownership;

/// <summary>
/// Checks exclusivity of safe aliases and references.  Loans are propagated through the lowered
/// CFG, so branches and loops are handled soundly.  Backward liveness of the alias/reference
/// holder gives named loans non-lexical end points: they end after their final use, not merely at
/// the closing brace.
/// </summary>
internal sealed class BorrowAnalysis
{
    private readonly ResolutionContext context;
    private readonly OwnershipBody body;
    private readonly Dictionary<OwnershipBlock, HashSet<Loan>> incoming = [];
    private readonly Dictionary<OwnershipBlock, HashSet<Loan>> outgoing = [];
    private readonly Dictionary<OwnershipBlock, HashSet<Symbol>> liveIn = [];
    private readonly Dictionary<OwnershipBlock, HashSet<Symbol>> liveOut = [];
    private readonly Dictionary<OwnershipStatement, HashSet<Symbol>> liveAfter = [];

    public BorrowAnalysis(ResolutionContext context, OwnershipBody body)
    {
        this.context = context;
        this.body = body;
    }

    /// <summary>
    /// Computes non-lexical holder liveness, solves active loans forward to a fixed point, then
    /// performs one stable diagnostic traversal.
    /// </summary>
    /// <remarks>
    /// A two-stage solution matters for loops: the forward loan set needs converged liveness, and
    /// diagnostics must not be emitted repeatedly while either fixed point is changing.
    /// </remarks>
    public void Analyze()
    {
        ComputeLoanLiveness();
        bool changed;
        do
        {
            changed = false;
            foreach (var block in body.Blocks)
            {
                var input = Join(block);
                if (!SetEquals(Get(incoming, block), input))
                {
                    incoming[block] = input;
                    changed = true;
                }

                var output = Transfer(input, block, report: false);
                if (!SetEquals(Get(outgoing, block), output))
                {
                    outgoing[block] = output;
                    changed = true;
                }
            }
        }
        while (changed);

        foreach (var block in body.Blocks)
            Transfer(Get(incoming, block), block, report: true);
    }

    /// <summary>
    /// Unions all active predecessor loans because a loan live on any feasible path is live at the
    /// merge until its holder is proven dead.
    /// </summary>
    private HashSet<Loan> Join(OwnershipBlock block)
    {
        var result = new HashSet<Loan>();
        foreach (var predecessor in block.Predecessors)
            result.UnionWith(Get(outgoing, predecessor));
        return result;
    }

    /// <summary>
    /// Applies ownership operations to the active loan set for one block.
    /// </summary>
    /// <remarks>
    /// Storage death kills loans owned by that holder. A new mutable loan conflicts with every
    /// overlapping loan; a shared loan conflicts only with a mutable one. Moves and assignments
    /// conflict with all overlapping loans. Holder liveness then retires loans at their final use.
    /// </remarks>
    private HashSet<Loan> Transfer(IEnumerable<Loan> input, OwnershipBlock block, bool report)
    {
        var active = new HashSet<Loan>(input);
        foreach (var statement in block.Statements)
        {
            if (statement.Kind == OwnershipStatementKind.StorageDead)
            {
                active.RemoveWhere(loan => ReferenceEquals(loan.Owner, statement.Place.Root));
                var referentLoans = active.Where(loan => Overlaps(loan.Referent, statement.Place)).ToList();
                foreach (var loan in referentLoans)
                {
                    if (report && statement.Place.Root is LocalVariableSymbol local)
                    {
                        context.Diagnostics.ReportBorrowEscapesLocal(
                            loan.Origin,
                            local.Name.Text,
                            statement.Span,
                            loan.Source,
                            statement.Source);
                    }
                    active.Remove(loan);
                }
                continue;
            }

            if (statement.Kind == OwnershipStatementKind.Borrow)
            {
                var kind = statement.BorrowKind ?? BorrowKind.Shared;
                foreach (var loan in active.Where(loan => Overlaps(loan.Referent, statement.Place)))
                {
                    bool conflicts = kind == BorrowKind.Mutable || loan.Kind == BorrowKind.Mutable;
                    if (conflicts && report)
                        ReportConflict(statement, loan, kind == BorrowKind.Mutable ? "mutably borrow" : "borrow");
                }

                // A borrow passed directly to a call ends with the call; only a local/parameter
                // owner creates a loan that can survive into a subsequent program point.
                if (statement.LoanOwner is { } owner)
                    active.Add(new Loan(statement.Place, kind, owner, statement.Span, statement.Source));
                EndDeadLoans(active, statement);
                continue;
            }

            if (statement.Kind is OwnershipStatementKind.Assign or OwnershipStatementKind.Use)
            {
                bool isMove = statement.Kind == OwnershipStatementKind.Use && statement.UseKind == OwnershipUseKind.Move;
                bool isWrite = statement.Kind == OwnershipStatementKind.Assign;
                if (isMove || isWrite)
                {
                    foreach (var loan in active.Where(loan => Overlaps(loan.Referent, statement.Place)))
                    {
                        if (report)
                            ReportConflict(statement, loan, isMove ? "move" : "assign to");
                    }
                }
                if (isWrite &&
                    statement.Place.Projections.Count == 0 &&
                    (!body.PlaceTypes.TryGetValue(statement.Place.Root, out var targetType) ||
                     TypeRef.UnwrapDyn(targetType).SpecialType is not AliasSemanticType))
                {
                    active.RemoveWhere(loan => ReferenceEquals(loan.Owner, statement.Place.Root));
                }
            }

            EndDeadLoans(active, statement);
        }
        active.RemoveWhere(loan => !GetLive(liveOut, block).Contains(loan.Owner));
        return active;
    }

    /// <summary>
    /// Solves backward liveness of alias/reference holders and records each statement's live-after
    /// set for non-lexical loan expiration.
    /// </summary>
    /// <remarks>
    /// Liveness is over holder symbols, not referents: a named loan may end when its alias is no
    /// longer usable even though the referent's storage remains alive.
    /// </remarks>
    private void ComputeLoanLiveness()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var block in body.Blocks.AsEnumerable().Reverse())
            {
                var output = new HashSet<Symbol>(block.Successors.SelectMany(successor => GetLive(liveIn, successor)));
                var input = TransferLiveness(block, output);
                if (!SetEquals(GetLive(liveOut, block), output))
                {
                    liveOut[block] = output;
                    changed = true;
                }
                if (!SetEquals(GetLive(liveIn, block), input))
                {
                    liveIn[block] = input;
                    changed = true;
                }
            }
        }
        while (changed);

        liveAfter.Clear();
        foreach (var block in body.Blocks)
            TransferLiveness(block, GetLive(liveOut, block), recordAfter: true);
    }

    /// <summary>
    /// Runs the backwards liveness transfer for one block.
    /// </summary>
    /// <param name="recordAfter">
    /// When true, snapshots the set immediately after each statement for the forward loan pass.
    /// </param>
    private HashSet<Symbol> TransferLiveness(OwnershipBlock block, IEnumerable<Symbol> output, bool recordAfter = false)
    {
        var live = new HashSet<Symbol>(output);
        for (int index = block.Statements.Count - 1; index >= 0; index--)
        {
            var statement = block.Statements[index];
            if (recordAfter)
                liveAfter[statement] = [.. live];

            switch (statement.Kind)
            {
                case OwnershipStatementKind.StorageDead:
                case OwnershipStatementKind.Assign:
                    live.Remove(statement.Place.Root);
                    break;
                case OwnershipStatementKind.Borrow:
                case OwnershipStatementKind.Use:
                    live.Add(statement.Place.Root);
                    break;
            }
        }
        return live;
    }

    /// <summary>Removes named loans whose holder is not live after <paramref name="statement"/>.</summary>
    private void EndDeadLoans(HashSet<Loan> active, OwnershipStatement statement)
    {
        if (liveAfter.TryGetValue(statement, out var live))
            active.RemoveWhere(loan => !live.Contains(loan.Owner));
    }

    /// <summary>Emits a two-span diagnostic linking a conflicting operation to the loan origin.</summary>
    private void ReportConflict(OwnershipStatement statement, Loan loan, string operation)
    {
        string name = statement.Place.Root.Name.Text;
        context.Diagnostics.ReportBorrowConflict(statement.Span, operation, name, loan.Origin, statement.Source, loan.Source);
    }

    // Projection-sensitive overlap is deliberately conservative until element-disjointness is
    // proven.  This is sound for fields, indices, dereferences, and dyn fat-reference data.
    /// <summary>
    /// Determines whether two places might alias. Distinct fields of the same struct are disjoint.
    /// </summary>
    private static bool Overlaps(Place left, Place right)
    {
        if (!ReferenceEquals(left.Root, right.Root))
            return false;

        int minLen = Math.Min(left.Projections.Count, right.Projections.Count);
        for (int i = 0; i < minLen; i++)
        {
            var pLeft = left.Projections[i];
            var pRight = right.Projections[i];

            if (pLeft.Kind == PlaceProjectionKind.Field && pRight.Kind == PlaceProjectionKind.Field)
            {
                if (pLeft.Field != pRight.Field)
                    return false; // Distinct fields do not overlap!
            }
            else if (pLeft.Kind != pRight.Kind)
            {
                return true;
            }
        }

        return true;
    }

    private static HashSet<Loan> Get(IReadOnlyDictionary<OwnershipBlock, HashSet<Loan>> map, OwnershipBlock block) =>
        map.TryGetValue(block, out var loans) ? loans : [];

    private static HashSet<Symbol> GetLive(IReadOnlyDictionary<OwnershipBlock, HashSet<Symbol>> map, OwnershipBlock block) =>
        map.TryGetValue(block, out var symbols) ? symbols : [];

    private static bool SetEquals(ISet<Loan> left, ISet<Loan> right) => left.SetEquals(right);

    private static bool SetEquals(HashSet<Symbol> left, ISet<Symbol> right) => left.SetEquals(right);

    private sealed record Loan(Place Referent, BorrowKind Kind, Symbol Owner, TextSpan Origin, SourceText? Source);
}
