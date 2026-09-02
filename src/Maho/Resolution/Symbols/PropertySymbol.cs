using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class PropertySymbol : Symbol
{
    public bool HasBacking { get; }
    public FunctionFlags GetterFlags { get; internal set; }
    public FunctionFlags SetterFlags { get; internal set; }

    public IReadOnlyList<SymbolHandle> TypeParameters { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }
    public List<SymbolHandle> GetterAttributes { get; internal set; }
    public List<SymbolHandle> SetterAttributes { get; internal set; }

    public List<SymbolHandle> GetterLocalVariables { get; internal set; }
    public List<SymbolHandle> GetterLocalFunctions { get; internal set; }
    public List<SymbolHandle> GetterLocalTypes { get; internal set; }

    public List<SymbolHandle> SetterLocalVariables { get; internal set; }
    public List<SymbolHandle> SetterLocalFunctions { get; internal set; }
    public List<SymbolHandle> SetterLocalTypes { get; internal set; }

    public MemberPropertyDeclaration? Syntax { get; }

    public PropertySymbol(SymbolID id, Scope enclosingScope, SymbolPart name, bool hasBacking,
                        MemberPropertyDeclaration? syntax) : base(id, name, enclosingScope)
    {
        HasBacking = hasBacking;
        TypeParameters = [];
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