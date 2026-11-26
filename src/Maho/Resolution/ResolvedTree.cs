using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// Maintains AST syntax-to-symbol resolution references and bindings across compilation passes.
/// </summary>
internal sealed class ResolvedTree
{
    private readonly Dictionary<SyntaxNode, SymbolHandle> references = [];
    private readonly Dictionary<SyntaxNode, SymbolHandle> allocationOperatorReferences = [];
    private readonly Dictionary<SyntaxNode, SymbolHandle> allocDerefReferences = [];
    private readonly Dictionary<SyntaxNode, InterfaceConformance> conformances = [];

    /// <summary> Gets all syntax-to-symbol bindings registered in this tree. </summary>
    public IReadOnlyDictionary<SyntaxNode, SymbolHandle> References => references;

    /// <summary> Gets all allocation operator (<c>New</c>/<c>Put</c>) bindings registered on object creation nodes. </summary>
    public IReadOnlyDictionary<SyntaxNode, SymbolHandle> AllocationOperatorReferences => allocationOperatorReferences;

    /// <summary> Gets all <c>AllocDeref</c> operator bindings registered on object creation nodes. </summary>
    public IReadOnlyDictionary<SyntaxNode, SymbolHandle> AllocDerefReferences => allocDerefReferences;

    /// <summary> Gets all syntax-to-conformance bindings registered in this tree. </summary>
    public IReadOnlyDictionary<SyntaxNode, InterfaceConformance> Conformances => conformances;

    internal System.Action<SyntaxNode, SymbolHandle>? OnReferenceAdded;

    /// <summary> Associates an AST node with its resolved symbol handle. </summary>
    public void AddReference(SyntaxNode syntax, SymbolHandle symbol)
    {
        references[syntax] = symbol;
        OnReferenceAdded?.Invoke(syntax, symbol);
    }

    /// <summary> Associates an object creation node with its resolved <c>New</c> or <c>Put</c> operator symbol. </summary>
    public void AddAllocationOperatorReference(SyntaxNode syntax, SymbolHandle symbol) => allocationOperatorReferences[syntax] = symbol;

    /// <summary> Attempts to retrieve the resolved allocation operator handle for an object creation node. </summary>
    public bool TryGetAllocationOperatorReference(SyntaxNode syntax, out SymbolHandle symbol) => allocationOperatorReferences.TryGetValue(syntax, out symbol);

    /// <summary> Associates an object creation node with the <c>AllocDeref</c> method used to reach its constructed target. </summary>
    public void AddAllocDerefReference(SyntaxNode syntax, SymbolHandle symbol) => allocDerefReferences[syntax] = symbol;

    /// <summary> Attempts to retrieve the resolved <c>AllocDeref</c> method handle for an object creation node. </summary>
    public bool TryGetAllocDerefReference(SyntaxNode syntax, out SymbolHandle symbol) => allocDerefReferences.TryGetValue(syntax, out symbol);

    /// <summary> Attempts to retrieve the resolved symbol handle for a given AST node. </summary>
    public bool TryGetReference(SyntaxNode syntax, out SymbolHandle symbol) => references.TryGetValue(syntax, out symbol);

    /// <summary> Associates an AST node (cast, call, return, or generic argument) with its selected interface conformance. </summary>
    public void AddConformance(SyntaxNode syntax, InterfaceConformance conformance) => conformances[syntax] = conformance;

    /// <summary> Attempts to retrieve the selected interface conformance for a given AST node. </summary>
    public bool TryGetConformance(SyntaxNode syntax, out InterfaceConformance? conformance) => conformances.TryGetValue(syntax, out conformance);

    /// <summary> Removes the symbol reference associated with an AST node. </summary>
    public void RemoveReference(SyntaxNode syntax)
    {
        references.Remove(syntax);
        allocationOperatorReferences.Remove(syntax);
        allocDerefReferences.Remove(syntax);
        conformances.Remove(syntax);
    }

    public sealed record LambdaCaptureInfo(Symbol Symbol, bool IsMutable);
    public sealed record LambdaAnalysisEntry(LambdaExpression Lambda, Scope Scope, SymbolHandle ContainingFunction);

    private readonly Dictionary<LambdaExpression, List<LambdaCaptureInfo>> lambdaCaptures = new();
    private readonly List<LambdaAnalysisEntry> resolvedLambdas = [];
    private readonly HashSet<LambdaExpression> resolvedLambdaSet = new();

    public IReadOnlyList<LambdaAnalysisEntry> ResolvedLambdas => resolvedLambdas;

    public void AddResolvedLambda(LambdaExpression lambda, Scope scope, SymbolHandle containingFunction)
    {
        if (resolvedLambdaSet.Add(lambda))
            resolvedLambdas.Add(new LambdaAnalysisEntry(lambda, scope, containingFunction));
    }

    public void SetLambdaCaptures(LambdaExpression lambda, List<LambdaCaptureInfo> captures)
    {
        lambdaCaptures[lambda] = captures;
    }

    public bool TryGetLambdaCaptures(LambdaExpression lambda, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out List<LambdaCaptureInfo>? captures) =>
        lambdaCaptures.TryGetValue(lambda, out captures);

    /// <summary> Clears all resolved syntax-to-symbol bindings. </summary>
    public void Clear()
    {
        references.Clear();
        allocationOperatorReferences.Clear();
        allocDerefReferences.Clear();
        conformances.Clear();
        lambdaCaptures.Clear();
        resolvedLambdas.Clear();
        resolvedLambdaSet.Clear();
    }
}