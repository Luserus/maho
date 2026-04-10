namespace Maho.Resolution;

/// <summary>
/// Controls how the coordinator schedules per-unit work for one semantic pass.
/// </summary>
internal enum ResolutionExecutionMode : byte
{
    /// <summary>
    /// Runs units one at a time. Use this when unit traversal mutates shared project state
    /// directly, or when ordering between units matters inside the pass.
    /// </summary>
    Sequential,
    /// <summary>
    /// Runs units in parallel when each unit only reads shared state and writes to unit-local state.
    /// No merge phase is required because the pass is effectively embarrassingly parallel.
    /// </summary>
    ParallelUnitLocal,
    /// <summary>
    /// Runs a parallel unit-local build phase first, then attaches those per-unit results
    /// sequentially into shared project state. This is the safest model for declaration-building
    /// passes that need real symbols or scopes before merge.
    /// </summary>
    ParallelCollectThenMerge
}
