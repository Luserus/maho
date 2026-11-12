namespace Maho.Resolution.ControlFlow;

/// <summary>
/// Classifies the mechanism by which control flow transitions out of a <see cref="BasicBlock"/>.
/// </summary>
internal enum BlockTerminatorKind
{
    /// <summary>Unconditional sequential fallthrough to an adjacent basic block.</summary>
    Fallthrough,

    /// <summary>Unconditional branch/jump to a specific target basic block (e.g. goto, break, continue).</summary>
    Branch,

    /// <summary>Two-way conditional branch based on an expression evaluating to true or false.</summary>
    ConditionalBranch,

    /// <summary>Function or method exit via a return statement, optionally returning an expression.</summary>
    Return,

    /// <summary>Terminal unreachable state (e.g. following an unconditional divergence or throw).</summary>
    Unreachable
}
