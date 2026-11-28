using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class PropertySymbol : Symbol
{
    public bool HasBacking { get; internal set; }
    public SymbolHandle? Parent { get; }
    public SymbolName? ExplicitInterfaceName { get; internal set; }
    public FunctionFlags Flags { get; internal set; }
    public SymbolHandle? BackingField { get; internal set; }
    public string? IntrinsicSignature { get; internal set; }

    public FunctionFlags GetterFlags { get; internal set; }
    public FunctionFlags SetterFlags { get; internal set; }

    public IReadOnlyList<SymbolHandle> GenericParameters { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }
    public List<SymbolHandle> GetterAttributes { get; internal set; }
    public List<SymbolHandle> SetterAttributes { get; internal set; }

    public List<SymbolHandle> GetterLocalVariables { get; internal set; }
    public List<SymbolHandle> GetterLocalFunctions { get; internal set; }
    public List<SymbolHandle> GetterLocalTypes { get; internal set; }

    public List<SymbolHandle> SetterLocalVariables { get; internal set; }
    public List<SymbolHandle> SetterLocalFunctions { get; internal set; }
    public List<SymbolHandle> SetterLocalTypes { get; internal set; }
    public TypeRef Type { get; internal set; } = TypeRef.Unresolved;

    public MemberPropertyDeclaration? Syntax { get; }

    public PropertySymbol(SymbolID id, Scope enclosingScope, SymbolPart name, SymbolHandle? parent, bool hasBacking,
                        MemberPropertyDeclaration? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Property;
        Parent = parent;
        HasBacking = hasBacking;
        GenericParameters = [];
        Attributes = [];
        GetterAttributes = [];
        SetterAttributes = [];
        GetterLocalVariables = [];
        GetterLocalFunctions = [];
        GetterLocalTypes = [];
        SetterLocalVariables = [];
        SetterLocalFunctions = [];
        SetterLocalTypes = [];
        Syntax = syntax;
    }
}