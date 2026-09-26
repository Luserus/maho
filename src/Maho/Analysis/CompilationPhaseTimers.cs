using System;

namespace Maho;

/// <summary>
/// Measures execution duration across distinct compiler pipeline phases.
/// </summary>
public sealed record CompilationPhaseTimers(
    TimeSpan Syntax,
    TimeSpan SemanticAnalysis,
    TimeSpan Lowering,
    TimeSpan? Lexing = null,
    TimeSpan? Parsing = null)
{
    /// <summary>
    /// Combined duration of core compilation across all phases.
    /// </summary>
    public TimeSpan Total => Syntax + SemanticAnalysis + Lowering;

    public static CompilationPhaseTimers Zero { get; } = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
}
