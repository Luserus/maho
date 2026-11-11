using System.Collections.Generic;

namespace Maho.Resolution.ControlFlow;

internal sealed class ControlFlowGraph
{
    public SymbolHandle ContainingSymbol { get; }
    public BasicBlock Entry { get; }
    public BasicBlock Exit { get; }
    public List<BasicBlock> Blocks { get; } = [];

    public ControlFlowGraph(SymbolHandle containingSymbol, BasicBlock entry, BasicBlock exit)
    {
        ContainingSymbol = containingSymbol;
        Entry = entry;
        Exit = exit;
    }
}

