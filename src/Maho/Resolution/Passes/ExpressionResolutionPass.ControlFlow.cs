using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

internal sealed partial class ExpressionResolutionPass
{
    /// <summary>
    /// Resolves an <c>if</c> conditional expression.
    /// <para>
    /// Validates condition expression (<c>MH2006</c> if not boolean).
    /// Maho enforces strict exact branch matching without implicit widening: both branches must evaluate
    /// to the exact same concrete type or compatible literal type, emitting <c>MH2011</c> if they diverge.
    /// </para>
    /// </summary>
    /// <param name="conditional">The if-expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The common branch type, <see cref="TypeRef.Void"/> (if no else branch), or <see cref="TypeRef.Error"/>.</returns>
    private TypeRef ResolveIf(IfExpression conditional, Scope scope, SymbolHandle containingFunction)
    {
        Scope ifScope = conditional.Declaration is not null ? context.GetSyntaxScope(conditional, scope) : scope;
        if (conditional.Declaration is not null)
            ResolveVariableDeclaration(conditional.Declaration, ifScope, containingFunction);

        var condType = ResolveExpression(conditional.Condition, ifScope, containingFunction);
        if (!IsBool(condType) && !condType.IsError && !condType.IsUnresolved)
        {
            context.Diagnostics.ReportConditionTypeMismatch(
                conditional.Condition.GetSpan() ?? default,
                condType.ToQualifiedString(context),
                actualLabel: condType.ToLabelString(context, ifScope),
                source: conditional.GetSource());
        }

        var thenType = ResolveExpression(conditional.ThenExpression, ifScope, containingFunction);
        if (conditional.ElseExpression is null)
            return voidType;

        var elseType = ResolveExpression(conditional.ElseExpression.Expression, ifScope, containingFunction);

        if (thenType.IsError || elseType.IsError)
            return TypeRef.Error;
        if (thenType.IsUnresolved || elseType.IsUnresolved)
            return TypeRef.Unresolved;

        // Strict exact matching:
        if (thenType.Equals(elseType))
            return thenType;

        if (thenType.IsLiteral && elseType.IsLiteral &&
            thenType.SpecialType is CompileTimeLiteralType l1 &&
            elseType.SpecialType is CompileTimeLiteralType l2 &&
            l1.LiteralKind == l2.LiteralKind)
        {
            return thenType;
        }

        if (thenType.IsLiteral && !elseType.IsLiteral && elseType.IsAssignableFrom(thenType, context))
            return elseType;

        if (!thenType.IsLiteral && elseType.IsLiteral && thenType.IsAssignableFrom(elseType, context))
            return thenType;

        // Branches mismatch! Do not widen to common base / Object!
        context.Diagnostics.ReportAmbiguousConditionalBranches(
            conditional.GetSpan() ?? default,
            thenType.ToQualifiedString(context),
            elseType.ToQualifiedString(context),
            thenLabel: thenType.ToLabelString(context, ifScope),
            elseLabel: elseType.ToLabelString(context, ifScope),
            source: conditional.GetSource());

        return TypeRef.Error;
    }

    /// <summary>
    /// Resolves a <c>while</c> loop expression and validates that its condition evaluates to a boolean (<c>MH2006</c>).
    /// </summary>
    /// <param name="loop">The while expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns><see cref="TypeRef.Void"/>.</returns>
    private TypeRef ResolveWhile(WhileExpression loop, Scope scope, SymbolHandle containingFunction)
    {
        Scope whileScope = loop.Declaration is not null ? context.GetSyntaxScope(loop, scope) : scope;
        if (loop.Declaration is not null)
            ResolveVariableDeclaration(loop.Declaration, whileScope, containingFunction);

        var condType = ResolveExpression(loop.Condition, whileScope, containingFunction);
        if (!IsBool(condType) && !condType.IsError && !condType.IsUnresolved)
        {
            context.Diagnostics.ReportConditionTypeMismatch(
                loop.Condition.GetSpan() ?? default,
                condType.ToQualifiedString(context),
                actualLabel: condType.ToLabelString(context, whileScope),
                source: loop.GetSource());
        }

        ResolveExpression(loop.Body, whileScope, containingFunction);
        return voidType;
    }

    /// <summary>
    /// Resolves a <c>try-catch-finally</c> expression: resolves the try block, checks catch clause
    /// type filters (ensuring they implement <see cref="KnownIntrinsics.Throwable"/> directly or via reference),
    /// scopes catch variables, and checks finally clause restrictions.
    /// </summary>
    private TypeRef ResolveTry(TryExpression tryExpr, Scope scope, SymbolHandle containingFunction)
    {
        var tryType = ResolveExpression(tryExpr.TryBlock, scope, containingFunction);
        var commonType = tryType;

        foreach (var catchClause in tryExpr.CatchClauses)
        {
            var catchScope = context.GetSyntaxScope(catchClause, scope);
            TypeRef? boundType = null;

            if (catchClause.Type is not null)
            {
                boundType = ResolveTypeSyntax(catchClause.Type, catchScope);
                if (!boundType.Value.IsError && !boundType.Value.IsUnresolved && !boundType.Value.IsSized(context))
                {
                    context.Diagnostics.ReportUnsizedTypeNotAllowed(
                        catchClause.Type.GetSpan() ?? default,
                        boundType.Value.ToDisplayString(context),
                        "as a catch variable",
                        catchClause.GetSource());
                }
                else
                {
                    ValidateCatchType(boundType.Value, catchClause.Type.GetSpan() ?? default, catchScope, catchClause.GetSource());
                }
            }

            if (catchClause.VariableIdentifier is { } varId && varId.Value != "_")
            {
                var varPart = new SymbolPart(varId.Value);
                if (catchScope.SymbolsByName.TryGetValue(varPart, out var symList) && symList.Count > 0 && symList[0] is LocalVariableSymbol local)
                {
                    local.Type = boundType ?? (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Throwable, out var th) ? TypeRef.Resolved(th) : TypeRef.Unresolved);
                }
            }

            currentCatchDepth++;
            try
            {
                var catchType = ResolveExpression(catchClause.Block, catchScope, containingFunction);
                if (commonType.IsNever(context))
                    commonType = catchType;
            }
            finally
            {
                currentCatchDepth--;
            }
        }

        if (tryExpr.FinallyClause is { } finallyClause)
        {
            inFinallyBlock = true;
            try
            {
                ResolveExpression(finallyClause.Block, scope, containingFunction);
            }
            finally
            {
                inFinallyBlock = false;
            }
        }

        return commonType;
    }

    private void ValidateCatchType(TypeRef catchType, TextSpan span, Scope scope, SourceText? source)
    {
        if (catchType.Kind == TypeRefKind.Error)
            return;

        // Catch type can be:
        // 1. Concrete struct: must implement Throwable
        // 2. Reference / view: &T, mut &T, dyn &T, T&, dyn T&, Reference<T>, where T implements Throwable
        TypeRef pointee = catchType;
        bool changed = true;
        while (changed)
        {
            changed = false;
            if (pointee.SpecialType is ReferenceSemanticType r)
            {
                pointee = r.ElementType;
                changed = true;
            }
            else if (pointee.SpecialType is AliasSemanticType a)
            {
                pointee = a.TargetType;
                changed = true;
            }
            else if (pointee.SpecialType is DynSemanticType d)
            {
                pointee = d.TargetType;
                changed = true;
            }
            else if (pointee.SpecialType is GenericSemanticType gen &&
                     context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Reference, out var refH) &&
                     gen.TargetHandle == refH)
            {
                pointee = gen.TypeArguments.Count > 0 ? gen.TypeArguments[0] : TypeRef.Unresolved;
                changed = true;
            }
        }

        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Throwable, out var throwableHandle))
        {
            var throwableType = TypeRef.Resolved(throwableHandle);
            if (!throwableType.IsAssignableFrom(pointee, context))
            {
                context.Diagnostics.ReportTypeNotThrowable(
                    span,
                    catchType.ToQualifiedString(context),
                    catchType.ToLabelString(context, scope),
                    source);
            }
        }
    }

    /// <summary>
    /// Resolves a block expression: enters a block scope, resolves local statements sequentially,
    /// evaluates the final tail expression (if any), and triggers <see cref="CollapseRemainingLocals"/>
    /// for any local literals left unconstrained at the end of the block.
    /// </summary>
    /// <param name="block">The block expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The semantic type of the final expression, or <see cref="TypeRef.Void"/> if none.</returns>
    private TypeRef ResolveBlock(BlockExpression block, Scope scope, SymbolHandle containingFunction)
    {
        var blockScope = context.GetSyntaxScope(block, scope);
        var expectedReturn = GetFunctionReturnType(containingFunction);
        foreach (var local in block.Locals)
            ResolveLocal(local, blockScope, containingFunction, expectedReturn);

        TypeRef result = voidType;
        if (block.FinalExpression is { } finalExpr)
            result = ResolveExpression(finalExpr, blockScope, containingFunction);

        CollapseRemainingLocals(blockScope, containingFunction);

        return result;
    }

    /// <summary>
    /// Resolves a lambda expression, performing bidirectional type inference against expected function/delegate types,
    /// tracking captured symbols (locals, parameters, 'this'), and validating block body return semantics.
    /// </summary>
    /// <param name="lambda">The lambda expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>A <see cref="TypeRef"/> representing a Function or Delegate type.</returns>
    private TypeRef ResolveLambda(LambdaExpression lambda, Scope scope, SymbolHandle containingFunction)
    {
        IReadOnlyList<TypeRef>? contextualParamTypes = null;
        TypeRef? contextualReturnType = null;

        if (expectedExpressionTypes.TryGetValue(lambda, out var expectedType))
            TryExtractCallableExpectation(expectedType, out contextualParamTypes, out contextualReturnType);

        var lambdaScope = context.GetSyntaxScope(lambda, scope);
        var paramTypes = new List<TypeRef>(lambda.Parameters.Count);

        for (int i = 0; i < lambda.Parameters.Count; i++)
        {
            var pSyntax = lambda.Parameters[i];
            TypeRef pType;

            var paramPart = ResolutionContext.GetSymbolName(pSyntax.Identifier).Last;

            if (pSyntax.Type is not null)
            {
                pType = ResolveTypeSyntax(pSyntax.Type, scope);
            }
            else if (contextualParamTypes is not null && i < contextualParamTypes.Count)
            {
                pType = contextualParamTypes[i];
            }
            else
            {
                var span = pSyntax.Identifier.GetSpan() ?? default;
                context.Diagnostics.ReportCannotInferLambdaParameterType(span, paramPart.Text, pSyntax.Identifier.GetSource());
                pType = TypeRef.Error;
            }

            paramTypes.Add(pType);
            var paramSymbol = lambdaScope[paramPart].OfType<ParameterSymbol>().FirstOrDefault()
                ?? context.CreateParameterSymbol(lambdaScope, paramPart, containingFunction, null);
            paramSymbol.Type = pType;
        }

        TypeRef? explicitReturnType = null;
        if (lambda.ReturnType is not null)
        {
            explicitReturnType = ResolveTypeSyntax(lambda.ReturnType, lambdaScope);
        }

        TypeRef targetReturnType = explicitReturnType ?? contextualReturnType ?? TypeRef.Inferred;

        var captures = new HashSet<Symbol>();
        lambdaCapturedSymbols.Push(captures);
        activeLambdaScopes.Push(lambdaScope);

        TypeRef resolvedReturnType = voidType;

        try
        {
            if (lambda.Body is BlockExpression block)
            {
                if (block.FinalExpression is not null)
                {
                    var span = block.FinalExpression.GetSpan() ?? block.CloseBrace.Span;
                    context.Diagnostics.ReportLambdaBlockBodyTrailingExpression(span, block.CloseBrace.Source);
                }

                var blockScope = context.GetSyntaxScope(block, lambdaScope);
                var lambdaReturnList = new List<TypeRef>();
                lambdaReturnTypes.Push(lambdaReturnList);

                try
                {
                    foreach (var local in block.Locals)
                    {
                        ResolveLocal(local, blockScope, containingFunction, targetReturnType);
                    }
                }
                finally
                {
                    lambdaReturnTypes.Pop();
                }

                CollapseRemainingLocals(blockScope, containingFunction);

                if (explicitReturnType is { } expRet)
                {
                    resolvedReturnType = expRet;
                }
                else if (contextualReturnType is { } ctxRet && !ctxRet.IsInferred && !ctxRet.IsUnresolved)
                {
                    resolvedReturnType = ctxRet;
                }
                else if (lambdaReturnList.Count > 0)
                {
                    resolvedReturnType = lambdaReturnList[0];
                }
                else
                {
                    resolvedReturnType = voidType;
                }
            }
            else
            {
                // Expression body
                if (!targetReturnType.IsInferred && !targetReturnType.IsUnresolved && !targetReturnType.IsError)
                {
                    expectedExpressionTypes[lambda.Body] = targetReturnType;
                }

                var bodyType = ResolveExpression(lambda.Body, lambdaScope, containingFunction);

                if (explicitReturnType is { } expRet)
                {
                    if (bodyType.IsError || bodyType.Kind == TypeRefKind.Error || expRet.IsError || expRet.Kind == TypeRefKind.Error)
                        return TypeRef.Error;

                    if (bodyType.IsLiteral)
                        bodyType = CollapseExpression(lambda.Body, expRet, lambdaScope, containingFunction);

                    if (!expRet.IsAssignableFrom(bodyType, context) &&
                        !TryResolveImplicitCast(bodyType, expRet, out _))
                    {
                        var span = lambda.Body.GetSpan() ?? lambda.EqualsGreaterThanToken.Span;
                        context.Diagnostics.ReportReturnTypeMismatch(
                            span,
                            bodyType.ToQualifiedString(context),
                            expRet.ToQualifiedString(context),
                            exprLabel: bodyType.ToLabelString(context, lambdaScope),
                            returnLabel: expRet.ToLabelString(context, lambdaScope),
                            source: lambda.EqualsGreaterThanToken.Source);
                    }
                    resolvedReturnType = expRet;
                }
                else if (contextualReturnType is { } ctxRet && !ctxRet.IsInferred && !ctxRet.IsUnresolved)
                {
                    if (bodyType.IsLiteral)
                        bodyType = CollapseExpression(lambda.Body, ctxRet, lambdaScope, containingFunction);

                    resolvedReturnType = ctxRet;
                }
                else
                {
                    resolvedReturnType = bodyType;
                }
            }
        }
        finally
        {
            activeLambdaScopes.Pop();
            lambdaCapturedSymbols.Pop();
        }

        var captureInfos = new List<ResolvedTree.LambdaCaptureInfo>(captures.Count);
        foreach (var cap in captures)
        {
            TypeRef capType = GetTypeFromSymbol(cap);
            bool isMove = IsSymbolMovedInLambda(lambda, cap, capType);
            bool isMutable = !isMove && IsSymbolMutablyCaptured(lambda.Body, cap);
            captureInfos.Add(new ResolvedTree.LambdaCaptureInfo(cap, isMutable, isMove));
        }
        context.ResolvedTree.SetLambdaCaptures(lambda, captureInfos);
        context.ResolvedTree.AddResolvedLambda(lambda, lambdaScope, containingFunction);

        if (captureInfos.Count > 0)
            return SynthesizeLambdaClosureType(lambda, lambdaScope, resolvedReturnType, paramTypes, captureInfos);

        return TypeRef.Function(resolvedReturnType, paramTypes);
    }

    private bool IsSymbolMutablyCaptured(SyntaxNode root, Symbol targetSym)
    {
        var targetHandle = ResolutionContext.GetHandle(targetSym);
        bool IsRootTarget(Expression? expr) => expr switch
        {
            NamedExpression ne when context.ResolvedTree.TryGetReference(ne, out var h) =>
                h == targetHandle || (ne.Identifier.MatchingKind is MatchingKeywordKind.This && h == targetHandle),
            MemberAccessExpression mae => IsRootTarget(mae.Expression),
            IndexExpression ie => IsRootTarget(ie.Expression),
            ParenthesizedExpression pe => IsRootTarget(pe.Expression),
            _ => false
        };

        var stack = new Stack<SyntaxNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is LambdaExpression)
            {
                if (!ReferenceEquals(node, root) &&
                    context.ResolvedTree.TryGetLambdaCaptures((LambdaExpression)node, out var nestedCaps) &&
                    nestedCaps.Any(c => ReferenceEquals(c.Symbol, targetSym) && c.IsMutable))
                {
                    return true;
                }
                if (!ReferenceEquals(node, root))
                    continue;
            }

            switch (node)
            {
                case AssignmentExpression assign when IsRootTarget(assign.LhsExpression):
                    return true;
                case UnaryExpression { OperatorToken.Kind: TokenKind.PlusPlus or TokenKind.MinusMinus } unary when IsRootTarget(unary.Operand):
                    return true;
                case VariableDeclaration decl when decl.Declarator.Initializer?.Initializer is { } initExpr && IsRootTarget(initExpr):
                    if (decl.Modifiers.Any(m => m.MatchingKind == MatchingKeywordKind.Mut) ||
                        decl.Type is PrefixModifiedType { Modifier: SafeAliasTypeModifier { IsMutable: true } } ||
                        decl.Type is ModifiedType { Modifier: ReferenceTypeModifier { IsMutable: true } })
                        return true;
                    break;
                case CallExpression call:
                    if (call.Callee is MemberAccessExpression calMae &&
                        IsRootTarget(calMae.Expression) &&
                        context.ResolvedTree.TryGetReference(calMae, out var mH) &&
                        context.GetMethodSymbol(mH) is { Parameters.Count: > 0 } calleeMethod &&
                        context.GetParameterSymbol(calleeMethod.Parameters[0]) is { ReceiverKind: ReceiverKind.MutAlias or ReceiverKind.MutReference })
                    {
                        return true;
                    }
                    if (context.ResolvedTree.TryGetReference(call.Callee, out var fnH))
                    {
                        IReadOnlyList<SymbolHandle> pHandles = context.GetSymbol(fnH) switch
                        {
                            FunctionSymbol fs => fs.Parameters,
                            MethodSymbol ms => ms.Parameters,
                            _ => []
                        };
                        int argOffset = (context.GetSymbol(fnH) is MethodSymbol mSym && !mSym.Flags.HasFlag(FunctionFlags.Static) &&
                                         pHandles.Count > 0 && context.GetParameterSymbol(pHandles[0])?.IsReceiver == true &&
                                         call.Callee is MemberAccessExpression maCall && context.ResolvedTree.IsBoundMethodCall(maCall))
                            ? 1
                            : (context.GetSymbol(fnH) is MethodSymbol mSym2 && !mSym2.Flags.HasFlag(FunctionFlags.Static) &&
                               pHandles.Count > 0 && context.GetParameterSymbol(pHandles[0])?.IsReceiver == true &&
                               call.Callee is IdentifierNameExpression or NamedExpression)
                                ? 1
                                : 0;
                        for (int i = 0; i < call.Arguments.Count && i + argOffset < pHandles.Count; i++)
                        {
                            if (IsRootTarget(call.Arguments[i]) &&
                                context.GetParameterSymbol(pHandles[i + argOffset]) is { } pSym &&
                                TypeRef.UnwrapDyn(pSym.Type).SpecialType is AliasSemanticType { IsMutable: true } or ReferenceSemanticType { IsMutable: true })
                            {
                                return true;
                            }
                        }
                    }
                    break;
            }

            foreach (var (_, child) in SyntaxSpan.GetChildren(node))
                stack.Push(child);
        }
        return false;
    }

    /// <summary>
    /// Retrieves the declared return type of the enclosing function, method, or property.
    /// </summary>
    /// <param name="containingFunction">Symbol handle to the enclosing symbol.</param>
    /// <returns>The declared return type, or <see cref="TypeRef.Void"/> if undefined.</returns>
    private TypeRef GetFunctionReturnType(SymbolHandle containingFunction)
    {
        var sym = context.GetSymbol(containingFunction);
        return sym switch
        {
            FunctionSymbol fn => fn.ReturnType,
            MethodSymbol m => m.ReturnType,
            PropertySymbol p => currentAccessorKind is PropertyAccessorKind.Set ? voidType : p.Type,
            _ => voidType
        };
    }

    /// <summary>
    /// Resolves a <c>match</c> pattern matching expression.
    /// <para>
    /// Evaluates the governing expression, enforces sum type pattern matching safety (prohibiting match on
    /// untagged sum types without an explicit discriminant, <c>MH0606</c>), validates pattern arms against
    /// enum variants or union constituents (<c>MH0607</c>), checks exhaustiveness (<c>MH0605</c>),
    /// and determines the common arm result type.
    /// </para>
    /// </summary>
    /// <param name="match">The match expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The common return type of the match arms, or <see cref="TypeRef.Error"/>.</returns>
    private TypeRef ResolveMatch(MatchExpression match, Scope scope, SymbolHandle containingFunction)
    {
        var governingType = ResolveExpression(match.Expression, scope, containingFunction);

        // 1. Untagged sum types cannot be matched without an external discriminant: MH0606
        bool isUntaggedSumType = false;
        if (governingType.IsUntagged)
            isUntaggedSumType = true;
        else if (governingType.Handle is { } h && context.GetTypeSymbol(h) is { } ts && ts.Flags.HasFlag(TypeFlags.Untagged))
            isUntaggedSumType = true;

        if (isUntaggedSumType)
        {
            context.Diagnostics.ReportUntaggedMatchDisallowed(
                match.Expression.GetSpan() ?? default,
                governingType.ToDisplayString(context),
                match.GetSource());
        }

        ISumTypeSymbol? sumTypeSym = null;
        TypeSymbol? typeSym = null;
        UnionSemanticType? unionType = null;

        if (governingType.SpecialType is UnionSemanticType u)
            unionType = u;
        else if (governingType.Handle is { } gh)
        {
            typeSym = context.GetTypeSymbol(gh);
            sumTypeSym = typeSym as ISumTypeSymbol;
        }

        bool hasWildcard = false;
        var matchedVariants = new HashSet<string>();
        var matchedTypes = new HashSet<TypeRef>();
        TypeRef armResultType = voidType;

        // 2. Validate patterns in each match arm and resolve arm body expressions
        foreach (var arm in match.Arms)
        {
            var armScope = context.GetSyntaxScope(arm, scope);
            ValidatePattern(arm.Pattern, armScope, governingType, sumTypeSym, unionType, ref hasWildcard, matchedVariants, matchedTypes, arm.GetSource());
            var armType = ResolveExpression(arm.Expression, armScope, containingFunction);
            if (armResultType.IsResolved && armResultType == voidType)
                armResultType = armType;
        }

        // 3. Exhaustiveness checking: verify that all variants or constituents are covered
        if (!hasWildcard && !isUntaggedSumType)
        {
            if (sumTypeSym is not null && typeSym?.TypeKind == TypeKind.Enum)
            {
                foreach (var vHandle in sumTypeSym.EnumVariants)
                {
                    if (context.GetEnumVariantSymbol(vHandle) is { } vSym && !matchedVariants.Contains(vSym.Name.Text))
                    {
                        context.Diagnostics.ReportNonExhaustiveMatch(
                            match.MatchKeyword.Span,
                            vSym.Name.Text,
                            match.GetSource());
                        break;
                    }
                }
            }
            else if (unionType is not null)
            {
                foreach (var constituent in unionType.Variants)
                {
                    if (!matchedTypes.Any(t => t.Equals(constituent)))
                    {
                        context.Diagnostics.ReportNonExhaustiveMatch(
                            match.MatchKeyword.Span,
                            constituent.ToDisplayString(context),
                            match.GetSource());
                        break;
                    }
                }
            }
            else if (sumTypeSym is not null && sumTypeSym.UnionConstituents.Count > 0)
            {
                foreach (var constituent in sumTypeSym.UnionConstituents)
                {
                    if (!matchedTypes.Any(t => t.Equals(constituent)))
                    {
                        context.Diagnostics.ReportNonExhaustiveMatch(
                            match.MatchKeyword.Span,
                            constituent.ToDisplayString(context),
                            match.GetSource());
                        break;
                    }
                }
            }
        }

        return armResultType;
    }

    /// <summary>
    /// Validates an individual match arm pattern against the governing sum type or union type.
    /// Emits <c>MH0607</c> if a pattern specifies an invalid or unrecognized variant for the type.
    /// </summary>
    /// <param name="pattern">The pattern syntax node.</param>
    /// <param name="armScope">The lexical scope of the match arm.</param>
    /// <param name="governingType">The type of the matched expression.</param>
    /// <param name="sumTypeSym">The sum type symbol if the governing type is a sum type.</param>
    /// <param name="unionType">The union semantic type if the governing type is an anonymous union.</param>
    /// <param name="hasWildcard">Updated to <see langword="true"/> if a wildcard or catch-all variable pattern is encountered.</param>
    /// <param name="matchedVariants">Set of matched enum variant names.</param>
    /// <param name="matchedTypes">Set of matched constituent types.</param>
    /// <param name="source">The source file context.</param>
    private void ValidatePattern(
        PatternSyntax pattern,
        Scope armScope,
        TypeRef governingType,
        ISumTypeSymbol? sumTypeSym,
        UnionSemanticType? unionType,
        ref bool hasWildcard,
        HashSet<string> matchedVariants,
        HashSet<TypeRef> matchedTypes,
        SourceText? source)
    {
        switch (pattern)
        {
            case WildcardPattern:
            case VariablePattern:
                hasWildcard = true;
                break;

            case EnumVariantPattern variantPat:
                var variantName = context.GetScopedSymbolName(variantPat.Name).Last.Text;
                if (sumTypeSym is not null && sumTypeSym.EnumVariants.Count > 0)
                {
                    bool found = false;
                    foreach (var vHandle in sumTypeSym.EnumVariants)
                    {
                        if (context.GetEnumVariantSymbol(vHandle) is { } v && v.Name.Text == variantName)
                        {
                            found = true;
                            matchedVariants.Add(variantName);
                            break;
                        }
                    }

                    if (!found)
                    {
                        context.Diagnostics.ReportInvalidPatternForSumType(
                            variantPat.GetSpan() ?? default,
                            variantName,
                            (sumTypeSym as Symbol)?.Name.ToString() ?? "Enum",
                            source);
                    }
                }
                else if (unionType is not null || (sumTypeSym is not null && sumTypeSym.UnionConstituents.Count > 0))
                {
                    var constituents = unionType?.Variants ?? sumTypeSym!.UnionConstituents;
                    var matched = constituents.FirstOrDefault(c => c.ToDisplayString(context) == variantName || (c.Handle is { } ch && context.GetTypeSymbol(ch)?.Name.Text == variantName));
                    if (matched != default)
                    {
                        matchedTypes.Add(matched);
                    }
                    else
                    {
                        string name = unionType?.ToDisplayString(context) ?? (sumTypeSym as Symbol)?.Name.ToString() ?? "Union";
                        context.Diagnostics.ReportInvalidPatternForSumType(
                            variantPat.GetSpan() ?? default,
                            variantName,
                            name,
                            source);
                    }
                }
                break;

            case TypePattern typePat:
                var resolvedType = ResolveTypeSyntax(typePat.Type, armScope);
                var constituentsList = unionType?.Variants ?? sumTypeSym?.UnionConstituents;

                if (constituentsList is not null)
                {
                    bool isValid = constituentsList.Any(c => c.Equals(resolvedType) || (c.Handle is { } ch && resolvedType.Handle is { } rh && ch == rh));
                    if (isValid)
                    {
                        matchedTypes.Add(resolvedType);
                    }
                    else
                    {
                        string name = unionType?.ToDisplayString(context) ?? (sumTypeSym as Symbol)?.Name.ToString() ?? "Union";
                        context.Diagnostics.ReportInvalidPatternForSumType(
                            typePat.GetSpan() ?? default,
                            resolvedType.ToDisplayString(context),
                            name,
                            source);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Resolves a constructor call expression (<c>new Type(args...)</c> or <c>new(alloc) Type(args...)</c>).
    /// Resolves optional custom <see cref="OperatorKind.New"/> allocation operators and binds constructor methods (<c>Construct</c>)
    /// on the target type (and any matching extensions), collapsing literal arguments.
    /// </summary>
    private TypeRef ResolveConstructorCall(ConstructorCallExpression constructorCall, Scope scope, SymbolHandle containingFunction)
    {
        var targetType = ResolveTypeSyntax(constructorCall.Type, scope);

        var ctorArgTypes = new List<TypeRef>(constructorCall.Arguments.Count);
        foreach (var arg in constructorCall.Arguments)
            ctorArgTypes.Add(ResolveExpression(arg, scope, containingFunction));

        var allocArgTypes = new List<TypeRef>();
        if (constructorCall.AllocationArguments is { } allocArgs)
        {
            foreach (var a in allocArgs)
                allocArgTypes.Add(ResolveExpression(a, scope, containingFunction));
        }

        // Phase 1: Custom New operator or direct value construction
        ResolveAllocationAndTarget(
            targetType,
            constructorCall.AllocationArguments,
            constructorCall.AllocationTypeArguments,
            allocArgTypes,
            constructorCall,
            scope,
            containingFunction,
            out TypeRef allocResultType,
            out TypeRef ctorTargetType);

        // Phase 2: Constructor method resolution on ctorTargetType (direct members + matching extensions)
        ResolveConstructorMethod(constructorCall, ctorTargetType, ctorArgTypes, scope, containingFunction);

        return allocResultType;
    }

    private void ResolveConstructorMethod(
        ConstructorCallExpression constructorCall,
        TypeRef ctorTargetType,
        List<TypeRef> ctorArgTypes,
        Scope scope,
        SymbolHandle containingFunction)
    {
        if (ctorTargetType.Handle is not { } rawCtorH || context.GetTypeSymbol(context.UnwrapAlias(rawCtorH)) is not { } targetSym)
            return;

        Dictionary<SymbolHandle, TypeRef> targetSubs = BuildGenericSubstitutionMap(ctorTargetType);
        if (TryBindConstructInScope(constructorCall, GetOwnedScope(targetSym), targetSubs, ctorArgTypes, scope, containingFunction))
            return;

        foreach (var ext in context.ExtensionSymbols)
        {
            if (ext.Accessibility == Accessibility.Internal && !context.IsDeclaredInCurrentProject(ResolutionContext.GetHandle(ext)))
                continue;

            var extSubs = new Dictionary<SymbolHandle, TypeRef>();
            if (!TryUnifyExtensionTarget(ext.TargetTypeRef, ctorTargetType, ext.GenericParameters, extSubs))
                continue;

            if (TryBindConstructInScope(constructorCall, GetOwnedScope(ext), extSubs, ctorArgTypes, scope, containingFunction))
                return;
        }
    }

    private bool TryBindConstructInScope(
        ConstructorCallExpression constructorCall,
        Scope memberScope,
        Dictionary<SymbolHandle, TypeRef> ctorSubs,
        List<TypeRef> ctorArgTypes,
        Scope callScope,
        SymbolHandle containingFunction)
    {
        foreach (var sym in memberScope.Symbols.Values)
        {
            if (sym is not MethodSymbol m || m.Name.Text != "Construct")
                continue;

            bool hasReceiver = m.Parameters.Count > 0 &&
                context.GetParameterSymbol(m.Parameters[0])?.Syntax?.Declarator.IsReceiver == true;
            int paramStart = hasReceiver ? 1 : 0;
            if (m.Parameters.Count - paramStart != ctorArgTypes.Count)
                continue;

            bool matches = true;
            for (int i = 0; i < ctorArgTypes.Count; i++)
            {
                if (context.GetParameterSymbol(m.Parameters[i + paramStart]) is { } param)
                {
                    var effParamType = ctorSubs.Count > 0 ? param.Type.Substitute(ctorSubs) : param.Type;
                    if (ctorArgTypes[i].IsLiteral)
                    {
                        if (!IsLiteralCompatibleWith(ctorArgTypes[i], effParamType))
                        {
                            matches = false;
                            break;
                        }
                    }
                    else if (!effParamType.IsAssignableFrom(ctorArgTypes[i], context))
                    {
                        matches = false;
                        break;
                    }
                }
            }
            if (matches)
            {
                for (int i = 0; i < ctorArgTypes.Count; i++)
                {
                    if (ctorArgTypes[i].IsLiteral && context.GetParameterSymbol(m.Parameters[i + paramStart]) is { } param)
                    {
                        var effParamType = ctorSubs.Count > 0 ? param.Type.Substitute(ctorSubs) : param.Type;
                        CollapseExpression(constructorCall.Arguments[i], effParamType, callScope, containingFunction);
                        ctorArgTypes[i] = effParamType;
                    }
                }
                context.ResolvedTree.AddReference(constructorCall, ResolutionContext.GetHandle(m));
                return true;
            }
        }

        return false;
    }

    private void ResolveAllocationAndTarget(
        TypeRef targetType,
        SeparatedSyntaxList<Expression>? allocArgsSyntax,
        SeparatedSyntaxList<TypeSyntax>? allocTypeArgsSyntax,
        List<TypeRef> allocArgTypes,
        Expression creationNode,
        Scope scope,
        SymbolHandle containingFunction,
        out TypeRef allocResultType,
        out TypeRef ctorTargetType)
    {
        MethodSymbol? boundNewOperator = ResolveAllocationOperator(
            targetType,
            allocArgsSyntax,
            allocTypeArgsSyntax,
            allocArgTypes,
            out var allocTypeArgs,
            out var boundAllocReturnType,
            creationNode,
            scope,
            containingFunction);

        allocResultType = ResolveObjectCreationReturnType(
            targetType,
            boundNewOperator,
            boundAllocReturnType,
            creationNode,
            allocArgTypes,
            allocTypeArgs);

        ctorTargetType = targetType;
    }

    private MethodSymbol? ResolveAllocationOperator(
        TypeRef targetType,
        SeparatedSyntaxList<Expression>? allocArgsSyntax,
        SeparatedSyntaxList<TypeSyntax>? allocTypeArgsSyntax,
        List<TypeRef> allocArgTypes,
        out List<TypeRef> allocTypeArgs,
        out TypeRef resolvedReturnType,
        Expression creationNode,
        Scope scope,
        SymbolHandle containingFunction)
    {
        allocTypeArgs = [];
        resolvedReturnType = TypeRef.Unresolved;
        if (allocTypeArgsSyntax is { } typeArgsSyntax)
        {
            foreach (var ta in typeArgsSyntax)
                allocTypeArgs.Add(ResolveTypeSyntax(ta, scope));
        }

        if (targetType.IsError || targetType.IsUnresolved || targetType.Handle is not { } rawTargetH)
            return null;

        if (!context.DiscoveredOperators.TryGetOperators(OperatorKind.New, out var candidates))
            return null;

        var unwrappedH = context.UnwrapAlias(rawTargetH);
        var targetSym = (Symbol?)context.GetTypeSymbol(unwrappedH) ?? context.GetSymbol(unwrappedH);
        if (targetSym is null)
            return null;

        var targetSubs = BuildGenericSubstitutionMap(targetType);
        var candidateScopes = new List<Scope> { GetOwnedScope(targetSym) };
        foreach (var conf in context.Conformances)
        {
            if (conf.TargetType.Handle is { } cth && context.UnwrapAlias(cth) == unwrappedH &&
                context.GetSymbol(conf.OwnerHandle) is ExtensionSymbol ext)
            {
                candidateScopes.Add(GetOwnedScope(ext));
            }
        }

        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null)
                continue;

            bool targetImplementsIface = TypeRef.IsSubtypeOf(unwrappedH, candidate.InterfaceHandle, context) ||
                context.Conformances.Any(c =>
                    c.TargetType.Handle is { } cth &&
                    context.UnwrapAlias(cth) == unwrappedH &&
                    (c.InterfaceHandle == candidate.InterfaceHandle || TypeRef.IsSubtypeOf(c.InterfaceHandle, candidate.InterfaceHandle, context)));

            if (!targetImplementsIface)
                continue;

            var candSubs = BuildInterfaceSubstitutionMap(unwrappedH, targetType, candidate.InterfaceHandle, ifaceSym);

            foreach (var candScope in candidateScopes)
            {
                foreach (var sym in candScope.Symbols.Values)
                {
                    if (sym is not MethodSymbol method)
                        continue;

                    bool isMatch = method.Flags.HasFlag(FunctionFlags.InterfaceScoped)
                        ? method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text)
                        : context.GetMethodSymbol(candidate.MethodHandle) is { } opM &&
                          method.Name.Text == opM.Name.Text;
                    if (!isMatch)
                        continue;

                    if (TryMatchAllocationCandidate(
                        method,
                        targetSubs,
                        candSubs,
                        allocArgsSyntax,
                        allocTypeArgsSyntax,
                        allocArgTypes,
                        allocTypeArgs,
                        creationNode,
                        scope,
                        containingFunction,
                        out resolvedReturnType))
                    {
                        return method;
                    }
                }
            }

            // Check default interface method conformance entry (e.g. Put<T : this = this>.Put() default body)
            if (context.GetMethodSymbol(candidate.MethodHandle) is { } ifaceOpMethod &&
                (!ifaceOpMethod.Flags.HasFlag(FunctionFlags.Intrinsic) && ifaceOpMethod.Syntax?.Body is not null and not FunctionEmptyBody))
            {
                if (TryMatchAllocationCandidate(
                    ifaceOpMethod,
                    targetSubs,
                    candSubs,
                    allocArgsSyntax,
                    allocTypeArgsSyntax,
                    allocArgTypes,
                    allocTypeArgs,
                    creationNode,
                    scope,
                    containingFunction,
                    out resolvedReturnType))
                {
                    return ifaceOpMethod;
                }
            }
        }

        return null;
    }

    private bool TryMatchAllocationCandidate(
        MethodSymbol method,
        Dictionary<SymbolHandle, TypeRef> targetSubs,
        Dictionary<SymbolHandle, TypeRef> ifaceSubs,
        SeparatedSyntaxList<Expression>? allocArgsSyntax,
        SeparatedSyntaxList<TypeSyntax>? allocTypeArgsSyntax,
        List<TypeRef> allocArgTypes,
        List<TypeRef> allocTypeArgs,
        Expression creationNode,
        Scope scope,
        SymbolHandle containingFunction,
        out TypeRef resolvedReturnType)
    {
        resolvedReturnType = TypeRef.Unresolved;

        if (allocTypeArgs.Count != method.GenericParameters.Count)
            return false;

        bool hasReceiver = method.Parameters.Count > 0 &&
            context.GetParameterSymbol(method.Parameters[0])?.Syntax?.Declarator.IsReceiver == true;
        int paramStart = hasReceiver ? 1 : 0;
        if (method.Parameters.Count - paramStart != allocArgTypes.Count)
            return false;

        var mergedSubs = new Dictionary<SymbolHandle, TypeRef>(ifaceSubs);
        foreach (var kvp in targetSubs)
            mergedSubs[kvp.Key] = kvp.Value;

        if (allocTypeArgs.Count > 0)
        {
            for (int g = 0; g < method.GenericParameters.Count; g++)
                mergedSubs[method.GenericParameters[g]] = allocTypeArgs[g];
        }

        for (int i = 0; i < allocArgTypes.Count; i++)
        {
            if (context.GetParameterSymbol(method.Parameters[i + paramStart]) is { } p)
            {
                var expectedParamType = mergedSubs.Count > 0 ? p.Type.Substitute(mergedSubs) : p.Type;
                if (allocArgTypes[i].IsLiteral && allocArgsSyntax is { } aArgsLit)
                {
                    if (!IsLiteralCompatibleWith(allocArgTypes[i], expectedParamType))
                        return false;
                    CollapseExpression(aArgsLit[i], expectedParamType, scope, containingFunction);
                }
                else if (!expectedParamType.IsAssignableFrom(allocArgTypes[i], context) &&
                         !TryResolveImplicitCast(allocArgTypes[i], expectedParamType, out _))
                {
                    return false;
                }
            }
        }

        if (allocTypeArgsSyntax is { Count: > 0 } taSyn && method.GenericParameters.Count > 0)
        {
            context.ValidateGenericTypeArgumentConformances(
                ResolutionContext.GetHandle(method),
                [.. taSyn],
                allocTypeArgs,
                scope,
                containingFunction,
                reportDiagnostics: true);
        }

        var methodHandle = ResolutionContext.GetHandle(method);
        context.ResolvedTree.AddAllocationOperatorReference(creationNode, methodHandle);
        context.ResolvedTree.AddReference(creationNode, methodHandle);
        resolvedReturnType = mergedSubs.Count > 0 ? method.ReturnType.Substitute(mergedSubs) : method.ReturnType;
        return true;
    }

    private Dictionary<SymbolHandle, TypeRef> BuildInterfaceSubstitutionMap(
        SymbolHandle concreteTypeHandle,
        TypeRef concreteType,
        SymbolHandle ifaceHandle,
        TypeSymbol ifaceSym)
    {
        var result = new Dictionary<SymbolHandle, TypeRef>();
        if (ifaceSym.GenericParameters.Count == 0)
            return result;

        var targetSubs = BuildGenericSubstitutionMap(concreteType);
        if (context.GetSymbol(concreteTypeHandle) is TypeSymbol concreteTs)
        {
            foreach (var b in concreteTs.BaseTypes)
            {
                if (b.Handle is { } bh && context.UnwrapAlias(bh) == ifaceHandle)
                {
                    if (b.SpecialType is GenericSemanticType gst)
                    {
                        for (int i = 0; i < ifaceSym.GenericParameters.Count && i < gst.TypeArguments.Count; i++)
                        {
                            var arg = targetSubs.Count > 0 ? gst.TypeArguments[i].Substitute(targetSubs) : gst.TypeArguments[i];
                            result[ifaceSym.GenericParameters[i]] = arg;
                        }
                    }
                }
            }
        }

        foreach (var conf in context.Conformances)
        {
            if (conf.TargetType.Handle is { } cth && context.UnwrapAlias(cth) == concreteTypeHandle &&
                conf.InterfaceHandle == ifaceHandle && conf.InterfaceType.SpecialType is GenericSemanticType cgst)
            {
                for (int i = 0; i < ifaceSym.GenericParameters.Count && i < cgst.TypeArguments.Count; i++)
                {
                    var arg = targetSubs.Count > 0 ? cgst.TypeArguments[i].Substitute(targetSubs) : cgst.TypeArguments[i];
                    result[ifaceSym.GenericParameters[i]] = arg;
                }
            }
        }

        for (int i = 0; i < ifaceSym.GenericParameters.Count; i++)
        {
            var gpH = ifaceSym.GenericParameters[i];
            if (!result.ContainsKey(gpH) && context.GetGenericParameterSymbol(gpH)?.ParameterKind == GenericParameterKind.This)
                result[gpH] = concreteType;
        }

        return result;
    }

    private TypeRef ResolveObjectCreationReturnType(
        TypeRef targetType,
        MethodSymbol? boundNewOperator,
        TypeRef boundAllocReturnType,
        Expression creationNode,
        List<TypeRef> allocArgTypes,
        List<TypeRef> allocTypeArgs)
    {
        if (boundNewOperator is not null)
        {
            return boundAllocReturnType.IsResolved || boundAllocReturnType.IsSpecial ? boundAllocReturnType : boundNewOperator.ReturnType;
        }

        if (targetType.IsError || targetType.IsUnresolved)
            return targetType;

        if (allocArgTypes.Count > 0 || allocTypeArgs.Count > 0)
        {
            context.Diagnostics.ReportNoMatchingAllocationOperator(
                creationNode.GetSpan() ?? default,
                "new",
                targetType.ToDisplayString(context),
                KnownIntrinsics.OpNew,
                creationNode.GetSource());
            return TypeRef.Error;
        }

        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.ValueType, out var valTypeH) &&
            !IsDerivedFromValueType(targetType, valTypeH))
        {
            context.Diagnostics.ReportObjectCreationNotSupported(
                creationNode.GetSpan() ?? default,
                "new",
                targetType.ToDisplayString(context),
                KnownIntrinsics.ValueType,
                KnownIntrinsics.OpNew,
                creationNode.GetSource());
            return TypeRef.Error;
        }

        // `new T(...)` / `new T { ... }` / `new T[N]` constructs `T` directly by value (requiring `Sized`).
        if (!targetType.IsSized(context))
        {
            context.Diagnostics.ReportUnsizedTypeNotAllowed(
                creationNode.GetSpan() ?? default,
                targetType.ToDisplayString(context),
                "in a 'new' expression",
                creationNode.GetSource());
        }

        return targetType;
    }

    private bool IsDerivedFromValueType(TypeRef type, SymbolHandle valTypeH)
    {
        if (type.IsFunction ||
            type.Kind is TypeRefKind.Slice or TypeRefKind.Array or TypeRefKind.Tuple or
            TypeRefKind.Reference or TypeRefKind.Pointer or TypeRefKind.Optional or TypeRefKind.Literal)
            return true;

        if (type.Handle is { } h)
            return TypeRef.IsSubtypeOf(context.UnwrapAlias(h), valTypeH, context);

        return false;
    }

    /// <summary>
    /// Resolves a direct struct initializer (<c>new T { f1 = v1, ... }</c>)
    /// without invoking a constructor, grouping complete piecewise initialization into one statement.
    /// </summary>
    private TypeRef ResolveObjectInitializer(ObjectInitializerExpression objInit, Scope scope, SymbolHandle containingFunction)
    {
        var targetType = ResolveTypeSyntax(objInit.Type, scope);

        var allocArgTypes = new List<TypeRef>();
        if (objInit.AllocationArguments is { } allocArgs)
        {
            foreach (var a in allocArgs)
                allocArgTypes.Add(ResolveExpression(a, scope, containingFunction));
        }

        ResolveAllocationAndTarget(
            targetType,
            objInit.AllocationArguments,
            objInit.AllocationTypeArguments,
            allocArgTypes,
            objInit,
            scope,
            containingFunction,
            out TypeRef allocResultType,
            out TypeRef ctorTargetType);

        var structFields = CollectStructFields(ctorTargetType);
        Dictionary<SymbolHandle, TypeRef> genericSubs = BuildGenericSubstitutionMap(ctorTargetType);
        var initializedFields = new HashSet<SymbolHandle>();

        foreach (var entry in objInit.Initializer.Expressions)
        {
            if (entry is not AssignmentExpression assign || assign.EqualsOperator.Kind != TokenKind.Equals)
            {
                ResolveExpression(entry, scope, containingFunction);
                continue;
            }

            if (assign.LhsExpression is not IdentifierNameExpression fieldIdent)
            {
                ResolveExpression(assign.RhsExpression, scope, containingFunction);
                continue;
            }

            string fieldName = fieldIdent.Identifier.Value;
            FieldSymbol? matchedField = structFields.FirstOrDefault(f => f.Name.Text == fieldName);
            if (matchedField is null)
            {
                if (!targetType.IsError && !targetType.IsUnresolved)
                {
                    context.Diagnostics.ReportMemberNotFound(
                        fieldIdent.Identifier.Span,
                        fieldName,
                        targetType.ToQualifiedString(context),
                        labelTypeName: targetType.ToLabelString(context, scope),
                        source: objInit.GetSource());
                }
                ResolveExpression(assign.RhsExpression, scope, containingFunction);
                continue;
            }

            var fieldHandle = ResolutionContext.GetHandle(matchedField);
            context.ResolvedTree.AddReference(fieldIdent, fieldHandle);

            if (!initializedFields.Add(fieldHandle))
            {
                context.Diagnostics.ReportDuplicateInitializerField(
                    fieldIdent.Identifier.Span,
                    targetType.ToDisplayString(context),
                    fieldName,
                    objInit.GetSource());
            }

            var fieldType = genericSubs.Count > 0 ? matchedField.Type.Substitute(genericSubs) : matchedField.Type;
            if (!fieldType.IsInferred && !fieldType.IsUnresolved && !fieldType.IsError && !fieldType.IsLiteral)
                expectedExpressionTypes[assign.RhsExpression] = fieldType;

            var rhsType = ResolveExpression(assign.RhsExpression, scope, containingFunction);
            context.SetExpressionType(assign.LhsExpression, fieldType);
            context.SetExpressionType(assign, fieldType);

            if (rhsType.IsLiteral && !fieldType.IsError && !fieldType.IsUnresolved)
            {
                var collapsed = CollapseExpression(assign.RhsExpression, fieldType, scope, containingFunction);
                if (collapsed.IsError)
                {
                    context.Diagnostics.ReportAssignmentTypeMismatch(
                        assign.RhsExpression.GetSpan() ?? default,
                        rhsType.ToQualifiedString(context),
                        fieldType.ToQualifiedString(context),
                        rhsLabel: rhsType.ToLabelString(context, scope),
                        lhsLabel: fieldType.ToLabelString(context, scope),
                        source: objInit.GetSource());
                }
            }
            else if (!rhsType.IsError && !rhsType.IsUnresolved && !rhsType.IsInferred &&
                     !fieldType.IsError && !fieldType.IsUnresolved && !fieldType.IsInferred &&
                     !fieldType.IsAssignableFrom(rhsType, context) &&
                     !TryResolveImplicitCast(rhsType, fieldType, out _))
            {
                context.Diagnostics.ReportAssignmentTypeMismatch(
                    assign.RhsExpression.GetSpan() ?? default,
                    rhsType.ToQualifiedString(context),
                    fieldType.ToQualifiedString(context),
                    rhsLabel: rhsType.ToLabelString(context, scope),
                    lhsLabel: fieldType.ToLabelString(context, scope),
                    source: objInit.GetSource());
            }
            else if (!rhsType.IsError && !rhsType.IsUnresolved && !rhsType.IsInferred &&
                     !fieldType.IsError && !fieldType.IsUnresolved && !fieldType.IsInferred)
            {
                ValidateConformanceConversion(rhsType, fieldType, assign.RhsExpression, scope, containingFunction);
            }
        }

        // Verify all fields of ctorTargetType are covered
        if (!ctorTargetType.IsError && !ctorTargetType.IsUnresolved)
        {
            foreach (var field in structFields)
            {
                var fh = ResolutionContext.GetHandle(field);
                if (!initializedFields.Contains(fh))
                {
                    context.Diagnostics.ReportDirectInitializerMissingField(
                        objInit.Initializer.RightBrace.Span,
                        ctorTargetType.ToDisplayString(context),
                        field.Name.Text,
                        objInit.GetSource());
                }
            }
        }

        return allocResultType;
    }

    private List<FieldSymbol> CollectStructFields(TypeRef targetType)
    {
        var result = new List<FieldSymbol>();
        var seen = new HashSet<SymbolHandle>();
        if (targetType.Handle is not { } h)
            return result;

        var unwrapped = context.UnwrapAlias(h);
        var sym = context.GetSymbol(unwrapped);

        void AddFieldsFrom(SymbolHandle th)
        {
            if (!seen.Add(th))
                return;

            IReadOnlyList<SymbolHandle> fieldHandles = context.GetSymbol(th) switch
            {
                ProductTypeSymbol p => p.Fields,
                MemberProductTypeSymbol mp => mp.Fields,
                _ => []
            };
            foreach (var fh in fieldHandles)
            {
                if (context.GetFieldSymbol(fh) is { } fs && !fs.Flags.HasFlag(VariableFlags.Static))
                    result.Add(fs);
            }
        }

        IReadOnlyList<SymbolHandle> hierarchy = sym switch
        {
            TypeSymbol ts => ts.LinearizedBases,
            NestedTypeSymbol nts => nts.LinearizedBases,
            _ => []
        };

        foreach (var bh in hierarchy)
            AddFieldsFrom(context.UnwrapAlias(bh));

        AddFieldsFrom(unwrapped);
        return result;
    }

    private Dictionary<SymbolHandle, TypeRef> BuildGenericSubstitutionMap(TypeRef targetType)
    {
        var subs = new Dictionary<SymbolHandle, TypeRef>();
        if (targetType.SpecialType is GenericSemanticType gst && targetType.Handle is { } h)
        {
            var sym = context.GetSymbol(context.UnwrapAlias(h));
            IReadOnlyList<SymbolHandle> gpHandles = sym switch
            {
                TypeSymbol ts => ts.GenericParameters,
                NestedTypeSymbol nts => nts.GenericParameters,
                _ => []
            };
            for (int i = 0; i < gpHandles.Count && i < gst.TypeArguments.Count; i++)
                subs[gpHandles[i]] = gst.TypeArguments[i];
        }
        return subs;
    }

    private TypeRef ResolveDefaultExpression(DefaultExpression defaultExpr, Scope scope)
    {
        if (defaultExpr.ExplicitType is { } explicitType)
        {
            var resolved = ResolveTypeSyntax(explicitType, scope);
            if (!resolved.IsError && !resolved.IsUnresolved && !resolved.IsSized(context))
            {
                context.Diagnostics.ReportUnsizedTypeNotAllowed(
                    explicitType.GetSpan() ?? default,
                    resolved.ToDisplayString(context),
                    "in a 'default' expression",
                    defaultExpr.GetSource());
            }
            return resolved;
        }

        if (expectedExpressionTypes.TryGetValue(defaultExpr, out var expected) &&
            !expected.IsInferred && !expected.IsUnresolved && !expected.IsError && !expected.IsLiteral)
        {
            if (!expected.IsSized(context))
            {
                context.Diagnostics.ReportUnsizedTypeNotAllowed(
                    defaultExpr.DefaultKeyword.Span,
                    expected.ToDisplayString(context),
                    "in a 'default' expression",
                    defaultExpr.GetSource());
            }
            return expected;
        }

        context.Diagnostics.ReportCannotInferType(defaultExpr.DefaultKeyword.Span, "default", defaultExpr.GetSource());
        return TypeRef.Error;
    }

    /// <summary>
    /// Resolves an explicit type-cast expression (<c>&lt;expr as TargetType&gt;</c>).
    /// </summary>
    /// <param name="typeAs">The type-as expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <returns>The resolved target type.</returns>
    private TypeRef ResolveTypeAs(TypeAsExpression typeAs, Scope scope)
    {
        context.TryPrebindConformanceInterface(typeAs.InterfaceType, scope);
        ResolveTypeSyntax(typeAs.InterfaceType, scope, isTraitBoundContext: true);
        return ResolveTypeSyntax(typeAs.TargetType, scope);
    }

    private static bool IsNonIndirectionTypeSyntax(TypeSyntax syntax)
    {
        while (syntax is GroupedType g)
            syntax = g.InnerType;

        return syntax is SimpleType or QualifiedType or GenericType or ScopeQualifiedType;
    }

    /// <summary>
    /// Resolves a cast expression (<c>expr as TargetType</c>).
    /// </summary>
    /// <param name="asExpr">The as-expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <returns>The resolved target type.</returns>
    private TypeRef ResolveAs(AsExpression asExpr, Scope scope, SymbolHandle containingFunction)
    {
        context.TryPrebindConformanceInterface(asExpr.Type, scope);
        var exprType = ResolveExpression(asExpr.Expression, scope, containingFunction);

        if (IsNonIndirectionTypeSyntax(asExpr.Type) && !exprType.IsError && !exprType.IsUnresolved)
        {
            var probeTarget = ResolveTypeSyntax(asExpr.Type, scope, reportDiagnostics: false, isTraitBoundContext: true);
            if (probeTarget.Handle is { } ph && IsInterfaceSymbol(context, ph))
            {
                var conf = ValidateConformanceConversion(
                    exprType,
                    probeTarget,
                    asExpr,
                    scope,
                    containingFunction,
                    ResolutionContext.ExtractModuleQualifier(asExpr.Type));
                if (conf is not null)
                    return TypeRef.Conformance(exprType, conf);
            }
        }

        var targetType = ResolveTypeSyntax(asExpr.Type, scope);

        if (!exprType.IsError && !exprType.IsUnresolved && !targetType.IsError && !targetType.IsUnresolved &&
            TryResolveCastOperator(OperatorKind.ExplicitCast, exprType, targetType, out var explicitCastMethod))
        {
            if (explicitCastMethod is not null)
                context.ResolvedTree.AddReference(asExpr, ResolutionContext.GetHandle(explicitCastMethod));
            return targetType;
        }

        if (TypeRef.IsNonContiguousSplitBase(targetType, exprType, context, out var splitBaseName))
        {
            context.Diagnostics.ReportNonContiguousBaseConversion(
                asExpr.GetSpan() ?? default,
                splitBaseName!,
                asExpr.GetSource());
        }
        else
        {
            ValidateConformanceConversion(
                exprType,
                targetType,
                asExpr,
                scope,
                containingFunction,
                ResolutionContext.ExtractModuleQualifier(asExpr.Type));
        }

        return targetType;
    }

    /// <summary>
    /// Resolves a type syntax node to a semantic <see cref="TypeRef"/> by querying the lexical scope.
    /// </summary>
    private TypeRef ResolveTypeSyntax(TypeSyntax syntax, Scope scope, bool reportDiagnostics = true, bool isTraitBoundContext = false, bool isDynTargetContext = false, bool allowAlias = false)
    {
        if (syntax is GroupedType grouped)
        {
            return ResolveTypeSyntax(grouped.InnerType, scope, reportDiagnostics, isTraitBoundContext, isDynTargetContext, allowAlias);
        }

        if (syntax is DerivedGenericArgumentSyntax derivedArg)
        {
            if (reportDiagnostics)
            {
                context.Diagnostics.ReportInheritanceOrOverrideError(
                    derivedArg.GetSpan() ?? derivedArg.DerivedKeyword.Span,
                    "'derived' generic type argument is only allowed in a base interface's generic arguments on a struct, class, enum, or union",
                    derivedArg.GetSource());
            }
            return ResolveTypeSyntax(derivedArg.TargetType, scope, reportDiagnostics, isTraitBoundContext, isDynTargetContext, allowAlias);
        }

        if (syntax is LiteralGenericArgument litArg)
        {
            var litKind = litArg.Expression.Literal.Kind switch
            {
                TokenKind.Float => CompileTimeLiteralKind.Float,
                TokenKind.String => CompileTimeLiteralKind.Str,
                TokenKind.Char => CompileTimeLiteralKind.Char,
                _ => CompileTimeLiteralKind.Int
            };
            return TypeRef.Literal(litKind, litArg.Expression.Literal.Value);
        }

        if (syntax is FunctionTypeSyntax fnSyntax)
        {
            TypeRef ret = fnSyntax.ArrowToken is not null
                ? ResolveTypeSyntax(fnSyntax.ReturnType, scope, reportDiagnostics)
                : TypeRef.EmptyTuple;
            var paramTypes = new List<TypeRef>(fnSyntax.ParameterTypes.Count);
            foreach (var p in fnSyntax.ParameterTypes)
                paramTypes.Add(ResolveTypeSyntax(p, scope, reportDiagnostics));
            return TypeRef.Function(ret, paramTypes);
        }

        if (syntax is UnionType union)
        {
            var variants = new List<TypeRef>(union.Variants.Count);
            foreach (var v in union.Variants)
            {
                var vt = ResolveTypeSyntax(v, scope, reportDiagnostics);
                if (reportDiagnostics && !vt.IsError && !vt.IsSized(context))
                    context.Diagnostics.ReportUnsizedTypeNotAllowed(v.GetSpan() ?? default, vt.ToDisplayString(context), "as a union variant", v.GetSource());
                variants.Add(vt);
            }
            return TypeRef.Union(variants, isUntagged: false);
        }

        if (syntax is TupleType tuple)
        {
            if (tuple.Elements.Count == 0)
                return TypeRef.EmptyTuple;

            var elements = new List<TupleElement>(tuple.Elements.Count);
            foreach (var elem in tuple.Elements)
                elements.Add(new TupleElement(ResolveTypeSyntax(elem.Type, scope, reportDiagnostics), elem.Name?.Value));
            context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var tupleHandle);
            return TypeRef.Tuple(elements, tupleHandle != default ? tupleHandle : null);
        }

        if (syntax is ModifiedType modified)
        {
            if (modified.Modifier is PointerTypeModifier ptrMod)
            {
                if (CheckAndReportInvalidDynIndirection(modified.Type, modified, reportDiagnostics))
                    return TypeRef.Error;
                var innerPtr = ResolveTypeSyntax(modified.Type, scope, reportDiagnostics, isDynTargetContext: isDynTargetContext);
                return TypeRef.Pointer(innerPtr, ptrMod.IsMutable);
            }

            if (modified.Modifier is ReferenceTypeModifier refMod)
            {
                if (CheckAndReportInvalidDynIndirection(modified.Type, modified, reportDiagnostics))
                    return TypeRef.Error;
                var innerRef = ResolveTypeSyntax(modified.Type, scope, reportDiagnostics, isDynTargetContext: isDynTargetContext);
                return TypeRef.Reference(innerRef, refMod.IsMutable);
            }

            var inner = ResolveTypeSyntax(modified.Type, scope, reportDiagnostics);
            if (modified.Modifier is OptionalTypeModifier)
            {
                if (reportDiagnostics && !inner.IsError && !inner.IsSized(context))
                    context.Diagnostics.ReportGenericConstraintNotSized(modified.Type.GetSpan() ?? default, inner.ToDisplayString(context), "T", KnownIntrinsics.Optional, "Sized", modified.GetSource());
                context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var optH);
                return TypeRef.Optional(inner, optH != default ? optH : null);
            }

            if (modified.Modifier is SliceTypeModifier sliceMod)
            {
                if (reportDiagnostics && !inner.IsError && !inner.IsSized(context))
                    context.Diagnostics.ReportGenericConstraintNotSized(modified.Type.GetSpan() ?? default, inner.ToDisplayString(context), "T", KnownIntrinsics.Slice, "Sized", modified.GetSource());
                context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Slice, out var sliceH);
                return TypeRef.Slice(inner, sliceMod.Dimension, sliceH != default ? sliceH : null);
            }

            if (modified.Modifier is ArrayTypeModifier arrMod)
            {
                if (reportDiagnostics && !inner.IsError && !inner.IsSized(context))
                    context.Diagnostics.ReportGenericConstraintNotSized(modified.Type.GetSpan() ?? default, inner.ToDisplayString(context), "T", KnownIntrinsics.Array, "Sized", modified.GetSource());
                context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var arrH);
                return TypeRef.Array(inner, arrMod.Sizes.ToList(), arrH != default ? arrH : null);
            }

            return TypeRef.Error;
        }

        if (syntax is PrefixModifiedType prefixModified)
        {
            if (prefixModified.Modifier is DynTypeModifier)
            {
                var inner = ResolveTypeSyntax(prefixModified.Type, scope, reportDiagnostics, isDynTargetContext: true, allowAlias: allowAlias);
                return TypeRef.CreateValidatedDynType(inner, prefixModified, context, reportDiagnostics);
            }
            if (prefixModified.Modifier is UntaggedTypeModifier)
                return TypeRef.Untagged(ResolveTypeSyntax(prefixModified.Type, scope, reportDiagnostics));
            if (prefixModified.Modifier is SafeAliasTypeModifier safeMod)
            {
                if (!allowAlias)
                {
                    if (reportDiagnostics)
                        context.Diagnostics.ReportAliasNotAllowedOutsideLocal(prefixModified.GetSpan() ?? default, "outside a local variable declaration", prefixModified.GetSource());
                    return TypeRef.Error;
                }
                if (CheckAndReportInvalidDynIndirection(prefixModified.Type, prefixModified, reportDiagnostics))
                    return TypeRef.Error;
                var inner = ResolveTypeSyntax(prefixModified.Type, scope, reportDiagnostics, isDynTargetContext: isDynTargetContext);
                return TypeRef.Alias(inner, isMutable: safeMod.IsMutable, isUnsafe: false);
            }
            if (prefixModified.Modifier is UnsafeAliasTypeModifier unsafeMod)
            {
                if (!allowAlias)
                {
                    if (reportDiagnostics)
                        context.Diagnostics.ReportAliasNotAllowedOutsideLocal(prefixModified.GetSpan() ?? default, "outside a local variable declaration", prefixModified.GetSource());
                    return TypeRef.Error;
                }
                if (CheckAndReportInvalidDynIndirection(prefixModified.Type, prefixModified, reportDiagnostics))
                    return TypeRef.Error;
                var inner = ResolveTypeSyntax(prefixModified.Type, scope, reportDiagnostics, isDynTargetContext: isDynTargetContext);
                return TypeRef.Alias(inner, isMutable: unsafeMod.IsMutable, isUnsafe: true);
            }
            return TypeRef.Error;
        }

        if (syntax is ConformanceQualifiedType confQualified)
        {
            return context.ResolveConformanceQualifiedType(
                confQualified,
                scope,
                default,
                (ts, sc) => ResolveTypeSyntax(ts, sc, reportDiagnostics, isTraitBoundContext: true),
                (ts, sc) => ResolveTypeSyntax(ts, sc, reportDiagnostics, isTraitBoundContext, isDynTargetContext),
                reportDiagnostics);
        }

        if (syntax is ScopeQualifiedType scopeQualified)
        {
            return ResolveScopeQualifiedTypeSyntax(scopeQualified, scope, reportDiagnostics, isTraitBoundContext, isDynTargetContext);
        }

        if (syntax is GenericType generic)
        {
            return ResolveNamedTypeSyntax(generic, generic.Name.Value, generic.GenericArguments, scope, reportDiagnostics: reportDiagnostics, isTraitBoundContext: isTraitBoundContext, isDynTargetContext: isDynTargetContext);
        }

        if (syntax is QualifiedType qualified && qualified.Right is GenericType qualifiedGeneric)
        {
            return ResolveNamedTypeSyntax(syntax, qualifiedGeneric.Name.Value, qualifiedGeneric.GenericArguments, scope, qualifiedGeneric, reportDiagnostics, isTraitBoundContext, isDynTargetContext);
        }

        var name = ResolutionContext.GetSymbolName(syntax);
        var syms = scope.ResolveType(name);
        if (syms.Count > 0)
        {
            var handle = ResolutionContext.GetHandle(syms[0]);
            context.ResolvedTree.AddReference(syntax, handle);
            var res = TypeRef.Resolved(handle);
            if (HasUninstantiatedThisGenericParameter(handle, out var missingParamName))
            {
                if (reportDiagnostics)
                    context.Diagnostics.ReportMissingExplicitThisTypeArgument(syntax.GetSpan() ?? default, context.GetSymbol(handle)?.Name.Text ?? res.ToDisplayString(context), missingParamName, syntax.GetSource());
                return TypeRef.Error;
            }
            return res;
        }

        return TypeRef.Unresolved;
    }

    private bool CheckAndReportInvalidDynIndirection(TypeSyntax innerSyntax, TypeSyntax outerSyntax, bool reportDiagnostics)
    {
        while (innerSyntax is GroupedType g)
            innerSyntax = g.InnerType;

        if (innerSyntax is PrefixModifiedType { Modifier: DynTypeModifier } innerDyn)
        {
            if (reportDiagnostics)
            {
                var innerName = innerDyn.Type.ToString() ?? "T";
                context.Diagnostics.ReportInvalidInterfaceOrDynUsage(
                    outerSyntax.GetSpan() ?? default,
                    "indirection modifier cannot wrap 'dyn'; write 'dyn' outside the reference, alias, or pointer",
                    "apply 'dyn' outside '&' or '*'",
                    $"write 'dyn &{innerName}' (or 'dyn {innerName}&' / 'dyn {innerName}*') instead",
                    outerSyntax.GetSource());
            }
            return true;
        }

        return false;
    }

    private TypeRef ResolveNamedTypeSyntax(
        TypeSyntax syntax,
        string leafName,
        SeparatedSyntaxList<TypeSyntax> genericArgSyntax,
        Scope scope,
        GenericType? innerGeneric = null,
        bool reportDiagnostics = true,
        bool isTraitBoundContext = false,
        bool isDynTargetContext = false)
    {
        var name = ResolutionContext.GetSymbolName(syntax);
        var syms = scope.ResolveType(name);
        if (syms.Count == 0)
            return TypeRef.Unresolved;

        var handle = ResolutionContext.GetHandle(syms[0]);
        context.ResolvedTree.AddReference(syntax, handle);
        if (innerGeneric is not null)
            context.ResolvedTree.AddReference(innerGeneric, handle);

        return InstantiateNamedTypeFromHandle(handle, leafName, genericArgSyntax, scope, syntax, reportDiagnostics, isTraitBoundContext, isDynTargetContext);
    }

    private TypeRef ResolveScopeQualifiedTypeSyntax(ScopeQualifiedType scopeQualified, Scope scope, bool reportDiagnostics = true, bool isTraitBoundContext = false, bool isDynTargetContext = false)
    {
        if (context.ResolvedTree.TryGetReference(scopeQualified, out var existingH))
        {
            GenericType? cachedGen = FindLeafGenericType(scopeQualified.MemberType);
            if (cachedGen is not null)
                return InstantiateNamedTypeFromHandle(existingH, cachedGen.Name.Value, cachedGen.GenericArguments, scope, scopeQualified, reportDiagnostics, isTraitBoundContext, isDynTargetContext);

            return TypeRef.Resolved(existingH);
        }

        var memberPath = ResolutionContext.GetSymbolName(scopeQualified.MemberType);
        var syms = context.ResolveScopedSymbols(
            scopeQualified.Qualifier,
            memberPath,
            scope,
            typesOnly: true,
            scopeQualified.GetSpan() ?? default,
            scopeQualified.GetSource(),
            reportAmbiguity: false,
            out _);

        if (syms.Count == 0)
            return TypeRef.Unresolved;

        var handle = ResolutionContext.GetHandle(syms[0]);
        context.ResolvedTree.AddReference(scopeQualified, handle);
        context.ResolvedTree.AddReference(scopeQualified.MemberType, handle);

        GenericType? leafGeneric = FindLeafGenericType(scopeQualified.MemberType);
        if (leafGeneric is not null)
        {
            context.ResolvedTree.AddReference(leafGeneric, handle);
            return InstantiateNamedTypeFromHandle(handle, leafGeneric.Name.Value, leafGeneric.GenericArguments, scope, scopeQualified, reportDiagnostics, isTraitBoundContext, isDynTargetContext);
        }

        return TypeRef.Resolved(handle);
    }

    private TypeRef InstantiateNamedTypeFromHandle(
        SymbolHandle handle,
        string leafName,
        SeparatedSyntaxList<TypeSyntax> genericArgSyntax,
        Scope scope,
        TypeSyntax? diagnosticSyntax = null,
        bool reportDiagnostics = true,
        bool isTraitBoundContext = false,
        bool isDynTargetContext = false)
    {
        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var optH) &&
            handle == optH &&
            genericArgSyntax.Count == 1)
        {
            var optElem = ResolveTypeSyntax(genericArgSyntax[0], scope, reportDiagnostics);
            if (reportDiagnostics && !optElem.IsError && !optElem.IsSized(context))
                context.Diagnostics.ReportGenericConstraintNotSized(genericArgSyntax[0].GetSpan() ?? default, optElem.ToDisplayString(context), "T", KnownIntrinsics.Optional, "Sized", diagnosticSyntax?.GetSource());
            return TypeRef.Optional(optElem, optH);
        }

        var typeArgs = new List<TypeRef>(genericArgSyntax.Count);
        foreach (var arg in genericArgSyntax)
            typeArgs.Add(ResolveTypeSyntax(arg, scope, reportDiagnostics));

        var genericParams = context.GetSymbol(handle) switch
        {
            TypeSymbol ts => ts.GenericParameters,
            NestedTypeSymbol nts => nts.GenericParameters,
            _ => []
        };
        if (typeArgs.Count < genericParams.Count)
        {
            for (int i = typeArgs.Count; i < genericParams.Count; i++)
            {
                var gp = context.GetGenericParameterSymbol(genericParams[i]);
                if (gp?.DefaultType is { } def && def.IsResolved)
                    typeArgs.Add(def);
                else
                    break;
            }
        }

        if (genericParams.Count == 0 || typeArgs.Count > 0)
            context.ValidateGenericTypeArgumentConformances(handle, genericArgSyntax.ToList(), typeArgs, scope, default, reportDiagnostics);

        var unwrapped = context.UnwrapAlias(handle);
        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Slice, out var sliceH) && unwrapped == sliceH && typeArgs.Count >= 1)
        {
            int dim = 1;
            if (genericArgSyntax.Count >= 2 && genericArgSyntax[1] is LiteralGenericArgument lit && int.TryParse(lit.Expression.Literal.Value, out int parsedDim) && parsedDim >= 1)
                dim = parsedDim;
            return TypeRef.Slice(typeArgs[0], dim, sliceH);
        }

        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var arrH) && unwrapped == arrH && typeArgs.Count >= 2 && genericArgSyntax.Count >= 2 && genericArgSyntax[1] is LiteralGenericArgument litSize)
        {
            var sizes = new List<Expression> { litSize.Expression };
            for (int i = 2; i < genericArgSyntax.Count; i++)
            {
                if (genericArgSyntax[i] is LiteralGenericArgument l)
                    sizes.Add(l.Expression);
            }
            return TypeRef.Array(typeArgs[0], sizes, arrH);
        }

        return TypeRef.Generic(handle, leafName, typeArgs);
    }

    private static GenericType? FindLeafGenericType(TypeSyntax syntax) => syntax switch
    {
        GenericType gt => gt,
        QualifiedType qt => FindLeafGenericType(qt.Right),
        _ => null
    };

    private bool HasUninstantiatedThisGenericParameter(SymbolHandle handle, out string paramName)
    {
        paramName = string.Empty;
        IReadOnlyList<SymbolHandle> gps = context.GetSymbol(handle) switch
        {
            TypeSymbol ts => ts.GenericParameters,
            NestedTypeSymbol nts => nts.GenericParameters,
            _ => []
        };
        if (gps.Count == 0)
            return false;
        foreach (var gpH in gps)
        {
            if (context.GetGenericParameterSymbol(gpH) is { ParameterKind: GenericParameterKind.This } gp)
            {
                paramName = gp.Name.Text;
                return true;
            }
        }
        return false;
    }

    private static bool IsInterfaceSymbol(ResolutionContext context, SymbolHandle handle)
    {
        var sym = context.GetSymbol(context.UnwrapAlias(handle));
        return sym switch
        {
            TypeSymbol ts => ts.TypeKind == TypeKind.Interface,
            NestedTypeSymbol nts => nts.TypeKind == TypeKind.Interface,
            _ => false
        };
    }

}
