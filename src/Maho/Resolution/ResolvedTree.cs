using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// Maintains AST syntax-to-symbol resolution references and bindings across compilation passes.
/// </summary>
internal sealed class ResolvedTree
{
    private readonly Dictionary<SyntaxNode, SymbolHandle> references = [];
    private readonly Dictionary<SyntaxNode, InterfaceConformance> conformances = [];

    /// <summary> Gets all syntax-to-symbol bindings registered in this tree. </summary>
    public IReadOnlyDictionary<SyntaxNode, SymbolHandle> References => references;

    /// <summary> Gets all syntax-to-conformance bindings registered in this tree. </summary>
    public IReadOnlyDictionary<SyntaxNode, InterfaceConformance> Conformances => conformances;

    internal System.Action<SyntaxNode, SymbolHandle>? OnReferenceAdded;

    /// <summary> Associates an AST node with its resolved symbol handle. </summary>
    public void AddReference(SyntaxNode syntax, SymbolHandle symbol)
    {
        references[syntax] = symbol;
        OnReferenceAdded?.Invoke(syntax, symbol);
    }

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
        conformances.Remove(syntax);
    }

    /// <summary> Clears all resolved syntax-to-symbol bindings. </summary>
    public void Clear()
    {
        references.Clear();
        conformances.Clear();
    }
}