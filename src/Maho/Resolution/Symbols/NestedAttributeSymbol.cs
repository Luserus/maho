using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal abstract class NestedAttributeSymbol : Symbol
{
    public AttributeFlags Flags { get; internal set; }
    public SymbolHandle? Parent { get; }

    public List<SymbolHandle> Attributes { get; internal set; }

    public List<SymbolHandle> Parameters { get; internal set; }

    public AttributeSignature? Syntax { get; }

    protected NestedAttributeSymbol(SymbolID id, SymbolPart name, Scope enclosingScope, SymbolHandle? parent, AttributeSignature? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.NestedAttribute;
        Parent = parent;
        Attributes = [];
        Parameters = [];
        Syntax = syntax;
    }
}