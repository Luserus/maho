namespace Maho.Resolution;

/// <summary>
/// Base contract for one semantic resolution stage. Each pass owns its full control flow, including
/// any project-wide setup/finalization and any unit-level parallelization it wants to perform.
/// </summary>
internal abstract class ResolutionPass
{
    /// <summary>
    /// Human-readable name for logging/debugging. By default the runtime type name is good enough,
    /// but passes can override it if they want a more stable or friendlier label.
    /// </summary>
    public virtual string Name => GetType().Name;
    /// <summary>
    /// Runs the pass against the shared project context. Passes can do all their work sequentially,
    /// fan out unit-local work in parallel, or combine both inside this one entrypoint.
    /// </summary>
    public abstract void Execute(ResolutionCoordinatorContext context);
}
