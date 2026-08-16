using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class FieldSymbol : Symbol
{
    public SymbolName Name { get; }
    public VariableFlags Flags { get; internal set; }
    public SymbolHandle? Parent { get; }

    public IReadOnlyList<SymbolHandle> TypeParameters { get; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public SymbolHandle Type { get; internal set; }

    public VariableDeclaration? Syntax { get; }

    public FieldSymbol(SymbolID id, Scope enclosingScope, SymbolName name, SymbolHandle? parent, IReadOnlyList<SymbolHandle> typeParameters, VariableDeclaration? syntax)
    : base(id, enclosingScope)
    {
        Kind = SymbolKind.Field;
        Name = name;
        Parent = parent;
        TypeParameters = typeParameters;
        Attributes = [];
        Syntax = syntax;
    }
}

