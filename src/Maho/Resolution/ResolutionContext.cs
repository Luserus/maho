using System.Collections.Generic;
using System.Runtime.InteropServices;
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

        foreach (var symbol in scope.Symbols.Values)
            parent?.ChildScopes.Add((symbol.Kind, symbol.ID), scope);

        Scopes.Add(scope);
        return scope;
    }

    public TypeSymbol CreateTypeSymbol(Scope enclosingScope, SymbolPart name, TypeKind typeKind, NamespaceTrieNode? containingNamespace, TypeDeclaration? syntax)
    {
        var symbol = new TypeSymbol(typeID++, enclosingScope, name, typeKind, containingNamespace, syntax);
        TypeSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public MemberNestedTypeSymbol CreateMemberNestedTypeSymbol(Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent, TypeDeclaration? syntax)
    {
        var symbol = new MemberNestedTypeSymbol(nestedTypeID++, enclosingScope, name, typeKind, parent, syntax);
        NestedTypeSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalTypeSymbol CreateLocalTypeSymbol(Scope enclosingScope, SymbolPart name, TypeKind typeKind, MethodSymbol? parent, TypeDeclaration? syntax)
    {
        var symbol = new LocalTypeSymbol(nestedTypeID++, enclosingScope, name, typeKind, parent, syntax);
        NestedTypeSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public FunctionSymbol CreateFunctionSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, FunctionDeclaration? syntax)
    {
        var symbol = new FunctionSymbol(functionID++, enclosingScope, name, containingNamespace, syntax);
        FunctionSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public MemberMethodSymbol CreateMemberMethodSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, FunctionDeclaration? syntax)
    {
        var symbol = new MemberMethodSymbol(methodID++, enclosingScope, name, parent, syntax);
        MethodSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalFunctionSymbol CreateLocalFunctionSymbol(Scope enclosingScope, SymbolPart name, MethodSymbol? parent, FunctionDeclaration? syntax)
    {
        var symbol = new LocalFunctionSymbol(methodID++, name, enclosingScope, parent, syntax);
        MethodSymbols.Add(symbol);

        var parameters = new List<TypeSyntax>(syntax?.Signature.Parameters.Count ?? 0);

        if (syntax is not null)
            foreach (var p in syntax.Signature.Parameters)
            {
                var type = p.Declarator.Type;
                parameters.Add(type);
            }

        var functionParams = new Parameters(parameters);

        Register(enclosingScope, symbol, functionParams);
        return symbol;
    }

    public GlobalVariableSymbol CreateGlobalVariableSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, VariableDeclaration? syntax)
    {
        var symbol = new GlobalVariableSymbol(globalVariableID++, enclosingScope, name, containingNamespace, syntax);
        GlobalVariableSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public FieldSymbol CreateFieldSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, VariableDeclaration? syntax)
    {
        var symbol = new FieldSymbol(fieldID++, enclosingScope, name, parent, syntax);
        FieldSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public ParameterSymbol CreateParameterSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? containingFunction)
    {
        var symbol = new ParameterSymbol(parameterID++, enclosingScope, name, containingFunction);
        ParameterSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalVariableSymbol CreateLocalVariableSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, VariableDeclaration? syntax)
    {
        var symbol = new LocalVariableSymbol(localVariableID++, enclosingScope, name, parent, syntax);
        LocalVariableSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public PropertySymbol CreatePropertySymbol(Scope enclosingScope, SymbolPart name, bool hasBacking, MemberPropertyDeclaration? syntax)
    {
        var symbol = new PropertySymbol(propertyID++, enclosingScope, name, hasBacking, syntax);
        PropertySymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public TypeParameterSymbol CreateTypeParameterSymbol(Scope enclosingScope, SymbolPart name, Symbol genericSymbol)
    {
        var symbol = new TypeParameterSymbol(typeParameterID++, enclosingScope, name, genericSymbol);
        TypeParameterSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public LabelSymbol CreateLabelSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? containingFunction, SyntaxNode? syntax)
    {
        var symbol = new LabelSymbol(labelID++, enclosingScope, name, containingFunction, syntax);
        LabelSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public AliasSymbol CreateAliasSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? containingSymbol, SyntaxNode? syntax)
    {
        var symbol = new AliasSymbol(aliasID++, enclosingScope, name, containingSymbol, syntax);
        AliasSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public AliasSymbol CreateAliasSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, SyntaxNode? syntax)
    {
        var symbol = new AliasSymbol(aliasID++, enclosingScope, name, containingNamespace, syntax);
        AliasSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public static SymbolHandle GetHandle(Symbol symbol) => (symbol.Kind, symbol.ID);

    private static void Register(Scope scope, Symbol symbol, Parameters? parameters = null)
    {
        scope.Symbols.Add(GetHandle(symbol), symbol);



        ref var symbols = ref CollectionsMarshal.GetValueRefOrAddDefault(scope.SymbolsByName, symbol.Name, out _);

        symbols ??= [];
        symbols.Add(symbol);
    }

    public static NamespaceTrieNode GetOrDeclareNamespace(NamespaceTrieNode trieNode, SymbolPart ns)
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

    public static SymbolName GetSymbolName(TypeSyntax typeSyntax)
    {
        var listOfParts = new List<SymbolPart>();

        AddTypeNameParts(typeSyntax, listOfParts);
        
        SymbolPart[] parts = [.. listOfParts];

        return new SymbolName(parts);
    }

    public static SymbolName GetSymbolName(NamedSyntax name) => name switch
    {
        SimpleName simpleName => new SymbolName(new SymbolPart(simpleName.Name)),
        GenericName genericName => new SymbolName(new SymbolPart(genericName.Name, genericName.TypeParameters.Count)),
        QualifiedName qualifiedName => GetQualifiedName(qualifiedName),
        _ => throw new System.ArgumentOutOfRangeException(nameof(name))
    };

    private static SymbolPart GetSymbolPart(NamedSyntax name) => name switch
    {
        SimpleName simpleName => new SymbolPart(simpleName.Name),
        GenericName genericName => new SymbolPart(genericName.Name, genericName.TypeParameters.Count),
        _ => throw new System.ArgumentOutOfRangeException(nameof(name))
    };

    private static SymbolName GetQualifiedName(QualifiedName qualifiedName)
    {
        var parts = new SymbolPart[qualifiedName.Parts.Count];

        for (int i = 0; i < parts.Length; i++)
            parts[i] = GetSymbolPart(qualifiedName.Parts[i]);

        return new SymbolName(parts);
    }

    private static void AddTypeNameParts(TypeSyntax type, List<SymbolPart> parts)
    {
        switch (type)
        {
            case SimpleType simple:
                parts.Add(new SymbolPart(simple.Name));
                break;

            case GenericType generic:
                parts.Add(new SymbolPart(generic.Name, generic.TypeArguments.Count));
                break;

            case QualifiedType qualified:
                AddTypeNameParts(qualified.Left, parts);
                AddTypeNameParts(qualified.Right, parts);
                break;

            default:
                throw new System.ArgumentOutOfRangeException(nameof(type));
        }
    }
}
