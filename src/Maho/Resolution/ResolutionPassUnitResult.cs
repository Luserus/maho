namespace Maho.Resolution;

/// <summary>
/// Base type for per-unit facts produced by collect-then-merge semantic passes. A pass returns one
/// instance per compilation unit from <see cref="ResolutionPass.CollectUnit"/>, then the
/// coordinator feeds those instances back into <see cref="ResolutionPass.MergeUnit"/> one by one.
/// </summary>
internal abstract class ResolutionPassUnitResult;
