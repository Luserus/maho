namespace Maho.Symbols;

/// <summary> Base semantic model for every symbol the compiler can discover. </summary>
internal abstract class Symbol
{
    /// <summary>
    /// Cached qualified metadata name so repeated semantic lookups do not rebuild the same parent
    /// chain over and over.
    /// </summary>
    private string? qualifiedMetadataName;

    /// <summary> Broad semantic category of this symbol. </summary>
    public SymbolKind Kind { get; }
    /// <summary> Source-backed simple name for the declaration represented by this symbol. </summary>
    public SymbolName Name { get; }
    /// <summary> Semantic container that lexically encloses this symbol, when one exists. </summary>
    public Symbol? ParentSymbol { get; private set; }
    /// <summary>
    /// Metadata-oriented name materialized on demand. Most of resolution stays on
    /// <see cref="SymbolName"/> to avoid eager string allocation.
    /// </summary>
    public virtual string MetadataName => Name.ToString();
    /// <summary> Fully qualified metadata-style name built from the parent symbol chain. </summary>
    public string QualifiedMetadataName => qualifiedMetadataName ??= CreateQualifiedMetadataName();

    /// <summary> Creates one semantic symbol with its simple name and parent container. </summary>
    protected Symbol(SymbolKind kind, SymbolName name, Symbol? parentSymbol)
    {
        Kind = kind;
        Name = name;
        ParentSymbol = parentSymbol;
    }

    /// <summary>
    /// Reattaches this symbol under a different container while clearing any parent-derived caches.
    /// Symbol discovery uses this once when unit-local declaration graphs are attached to the
    /// canonical project graph.
    /// </summary>
    internal void Reparent(Symbol? parentSymbol)
    {
        if (ReferenceEquals(ParentSymbol, parentSymbol))
            return;

        ParentSymbol = parentSymbol;
        qualifiedMetadataName = null;
        OnParentChanged();
    }

    /// <summary> Allows derived symbols to invalidate caches that depend on the parent chain. </summary>
    protected virtual void OnParentChanged() { }

    /// <summary>
    /// Computes the fully qualified metadata name lazily from the parent symbol chain. This is kept
    /// as a helper rather than eager state so the semantic core does not pay allocation cost unless
    /// a later consumer actually needs a string form.
    /// </summary>
    private string CreateQualifiedMetadataName()
    {
        string metadataName = MetadataName;

        if (ParentSymbol is null)
            return metadataName;

        string parentQualifiedName = ParentSymbol.QualifiedMetadataName;

        if (string.IsNullOrEmpty(parentQualifiedName))
            return metadataName;

        if (string.IsNullOrEmpty(metadataName))
            return parentQualifiedName;

        return $"{parentQualifiedName}.{metadataName}";
    }
}
