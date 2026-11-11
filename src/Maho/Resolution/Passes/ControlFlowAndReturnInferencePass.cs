using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Resolution.ControlFlow;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution.Passes;

internal sealed class ControlFlowAndReturnInferencePass : ResolutionPass
{
    private ResolutionContext context = null!;

    private SymbolHandle? hInt8, hInt16, hInt32, hInt64, hInt128, hNInt;
    private SymbolHandle? hUInt8, hUInt16, hUInt32, hUInt64, hUInt128, hNUInt;
    private SymbolHandle? hFloat16, hFloat32, hFloat64, hFloat128;
    private SymbolHandle? hBool, hChar, hStr, hVoid;
    private TypeRef voidType = TypeRef.Unresolved;
    private TypeRef boolType = TypeRef.Unresolved;

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

        // Collapse unconstrained global and field variables
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

    private ControlFlowGraph BuildCFG(FunctionBody body, SymbolHandle containingSymbol)
    {
        int nextId = 0;
        BasicBlock CreateBlock() => new(nextId++);

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

        if (body is FunctionBlockBody blockBody)
        {
            currentBlock = ProcessLocals(blockBody.Locals, currentBlock, exit, NewBlock);
        }
        else if (body is FunctionLambdaBody lambdaBody)
        {
            currentBlock.Instructions.Add(lambdaBody.Statement);
            currentBlock = ProcessLocals([lambdaBody.Statement], currentBlock, exit, NewBlock);
        }

        if (currentBlock.Terminator.Kind == BlockTerminatorKind.Unreachable && currentBlock.Successors.Count == 0)
        {
            currentBlock.Terminator = BlockTerminator.Fallthrough(exit);
            currentBlock.AddSuccessor(exit);
        }

        var cfg = new ControlFlowGraph(containingSymbol, entry, exit);
        cfg.Blocks.AddRange(allBlocks);
        return cfg;
    }

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
                        block.Terminator = BlockTerminator.Return(exprStmt.Expression);
                        block.AddSuccessor(exit);
                        block = newBlock();
                    }
                    break;

                case LocalReturnStatement retStmt:
                    block.Instructions.Add(retStmt.Statement);
                    block.Terminator = BlockTerminator.Return(retStmt.Statement.Expression);
                    block.AddSuccessor(exit);
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

    private BasicBlock ProcessIf(
        IfExpression ifExpr,
        BasicBlock current,
        BasicBlock exit,
        Func<BasicBlock> newBlock)
    {
        var thenBlock = newBlock();
        var elseBlock = newBlock();
        var mergeBlock = newBlock();

        current.Terminator = BlockTerminator.Conditional(ifExpr.Condition, thenBlock, elseBlock);
        current.AddSuccessor(thenBlock);
        current.AddSuccessor(elseBlock);

        // Process then branch
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

        // Process else branch
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

    private static BasicBlock ProcessWhile(
        WhileExpression whileExpr,
        BasicBlock current,
        BasicBlock exit,
        Func<BasicBlock> newBlock)
    {
        var headerBlock = newBlock();
        var bodyBlock = newBlock();
        var exitBlock = newBlock();

        current.Terminator = BlockTerminator.Branch(headerBlock);
        current.AddSuccessor(headerBlock);

        headerBlock.Instructions.Add(whileExpr.Condition);
        headerBlock.Terminator = BlockTerminator.Conditional(whileExpr.Condition, bodyBlock, exitBlock);
        headerBlock.AddSuccessor(bodyBlock);
        headerBlock.AddSuccessor(exitBlock);

        bodyBlock.Instructions.Add(whileExpr.Body);
        bodyBlock.Terminator = BlockTerminator.Branch(headerBlock);
        bodyBlock.AddSuccessor(headerBlock);

        return exitBlock;
    }

    private static void AnalyzeReachability(ControlFlowGraph cfg)
    {
        var reachable = new HashSet<int>();
        var queue = new Queue<BasicBlock>();

        queue.Enqueue(cfg.Entry);
        reachable.Add(cfg.Entry.Id);

        while (queue.Count > 0)
        {
            var block = queue.Dequeue();

            // Evaluate constant conditions
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

            foreach (var succ in block.Successors)
            {
                if (reachable.Add(succ.Id))
                    queue.Enqueue(succ);
            }
        }

        foreach (var b in cfg.Blocks)
        {
            b.IsReachable = reachable.Contains(b.Id);
        }
    }

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

        if (reachableReturns.Count == 0 && fallthroughReachesExit)
        {
            SetSymbolReturnType(function, voidType);
            return;
        }

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

    private static void SetSymbolReturnType(Symbol function, TypeRef type)
    {
        switch (function)
        {
            case FunctionSymbol fn: fn.ReturnType = type; break;
            case MethodSymbol m: m.ReturnType = type; break;
        }
    }
}
