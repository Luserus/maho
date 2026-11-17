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
        if (isPut)
        {
            if (context.DiscoveredOperators.TryGetOperators(OperatorKind.Put, out var putCandidates))
            {
                foreach (var candidate in putCandidates)
                {
                    if (context.GetMethodSymbol(candidate.MethodHandle) is { } putMethod)
                    {
                        if (constructorCall.AllocationArguments is { } aArgs)
                        {
                            for (int i = 0; i < aArgs.Count && i < putMethod.Parameters.Count; i++)
                            {
                                if (allocArgTypes[i].IsLiteral && context.GetParameterSymbol(putMethod.Parameters[i]) is { } p)
                                    CollapseExpression(aArgs[i], p.Type, scope, containingFunction);
                            }
                        }
                        context.ResolvedTree.AddReference(constructorCall, candidate.MethodHandle);
                        break;
                    }
                }
            }
        }
        else if (allocArgTypes.Count > 0 || constructorCall.AllocationTypeArguments is not null)
        {
            if (context.DiscoveredOperators.TryGetOperators(OperatorKind.New, out var newCandidates))
            {
                foreach (var candidate in newCandidates)
                {
                    if (context.GetMethodSymbol(candidate.MethodHandle) is { } newMethod)
                    {
                        if (constructorCall.AllocationArguments is { } aArgs)
                        {
                            for (int i = 0; i < aArgs.Count && i < newMethod.Parameters.Count; i++)
                            {
                                if (allocArgTypes[i].IsLiteral && context.GetParameterSymbol(newMethod.Parameters[i]) is { } p)
                                    CollapseExpression(aArgs[i], p.Type, scope, containingFunction);
                            }
                        }
                        context.ResolvedTree.AddReference(constructorCall, candidate.MethodHandle);
                        break;
                    }
                }
            }
        }

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

        return isPut ? voidType : targetType;
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
        var targetType = ResolveTypeSyntax(asExpr.Type, scope);
        var exprType = ResolveExpression(asExpr.Expression, scope, containingFunction);

        if (TypeRef.IsNonContiguousSplitBase(targetType, exprType, context, out var splitBaseName))
        {
            context.Diagnostics.ReportNonContiguousBaseConversion(
                asExpr.GetSpan() ?? default,
                splitBaseName!,
                asExpr.GetSource());
        }

        return targetType;
    }

    /// <summary>
    /// Resolves a collection literal expression (<c>[a, b, c]</c>) and types it as an array.
    /// </summary>
    /// <param name="collection">The collection expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>An array <see cref="TypeRef"/> wrapping the inferred element type.</returns>
    private TypeRef ResolveCollection(CollectionExpression collection, Scope scope, SymbolHandle containingFunction)
    {
        TypeRef elemType = TypeRef.Unresolved;
        foreach (var item in collection.Expressions)
        {
            var itemType = ResolveExpression(item, scope, containingFunction);
            if (elemType.IsUnresolved)
                elemType = itemType;
        }

        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.FixedArray, out var fixedArrayHandle);
        return TypeRef.FixedArray(elemType, collection.Expressions.Count, fixedArrayHandle != default ? fixedArrayHandle : null);
    }

    /// <summary>
    /// Resolves a type syntax node to a semantic <see cref="TypeRef"/> by querying the lexical scope.
    /// </summary>
    /// <param name="syntax">The type syntax node to resolve.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <returns>The resolved <see cref="TypeRef"/>, or <see cref="TypeRef.Unresolved"/> if not found.</returns>
    private TypeRef ResolveTypeSyntax(TypeSyntax syntax, Scope scope)
    {
        if (syntax is ModifiedType modified)
        {
            var inner = ResolveTypeSyntax(modified.Type, scope);
            return modified.Modifier switch
            {
                PointerTypeModifier => TypeRef.Pointer(inner),
                ReferenceTypeModifier => TypeRef.Reference(inner),
                OptionalTypeModifier => TypeRef.Optional(inner),
                ArrayTypeModifier => TypeRef.Array(inner),
                _ => TypeRef.Error
            };
        }

        if (syntax is PrefixModifiedType prefixModified)
        {
            var inner = ResolveTypeSyntax(prefixModified.Type, scope);
            if (prefixModified.Modifier is DynTypeModifier)
                return TypeRef.Dyn(inner);
            if (prefixModified.Modifier is UntaggedTypeModifier)
                return TypeRef.Untagged(inner);
            return TypeRef.Error;
        }

        var name = ResolutionContext.GetSymbolName(syntax);
        var syms = scope.ResolveType(name);
        if (syms.Count > 0)
            return TypeRef.Resolved(ResolutionContext.GetHandle(syms[0]));

        return TypeRef.Unresolved;
    }

}
