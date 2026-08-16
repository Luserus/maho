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
    public Scope GlobalScope => Scopes[0];

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
        var scope = new Scope(parent);
        parent?.ChildScopes.Add(scope);
        Scopes.Add(scope);
        return scope;
    }

    public TypeSymbol CreateTypeSymbol(Scope enclosingScope, SymbolName name, TypeKind typeKind, NamespaceTrieNode? containingNamespace,
                                       IReadOnlyList<SymbolHandle> typeParameters, TypeDeclaration? syntax)
    {
        var symbol = new TypeSymbol(typeID++, enclosingScope, name, typeKind, containingNamespace, typeParameters, syntax);
        TypeSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public MemberNestedTypeSymbol CreateMemberNestedTypeSymbol(Scope enclosingScope, SymbolName name, TypeKind typeKind, SymbolHandle? parent,
                                                                IReadOnlyList<SymbolHandle> typeParameters, TypeDeclaration? syntax)
    {
        var symbol = new MemberNestedTypeSymbol(nestedTypeID++, enclosingScope, name, typeKind, parent, typeParameters, syntax);
        NestedTypeSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalTypeSymbol CreateLocalTypeSymbol(Scope enclosingScope, SymbolName name, TypeKind typeKind, MethodSymbol? parent,
                                                  IReadOnlyList<SymbolHandle> typeParameters, TypeDeclaration? syntax)
    {
        var symbol = new LocalTypeSymbol(nestedTypeID++, enclosingScope, name, typeKind, parent, typeParameters, syntax);
        NestedTypeSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public FunctionSymbol CreateFunctionSymbol(Scope enclosingScope, SymbolName name, NamespaceTrieNode? containingNamespace,
                                               IReadOnlyList<SymbolHandle> typeParameters, FunctionDeclaration? syntax)
    {
        var symbol = new FunctionSymbol(functionID++, enclosingScope, name, containingNamespace, typeParameters, syntax);
        FunctionSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public MemberMethodSymbol CreateMemberMethodSymbol(Scope enclosingScope, SymbolHandle? parent,
                                                        IReadOnlyList<SymbolHandle> typeParameters, FunctionDeclaration? syntax)
    {
        var symbol = new MemberMethodSymbol(methodID++, enclosingScope, parent, typeParameters, syntax);
        MethodSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalFunctionSymbol CreateLocalFunctionSymbol(Scope enclosingScope, MethodSymbol? parent,
                                                          IReadOnlyList<SymbolHandle> typeParameters, FunctionDeclaration? syntax)
    {
        var symbol = new LocalFunctionSymbol(methodID++, enclosingScope, parent, typeParameters, syntax);
        MethodSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public GlobalVariableSymbol CreateGlobalVariableSymbol(Scope enclosingScope, SymbolName name, NamespaceTrieNode? containingNamespace,
                                                            IReadOnlyList<SymbolHandle> typeParameters, VariableDeclaration? syntax)
    {
        var symbol = new GlobalVariableSymbol(globalVariableID++, enclosingScope, name, containingNamespace, typeParameters, syntax);
        GlobalVariableSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public FieldSymbol CreateFieldSymbol(Scope enclosingScope, SymbolName name, SymbolHandle? parent,
                                         IReadOnlyList<SymbolHandle> typeParameters, VariableDeclaration? syntax)
    {
        var symbol = new FieldSymbol(fieldID++, enclosingScope, name, parent, typeParameters, syntax);
        FieldSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public ParameterSymbol CreateParameterSymbol(Scope enclosingScope, SymbolHandle? containingFunction)
    {
        var symbol = new ParameterSymbol(parameterID++, enclosingScope, containingFunction);
        ParameterSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalVariableSymbol CreateLocalVariableSymbol(Scope enclosingScope, SymbolName name, SymbolHandle? parent,
                                                          IReadOnlyList<SymbolHandle> typeParameters, VariableDeclaration? syntax)
    {
        var symbol = new LocalVariableSymbol(localVariableID++, enclosingScope, name, parent, typeParameters, syntax);
        LocalVariableSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public PropertySymbol CreatePropertySymbol(Scope enclosingScope, SymbolName name, bool hasBacking,
                                                IReadOnlyList<SymbolHandle> typeParameters, MemberPropertyDeclaration? syntax)
    {
        var symbol = new PropertySymbol(propertyID++, enclosingScope, name, hasBacking, typeParameters, syntax);
        PropertySymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public TypeParameterSymbol CreateTypeParameterSymbol(Scope enclosingScope, SymbolName name, Symbol genericSymbol)
    {
        var symbol = new TypeParameterSymbol(typeParameterID++, enclosingScope, name, genericSymbol);
        TypeParameterSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public LabelSymbol CreateLabelSymbol(Scope enclosingScope, SymbolName name, SymbolHandle? containingFunction, SyntaxNode? syntax)
    {
        var symbol = new LabelSymbol(labelID++, enclosingScope, name, containingFunction, syntax);
        LabelSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public AliasSymbol CreateAliasSymbol(Scope enclosingScope, SymbolName name, SymbolHandle? containingSymbol, SyntaxNode? syntax)
    {
        var symbol = new AliasSymbol(aliasID++, enclosingScope, name, containingSymbol, syntax);
        AliasSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public AliasSymbol CreateAliasSymbol(Scope enclosingScope, SymbolName name, NamespaceTrieNode? containingNamespace, SyntaxNode? syntax)
    {
        var symbol = new AliasSymbol(aliasID++, enclosingScope, name, containingNamespace, syntax);
        AliasSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public static SymbolHandle GetHandle(Symbol symbol) => (symbol.Kind, symbol.ID);

    private static void Register(Scope scope, Symbol symbol) => scope.Symbols.Add(GetHandle(symbol), symbol);

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
