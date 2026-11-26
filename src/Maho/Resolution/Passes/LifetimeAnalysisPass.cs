using Maho.Resolution.Ownership;
using Maho.Syntax;

namespace Maho.Resolution.Passes;

/// <summary>
/// Builds the ownership-specific CFG for every executable body and runs its first data-flow
/// analyses: implicit copy/move state, non-lexical safe loans, direct local-borrow escape
/// checking, and cleanup elaboration.  Later language work extends the same bodies with
/// declared lifetime constraints and projection-aware partial-move state.
/// </summary>
internal sealed class LifetimeAnalysisPass : ResolutionPass
{
    /// <summary>
    /// Runs ownership analysis after expression resolution has supplied types, symbols, call
    /// targets, receiver modes, and the ordinary control-flow diagnostics.
    /// </summary>
    /// <remarks>
    /// Only callables with executable bodies are eligible. Intrinsics have no source storage model;
    /// property accessors await real synthetic receiver/value symbols.
    /// </remarks>
    public override void Resolve(ResolutionContext context)
    {
        foreach (var function in context.FunctionSymbols)
        {
            if (function.Flags.HasFlag(FunctionFlags.Intrinsic) || function.Syntax?.Body is FunctionEmptyBody)
                continue;
            if (function.Syntax?.Body is { } body)
                AnalyzeBody(context, body, context.GetSyntaxScope(body, function.EnclosingScope), ResolutionContext.GetHandle(function));
        }

        foreach (var method in context.MethodSymbols)
        {
            if (method.Flags.HasFlag(FunctionFlags.Intrinsic) || method.Syntax?.Body is FunctionEmptyBody)
                continue;
            if (method.Syntax?.Body is { } body)
                AnalyzeBody(context, body, context.GetSyntaxScope(body, method.EnclosingScope), ResolutionContext.GetHandle(method));
        }

        for (int i = 0; i < context.ResolvedTree.ResolvedLambdas.Count; i++)
        {
            var entry = context.ResolvedTree.ResolvedLambdas[i];
            AnalyzeLambdaBody(context, entry.Lambda, entry.Scope, entry.ContainingFunction);
        }

        // Property accessors need synthetic receiver/value storage roots.  Those are not yet
        // symbols in the resolved model, so they intentionally wait for the receiver lowering
        // slice rather than being analysed with guessed identities.
    }

    /// <summary>
    /// Executes the required ownership-analysis order for a single callable and retains the move
    /// facts/body for later lowering consumers.
    /// </summary>
    /// <remarks>
    /// Drop elaboration deliberately runs last because it depends on stable move facts and mutates
    /// the ownership body. Borrow diagnostics run before mutation so their statement provenance
    /// remains exactly the source-lowered order.
    /// </remarks>
    private static void AnalyzeBody(ResolutionContext context, FunctionBody bodySyntax, Scope scope, SymbolHandle containingSymbol)
    {
        var body = new OwnershipLowerer(context).Lower(bodySyntax, scope, containingSymbol);
        AnalyzeOwnershipBody(context, body, containingSymbol);
    }

    private static void AnalyzeLambdaBody(ResolutionContext context, LambdaExpression lambda, Scope scope, SymbolHandle containingFunction)
    {
        var body = new OwnershipLowerer(context).LowerLambda(lambda, scope, containingFunction);
        AnalyzeOwnershipBody(context, body, storeFactsFor: null);
    }

    private static void AnalyzeOwnershipBody(ResolutionContext context, Ownership.OwnershipBody body, SymbolHandle? storeFactsFor)
    {
        var facts = new MoveAnalysis(context, body).Analyze();
        new BorrowAnalysis(context, body).Analyze();
        new BorrowEscapeAnalysis(context, body).Analyze();
        new DropElaborator(context, facts).Elaborate();
        if (storeFactsFor is { } symHandle)
            context.SetOwnershipFacts(symHandle, facts);
    }
}
