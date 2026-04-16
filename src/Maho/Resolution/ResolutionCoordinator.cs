using Maho.Diagnostics;

namespace Maho.Resolution;

/// <summary>
/// Coordinates semantic passes across every compilation unit in a project.
/// </summary>
internal sealed class ResolutionCoordinator
{
    /// <summary>
    /// Ordered semantic stages to execute. The coordinator enforces full pass barriers, so pass N+1
    /// never starts until pass N has finished across the whole project.
    /// </summary>
    private readonly ResolutionPass[] passes;

    /// <summary> Creates a coordinator for one fixed pass pipeline. </summary>
    public ResolutionCoordinator(ResolutionPass[] passes) => this.passes = passes;

    /// <summary>
    /// Runs the full semantic pipeline for one project. This method owns pass ordering and the
    /// lifetime of the shared project context.
    /// </summary>
    public ResolutionProjectResult Resolve(ResolutionProject project, DiagnosticsManager diagnostics)
    {
        ResolutionCoordinatorContext context = new(project, diagnostics);

        foreach (ResolutionPass pass in passes)
            pass.Execute(context);

        return context.ToResult();
    }
}
