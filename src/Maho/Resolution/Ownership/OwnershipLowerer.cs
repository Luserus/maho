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
    // A safe alias/reference local does not own a new object.  Keep its provenance while
    // lowering so a reborrow (`&T second = first`) remains a loan of the original place rather
    // than a loan of the first handle's stack slot.
    private readonly Dictionary<Symbol, Place> knownReferents = [];
    private readonly Stack<List<Symbol>> scopes = [];
    private OwnershipBody body = null!;
    private OwnershipBlock current = null!;
    private ParameterSymbol? receiverParameter;
    private int nextBlockId;
    private int nextTemporaryId;

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
        labels.Clear();
        knownReferents.Clear();
        scopes.Clear();
        receiverParameter = null;
        nextBlockId = 0;

        var entry = NewBlock();
        var exit = NewBlock();
        body = new OwnershipBody(containingSymbol, entry, exit);
        body.Blocks.Add(entry);
        body.Blocks.Add(exit);
        current = entry;

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

            if (parameter.Syntax?.Declarator.ReceiverKind is not ReceiverKind.None)
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
                else
                    LowerValue(expression.Expression, scope);
                break;

            case LocalYieldStatement yieldStmt:
                if (yieldStmt.Expression is not null)
                    LowerValue(yieldStmt.Expression, scope);
                break;

            case LocalReturnStatement returned:
                if (returned.Statement.Expression is { } value)
                    LowerValue(value, scope);
                ExitAllScopes();
                current.Terminator = OwnershipTerminator.Return(returned.Statement.Expression);
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
                    borrowed = ResolveKnownReferent(borrowed);
                    // The binding is initialized before its named loan becomes live.  This keeps
                    // holder liveness precise: the loan begins at the declaration's completed
                    // program point and ends after the holder's last use.
                    Emit(OwnershipStatementKind.Assign, new Place(local), declaration.Declarator.Identifier.GetSpan() ?? default, declaration.GetSource(), origin: declaration);
                    Emit(OwnershipStatementKind.Borrow, borrowed, initializer.GetSpan() ?? default, initializer.GetSource(), borrowKind: borrowKind, loanOwner: local, origin: initializer);
                    knownReferents[local] = borrowed;
                }
                else
                {
                    LowerValueForDestination(initializer, local.Type, scope);
                    Emit(OwnershipStatementKind.Assign, new Place(local), declaration.Declarator.Identifier.GetSpan() ?? default, declaration.GetSource(), origin: declaration);
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

        if (targetType.SpecialType is AliasSemanticType { IsUnsafe: false } alias)
        {
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

        if (targetType.SpecialType is ReferenceSemanticType reference && initializer is UnaryExpression { OperatorToken.Kind: TokenKind.Ampersand } address)
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

    private static bool IsTemporaryExpression(Expression expr)
    {
        while (expr is ParenthesizedExpression p)
            expr = p.Expression;

        return expr is LiteralExpression or BinaryExpression or CallExpression or TupleExpression or ObjectCreationExpression;
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
        while (seen.Add(place.Root) && knownReferents.TryGetValue(place.Root, out var referent))
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
        LowerValue(expression.Collection, scope);

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
        if (expression.VariableExpression is { } varExpr && TryGetPlace(varExpr, scope, out var place))
        {
            Emit(OwnershipStatementKind.Assign, place, varExpr.GetSpan() ?? default, varExpr.GetSource(), origin: varExpr);
        }

        LowerBranchExpression(expression.Body, forScope);
        ConnectFallthrough(current, header);
        current = exit;
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
                bool mutable = context.GetRequiredExpressionType(unary).SpecialType is ReferenceSemanticType reference && reference.IsMutable;
                if (TryGetPlace(unary.Operand, scope, out var borrowed))
                {
                    Emit(OwnershipStatementKind.Borrow, borrowed, unary.GetSpan() ?? default, unary.GetSource(), borrowKind: mutable ? BorrowKind.Mutable : BorrowKind.Shared, origin: unary);
                }
                else
                {
                    var operandType = context.GetRequiredExpressionType(unary.Operand);
                    var tempPlace = CreateTemporaryPlace(unary.Operand, operandType, scope);
                    Emit(OwnershipStatementKind.StorageLive, tempPlace, unary.GetSpan() ?? default, unary.GetSource(), origin: unary);
                    LowerValue(unary.Operand, scope);
                    Emit(OwnershipStatementKind.Assign, tempPlace, unary.GetSpan() ?? default, unary.GetSource(), origin: unary);
                    Emit(OwnershipStatementKind.Borrow, tempPlace, unary.GetSpan() ?? default, unary.GetSource(), borrowKind: mutable ? BorrowKind.Mutable : BorrowKind.Shared, origin: unary);
                    Emit(OwnershipStatementKind.StorageDead, tempPlace, unary.GetSpan() ?? default, unary.GetSource(), origin: unary);
                }
                break;

            case UnaryExpression unary:
                LowerValue(unary.Operand, scope);
                break;

            case BinaryExpression binary:
                LowerValue(binary.LeftExpression, scope);
                LowerValue(binary.RightExpression, scope);
                break;

            case CallExpression call:
                LowerCall(call, scope);
                break;

            case MemberAccessExpression member:
                LowerPlaceEvaluation(member.Expression, scope);
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
    private void LowerValueForDestination(Expression expression, TypeRef destination, Scope scope)
    {
        if (IsOwnedValueDestination(destination) && TryGetPlace(expression, scope, out var place))
        {
            place = ResolveKnownReferent(place);
            TypeRef sourceType = context.GetRequiredExpressionType(expression);
            bool isAliasMaterialization = sourceType.SpecialType is AliasSemanticType or ReferenceSemanticType;
            OwnershipUseKind useKind;
            if (isAliasMaterialization)
            {
                // Alias-to-value syntax is always a copy of the referent, never a hidden move.
                // A move-only referent therefore needs a focused diagnostic rather than silently
                // consuming storage that the alias promises to keep referring to.
                useKind = OwnershipUseKind.Copy;
                if (ownership.Classify(destination) == OwnershipKind.Move)
                    context.Diagnostics.ReportCannotCopyMoveOnlyAlias(expression.GetSpan() ?? default, place.Root.Name.Text, expression.GetSource());
            }
            else
            {
                useKind = ownership.Classify(destination) == OwnershipKind.Copy
                    ? OwnershipUseKind.Copy
                    : OwnershipUseKind.Move;
            }
            Emit(OwnershipStatementKind.Use, place, expression.GetSpan() ?? default, expression.GetSource(), useKind, origin: expression);
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
    private static bool IsOwnedValueDestination(TypeRef type) => type.SpecialType is not (AliasSemanticType or ReferenceSemanticType or PointerSemanticType);

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
            Emit(OwnershipStatementKind.Assign, target, assignment.LhsExpression.GetSpan() ?? default, assignment.GetSource(), origin: assignment);
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
        if (call.Callee is MemberAccessExpression member)
            LowerPlaceEvaluation(member.Expression, scope);
        else if (call.Callee is not NamedExpression)
            LowerValue(call.Callee, scope);

        for (int index = 0; index < call.Arguments.Count; index++)
        {
            var argument = call.Arguments[index];
            var parameter = index < parameters.Count ? parameters[index] : null;
            if (parameter is not null && IsSafeBorrow(parameter.Type))
            {
                bool mutable = parameter.Type.SpecialType switch
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
            }
        }
    }

    private List<ParameterSymbol> GetCallParameters(CallExpression call)
    {
        if (!context.ResolvedTree.TryGetReference(call.Callee, out var handle))
            return [];

        IReadOnlyList<SymbolHandle> handles = context.GetSymbol(handle) switch
        {
            FunctionSymbol function => function.Parameters,
            MethodSymbol method => method.Parameters,
            _ => []
        };
        return handles.Select(context.GetParameterSymbol).OfType<ParameterSymbol>().ToList();
    }

    private static bool IsSafeBorrow(TypeRef type) => type.SpecialType is ReferenceSemanticType or AliasSemanticType { IsUnsafe: false };

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
                if (context.GetRequiredExpressionType(unary.Operand).SpecialType is PointerSemanticType)
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
        if (ReferenceEquals(place.Root, receiverParameter) && receiverParameter?.Type.SpecialType is AliasSemanticType or ReferenceSemanticType or PointerSemanticType)
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
    private void Emit(OwnershipStatementKind kind, Place place, TextSpan span, SourceText? source, OwnershipUseKind? useKind = null, BorrowKind? borrowKind = null, Symbol? loanOwner = null, bool conditional = false, SyntaxNode? origin = null) =>
        current.Statements.Add(new OwnershipStatement(kind, place, span, source, useKind, borrowKind, loanOwner, conditional, origin));

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
