using Maho.Syntax;

namespace Maho.Resolution.ControlFlow;

internal enum BlockTerminatorKind
{
    Fallthrough,
    Branch,
    ConditionalBranch,
    Return,
    Unreachable
}

internal sealed class BlockTerminator
{
    public BlockTerminatorKind Kind { get; }
    public BasicBlock? Target { get; }
    public BasicBlock? FalseTarget { get; }
    public Expression? Condition { get; }
    public Expression? ReturnExpression { get; }

    public static BlockTerminator Fallthrough(BasicBlock target) =>
        new(BlockTerminatorKind.Fallthrough, target);

    public static BlockTerminator Branch(BasicBlock target) =>
        new(BlockTerminatorKind.Branch, target);

    public static BlockTerminator Conditional(Expression condition, BasicBlock trueTarget, BasicBlock falseTarget) =>
        new(BlockTerminatorKind.ConditionalBranch, trueTarget, falseTarget, condition);

    public static BlockTerminator Return(Expression? expr = null) =>
        new(BlockTerminatorKind.Return, returnExpr: expr);

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

