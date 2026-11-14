using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// Maintains AST syntax-to-symbol resolution references and bindings across compilation passes.
/// </summary>
internal sealed class ResolvedTree
{
    private readonly Dictionary<SyntaxNode, SymbolHandle> references = [];

    /// <summary> Gets all syntax-to-symbol bindings registered in this tree. </summary>
    public IReadOnlyDictionary<SyntaxNode, SymbolHandle> References => references;

    internal System.Action<SyntaxNode, SymbolHandle>? OnReferenceAdded;

    /// <summary> Associates an AST node with its resolved symbol handle. </summary>
    public void AddReference(SyntaxNode syntax, SymbolHandle symbol)
    {
        references[syntax] = symbol;
        OnReferenceAdded?.Invoke(syntax, symbol);
    }

    /// <summary> Attempts to retrieve the resolved symbol handle for a given AST node. </summary>
    public bool TryGetReference(SyntaxNode syntax, out SymbolHandle symbol) => references.TryGetValue(syntax, out symbol);

    /// <summary> Removes the symbol reference associated with an AST node. </summary>
    public void RemoveReference(SyntaxNode syntax) => references.Remove(syntax);

    /// <summary> Clears all resolved syntax-to-symbol bindings. </summary>
    public void Clear() => references.Clear();
}