using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal abstract class MethodSymbol : Symbol
{
    public FunctionFlags Flags { get; internal set; }
    public SymbolHandle? Parent { get; }

    public IReadOnlyList<SymbolHandle> GenericParameters { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public List<SymbolHandle> Parameters { get; internal set; }
    public List<SymbolHandle> LocalVariables { get; internal set; }
    public List<SymbolHandle> LocalFunctions { get; internal set; }
    public List<SymbolHandle> LocalTypes { get; internal set; }
    public TypeRef ReturnType { get; internal set; } = TypeRef.Unresolved;

    public FunctionDeclaration? Syntax { get; }

    protected MethodSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, SymbolHandle? parent, FunctionDeclaration? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Method;
        Parent = parent;
        GenericParameters = [];
        Attributes = [];
        Parameters = [];
        LocalVariables = [];
        LocalFunctions = [];
        LocalTypes = [];
        Syntax = syntax;
    }
}