using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution.Ownership;

/// <summary>
/// Enforces the first escape boundary for safe borrows: a returned safe reference may not refer
/// to automatic local storage.  Parameter-origin and global-origin lifetime relationships are
/// preserved for the signature/lifetime-parameter pass rather than guessed here.
/// </summary>
internal sealed class BorrowEscapeAnalysis
{
    private readonly ResolutionContext context;
    private readonly OwnershipBody body;
    private readonly Dictionary<Symbol, Place> namedLoans = [];
    private readonly Dictionary<Expression, Place> temporaryLoans = [];

    public BorrowEscapeAnalysis(ResolutionContext context, OwnershipBody body)
    {
        this.context = context;
        this.body = body;
    }

    /// <summary>
    /// Rejects a returned safe borrow when its resolved referent is automatic local storage.
    /// </summary>
    /// <remarks>
    /// This is intentionally the first escape boundary, not a full lifetime-constraint solver.
    /// Parameter and global referents are retained for the future signature/interprocedural pass.
    /// </remarks>
    public void Analyze()
    {
        IndexLoans();

        // 1. Safe borrows of temporaries cannot escape statement to local variables
        foreach (var statement in body.Blocks.SelectMany(static block => block.Statements))
        {
            if (statement.Kind != OwnershipStatementKind.Borrow)
                continue;

            if (statement.LoanOwner is LocalVariableSymbol localOwner && statement.Place.Root is TemporaryStorageSymbol)
            {
                context.Diagnostics.ReportBorrowEscapesTemporary(
                    statement.Span,
                    $"local variable '{localOwner.Name.Text}'",
                    statement.Source);
            }
        }

        // 2. Safe borrows cannot escape enclosing function
        if (!ReturnsSafeBorrow())
            return;

        foreach (var block in body.Blocks)
        {
            if (block.Terminator.ReturnExpression is { } returned && TryFindReferent(returned, out var referent))
            {
                if (referent.Root is TemporaryStorageSymbol)
                {
                    context.Diagnostics.ReportBorrowEscapesTemporary(
                        returned.GetSpan() ?? default,
                        "enclosing function",
                        returned.GetSource());
                }
                else if (referent.Root is LocalVariableSymbol local)
                {
                    Report(returned, local);
                }
            }
        }
    }

    private bool ReturnsSafeBorrow() => GetReturnType().SpecialType is ReferenceSemanticType or AliasSemanticType { IsUnsafe: false };

    private TypeRef GetReturnType() => context.GetSymbol(body.ContainingSymbol) switch
    {
        FunctionSymbol function => function.ReturnType,
        MethodSymbol method => method.ReturnType,
        _ => TypeRef.Unresolved
    };

    /// <summary>
    /// Builds reverse maps from named loan holders and address-of syntax to their referent places.
    /// </summary>
    /// <remarks>
    /// The maps preserve lowering provenance so return analysis does not need to reconstruct it
    /// from surface syntax after aliases and reborrows were normalized.
    /// </remarks>
    private void IndexLoans()
    {
        foreach (var statement in body.Blocks.SelectMany(static block => block.Statements))
        {
            if (statement.Kind != OwnershipStatementKind.Borrow)
                continue;
            if (statement.LoanOwner is { } owner)
                namedLoans[owner] = statement.Place;
            if (statement.Origin is Expression expression)
                temporaryLoans[expression] = statement.Place;
        }
    }

    /// <summary>
    /// Resolves a return expression to the place borrowed by either a direct temporary borrow or a
    /// named alias/reference holder.
    /// </summary>
    private bool TryFindReferent(Expression returned, out Place referent)
    {
        if (temporaryLoans.TryGetValue(returned, out referent))
            return true;

        foreach (var statement in body.Blocks.SelectMany(static block => block.Statements))
        {
            if (statement.Kind == OwnershipStatementKind.Use && ReferenceEquals(statement.Origin, returned) && namedLoans.TryGetValue(statement.Place.Root, out referent))
                return true;
        }

        referent = default;
        return false;
    }

    /// <summary>Reports the return site and the local declaration whose storage would escape.</summary>
    private void Report(Expression returned, LocalVariableSymbol local)
    {
        TextSpan localSpan = local.Syntax?.GetSpan() ?? default;
        context.Diagnostics.ReportBorrowEscapesLocal(
            returned.GetSpan() ?? default,
            local.Name.Text,
            localSpan,
            returned.GetSource(),
            local.Syntax?.GetSource());
    }
}
