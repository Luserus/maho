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
    public List<ConstantSymbol> ConstantSymbols { get; } = [];
    public List<FieldSymbol> FieldSymbols { get; } = [];
    public List<ExtensionSymbol> ExtensionSymbols { get; } = [];
    public List<SymbolHandle> StaticConstructors { get; } = [];
    public SymbolHandle? EntryPoint { get; internal set; }
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

    /// <summary> Canonical PascalCase module symbol identity for this compilation. </summary>
    public string ModuleName { get; }

    /// <summary> Imported modules grouped by canonical module name (non-clashing with lexical/namespace tables). </summary>
    public Dictionary<string, List<ModuleSymbol>> ModulesByName { get; } = new(StringComparer.Ordinal);

    /// <summary> Imported modules keyed by explicit compiler reference alias for <c>extern using</c>. </summary>
    public Dictionary<string, ModuleSymbol> ExternAliasesByName { get; } = new(StringComparer.Ordinal);

    /// <summary> Scoped interface conformances declared within this compilation module. </summary>
    public List<InterfaceConformance> Conformances { get; } = [];
    private readonly Dictionary<SymbolHandle, List<InterfaceConformance>> conformancesByTarget = [];
    private int nextConformanceSerial;

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
    private int constantID;
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

        if (baseContext is not null)
        {
            AttributeSymbols.AddRange(baseContext.AttributeSymbols);
            NestedAttributeSymbols.AddRange(baseContext.NestedAttributeSymbols);
            TypeSymbols.AddRange(baseContext.TypeSymbols);
            NestedTypeSymbols.AddRange(baseContext.NestedTypeSymbols);
            FunctionSymbols.AddRange(baseContext.FunctionSymbols);
            MethodSymbols.AddRange(baseContext.MethodSymbols);
            GlobalVariableSymbols.AddRange(baseContext.GlobalVariableSymbols);
            ConstantSymbols.AddRange(baseContext.ConstantSymbols);
            FieldSymbols.AddRange(baseContext.FieldSymbols);
            ParameterSymbols.AddRange(baseContext.ParameterSymbols);
            PropertySymbols.AddRange(baseContext.PropertySymbols);
            GenericParameterSymbols.AddRange(baseContext.GenericParameterSymbols);
            LabelSymbols.AddRange(baseContext.LabelSymbols);
            AliasSymbols.AddRange(baseContext.AliasSymbols);
            MacroSymbols.AddRange(baseContext.MacroSymbols);
            EnumVariantSymbols.AddRange(baseContext.EnumVariantSymbols);
            ExtensionSymbols.AddRange(baseContext.ExtensionSymbols);
        }

        ReferencedProjects = referencedProjects ?? baseContext?.ReferencedProjects ?? [];
        ProjectId = baseContext?.ProjectId ?? System.Threading.Interlocked.Increment(ref nextProjectId);
        ModuleName = !string.IsNullOrWhiteSpace(Options.ModuleName)
            ? CompilationOptions.NormalizeModuleIdentifier(Options.ModuleName)
            : baseContext?.ModuleName ?? CompilationOptions.NormalizeModuleIdentifier(
                SyntaxTrees.Count > 0 ? System.IO.Path.GetFileNameWithoutExtension(SyntaxTrees[0].FilePath) : null);

        if (baseContext is not null)
        {
            foreach (var (k, v) in baseContext.ModulesByName)
                ModulesByName[k] = [.. v];
            foreach (var (k, v) in baseContext.ExternAliasesByName)
                ExternAliasesByName[k] = v;
        }

        attributeID = AttributeSymbols.Count;
        nestedAttributeID = NestedAttributeSymbols.Count;
        typeID = TypeSymbols.Count;
        nestedTypeID = NestedTypeSymbols.Count;
        functionID = FunctionSymbols.Count;
        methodID = MethodSymbols.Count;
        globalVariableID = GlobalVariableSymbols.Count;
        constantID = ConstantSymbols.Count;
        fieldID = FieldSymbols.Count;
        parameterID = ParameterSymbols.Count;
        propertyID = PropertySymbols.Count;
        genericParameterID = GenericParameterSymbols.Count;
        labelID = LabelSymbols.Count;
        aliasID = AliasSymbols.Count;
        macroID = MacroSymbols.Count;
        enumVariantID = EnumVariantSymbols.Count;
        extensionID = ExtensionSymbols.Count;

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
        for (int i = 0; i < ReferencedProjects.Count; i++)
        {
            var project = ReferencedProjects[i];
            MergeNamespaceTrie(GlobalNamespace, project.GlobalNamespace);

            if (!GlobalScope.ImportedScopes.Contains(project.GlobalScope))
                GlobalScope.ImportedScopes.Add(project.GlobalScope);

            string? alias = null;
            if (Options.ReferencedAliases is { Count: > 0 } && i < Options.ReferencedAliases.Count &&
                !string.IsNullOrWhiteSpace(Options.ReferencedAliases[i]))
            {
                alias = Options.ReferencedAliases[i];
            }
            else if (i < Options.ReferencedProjects.Count)
            {
                CompilationOptions.ParseProjectReferenceSpec(Options.ReferencedProjects[i], out alias, out _);
            }

            var moduleSym = new ModuleSymbol(new SymbolID(i), GlobalScope, project.ModuleName, alias, project);
            if (!ModulesByName.TryGetValue(project.ModuleName, out var list))
            {
                list = [];
                ModulesByName[project.ModuleName] = list;
            }
            list.Add(moduleSym);

            if (!string.IsNullOrWhiteSpace(alias))
                ExternAliasesByName[alias] = moduleSym;
        }
    }

    internal static bool IsTypeSymbol(Symbol s) =>
        s is TypeSymbol or NestedTypeSymbol or GenericParameterSymbol or AliasSymbol;

    internal IReadOnlyList<ResolutionContext> ResolveModuleTargets(Token qualifier, Scope scope, out bool isGlobalSelf)
    {
        if (qualifier.MatchingKind == MatchingKeywordKind.Global)
        {
            isGlobalSelf = true;
            return [this];
        }

        isGlobalSelf = false;
        if (scope.TryLookupExternAlias(qualifier.Value, out var aliasedModule))
            return [aliasedModule.SourceContext];

        if (ModulesByName.TryGetValue(qualifier.Value, out var matchingModules))
        {
            var list = new List<ResolutionContext>(matchingModules.Count);
            foreach (var m in matchingModules)
                list.Add(m.SourceContext);
            return list;
        }

        return [];
    }

    internal IReadOnlyList<Symbol> ResolveScopedSymbols(
        Token qualifier,
        SymbolName memberPath,
        Scope scope,
        bool typesOnly,
        Maho.Text.TextSpan diagnosticSpan,
        Maho.Text.SourceText? diagnosticSource,
        bool reportAmbiguity,
        out bool isAmbiguousAcrossModules)
    {
        isAmbiguousAcrossModules = false;
        var targets = ResolveModuleTargets(qualifier, scope, out _);
        if (targets.Count == 0 || memberPath.Count == 0)
            return [];

        List<List<Symbol>> matchingGroups = [];
        foreach (var targetMod in targets)
        {
            var matches = ResolveInModuleRoot(targetMod, memberPath, typesOnly);
            if (matches.Count > 0)
                matchingGroups.Add(matches);
        }

        if (matchingGroups.Count > 1)
        {
            isAmbiguousAcrossModules = true;
            if (reportAmbiguity)
                Diagnostics.ReportAmbiguousModuleMember(diagnosticSpan, qualifier.Value, memberPath.ToDisplayString(), diagnosticSource);
            return [];
        }

        return matchingGroups.Count == 1 ? matchingGroups[0] : [];
    }

    internal List<(ResolutionContext Module, NamespaceTrieNode Node)> ResolveScopedNamespaces(
        Token qualifier,
        SymbolName nsPath,
        Scope scope,
        Maho.Text.TextSpan diagnosticSpan,
        Maho.Text.SourceText? diagnosticSource,
        bool reportAmbiguity)
    {
        var targets = ResolveModuleTargets(qualifier, scope, out _);
        var result = new List<(ResolutionContext, NamespaceTrieNode)>();
        if (targets.Count == 0 || nsPath.Count == 0)
            return result;

        foreach (var targetMod in targets)
        {
            var curr = targetMod.GlobalNamespace;
            bool ok = true;
            for (int i = 0; i < nsPath.Count; i++)
            {
                if (!curr.Next.TryGetValue(nsPath[i], out var next))
                {
                    ok = false;
                    break;
                }
                curr = next;
            }

            if (ok && HasOwnedSymbolsInSubtree(curr, targetMod.ProjectId))
                result.Add((targetMod, curr));
        }

        if (reportAmbiguity && result.Count > 1)
            Diagnostics.ReportAmbiguousModuleMember(diagnosticSpan, qualifier.Value, nsPath.ToDisplayString(), diagnosticSource);

        return result;
    }

    internal InterfaceConformance RegisterConformance(
        TypeRef targetType,
        TypeRef interfaceType,
        SymbolHandle interfaceHandle,
        Accessibility accessibility,
        SymbolHandle ownerHandle,
        bool isExtension,
        BaseTypeItemSyntax? syntax)
    {
        var unwrappedTargetH = targetType.Handle is { } th ? UnwrapAlias(th) : default;
        var unwrappedIfaceH = UnwrapAlias(interfaceHandle);
        var conformance = new InterfaceConformance(
            nextConformanceSerial++,
            targetType,
            interfaceType,
            unwrappedIfaceH,
            accessibility,
            ownerHandle,
            isExtension,
            ModuleName,
            ProjectId,
            syntax);

        Conformances.Add(conformance);
        if (unwrappedTargetH != default)
        {
            if (!conformancesByTarget.TryGetValue(unwrappedTargetH, out var list))
            {
                list = [];
                conformancesByTarget[unwrappedTargetH] = list;
            }
            list.Add(conformance);
        }

        return conformance;
    }

    internal void CollectConformancesForTarget(
        SymbolHandle targetTypeHandle,
        List<InterfaceConformance> destination)
    {
        var visitedProjects = new HashSet<int>();
        CollectFromProject(this);

        void CollectFromProject(ResolutionContext proj)
        {
            if (!visitedProjects.Add(proj.ProjectId))
                return;

            if (proj.conformancesByTarget.TryGetValue(targetTypeHandle, out var localList))
                destination.AddRange(localList);

            foreach (var dep in proj.ReferencedProjects)
                CollectFromProject(dep);
        }
    }

    internal bool HasAnyConformance(SymbolHandle sourceHandle, SymbolHandle interfaceHandle, GenericSemanticType? superGen = null)
    {
        var visited = new HashSet<SymbolHandle>();
        return CheckRecursive(UnwrapAlias(sourceHandle));

        bool CheckRecursive(SymbolHandle h)
        {
            if (!visited.Add(h))
                return false;

            var list = new List<InterfaceConformance>();
            CollectConformancesForTarget(h, list);
            foreach (var conf in list)
            {
                if (conf.InterfaceHandle == interfaceHandle || TypeRef.IsSubtypeOf(conf.InterfaceHandle, interfaceHandle, this))
                {
                    if (superGen is not null && conf.InterfaceType.SpecialType is GenericSemanticType confGen)
                    {
                        if (!confGen.Equals(superGen))
                            continue;
                    }
                    return true;
                }
            }

            IReadOnlyList<TypeRef> bases = (GetSymbol(h)) switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                _ => []
            };
            foreach (var b in bases)
            {
                if (b.Handle is { } bh && CheckRecursive(UnwrapAlias(bh)))
                    return true;
            }
            return false;
        }
    }

    internal enum ConformanceLookupStatus
    {
        NotApplicable,
        Found,
        Inaccessible,
        Ambiguous,
        AmbiguousModule,
        NotFound
    }

    internal readonly record struct ConformanceLookupResult(
        ConformanceLookupStatus Status,
        InterfaceConformance? Conformance,
        IReadOnlyList<InterfaceConformance>? AmbiguousCandidates = null,
        string? AmbiguousModuleName = null);

    internal SymbolHandle? GetEnclosingOwnerHandle(SymbolHandle containingFunction, Scope? scope)
    {
        if (containingFunction != default && GetSymbol(containingFunction) is { } fnSym)
        {
            if (fnSym is MethodSymbol m && m.Parent is { } mParent)
                return mParent;
            if (fnSym is PropertySymbol p && p.Parent is { } pParent)
                return pParent;
        }

        for (var cur = scope; cur is not null; cur = cur.Parent)
        {
            if (cur.Parent is { } parentScope)
            {
                foreach (var (assoc, childScope) in parentScope.ChildScopes)
                {
                    if (!ReferenceEquals(childScope, cur))
                        continue;

                    if (GetSymbol(assoc) is { } sym)
                    {
                        if (sym is ExtensionSymbol or TypeSymbol or NestedTypeSymbol)
                            return assoc;
                        if (sym is MethodSymbol m && m.Parent is { } mp)
                            return mp;
                        if (sym is PropertySymbol p && p.Parent is { } pp)
                            return pp;
                    }
                }
            }

            foreach (var ext in ExtensionSymbols)
            {
                if (ReferenceEquals(ext.Scope, cur))
                    return GetHandle(ext);
            }
        }

        return null;
    }

    internal ConformanceLookupResult ResolveInterfaceConformance(
        TypeRef sourceType,
        TypeRef expectedInterfaceType,
        Token? explicitModuleQualifier,
        Scope? scope,
        SymbolHandle containingFunction)
    {
        var ifaceUnwrapped = UnwrapInterfaceRef(expectedInterfaceType);
        if (ifaceUnwrapped.Handle is not { } rawIfaceH)
            return new(ConformanceLookupStatus.NotApplicable, null);

        var ifaceH = UnwrapAlias(rawIfaceH);
        var ifaceSym = GetSymbol(ifaceH);
        bool isIfaceTarget = ifaceSym switch
        {
            TypeSymbol ts => ts.TypeKind == TypeKind.Interface,
            NestedTypeSymbol nts => nts.TypeKind == TypeKind.Interface,
            _ => false
        };
        if (!isIfaceTarget)
            return new(ConformanceLookupStatus.NotApplicable, null);

        var enclosingOwner = GetEnclosingOwnerHandle(containingFunction, scope);

        if (TypeRef.TryGetExplicitConformance(sourceType, out var explicitConf))
        {
            if (explicitConf.InterfaceHandle == ifaceH || TypeRef.IsSubtypeOf(explicitConf.InterfaceHandle, ifaceH, this))
            {
                if (!IsConformanceAccessible(explicitConf, enclosingOwner))
                    return new(ConformanceLookupStatus.Inaccessible, explicitConf);
                return new(ConformanceLookupStatus.Found, explicitConf);
            }
        }

        var concreteTarget = UnwrapConcreteTarget(sourceType);
        if (concreteTarget.Handle is not { } rawTargetH)
            return new(ConformanceLookupStatus.NotApplicable, null);

        var targetH = UnwrapAlias(rawTargetH);
        if (targetH == ifaceH)
            return new(ConformanceLookupStatus.NotApplicable, null);

        if (GetSymbol(targetH) is GenericParameterSymbol)
            return new(ConformanceLookupStatus.NotApplicable, null);

        if (GetTypeSymbol(targetH) is { TypeKind: TypeKind.Interface })
            return new(ConformanceLookupStatus.NotApplicable, null);

        // Gather candidates on targetH (and non-interface linearized superclasses if targetH inherits them)
        var candidates = new List<InterfaceConformance>();
        var rawCandidates = new List<InterfaceConformance>();
        CollectConformancesForTypeHierarchy(targetH, rawCandidates);

        var expectedGen = ifaceUnwrapped.SpecialType as GenericSemanticType;
        foreach (var c in rawCandidates)
        {
            if (c.InterfaceHandle != ifaceH && !TypeRef.IsSubtypeOf(c.InterfaceHandle, ifaceH, this))
                continue;

            if (expectedGen is not null && c.InterfaceType.SpecialType is GenericSemanticType confGen && !confGen.Equals(expectedGen))
                continue;

            candidates.Add(c);
        }

        if (candidates.Count == 0)
            return new(ConformanceLookupStatus.NotFound, null);

        // Explicit module qualifier: ModA::Bar / Disamb::Bar / global::Bar
        if (explicitModuleQualifier is { } qualifierTok)
        {
            var targets = ResolveModuleTargets(qualifierTok, scope ?? GlobalScope, out _);
            var targetIds = new HashSet<int>(targets.Select(t => t.ProjectId));
            var modFiltered = candidates.Where(c => targetIds.Contains(c.ModuleProjectId)).ToList();
            if (modFiltered.Count == 0)
                return new(ConformanceLookupStatus.NotFound, null);

            // Group by module and prefer owner private shadow over non-private within each target module
            var accessiblePerModule = new List<InterfaceConformance>();
            InterfaceConformance? inaccessibleInMod = null;
            foreach (var targetMod in targets)
            {
                var inMod = modFiltered.Where(c => c.ModuleProjectId == targetMod.ProjectId).ToList();
                if (inMod.Count == 0) continue;

                var ownerPrivate = inMod.FirstOrDefault(c => c.Accessibility == Accessibility.Private && IsOwnerMatch(enclosingOwner, c));
                if (ownerPrivate is not null)
                {
                    accessiblePerModule.Add(ownerPrivate);
                    continue;
                }

                var acc = inMod.FirstOrDefault(c => IsConformanceAccessible(c, enclosingOwner));
                if (acc is not null)
                    accessiblePerModule.Add(acc);
                else
                    inaccessibleInMod ??= inMod[0];
            }

            if (accessiblePerModule.Count > 1)
                return new(ConformanceLookupStatus.AmbiguousModule, null, accessiblePerModule, qualifierTok.Value);
            if (accessiblePerModule.Count == 1)
                return new(ConformanceLookupStatus.Found, accessiblePerModule[0]);
            return new(ConformanceLookupStatus.Inaccessible, inaccessibleInMod);
        }

        // Tier 1: Strictly body-local private conformance (shadows higher-visibility conformances outside)
        if (enclosingOwner is not null)
        {
            var tier1 = candidates.FirstOrDefault(c =>
                c.ModuleProjectId == ProjectId &&
                c.Accessibility == Accessibility.Private &&
                IsOwnerMatch(enclosingOwner, c));
            if (tier1 is not null)
                return new(ConformanceLookupStatus.Found, tier1);
        }

        // Tier 2: Current module's non-private conformance (Internal / Protected / Public)
        InterfaceConformance? inaccessibleFallback = null;
        foreach (var c in candidates)
        {
            if (c.ModuleProjectId == ProjectId && c.Accessibility != Accessibility.Private)
            {
                if (IsConformanceAccessible(c, enclosingOwner))
                    return new(ConformanceLookupStatus.Found, c);
                inaccessibleFallback ??= c;
            }
        }

        // Tier 3: Imported modules' accessible conformances (Public or inherited Protected)
        var tier3 = new List<InterfaceConformance>();
        foreach (var c in candidates)
        {
            if (c.ModuleProjectId != ProjectId)
            {
                if (IsConformanceAccessible(c, enclosingOwner))
                    tier3.Add(c);
                else
                    inaccessibleFallback ??= c;
            }
        }

        if (tier3.Count == 1)
            return new(ConformanceLookupStatus.Found, tier3[0]);

        if (tier3.Count > 1)
            return new(ConformanceLookupStatus.Ambiguous, null, tier3);

        // Tier 4: An inaccessible conformance existed (e.g. private or foreign internal)
        inaccessibleFallback ??= candidates[0];
        return new(ConformanceLookupStatus.Inaccessible, inaccessibleFallback);
    }

    private void CollectConformancesForTypeHierarchy(SymbolHandle targetH, List<InterfaceConformance> results)
    {
        CollectConformancesForTarget(targetH, results);
        var sym = GetSymbol(targetH);
        IReadOnlyList<SymbolHandle> bases = sym switch
        {
            TypeSymbol ts => ts.LinearizedBases,
            NestedTypeSymbol nts => nts.LinearizedBases,
            _ => []
        };
        foreach (var bh in bases.Skip(1))
        {
            if (GetTypeSymbol(bh) is { TypeKind: not TypeKind.Interface })
                CollectConformancesForTarget(bh, results);
        }
    }

    private bool IsConformanceAccessible(InterfaceConformance c, SymbolHandle? enclosingOwner)
    {
        return c.Accessibility switch
        {
            Accessibility.Public => true,
            Accessibility.Internal => c.ModuleProjectId == ProjectId,
            Accessibility.Private => c.ModuleProjectId == ProjectId && IsOwnerMatch(enclosingOwner, c),
            Accessibility.Protected => IsOwnerWithinOrDerivedFrom(enclosingOwner, c),
            _ => false
        };
    }

    private bool IsOwnerMatch(SymbolHandle? enclosingOwner, InterfaceConformance c)
    {
        if (enclosingOwner is null)
            return false;
        if (enclosingOwner.Value == c.OwnerHandle)
            return true;
        if (c.IsExtension)
            return false;

        // Nested types inside a struct/class share private access with the enclosing type declaration,
        // whereas extensions have distinct OwnerHandles and do NOT match unless c.OwnerHandle == enclosingOwner.
        for (var cur = enclosingOwner.Value; ;)
        {
            if (GetSymbol(cur) is NestedTypeSymbol nts && nts.Parent is { } pH)
            {
                if (pH == c.OwnerHandle)
                    return true;
                cur = pH;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    private bool IsOwnerWithinOrDerivedFrom(SymbolHandle? enclosingOwner, InterfaceConformance c)
    {
        if (IsOwnerMatch(enclosingOwner, c))
            return true;
        if (enclosingOwner is not { } ownerH)
            return false;
        var ownerTargetH = GetSymbol(ownerH) is ExtensionSymbol ext ? ext.TargetTypeHandle : ownerH;
        if (ownerTargetH is not { } srcH || c.TargetType.Handle is not { } confTargetH)
            return false;
        var unwrappedSrc = UnwrapAlias(srcH);
        var unwrappedTarget = UnwrapAlias(confTargetH);
        return unwrappedSrc == unwrappedTarget || TypeRef.IsSubtypeOf(unwrappedSrc, unwrappedTarget, this);
    }

    private static TypeRef UnwrapInterfaceRef(TypeRef t)
    {
        while (true)
        {
            if (t.SpecialType is DynSemanticType d) { t = d.TargetType; continue; }
            if (t.SpecialType is AliasSemanticType a) { t = a.TargetType; continue; }
            if (t.SpecialType is ReferenceSemanticType r) { t = r.ElementType; continue; }
            if (t.SpecialType is PointerSemanticType p) { t = p.ElementType; continue; }
            if (t.SpecialType is ConformanceSemanticType c) { t = c.InterfaceType; continue; }
            return t;
        }
    }

    private static TypeRef UnwrapConcreteTarget(TypeRef t)
    {
        while (true)
        {
            if (t.SpecialType is ConformanceSemanticType c) { t = c.TargetType; continue; }
            if (t.SpecialType is AliasSemanticType a) { t = a.TargetType; continue; }
            if (t.SpecialType is ReferenceSemanticType r) { t = r.ElementType; continue; }
            if (t.SpecialType is PointerSemanticType p) { t = p.ElementType; continue; }
            return t;
        }
    }

    internal static Token? ExtractModuleQualifier(TypeSyntax syntax) => syntax switch
    {
        ScopeQualifiedType sq => sq.Qualifier,
        ModifiedType m => ExtractModuleQualifier(m.Type),
        PrefixModifiedType pm => ExtractModuleQualifier(pm.Type),
        _ => null
    };

    internal void TryPrebindConformanceInterface(TypeSyntax typeSyntax, Scope scope)
    {
        var inner = typeSyntax;
        while (inner is ModifiedType m) inner = m.Type;
        while (inner is PrefixModifiedType pm) inner = pm.Type;
        if (inner is not ScopeQualifiedType sq)
            return;

        if (ResolvedTree.TryGetReference(sq, out _))
            return;

        var memberPath = GetSymbolName(sq.MemberType);
        var strictSyms = ResolveScopedSymbols(
            sq.Qualifier,
            memberPath,
            scope,
            typesOnly: true,
            sq.GetSpan() ?? default,
            sq.GetSource(),
            reportAmbiguity: false,
            out _);

        if (strictSyms.Count > 0)
            return;

        var targets = ResolveModuleTargets(sq.Qualifier, scope, out bool isGlobalSelf);
        if (isGlobalSelf || targets.Count == 0)
            return;

        SymbolHandle? resolvedHandle = null;
        foreach (var targetMod in targets)
        {
            var found = targetMod.GlobalScope.ResolveType(memberPath);
            if (found.Count == 0)
                continue;

            var h = GetHandle(found[0]);
            if (resolvedHandle is null)
                resolvedHandle = h;
            else if (resolvedHandle.Value != h)
                return;
        }

        if (resolvedHandle is not { } symH)
            return;

        ResolvedTree.AddReference(sq, symH);
        ResolvedTree.AddReference(sq.MemberType, symH);
        if (sq.MemberType is QualifiedType qt)
            ResolvedTree.AddReference(qt.Right, symH);
        for (TypeSyntax? cur = sq.MemberType; cur != null;)
        {
            if (cur is GenericType gt)
            {
                ResolvedTree.AddReference(gt, symH);
                break;
            }
            cur = cur is QualifiedType q ? q.Right : null;
        }
    }

    internal TypeRef ResolveConformanceQualifiedType(
        ConformanceQualifiedType confQualified,
        Scope scope,
        SymbolHandle containingFunction,
        Func<TypeSyntax, Scope, TypeRef> resolveInterfaceType,
        Func<TypeSyntax, Scope, TypeRef> resolveTargetType,
        bool reportDiagnostics)
    {
        TryPrebindConformanceInterface(confQualified.InterfaceType, scope);
        var ifaceType = resolveInterfaceType(confQualified.InterfaceType, scope);
        var targetType = resolveTargetType(confQualified.TargetType, scope);

        if (targetType.IsError || ifaceType.IsError || targetType.IsUnresolved || ifaceType.IsUnresolved)
            return targetType;

        var qualifier = ExtractModuleQualifier(confQualified.InterfaceType);
        var lookup = ResolveInterfaceConformance(targetType, ifaceType, qualifier, scope, containingFunction);

        switch (lookup.Status)
        {
            case ConformanceLookupStatus.Found:
                ResolvedTree.AddConformance(confQualified, lookup.Conformance!);
                return TypeRef.Conformance(targetType, lookup.Conformance!);

            case ConformanceLookupStatus.Inaccessible:
                if (reportDiagnostics)
                {
                    Diagnostics.ReportInaccessibleInterfaceConformance(
                        confQualified.GetSpan() ?? default,
                        targetType.ToDisplayString(this),
                        lookup.Conformance!.InterfaceType.ToDisplayString(this),
                        lookup.Conformance.Accessibility.ToString().ToLowerInvariant(),
                        confQualified.GetSource());
                }
                return targetType;

            case ConformanceLookupStatus.Ambiguous:
                if (reportDiagnostics)
                {
                    var mods = lookup.AmbiguousCandidates!.Select(c => c.ModuleName).Distinct().ToList();
                    Diagnostics.ReportAmbiguousInterfaceConformance(
                        confQualified.GetSpan() ?? default,
                        targetType.ToDisplayString(this),
                        ifaceType.ToDisplayString(this),
                        mods,
                        confQualified.GetSource());
                }
                return targetType;

            case ConformanceLookupStatus.AmbiguousModule:
                if (reportDiagnostics)
                {
                    Diagnostics.ReportAmbiguousModuleMember(
                        confQualified.GetSpan() ?? default,
                        lookup.AmbiguousModuleName!,
                        ifaceType.ToDisplayString(this),
                        confQualified.GetSource());
                }
                return targetType;

            default:
                return targetType;
        }
    }

    internal void ValidateGenericTypeArgumentConformances(
        SymbolHandle targetHandle,
        IReadOnlyList<TypeSyntax> argSyntaxes,
        IReadOnlyList<TypeRef> resolvedArgs,
        Scope scope,
        SymbolHandle containingFunction,
        bool reportDiagnostics)
    {
        IReadOnlyList<SymbolHandle> genericParams = GetSymbol(targetHandle) switch
        {
            TypeSymbol ts => ts.GenericParameters,
            NestedTypeSymbol nts => nts.GenericParameters,
            FunctionSymbol fn => fn.GenericParameters,
            MethodSymbol ms => ms.GenericParameters,
            AliasSymbol al => al.GenericParameters,
            _ => []
        };

        int count = Math.Min(genericParams.Count, Math.Min(argSyntaxes.Count, resolvedArgs.Count));
        for (int i = 0; i < count; i++)
        {
            var gp = GetGenericParameterSymbol(genericParams[i]);
            if (gp is null || gp.Constraints.Count == 0)
                continue;

            var argType = resolvedArgs[i];
            if (argType.IsError || argType.IsUnresolved)
                continue;

            foreach (var constraint in gp.Constraints)
            {
                if (constraint.IsError || constraint.IsUnresolved)
                    continue;

                var lookup = ResolveInterfaceConformance(argType, constraint, explicitModuleQualifier: null, scope, containingFunction);
                if (lookup.Status == ConformanceLookupStatus.Found)
                {
                    ResolvedTree.AddConformance(argSyntaxes[i], lookup.Conformance!);
                }
                else if (reportDiagnostics && lookup.Status == ConformanceLookupStatus.Inaccessible)
                {
                    Diagnostics.ReportInaccessibleInterfaceConformance(
                        argSyntaxes[i].GetSpan() ?? default,
                        TypeRef.UnwrapConformance(argType).ToDisplayString(this),
                        lookup.Conformance!.InterfaceType.ToDisplayString(this),
                        lookup.Conformance.Accessibility.ToString().ToLowerInvariant(),
                        argSyntaxes[i].GetSource());
                }
                else if (reportDiagnostics && lookup.Status == ConformanceLookupStatus.Ambiguous)
                {
                    var mods = lookup.AmbiguousCandidates!.Select(c => c.ModuleName).Distinct().ToList();
                    Diagnostics.ReportAmbiguousInterfaceConformance(
                        argSyntaxes[i].GetSpan() ?? default,
                        TypeRef.UnwrapConformance(argType).ToDisplayString(this),
                        constraint.ToDisplayString(this),
                        mods,
                        argSyntaxes[i].GetSource());
                }
            }
        }
    }

    internal static bool HasOwnedSymbolsInSubtree(NamespaceTrieNode node, int projectId)
    {
        foreach (var sym in node.Symbols)
        {
            if (sym.ProjectId == projectId)
                return true;
        }
        foreach (var child in node.Next.Values)
        {
            if (HasOwnedSymbolsInSubtree(child, projectId))
                return true;
        }
        return false;
    }

    private static List<Symbol> ResolveInModuleRoot(ResolutionContext targetMod, SymbolName path, bool typesOnly)
    {
        int projectId = targetMod.ProjectId;

        // 1. Check module's own root GlobalScope (filtered strictly to targetMod.ProjectId so external imports are bypassed!)
        var rootMatches = new List<Symbol>();
        foreach (var s in targetMod.GlobalScope.GetLocalSymbols(path[0]))
        {
            if (s.ProjectId == projectId && (path.Count > 1 || !typesOnly || IsTypeSymbol(s)))
                rootMatches.Add(s);
        }

        if (rootMatches.Count > 0)
        {
            if (path.Count == 1)
                return rootMatches;

            return TraverseNestedTypesInModule(targetMod, rootMatches[0], path, 1, typesOnly);
        }

        // 2. Traverse module's own GlobalNamespace filtered strictly to targetMod.ProjectId
        var nsNode = targetMod.GlobalNamespace;
        for (int index = 0; index < path.Count; index++)
        {
            var part = path[index];
            if (index < path.Count - 1 && nsNode.Next.TryGetValue(part, out var nextNs))
            {
                nsNode = nextNs;
                continue;
            }

            var nsMatches = new List<Symbol>();
            foreach (var s in nsNode.GetSymbols(part))
            {
                if (s.ProjectId == projectId && (index < path.Count - 1 || !typesOnly || IsTypeSymbol(s)))
                    nsMatches.Add(s);
            }

            if (nsMatches.Count == 0)
                return [];

            if (index == path.Count - 1)
                return nsMatches;

            return TraverseNestedTypesInModule(targetMod, nsMatches[0], path, index + 1, typesOnly);
        }

        return [];
    }

    private static List<Symbol> TraverseNestedTypesInModule(
        ResolutionContext targetMod,
        Symbol current,
        SymbolName path,
        int startIndex,
        bool typesOnly)
    {
        var matches = new List<Symbol> { current };
        int projectId = targetMod.ProjectId;
        var currScope = targetMod.GlobalScope;

        for (int i = startIndex; i < path.Count; i++)
        {
            var sym = matches[0];
            var handle = GetHandle(sym);
            var child = currScope.GetChildScope(handle) ?? sym.EnclosingScope?.GetChildScope(handle);
            if (child is null)
                return [];

            currScope = child;
            matches.Clear();
            bool isLast = i == path.Count - 1;
            foreach (var s in currScope.GetLocalSymbols(path[i]))
            {
                if (s.ProjectId == projectId && (!isLast || !typesOnly || IsTypeSymbol(s)))
                    matches.Add(s);
            }
            if (matches.Count == 0)
                return [];
        }

        return matches;
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

    public ConstantSymbol CreateConstantSymbol(Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, ConstantDeclaration? syntax)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : constantID++;
        var symbol = TrackSymbol(new ConstantSymbol(id, enclosingScope, name, containingNamespace, syntax));

        if (isSink)
        {
            SinkSymbols.ConstantSymbols.Add(symbol);
            sinkSymbolsByHandle[GetHandle(symbol)] = symbol;
        }
        else
            ConstantSymbols.Add(symbol);

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

    public GenericParameterSymbol CreateGenericParameterSymbol(Scope enclosingScope, SymbolPart name, Symbol genericSymbol, GenericParameterKind parameterKind, bool isVariadic, GenericParameterSyntax? syntax = null)
    {
        bool isSink = name.Text == "_" || enclosingScope.IsSink;
        SymbolID id = isSink ? new SymbolID(sinkID--) : genericParameterID++;
        var symbol = TrackSymbol(new GenericParameterSymbol(id, enclosingScope, name, genericSymbol, parameterKind, isVariadic, syntax));

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

    private int extensionID;

    public ExtensionSymbol CreateExtensionSymbol(Scope enclosingScope, Scope extensionScope, SymbolPart name, TypeSyntax targetTypeSyntax, ExtensionDeclaration? syntax)
    {
        var id = new SymbolID(extensionID++);
        var symbol = new ExtensionSymbol(id, enclosingScope, extensionScope, name, targetTypeSyntax, syntax);
        TrackSymbol(symbol);
        ExtensionSymbols.Add(symbol);
        Register(enclosingScope, symbol);
        return symbol;
    }

    public ExtensionSymbol? GetExtensionSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Extension)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetExtensionSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < ExtensionSymbols.Count)
            return ExtensionSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is ExtensionSymbol ext)
            return ext;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetExtensionSymbol(handle) is { } refSym)
                    return refSym;
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

    public ConstantSymbol? GetConstantSymbol(SymbolHandle handle)
    {
        if (handle.Kind != SymbolKind.Constant)
            return null;
        if (handle.ProjectId != 0 && handle.ProjectId != ProjectId)
        {
            if (TryGetReferencedContext(handle.ProjectId, out var refContext))
                return refContext.GetConstantSymbol(handle);
            return null;
        }

        if (handle.ID.Value >= 0 && handle.ID.Value < ConstantSymbols.Count)
            return ConstantSymbols[handle.ID];
        if (sinkSymbolsByHandle.TryGetValue(handle, out var s) && s is ConstantSymbol c)
            return c;

        if (handle.ProjectId == 0)
        {
            foreach (var refProj in ReferencedProjects)
            {
                if (refProj.GetConstantSymbol(handle) is { } refConst)
                    return refConst;
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
            SymbolKind.Constant => GetConstantSymbol(handle),
            SymbolKind.Variable => sinkSymbolsByHandle.TryGetValue(handle, out var sl) ? sl : null,
            SymbolKind.Field => GetFieldSymbol(handle),
            SymbolKind.Property => GetPropertySymbol(handle),
            SymbolKind.Alias => GetAliasSymbol(handle),
            SymbolKind.GenericParameter => GetGenericParameterSymbol(handle),
            SymbolKind.Parameter => GetParameterSymbol(handle),
            SymbolKind.Attribute => GetAttributeSymbol(handle),
            SymbolKind.NestedAttribute => GetNestedAttributeSymbol(handle),
            SymbolKind.EnumVariant => GetEnumVariantSymbol(handle),
            SymbolKind.Extension => GetExtensionSymbol(handle),
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
        while (curr != null && curr != globalNs && curr.Parent != null)
        {
            var text = curr.Name.Text;
            if (string.IsNullOrEmpty(text))
                break;
            parts.Add(text);
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
        ScopeQualifiedName scopeQualifiedName => GetSymbolName(scopeQualifiedName.MemberName, hygiene),
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

            case ScopeQualifiedType scopeQualified:
                AddTypeNameParts(scopeQualified.MemberType, parts);
                break;

            case ConformanceQualifiedType confQualified:
                AddTypeNameParts(confQualified.TargetType, parts);
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
