using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Runs the configured semantic passes for one parsed compilation unit or a whole project. </summary>
internal sealed class Resolver
{
    private static readonly IReadOnlyList<ResolutionPass> Passes =
    [
        new SymbolDiscoveryPass()
    ];

    private static readonly ResolutionCoordinator Coordinator = new(Passes);
    private readonly DiagnosticsManager diagnostics;

    public Resolver(DiagnosticsManager diagnostics) => this.diagnostics = diagnostics;

    /// <summary> Resolves semantic state for one compilation unit via the project-level coordinator. </summary>
    public ResolutionResult Resolve(CompilationUnit root) => Resolve(SyntaxTree.CreateSingleRoot(root)).Units[0];

    /// <summary> Resolves semantic state for every compilation unit captured by one syntax tree. </summary>
    public ResolutionProjectResult Resolve(SyntaxTree syntaxTree) => ResolveProject(new ResolutionProject(syntaxTree));

    /// <summary> Resolves semantic state for every compilation unit in a project. </summary>
    public ResolutionProjectResult ResolveProject(ResolutionProject project) => Coordinator.Resolve(project, diagnostics);
}
