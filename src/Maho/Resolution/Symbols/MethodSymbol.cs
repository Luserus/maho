using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal abstract class MethodSymbol : Symbol
{
    public SpecialFunctionKind SpecialKind { get; internal set; }
    public FunctionFlags Flags { get; internal set; }
    public SymbolHandle? Parent { get; }
    public SymbolName? ExplicitInterfaceName { get; internal set; }

    public IReadOnlyList<SymbolHandle> GenericParameters { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public List<SymbolHandle> Parameters { get; internal set; }
    public List<LocalVariableSymbol> LocalVariables { get; internal set; }
    public List<SymbolHandle> LocalFunctions { get; internal set; }
    public List<SymbolHandle> LocalTypes { get; internal set; }
    public TypeRef ReturnType { get; internal set; } = TypeRef.Unresolved;
    public TypeRef Type { get; internal set; } = TypeRef.Unresolved;
    /// <summary>
    /// Gets or sets the intrinsic member signature descriptor specified in the <c>[IntrinsicFunction("...")]</c> attribute,
    /// such as <c>"Int32.Add(Int32, Int32)"</c> or <c>"Int32.&lt;i32&gt;"</c>.
    /// </summary>
    public string? IntrinsicSignature { get; internal set; }

    public FunctionDeclaration? Syntax { get; }

    public LocalVariableSymbol this[int index] => LocalVariables[index];
    public LocalVariableSymbol this[SymbolID id] => LocalVariables[(int)id];
    public LocalVariableSymbol this[SymbolHandle handle] => LocalVariables[(int)handle.ID];

    public bool TryGetLocalVariable(int index, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LocalVariableSymbol? symbol)
    {
        if (index >= 0 && index < LocalVariables.Count)
        {
            symbol = LocalVariables[index];
            return true;
        }

        symbol = null;
        return false;
    }

    public bool TryGetLocalVariable(SymbolID id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LocalVariableSymbol? symbol) =>
        TryGetLocalVariable((int)id, out symbol);

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