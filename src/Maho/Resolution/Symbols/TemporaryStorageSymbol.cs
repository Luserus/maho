namespace Maho.Resolution;

/// <summary>
/// A synthetic storage root for a materialized statement-scoped temporary value.
/// </summary>
internal sealed class TemporaryStorageSymbol : Symbol
{
    public TypeRef Type { get; }

    public TemporaryStorageSymbol(SymbolID id, Scope enclosingScope, TypeRef type)
        : base(id, new SymbolPart("$temp"), enclosingScope)
    {
        Kind = SymbolKind.Variable;
        Type = type;
    }
}

