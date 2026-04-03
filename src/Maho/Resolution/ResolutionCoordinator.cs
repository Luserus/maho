using System.Collections.Generic;
using Maho.Diagnostics;

namespace Maho.Resolution;

/// <summary>
/// Coordinates semantic passes across every compilation unit in a project and exposes project-wide
/// hooks before and after unit-level work.
/// </summary>
internal sealed class ResolutionCoordinator
{
    private readonly IReadOnlyList<ResolutionPass> passes;

    public ResolutionCoordinator(IReadOnlyList<ResolutionPass> passes) => this.passes = passes;

    public ResolutionProjectResult Resolve(ResolutionProject project, DiagnosticsManager diagnostics)
    {
        ResolutionCoordinatorContext context = new(project, diagnostics);

        for (int i = 0; i < passes.Count; i++)
        {
            ResolutionPass pass = passes[i];
            pass.BeforeProject(context);

            for (int unitIndex = 0; unitIndex < context.Units.Count; unitIndex++)
                pass.ExecuteUnit(context.Units[unitIndex]);

            pass.AfterProject(context);
        }

        return context.ToResult();
    }
}
