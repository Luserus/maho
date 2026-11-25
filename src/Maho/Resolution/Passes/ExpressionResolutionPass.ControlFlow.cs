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
                ValidateCatchType(boundType.Value, catchClause.Type.GetSpan() ?? default, catchScope, catchClause.GetSource());
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
        FunctionSemanticType? expectedFn = null;
        DelegateSemanticType? expectedDel = null;

        if (expectedExpressionTypes.TryGetValue(lambda, out var expectedType))
        {
            if (expectedType.SpecialType is FunctionSemanticType f)
                expectedFn = f;
            else if (expectedType.SpecialType is DelegateSemanticType d)
                expectedDel = d;
        }

        IReadOnlyList<TypeRef>? contextualParamTypes = expectedFn?.ParameterTypes ?? expectedDel?.ParameterTypes;
        TypeRef? contextualReturnType = expectedFn?.ReturnType ?? expectedDel?.ReturnType;

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

        // Non-capturing lambdas yield Function; capturing lambdas yield Delegate
        if (captures.Count > 0 || expectedDel is not null)
        {
            return TypeRef.Delegate(resolvedReturnType, paramTypes);
        }

        return TypeRef.Function(resolvedReturnType, paramTypes);
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
    /// Resolves an object creation expression implementing Maho's two-phase allocation and construction model.
    /// <para>
    /// In Maho, allocation and construction are strictly separated:
    /// 1. Phase 1 (Allocation): resolves the <see cref="OperatorKind.Put"/> operator for placement allocation
    ///    (<c>put&lt;arena&gt; Type(...)</c>) or the <see cref="OperatorKind.New"/> operator for heap allocation
    ///    (<c>new&lt;allocator&gt; Type(...)</c>), eagerly collapsing any literal allocation arguments.
    /// 2. Phase 2 (Construction): resolves constructor methods (<c>Construct</c> or <c>.ctor</c>) on the target type,
    ///    matching argument counts and types and collapsing literal arguments.
    /// </para>
    /// </summary>
    /// <param name="constructorCall">The constructor call syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The constructed <see cref="TypeRef"/>, or <see cref="TypeRef.Void"/> for placement <c>put</c>.</returns>
    private TypeRef ResolveConstructorCall(ConstructorCallExpression constructorCall, Scope scope, SymbolHandle containingFunction)
    {
        var isPut = constructorCall.Kind == ObjectCreationKind.Put;
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

        // Phase 1: Allocation operator resolution (Put vs New)
        bool hasAlloc = allocArgTypes.Count > 0 || constructorCall.AllocationTypeArguments is not null;
        MethodSymbol? boundNewOperator = ResolveAllocationOperator(
            isPut,
            constructorCall.AllocationArguments,
            constructorCall.AllocationTypeArguments,
            allocArgTypes,
            constructorCall,
            scope,
            containingFunction);

        // Phase 2: Constructor method resolution on targetType
        if (targetType.Handle is { } targetHandle && context.GetTypeSymbol(targetHandle) is { } targetSym)
        {
            var targetScope = GetOwnedScope(targetSym);
            foreach (var sym in targetScope.Symbols.Values)
            {
                if (sym is MethodSymbol m && m.Name.Text == "Construct")
                {
                    if (m.Parameters.Count == ctorArgTypes.Count)
                    {
                        bool matches = true;
                        for (int i = 0; i < ctorArgTypes.Count; i++)
                        {
                            if (context.GetParameterSymbol(m.Parameters[i]) is { } param)
                            {
                                if (ctorArgTypes[i].IsLiteral)
                                {
                                    if (!IsLiteralCompatibleWith(ctorArgTypes[i], param.Type))
                                    {
                                        matches = false;
                                        break;
                                    }
                                }
                                else if (!param.Type.IsAssignableFrom(ctorArgTypes[i], context))
                                {
                                    matches = false;
                                    break;
                                }
                            }
                        }
                        if (matches)
                        {
                            // Collapse literal arguments to the matched parameter types
                            for (int i = 0; i < ctorArgTypes.Count; i++)
                            {
                                if (ctorArgTypes[i].IsLiteral && context.GetParameterSymbol(m.Parameters[i]) is { } param)
                                {
                                    CollapseExpression(constructorCall.Arguments[i], param.Type, scope, containingFunction);
                                    ctorArgTypes[i] = param.Type;
                                }
                            }
                            context.ResolvedTree.AddReference(constructorCall, ResolutionContext.GetHandle(m));
                            break;
                        }
                    }
                }
            }
        }

        return ResolveObjectCreationReturnType(isPut, hasAlloc, targetType, boundNewOperator, constructorCall);
    }

    private MethodSymbol? ResolveAllocationOperator(
        bool isPut,
        SeparatedSyntaxList<Expression>? allocArgsSyntax,
        SeparatedSyntaxList<TypeSyntax>? allocTypeArgsSyntax,
        List<TypeRef> allocArgTypes,
        Expression creationNode,
        Scope scope,
        SymbolHandle containingFunction)
    {
        if (isPut)
        {
            if (context.DiscoveredOperators.TryGetOperators(OperatorKind.Put, out var putCandidates))
            {
                foreach (var candidate in putCandidates)
                {
                    if (context.GetMethodSymbol(candidate.MethodHandle) is { } putMethod)
                    {
                        if (allocArgsSyntax is { } aArgs)
                        {
                            for (int i = 0; i < aArgs.Count && i < putMethod.Parameters.Count; i++)
                            {
                                if (allocArgTypes[i].IsLiteral && context.GetParameterSymbol(putMethod.Parameters[i]) is { } p)
                                    CollapseExpression(aArgs[i], p.Type, scope, containingFunction);
                            }
                        }
                        context.ResolvedTree.AddReference(creationNode, candidate.MethodHandle);
                        break;
                    }
                }
            }
            return null;
        }

        if (allocArgTypes.Count > 0 || allocTypeArgsSyntax is not null)
        {
            if (context.DiscoveredOperators.TryGetOperators(OperatorKind.New, out var newCandidates))
            {
                foreach (var candidate in newCandidates)
                {
                    if (context.GetMethodSymbol(candidate.MethodHandle) is { } newMethod)
                    {
                        if (allocArgsSyntax is { } aArgs)
                        {
                            for (int i = 0; i < aArgs.Count && i < newMethod.Parameters.Count; i++)
                            {
                                if (allocArgTypes[i].IsLiteral && context.GetParameterSymbol(newMethod.Parameters[i]) is { } p)
                                    CollapseExpression(aArgs[i], p.Type, scope, containingFunction);
                            }
                        }
                        context.ResolvedTree.AddReference(creationNode, candidate.MethodHandle);
                        return newMethod;
                    }
                }
            }
        }

        return null;
    }

    private TypeRef ResolveObjectCreationReturnType(
        bool isPut,
        bool hasAllocArgs,
        TypeRef targetType,
        MethodSymbol? boundNewOperator,
        Expression creationNode)
    {
        // Placement `put(dest) T(...)` constructs in-place and returns `()`;
        // value/stack `put T(...)` or `put T { ... }` evaluates to `targetType`.
        if (isPut)
            return hasAllocArgs ? voidType : targetType;

        if (boundNewOperator is not null)
            return boundNewOperator.ReturnType;

        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Box, out var boxHandle))
        {
            var boxName = context.GetTypeSymbol(boxHandle)?.Name.Text ?? KnownIntrinsics.Box;
            return TypeRef.Generic(boxHandle, boxName, [targetType]);
        }

        if (context.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null && !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Box))
        {
            context.Diagnostics.ReportMissingIntrinsicForSyntax(creationNode.GetSpan() ?? default, "new expression", KnownIntrinsics.Box, creationNode.GetSource());
        }

        return targetType;
    }

    /// <summary>
    /// Resolves a direct struct initializer (<c>put T { f1 = v1, ... }</c> or <c>new T { ... }</c>)
    /// without invoking a constructor, grouping complete piecewise initialization into one statement.
    /// </summary>
    private TypeRef ResolveObjectInitializer(ObjectInitializerExpression objInit, Scope scope, SymbolHandle containingFunction)
    {
        bool isPut = objInit.Kind == ObjectCreationKind.Put;
        var targetType = ResolveTypeSyntax(objInit.Type, scope);

        var allocArgTypes = new List<TypeRef>();
        if (objInit.AllocationArguments is { } allocArgs)
        {
            foreach (var a in allocArgs)
                allocArgTypes.Add(ResolveExpression(a, scope, containingFunction));
        }

        bool hasAlloc = allocArgTypes.Count > 0 || objInit.AllocationTypeArguments is not null;
        MethodSymbol? boundNewOperator = ResolveAllocationOperator(
            isPut,
            objInit.AllocationArguments,
            objInit.AllocationTypeArguments,
            allocArgTypes,
            objInit,
            scope,
            containingFunction);

        var structFields = CollectStructFields(targetType);
        Dictionary<SymbolHandle, TypeRef> genericSubs = BuildGenericSubstitutionMap(targetType);
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

        // Verify all fields of targetType are covered
        if (!targetType.IsError && !targetType.IsUnresolved)
        {
            foreach (var field in structFields)
            {
                var fh = ResolutionContext.GetHandle(field);
                if (!initializedFields.Contains(fh))
                {
                    context.Diagnostics.ReportDirectInitializerMissingField(
                        objInit.Initializer.RightBrace.Span,
                        targetType.ToDisplayString(context),
                        field.Name.Text,
                        objInit.GetSource());
                }
            }
        }

        return ResolveObjectCreationReturnType(isPut, hasAlloc, targetType, boundNewOperator, objInit);
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
                if (context.GetFieldSymbol(fh) is { } fs)
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
            return ResolveTypeSyntax(explicitType, scope);

        if (expectedExpressionTypes.TryGetValue(defaultExpr, out var expected) &&
            !expected.IsInferred && !expected.IsUnresolved && !expected.IsError && !expected.IsLiteral)
        {
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
    private TypeRef ResolveTypeAs(TypeAsExpression typeAs, Scope scope) =>
        ResolveTypeSyntax(typeAs.TargetType, scope);

    /// <summary>
    /// Resolves a cast expression (<c>expr as TargetType</c>).
    /// </summary>
    /// <param name="asExpr">The as-expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <returns>The resolved target type.</returns>
    private TypeRef ResolveAs(AsExpression asExpr, Scope scope, SymbolHandle containingFunction)
    {
        context.TryPrebindConformanceInterface(asExpr.Type, scope);
        var targetType = ResolveTypeSyntax(asExpr.Type, scope);
        var exprType = ResolveExpression(asExpr.Expression, scope, containingFunction);

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
    /// <param name="syntax">The type syntax node to resolve.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <returns>The resolved <see cref="TypeRef"/>, or <see cref="TypeRef.Unresolved"/> if not found.</returns>
    private TypeRef ResolveTypeSyntax(TypeSyntax syntax, Scope scope)
    {
        if (syntax is TupleType tuple)
        {
            if (tuple.Elements.Count == 0)
                return TypeRef.EmptyTuple;

            var elements = new List<TupleElement>(tuple.Elements.Count);
            foreach (var elem in tuple.Elements)
                elements.Add(new TupleElement(ResolveTypeSyntax(elem.Type, scope), elem.Name?.Value));
            context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var tupleHandle);
            return TypeRef.Tuple(elements, tupleHandle != default ? tupleHandle : null);
        }

        if (syntax is ModifiedType modified)
        {
            var inner = ResolveTypeSyntax(modified.Type, scope);
            return modified.Modifier switch
            {
                PointerTypeModifier => TypeRef.Pointer(inner),
                ReferenceTypeModifier => TypeRef.Reference(inner),
                OptionalTypeModifier => TypeRef.Optional(inner),
                ArrayTypeModifier arrMod => TypeRef.Array(inner, arrMod.Dimension),
                _ => TypeRef.Error
            };
        }

        if (syntax is PrefixModifiedType prefixModified)
        {
            var inner = ResolveTypeSyntax(prefixModified.Type, scope);
            if (prefixModified.Modifier is DynTypeModifier)
                return TypeRef.Dyn(inner, isExplicitDyn: true);
            if (prefixModified.Modifier is UntaggedTypeModifier)
                return TypeRef.Untagged(inner);
            if (prefixModified.Modifier is SafeAliasTypeModifier safeMod)
                return TypeRef.Alias(inner, isMutable: safeMod.IsMutable, isUnsafe: false);
            if (prefixModified.Modifier is UnsafeAliasTypeModifier unsafeMod)
                return TypeRef.Alias(inner, isMutable: unsafeMod.IsMutable, isUnsafe: true);
            return TypeRef.Error;
        }

        if (syntax is ConformanceQualifiedType confQualified)
        {
            return context.ResolveConformanceQualifiedType(
                confQualified,
                scope,
                default,
                ResolveTypeSyntax,
                ResolveTypeSyntax,
                reportDiagnostics: true);
        }

        if (syntax is ScopeQualifiedType scopeQualified)
        {
            return ResolveScopeQualifiedTypeSyntax(scopeQualified, scope);
        }

        if (syntax is GenericType generic)
        {
            return ResolveNamedTypeSyntax(generic, generic.Name.Value, generic.GenericArguments, scope);
        }

        if (syntax is QualifiedType qualified && qualified.Right is GenericType qualifiedGeneric)
        {
            return ResolveNamedTypeSyntax(syntax, qualifiedGeneric.Name.Value, qualifiedGeneric.GenericArguments, scope, qualifiedGeneric);
        }

        var name = ResolutionContext.GetSymbolName(syntax);
        var syms = scope.ResolveType(name);
        if (syms.Count > 0)
        {
            var handle = ResolutionContext.GetHandle(syms[0]);
            context.ResolvedTree.AddReference(syntax, handle);
            var res = TypeRef.Resolved(handle);
            if (IsInterfaceSymbol(context, handle))
                return TypeRef.Dyn(res);
            return res;
        }

        return TypeRef.Unresolved;
    }

    private TypeRef ResolveNamedTypeSyntax(
        TypeSyntax syntax,
        string leafName,
        SeparatedSyntaxList<TypeSyntax> genericArgSyntax,
        Scope scope,
        GenericType? innerGeneric = null)
    {
        var name = ResolutionContext.GetSymbolName(syntax);
        var syms = scope.ResolveType(name);
        if (syms.Count == 0)
            return TypeRef.Unresolved;

        var handle = ResolutionContext.GetHandle(syms[0]);
        context.ResolvedTree.AddReference(syntax, handle);
        if (innerGeneric is not null)
            context.ResolvedTree.AddReference(innerGeneric, handle);

        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var optH) &&
            handle == optH &&
            genericArgSyntax.Count == 1)
        {
            return TypeRef.Optional(ResolveTypeSyntax(genericArgSyntax[0], scope), optH);
        }

        var typeArgs = new List<TypeRef>(genericArgSyntax.Count);
        foreach (var arg in genericArgSyntax)
            typeArgs.Add(ResolveTypeSyntax(arg, scope));

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

        var gen = TypeRef.Generic(handle, leafName, typeArgs);
        if (IsInterfaceSymbol(context, handle))
            return TypeRef.Dyn(gen);
        return gen;
    }

    private TypeRef ResolveScopeQualifiedTypeSyntax(ScopeQualifiedType scopeQualified, Scope scope)
    {
        if (context.ResolvedTree.TryGetReference(scopeQualified, out var existingH))
        {
            GenericType? cachedGen = FindLeafGenericType(scopeQualified.MemberType);
            if (cachedGen is not null)
                return InstantiateNamedTypeFromHandle(existingH, cachedGen.Name.Value, cachedGen.GenericArguments, scope);

            var existingRes = TypeRef.Resolved(existingH);
            return IsInterfaceSymbol(context, existingH) ? TypeRef.Dyn(existingRes) : existingRes;
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
            return InstantiateNamedTypeFromHandle(handle, leafGeneric.Name.Value, leafGeneric.GenericArguments, scope);
        }

        var res = TypeRef.Resolved(handle);
        return IsInterfaceSymbol(context, handle) ? TypeRef.Dyn(res) : res;
    }

    private TypeRef InstantiateNamedTypeFromHandle(
        SymbolHandle handle,
        string leafName,
        SeparatedSyntaxList<TypeSyntax> genericArgSyntax,
        Scope scope)
    {
        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var optH) &&
            handle == optH &&
            genericArgSyntax.Count == 1)
        {
            return TypeRef.Optional(ResolveTypeSyntax(genericArgSyntax[0], scope), optH);
        }

        var typeArgs = new List<TypeRef>(genericArgSyntax.Count);
        foreach (var arg in genericArgSyntax)
            typeArgs.Add(ResolveTypeSyntax(arg, scope));

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

        var gen = TypeRef.Generic(handle, leafName, typeArgs);
        return IsInterfaceSymbol(context, handle) ? TypeRef.Dyn(gen) : gen;
    }

    private static GenericType? FindLeafGenericType(TypeSyntax syntax) => syntax switch
    {
        GenericType gt => gt,
        QualifiedType qt => FindLeafGenericType(qt.Right),
        _ => null
    };

    private static bool IsInterfaceSymbol(ResolutionContext context, SymbolHandle handle)
    {
        var sym = context.GetSymbol(handle);
        return sym switch
        {
            TypeSymbol ts => ts.TypeKind == TypeKind.Interface,
            NestedTypeSymbol nts => nts.TypeKind == TypeKind.Interface,
            _ => false
        };
    }

}
