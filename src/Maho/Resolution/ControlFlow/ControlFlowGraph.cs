using System.Collections.Generic;

namespace Maho.Resolution.ControlFlow;

/// <summary>
/// Represents the directed control-flow graph (CFG) for a single callable unit
/// (function, member method, local function, or property accessor).
/// </summary>
internal sealed class ControlFlowGraph
{
    /// <summary>
    /// The symbol handle of the function, method, or property accessor owning this control-flow graph.
    /// </summary>
    public SymbolHandle ContainingSymbol { get; }

    /// <summary>
    /// The unique entry basic block where execution begins.
    /// </summary>
    public BasicBlock Entry { get; }

    /// <summary>
    /// The synthesized exit basic block serving as the single convergence sink for all terminal paths.
    /// </summary>
    public BasicBlock Exit { get; }

    /// <summary>
    /// The ordered list of all basic blocks belonging to this control-flow graph.
    /// </summary>
    public List<BasicBlock> Blocks { get; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ControlFlowGraph"/> class.
    /// </summary>
    /// <param name="containingSymbol">The symbol handle of the owning callable declaration.</param>
    /// <param name="entry">The entry basic block.</param>
    /// <param name="exit">The exit basic block.</param>
    public ControlFlowGraph(SymbolHandle containingSymbol, BasicBlock entry, BasicBlock exit)
    {
        ContainingSymbol = containingSymbol;
        Entry = entry;
        Exit = exit;
    }
}


