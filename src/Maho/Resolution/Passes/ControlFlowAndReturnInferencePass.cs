using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Resolution.ControlFlow;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution.Passes;

/// <summary>
/// Pass 6 of semantic resolution: Control Flow Graph (CFG) Construction and Return Type Inference.
/// <para>
/// Responsibilities:
/// <list type="bullet">
///   <item><description>Constructs a directed control-flow graph of basic blocks for every function, member method, and property accessor.</description></item>
///   <item><description>Performs reachability analysis from the entry block to identify dead code and divergent control paths (for static analysis/diagnostics only; no constant folding optimizations).</description></item>
///   <item><description>Infers inferred (<c>var</c>) return types by collecting and unifying return expressions across all reachable execution paths, reporting <c>MH2012</c> if branches conflict.</description></item>
///   <item><description>Collapses remaining unconstrained scalar literals in global variables and fields to their canonical intrinsic defaults.</description></item>
/// </list>
/// </para>
/// </summary>
internal sealed class ControlFlowAndReturnInferencePass : ResolutionPass
{
    private ResolutionContext context = null!;

    // Cached handles to discovered primitive intrinsics for zero-allocation type resolution
    private SymbolHandle? hInt8, hInt16, hInt32, hInt64, hInt128, hNInt;
    private SymbolHandle? hUInt8, hUInt16, hUInt32, hUInt64, hUInt128, hNUInt;
    private SymbolHandle? hFloat16, hFloat32, hFloat64, hFloat128;
    private SymbolHandle? hBool, hChar, hStr, hVoid;
    private TypeRef voidType = TypeRef.Unresolved;
    private TypeRef boolType = TypeRef.Unresolved;

    /// <summary>
    /// Executes Pass 6: builds CFGs, runs reachability analysis, infers return types, and collapses unconstrained globals/fields.
    /// </summary>
    /// <param name="context">The active resolution context containing discovered symbols and ASTs.</param>
    public override void Resolve(ResolutionContext context)
    {
        this.context = context;
        CacheIntrinsicHandles();

        // 1. Build CFG and analyze reachability for all functions, methods, and accessors
        foreach (var function in context.FunctionSymbols)
        {
            if (function.Syntax is { } fnDecl && fnDecl.Body is { } body)
            {
                var handle = ResolutionContext.GetHandle(function);
                var cfg = BuildCFG(body, handle);
                context.SetControlFlowGraph(handle, cfg);
                AnalyzeReachability(cfg);
                InferReturnType(cfg, function);
            }
        }

        foreach (var method in context.MethodSymbols)
        {
            if (method.Syntax is { } methodDecl && methodDecl.Body is { } body)
            {
                var handle = ResolutionContext.GetHandle(method);
                var cfg = BuildCFG(body, handle);
                context.SetControlFlowGraph(handle, cfg);
                AnalyzeReachability(cfg);
                InferReturnType(cfg, method);
            }
        }

        foreach (var prop in context.PropertySymbols)
        {
            if (prop.Syntax is { } syntax)
            {
                foreach (var accessor in syntax.Body.Accessors)
                {
                    if (accessor.Body is { } body)
                    {
                        var handle = ResolutionContext.GetHandle(prop);
                        var cfg = BuildCFG(body, handle);
                        context.SetControlFlowGraph(handle, cfg);
                        AnalyzeReachability(cfg);
                    }
                }
            }
        }

        // 2. Collapse unconstrained global and field literal variables to their default intrinsic types
        foreach (var global in context.GlobalVariableSymbols)
        {
            if (global.Type.IsLiteral)
                global.Type = CollapseToDefault(global.Type);
        }

        foreach (var field in context.FieldSymbols)
        {
            if (field.Type.IsLiteral)
                field.Type = CollapseToDefault(field.Type);
        }
    }

    /// <summary>
    /// Caches symbol handles for discovered core intrinsic types so downstream queries avoid dictionary lookups.
    /// </summary>
    private void CacheIntrinsicHandles()
    {
        var intrinsics = context.DiscoveredIntrinsics;
        if (intrinsics.TryGet(KnownIntrinsics.Int8, out var h1)) hInt8 = h1;
        if (intrinsics.TryGet(KnownIntrinsics.Int16, out var h2)) hInt16 = h2;
        if (intrinsics.TryGet(KnownIntrinsics.Int32, out var h3)) hInt32 = h3;
        if (intrinsics.TryGet(KnownIntrinsics.Int64, out var h4)) hInt64 = h4;
        if (intrinsics.TryGet(KnownIntrinsics.Int128, out var h5)) hInt128 = h5;
        if (intrinsics.TryGet(KnownIntrinsics.NInt, out var h6)) hNInt = h6;

        if (intrinsics.TryGet(KnownIntrinsics.UInt8, out var uh1)) hUInt8 = uh1;
        if (intrinsics.TryGet(KnownIntrinsics.UInt16, out var uh2)) hUInt16 = uh2;
        if (intrinsics.TryGet(KnownIntrinsics.UInt32, out var uh3)) hUInt32 = uh3;
        if (intrinsics.TryGet(KnownIntrinsics.UInt64, out var uh4)) hUInt64 = uh4;
        if (intrinsics.TryGet(KnownIntrinsics.UInt128, out var uh5)) hUInt128 = uh5;
        if (intrinsics.TryGet(KnownIntrinsics.NUInt, out var uh6)) hNUInt = uh6;

        if (intrinsics.TryGet(KnownIntrinsics.Float16, out var f1)) hFloat16 = f1;
        if (intrinsics.TryGet(KnownIntrinsics.Float32, out var f2)) hFloat32 = f2;
        if (intrinsics.TryGet(KnownIntrinsics.Float64, out var f3)) hFloat64 = f3;
        if (intrinsics.TryGet(KnownIntrinsics.Float128, out var f4)) hFloat128 = f4;

        if (intrinsics.TryGet(KnownIntrinsics.Bool, out var b))
        {
            hBool = b;
            boolType = TypeRef.Resolved(b);
        }

        if (intrinsics.TryGet(KnownIntrinsics.Char, out var c)) hChar = c;
        if (intrinsics.TryGet(KnownIntrinsics.Str, out var s)) hStr = s;

        if (intrinsics.TryGet(KnownIntrinsics.Void, out var v))
        {
            hVoid = v;
            voidType = TypeRef.Resolved(v);
        }
    }

    /// <summary>
    /// Constructs a <see cref="ControlFlowGraph"/> from the AST statements of a function body.
    /// </summary>
    /// <param name="body">The AST body of the function or method.</param>
    /// <param name="containingSymbol">The symbol handle of the callable owner.</param>
    /// <returns>A fully structured directed control-flow graph.</returns>
    private ControlFlowGraph BuildCFG(FunctionBody body, SymbolHandle containingSymbol)
    {
        int nextId = 0;
        BasicBlock CreateBlock() => new(nextId++);

        // Create the dedicated entry and exit blocks
        var entry = CreateBlock();
        var exit = CreateBlock();
        var allBlocks = new List<BasicBlock> { entry, exit };

        BasicBlock NewBlock()
        {
            var b = CreateBlock();
            allBlocks.Add(b);
            return b;
        }

        var currentBlock = entry;

        // Traverse the AST statements and construct basic blocks at branch points
        if (body is FunctionBlockBody blockBody)
        {
            currentBlock = ProcessLocals(blockBody.Locals, currentBlock, exit, NewBlock);
        }
        else if (body is FunctionLambdaBody lambdaBody)
        {
            currentBlock.Instructions.Add(lambdaBody.Statement);
            currentBlock = ProcessLocals([lambdaBody.Statement], currentBlock, exit, NewBlock);
        }

        // If the final block did not terminate in an explicit branch/return, wire fallthrough to exit
        if (currentBlock.Terminator.Kind == BlockTerminatorKind.Unreachable && currentBlock.Successors.Count == 0)
        {
            currentBlock.Terminator = BlockTerminator.Fallthrough(exit);
            currentBlock.AddSuccessor(exit);
        }

        var cfg = new ControlFlowGraph(containingSymbol, entry, exit);
        cfg.Blocks.AddRange(allBlocks);
        return cfg;
    }

    /// <summary>
    /// Processes a linear sequence of local statements and attaches them to basic blocks.
    /// When a control-flow statement (if, while, return, goto) is encountered, blocks are split and wired.
    /// </summary>
    /// <param name="locals">The local statements to process.</param>
    /// <param name="current">The currently active basic block.</param>
    /// <param name="exit">The CFG exit basic block.</param>
    /// <param name="newBlock">Factory delegate to allocate new basic blocks.</param>
    /// <returns>The basic block that is active after processing all statements.</returns>
    private BasicBlock ProcessLocals(
        IReadOnlyList<Local> locals,
        BasicBlock current,
        BasicBlock exit,
        Func<BasicBlock> newBlock)
    {
        var block = current;
        foreach (var local in locals)
        {
            switch (local)
            {
                case LocalExpressionStatement exprStmt:
                    block.Instructions.Add(exprStmt.Expression);

                    // Branching expressions (if, while) construct their own sub-graphs
                    if (exprStmt.Expression is IfExpression ifExpr)
                    {
                        block = ProcessIf(ifExpr, block, exit, newBlock);
                    }
                    else if (exprStmt.Expression is WhileExpression whileExpr)
                    {
                        block = ProcessWhile(whileExpr, block, exit, newBlock);
                    }
                    else if (exprStmt.IsFinalExpression)
                    {
                        // In expression-bodied functions or final block expressions, this is an implicit return
                        block.Terminator = BlockTerminator.Return(exprStmt.Expression);
                        block.AddSuccessor(exit);
                        block = newBlock();
                    }
                    break;

                case LocalReturnStatement retStmt:
                    block.Instructions.Add(retStmt.Statement);
                    block.Terminator = BlockTerminator.Return(retStmt.Statement.Expression);
                    block.AddSuccessor(exit);
                    // Code following a return begins in a new block (potentially unreachable)
                    block = newBlock();
                    break;

                case LocalVariableDeclarationStatement varStmt:
                    block.Instructions.Add(varStmt.Declaration);
                    break;

                case LocalGotoStatement gotoStmt:
                    block.Instructions.Add(gotoStmt);
                    block.Terminator = BlockTerminator.Unreachable();
                    block = newBlock();
                    break;
            }
        }

        return block;
    }

    /// <summary>
    /// Lowers an <see cref="IfExpression"/> into conditional basic block branches:
    /// wires the condition to then/else targets and converges both paths into a merge block.
    /// </summary>
    private BasicBlock ProcessIf(
        IfExpression ifExpr,
        BasicBlock current,
        BasicBlock exit,
        Func<BasicBlock> newBlock)
    {
        var thenBlock = newBlock();
        var elseBlock = newBlock();
        var mergeBlock = newBlock();

        // 1. Current block terminates with a two-way conditional branch
        current.Terminator = BlockTerminator.Conditional(ifExpr.Condition, thenBlock, elseBlock);
        current.AddSuccessor(thenBlock);
        current.AddSuccessor(elseBlock);

        // 2. Process 'then' branch statements
        thenBlock.Instructions.Add(ifExpr.ThenExpression);
        if (ifExpr.ThenExpression is BlockExpression thenBlockExpr)
        {
            var endThen = ProcessLocals(thenBlockExpr.Locals, thenBlock, exit, newBlock);
            if (endThen.Terminator.Kind == BlockTerminatorKind.Unreachable && endThen.Successors.Count == 0)
            {
                endThen.Terminator = BlockTerminator.Fallthrough(mergeBlock);
                endThen.AddSuccessor(mergeBlock);
            }
        }
        else
        {
            thenBlock.Terminator = BlockTerminator.Fallthrough(mergeBlock);
            thenBlock.AddSuccessor(mergeBlock);
        }

        // 3. Process 'else' branch statements (or direct fallthrough if no else)
        if (ifExpr.ElseExpression is { Expression: var elseSubExpr })
        {
            elseBlock.Instructions.Add(elseSubExpr);
            if (elseSubExpr is BlockExpression elseBlockExpr)
            {
                var endElse = ProcessLocals(elseBlockExpr.Locals, elseBlock, exit, newBlock);
                if (endElse.Terminator.Kind == BlockTerminatorKind.Unreachable && endElse.Successors.Count == 0)
                {
                    endElse.Terminator = BlockTerminator.Fallthrough(mergeBlock);
                    endElse.AddSuccessor(mergeBlock);
                }
            }
            else
            {
                elseBlock.Terminator = BlockTerminator.Fallthrough(mergeBlock);
                elseBlock.AddSuccessor(mergeBlock);
            }
        }
        else
        {
            elseBlock.Terminator = BlockTerminator.Fallthrough(mergeBlock);
            elseBlock.AddSuccessor(mergeBlock);
        }

        return mergeBlock;
    }

    /// <summary>
    /// Lowers a <see cref="WhileExpression"/> loop into header, body, and exit basic blocks with a backedge.
    /// </summary>
    private static BasicBlock ProcessWhile(
        WhileExpression whileExpr,
        BasicBlock current,
        BasicBlock exit,
        Func<BasicBlock> newBlock)
    {
        var headerBlock = newBlock();
        var bodyBlock = newBlock();
        var exitBlock = newBlock();

        // Jump into loop header
        current.Terminator = BlockTerminator.Branch(headerBlock);
        current.AddSuccessor(headerBlock);

        // Header tests condition: true branches to body, false breaks out to exit
        headerBlock.Instructions.Add(whileExpr.Condition);
        headerBlock.Terminator = BlockTerminator.Conditional(whileExpr.Condition, bodyBlock, exitBlock);
        headerBlock.AddSuccessor(bodyBlock);
        headerBlock.AddSuccessor(exitBlock);

        // Body executes statements then unconditionally loops back to header
        bodyBlock.Instructions.Add(whileExpr.Body);
        bodyBlock.Terminator = BlockTerminator.Branch(headerBlock);
        bodyBlock.AddSuccessor(headerBlock);

        return exitBlock;
    }

    /// <summary>
    /// Performs reachability analysis via breadth-first search from the entry basic block.
    /// Evaluates static boolean constants (<c>true</c>/<c>false</c>) on branch conditions to mark unreachable blocks.
    /// Note: this analysis is purely for dead-code and return diagnostics; no optimizations or AST mutation occur.
    /// </summary>
    /// <param name="cfg">The control-flow graph to analyze.</param>
    private static void AnalyzeReachability(ControlFlowGraph cfg)
    {
        var reachable = new HashSet<int>();
        var queue = new Queue<BasicBlock>();

        queue.Enqueue(cfg.Entry);
        reachable.Add(cfg.Entry.Id);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();

            // Check if branch condition is statically known to evaluate to true or false
            if (block.Terminator.Kind == BlockTerminatorKind.ConditionalBranch &&
                block.Terminator.Condition is { } cond)
            {
                bool? constBool = TryEvaluateConstantBool(cond);
                if (constBool == true)
                {
                    if (block.Terminator.Target is { } t && reachable.Add(t.Id))
                        queue.Enqueue(t);
                    continue;
                }
                if (constBool == false)
                {
                    if (block.Terminator.FalseTarget is { } f && reachable.Add(f.Id))
                        queue.Enqueue(f);
                    continue;
                }
            }

            // Normal control flow: enqueue all successor blocks
            foreach (var succ in block.Successors)
            {
                if (reachable.Add(succ.Id))
                    queue.Enqueue(succ);
            }
        }

        // Tag each block's reachability flag
        foreach (var b in cfg.Blocks)
        {
            b.IsReachable = reachable.Contains(b.Id);
        }
    }

    /// <summary>
    /// Evaluates whether an expression is a static literal boolean <c>true</c> or <c>false</c>.
    /// Uses <see cref="MatchingKeywordKind"/> enum checks with zero string comparisons.
    /// </summary>
    private static bool? TryEvaluateConstantBool(Expression cond)
    {
        if (cond is NamedExpression named)
        {
            if (named.Identifier.MatchingKind is MatchingKeywordKind.True) return true;
            if (named.Identifier.MatchingKind is MatchingKeywordKind.False) return false;
        }

        if (cond is LiteralExpression lit)
        {
            if (lit.Literal.MatchingKind is MatchingKeywordKind.True) return true;
            if (lit.Literal.MatchingKind is MatchingKeywordKind.False) return false;
        }

        return null;
    }

    /// <summary>
    /// Collapses an unconstrained scalar literal type into its canonical default intrinsic type
    /// (Int -> int32, Float -> float64, Str -> Str, Char -> char).
    /// </summary>
    private TypeRef CollapseToDefault(TypeRef type)
    {
        if (type.IsLiteral && type.SpecialType is CompileTimeLiteralType lit)
        {
            return lit.LiteralKind switch
            {
                CompileTimeLiteralKind.Int => hInt32 is { } h ? TypeRef.Resolved(h) : TypeRef.Inferred,
                CompileTimeLiteralKind.Float => hFloat64 is { } h ? TypeRef.Resolved(h) : TypeRef.Inferred,
                CompileTimeLiteralKind.Str => hStr is { } h ? TypeRef.Resolved(h) : TypeRef.Inferred,
                CompileTimeLiteralKind.Char => hChar is { } h ? TypeRef.Resolved(h) : TypeRef.Inferred,
                _ => TypeRef.Inferred
            };
        }

        if (type.IsInferred)
        {
            return hInt32 is { } h ? TypeRef.Resolved(h) : type;
        }

        return type;
    }

    /// <summary>
    /// Infers the return type of a function or method whose return type was declared as <c>var</c> (inferred).
    /// Inspects all reachable return statements in the CFG:
    /// <list type="bullet">
    ///   <item><description>If zero returns and fallthrough reaches exit, infers <c>void</c>.</description></item>
    ///   <item><description>If returns exist, unifies their types; all branches must evaluate to the exact same concrete type.</description></item>
    ///   <item><description>If conflicting return types are detected, reports <c>MH2012</c> without widening.</description></item>
    ///   <item><description>If non-void function falls through to exit without return, reports <c>MH2012</c>.</description></item>
    /// </list>
    /// </summary>
    /// <param name="cfg">The control-flow graph for the callable.</param>
    /// <param name="function">The function or method symbol.</param>
    private void InferReturnType(ControlFlowGraph cfg, Symbol function)
    {
        var returnType = function switch
        {
            FunctionSymbol fn => fn.ReturnType,
            MethodSymbol m => m.ReturnType,
            _ => TypeRef.Unresolved
        };

        if (!returnType.IsInferred)
            return;

        var reachableReturns = new List<(TypeRef Type, TextSpan Span, SourceText? Source)>();
        bool fallthroughReachesExit = false;

        // Collect return types from all reachable blocks
        foreach (var block in cfg.Blocks.Where(b => b.IsReachable))
        {
            if (block.Terminator.Kind == BlockTerminatorKind.Return)
            {
                if (block.Terminator.ReturnExpression is { } retExpr)
                {
                    var exprType = context.GetRequiredExpressionType(retExpr);
                    if (exprType.IsLiteral)
                        exprType = CollapseToDefault(exprType);

                    reachableReturns.Add((exprType, retExpr.GetSpan() ?? default, retExpr.GetSource()));
                }
                else
                {
                    reachableReturns.Add((voidType, default, null));
                }
            }
            else if (block.Successors.Contains(cfg.Exit) && block.Terminator.Kind == BlockTerminatorKind.Fallthrough)
            {
                fallthroughReachesExit = true;
            }
        }

        // Case 1: No returns and fallthrough reaches exit -> void function
        if (reachableReturns.Count == 0 && fallthroughReachesExit)
        {
            SetSymbolReturnType(function, voidType);
            return;
        }

        // Case 2: One or more returns exist -> verify exact match across all branches
        if (reachableReturns.Count > 0)
        {
            var first = reachableReturns[0];
            bool hasMismatch = false;

            // If return is uncollapsed literal and no standard intrinsics exist, cannot finalize
            if (first.Type.IsLiteral && hInt32 is null)
                return;

            for (int i = 1; i < reachableReturns.Count; i++)
            {
                var current = reachableReturns[i];
                if (!first.Type.Equals(current.Type))
                {
                    hasMismatch = true;
                    context.Diagnostics.ReportConflictingReturnType(
                        current.Span,
                        current.Type.ToDisplayString(context),
                        first.Type.ToDisplayString(context),
                        current.Source);
                }
            }

            // If code can fall through to exit without a return in a non-void function, report mismatch
            if (fallthroughReachesExit && !first.Type.Equals(voidType))
            {
                hasMismatch = true;
                context.Diagnostics.ReportConflictingReturnType(
                    first.Span,
                    "void",
                    first.Type.ToDisplayString(context),
                    first.Source);
            }

            SetSymbolReturnType(function, hasMismatch ? TypeRef.Error : first.Type);
        }
        else
        {
            SetSymbolReturnType(function, voidType);
        }
    }

    /// <summary>
    /// Assigns the finalized return type to a function or method symbol.
    /// </summary>
    private static void SetSymbolReturnType(Symbol function, TypeRef type)
    {
        switch (function)
        {
            case FunctionSymbol fn: fn.ReturnType = type; break;
            case MethodSymbol m: m.ReturnType = type; break;
        }
    }
}

