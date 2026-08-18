using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class LocalVariableSymbol : Symbol
{
    public SymbolName Name { get; }
    public VariableFlags Flags { get; internal set; }
    public SymbolHandle? Parent { get; }

    public IReadOnlyList<SymbolHandle> TypeParameters { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public SymbolHandle Type { get; internal set; }

    public VariableDeclaration? Syntax { get; }

    public LocalVariableSymbol(SymbolID id, Scope enclosingScope, SymbolName name, SymbolHandle? parent,
                                VariableDeclaration? syntax) : base(id, enclosingScope)
    {
        Kind = SymbolKind.Variable;
        Name = name;
        Parent = parent;
        TypeParameters = [];
        Attributes = [];
        Syntax = syntax;
    }
}

