using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal abstract class MethodSymbol : Symbol
{
    public FunctionFlags Flags { get; internal set; }

    public IReadOnlyList<SymbolHandle> GenericParameters { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public List<SymbolHandle> LocalVariables { get; internal set; }
    public List<SymbolHandle> LocalFunctions { get; internal set; }
    public List<SymbolHandle> LocalTypes { get; internal set; }
    public SymbolHandle? ReturnType { get; internal set; }

    public FunctionDeclaration? Syntax { get; }

    protected MethodSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, FunctionDeclaration? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Method;
        GenericParameters = [];
        Attributes = [];
        LocalVariables = [];
        LocalFunctions = [];
        LocalTypes = [];
        Syntax = syntax;
    }
}
