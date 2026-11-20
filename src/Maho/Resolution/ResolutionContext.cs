using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Maho.Diagnostics;
using Maho.Resolution.Ownership;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class ResolutionContext
{
    public IReadOnlyList<SyntaxTree> SyntaxTrees { get; }
    public ResolvedTree ResolvedTree { get; }
    public DiagnosticsManager Diagnostics { get; }

    public NamespaceTrieNode GlobalNamespace { get; }

    public DiscoveredIntrinsics DiscoveredIntrinsics { get; }
    public DiscoveredOperators DiscoveredOperators { get; }

    public List<AttributeSymbol> AttributeSymbols { get; } = [];
    public List<NestedAttributeSymbol> NestedAttributeSymbols { get; } = [];
    public List<TypeSymbol> TypeSymbols { get; } = [];
    public List<NestedTypeSymbol> NestedTypeSymbols { get; } = [];
    public List<FunctionSymbol> FunctionSymbols { get; } = [];
    public List<MethodSymbol> MethodSymbols { get; } = [];
    public List<GlobalVariableSymbol> GlobalVariableSymbols { get; } = [];
    public List<FieldSymbol> FieldSymbols { get; } = [];
    public List<ParameterSymbol> ParameterSymbols { get; } = [];
    public IEnumerable<LocalVariableSymbol> LocalVariableSymbols =>
        FunctionSymbols.SelectMany(f => f.LocalVariables).Concat(MethodSymbols.SelectMany(m => m.LocalVariables));
    public List<PropertySymbol> PropertySymbols { get; } = [];
    public List<GenericParameterSymbol> GenericParameterSymbols { get; } = [];
    public List<LabelSymbol> LabelSymbols { get; } = [];
    public List<AliasSymbol> AliasSymbols { get; } = [];
    public List<MacroSymbol> MacroSymbols { get; } = [];
    public List<EnumVariantSymbol> EnumVariantSymbols { get; } = [];

    public SymbolSink SinkSymbols { get; } = new();
    private readonly List<LocalVariableSymbol> sinkLocalVariables = [];
    public IReadOnlyList<LocalVariableSymbol> SinkLocalVariables => sinkLocalVariables;
    public IEnumerable<LocalVariableSymbol> SinkLocalVariableSymbols =>
        SinkSymbols.FunctionSymbols.SelectMany(f => f.LocalVariables)
            .Concat(SinkSymbols.MethodSymbols.SelectMany(m => m.LocalVariables))
            .Concat(sinkLocalVariables);
    private readonly Dictionary<SymbolHandle, Symbol> sinkSymbolsByHandle = [];
    private int sinkID = -1;
    private int enumVariantID;

    /// <summary>
    /// Gets or sets the file path of the syntax tree currently being processed by the resolver.
    /// </summary>
    public string? CurrentTreeFilePath { get; set; }
    private readonly Dictionary<string, List<SymbolHandle>> treeSymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SymbolHandle, string> symbolTree = [];

    /// <summary>
    /// Records a declared symbol handle against its declaring source file path for incremental invalidation.
    /// </summary>
    /// <param name="filePath">The declaring source file path.</param>
    /// <param name="handle">The resolved symbol handle.</param>
    public void TrackTreeSymbol(string filePath, SymbolHandle handle)
    {
        if (!treeSymbols.TryGetValue(filePath, out var list))
        {
            list = [];
            treeSymbols[filePath] = list;
        }
        list.Add(handle);
        symbolTree[handle] = filePath;
    }

    /// <summary>
    /// Retrieves the source file path where a symbol handle was declared.
    /// </summary>
    /// <param name="handle">The symbol handle.</param>
    /// <returns>The declaring file path, or <see langword="null"/> if not tracked.</returns>
    public string? GetSymbolTreeFilePath(SymbolHandle handle) =>
        symbolTree.TryGetValue(handle, out var path) ? path : (GetSymbol(handle) is { } s ? GetSymbolSourceFilePath(s) : null);

    /// <summary>
    /// Retrieves all symbol handles declared within a specific source file path.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <returns>A read-only list of declared symbol handles.</returns>
    public IReadOnlyList<SymbolHandle> GetTreeSymbols(string filePath) =>
        treeSymbols.TryGetValue(filePath, out var list) ? list : Array.Empty<SymbolHandle>();

    /// <summary>
    /// Clears and unregisters all symbols declared within the specified source file from their containing namespaces and symbol tables.
    /// </summary>
    /// <param name="filePath">The source file path whose symbols should be cleared.</param>
    public void ClearTreeSymbols(string filePath)
    {
        if (treeSymbols.TryGetValue(filePath, out var handles))
        {
            foreach (var h in handles)
            {
                symbolTree.Remove(h);
                var sym = GetSymbol(h);
                var ns = sym switch
                {
                    TypeSymbol t => t.ContainingNamespace,
                    FunctionSymbol f => f.ContainingNamespace,
                    GlobalVariableSymbol g => g.ContainingNamespace,
                    AttributeSymbol a => a.ContainingNamespace,
                    AliasSymbol al => al.ContainingNamespace,
                    _ => null
                };
                if (ns != null && sym != null)
                {
                    ns.Symbols.Remove(sym);
                }
            }
            treeSymbols.Remove(filePath);
        }
    }

    public List<Scope> Scopes { get; }
    public Scope GlobalScope => Scopes[0];
    private Dictionary<SyntaxNode, Scope> SyntaxScopes { get; } = [];
    private readonly Dictionary<SyntaxNode, HygieneId> nodeHygiene = [];
    private readonly Dictionary<Expression, TypeRef> expressionTypes = [];
    private readonly Dictionary<SymbolHandle, ControlFlow.ControlFlowGraph> controlFlowGraphs = [];
    private readonly Dictionary<SymbolHandle, OwnershipFacts> ownershipFacts = [];
    private readonly Dictionary<string, Dictionary<SyntaxNode, SymbolHandle>> treeNodeSymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<Expression, TypeRef>> treeExpressionTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Expression, ConstantValue> constantValues = [];

    public void SetConstantValue(Expression expr, ConstantValue value) => constantValues[expr] = value;
    public bool TryGetConstantValue(Expression expr, out ConstantValue value) => constantValues.TryGetValue(expr, out value);
    public IReadOnlyDictionary<Expression, ConstantValue> ConstantValues => constantValues;

    public void SetExpressionType(Expression expr, TypeRef type)
    {
        expressionTypes[expr] = type;
        if (CurrentTreeFilePath != null)
        {
            if (!treeExpressionTypes.TryGetValue(CurrentTreeFilePath, out var dict))
            {
                dict = [];
                treeExpressionTypes[CurrentTreeFilePath] = dict;
            }
            dict[expr] = type;
        }
    }

    public TypeRef? GetExpressionType(Expression expr) =>
        expressionTypes.TryGetValue(expr, out var type) ? type : null;

    public TypeRef GetRequiredExpressionType(Expression expr) =>
        expressionTypes.TryGetValue(expr, out var type) ? type : TypeRef.Error;

    public void SetControlFlowGraph(SymbolHandle handle, ControlFlow.ControlFlowGraph cfg) =>
        controlFlowGraphs[handle] = cfg;

    public bool TryGetControlFlowGraph(SymbolHandle handle, out ControlFlow.ControlFlowGraph? cfg) =>
        controlFlowGraphs.TryGetValue(handle, out cfg);

    public IReadOnlyDictionary<SymbolHandle, ControlFlow.ControlFlowGraph> ControlFlowGraphs => controlFlowGraphs;

    /// <summary> Gets lowered ownership bodies and their fixed-point initialization facts. </summary>
    public IReadOnlyDictionary<SymbolHandle, OwnershipFacts> OwnershipFacts => ownershipFacts;

    public void SetOwnershipFacts(SymbolHandle handle, OwnershipFacts facts) => ownershipFacts[handle] = facts;

    public bool TryGetOwnershipFacts(SymbolHandle handle, out OwnershipFacts? facts) =>
        ownershipFacts.TryGetValue(handle, out facts);

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

    /// <summary> Compilation options governing recursion limits and diagnostics. </summary>
    public CompilationOptions Options { get; }

    private static int nextProjectId = 1;
    public int ProjectId { get; }

    private readonly Dictionary<int, ResolutionContext> referencedContextsByProjectId = [];

    public bool TryGetReferencedContext(int projectId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ResolutionContext? context)
    {
        if (referencedContextsByProjectId.TryGetValue(projectId, out context))
            return true;

        foreach (var refProj in ReferencedProjects)
        {
            if (refProj.ProjectId == projectId)
            {
                context = refProj;
                referencedContextsByProjectId[projectId] = refProj;
                return true;
            }

            if (refProj.TryGetReferencedContext(projectId, out context))
            {
                referencedContextsByProjectId[projectId] = context;
                return true;
            }
        }

        context = null;
        return false;
    }

    private T TrackSymbol<T>(T symbol) where T : Symbol
    {
        symbol.ProjectId = ProjectId;
        return symbol;
    }

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
        IReadOnlyList<SyntaxTree> syntaxTrees,
        ResolvedTree? resolvedTree = null,
        NamespaceTrieNode? globalNamespace = null,
        List<Scope>? scopes = null,
        IReadOnlyList<ResolutionContext>? referencedProjects = null,
        DiagnosticsManager? diagnostics = null,
        CompilationOptions? options = null,
        DiscoveredIntrinsics? discoveredIntrinsics = null,
        DiscoveredOperators? discoveredOperators = null,
        ResolutionContext? baseContext = null)
    {
        SyntaxTrees = syntaxTrees;
        ResolvedTree = resolvedTree ?? baseContext?.ResolvedTree ?? new ResolvedTree();
        Diagnostics = diagnostics ?? baseContext?.Diagnostics ?? new DiagnosticsManager();
        Options = options ?? baseContext?.Options ?? new CompilationOptions();
        DiscoveredIntrinsics = discoveredIntrinsics ?? baseContext?.DiscoveredIntrinsics.Clone() ?? new DiscoveredIntrinsics();
        DiscoveredOperators = discoveredOperators ?? baseContext?.DiscoveredOperators.Clone() ?? new DiscoveredOperators();

        GlobalNamespace = globalNamespace ?? baseContext?.GlobalNamespace ?? new NamespaceTrieNode();
        Scopes = scopes ?? baseContext?.Scopes ?? [new Scope(null)];
        GlobalScope.GlobalNamespace = GlobalNamespace;

        if (baseContext != null)
        {
            AttributeSymbols.AddRange(baseContext.AttributeSymbols);
            NestedAttributeSymbols.AddRange(baseContext.NestedAttributeSymbols);
            TypeSymbols.AddRange(baseContext.TypeSymbols);
            NestedTypeSymbols.AddRange(baseContext.NestedTypeSymbols);
            FunctionSymbols.AddRange(baseContext.FunctionSymbols);
            MethodSymbols.AddRange(baseContext.MethodSymbols);
            GlobalVariableSymbols.AddRange(baseContext.GlobalVariableSymbols);
            FieldSymbols.AddRange(baseContext.FieldSymbols);
            ParameterSymbols.AddRange(baseContext.ParameterSymbols);
            PropertySymbols.AddRange(baseContext.PropertySymbols);
            GenericParameterSymbols.AddRange(baseContext.GenericParameterSymbols);
            LabelSymbols.AddRange(baseContext.LabelSymbols);
            AliasSymbols.AddRange(baseContext.AliasSymbols);
            MacroSymbols.AddRange(baseContext.MacroSymbols);
            EnumVariantSymbols.AddRange(baseContext.EnumVariantSymbols);
        }

        ReferencedProjects = referencedProjects ?? baseContext?.ReferencedProjects ?? [];
        ProjectId = baseContext?.ProjectId ?? System.Threading.Interlocked.Increment(ref nextProjectId);

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
        enumVariantID = EnumVariantSymbols.Count;

        ResolvedTree.OnReferenceAdded = (syntax, symbol) =>
        {
            if (CurrentTreeFilePath != null)
            {
                if (!treeNodeSymbols.TryGetValue(CurrentTreeFilePath, out var dict))
                {
                    dict = [];
                    treeNodeSymbols[CurrentTreeFilePath] = dict;
                }
                dict[syntax] = symbol;
            }
        };

        if (ReferencedProjects.Count > 0)
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
    }

    private static void MergeNamespaceTrie(NamespaceTrieNode target, NamespaceTrieNode source)
    {
        foreach (var symbol in source.Symbols)
            target.RegisterSymbol(symbol);

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
            symbol = new ProductTypeSymbol(id, enclosingScope, name, typeKind, containingNamespace, syntax);
        else
            symbol = new SumTypeSymbol(id, enclosingScope, name, typeKind, containingNamespace, syntax);

        TrackSymbol(symbol);
        if (isSink)
        {
            SinkSymbols.TypeSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            TypeSymbols.Add(symbol);

        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public MemberTypeSymbol CreateMemberTypeSymbol(Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent, TypeDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : nestedTypeID++;
        MemberTypeSymbol symbol;

        if (typeKind is TypeKind.Struct or TypeKind.Class or TypeKind.Delegate or TypeKind.Interface)
            symbol = new MemberProductTypeSymbol(id, enclosingScope, name, typeKind, parent, syntax);
        else
            symbol = new MemberSumTypeSymbol(id, enclosingScope, name, typeKind, parent, syntax);

        TrackSymbol(symbol);
        if (isSink)
        {
            SinkSymbols.NestedTypeSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            NestedTypeSymbols.Add(symbol);

        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalTypeSymbol CreateLocalTypeSymbol(Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent, TypeDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : nestedTypeID++;
        LocalTypeSymbol symbol;

        if (typeKind is TypeKind.Struct or TypeKind.Class or TypeKind.Delegate or TypeKind.Interface)
            symbol = new LocalProductTypeSymbol(id, enclosingScope, name, typeKind, parent, syntax);
        else
            symbol = new LocalSumTypeSymbol(id, enclosingScope, name, typeKind, parent, syntax);

        TrackSymbol(symbol);
        if (isSink)
        {
            SinkSymbols.NestedTypeSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            NestedTypeSymbols.Add(symbol);

        Register(enclosingScope, symbol);
        return symbol;
    }

    public AttributeSymbol CreateAttributeSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, AttributeSignature? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : attributeID++;
        var symbol = TrackSymbol(new AttributeSymbol(id, name, enclosingScope, containingNamespace, syntax));

        if (isSink)
        {
            SinkSymbols.AttributeSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            AttributeSymbols.Add(symbol);

        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public MemberAttributeSymbol CreateMemberAttributeSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, AttributeSignature? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : nestedAttributeID++;
        var symbol = TrackSymbol(new MemberAttributeSymbol(id, name, enclosingScope, parent, syntax));

        if (isSink)
        {
            SinkSymbols.NestedAttributeSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            NestedAttributeSymbols.Add(symbol);

        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalAttributeSymbol CreateLocalAttributeSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, AttributeSignature? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : nestedAttributeID++;
        var symbol = TrackSymbol(new LocalAttributeSymbol(id, name, enclosingScope, parent, syntax));

        if (isSink)
        {
            SinkSymbols.NestedAttributeSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            NestedAttributeSymbols.Add(symbol);

        Register(enclosingScope, symbol);
        return symbol;
    }

    public FunctionSymbol CreateFunctionSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, FunctionDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : functionID++;
        var symbol = TrackSymbol(new FunctionSymbol(id, enclosingScope, name, containingNamespace, syntax));

        if (isSink)
        {
            SinkSymbols.FunctionSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            FunctionSymbols.Add(symbol);

        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public MemberMethodSymbol CreateMemberMethodSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, FunctionDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : methodID++;
        var symbol = TrackSymbol(new MemberMethodSymbol(id, enclosingScope, name, parent, syntax));

        if (isSink)
        {
            SinkSymbols.MethodSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            MethodSymbols.Add(symbol);

        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalFunctionSymbol CreateLocalFunctionSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, FunctionDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : methodID++;
        var symbol = TrackSymbol(new LocalFunctionSymbol(id, name, enclosingScope, parent, syntax));

        if (isSink)
        {
            SinkSymbols.MethodSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            MethodSymbols.Add(symbol);

        Register(enclosingScope, symbol);
        return symbol;
    }

    public GlobalVariableSymbol CreateGlobalVariableSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, VariableDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : globalVariableID++;
        var symbol = TrackSymbol(new GlobalVariableSymbol(id, enclosingScope, name, containingNamespace, syntax));

        if (isSink)
        {
            SinkSymbols.GlobalVariableSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            GlobalVariableSymbols.Add(symbol);

        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public FieldSymbol CreateFieldSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, VariableDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : fieldID++;
        var symbol = TrackSymbol(new FieldSymbol(id, enclosingScope, name, parent, syntax));

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
        var symbol = TrackSymbol(new ParameterSymbol(id, enclosingScope, name, containingSymbol, syntax));

        if (isSink)
        {
            SinkSymbols.ParameterSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            ParameterSymbols.Add(symbol);

        Register(enclosingScope, symbol);
        return symbol;
    }

    public LocalVariableSymbol CreateLocalVariableSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, VariableDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        if (isSink)
        {
            var sinkSym = TrackSymbol(new LocalVariableSymbol(new SymbolID(sinkID--), enclosingScope, name, parent, syntax));
            sinkLocalVariables.Add(sinkSym);
            sinkSymbolsByHandle[GetHandle(sinkSym)] = sinkSym;

            if (name.Text != "_")
            {
                if (parent is { Kind: SymbolKind.Function })
                {
                    var func = GetFunctionSymbol(parent.Value);
                    func?.LocalVariables.Add(sinkSym);
                }
                else if (parent is { Kind: SymbolKind.Method })
                {
                    var method = GetMethodSymbol(parent.Value);
                    method?.LocalVariables.Add(sinkSym);
                }
            }

            Register(enclosingScope, sinkSym);
            return sinkSym;
        }

        SymbolID id = 0;
        if (parent is { Kind: SymbolKind.Function, ID: var funcId } && (int)funcId >= 0 && (int)funcId < FunctionSymbols.Count)
        {
            var func = FunctionSymbols[funcId];
            id = new SymbolID(func.LocalVariables.Count);
            var symbol = TrackSymbol(new LocalVariableSymbol(id, enclosingScope, name, parent, syntax));
            func.LocalVariables.Add(symbol);
            Register(enclosingScope, symbol);
            return symbol;
        }
        else if (parent is { Kind: SymbolKind.Method, ID: var methodId } && (int)methodId >= 0 && (int)methodId < MethodSymbols.Count)
        {
            var method = MethodSymbols[methodId];
            id = new SymbolID(method.LocalVariables.Count);
            var symbol = TrackSymbol(new LocalVariableSymbol(id, enclosingScope, name, parent, syntax));
            method.LocalVariables.Add(symbol);
            Register(enclosingScope, symbol);
            return symbol;
        }
        else
        {
            var symbol = TrackSymbol(new LocalVariableSymbol(id, enclosingScope, name, parent, syntax));
            Register(enclosingScope, symbol);
            return symbol;
        }
    }

    public PropertySymbol CreatePropertySymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? parent, bool hasBacking, MemberPropertyDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : propertyID++;
        var symbol = TrackSymbol(new PropertySymbol(id, enclosingScope, name, parent, hasBacking, syntax));

        if (isSink)
        {
            SinkSymbols.PropertySymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            PropertySymbols.Add(symbol);

        Register(enclosingScope, symbol);
        return symbol;
    }

    public GenericParameterSymbol CreateGenericParameterSymbol(Scope enclosingScope, SymbolPart name, Symbol genericSymbol, GenericParameterKind parameterKind, bool isVariadic)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : genericParameterID++;
        var symbol = TrackSymbol(new GenericParameterSymbol(id, enclosingScope, name, genericSymbol, parameterKind, isVariadic));

        if (isSink)
        {
            SinkSymbols.GenericParameterSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            GenericParameterSymbols.Add(symbol);

        Register(enclosingScope, symbol);
        return symbol;
    }

    public LabelSymbol CreateLabelSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? containingFunction, SyntaxNode? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : labelID++;
        var symbol = TrackSymbol(new LabelSymbol(id, enclosingScope, name, containingFunction, syntax));

        if (isSink)
        {
            SinkSymbols.LabelSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            LabelSymbols.Add(symbol);

        Register(enclosingScope, symbol);
        return symbol;
    }

    public AliasSymbol CreateAliasSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? containingSymbol, AliasDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : aliasID++;
        var symbol = TrackSymbol(new AliasSymbol(id, enclosingScope, name, containingSymbol, syntax));

        if (isSink)
        {
            SinkSymbols.AliasSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            AliasSymbols.Add(symbol);

        Register(enclosingScope, symbol);
        return symbol;
    }

    public AliasSymbol CreateAliasSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, AliasDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : aliasID++;
        var symbol = TrackSymbol(new AliasSymbol(id, enclosingScope, name, containingNamespace, syntax));

        if (isSink)
        {
            SinkSymbols.AliasSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            AliasSymbols.Add(symbol);

        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public MacroSymbol CreateMacroSymbol(Scope enclosingScope, SymbolPart name, SymbolHandle? containingSymbol, MacroDeclaration syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : macroID++;
        var symbol = TrackSymbol(new MacroSymbol(id, enclosingScope, name, containingSymbol, syntax));

        if (isSink)
        {
            SinkSymbols.MacroSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            MacroSymbols.Add(symbol);

        Register(enclosingScope, symbol);
        return symbol;
    }

    public MacroSymbol CreateMacroSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, MacroDeclaration syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : macroID++;
        var symbol = TrackSymbol(new MacroSymbol(id, enclosingScope, name, containingNamespace, syntax));

        if (isSink)
        {
            SinkSymbols.MacroSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            MacroSymbols.Add(symbol);

        Register(enclosingScope, symbol, containingNamespace);
        return symbol;
    }

    public EnumVariantSymbol CreateEnumVariantSymbol(
        Scope enclosingScope,
        SymbolPart name,
        EnumVariantKind variantKind,
        SymbolHandle containingType,
        EnumVariant? syntax,
        Expression? explicitValue = null)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : enumVariantID++;
        var symbol = TrackSymbol(new EnumVariantSymbol(id, enclosingScope, name, variantKind, containingType, syntax, explicitValue));

        if (isSink)
        {
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            EnumVariantSymbols.Add(symbol);

        Register(enclosingScope, symbol);
        return symbol;
    }

    public FunctionSymbol? GetFunctionSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Function)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetFunctionSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < FunctionSymbols.Count)
            return FunctionSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is FunctionSymbol func)
            return func;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetFunctionSymbol(handle) is { } refFn)
                    return refFn;
            }
        }

        return null;
    }

    public MethodSymbol? GetMethodSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Method)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetMethodSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < MethodSymbols.Count)
            return MethodSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is MethodSymbol method)
            return method;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetMethodSymbol(handle) is { } refMethod)
                    return refMethod;
            }
        }

        return null;
    }

    public TypeSymbol? GetTypeSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Type)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetTypeSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < TypeSymbols.Count)
            return TypeSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is TypeSymbol t)
            return t;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetTypeSymbol(handle) is { } refType)
                    return refType;
            }
        }

        return null;
    }

    public NestedTypeSymbol? GetNestedTypeSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.NestedType)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetNestedTypeSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < NestedTypeSymbols.Count)
            return NestedTypeSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is NestedTypeSymbol t)
            return t;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetNestedTypeSymbol(handle) is { } refType)
                    return refType;
            }
        }

        return null;
    }

    public AliasSymbol? GetAliasSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Alias)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetAliasSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < AliasSymbols.Count)
            return AliasSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is AliasSymbol a)
            return a;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetAliasSymbol(handle) is { } refAlias)
                    return refAlias;
            }
        }

        return null;
    }

    public GenericParameterSymbol? GetGenericParameterSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.GenericParameter)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetGenericParameterSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < GenericParameterSymbols.Count)
            return GenericParameterSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is GenericParameterSymbol gp)
            return gp;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetGenericParameterSymbol(handle) is { } refGp)
                    return refGp;
            }
        }

        return null;
    }

    public ParameterSymbol? GetParameterSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Parameter)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetParameterSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < ParameterSymbols.Count)
            return ParameterSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is ParameterSymbol p)
            return p;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetParameterSymbol(handle) is { } refP)
                    return refP;
            }
        }

        return null;
    }

    public AttributeSymbol? GetAttributeSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Attribute)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetAttributeSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < AttributeSymbols.Count)
            return AttributeSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is AttributeSymbol a)
            return a;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetAttributeSymbol(handle) is { } refAttr)
                    return refAttr;
            }
        }

        return null;
    }

    public NestedAttributeSymbol? GetNestedAttributeSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.NestedAttribute)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetNestedAttributeSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < NestedAttributeSymbols.Count)
            return NestedAttributeSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is NestedAttributeSymbol a)
            return a;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetNestedAttributeSymbol(handle) is { } refAttr)
                    return refAttr;
            }
        }

        return null;
    }

    public FieldSymbol? GetFieldSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Field)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetFieldSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < FieldSymbols.Count)
            return FieldSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is FieldSymbol f)
            return f;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetFieldSymbol(handle) is { } refField)
                    return refField;
            }
        }

        return null;
    }

    public PropertySymbol? GetPropertySymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Property)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetPropertySymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < PropertySymbols.Count)
            return PropertySymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is PropertySymbol p)
            return p;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetPropertySymbol(handle) is { } refProp)
                    return refProp;
            }
        }

        return null;
    }

    public EnumVariantSymbol? GetEnumVariantSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.EnumVariant)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetEnumVariantSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < EnumVariantSymbols.Count)
            return EnumVariantSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is EnumVariantSymbol v)
            return v;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetEnumVariantSymbol(handle) is { } refEnum)
                    return refEnum;
            }
        }

        return null;
    }

    public GlobalVariableSymbol? GetGlobalVariableSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.GlobalVariable)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetGlobalVariableSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < GlobalVariableSymbols.Count)
            return GlobalVariableSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is GlobalVariableSymbol g)
            return g;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetGlobalVariableSymbol(handle) is { } refGlob)
                    return refGlob;
            }
        }

        return null;
    }

    public Symbol? GetSymbol(SymbolHandle handle)
    {
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetSymbol(handle);

            return null;
        }

        var sym = handle.Kind switch
        {
            SymbolKind.Type => GetTypeSymbol(handle),
            SymbolKind.NestedType => GetNestedTypeSymbol(handle),
            SymbolKind.Function => GetFunctionSymbol(handle),
            SymbolKind.Method => GetMethodSymbol(handle),
            SymbolKind.GlobalVariable => GetGlobalVariableSymbol(handle),
            SymbolKind.Variable => sinkSymbolsByHandle.TryGetValue(handle, out var sl) ? sl : null,
            SymbolKind.Field => GetFieldSymbol(handle),
            SymbolKind.Property => GetPropertySymbol(handle),
            SymbolKind.Alias => GetAliasSymbol(handle),
            SymbolKind.GenericParameter => GetGenericParameterSymbol(handle),
            SymbolKind.Parameter => GetParameterSymbol(handle),
            SymbolKind.Attribute => GetAttributeSymbol(handle),
            SymbolKind.NestedAttribute => GetNestedAttributeSymbol(handle),
            SymbolKind.EnumVariant => GetEnumVariantSymbol(handle),
            _ => sinkSymbolsByHandle.TryGetValue(handle, out var s) ? s : null
        };

        if (sym is not null)
            return sym;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetSymbol(handle) is { } refSym)
                    return refSym;
            }
        }

        return null;
    }

    /// <summary> Tests whether a symbol was declared in the current compilation context rather than imported from a referenced project. </summary>
    public bool IsDeclaredInCurrentProject(SymbolHandle handle) =>
        handle.ProjectId == 0 || handle.ProjectId == ProjectId;

    public static SymbolHandle GetHandle(Symbol symbol) => new(symbol.Kind, symbol.ID, symbol.ProjectId);

    public static void BindChildScope(Scope parent, Symbol owner, Scope child) => parent.ChildScopes[GetHandle(owner)] = child;

    public void RegisterSyntaxScope(SyntaxNode syntax, Scope scope) => SyntaxScopes[syntax] = scope;

    public Scope GetSyntaxScope(SyntaxNode syntax, Scope fallback) => SyntaxScopes.TryGetValue(syntax, out var scope) ? scope : fallback;

    /// <summary>
    /// Gets the underlying type <see cref="SymbolHandle"/> (global or nested) referenced by a <see cref="TypeRef"/>,
    /// unwrapping any aliases transitively. Returns <c>null</c> if unresolved or not a type.
    /// </summary>
    public SymbolHandle? GetType(TypeRef typeRef)
    {
        if (typeRef.Kind == TypeRefKind.Untagged && typeRef.SpecialType is UntaggedSemanticType untagged)
            return GetType(untagged.InnerType);

        if (typeRef.Kind == TypeRefKind.Function)
            return DiscoveredIntrinsics.TryGet(KnownIntrinsics.Function, out var fnH) ? fnH : null;

        if (typeRef.Kind == TypeRefKind.Delegate)
            return DiscoveredIntrinsics.TryGet(KnownIntrinsics.Delegate, out var delH) ? delH : null;

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
        if (typeRef.Kind == TypeRefKind.Untagged && typeRef.SpecialType is UntaggedSemanticType untagged)
            return TypeRef.Untagged(UnwrapAlias(untagged.InnerType));

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

    /// <summary>
    /// Unwraps any aliases transitively, returning the underlying symbol handle.
    /// </summary>
    public SymbolHandle UnwrapAlias(SymbolHandle handle)
    {
        var visited = new HashSet<SymbolHandle>();
        var curr = handle;
        while (curr.Kind == SymbolKind.Alias && visited.Add(curr))
        {
            var alias = GetAliasSymbol(curr);
            if (alias is null || !alias.Target.IsResolved || alias.Target.Handle is not { } targetH)
                break;
            curr = targetH;
        }
        return curr;
    }

    private void Register(Scope scope, Symbol symbol, NamespaceTrieNode? containingNamespace = null)
    {
        if (symbol.Name.Text == "_")
            return;

        var handle = GetHandle(symbol);
        string? treePath = CurrentTreeFilePath ?? GetSymbolSourceFilePath(symbol);
        if (treePath != null)
            TrackTreeSymbol(treePath, handle);

        scope.Symbols[handle] = symbol;

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

    private static string? GetSymbolSourceFilePath(Symbol symbol) => symbol switch
    {
        TypeSymbol t => t.Syntax?.GetSource()?.FilePath,
        NestedTypeSymbol nt => nt.Syntax?.GetSource()?.FilePath,
        FunctionSymbol f => f.Syntax?.GetSource()?.FilePath,
        MethodSymbol m => m.Syntax?.GetSource()?.FilePath,
        GlobalVariableSymbol g => g.Syntax?.GetSource()?.FilePath,
        FieldSymbol fld => fld.Syntax?.GetSource()?.FilePath,
        ParameterSymbol p => p.Syntax?.GetSource()?.FilePath,
        PropertySymbol prop => prop.Syntax?.GetSource()?.FilePath,
        AliasSymbol a => a.Syntax?.GetSource()?.FilePath,
        _ => null
    };

    /// <summary>
    /// Extracts AST-to-symbol, expression-to-type, and CFG bindings belonging to a specific syntax tree from the global resolution context.
    /// </summary>
    /// <param name="tree">The syntax tree whose local bindings should be extracted.</param>
    /// <returns>A tuple containing node symbols, expression types, and control flow graphs for the syntax tree.</returns>
    public (Dictionary<SyntaxNode, SymbolHandle> NodeSymbols,
            Dictionary<Expression, TypeRef> ExpressionTypes,
            Dictionary<SymbolHandle, ControlFlow.ControlFlowGraph> ControlFlowGraphs) ExtractTreeBindings(SyntaxTree tree)
    {
        var nodeSymbols = treeNodeSymbols.TryGetValue(tree.FilePath, out var nodes)
            ? new Dictionary<SyntaxNode, SymbolHandle>(nodes)
            : [];
        var exprTypes = treeExpressionTypes.TryGetValue(tree.FilePath, out var exprs)
            ? new Dictionary<Expression, TypeRef>(exprs)
            : [];
        var cfgs = new Dictionary<SymbolHandle, ControlFlow.ControlFlowGraph>();

        var treeHandles = new HashSet<SymbolHandle>(GetTreeSymbols(tree.FilePath));
        foreach (var (handle, cfg) in controlFlowGraphs)
        {
            if (treeHandles.Contains(handle))
            {
                cfgs[handle] = cfg;
            }
        }

        return (nodeSymbols, exprTypes, cfgs);
    }

    /// <summary>
    /// Merges cached semantic bindings from a reused <see cref="SemanticModel"/> back into the active resolution context.
    /// </summary>
    /// <param name="model">The cached semantic model whose bindings are to be incorporated.</param>
    public void MergeTreeBindings(SemanticModel model)
    {
        string path = model.SyntaxTree.FilePath;
        if (!treeNodeSymbols.TryGetValue(path, out var nodes))
        {
            nodes = [];
            treeNodeSymbols[path] = nodes;
        }
        foreach (var (node, handle) in model.NodeSymbols)
        {
            ResolvedTree.AddReference(node, handle);
            nodes[node] = handle;
        }

        if (!treeExpressionTypes.TryGetValue(path, out var exprs))
        {
            exprs = [];
            treeExpressionTypes[path] = exprs;
        }
        foreach (var (expr, type) in model.ExpressionTypes)
        {
            expressionTypes[expr] = type;
            exprs[expr] = type;
        }

        foreach (var (handle, cfg) in model.ControlFlowGraphs)
            controlFlowGraphs[handle] = cfg;
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

    public static string GetQualifiedSymbolName(SymbolHandle handle, ResolutionContext? context)
    {
        if (context is null)
            return handle.ToString() ?? string.Empty;

        var sym = (Symbol?)context.GetTypeSymbol(handle) ??
                  (Symbol?)context.GetNestedTypeSymbol(handle) ??
                  (Symbol?)context.GetAliasSymbol(handle) ??
                  (Symbol?)context.GetGenericParameterSymbol(handle) ??
                  context.GetSymbol(handle);

        if (sym is null)
            return handle.ToString() ?? string.Empty;

        return GetQualifiedSymbolName(sym, context);
    }

    public static string GetQualifiedSymbolName(Symbol sym, ResolutionContext? context)
    {
        if (sym is AliasSymbol alias)
        {
            if (alias.Target.IsResolved && alias.Target.Handle is { } targetH && context is not null)
                return GetQualifiedSymbolName(targetH, context);
            return alias.Name.Text;
        }

        if (sym is GenericParameterSymbol gp)
            return gp.Name.Text;

        if (sym is NestedTypeSymbol nested)
        {
            if (nested.Parent is { } parentH && context is not null)
                return $"{GetQualifiedSymbolName(parentH, context)}.{nested.Name.Text}";
            return nested.Name.Text;
        }

        if (sym is TypeSymbol typeSym)
        {
            if (typeSym.ContainingNamespace != null && typeSym.ContainingNamespace != context?.GlobalNamespace)
            {
                var ns = GetNamespacePath(typeSym.ContainingNamespace, context?.GlobalNamespace);
                if (!string.IsNullOrEmpty(ns))
                    return $"{ns}.{typeSym.Name.Text}";
            }

            return typeSym.Name.Text;
        }

        if (sym is FunctionSymbol fn)
        {
            if (fn.ContainingNamespace != null && fn.ContainingNamespace != context?.GlobalNamespace)
            {
                var ns = GetNamespacePath(fn.ContainingNamespace, context?.GlobalNamespace);
                if (!string.IsNullOrEmpty(ns))
                    return $"{ns}.{fn.Name.Text}";
            }
            return fn.Name.Text;
        }

        return sym.Name.Text;
    }

    public static string GetLabelSymbolName(SymbolHandle handle, ResolutionContext? context, Scope? scope)
    {
        if (context is null)
            return handle.ToString() ?? string.Empty;

        var sym = (Symbol?)context.GetTypeSymbol(handle) ??
                  (Symbol?)context.GetNestedTypeSymbol(handle) ??
                  (Symbol?)context.GetAliasSymbol(handle) ??
                  (Symbol?)context.GetGenericParameterSymbol(handle) ??
                  context.GetSymbol(handle);

        if (sym is null)
            return handle.ToString() ?? string.Empty;

        return GetLabelSymbolName(sym, context, scope);
    }

    public static string GetLabelSymbolName(Symbol sym, ResolutionContext? context, Scope? scope)
    {
        if (sym is AliasSymbol alias)
            return alias.Name.Text;

        if (sym is GenericParameterSymbol gp)
            return gp.Name.Text;

        if (sym is NestedTypeSymbol nested)
        {
            if (nested.Parent is { } parentH && context is not null)
                return $"{GetLabelSymbolName(parentH, context, scope)}.{nested.Name.Text}";
            return nested.Name.Text;
        }

        if (sym is TypeSymbol typeSym)
        {
            if (typeSym.ContainingNamespace == null || typeSym.ContainingNamespace == context?.GlobalNamespace)
                return typeSym.Name.Text;

            if (scope != null && IsNamespaceImported(scope, typeSym.ContainingNamespace))
                return typeSym.Name.Text;

            var ns = GetNamespacePath(typeSym.ContainingNamespace, context?.GlobalNamespace);
            return !string.IsNullOrEmpty(ns) ? $"{ns}.{typeSym.Name.Text}" : typeSym.Name.Text;
        }

        if (sym is FunctionSymbol fn)
        {
            if (fn.ContainingNamespace == null || fn.ContainingNamespace == context?.GlobalNamespace)
                return fn.Name.Text;

            if (scope != null && IsNamespaceImported(scope, fn.ContainingNamespace))
                return fn.Name.Text;

            var ns = GetNamespacePath(fn.ContainingNamespace, context?.GlobalNamespace);
            return !string.IsNullOrEmpty(ns) ? $"{ns}.{fn.Name.Text}" : fn.Name.Text;
        }

        return sym.Name.Text;
    }

    public static bool IsNamespaceImported(Scope scope, NamespaceTrieNode targetNs)
    {
        for (var current = scope; current != null; current = current.Parent)
        {
            foreach (var usingNs in current.UsingNamespaces)
            {
                if (usingNs == targetNs || usingNs.Name.Text == targetNs.Name.Text)
                    return true;
            }
        }
        return false;
    }

    public static bool IsNamespaceImported(Scope scope, string nsName)
    {
        for (var current = scope; current != null; current = current.Parent)
        {
            foreach (var usingNs in current.UsingNamespaces)
            {
                if (usingNs.Name.Text == nsName)
                    return true;
            }
        }
        return false;
    }

    public static string GetNamespacePath(NamespaceTrieNode node, NamespaceTrieNode? globalNs)
    {
        var parts = new List<string>();
        var curr = node;
        while (curr != null && curr != globalNs && !string.IsNullOrEmpty(curr.Name.Text))
        {
            parts.Add(curr.Name.Text);
            curr = curr.Parent;
        }
        parts.Reverse();
        return string.Join(".", parts);
    }



    public SymbolName GetScopedSymbolName(NamedSyntax name) =>
        GetSymbolName(name, GetHygiene(name));

    public static SymbolName GetSymbolName(NamedSyntax name, HygieneId hygiene = default) => name switch
    {
        SimpleName simpleName => new SymbolName(new SymbolPart(simpleName.Name, 0, hygiene)),
        GenericName genericName => new SymbolName(new SymbolPart(genericName.Name, genericName.GenericParameters.Count, hygiene)),
        QualifiedName qualifiedName => GetQualifiedName(qualifiedName, hygiene),
        TupleName => new SymbolName(new SymbolPart(string.Empty, 0, hygiene)),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static SymbolPart GetSymbolPart(NamedSyntax name, HygieneId hygiene = default) => name switch
    {
        SimpleName simpleName => new SymbolPart(simpleName.Name, 0, hygiene),
        GenericName genericName => new SymbolPart(genericName.Name, genericName.GenericParameters.Count, hygiene),
        TupleName => new SymbolPart(string.Empty, 0, hygiene),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
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

            case PrefixModifiedType prefixModified:
                AddTypeNameParts(prefixModified.Type, parts);
                break;

            case TupleType:
            case UniformTupleType:
            case FunctionTypeSyntax:
            case DelegateTypeSyntax:
            case UnionType:
            case LiteralGenericArgument:
                parts.Add(new SymbolPart(string.Empty));
                break;

            case NamedExpressionGenericArgument named:
                parts.Add(new SymbolPart(named.Expression.Identifier));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(type), $"Unhandled type syntax: {type.GetType().FullName}");
        }
    }
}
