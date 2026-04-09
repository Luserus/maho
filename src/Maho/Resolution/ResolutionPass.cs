namespace Maho.Resolution;

/// <summary>
/// Base contract for one semantic resolution stage. A pass can choose to do project-wide setup,
/// per-unit work, project-wide finalization, or any combination of the three.
/// </summary>
internal abstract class ResolutionPass
{
    /// <summary>
    /// Human-readable name for logging/debugging. By default the runtime type name is good enough,
    /// but passes can override it if they want a more stable or friendlier label.
    /// </summary>
    public virtual string Name => GetType().Name;
    /// <summary>
    /// Declares how the coordinator is allowed to schedule unit work for this pass. The execution
    /// mode is part of the pass contract because only the pass author knows whether shared mutation is safe.
    /// </summary>
    public virtual ResolutionExecutionMode ExecutionMode => ResolutionExecutionMode.Sequential;

    /// <summary>
    /// Performs project-wide setup before any compilation unit for this pass is touched. Typical
    /// uses are initializing pass-owned caches or freezing earlier shared state for read-only use.
    /// </summary>
    public virtual void BeforeProject(ResolutionCoordinatorContext context) { }

    /// <summary>
    /// Executes per-unit work directly. Sequential passes and fully unit-local parallel passes use
    /// this as their main body.
    /// </summary>
    public virtual void ExecuteUnit(ResolutionContext context) { }

    /// <summary>
    /// Collects per-unit facts for collect-then-merge passes. The default implementation forwards
    /// to <see cref="ExecuteUnit"/> so simple passes only need one override.
    /// </summary>
    public virtual ResolutionPassUnitResult? CollectUnit(ResolutionContext context)
    {
        ExecuteUnit(context);
        return null;
    }

    /// <summary>
    /// Merges one unit's collected facts back into shared project state. This only runs for
    /// <see cref="ResolutionExecutionMode.ParallelCollectThenMerge"/> passes.
    /// </summary>
    public virtual void MergeUnit(ResolutionCoordinatorContext projectContext, ResolutionContext unitContext, ResolutionPassUnitResult? result) { }

    /// <summary>
    /// Performs project-wide finalization after all units have been processed for this pass. This
    /// is the right place for pass-wide validation or for sealing mutable pass-owned state.
    /// </summary>
    public virtual void AfterProject(ResolutionCoordinatorContext context) { }
}
