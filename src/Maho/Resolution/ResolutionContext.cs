using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class ResolutionContext
{
    public SyntaxTree SyntaxTree { get; }
    public ResolvedTree ResolvedTree { get; }

    public NamespaceTrieNode GlobalNamespace { get; }

    public List<TypeSymbol> TypeSymbols { get; }
    public List<NestedTypeSymbol> NestedTypeSymbols { get; }
    public List<FunctionSymbol> FunctionSymbols { get; }
    public List<MethodSymbol> MethodSymbols { get; }
    public List<GlobalVariableSymbol> GlobalVariableSymbols { get; }
    public List<FieldSymbol> FieldSymbols { get; }
    public List<ParameterSymbol> ParameterSymbols { get; }
    public List<LocalVariableSymbol> LocalVariableSymbols { get; }
    public List<PropertySymbol> PropertySymbols { get; }
    public List<TypeParameterSymbol> TypeParameterSymbols {get; }
    public List<LabelSymbol> LabelSymbols { get; }
    public List<AliasSymbol> AliasSymbols { get; }

    public List<Scope> Scopes { get; }

    private int typeID;
    private int nestedTypeID;
    private int functionID;
    private int methodID;
    private int globalVariableID;
    private int fieldID;
    private int parameterID;
    private int localVariableID;
    private int propertyID;
    private int typeParameterID;
    private int labelID;
    private int aliasID;

    public ResolutionContext(SyntaxTree syntaxTree, ResolvedTree resolvedTree, NamespaceTrieNode globalNamespace, SymbolStore symbols, List<Scope> scopes)
    {
        SyntaxTree = syntaxTree;
        ResolvedTree = resolvedTree;

        GlobalNamespace = globalNamespace;

        TypeSymbols = symbols.TypeSymbols;
        NestedTypeSymbols = symbols.NestedTypeSymbols;
        FunctionSymbols = symbols.FunctionSymbols;
        MethodSymbols = symbols.MethodSymbols;
        GlobalVariableSymbols = symbols.GlobalVariableSymbols;
        FieldSymbols = symbols.FieldSymbols;
        ParameterSymbols = symbols.ParameterSymbols;
        LocalVariableSymbols = symbols.LocalVariableSymbols;
        PropertySymbols = symbols.PropertySymbols;
        TypeParameterSymbols = symbols.TypeParameterSymbols;
        LabelSymbols = symbols.LabelSymbols;
        AliasSymbols = symbols.AliasSymbols;

        Scopes = scopes;

        typeID = TypeSymbols.Count;
        nestedTypeID = NestedTypeSymbols.Count;
        functionID = FunctionSymbols.Count;
        methodID = MethodSymbols.Count;
        globalVariableID = GlobalVariableSymbols.Count;
        fieldID = FieldSymbols.Count;
        parameterID = ParameterSymbols.Count;
        localVariableID = LocalVariableSymbols.Count;
        propertyID = PropertySymbols.Count;
        typeParameterID = TypeParameterSymbols.Count;
        labelID = LabelSymbols.Count;
        aliasID = AliasSymbols.Count;
    }

    public Scope CreateScope(Scope? parent)
    {
        return new Scope(parent);
    }

    public static NamespaceTrieNode GetOrDeclareNamespace(NamespaceTrieNode trieNode, SymbolName ns)
    {
        var node = trieNode.Next.GetValueOrDefault(ns);

        if (node is null)
        {
            var newNode = new NamespaceTrieNode();
            trieNode.Next[ns] = newNode;
            return newNode;
        }

        return node;
    }
}
