using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution.ControlFlow;

internal sealed class BasicBlock
{
    public int Id { get; }
    public List<SyntaxNode> Instructions { get; } = [];
    public List<BasicBlock> Predecessors { get; } = [];
    public List<BasicBlock> Successors { get; } = [];
    public BlockTerminator Terminator { get; set; } = BlockTerminator.Unreachable();

    public bool IsReachable { get; set; } = true;
    public bool IsDivergent { get; set; }

    public BasicBlock(int id)
    {
        Id = id;
    }

    public void AddPredecessor(BasicBlock pred)
    {
        if (!Predecessors.Contains(pred))
            Predecessors.Add(pred);
    }

    public void AddSuccessor(BasicBlock succ)
    {
        if (!Successors.Contains(succ))
            Successors.Add(succ);
        succ.AddPredecessor(this);
    }
}

