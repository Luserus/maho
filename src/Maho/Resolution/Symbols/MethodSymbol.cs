using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal abstract class MethodSymbol : Symbol
{
    public SymbolName Name { get; }
    public FunctionFlags Flags { get; internal set; }

    public IReadOnlyList<SymbolHandle> TypeParameters { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public List<SymbolHandle> LocalVariables { get; internal set; }
    public List<SymbolHandle> LocalFunctions { get; internal set; }
    public List<SymbolHandle> LocalTypes { get; internal set; }

    public FunctionDeclaration? Syntax { get; }

    protected MethodSymbol(SymbolID id, Scope enclosingScope, FunctionDeclaration? syntax) : base(id, enclosingScope)
    {
        Kind = SymbolKind.Method;
        TypeParameters = [];
        Attributes = [];
        LocalVariables = [];
        LocalFunctions = [];
        LocalTypes = [];
        Syntax = syntax;
    }
}

