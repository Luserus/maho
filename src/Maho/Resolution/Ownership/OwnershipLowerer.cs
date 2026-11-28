using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution.Ownership;

/// <summary>
/// Lowers resolved source bodies into ordered storage operations.  This phase deliberately does
/// not perform diagnostics: a body remains useful to later analyses even if typing already found
/// source errors.
/// </summary>
internal sealed class OwnershipLowerer
{
    private readonly ResolutionContext context;
    private readonly OwnershipTypeClassifier ownership;
    private readonly Dictionary<string, OwnershipBlock> labels = new(StringComparer.Ordinal);
    // Safe alias/reference locals unwrap directly to their target referent place (`aliasReferents`).
    // Delegates and non-owning borrow-holding composites retain their own storage identity while
    // tracking captured/borrowed places separately in `borrowReferents` and `body.ReferentMap`.
    private readonly Dictionary<Symbol, Place> aliasReferents = [];
    private readonly Dictionary<Symbol, List<(Place Referent, BorrowKind Kind)>> borrowReferents = [];
    private readonly HashSet<SyntaxNode> returnLeafExpressions = [];
    private readonly Stack<List<Symbol>> scopes = [];
    private OwnershipBody body = null!;
    private OwnershipBlock current = null!;
    private ParameterSymbol? receiverParameter;
    private int nextBlockId;
    private int nextTemporaryId;

    private void SetKnownReferents(Symbol owner, List<(Place Referent, BorrowKind Kind)> referents)
    {
        borrowReferents[owner] = referents;
        body.ReferentMap[owner] = [.. referents];
    }

    private void ClearKnownReferents(Symbol owner)
    {
        aliasReferents.Remove(owner);
        borrowReferents.Remove(owner);
        body.ReferentMap.Remove(owner);
    }

    public OwnershipLowerer(ResolutionContext context)
    {
        this.context = context;
        ownership = new OwnershipTypeClassifier(context);
    }

    private Place CreateTemporaryPlace(Expression expression, TypeRef type, Scope scope)
    {
        var tempSym = new TemporaryStorageSymbol(new SymbolID(++nextTemporaryId), scope, type);
        var place = new Place(tempSym);
        body.RegisterPlace(tempSym, type);
        return place;
    }

    /// <summary>
    /// Produces one fresh ownership CFG for a resolved callable body.
    /// </summary>
    /// <remarks>
    /// This resets all state that is callable-local: labels, safe-alias provenance, lexical scope
    /// frames, receiver identity, and block numbering. Parameters are modeled as already assigned;
    /// locals are modeled only when their declarations are encountered.
    /// </remarks>
    public OwnershipBody Lower(FunctionBody syntax, Scope scope, SymbolHandle containingSymbol)
    {
        BeginBodyLowering(containingSymbol);
        BeginScope();
        RegisterParameters(containingSymbol);
        if (syntax is FunctionBlockBody block)
        {
            PredeclareLabels(block.Locals);
            LowerLocals(block.Locals, scope);
        }
        else if (syntax is FunctionLambdaBody lambda)
        {
            LowerLocal(lambda.Statement, scope);
        }

        EndScope();
        ConnectFallthroughToExit(current);
        return body;
    }

    /// <summary>
    /// Produces one fresh ownership CFG for a resolved lambda body so move, borrow, and return
    /// escape checks cover inline closures uniformly alongside named functions.
    /// </summary>
    public OwnershipBody LowerLambda(LambdaExpression lambda, Scope lambdaScope, SymbolHandle containingFunction)
    {
        BeginBodyLowering(containingFunction);
        BeginScope();

        foreach (var pSyntax in lambda.Parameters)
        {
            var paramPart = ResolutionContext.GetSymbolName(pSyntax.Identifier).Last;
            if (lambdaScope[paramPart].OfType<ParameterSymbol>().FirstOrDefault() is { } paramSymbol)
            {
                body.RegisterPlace(paramSymbol, paramSymbol.Type);
                scopes.Peek().Add(paramSymbol);
                Emit(OwnershipStatementKind.StorageLive, new Place(paramSymbol), pSyntax.GetSpan() ?? default, pSyntax.GetSource());
                Emit(OwnershipStatementKind.Assign, new Place(paramSymbol), pSyntax.GetSpan() ?? default, pSyntax.GetSource());
            }
        }

        if (context.ResolvedTree.TryGetLambdaCaptures(lambda, out var captures))
        {
            foreach (var cap in captures)
            {
                TypeRef capType = cap.Symbol switch
                {
                    LocalVariableSymbol lv => lv.Type,
                    ParameterSymbol pv => pv.Type,
                    _ => TypeRef.Unresolved
                };
                if (!capType.IsUnresolved && !body.PlaceTypes.ContainsKey(cap.Symbol))
                {
                    body.RegisterPlace(cap.Symbol, capType);
                    Emit(OwnershipStatementKind.StorageLive, new Place(cap.Symbol), GetSymbolSpan(cap.Symbol), GetSymbolSource(cap.Symbol));
                    Emit(OwnershipStatementKind.Assign, new Place(cap.Symbol), GetSymbolSpan(cap.Symbol), GetSymbolSource(cap.Symbol));
                }
            }
        }

        if (lambda.Body is BlockExpression blockBody)
        {
            var innerScope = context.GetSyntaxScope(blockBody, lambdaScope);
            PredeclareLabels(blockBody.Locals);
            LowerLocals(blockBody.Locals, innerScope);
            if (blockBody.FinalExpression is { } finalExpr)
                LowerValue(finalExpr, innerScope);
            EndScope();
        }
        else
        {
            LowerValue(lambda.Body, lambdaScope);
            EmitBorrowOriginsForExpression(lambda.Body, lambdaScope, lambda.Body);
            EndScope();
            current.Terminator = OwnershipTerminator.Return(lambda.Body);
            current.AddSuccessor(body.Exit);
            current = AddBlock();
        }

        ConnectFallthroughToExit(current);
        return body;
    }

    private void BeginBodyLowering(SymbolHandle containingSymbol)
    {
        labels.Clear();
        aliasReferents.Clear();
        borrowReferents.Clear();
        returnLeafExpressions.Clear();
        scopes.Clear();
        receiverParameter = null;
        nextBlockId = 0;

        var entry = NewBlock();
        var exit = NewBlock();
        body = new OwnershipBody(containingSymbol, entry, exit);
        body.Blocks.Add(entry);
        body.Blocks.Add(exit);
        current = entry;
    }

    private OwnershipBlock NewBlock() => new(nextBlockId++);

    private OwnershipBlock AddBlock()
    {
        var block = NewBlock();
        body.Blocks.Add(block);
        return block;
    }

    /// <summary>
    /// Makes every declared parameter a root place and initializes it at CFG entry.
    /// </summary>
    /// <remarks>
    /// Callers cannot enter a callable without providing its parameters, so each parameter gets a
    /// <c>StorageLive</c> followed by <c>Assign</c>. The explicit receiver is retained separately
    /// so source <c>this</c> resolves to this exact storage symbol rather than a synthetic pointer.
    /// </remarks>
    private void RegisterParameters(SymbolHandle containingSymbol)
    {
        IReadOnlyList<SymbolHandle> parameters = context.GetSymbol(containingSymbol) switch
        {
            FunctionSymbol function => function.Parameters,
            MethodSymbol method => method.Parameters,
            _ => []
        };

        foreach (var handle in parameters)
        {
            if (context.GetParameterSymbol(handle) is not { } parameter)
                continue;

            if (parameter.IsReceiver)
                receiverParameter = parameter;

            body.RegisterPlace(parameter, parameter.Type);
            scopes.Peek().Add(parameter);
            Emit(OwnershipStatementKind.StorageLive, new Place(parameter), parameter.Syntax?.GetSpan() ?? default, parameter.Syntax?.GetSource());
            Emit(OwnershipStatementKind.Assign, new Place(parameter), parameter.Syntax?.GetSpan() ?? default, parameter.Syntax?.GetSource());
        }
    }

    /// <summary>Allocates every label block before lowering statements so forward gotos can target it.</summary>
    private void PredeclareLabels(IReadOnlyList<Local> locals)
    {
        foreach (var local in locals)
        {
            if (local is LocalLabelStatement label)
                labels.TryAdd(label.Identifier.Value, AddBlock());
        }
    }

    private void LowerLocals(IReadOnlyList<Local> locals, Scope scope)
    {
        foreach (var local in locals)
            LowerLocal(local, scope);
    }

    /// <summary>
    /// Lowers one statement-like local syntax node, splitting the current block at control-flow
    /// boundaries such as return, goto, and labels.
    /// </summary>
    private void LowerLocal(Local local, Scope scope)
    {
        switch (local)
        {
            case LocalVariableDeclarationStatement declaration:
                LowerDeclaration(declaration.Declaration, scope);
                break;

            case LocalExpressionStatement expression:
                if (expression.Expression is IfExpression conditional)
                    LowerIf(conditional, scope);
                else if (expression.Expression is WhileExpression loop)
                    LowerWhile(loop, scope);
                else if (expression.Expression is ForExpression forExpr)
                    LowerFor(forExpr, scope);
                else if (expression.Expression is TryExpression tryExpr)
                    LowerTry(tryExpr, scope);
                else
                    LowerValue(expression.Expression, scope);
                break;

            case LocalYieldStatement yieldStmt:
                if (yieldStmt.Expression is not null)
                    LowerValue(yieldStmt.Expression, scope);
                break;

            case LocalReturnStatement returned:
                if (returned.Statement.Expression is { } value)
                {
                    LowerReturnValue(value, scope);
                    EmitBorrowOriginsForExpression(value, scope, value);
                }
                ExitAllScopes();
                current.Terminator = OwnershipTerminator.Return(returned.Statement.Expression);
                current.AddSuccessor(body.Exit);
                current = AddBlock();
                break;

            case LocalThrowStatement thrown:
                if (thrown.Statement.Expression is { } thrownVal)
                {
                    LowerValue(thrownVal, scope);
                    EmitBorrowOriginsForExpression(thrownVal, scope, thrownVal);
                }
                ExitAllScopes();
                current.Terminator = OwnershipTerminator.Throw(thrown.Statement.Expression);
                current.AddSuccessor(body.Exit);
                current = AddBlock();
                break;

            case LocalGotoStatement jump:
                if (labels.TryGetValue(jump.Identifier.Value, out var target))
                {
                    current.Terminator = OwnershipTerminator.Branch(target);
                    current.AddSuccessor(target);
                }
                current = AddBlock();
                break;

            case LocalLabelStatement label when labels.TryGetValue(label.Identifier.Value, out var tgt):
                ConnectFallthrough(current, tgt);
                current = tgt;
                break;
        }
    }

    /// <summary>
    /// Adds storage for a local declaration and lowers its initializer.
    /// </summary>
    /// <remarks>
    /// A safe alias/reference initializer is special: the alias handle is assigned first and then
    /// its named loan begins. That ordering is required for non-lexical liveness to regard the loan
    /// as live only after the declaration has completed.
    /// </remarks>
    private void LowerDeclaration(VariableDeclaration declaration, Scope scope)
    {
        foreach (var local in GetDeclaredLocals(declaration))
        {
            var place = new Place(local);
            body.RegisterPlace(local, local.Type);
            scopes.Peek().Add(local);
            Emit(OwnershipStatementKind.StorageLive, place, declaration.GetSpan() ?? default, declaration.GetSource(), origin: declaration);
        }

        if (declaration.Declarator.Initializer?.Initializer is { } initializer)
        {
            foreach (var local in GetDeclaredLocals(declaration))
            {
                if (TryGetDeclarationBorrow(initializer, scope, local.Type, out var borrowed, out var borrowKind))
                {
                    // The binding is initialized before its named loan becomes live.  This keeps
                    // holder liveness precise: the loan begins at the declaration's completed
                    // program point and ends after the holder's last use.
                    EmitBorrowedBindingWithInnerReferents(
                        initializer,
                        borrowed,
                        borrowKind,
                        local,
                        declaration.GetSource(),
                        declaration.Declarator.Identifier.GetSpan() ?? default,
                        scope,
                        emitAssignBeforeBorrow: true);
                }
                else
                {
                    LowerValueForDestination(initializer, local.Type, scope);
                    Emit(OwnershipStatementKind.Assign, new Place(local), declaration.Declarator.Identifier.GetSpan() ?? default, declaration.GetSource(), origin: declaration);
                    EmitOwnedReferentsAfterAssign(initializer, local, scope);
                }
            }
        }
    }

    private IEnumerable<LocalVariableSymbol> GetDeclaredLocals(VariableDeclaration declaration)
    {
        foreach (var local in context.LocalVariableSymbols)
        {
            if (ReferenceEquals(local.Syntax, declaration))
                yield return local;
        }
    }

    /// <summary>
    /// Recognizes declaration forms that create a persistent safe loan instead of consuming an
    /// initializer as a value.
    /// </summary>
    /// <remarks>
    /// Alias declarations borrow an existing place directly. Reference declarations must spell
    /// address-of. Unsafe aliases and raw pointers deliberately do not enter safe-loan analysis.
    /// </remarks>
    private bool TryGetDeclarationBorrow(Expression initializer, Scope scope, TypeRef targetType, out Place borrowed, out BorrowKind borrowKind)
    {
        borrowed = default;
        borrowKind = BorrowKind.Shared;

        var unwrappedTarget = TypeRef.UnwrapDyn(targetType);
        if (unwrappedTarget.SpecialType is AliasSemanticType { IsUnsafe: false } alias)
        {
            if (IsStaticStorageExpression(initializer))
                return false;

            if (TryGetPlace(initializer, scope, out borrowed))
            {
                borrowKind = alias.IsMutable ? BorrowKind.Mutable : BorrowKind.Shared;
                return true;
            }
            if (IsTemporaryExpression(initializer))
            {
                borrowed = CreateTemporaryPlace(initializer, context.GetRequiredExpressionType(initializer), scope);
                borrowKind = alias.IsMutable ? BorrowKind.Mutable : BorrowKind.Shared;
                return true;
            }
            return false;
        }

        if (unwrappedTarget.SpecialType is ReferenceSemanticType reference && initializer is UnaryExpression { OperatorToken.Kind: TokenKind.Ampersand } address)
        {
            if (TryGetPlace(address.Operand, scope, out borrowed))
            {
                borrowKind = reference.IsMutable ? BorrowKind.Mutable : BorrowKind.Shared;
                return true;
            }
            if (IsTemporaryExpression(address.Operand))
            {
                borrowed = CreateTemporaryPlace(address.Operand, context.GetRequiredExpressionType(address.Operand), scope);
                borrowKind = reference.IsMutable ? BorrowKind.Mutable : BorrowKind.Shared;
                return true;
            }
            return false;
        }

        return false;
    }

    private void EmitBorrowedBindingWithInnerReferents(
        Expression sourceExpr,
        Place borrowed,
        BorrowKind borrowKind,
        Symbol loanOwner,
        SourceText? assignSource,
        TextSpan assignSpan,
        Scope scope,
        bool emitAssignBeforeBorrow)
    {
        borrowed = ResolveKnownReferent(borrowed);
        List<(Place Referent, BorrowKind Kind)> innerRefs = [];
        bool hasTemporaryAddress = sourceExpr is UnaryExpression { OperatorToken.Kind: TokenKind.Ampersand } addr &&
                                   !TryGetPlace(addr.Operand, scope, out _);
        if (hasTemporaryAddress && sourceExpr is UnaryExpression addrUnary)
        {
            var tempSpan = addrUnary.Operand.GetSpan() ?? default;
            var tempSource = addrUnary.Operand.GetSource();
            Emit(OwnershipStatementKind.StorageLive, borrowed, tempSpan, tempSource, origin: sourceExpr);
            LowerValue(addrUnary.Operand, scope);
            Emit(OwnershipStatementKind.Assign, borrowed, tempSpan, tempSource, origin: sourceExpr);
            TryCollectSafeBorrowReferents(addrUnary.Operand, scope, innerRefs);
        }

        if (emitAssignBeforeBorrow)
            Emit(OwnershipStatementKind.Assign, new Place(loanOwner), assignSpan, assignSource, origin: sourceExpr);

        var span = sourceExpr.GetSpan() ?? default;
        var source = sourceExpr.GetSource();
        Emit(OwnershipStatementKind.Borrow, borrowed, span, source, borrowKind: borrowKind, loanOwner: loanOwner, origin: sourceExpr);
        aliasReferents[loanOwner] = borrowed;

        foreach (var (innerRef, innerKind) in innerRefs)
            Emit(OwnershipStatementKind.Borrow, ResolveKnownReferent(innerRef), span, source, borrowKind: innerKind, loanOwner: loanOwner, origin: sourceExpr);

        if (emitAssignBeforeBorrow)
            return;

        if (innerRefs.Count > 0)
            SetKnownReferents(loanOwner, [(borrowed, borrowKind), .. innerRefs.Select(r => (ResolveKnownReferent(r.Referent), r.Kind))]);
        else
            SetKnownReferents(loanOwner, [(borrowed, borrowKind)]);
    }

    private static bool IsStaticStorageExpression(Expression expr)
    {
        while (expr is ParenthesizedExpression p)
            expr = p.Expression;

        return expr is NameofExpression || (expr is LiteralExpression lit && lit.Literal.Kind == TokenKind.String);
    }

    private static bool IsTemporaryExpression(Expression expr)
    {
        while (expr is ParenthesizedExpression p)
            expr = p.Expression;

        return expr is LiteralExpression or BinaryExpression or CallExpression or TupleExpression or ObjectCreationExpression or DefaultExpression or LambdaExpression or MemberAccessExpression;
    }

    /// <summary>
    /// Follows safe-alias provenance to the original storage place for a reborrow.
    /// </summary>
    /// <remarks>
    /// Without this normalization, borrowing <c>first</c> in <c>&amp;T second = first</c> would
    /// incorrectly loan the alias handle's stack slot. The visited set also protects malformed or
    /// cyclic provenance from making lowering loop forever.
    /// </remarks>
    private Place ResolveKnownReferent(Place place)
    {
        var seen = new HashSet<Symbol>();
        while (seen.Add(place.Root) && aliasReferents.TryGetValue(place.Root, out var referent))
            place = new Place(referent.Root, [.. referent.Projections, .. place.Projections]);
        return place;
    }

    /// <summary>Builds then, else, and merge blocks for an if expression and lowers both arms.</summary>
    private void LowerIf(IfExpression expression, Scope scope)
    {
        if (expression.Declaration is not null)
            LowerDeclaration(expression.Declaration, scope);
        LowerValue(expression.Condition, scope);

        var thenBlock = AddBlock();
        var elseBlock = AddBlock();
        var merge = AddBlock();
        current.Terminator = OwnershipTerminator.Conditional(expression.Condition, thenBlock, elseBlock);
        current.AddSuccessor(thenBlock);
        current.AddSuccessor(elseBlock);

        current = thenBlock;
        LowerBranchExpression(expression.ThenExpression, scope);
        ConnectFallthrough(current, merge);

        current = elseBlock;
        if (expression.ElseExpression is { } otherwise)
            LowerBranchExpression(otherwise.Expression, scope);
        ConnectFallthrough(current, merge);

        current = merge;
    }

    /// <summary>
    /// Builds a loop header, body, and exit block, with the body fallthrough forming the back edge.
    /// </summary>
    private void LowerWhile(WhileExpression expression, Scope scope)
    {
        if (expression.Declaration is not null)
            LowerDeclaration(expression.Declaration, scope);

        var header = AddBlock();
        var loopBody = AddBlock();
        var exit = AddBlock();
        ConnectFallthrough(current, header);

        current = header;
        LowerValue(expression.Condition, scope);
        current.Terminator = OwnershipTerminator.Conditional(expression.Condition, loopBody, exit);
        current.AddSuccessor(loopBody);
        current.AddSuccessor(exit);

        current = loopBody;
        LowerBranchExpression(expression.Body, scope);
        ConnectFallthrough(current, header);
        current = exit;
    }

    private void LowerFor(ForExpression expression, Scope scope)
    {
        var forScope = context.GetSyntaxScope(expression, scope);
        LowerPlaceEvaluation(expression.Collection, scope);

        Place? collectionBorrowPlace = null;
        var colSpan = expression.Collection.GetSpan() ?? expression.Keyword.Span;
        var colSource = expression.Collection.GetSource() ?? expression.GetSource();
        if (TryGetPlace(expression.Collection, scope, out var colPlace))
        {
            var resolvedColPlace = ResolveKnownReferent(colPlace);
            collectionBorrowPlace = CreateTemporaryPlace(expression.Collection, context.GetRequiredExpressionType(expression.Collection), forScope);
            Emit(OwnershipStatementKind.StorageLive, collectionBorrowPlace.Value, colSpan, colSource, origin: expression.Collection);
            Emit(OwnershipStatementKind.Assign, collectionBorrowPlace.Value, colSpan, colSource, origin: expression.Collection);
            Emit(OwnershipStatementKind.Borrow, resolvedColPlace, colSpan, colSource, borrowKind: BorrowKind.Shared, loanOwner: collectionBorrowPlace.Value.Root, origin: expression.Collection);
        }

        if (expression.Declaration is not null)
            LowerDeclaration(expression.Declaration, forScope);

        var header = AddBlock();
        var loopBody = AddBlock();
        var exit = AddBlock();
        ConnectFallthrough(current, header);

        current = header;
        current.Terminator = OwnershipTerminator.Conditional(expression.Collection, loopBody, exit);
        current.AddSuccessor(loopBody);
        current.AddSuccessor(exit);

        current = loopBody;
        if (expression.Declaration is not null)
        {
            foreach (var local in GetDeclaredLocals(expression.Declaration))
            {
                Emit(OwnershipStatementKind.Assign, new Place(local), expression.Declaration.GetSpan() ?? default, expression.GetSource(), origin: expression.Declaration);
            }
        }
        if (expression.VariableExpression is { } varExpr && TryGetPlace(varExpr, scope, out var place))
        {
            Emit(OwnershipStatementKind.Assign, place, varExpr.GetSpan() ?? default, varExpr.GetSource(), origin: varExpr);
        }

        LowerBranchExpression(expression.Body, forScope);
        ConnectFallthrough(current, header);
        current = exit;

        if (collectionBorrowPlace is { } cbPlace)
        {
            Emit(OwnershipStatementKind.Use, cbPlace, colSpan, colSource, OwnershipUseKind.Read, origin: expression.Collection);
            Emit(OwnershipStatementKind.StorageDead, cbPlace, colSpan, colSource, origin: expression.Collection);
        }
    }

    private void LowerTry(TryExpression tryExpr, Scope scope)
    {
        var tryBlock = AddBlock();
        var mergeBlock = AddBlock();
        var finallyBlock = tryExpr.FinallyClause is not null ? AddBlock() : mergeBlock;

        ConnectFallthrough(current, tryBlock);
        current = tryBlock;

        LowerBranchExpression(tryExpr.TryBlock, scope);
        ConnectFallthrough(current, finallyBlock);

        foreach (var catchClause in tryExpr.CatchClauses)
        {
            var catchScope = context.GetSyntaxScope(catchClause, scope);
            var catchEntry = AddBlock();
            tryBlock.AddSuccessor(catchEntry);
            current = catchEntry;

            BeginScope();
            if (catchClause.VariableIdentifier is { } varId && varId.Value != "_")
            {
                var varPart = new SymbolPart(varId.Value);
                if (catchScope.SymbolsByName.TryGetValue(varPart, out var symList) && symList.Count > 0 && symList[0] is LocalVariableSymbol local)
                {
                    var place = new Place(local);
                    body.RegisterPlace(local, local.Type);
                    scopes.Peek().Add(local);
                    Emit(OwnershipStatementKind.StorageLive, place, varId.Span, catchClause.GetSource());
                    Emit(OwnershipStatementKind.Assign, place, varId.Span, catchClause.GetSource(), origin: catchClause);
                }
            }

            LowerBranchExpression(catchClause.Block, catchScope);
            EndScope();
            ConnectFallthrough(current, finallyBlock);
        }

        if (tryExpr.FinallyClause is { } finallyClause)
        {
            current = finallyBlock;
            LowerBranchExpression(finallyClause.Block, scope);
            ConnectFallthrough(current, mergeBlock);
        }

        current = mergeBlock;
    }

    /// <summary>
    /// Lowers an expression used as a control-flow branch, preserving block-local lexical scope
    /// when the branch is a block expression.
    /// </summary>
    private void LowerBranchExpression(Expression expression, Scope scope)
    {
        if (expression is BlockExpression block)
        {
            var blockScope = context.GetSyntaxScope(block, scope);
            BeginScope();
            LowerLocals(block.Locals, blockScope);
            if (block.FinalExpression is { } final)
                LowerValue(final, blockScope);
            EndScope();
            return;
        }

        if (expression is IfExpression conditional)
        {
            LowerIf(conditional, scope);
            return;
        }

        if (expression is WhileExpression loop)
        {
            LowerWhile(loop, scope);
            return;
        }

        if (expression is ForExpression forExpr)
        {
            LowerFor(forExpr, scope);
            return;
        }

        if (expression is TryExpression tryExpr)
        {
            LowerTry(tryExpr, scope);
            return;
        }

        LowerValue(expression, scope);
    }

    /// <summary>
    /// Lowers evaluation effects of an expression in source order.
    /// </summary>
    /// <remarks>
    /// A recognized place becomes one Copy/Move/Read operation. Other expressions recursively
    /// lower only their ownership-relevant children; type resolution has already validated their
    /// operators and conversions.
    /// </remarks>
    private void LowerValue(Expression expression, Scope scope)
    {
        if (TryGetPlace(expression, scope, out var place))
        {
            var useKind = GetUseKind(expression, place);
            Emit(OwnershipStatementKind.Use, place, expression.GetSpan() ?? default, expression.GetSource(), useKind, origin: expression);
            return;
        }

        switch (expression)
        {
            case ParenthesizedExpression parenthesized:
                LowerValue(parenthesized.Expression, scope);
                break;

            case AssignmentExpression assignment:
                LowerAssignment(assignment, scope);
                break;

            case UnaryExpression unary when unary.OperatorToken.Kind == TokenKind.Ampersand:
                bool mutable = TypeRef.UnwrapDyn(context.GetRequiredExpressionType(unary)).SpecialType is ReferenceSemanticType reference && reference.IsMutable;
                EmitEphemeralBorrow(unary.Operand, unary, mutable, scope);
                break;

            case UnaryExpression unary:
                LowerUnaryOperator(unary, scope);
                break;

            case BinaryExpression binary:
                LowerBinaryOperator(binary, scope);
                break;

            case CallExpression call:
                LowerCall(call, scope);
                break;

            case MemberAccessExpression member:
                if (context.ResolvedTree.TryGetBoundMethodClosure(member, out var boundInfo) &&
                    boundInfo.ReceiverKind == ReceiverKind.Value)
                {
                    LowerValueForDestination(member.Expression, boundInfo.ReceiverType, scope);
                }
                else
                {
                    LowerPlaceEvaluation(member.Expression, scope);
                }
                break;

            case LambdaExpression lambda:
                if (context.ResolvedTree.TryGetLambdaCaptures(lambda, out var lambdaCaps))
                {
                    foreach (var cap in lambdaCaps)
                    {
                        if (cap.IsMove && IsStorageSymbol(cap.Symbol))
                        {
                            Emit(
                                OwnershipStatementKind.Use,
                                new Place(cap.Symbol),
                                lambda.GetSpan() ?? default,
                                lambda.GetSource(),
                                OwnershipUseKind.Move,
                                origin: lambda);
                        }
                    }
                }
                break;

            case IndexExpression index:
                LowerPlaceEvaluation(index.Expression, scope);
                foreach (var arg in index.Arguments)
                    LowerValue(arg, scope);
                break;

            case TupleExpression tuple:
                foreach (var element in tuple.Arguments)
                    LowerValue(element, scope);
                break;

            case CollectionExpression collection:
                foreach (var element in collection.Expressions)
                    LowerValue(element, scope);
                break;

            case MultiTermExpression multiTerm:
                foreach (var term in multiTerm.Terms)
                    LowerValue(term, scope);
                break;

            case DefaultExpression:
                // Default zero/identity initialization does not read or move any existing places.
                break;

            case ObjectInitializerExpression objInit:
                if (objInit.AllocationArguments is { } objAllocArgs)
                    LowerCallArguments(objAllocArgs, GetAllocationOperatorParameters(objInit), scope);
                LowerInitializerEntries(objInit.Initializer, scope);
                break;

            case ConstructorCallExpression ctor:
                if (ctor.AllocationArguments is { } ctorAllocArgs)
                    LowerCallArguments(ctorAllocArgs, GetAllocationOperatorParameters(ctor), scope);
                IReadOnlyList<ParameterSymbol> ctorParams = GetConstructorParameters(ctor);
                LowerCallArguments(ctor.Arguments, ctorParams, scope);
                if (ctor.WithClause is { } withClause)
                    LowerInitializerEntries(withClause.Initializer, scope);
                break;

            case BlockExpression block:
                LowerBranchExpression(block, scope);
                break;

            case IfExpression conditional:
                LowerIf(conditional, scope);
                break;

            case WhileExpression loop:
                LowerWhile(loop, scope);
                break;

            case ForExpression forExpr:
                LowerFor(forExpr, scope);
                break;

            case TryExpression tryExpr:
                LowerTry(tryExpr, scope);
                break;
        }
    }

    /// <summary>
    /// Lowers an expression that supplies a concrete destination value rather than merely being
    /// evaluated for its own type.
    /// </summary>
    /// <remarks>
    /// Safe aliases and reference dereferences denote the referent, even though their expression
    /// type is handle-like. Assigning <c>&amp;T</c> or the result of <c>*Reference&lt;T&gt;</c> to
    /// <c>T</c> therefore consumes/copies the referent according to <paramref name="destination"/>,
    /// not the alias/reference handle. This is the destination-boundary implicit dereference rule;
    /// standalone alias evaluation still evaluates only its handle.
    /// </remarks>
    private void LowerReturnValue(Expression expression, Scope scope)
    {
        CollectReturnLeafExpressions(expression);
        LowerValueForDestination(expression, GetContainingReturnType(), scope, isReturnUse: returnLeafExpressions.Contains(expression));
    }

    private void CollectReturnLeafExpressions(Expression expr)
    {
        while (expr is ParenthesizedExpression paren)
            expr = paren.Expression;

        if (expr is IfExpression ifExpr)
        {
            CollectReturnLeafExpressions(ifExpr.ThenExpression);
            if (ifExpr.ElseExpression is { } elseBranch)
                CollectReturnLeafExpressions(elseBranch.Expression);
            return;
        }

        if (expr is BlockExpression blockExpr && blockExpr.FinalExpression is { } finalExpr)
        {
            CollectReturnLeafExpressions(finalExpr);
            return;
        }

        if (expr is NamedExpression or MemberAccessExpression or IndexExpression)
            returnLeafExpressions.Add(expr);
    }

    private TypeRef GetContainingReturnType() => context.GetSymbol(body.ContainingSymbol) switch
    {
        FunctionSymbol fn => fn.ReturnType,
        MethodSymbol m => m.ReturnType,
        _ => TypeRef.Unresolved
    };

    private void LowerValueForDestination(Expression expression, TypeRef destination, Scope scope, bool isReturnUse = false)
    {
        isReturnUse = isReturnUse || returnLeafExpressions.Contains(expression);
        if (IsOwnedValueDestination(destination) && TryGetPlace(expression, scope, out var place))
        {
            place = ResolveKnownReferent(place);
            TypeRef sourceType = context.GetRequiredExpressionType(expression);
            bool isAliasMaterialization = TypeRef.UnwrapDyn(sourceType).SpecialType is AliasSemanticType or ReferenceSemanticType;
            OwnershipUseKind useKind;
            if (isAliasMaterialization)
            {
                // Alias-to-value syntax is always a copy of the referent, never a hidden move.
                // A move-only referent therefore needs a focused diagnostic rather than silently
                // consuming storage that the alias promises to keep referring to.
                useKind = OwnershipUseKind.Copy;
                TypeRef materializedType = destination.Handle is { Kind: SymbolKind.GenericParameter }
                    ? TypeRef.UnwrapDyn(sourceType).SpecialType switch
                    {
                        AliasSemanticType a => a.TargetType,
                        ReferenceSemanticType r => r.ElementType,
                        _ => destination
                    }
                    : destination;
                if (ownership.Classify(materializedType) == OwnershipKind.Move)
                    context.Diagnostics.ReportCannotCopyMoveOnlyAlias(expression.GetSpan() ?? default, place.Root.Name.Text, expression.GetSource());
            }
            else
            {
                TypeRef classificationType = !sourceType.IsError && !sourceType.IsUnresolved && !sourceType.IsInferred && !sourceType.IsLiteral &&
                    destination.Handle is { Kind: SymbolKind.GenericParameter }
                    ? sourceType
                    : destination;
                useKind = ownership.Classify(classificationType) == OwnershipKind.Copy
                    ? OwnershipUseKind.Copy
                    : OwnershipUseKind.Move;
            }
            Emit(OwnershipStatementKind.Use, place, expression.GetSpan() ?? default, expression.GetSource(), useKind, origin: expression, isReturnUse: isReturnUse);
            return;
        }

        LowerValue(expression, scope);
    }

    /// <summary>
    /// Determines whether a destination expects an owned value, which activates implicit alias
    /// materialization at the assignment boundary.
    /// </summary>
    /// <remarks>
    /// Reference, alias, and pointer destinations retain their handle semantics. Their declarations
    /// are handled by <see cref="TryGetDeclarationBorrow"/> or ordinary handle assignment instead.
    /// </remarks>
    private static bool IsOwnedValueDestination(TypeRef type) => TypeRef.UnwrapDyn(type).SpecialType is not (AliasSemanticType or ReferenceSemanticType or PointerSemanticType);

    /// <summary>
    /// Lowers assignment while avoiding a false read of a directly assigned field/place.
    /// </summary>
    /// <remarks>
    /// The special treatment permits construction patterns such as <c>T value; value.x = ...;</c>.
    /// Index bases and indices remain genuine reads and are lowered before the right-hand side.
    /// </remarks>
    private void LowerAssignment(AssignmentExpression assignment, Scope scope)
    {
        // Writing a field is permitted while its aggregate is being initialized.  Treating the
        // LHS as a read would reject the standard construction pattern `T value; value.x = …`.
        // Index expressions still evaluate their base and index because those are genuine reads.
        if (assignment.LhsExpression is IndexExpression index)
        {
            LowerPlaceEvaluation(index.Expression, scope);
            LowerValue(index.Index, scope);
        }
        else if (!TryGetPlace(assignment.LhsExpression, scope, out _))
        {
            LowerPlaceEvaluation(assignment.LhsExpression, scope);
        }
        TypeRef destination = context.GetRequiredExpressionType(assignment.LhsExpression);
        LowerValueForDestination(assignment.RhsExpression, destination, scope);
        if (TryGetPlace(assignment.LhsExpression, scope, out var target))
        {
            Emit(OwnershipStatementKind.Assign, target, assignment.LhsExpression.GetSpan() ?? default, assignment.GetSource(), origin: assignment);
            var unwrappedDest = TypeRef.UnwrapDyn(destination);
            if (target.Projections.Count == 0 && unwrappedDest.SpecialType is not AliasSemanticType)
            {
                ClearKnownReferents(target.Root);
                if (unwrappedDest.SpecialType is ReferenceSemanticType &&
                    TryGetDeclarationBorrow(assignment.RhsExpression, scope, destination, out var borrowed, out var borrowKind))
                {
                    EmitBorrowedBindingWithInnerReferents(
                        assignment.RhsExpression,
                        borrowed,
                        borrowKind,
                        target.Root,
                        assignment.GetSource(),
                        assignment.LhsExpression.GetSpan() ?? default,
                        scope,
                        emitAssignBeforeBorrow: false);
                }
                else
                {
                    EmitOwnedReferentsAfterAssign(assignment.RhsExpression, target.Root, scope);
                }
            }
        }
    }

    /// <summary>
    /// Lowers a call after consulting its resolved parameter list to distinguish by-value uses from
    /// safe-reference/alias arguments.
    /// </summary>
    /// <remarks>
    /// A place passed to a safe-borrow parameter becomes an unowned, ephemeral <c>Borrow</c> that
    /// expires after this statement. All other arguments are lowered as ordinary value expressions.
    /// </remarks>
    private void LowerCall(CallExpression call, Scope scope)
    {
        IReadOnlyList<ParameterSymbol> parameters = GetCallParameters(call);
        if (context.ResolvedTree.TryGetReference(call, out var callOpHandle) &&
            context.GetSymbol(callOpHandle) is MethodSymbol callOpMethod &&
            callOpMethod.Parameters.Count > 0 &&
            context.GetParameterSymbol(callOpMethod.Parameters[0]) is { IsReceiver: true } opRecv)
        {
            if (opRecv.ReceiverKind == ReceiverKind.Value)
                LowerValueForDestination(call.Callee, opRecv.Type, scope);
            else
                EmitEphemeralBorrow(call.Callee, call.Callee, opRecv.ReceiverKind == ReceiverKind.MutReference, scope);
        }
        else if (call.Callee is MemberAccessExpression member)
        {
            if (!context.ResolvedTree.IsUnboundTypeMethodCall(call))
                LowerPlaceEvaluation(member.Expression, scope);
        }
        else if (call.Callee is NamedExpression namedCallee)
        {
            if (TryGetStorageSymbol(namedCallee, scope, out _))
                LowerPlaceEvaluation(namedCallee, scope);
        }
        else
            LowerValue(call.Callee, scope);

        List<(Place Owner, TextSpan Span, SourceText? Source, SyntaxNode Origin)> callTempOwners = [];
        List<(Place Referent, BorrowKind Kind)> calleeRefs = [];
        if (call.Callee is not MemberAccessExpression && call.Callee is not NamedExpression &&
            TryCollectSafeBorrowReferents(call.Callee, scope, calleeRefs))
        {
            var tempOwner = CreateTemporaryPlace(call.Callee, context.GetRequiredExpressionType(call.Callee), scope);
            var span = call.Callee.GetSpan() ?? default;
            var source = call.Callee.GetSource();
            Emit(OwnershipStatementKind.StorageLive, tempOwner, span, source, origin: call.Callee);
            Emit(OwnershipStatementKind.Assign, tempOwner, span, source, origin: call.Callee);
            foreach (var (referent, kind) in calleeRefs)
                Emit(OwnershipStatementKind.Borrow, ResolveKnownReferent(referent), span, source, borrowKind: kind, loanOwner: tempOwner.Root, origin: call.Callee);
            callTempOwners.Add((tempOwner, span, source, call.Callee));
        }

        LowerCallArguments(call.Arguments, parameters, scope, callTempOwners);
    }

    /// <summary>
    /// Lowers a non-<c>&amp;</c> unary operator using its resolved operator method parameters when present,
    /// borrowing the operand for <c>this&amp;</c>/<c>this mut&amp;</c> receivers and checking move/copy on value parameters.
    /// </summary>
    private void LowerUnaryOperator(UnaryExpression unary, Scope scope)
    {
        var parameters = GetResolvedParameters(unary);
        if (parameters.Count == 1)
        {
            var p0 = parameters[0];
            if (p0.IsReceiver && !IsOwnedValueDestination(p0.Type))
            {
                var unwrapped = TypeRef.UnwrapDyn(p0.Type);
                if (unwrapped.SpecialType is ReferenceSemanticType refRecv)
                    EmitEphemeralBorrow(unary.Operand, unary, refRecv.IsMutable, scope);
                else
                    LowerPlaceEvaluation(unary.Operand, scope);
                return;
            }

            LowerValueForDestination(unary.Operand, p0.Type, scope);
            return;
        }

        LowerValue(unary.Operand, scope);
    }

    /// <summary>
    /// Lowers a binary operator expression, routing user-defined operator operands through
    /// <see cref="LowerValueForDestination"/> so move-only values cannot be copied through aliases or references.
    /// </summary>
    private void LowerBinaryOperator(BinaryExpression binary, Scope scope)
    {
        var parameters = GetResolvedParameters(binary);
        if (parameters.Count == 2)
        {
            LowerValueForDestination(binary.LeftExpression, parameters[0].Type, scope);
            LowerValueForDestination(binary.RightExpression, parameters[1].Type, scope);
            return;
        }

        LowerValue(binary.LeftExpression, scope);
        LowerValue(binary.RightExpression, scope);
    }

    private void EmitEphemeralBorrow(Expression operand, SyntaxNode origin, bool isMutable, Scope scope)
    {
        if (TryGetPlace(operand, scope, out var borrowed))
        {
            Emit(OwnershipStatementKind.Borrow, borrowed, origin.GetSpan() ?? default, origin.GetSource(), borrowKind: isMutable ? BorrowKind.Mutable : BorrowKind.Shared, origin: origin);
            return;
        }

        var operandType = context.GetRequiredExpressionType(operand);
        var tempPlace = CreateTemporaryPlace(operand, operandType, scope);
        Emit(OwnershipStatementKind.StorageLive, tempPlace, origin.GetSpan() ?? default, origin.GetSource(), origin: origin);
        LowerValue(operand, scope);
        Emit(OwnershipStatementKind.Assign, tempPlace, origin.GetSpan() ?? default, origin.GetSource(), origin: origin);
        Emit(OwnershipStatementKind.Borrow, tempPlace, origin.GetSpan() ?? default, origin.GetSource(), borrowKind: isMutable ? BorrowKind.Mutable : BorrowKind.Shared, origin: origin);
        List<(Place Referent, BorrowKind Kind)> innerRefs = [];
        if (TryCollectSafeBorrowReferents(operand, scope, innerRefs))
        {
            foreach (var (innerRef, innerKind) in innerRefs)
                Emit(OwnershipStatementKind.Borrow, ResolveKnownReferent(innerRef), origin.GetSpan() ?? default, origin.GetSource(), borrowKind: innerKind, origin: origin);
        }
        Emit(OwnershipStatementKind.StorageDead, tempPlace, origin.GetSpan() ?? default, origin.GetSource(), origin: origin);
    }

    private IReadOnlyList<ParameterSymbol> GetResolvedParameters(SyntaxNode syntax)
    {
        if (!context.ResolvedTree.TryGetReference(syntax, out var handle))
            return [];

        IReadOnlyList<SymbolHandle> handles = context.GetSymbol(handle) switch
        {
            FunctionSymbol fn => fn.Parameters,
            MethodSymbol method => method.Parameters,
            _ => []
        };

        var result = new List<ParameterSymbol>(handles.Count);
        foreach (var h in handles)
        {
            if (context.GetParameterSymbol(h) is { } p)
                result.Add(p);
        }
        return result;
    }

    private void LowerCallArguments(
        SeparatedSyntaxList<Expression> arguments,
        IReadOnlyList<ParameterSymbol> parameters,
        Scope scope,
        List<(Place Owner, TextSpan Span, SourceText? Source, SyntaxNode Origin)>? existingTempOwners = null)
    {
        var tempOwners = existingTempOwners ?? [];
        for (int index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            var parameter = index < parameters.Count ? parameters[index] : null;
            if (parameter is not null && IsSafeBorrow(parameter.Type) && !IsStaticStorageExpression(argument))
            {
                bool mutable = TypeRef.UnwrapDyn(parameter.Type).SpecialType switch
                {
                    ReferenceSemanticType reference => reference.IsMutable,
                    AliasSemanticType alias => alias.IsMutable && !alias.IsUnsafe,
                    _ => false
                };
                if (!TryGetPlace(argument, scope, out var place))
                {
                    place = CreateTemporaryPlace(argument, context.GetRequiredExpressionType(argument), scope);
                    Emit(OwnershipStatementKind.StorageLive, place, argument.GetSpan() ?? default, argument.GetSource(), origin: argument);
                    LowerValue(argument, scope);
                    Emit(OwnershipStatementKind.Assign, place, argument.GetSpan() ?? default, argument.GetSource(), origin: argument);
                    Emit(OwnershipStatementKind.Borrow, place, argument.GetSpan() ?? default, argument.GetSource(), borrowKind: mutable ? BorrowKind.Mutable : BorrowKind.Shared, origin: argument);
                    Emit(OwnershipStatementKind.StorageDead, place, argument.GetSpan() ?? default, argument.GetSource(), origin: argument);
                }
                else
                {
                    Emit(OwnershipStatementKind.Borrow, place, argument.GetSpan() ?? default, argument.GetSource(), borrowKind: mutable ? BorrowKind.Mutable : BorrowKind.Shared, origin: argument);
                }
            }
            else
            {
                LowerValueForDestination(argument, parameter?.Type ?? context.GetRequiredExpressionType(argument), scope);
                List<(Place Referent, BorrowKind Kind)> argRefs = [];
                if (TryCollectSafeBorrowReferents(argument, scope, argRefs))
                {
                    var tempOwner = CreateTemporaryPlace(argument, context.GetRequiredExpressionType(argument), scope);
                    var span = argument.GetSpan() ?? default;
                    var source = argument.GetSource();
                    Emit(OwnershipStatementKind.StorageLive, tempOwner, span, source, origin: argument);
                    Emit(OwnershipStatementKind.Assign, tempOwner, span, source, origin: argument);
                    foreach (var (referent, kind) in argRefs)
                        Emit(OwnershipStatementKind.Borrow, ResolveKnownReferent(referent), span, source, borrowKind: kind, loanOwner: tempOwner.Root, origin: argument);
                    tempOwners.Add((tempOwner, span, source, argument));
                }
            }
        }

        foreach (var (owner, span, source, origin) in tempOwners)
        {
            Emit(OwnershipStatementKind.Use, owner, span, source, OwnershipUseKind.Read, origin: origin);
            Emit(OwnershipStatementKind.StorageDead, owner, span, source, origin: origin);
        }
    }

    private void EmitOwnedReferentsAfterAssign(Expression sourceExpr, Symbol owner, Scope scope)
    {
        List<(Place Referent, BorrowKind Kind)> collected = [];
        if (!TryCollectSafeBorrowReferents(sourceExpr, scope, collected))
            return;

        var resolvedList = new List<(Place Referent, BorrowKind Kind)>(collected.Count);
        var span = sourceExpr.GetSpan() ?? default;
        var source = sourceExpr.GetSource();
        foreach (var (referent, kind) in collected)
        {
            var resolved = ResolveKnownReferent(referent);
            Emit(OwnershipStatementKind.Borrow, resolved, span, source, borrowKind: kind, loanOwner: owner, origin: sourceExpr);
            resolvedList.Add((resolved, kind));
        }
        SetKnownReferents(owner, resolvedList);
    }

    private void EmitBorrowOriginsForExpression(Expression sourceExpr, Scope scope, Expression originExpr)
    {
        List<(Place Referent, BorrowKind Kind)> collected = [];
        if (!TryCollectSafeBorrowReferents(sourceExpr, scope, collected))
            return;

        var span = originExpr.GetSpan() ?? default;
        var source = originExpr.GetSource();
        foreach (var (referent, kind) in collected)
            Emit(OwnershipStatementKind.Borrow, ResolveKnownReferent(referent), span, source, borrowKind: kind, origin: originExpr);
    }

    private bool TryCollectSafeBorrowReferents(
        Expression expr,
        Scope scope,
        List<(Place Referent, BorrowKind Kind)> referents,
        HashSet<SyntaxNode>? visited = null)
    {
        while (expr is ParenthesizedExpression p)
            expr = p.Expression;

        visited ??= [];
        if (!visited.Add(expr))
            return false;

        int initialCount = referents.Count;

        if (expr is LambdaExpression lambda)
        {
            if (context.ResolvedTree.TryGetLambdaCaptures(lambda, out var captures))
            {
                foreach (var cap in captures)
                {
                    if (!cap.IsMove && IsStorageSymbol(cap.Symbol))
                        referents.Add((new Place(cap.Symbol), cap.IsMutable ? BorrowKind.Mutable : BorrowKind.Shared));
                }
            }
            return referents.Count > initialCount;
        }

        if (expr is TupleExpression tuple)
        {
            foreach (var arg in tuple.Arguments)
                TryCollectSafeBorrowElement(arg, scope, referents, visited);
            return referents.Count > initialCount;
        }

        if (expr is ObjectInitializerExpression objInit)
        {
            CollectInitializerBorrowReferents(objInit.Initializer, scope, referents, visited);
            return referents.Count > initialCount;
        }

        if (expr is ConstructorCallExpression ctor)
        {
            foreach (var arg in ctor.Arguments)
                TryCollectSafeBorrowElement(arg, scope, referents, visited);
            if (ctor.WithClause is { } withClause)
                CollectInitializerBorrowReferents(withClause.Initializer, scope, referents, visited);
            return referents.Count > initialCount;
        }

        if (expr is IfExpression ifExpr)
        {
            TryCollectSafeBorrowReferents(ifExpr.ThenExpression, scope, referents, visited);
            if (ifExpr.ElseExpression is { } elseBranch)
                TryCollectSafeBorrowReferents(elseBranch.Expression, scope, referents, visited);
            return referents.Count > initialCount;
        }

        if (expr is BlockExpression blockExpr && blockExpr.FinalExpression is { } finalExpr)
        {
            TryCollectSafeBorrowReferents(finalExpr, scope, referents, visited);
            return referents.Count > initialCount;
        }

        if (expr is MemberAccessExpression member &&
            context.ResolvedTree.TryGetBoundMethodClosure(member, out var boundInfo))
        {
            if (boundInfo.ReceiverKind is not (ReceiverKind.Value or ReceiverKind.Pointer or ReceiverKind.MutPointer))
            {
                Expression recvExpr = member.Expression;
                while (recvExpr is ParenthesizedExpression rp)
                    recvExpr = rp.Expression;
                if (recvExpr is UnaryExpression { OperatorToken.Kind: TokenKind.Ampersand } addrRecv)
                    recvExpr = addrRecv.Operand;
                if (TryGetPlace(recvExpr, scope, out var recvPlace))
                {
                    bool isMutRecv = boundInfo.ReceiverKind is ReceiverKind.MutReference or ReceiverKind.MutAlias;
                    referents.Add((recvPlace, isMutRecv ? BorrowKind.Mutable : BorrowKind.Shared));
                    return true;
                }
            }
            return false;
        }

        if (TryGetPlace(expr, scope, out var place) &&
            borrowReferents.TryGetValue(place.Root, out var tracked) &&
            tracked.Count > 0)
        {
            referents.AddRange(tracked);
            return true;
        }

        return referents.Count > initialCount;
    }

    private void CollectInitializerBorrowReferents(
        TypeInitializer initializer,
        Scope scope,
        List<(Place Referent, BorrowKind Kind)> referents,
        HashSet<SyntaxNode> visited)
    {
        foreach (var entry in initializer.Expressions)
        {
            if (entry is AssignmentExpression assign && assign.EqualsOperator.Kind == TokenKind.Equals)
                TryCollectSafeBorrowElement(assign.RhsExpression, scope, referents, visited);
            else
                TryCollectSafeBorrowElement(entry, scope, referents, visited);
        }
    }

    private void TryCollectSafeBorrowElement(
        Expression arg,
        Scope scope,
        List<(Place Referent, BorrowKind Kind)> referents,
        HashSet<SyntaxNode> visited)
    {
        if (arg is UnaryExpression { OperatorToken.Kind: TokenKind.Ampersand } addr &&
            TypeRef.UnwrapDyn(context.GetRequiredExpressionType(addr)).SpecialType is ReferenceSemanticType rf)
        {
            if (TryGetPlace(addr.Operand, scope, out var targetPlace))
                referents.Add((targetPlace, rf.IsMutable ? BorrowKind.Mutable : BorrowKind.Shared));
            else
                TryCollectSafeBorrowReferents(addr.Operand, scope, referents, visited);
            return;
        }

        TryCollectSafeBorrowReferents(arg, scope, referents, visited);
    }

    private bool TryGetMethodReceiverMutability(MethodSymbol method, out bool isMutable)
    {
        isMutable = false;
        if (method.Flags.HasFlag(FunctionFlags.Static) || method.Parameters.Count == 0)
            return false;

        if (context.GetParameterSymbol(method.Parameters[0]) is not { IsReceiver: true } p0)
        {
            return false;
        }

        isMutable = TypeRef.UnwrapDyn(p0.Type).SpecialType switch
        {
            ReferenceSemanticType rf => rf.IsMutable,
            AliasSemanticType { IsUnsafe: false } al => al.IsMutable,
            _ => false
        };
        return true;
    }

    private void LowerInitializerEntries(TypeInitializer initializer, Scope scope)
    {
        foreach (var entry in initializer.Expressions)
        {
            if (entry is AssignmentExpression assign && assign.EqualsOperator.Kind == TokenKind.Equals)
            {
                TypeRef fieldType = context.GetExpressionType(assign.LhsExpression)
                    ?? (context.ResolvedTree.TryGetReference(assign.LhsExpression, out var fieldHandle) &&
                        context.GetFieldSymbol(fieldHandle) is { } fs
                        ? fs.Type
                        : context.GetExpressionType(assign.RhsExpression) ?? TypeRef.Error);
                LowerValueForDestination(assign.RhsExpression, fieldType, scope);
            }
            else
            {
                LowerValue(entry, scope);
            }
        }
    }

    private List<ParameterSymbol> GetAllocationOperatorParameters(Expression creationNode)
    {
        if (!context.ResolvedTree.TryGetAllocationOperatorReference(creationNode, out var handle))
            return [];

        if (context.GetSymbol(handle) is MethodSymbol opMethod)
        {
            var list = opMethod.Parameters.Select(context.GetParameterSymbol).OfType<ParameterSymbol>().ToList();
            if (list.Count > 0 && list[0].IsReceiver)
                list.RemoveAt(0);
            return list;
        }

        return [];
    }

    private List<ParameterSymbol> GetConstructorParameters(ConstructorCallExpression ctor)
    {
        if (!context.ResolvedTree.TryGetReference(ctor, out var handle))
            return [];

        if (context.GetSymbol(handle) is MethodSymbol { Name.Text: "Construct" } ctorMethod)
        {
            var list = ctorMethod.Parameters.Select(context.GetParameterSymbol).OfType<ParameterSymbol>().ToList();
            if (list.Count > 0 && list[0].IsReceiver)
                list.RemoveAt(0);
            return list;
        }

        return [];
    }

    private List<ParameterSymbol> GetCallParameters(CallExpression call)
    {
        if (context.ResolvedTree.TryGetReference(call.Callee, out var handle))
        {
            switch (context.GetSymbol(handle))
            {
                case FunctionSymbol function:
                    return function.Parameters.Select(context.GetParameterSymbol).OfType<ParameterSymbol>().ToList();
                case MethodSymbol method:
                {
                    var list = method.Parameters.Select(context.GetParameterSymbol).OfType<ParameterSymbol>().ToList();
                    if (!method.Flags.HasFlag(FunctionFlags.Static) &&
                        list.Count > 0 &&
                        list[0].IsReceiver &&
                        !context.ResolvedTree.IsUnboundTypeMethodCall(call))
                    {
                        list.RemoveAt(0);
                    }
                    return list;
                }
            }
        }

        if (context.ResolvedTree.TryGetReference(call, out var opHandle) &&
            context.GetSymbol(opHandle) is MethodSymbol callableMethod)
        {
            var list = callableMethod.Parameters.Select(context.GetParameterSymbol).OfType<ParameterSymbol>().ToList();
            if (list.Count > 0 && list[0].IsReceiver)
                list.RemoveAt(0);
            return list;
        }

        return [];
    }

    private static bool IsSafeBorrow(TypeRef type) => TypeRef.UnwrapDyn(type).SpecialType is ReferenceSemanticType or AliasSemanticType { IsUnsafe: false };

    /// <summary>
    /// Evaluates a place strictly as a read, for example the receiver needed to reach a member.
    /// </summary>
    private void LowerPlaceEvaluation(Expression expression, Scope scope)
    {
        if (TryGetPlace(expression, scope, out var place))
        {
            Emit(OwnershipStatementKind.Use, place, expression.GetSpan() ?? default, expression.GetSource(), OwnershipUseKind.Read, origin: expression);
            return;
        }
        LowerValue(expression, scope);
    }

    /// <summary>
    /// Converts an lvalue-like expression into a storage root plus projections when it remains in
    /// the safe ownership domain.
    /// </summary>
    /// <remarks>
    /// Names, fields, indices, and safe dereferences are places. A raw-pointer dereference is
    /// intentionally not a place: using it opts into unsafe aliasing and escapes borrow tracking.
    /// </remarks>
    private bool TryGetPlace(Expression expression, Scope scope, out Place place)
    {
        switch (expression)
        {
            case ParenthesizedExpression parenthesized:
                return TryGetPlace(parenthesized.Expression, scope, out place);

            case NamedExpression named:
                if (TryGetStorageSymbol(named, scope, out var storage))
                {
                    place = new Place(storage);
                    return true;
                }
                break;

            case MemberAccessExpression member when
                context.ResolvedTree.TryGetReference(member, out var fieldHandle) &&
                fieldHandle.Kind == SymbolKind.Field &&
                TryGetPlace(member.Expression, scope, out var receiver):
                var recvType = context.GetExpressionType(member.Expression) ?? TypeRef.Unresolved;
                if (ownership.IsOwningIndirection(recvType, out _))
                {
                    place = receiver.Dereference().ProjectField(fieldHandle);
                    return true;
                }
                place = receiver.ProjectField(fieldHandle);
                return true;

            case IndexExpression index when TryGetPlace(index.Expression, scope, out var indexed):
                place = indexed.ProjectIndex();
                return true;

            case UnaryExpression unary when unary.OperatorToken.Kind == TokenKind.Asterisk:
                // A raw pointer dereference deliberately crosses the safe lifetime boundary.
                // Do not manufacture a tracked place from it; unsafe code owns that risk.
                if (TypeRef.UnwrapDyn(context.GetRequiredExpressionType(unary.Operand)).SpecialType is PointerSemanticType)
                    break;
                if (TryGetPlace(unary.Operand, scope, out var pointer))
                {
                    place = pointer.Dereference();
                    return true;
                }
                break;

            default:
                break;
        }

        place = default;
        return false;
    }

    /// <summary>
    /// Resolves a name to automatic storage, including the special receiver alias <c>this</c>.
    /// </summary>
    /// <remarks>
    /// Local handles are callable-relative and cannot always be recovered through the global
    /// context. The lexical scope fallback preserves the local symbol object used as place identity.
    /// </remarks>
    private bool TryGetStorageSymbol(NamedExpression named, Scope scope, out Symbol storage)
    {
        if (named.Identifier.MatchingKind == MatchingKeywordKind.This && receiverParameter is not null)
        {
            storage = receiverParameter;
            return true;
        }

        if (context.ResolvedTree.TryGetReference(named, out var handle) && context.GetSymbol(handle) is { } resolved && IsStorageSymbol(resolved))
        {
            storage = resolved;
            return true;
        }

        // Local handles are function-relative, so ResolveContext deliberately cannot look them up
        // globally.  Resolve again through the lexical scope to retain the actual symbol identity.
        var part = new SymbolPart(named.Identifier, named is GenericNameExpression generic ? generic.GenericArguments.Count : 0, context.GetHygiene(named));
        storage = scope[new SymbolName(part)].FirstOrDefault(IsStorageSymbol)!;
        return storage is not null;
    }

    // Field/global initialization belongs to construction and module initialization analyses.
    // This body-local pass owns only automatic locals and parameters; fields are represented as
    // projections once their receiver is itself a tracked place.
    private static bool IsStorageSymbol(Symbol symbol) => symbol is LocalVariableSymbol or ParameterSymbol;

    /// <summary>
    /// Selects Copy, Move, or Read for a place evaluation using ownership classification.
    /// </summary>
    /// <remarks>
    /// A pointer/reference/alias receiver is read-only from the ownership pass's perspective:
    /// consuming receiver semantics require an explicit future lowering representation.
    /// </remarks>
    private OwnershipUseKind GetUseKind(Expression expression, Place place)
    {
        // `this` is a source alias to the implicit/explicit receiver storage.  It is never
        // silently consumed merely because expression resolution presents its underlying Self
        // type; a consuming receiver will be represented explicitly in a later lowering slice.
        if (ReferenceEquals(place.Root, receiverParameter) && receiverParameter is not null && TypeRef.UnwrapDyn(receiverParameter.Type).SpecialType is AliasSemanticType or ReferenceSemanticType or PointerSemanticType)
            return OwnershipUseKind.Read;

        TypeRef type = context.GetRequiredExpressionType(expression);
        return ownership.Classify(type) == OwnershipKind.Copy ? OwnershipUseKind.Copy : OwnershipUseKind.Move;
    }

    private void BeginScope() => scopes.Push([]);

    /// <summary>Closes the innermost lexical scope and emits reverse-declaration storage deaths.</summary>
    private void EndScope()
    {
        if (scopes.Count == 0)
            return;

        foreach (var local in scopes.Pop().AsEnumerable().Reverse())
            Emit(OwnershipStatementKind.StorageDead, new Place(local), GetSymbolSpan(local), GetSymbolSource(local));
    }

    /// <summary>
    /// Emits cleanup boundaries for every currently open scope before a return terminator.
    /// </summary>
    /// <remarks>The scopes remain on the lowering stack because unreachable source still needs a CFG block.</remarks>
    private void ExitAllScopes()
    {
        foreach (var frame in scopes)
        {
            foreach (var local in frame.AsEnumerable().Reverse())
                Emit(OwnershipStatementKind.StorageDead, new Place(local), GetSymbolSpan(local), GetSymbolSource(local));
        }
    }

    private static TextSpan GetSymbolSpan(Symbol symbol) => symbol switch
    {
        LocalVariableSymbol local => local.Syntax?.GetSpan() ?? default,
        ParameterSymbol parameter => parameter.Syntax?.GetSpan() ?? default,
        _ => default
    };

    private static SourceText? GetSymbolSource(Symbol symbol) => symbol switch
    {
        LocalVariableSymbol local => local.Syntax?.GetSource(),
        ParameterSymbol parameter => parameter.Syntax?.GetSource(),
        _ => null
    };

    /// <summary>
    /// Appends one source-attributed storage effect to the current ownership block.
    /// </summary>
    /// <remarks>
    /// <paramref name="origin"/> is retained for escape analysis; <paramref name="loanOwner"/>
    /// distinguishes a named loan from an ephemeral call borrow.
    /// </remarks>
    private void Emit(OwnershipStatementKind kind, Place place, TextSpan span, SourceText? source, OwnershipUseKind? useKind = null, BorrowKind? borrowKind = null, Symbol? loanOwner = null, bool conditional = false, SyntaxNode? origin = null, bool isReturnUse = false) =>
        current.Statements.Add(new OwnershipStatement(kind, place, span, source, useKind, borrowKind, loanOwner, conditional, origin, isReturnUse));

    private void ConnectFallthroughToExit(OwnershipBlock block) => ConnectFallthrough(block, body.Exit);

    /// <summary>Connects an unfinished block without overwriting an explicit branch or return.</summary>
    private static void ConnectFallthrough(OwnershipBlock from, OwnershipBlock to)
    {
        if (from.Terminator.Kind != OwnershipTerminatorKind.Unreachable)
            return;
        from.Terminator = OwnershipTerminator.Fallthrough(to);
        from.AddSuccessor(to);
    }
}
