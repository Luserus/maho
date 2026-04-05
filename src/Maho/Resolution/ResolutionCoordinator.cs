using System.Threading.Tasks;
using Maho.Diagnostics;

namespace Maho.Resolution;

/// <summary>
/// Coordinates semantic passes across every compilation unit in a project and exposes project-wide
/// hooks before and after unit-level work.
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

        for (int i = 0; i < passes.Length; i++)
        {
            ResolutionPass pass = passes[i];
            pass.BeforeProject(context);

            ExecutePassUnits(pass, context);

            pass.AfterProject(context);
        }

        return context.ToResult();
    }

    /// <summary>
    /// Dispatches one pass according to its declared execution mode. This keeps scheduling policy in
    /// one place instead of scattering parallel/sequential concerns across every pass.
    /// </summary>
    private static void ExecutePassUnits(ResolutionPass pass, ResolutionCoordinatorContext context)
    {
        switch (pass.ExecutionMode)
        {
            case ResolutionExecutionMode.Sequential:
                for (int unitIndex = 0; unitIndex < context.Units.Count; unitIndex++)
                    pass.ExecuteUnit(context.Units[unitIndex]);
                break;

            case ResolutionExecutionMode.ParallelUnitLocal:
                // Unit contexts are isolated enough for this pass, so the coordinator can fan them
                // out directly without any merge phase.
                Parallel.For(0, context.Units.Count, unitIndex => pass.ExecuteUnit(context.Units[unitIndex]));
                break;

            case ResolutionExecutionMode.ParallelCollectThenMerge:
            {
                // Collect first so units never mutate shared project state concurrently. The pass
                // can then merge those unit-local facts in a deterministic single-threaded phase.
                ResolutionPassUnitResult?[] results = new ResolutionPassUnitResult?[context.Units.Count];
                Parallel.For(0, context.Units.Count, unitIndex => results[unitIndex] = pass.CollectUnit(context.Units[unitIndex]));

                for (int unitIndex = 0; unitIndex < context.Units.Count; unitIndex++)
                    pass.MergeUnit(context, context.Units[unitIndex], results[unitIndex]);

                break;
            }

            default:
                throw new System.InvalidOperationException($"Unhandled execution mode '{pass.ExecutionMode}'.");
        }
    }
}
