using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution.ControlFlow;

/// <summary>
/// Represents a basic block within a control-flow graph (CFG).
/// A basic block consists of a linear sequence of non-branching instructions / statements
/// terminated by exactly one <see cref="BlockTerminator"/>.
/// </summary>
internal sealed class BasicBlock
{
    /// <summary>
    /// The unique numeric identifier of this basic block within its parent control-flow graph.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// The linear sequence of statement and expression AST nodes executed sequentially in this block.
    /// </summary>
    public List<SyntaxNode> Instructions { get; } = [];

    /// <summary>
    /// Predecessor basic blocks from which control flow enters this block.
    /// </summary>
    public List<BasicBlock> Predecessors { get; } = [];

    /// <summary>
    /// Successor basic blocks to which control flow transitions upon executing the terminator.
    /// </summary>
    public List<BasicBlock> Successors { get; } = [];

    /// <summary>
    /// The terminator specifying how execution leaves this block (branch, conditional branch, return, fallthrough, or unreachable).
    /// </summary>
    public BlockTerminator Terminator { get; set; } = BlockTerminator.Unreachable();

    /// <summary>
    /// Indicates whether this block is reachable from the CFG entry point during static analysis.
    /// Used for dead-code detection.
    /// </summary>
    public bool IsReachable { get; set; } = true;

    /// <summary>
    /// Indicates whether this block diverges unconditionally (e.g., contains an infinite loop or throw).
    /// </summary>
    public bool IsDivergent { get; set; }

    /// <summary>
    /// Initializes a new basic block with the specified identifier.
    /// </summary>
    /// <param name="id">The block identifier.</param>
    /// <summary>
    /// Initializes a new basic block with the specified identifier.
    /// </summary>
    /// <param name="id">The block identifier.</param>
    public BasicBlock(int id) => Id = id;

    /// <summary>
    /// Adds a predecessor basic block to <see cref="Predecessors"/> if not already present.
    /// </summary>
    /// <param name="pred">The predecessor block.</param>
    public void AddPredecessor(BasicBlock pred)
    {
        if (!Predecessors.Contains(pred))
            Predecessors.Add(pred);
    }

    /// <summary>
    /// Adds a successor basic block to <see cref="Successors"/> and automatically links this block as its predecessor.
    /// </summary>
    /// <param name="succ">The successor block.</param>
    public void AddSuccessor(BasicBlock succ)
    {
        if (!Successors.Contains(succ))
            Successors.Add(succ);

        succ.AddPredecessor(this);
    }
}


