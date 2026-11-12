using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class EnumVariantSymbol : Symbol
{
    public EnumVariantKind VariantKind { get; }
    public SymbolHandle ContainingType { get; }
    public EnumVariant? Syntax { get; }
    public List<SymbolHandle> Parameters { get; internal set; }
    public List<SymbolHandle> Fields { get; internal set; }
    public Expression? ExplicitValue { get; }
    public int DiscriminantValue { get; internal set; }

    public EnumVariantSymbol(
        SymbolID id,
        Scope enclosingScope,
        SymbolPart name,
        EnumVariantKind variantKind,
        SymbolHandle containingType,
        EnumVariant? syntax,
        Expression? explicitValue = null)
        : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.EnumVariant;
        VariantKind = variantKind;
        ContainingType = containingType;
        Syntax = syntax;
        ExplicitValue = explicitValue;
        Parameters = [];
        Fields = [];
    }
}

