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
    private readonly Dictionary<Expression, List<Place>> temporaryLoans = [];

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
        if (ReturnsSafeBorrow())
        {
            foreach (var block in body.Blocks)
            {
                if (block.Terminator.ReturnExpression is { } returned && TryFindReferents(returned, out var referents))
                    ReportEscapingReferents(returned, referents, "enclosing function");
            }
        }

        // 3. Safe borrows cannot escape through throw statements
        foreach (var block in body.Blocks)
        {
            if (block.Terminator.ThrowExpression is { } thrown && TryFindReferents(thrown, out var referents))
                ReportEscapingReferents(thrown, referents, "enclosing function (via throw)");
        }
    }

    private void ReportEscapingReferents(Expression expr, List<Place> referents, string temporaryScopeLabel)
    {
        foreach (var referent in referents)
        {
            if (referent.Root is TemporaryStorageSymbol)
            {
                context.Diagnostics.ReportBorrowEscapesTemporary(
                    expr.GetSpan() ?? default,
                    temporaryScopeLabel,
                    expr.GetSource());
            }
            else if (referent.Root is LocalVariableSymbol local)
            {
                Report(expr, local);
            }
        }
    }

    private bool ReturnsSafeBorrow() => ContainsSafeBorrowType(GetReturnType());

    private bool ContainsSafeBorrowType(TypeRef type, HashSet<SymbolHandle>? visited = null)
    {
        var unwrapped = TypeRef.UnwrapDyn(type);
        if (unwrapped.SpecialType is ReferenceSemanticType or AliasSemanticType { IsUnsafe: false } or DelegateSemanticType)
            return true;

        if (unwrapped.SpecialType is OptionalSemanticType opt)
            return ContainsSafeBorrowType(opt.ElementType, visited);

        if (unwrapped.SpecialType is TupleSemanticType tuple)
            return tuple.Elements.Any(e => ContainsSafeBorrowType(e.Type, visited));

        if (unwrapped.Handle is { } handle)
        {
            var classifier = new OwnershipTypeClassifier(context);
            if (!classifier.RequiresDrop(unwrapped))
            {
                visited ??= [];
                var uH = context.UnwrapAlias(handle);
                if (!visited.Add(uH))
                    return false;
                IEnumerable<SymbolHandle> fields = context.GetSymbol(uH) switch
                {
                    ProductTypeSymbol p => p.Fields,
                    MemberProductTypeSymbol mp => mp.Fields,
                    _ => []
                };
                foreach (var fh in fields)
                {
                    if (context.GetFieldSymbol(fh) is { } fs && ContainsSafeBorrowType(fs.Type, visited))
                        return true;
                }
            }
        }

        return false;
    }

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
            {
                if (!temporaryLoans.TryGetValue(expression, out var list))
                {
                    list = [];
                    temporaryLoans[expression] = list;
                }
                list.Add(statement.Place);
            }
        }
    }

    /// <summary>
    /// Resolves a returned or thrown expression to all places borrowed by a direct temporary
    /// borrow, a named alias/reference holder, or a delegate/composite capture map.
    /// </summary>
    private bool TryFindReferents(Expression expr, out List<Place> referents)
    {
        if (temporaryLoans.TryGetValue(expr, out var directLoans) && directLoans.Count > 0)
        {
            referents = directLoans;
            return true;
        }

        foreach (var statement in body.Blocks.SelectMany(static block => block.Statements))
        {
            if (statement.Kind != OwnershipStatementKind.Use || !ReferenceEquals(statement.Origin, expr))
                continue;

            if (body.ReferentMap.TryGetValue(statement.Place.Root, out var mapEntries) && mapEntries.Count > 0)
            {
                referents = mapEntries.Select(static entry => entry.Referent).ToList();
                return true;
            }

            if (namedLoans.TryGetValue(statement.Place.Root, out var singleReferent))
            {
                referents = [singleReferent];
                return true;
            }
        }

        referents = [];
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
