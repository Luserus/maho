namespace Maho.Symbols;

/// <summary> This class represents all the symbols in the language. </summary>
internal abstract class Symbol
{
    public SymbolKind Kind { get; }
    public SymbolName Name { get; }
    public Symbol? ParentSymbol { get; }
    public virtual string MetadataName => Name.ToString();
    public string QualifiedMetadataName => GetQualifiedMetadataName();

    protected Symbol(SymbolKind kind, SymbolName name, Symbol? parentSymbol)
    {
        Kind = kind;
        Name = name;
        ParentSymbol = parentSymbol;
    }

    private string GetQualifiedMetadataName()
    {
        if (ParentSymbol is null)
            return MetadataName;

        string parentQualifiedName = ParentSymbol.QualifiedMetadataName;

        if (string.IsNullOrEmpty(parentQualifiedName))
            return MetadataName;

        if (string.IsNullOrEmpty(MetadataName))
            return parentQualifiedName;

        return $"{parentQualifiedName}.{MetadataName}";
    }
}
