using System.Collections.Generic;
using System.Runtime.InteropServices;
using Maho.Diagnostics;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class ResolutionContext
{
    public SyntaxTree SyntaxTree { get; }
    public ResolvedTree ResolvedTree { get; }
    public DiagnosticsManager Diagnostics { get; }

    public NamespaceTrieNode GlobalNamespace { get; }

    public List<AttributeSymbol> AttributeSymbols { get; }
    public List<NestedAttributeSymbol> NestedAttributeSymbols { get; }
    public List<TypeSymbol> TypeSymbols { get; }
    public List<NestedTypeSymbol> NestedTypeSymbols { get; }
    public List<FunctionSymbol> FunctionSymbols { get; }
    public List<MethodSymbol> MethodSymbols { get; }
    public List<GlobalVariableSymbol> GlobalVariableSymbols { get; }
    public List<FieldSymbol> FieldSymbols { get; }
    public List<ParameterSymbol> ParameterSymbols { get; }
    public List<LocalVariableSymbol> LocalVariableSymbols { get; }
    public List<PropertySymbol> PropertySymbols { get; }
    public List<GenericParameterSymbol> GenericParameterSymbols { get; }
    public List<LabelSymbol> LabelSymbols { get; }
    public List<AliasSymbol> AliasSymbols { get; }
    public List<MacroSymbol> MacroSymbols { get; }

    public List<Scope> Scopes { get; }
    public Scope GlobalScope => Scopes[0];
    private Dictionary<SyntaxNode, Scope> SyntaxScopes { get; } = [];

    /// <summary> Other projects or modules referenced by this compilation unit. </summary>
    public IReadOnlyList<ResolutionContext> ReferencedProjects { get; }

    /// <summary> Symbol stores imported from external projects or prior compilation phases. </summary>
    public IReadOnlyList<SymbolStore> ImportedProjectSymbols { get; }

    /// <summary> Compilation options governing recursion limits and diagnostics. </summary>
    public CompilationOptions Options { get; }

    private int attributeID;
    private int nestedAttributeID;
    private int typeID;
    private int nestedTypeID;
    private int functionID;
    private int methodID;
    private int globalVariableID;
    private int fieldID;
    private int parameterID;
    private int localVariableID;
    private int propertyID;
    private int genericParameterID;
    private int labelID;
    private int aliasID;
    private int macroID;

    public ResolutionContext(
        SyntaxTree syntaxTree,
        ResolvedTree resolvedTree,
        NamespaceTrieNode globalNamespace,
        SymbolStore symbols,
        List<Scope> scopes,
        IReadOnlyList<ResolutionContext>? referencedProjects = null,
        IReadOnlyList<SymbolStore>? importedSymbols = null,
        DiagnosticsManager? diagnostics = null,
        CompilationOptions? options = null)
    {
        SyntaxTree = syntaxTree;
        ResolvedTree = resolvedTree;
        Diagnostics = diagnostics ?? new DiagnosticsManager();
        Options = options ?? new CompilationOptions();

        GlobalNamespace = globalNamespace;
        Scopes = scopes;
        GlobalScope.GlobalNamespace = globalNamespace;

        AttributeSymbols = symbols.AttributeSymbols;
        NestedAttributeSymbols = symbols.NestedAttributeSymbols;
        TypeSymbols = symbols.TypeSymbols;
        NestedTypeSymbols = symbols.NestedTypeSymbols;
        FunctionSymbols = symbols.FunctionSymbols;
        MethodSymbols = symbols.MethodSymbols;
        GlobalVariableSymbols = symbols.GlobalVariableSymbols;
        FieldSymbols = symbols.FieldSymbols;
        ParameterSymbols = symbols.ParameterSymbols;
        LocalVariableSymbols = symbols.LocalVariableSymbols;
        PropertySymbols = symbols.PropertySymbols;
        GenericParameterSymbols = symbols.GenericParameterSymbols;
        LabelSymbols = symbols.LabelSymbols;
        AliasSymbols = symbols.AliasSymbols;
        MacroSymbols = symbols.MacroSymbols;

        ReferencedProjects = referencedProjects ?? [];
        ImportedProjectSymbols = importedSymbols ?? [];

        attributeID = AttributeSymbols.Count;
        nestedAttributeID = NestedAttributeSymbols.Count;
        typeID = TypeSymbols.Count;
        nestedTypeID = NestedTypeSymbols.Count;
        functionID = FunctionSymbols.Count;
        methodID = MethodSymbols.Count;
        globalVariableID = GlobalVariableSymbols.Count;
        fieldID = FieldSymbols.Count;
        parameterID = ParameterSymbols.Count;
        localVariableID = LocalVariableSymbols.Count;
        propertyID = PropertySymbols.Count;
        genericParameterID = GenericParameterSymbols.Count;
        labelID = LabelSymbols.Count;
        aliasID = AliasSymbols.Count;
        macroID = MacroSymbols.Count;

        if (ReferencedProjects.Count > 0 || ImportedProjectSymbols.Count > 0)
            InitializeProjectReferences();
    }

    private void InitializeProjectReferences()
    {
        foreach (var project in ReferencedProjects)
        {
            MergeNamespaceTrie(GlobalNamespace, project.GlobalNamespace);

            if (!GlobalScope.ImportedScopes.Contains(project.GlobalScope))
                GlobalScope.ImportedScopes.Add(project.GlobalScope);
        }

        if (ImportedProjectSymbols.Count > 0)
        {
            foreach (var store in ImportedProjectSymbols)
            {
                var importedScope = CreateScopeFromSymbolStore(store);
                if (!GlobalScope.ImportedScopes.Contains(importedScope))
                    GlobalScope.ImportedScopes.Add(importedScope);
            }
        }
    }

    private void CreateScopeFromSymbolStoreAndRegister(SymbolStore store, Scope scope)
    {
        foreach (var type in store.TypeSymbols)
            Register(scope, type, type.ContainingNamespace);

        foreach (var function in store.FunctionSymbols)
            Register(scope, function, function.ContainingNamespace);

        foreach (var global in store.GlobalVariableSymbols)
            Register(scope, global, global.ContainingNamespace);

        foreach (var attribute in store.AttributeSymbols)
            Register(scope, attribute, attribute.ContainingNamespace);

        foreach (var alias in store.AliasSymbols)
            Register(scope, alias, alias.ContainingNamespace);
    }

    private Scope CreateScopeFromSymbolStore(SymbolStore store)
    {
        var scope = new Scope(null);
        CreateScopeFromSymbolStoreAndRegister(store, scope);
        return scope;
    }

    private static void MergeNamespaceTrie(NamespaceTrieNode target, NamespaceTrieNode source)
    {
        foreach (var symbol in source.Symbols)
        {
            target.RegisterSymbol(symbol);
        }

        foreach (var (key, value) in source.Next)
        {
            if (!target.Next.TryGetValue(key, out var existing))
            {
                existing = new NamespaceTrieNode { Name = key, Parent = target };
                target.Next[key] = existing;
            }

            MergeNamespaceTrie(existing, value);
        }
    }

    public Scope CreateScope(Scope? parent)
    {
        var scope = new Scope(parent);
        Scopes.Add(scope);
        return scope;
    }

    public TypeSymbol CreateTypeSymbol(Scope enclosingScope, SymbolPart name, TypeKind typeKind, NamespaceTrieNode? containingNamespace, TypeDeclaration? syntax)
    {
        TypeSymbol symbol;

        if (typeKind is TypeKind.Struct or TypeKind.Class or TypeKind.Delegate or TypeKind.Interface)
        {
            symbol = new ProductTypeSymbol(typeID++, enclosingScope, name, typeKind, containingNamespace, syntax);
        }
        else
            symbol = new SumTypeSymbol(typeID++, enclosingScope, name, typeKind, containingNamespace, syntax);

        TypeSymbols.Add(symbol);
        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public MemberTypeSymbol CreateMemberTypeSymbol(Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent, TypeDeclaration? syntax)
    {
        MemberTypeSymbol symbol;

        if (typeKind is TypeKind.Struct or TypeKind.Class or TypeKind.Delegate or TypeKind.Interface)
        {
            symbol = new MemberProductTypeSymbol(nestedTypeID++, enclosingScope, name, typeKind, parent, syntax);
        }
        else
            symbol = new MemberSumTypeSymbol(nestedTypeID++, enclosingScope, name, typeKind, parent, syntax);

        NestedTypeSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalTypeSymbol CreateLocalTypeSymbol(Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent, TypeDeclaration? syntax)
    {
        LocalTypeSymbol symbol;

        if (typeKind is TypeKind.Struct or TypeKind.Class or TypeKind.Delegate or TypeKind.Interface)
        {
            symbol = new LocalProductTypeSymbol(nestedTypeID++, enclosingScope, name, typeKind, parent, syntax);
        }
        else
            symbol = new LocalSumTypeSymbol(nestedTypeID++, enclosingScope, name, typeKind, parent, syntax);

        NestedTypeSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public AttributeSymbol CreateAttributeSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, AttributeSignature? syntax)
    {
        var symbol = new AttributeSymbol(attributeID++, name, enclosingScope, containingNamespace, syntax);

        AttributeSymbols.Add(symbol);
        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public MemberAttributeSymbol CreateMemberAttributeSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, AttributeSignature? syntax)
    {
        var symbol = new MemberAttributeSymbol(nestedAttributeID++, name, enclosingScope, parent, syntax);

        NestedAttributeSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalAttributeSymbol CreateLocalAttributeSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, AttributeSignature? syntax)
    {
        var symbol = new LocalAttributeSymbol(nestedAttributeID++, name, enclosingScope, parent, syntax);

        NestedAttributeSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public FunctionSymbol CreateFunctionSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, FunctionDeclaration? syntax)
    {
        var symbol = new FunctionSymbol(functionID++, enclosingScope, name, containingNamespace, syntax);
        FunctionSymbols.Add(symbol);
        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public MemberMethodSymbol CreateMemberMethodSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, FunctionDeclaration? syntax)
    {
        var symbol = new MemberMethodSymbol(methodID++, enclosingScope, name, parent, syntax);
        MethodSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalFunctionSymbol CreateLocalFunctionSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, FunctionDeclaration? syntax)
    {
        var symbol = new LocalFunctionSymbol(methodID++, name, enclosingScope, parent, syntax);
        MethodSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public GlobalVariableSymbol CreateGlobalVariableSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, VariableDeclaration? syntax)
    {
        var symbol = new GlobalVariableSymbol(globalVariableID++, enclosingScope, name, containingNamespace, syntax);
        GlobalVariableSymbols.Add(symbol);
        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public FieldSymbol CreateFieldSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, VariableDeclaration? syntax)
    {
        var symbol = new FieldSymbol(fieldID++, enclosingScope, name, parent, syntax);
        FieldSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public ParameterSymbol CreateParameterSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? containingSymbol, Parameter? syntax)
    {
        var symbol = new ParameterSymbol(parameterID++, enclosingScope, name, containingSymbol, syntax);
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

    public GenericParameterSymbol CreateGenericParameterSymbol(Scope enclosingScope, SymbolPart name, Symbol genericSymbol, GenericParameterKind parameterKind, bool isVariadic)
    {
        var symbol = new GenericParameterSymbol(genericParameterID++, enclosingScope, name, genericSymbol, parameterKind, isVariadic);
        GenericParameterSymbols.Add(symbol);
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

    public AliasSymbol CreateAliasSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? containingSymbol, AliasDeclaration? syntax)
    {
        var symbol = new AliasSymbol(aliasID++, enclosingScope, name, containingSymbol, syntax);
        AliasSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public AliasSymbol CreateAliasSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, AliasDeclaration? syntax)
    {
        var symbol = new AliasSymbol(aliasID++, enclosingScope, name, containingNamespace, syntax);
        AliasSymbols.Add(symbol);
        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public MacroSymbol CreateMacroSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? containingSymbol, MacroDeclaration syntax)
    {
        var symbol = new MacroSymbol(macroID++, enclosingScope, name, containingSymbol, syntax);
        MacroSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public MacroSymbol CreateMacroSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, MacroDeclaration syntax)
    {
        var symbol = new MacroSymbol(macroID++, enclosingScope, name, containingNamespace, syntax);
        MacroSymbols.Add(symbol);
        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public static SymbolHandle GetHandle(Symbol symbol) => (symbol.Kind, symbol.ID);

    public static void BindChildScope(Scope parent, Symbol owner, Scope child) => parent.ChildScopes[GetHandle(owner)] = child;

    public void RegisterSyntaxScope(SyntaxNode syntax, Scope scope) => SyntaxScopes[syntax] = scope;

    public Scope GetSyntaxScope(SyntaxNode syntax, Scope fallback) => SyntaxScopes.TryGetValue(syntax, out var scope) ? scope : fallback;

    /// <summary>
    /// Gets the underlying type <see cref="SymbolHandle"/> (global or nested) referenced by a <see cref="TypeRef"/>,
    /// unwrapping any aliases transitively. Returns <c>null</c> if unresolved or not a type.
    /// </summary>
    public SymbolHandle? GetType(TypeRef typeRef)
    {
        if (!typeRef.IsResolved || typeRef.Handle is not { } handle)
            return null;

        var current = handle;
        var visited = new HashSet<SymbolHandle>();

        while (visited.Add(current))
        {
            switch (current.Kind)
            {
                case SymbolKind.Type when current.ID.Value >= 0 && current.ID.Value < TypeSymbols.Count:
                case SymbolKind.NestedType when current.ID.Value >= 0 && current.ID.Value < NestedTypeSymbols.Count:
                    return current;

                case SymbolKind.Alias when current.ID.Value >= 0 && current.ID.Value < AliasSymbols.Count:
                    var alias = AliasSymbols[current.ID];

                    if (!alias.Target.IsResolved || alias.Target.Handle is not { } target)
                        return null;

                    current = target;
                    break;

                default:
                    return null;
            }
        }

        return null;
    }

    private void Register(Scope scope, Symbol symbol, NamespaceTrieNode? containingNamespace = null)
    {
        scope.Symbols.Add(GetHandle(symbol), symbol);

        if (containingNamespace != null && containingNamespace != GlobalNamespace)
        {
            containingNamespace.RegisterSymbol(symbol);
            if (symbol is AliasSymbol)
            {
                ref var symbols = ref CollectionsMarshal.GetValueRefOrAddDefault(scope.SymbolsByName, symbol.Name, out _);
                symbols ??= [];
                symbols.Add(symbol);
            }
        }
        else
        {
            ref var symbols = ref CollectionsMarshal.GetValueRefOrAddDefault(scope.SymbolsByName, symbol.Name, out _);
            symbols ??= [];
            symbols.Add(symbol);
        }
    }

    public static NamespaceTrieNode GetOrDeclareNamespace(NamespaceTrieNode trieNode, SymbolPart ns)
    {
        var node = trieNode.Next.GetValueOrDefault(ns);

        if (node is null)
        {
            var newNode = new NamespaceTrieNode { Name = ns, Parent = trieNode };
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
        GenericName genericName => new SymbolName(new SymbolPart(genericName.Name, genericName.GenericParameters.Count)),
        QualifiedName qualifiedName => GetQualifiedName(qualifiedName),
        _ => throw new System.ArgumentOutOfRangeException(nameof(name))
    };

    private static SymbolPart GetSymbolPart(NamedSyntax name) => name switch
    {
        SimpleName simpleName => new SymbolPart(simpleName.Name),
        GenericName genericName => new SymbolPart(genericName.Name, genericName.GenericParameters.Count),
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
                parts.Add(new SymbolPart(generic.Name, generic.GenericArguments.Count));
                break;

            case QualifiedType qualified:
                AddTypeNameParts(qualified.Left, parts);
                AddTypeNameParts(qualified.Right, parts);
                break;

            case ModifiedType modified:
                AddTypeNameParts(modified.Type, parts);
                break;

            default:
                throw new System.ArgumentOutOfRangeException(nameof(type));
        }
    }
}