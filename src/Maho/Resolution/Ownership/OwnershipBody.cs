using System;
using System.Collections.Generic;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution.Ownership;

/// <summary>
/// Lowered, storage-oriented representation of one callable body.  It is intentionally separate
/// from <see cref="ResolvedTree"/>: source bindings stay attached to syntax while this graph is
/// free to contain compiler-generated storage and cleanup operations.
/// </summary>
internal sealed class OwnershipBody
{
    public SymbolHandle ContainingSymbol { get; }
    public OwnershipBlock Entry { get; }
    public OwnershipBlock Exit { get; }
    public List<OwnershipBlock> Blocks { get; } = [];
    public IReadOnlyDictionary<Symbol, TypeRef> PlaceTypes => placeTypes;

    private readonly Dictionary<Symbol, TypeRef> placeTypes = [];

    internal OwnershipBody(SymbolHandle containingSymbol, OwnershipBlock entry, OwnershipBlock exit)
    {
        ContainingSymbol = containingSymbol;
        Entry = entry;
        Exit = exit;
    }

    /// <summary>
    /// Registers automatic storage and its resolved type so data-flow and cleanup can classify the
    /// root without returning to syntax.
    /// </summary>
    internal void RegisterPlace(Symbol place, TypeRef type) => placeTypes[place] = type;
}

/// <summary>A basic block containing ordered ownership operations.</summary>
internal sealed class OwnershipBlock
{
    public int Id { get; }
    public List<OwnershipStatement> Statements { get; } = [];
    public List<OwnershipBlock> Predecessors { get; } = [];
    public List<OwnershipBlock> Successors { get; } = [];
    public OwnershipTerminator Terminator { get; set; } = OwnershipTerminator.Unreachable;

    internal OwnershipBlock(int id) => Id = id;

    /// <summary>
    /// Adds a CFG edge while maintaining the predecessor/successor relationship on both blocks.
    /// </summary>
    /// <remarks>Duplicate edges are ignored because analyses use set-like reachability.</remarks>
    internal void AddSuccessor(OwnershipBlock successor)
    {
        if (!Successors.Contains(successor))
            Successors.Add(successor);
        if (!successor.Predecessors.Contains(this))
            successor.Predecessors.Add(this);
    }
}

/// <summary>One ordered effect on a storage place.</summary>
internal sealed record OwnershipStatement(
    OwnershipStatementKind Kind,
    Place Place,
    TextSpan Span,
    SourceText? Source,
    OwnershipUseKind? UseKind = null,
    BorrowKind? BorrowKind = null,
    Symbol? LoanOwner = null,
    bool Conditional = false,
    SyntaxNode? Origin = null);

/// <summary>Control transfer for a lowered ownership block.</summary>
internal sealed class OwnershipTerminator
{
    public OwnershipTerminatorKind Kind { get; }
    public OwnershipBlock? Target { get; }
    public OwnershipBlock? FalseTarget { get; }
    public Expression? Condition { get; }

    public static OwnershipTerminator Unreachable { get; } = new(OwnershipTerminatorKind.Unreachable);
    /// <summary>Creates the ordinary sequential edge from a completed basic block.</summary>
    public static OwnershipTerminator Fallthrough(OwnershipBlock target) => new(OwnershipTerminatorKind.Fallthrough, target);

    /// <summary>Creates an unconditional source-level branch, for example from a goto.</summary>
    public static OwnershipTerminator Branch(OwnershipBlock target) => new(OwnershipTerminatorKind.Branch, target);

    /// <summary>Creates a two-way branch while retaining the source condition for diagnostics and escape analysis.</summary>
    public static OwnershipTerminator Conditional(Expression condition, OwnershipBlock whenTrue, OwnershipBlock whenFalse) =>
        new(OwnershipTerminatorKind.Conditional, whenTrue, whenFalse, condition);

    /// <summary>Creates a return terminator and retains its source expression for escape analysis.</summary>
    public static OwnershipTerminator Return(Expression? expression = null) => new(OwnershipTerminatorKind.Return, condition: expression);

    /// <summary>Creates a throw terminator and retains its source expression for escape analysis.</summary>
    public static OwnershipTerminator Throw(Expression? expression = null) => new(OwnershipTerminatorKind.Throw, condition: expression);

    /// <summary>The returned expression for a return terminator, if any.</summary>
    public Expression? ReturnExpression => Kind == OwnershipTerminatorKind.Return ? Condition : null;

    /// <summary>The thrown expression for a throw terminator, if any.</summary>
    public Expression? ThrowExpression => Kind == OwnershipTerminatorKind.Throw ? Condition : null;

    private OwnershipTerminator(OwnershipTerminatorKind kind, OwnershipBlock? target = null, OwnershipBlock? falseTarget = null, Expression? condition = null)
    {
        Kind = kind;
        Target = target;
        FalseTarget = falseTarget;
        Condition = condition;
    }
}

internal enum OwnershipTerminatorKind : byte
{
    Unreachable,
    Fallthrough,
    Branch,
    Conditional,
    Return,
    Throw
}
