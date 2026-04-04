using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Shared mutable state for one coordinated project-level resolution run. </summary>
internal sealed class ResolutionCoordinatorContext
{
    private readonly List<ResolutionContext> units = [];
    private readonly Dictionary<Symbol, Scope> symbolScopes = new(ReferenceEqualityComparer.Instance);

    public string ProjectName { get; }
    public DiagnosticsManager Diagnostics { get; }
    public SyntaxTree Root { get; }
    public NamespaceSymbol GlobalNamespace { get; }
    public Scope GlobalScope { get; }
    public IReadOnlyList<ResolutionProjectReference> References { get; }
    public IReadOnlyList<ResolutionContext> Units => units;
    public IReadOnlyDictionary<Symbol, Scope> SymbolScopes => symbolScopes;

    public ResolutionCoordinatorContext(ResolutionProject project, DiagnosticsManager diagnostics)
    {
        ProjectName = project.SyntaxTree.Name;
        Diagnostics = diagnostics;
        References = project.References;
        Root = project.SyntaxTree;
        GlobalNamespace = new NamespaceSymbol(SymbolName.Empty, parentSymbol: null, Root);
        GlobalScope = new Scope(parent: null, boundary: Root, ownerSymbol: GlobalNamespace);

        symbolScopes.Add(GlobalNamespace, GlobalScope);

        for (int i = 0; i < Root.Roots.Count; i++)
            units.Add(new ResolutionContext(Root.Roots[i], this));
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
