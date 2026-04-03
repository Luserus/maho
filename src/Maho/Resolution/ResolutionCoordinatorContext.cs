using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Symbols;

namespace Maho.Resolution;

/// <summary> Shared mutable state for one coordinated project-level resolution run. </summary>
internal sealed class ResolutionCoordinatorContext
{
    private readonly List<ResolutionContext> units = [];
    private readonly Dictionary<Symbol, Scope> symbolScopes = new(ReferenceEqualityComparer.Instance);

    public string ProjectName { get; }
    public DiagnosticsManager Diagnostics { get; }
    public ProjectRootSyntax ProjectRoot { get; }
    public NamespaceSymbol GlobalNamespace { get; }
    public Scope GlobalScope { get; }
    public IReadOnlyList<ResolutionProjectReference> References { get; }
    public IReadOnlyList<ResolutionContext> Units => units;
    public IReadOnlyDictionary<Symbol, Scope> SymbolScopes => symbolScopes;

    public ResolutionCoordinatorContext(ResolutionProject project, DiagnosticsManager diagnostics)
    {
        ProjectName = project.Name;
        Diagnostics = diagnostics;
        References = project.References;
        ProjectRoot = new ProjectRootSyntax(project.Name);
        GlobalNamespace = new NamespaceSymbol(string.Empty, parentSymbol: null, ProjectRoot);
        GlobalScope = new Scope(parent: null, boundary: ProjectRoot, ownerSymbol: GlobalNamespace);

        symbolScopes.Add(GlobalNamespace, GlobalScope);

        for (int i = 0; i < project.Units.Count; i++)
            units.Add(new ResolutionContext(project.Units[i], this));
    }

    public bool TryResolveSymbolScope(Symbol symbol, out Scope? scope) => symbolScopes.TryGetValue(symbol, out scope);

    public void ResolveSymbolScope(Symbol symbol, Scope scope) => symbolScopes.TryAdd(symbol, scope);

    public ResolutionProjectResult ToResult()
    {
        ResolutionResult[] unitResults = new ResolutionResult[units.Count];

        for (int i = 0; i < units.Count; i++)
            unitResults[i] = units[i].ToResult();

        return new ResolutionProjectResult(ProjectName, GlobalNamespace, GlobalScope, unitResults, References, symbolScopes);
    }
}
