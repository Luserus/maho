using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class FieldSymbol : Symbol
{
    public VariableFlags Flags { get; internal set; }
    public SymbolHandle? Parent { get; }

    public IReadOnlyList<SymbolHandle> TypeParameters { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public SymbolHandle Type { get; internal set; }

    public VariableDeclaration? Syntax { get; }

    public FieldSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, SymbolHandle? parent, VariableDeclaration? syntax)
    : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Field;
        Parent = parent;
        TypeParameters = [];
        Attributes = [];
        Syntax = syntax;
    }
}

