using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Shared mutable state for one coordinated project-level resolution run. </summary>
internal sealed class ResolutionCoordinatorContext
{
    /// <summary>
    /// One unit context per compilation unit in the project. These objects hold file-local maps but
    /// all point back to this shared project context.
    /// </summary>
    private readonly ResolutionContext[] units;
    /// <summary>
    /// Maps symbols that own lexical containers to the scopes they own. This is project-wide
    /// because symbols are project-wide identities even when their syntax came from one file.
    /// </summary>
    private readonly Dictionary<Symbol, Scope> symbolScopes = new(ReferenceEqualityComparer.Instance);
    /// <summary> Records intrinsic attribute declaration symbols by their simple compiler-known names. </summary>
    private readonly Dictionary<string, List<TypeSymbol>> intrinsicAttributeDefinitions = new(System.StringComparer.Ordinal);

    /// <summary> Friendly project identity carried through result objects and diagnostics. </summary>
    public string ProjectName { get; }
    /// <summary> Shared diagnostic sink for every pass and every unit in this resolution run. </summary>
    public DiagnosticsManager Diagnostics { get; }
    /// <summary> Root-of-roots syntax node for the whole parsed project. </summary>
    public SyntaxTree Root { get; }
    /// <summary> Unnamed root namespace symbol that contains every top-level declaration. </summary>
    public NamespaceSymbol GlobalNamespace { get; }
    /// <summary> Lexical scope associated with the project root / global namespace. </summary>
    public Scope GlobalScope { get; }
    /// <summary> Semantic surfaces from referenced projects that later passes may consult. </summary>
    public ResolutionProjectReference[] References { get; }
    /// <summary> File-local contexts participating in this coordinated resolution run. </summary>
    public ResolutionContext[] Units => units;
    /// <summary> Read-only view of the project-wide symbol -> owned scope map. </summary>
    public IReadOnlyDictionary<Symbol, Scope> SymbolScopes => symbolScopes;
    /// <summary> Read-only view of intrinsic attribute declarations collected during semantic analysis. </summary>
    public IReadOnlyDictionary<string, List<TypeSymbol>> IntrinsicAttributeDefinitions => intrinsicAttributeDefinitions;

    /// <summary>
    /// Creates all shared project state and one unit context per compilation unit. The coordinator
    /// constructs this once and reuses it across every semantic pass.
    /// </summary>
    public ResolutionCoordinatorContext(ResolutionProject project, DiagnosticsManager diagnostics)
    {
        ProjectName = project.SyntaxTree.Name;
        Diagnostics = diagnostics;
        References = project.References;
        Root = project.SyntaxTree;
        // The global namespace and global scope are synthetic semantic concepts, but the syntax tree
        // itself serves as the shared boundary node for both.
        GlobalNamespace = new NamespaceSymbol(SymbolName.Empty, parentSymbol: null, Root);
        GlobalScope = new Scope(parent: null, boundary: Root, ownerSymbol: GlobalNamespace);
        units = new ResolutionContext[Root.Roots.Length];

        symbolScopes.Add(GlobalNamespace, GlobalScope);

        for (int i = 0; i < Root.Roots.Length; i++)
            units[i] = new ResolutionContext(Root.Roots[i], this);
    }

    /// <summary>
    /// Attempts to resolve the lexical scope owned by a symbol. Only symbols that introduce a
    /// container, such as namespaces, types, and functions, appear in this map.
    /// </summary>
    public bool TryResolveSymbolScope(Symbol symbol, out Scope? scope) => symbolScopes.TryGetValue(symbol, out scope);

    /// <summary>
    /// Records the scope owned by a symbol if that relationship has not been registered yet.
    /// Passes call this through unit contexts so scope ownership stays centralized here.
    /// </summary>
    public void ResolveSymbolScope(Symbol symbol, Scope scope) => symbolScopes.TryAdd(symbol, scope);

    /// <summary> Records one intrinsic attribute declaration under its compiler-known simple name. </summary>
    public void RecordIntrinsicAttributeDefinition(string name, TypeSymbol symbol)
    {
        if (!intrinsicAttributeDefinitions.TryGetValue(name, out List<TypeSymbol>? symbols))
        {
            symbols = [];
            intrinsicAttributeDefinitions.Add(name, symbols);
        }

        symbols.Add(symbol);
    }

    /// <summary> Freezes the mutable project context into stable result objects once the full pass pipeline has completed. </summary>
    public ResolutionProjectResult ToResult()
    {
        ResolutionResult[] unitResults = new ResolutionResult[units.Length];
        Dictionary<string, TypeSymbol[]> frozenIntrinsicAttributes = new(intrinsicAttributeDefinitions.Count, System.StringComparer.Ordinal);

        for (int i = 0; i < units.Length; i++)
            unitResults[i] = units[i].ToResult();

        foreach ((string name, List<TypeSymbol> symbols) in intrinsicAttributeDefinitions)
            frozenIntrinsicAttributes.Add(name, [.. symbols]);

        return new ResolutionProjectResult(ProjectName, GlobalNamespace, GlobalScope, unitResults, References, symbolScopes, frozenIntrinsicAttributes);
    }
}
