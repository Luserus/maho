using Maho.Syntax;

namespace Maho.Resolution.ControlFlow;

/// <summary>
/// Encapsulates the terminating transfer of control at the end of a <see cref="BasicBlock"/>.
/// </summary>
internal sealed class BlockTerminator
{
    /// <summary>The classification kind of this terminator.</summary>
    public BlockTerminatorKind Kind { get; }

    /// <summary>The primary jump target, true-branch target, or fallthrough target.</summary>
    public BasicBlock? Target { get; }

    /// <summary>The false-branch target when <see cref="Kind"/> is <see cref="BlockTerminatorKind.ConditionalBranch"/>.</summary>
    public BasicBlock? FalseTarget { get; }

    /// <summary>The branch condition expression when <see cref="Kind"/> is <see cref="BlockTerminatorKind.ConditionalBranch"/>.</summary>
    public Expression? Condition { get; }

    /// <summary>The returned expression when <see cref="Kind"/> is <see cref="BlockTerminatorKind.Return"/>, or <c>null</c> for bare returns.</summary>
    public Expression? ReturnExpression { get; }

    /// <summary>Creates an unconditional fallthrough terminator targeting <paramref name="target"/>.</summary>
    public static BlockTerminator Fallthrough(BasicBlock target) =>
        new(BlockTerminatorKind.Fallthrough, target);

    /// <summary>Creates an unconditional branch terminator targeting <paramref name="target"/>.</summary>
    public static BlockTerminator Branch(BasicBlock target) =>
        new(BlockTerminatorKind.Branch, target);

    /// <summary>Creates a two-way conditional branch terminator evaluating <paramref name="condition"/>.</summary>
    public static BlockTerminator Conditional(Expression condition, BasicBlock trueTarget, BasicBlock falseTarget) =>
        new(BlockTerminatorKind.ConditionalBranch, trueTarget, falseTarget, condition);

    /// <summary>Creates a return terminator exiting the callable with an optional return expression.</summary>
    public static BlockTerminator Return(Expression? expr = null) =>
        new(BlockTerminatorKind.Return, returnExpr: expr);

    /// <summary>Creates an unreachable terminator indicating execution cannot continue past this point.</summary>
    public static BlockTerminator Unreachable() =>
        new(BlockTerminatorKind.Unreachable);

    private BlockTerminator(
        BlockTerminatorKind kind,
        BasicBlock? target = null,
        BasicBlock? falseTarget = null,
        Expression? condition = null,
        Expression? returnExpr = null)
    {
        Kind = kind;
        Target = target;
        FalseTarget = falseTarget;
        Condition = condition;
        ReturnExpression = returnExpr;
    }
}
