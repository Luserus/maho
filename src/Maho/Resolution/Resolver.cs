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

    /// <summary> Resolves semantic state for one compilation unit via the project-level coordinator. </summary>
    public ResolutionResult Resolve(CompilationUnit root, DiagnosticsManager diagnostics) =>
        ResolveProject(ResolutionProject.CreateSingleUnit(root), diagnostics).Units[0];

    /// <summary> Resolves semantic state for every compilation unit in a project. </summary>
    public ResolutionProjectResult ResolveProject(ResolutionProject project, DiagnosticsManager diagnostics) =>
        Coordinator.Resolve(project, diagnostics);
}
