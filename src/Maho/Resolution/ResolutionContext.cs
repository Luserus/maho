using System.Collections.Generic;
using System.Linq;
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
    public IEnumerable<LocalVariableSymbol> LocalVariableSymbols =>
        FunctionSymbols.SelectMany(f => f.LocalVariables).Concat(MethodSymbols.SelectMany(m => m.LocalVariables));
    public List<PropertySymbol> PropertySymbols { get; }
    public List<GenericParameterSymbol> GenericParameterSymbols { get; }
    public List<LabelSymbol> LabelSymbols { get; }
    public List<AliasSymbol> AliasSymbols { get; }
    public List<MacroSymbol> MacroSymbols { get; }

    public SymbolStore SinkSymbols { get; } = SymbolStore.CreateEmpty();
    private readonly List<LocalVariableSymbol> sinkLocalVariables = [];
    public IReadOnlyList<LocalVariableSymbol> SinkLocalVariables => sinkLocalVariables;
    public IEnumerable<LocalVariableSymbol> SinkLocalVariableSymbols =>
        SinkSymbols.FunctionSymbols.SelectMany(f => f.LocalVariables)
            .Concat(SinkSymbols.MethodSymbols.SelectMany(m => m.LocalVariables))
            .Concat(sinkLocalVariables);
    private readonly Dictionary<SymbolHandle, Symbol> sinkSymbolsByHandle = [];
    private int sinkID = -1;

    public List<Scope> Scopes { get; }
    public Scope GlobalScope => Scopes[0];
    private Dictionary<SyntaxNode, Scope> SyntaxScopes { get; } = [];
    private readonly Dictionary<SyntaxNode, HygieneId> nodeHygiene = [];

    public void RegisterHygiene(SyntaxNode node, HygieneId hygiene) => nodeHygiene[node] = hygiene;

    public HygieneId GetHygiene(SyntaxNode? node)
    {
        if (node is null)
            return HygieneId.Root;

        if (nodeHygiene.TryGetValue(node, out var hygiene))
            return hygiene;

        if (node is NamedExpression named && nodeHygiene.TryGetValue(named.Identifier, out var idHygiene))
            return idHygiene;

        if (node is SimpleName simple && nodeHygiene.TryGetValue(simple.Name, out var nameHygiene))
            return nameHygiene;

        return HygieneId.Root;
    }

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
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : typeID++;
        TypeSymbol symbol;

        if (typeKind is TypeKind.Struct or TypeKind.Class or TypeKind.Delegate or TypeKind.Interface)
        {
            symbol = new ProductTypeSymbol(id, enclosingScope, name, typeKind, containingNamespace, syntax);
        }
        else
            symbol = new SumTypeSymbol(id, enclosingScope, name, typeKind, containingNamespace, syntax);

        if (isSink)
        {
            SinkSymbols.TypeSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            TypeSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public MemberTypeSymbol CreateMemberTypeSymbol(Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent, TypeDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : nestedTypeID++;
        MemberTypeSymbol symbol;

        if (typeKind is TypeKind.Struct or TypeKind.Class or TypeKind.Delegate or TypeKind.Interface)
        {
            symbol = new MemberProductTypeSymbol(id, enclosingScope, name, typeKind, parent, syntax);
        }
        else
            symbol = new MemberSumTypeSymbol(id, enclosingScope, name, typeKind, parent, syntax);

        if (isSink)
        {
            SinkSymbols.NestedTypeSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            NestedTypeSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalTypeSymbol CreateLocalTypeSymbol(Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent, TypeDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : nestedTypeID++;
        LocalTypeSymbol symbol;

        if (typeKind is TypeKind.Struct or TypeKind.Class or TypeKind.Delegate or TypeKind.Interface)
        {
            symbol = new LocalProductTypeSymbol(id, enclosingScope, name, typeKind, parent, syntax);
        }
        else
            symbol = new LocalSumTypeSymbol(id, enclosingScope, name, typeKind, parent, syntax);

        if (isSink)
        {
            SinkSymbols.NestedTypeSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            NestedTypeSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol);
        return symbol;
    }

    public AttributeSymbol CreateAttributeSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, AttributeSignature? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : attributeID++;
        var symbol = new AttributeSymbol(id, name, enclosingScope, containingNamespace, syntax);

        if (isSink)
        {
            SinkSymbols.AttributeSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            AttributeSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public MemberAttributeSymbol CreateMemberAttributeSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, AttributeSignature? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : nestedAttributeID++;
        var symbol = new MemberAttributeSymbol(id, name, enclosingScope, parent, syntax);

        if (isSink)
        {
            SinkSymbols.NestedAttributeSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            NestedAttributeSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalAttributeSymbol CreateLocalAttributeSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, AttributeSignature? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : nestedAttributeID++;
        var symbol = new LocalAttributeSymbol(id, name, enclosingScope, parent, syntax);

        if (isSink)
        {
            SinkSymbols.NestedAttributeSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            NestedAttributeSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol);
        return symbol;
    }

    public FunctionSymbol CreateFunctionSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, FunctionDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : functionID++;
        var symbol = new FunctionSymbol(id, enclosingScope, name, containingNamespace, syntax);

        if (isSink)
        {
            SinkSymbols.FunctionSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            FunctionSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public MemberMethodSymbol CreateMemberMethodSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, FunctionDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : methodID++;
        var symbol = new MemberMethodSymbol(id, enclosingScope, name, parent, syntax);

        if (isSink)
        {
            SinkSymbols.MethodSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            MethodSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalFunctionSymbol CreateLocalFunctionSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, FunctionDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : methodID++;
        var symbol = new LocalFunctionSymbol(id, name, enclosingScope, parent, syntax);

        if (isSink)
        {
            SinkSymbols.MethodSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            MethodSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol);
        return symbol;
    }

    public GlobalVariableSymbol CreateGlobalVariableSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, VariableDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : globalVariableID++;
        var symbol = new GlobalVariableSymbol(id, enclosingScope, name, containingNamespace, syntax);

        if (isSink)
        {
            SinkSymbols.GlobalVariableSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            GlobalVariableSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public FieldSymbol CreateFieldSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, VariableDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : fieldID++;
        var symbol = new FieldSymbol(id, enclosingScope, name, parent, syntax);

        if (isSink)
        {
            SinkSymbols.FieldSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            FieldSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol);
        return symbol;
    }

    public ParameterSymbol CreateParameterSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? containingSymbol, Parameter? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : parameterID++;
        var symbol = new ParameterSymbol(id, enclosingScope, name, containingSymbol, syntax);

        if (isSink)
        {
            SinkSymbols.ParameterSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            ParameterSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalVariableSymbol CreateLocalVariableSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, VariableDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        if (isSink)
        {
            var symbol = new LocalVariableSymbol(new SymbolID(sinkID--), enclosingScope, name, parent, syntax);
            sinkLocalVariables.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;

            if (name.Text != "_")
            {
                if (parent is { Kind: SymbolKind.Function })
                {
                    var func = GetFunctionSymbol(parent.Value);
                    func?.LocalVariables.Add(symbol);
                }
                else if (parent is { Kind: SymbolKind.Method })
                {
                    var method = GetMethodSymbol(parent.Value);
                    method?.LocalVariables.Add(symbol);
                }
            }

            Register(enclosingScope, symbol);
            return symbol;
        }

        SymbolID id = 0;
        if (parent is { Kind: SymbolKind.Function, ID: var funcId } && (int)funcId >= 0 && (int)funcId < FunctionSymbols.Count)
        {
            var func = FunctionSymbols[funcId];
            id = func.LocalVariables.Count;
            var symbol = new LocalVariableSymbol(id, enclosingScope, name, parent, syntax);
            func.LocalVariables.Add(symbol);
            Register(enclosingScope, symbol);
            return symbol;
        }
        else if (parent is { Kind: SymbolKind.Method, ID: var methodId } && (int)methodId >= 0 && (int)methodId < MethodSymbols.Count)
        {
            var method = MethodSymbols[methodId];
            id = method.LocalVariables.Count;
            var symbol = new LocalVariableSymbol(id, enclosingScope, name, parent, syntax);
            method.LocalVariables.Add(symbol);
            Register(enclosingScope, symbol);
            return symbol;
        }
        else
        {
            var symbol = new LocalVariableSymbol(0, enclosingScope, name, parent, syntax);
            Register(enclosingScope, symbol);
            return symbol;
        }
    }

    public PropertySymbol CreatePropertySymbol(Scope enclosingScope, SymbolPart name, bool hasBacking, MemberPropertyDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : propertyID++;
        var symbol = new PropertySymbol(id, enclosingScope, name, hasBacking, syntax);

        if (isSink)
        {
            SinkSymbols.PropertySymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            PropertySymbols.Add(symbol);
        }

        Register(enclosingScope, symbol);
        return symbol;
    }

    public GenericParameterSymbol CreateGenericParameterSymbol(Scope enclosingScope, SymbolPart name, Symbol genericSymbol, GenericParameterKind parameterKind, bool isVariadic)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : genericParameterID++;
        var symbol = new GenericParameterSymbol(id, enclosingScope, name, genericSymbol, parameterKind, isVariadic);

        if (isSink)
        {
            SinkSymbols.GenericParameterSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            GenericParameterSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol);
        return symbol;
    }

    public LabelSymbol CreateLabelSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? containingFunction, SyntaxNode? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : labelID++;
        var symbol = new LabelSymbol(id, enclosingScope, name, containingFunction, syntax);

        if (isSink)
        {
            SinkSymbols.LabelSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            LabelSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol);
        return symbol;
    }

    public AliasSymbol CreateAliasSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? containingSymbol, AliasDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : aliasID++;
        var symbol = new AliasSymbol(id, enclosingScope, name, containingSymbol, syntax);

        if (isSink)
        {
            SinkSymbols.AliasSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            AliasSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol);
        return symbol;
    }

    public AliasSymbol CreateAliasSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, AliasDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : aliasID++;
        var symbol = new AliasSymbol(id, enclosingScope, name, containingNamespace, syntax);

        if (isSink)
        {
            SinkSymbols.AliasSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            AliasSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public MacroSymbol CreateMacroSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? containingSymbol, MacroDeclaration syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : macroID++;
        var symbol = new MacroSymbol(id, enclosingScope, name, containingSymbol, syntax);

        if (isSink)
        {
            SinkSymbols.MacroSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            MacroSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol);
        return symbol;
    }

    public MacroSymbol CreateMacroSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, MacroDeclaration syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : macroID++;
        var symbol = new MacroSymbol(id, enclosingScope, name, containingNamespace, syntax);

        if (isSink)
        {
            SinkSymbols.MacroSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
        {
            MacroSymbols.Add(symbol);
        }

        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public FunctionSymbol? GetFunctionSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Function)
            return null;
        if (handle.ID.Value >= 0 && handle.ID.Value < FunctionSymbols.Count)
            return FunctionSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is FunctionSymbol func)
            return func;

        return null;
    }

    public MethodSymbol? GetMethodSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Method)
            return null;
        if (handle.ID.Value >= 0 && handle.ID.Value < MethodSymbols.Count)
            return MethodSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is MethodSymbol method)
            return method;

        return null;
    }

    public TypeSymbol? GetTypeSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Type)
            return null;
        if (handle.ID.Value >= 0 && handle.ID.Value < TypeSymbols.Count)
            return TypeSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is TypeSymbol t)
            return t;

        return null;
    }

    public NestedTypeSymbol? GetNestedTypeSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.NestedType)
            return null;
        if (handle.ID.Value >= 0 && handle.ID.Value < NestedTypeSymbols.Count)
            return NestedTypeSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is NestedTypeSymbol t)
            return t;

        return null;
    }

    public AliasSymbol? GetAliasSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Alias)
            return null;
        if (handle.ID.Value >= 0 && handle.ID.Value < AliasSymbols.Count)
            return AliasSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is AliasSymbol a)
            return a;

        return null;
    }

    public GenericParameterSymbol? GetGenericParameterSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.GenericParameter)
            return null;
        if (handle.ID.Value >= 0 && handle.ID.Value < GenericParameterSymbols.Count)
            return GenericParameterSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is GenericParameterSymbol gp)
            return gp;

        return null;
    }

    public ParameterSymbol? GetParameterSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Parameter)
            return null;
        if (handle.ID.Value >= 0 && handle.ID.Value < ParameterSymbols.Count)
            return ParameterSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is ParameterSymbol p)
            return p;

        return null;
    }

    public AttributeSymbol? GetAttributeSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Attribute)
            return null;
        if (handle.ID.Value >= 0 && handle.ID.Value < AttributeSymbols.Count)
            return AttributeSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is AttributeSymbol a)
            return a;

        return null;
    }

    public NestedAttributeSymbol? GetNestedAttributeSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.NestedAttribute)
            return null;
        if (handle.ID.Value >= 0 && handle.ID.Value < NestedAttributeSymbols.Count)
            return NestedAttributeSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is NestedAttributeSymbol a)
            return a;

        return null;
    }

    public Symbol? GetSymbol(SymbolHandle handle) => handle.Kind switch
    {
        SymbolKind.Type => GetTypeSymbol(handle),
        SymbolKind.NestedType => GetNestedTypeSymbol(handle),
        SymbolKind.Function => GetFunctionSymbol(handle),
        SymbolKind.Method => GetMethodSymbol(handle),
        SymbolKind.Alias => GetAliasSymbol(handle),
        SymbolKind.GenericParameter => GetGenericParameterSymbol(handle),
        SymbolKind.Parameter => GetParameterSymbol(handle),
        SymbolKind.Attribute => GetAttributeSymbol(handle),
        SymbolKind.NestedAttribute => GetNestedAttributeSymbol(handle),
        _ => sinkSymbolsByHandle.TryGetValue(handle, out var s) ? s : null
    };

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

                case SymbolKind.Type when current.ID.Value < 0 && sinkSymbolsByHandle.ContainsKey(current):
                case SymbolKind.NestedType when current.ID.Value < 0 && sinkSymbolsByHandle.ContainsKey(current):
                    return current;

                case SymbolKind.Alias:
                    var alias = GetAliasSymbol(current);
                    if (alias is null || !alias.Target.IsResolved || alias.Target.Handle is not { } target)
                        return null;

                    current = target;
                    break;

                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Unwraps any aliases transitively, returning the underlying <see cref="TypeRef"/>.
    /// </summary>
    public TypeRef UnwrapAlias(TypeRef typeRef)
    {
        var current = typeRef;
        var visited = new HashSet<SymbolHandle>();

        while (current.Kind == TypeRefKind.Resolved && current.Handle is { Kind: SymbolKind.Alias } aliasHandle && visited.Add(aliasHandle))
        {
            var alias = GetAliasSymbol(aliasHandle);
            if (alias is null || !alias.Target.IsResolved)
                break;
            current = alias.Target;
        }

        return current;
    }

    private void Register(Scope scope, Symbol symbol, NamespaceTrieNode? containingNamespace = null)
    {
        if (symbol.Name.Text == "_")
            return;

        scope.Symbols[GetHandle(symbol)] = symbol;

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

    public SymbolName GetScopedSymbolName(NamedSyntax name) =>
        GetSymbolName(name, GetHygiene(name));

    public static SymbolName GetSymbolName(NamedSyntax name, HygieneId hygiene = default) => name switch
    {
        SimpleName simpleName => new SymbolName(new SymbolPart(simpleName.Name, 0, hygiene)),
        GenericName genericName => new SymbolName(new SymbolPart(genericName.Name, genericName.GenericParameters.Count, hygiene)),
        QualifiedName qualifiedName => GetQualifiedName(qualifiedName, hygiene),
        TupleName => new SymbolName(new SymbolPart(string.Empty, 0, hygiene)),
        _ => throw new System.ArgumentOutOfRangeException(nameof(name))
    };

    private static SymbolPart GetSymbolPart(NamedSyntax name, HygieneId hygiene = default) => name switch
    {
        SimpleName simpleName => new SymbolPart(simpleName.Name, 0, hygiene),
        GenericName genericName => new SymbolPart(genericName.Name, genericName.GenericParameters.Count, hygiene),
        TupleName => new SymbolPart(string.Empty, 0, hygiene),
        _ => throw new System.ArgumentOutOfRangeException(nameof(name))
    };

    private static SymbolName GetQualifiedName(QualifiedName qualifiedName, HygieneId hygiene = default)
    {
        var parts = new SymbolPart[qualifiedName.Parts.Count];

        for (int i = 0; i < parts.Length; i++)
            parts[i] = GetSymbolPart(qualifiedName.Parts[i], hygiene);

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

            case TupleType:
            case UniformTupleType:
                parts.Add(new SymbolPart(string.Empty));
                break;

            default:
                throw new System.ArgumentOutOfRangeException(nameof(type));
        }
    }
}