using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class ExpressionResolutionPass
{
    /// <summary>
    /// Resolves the semantic type of a function, method, or callable operator invocation.
    /// <para>
    /// Handles member access receivers, functions/methods bound in the resolution tree,
    /// and user-defined types implementing the <see cref="OperatorKind.Callable"/> operator interface.
    /// Emits <c>MH2009</c> if the callee expression is not callable.
    /// </para>
    /// </summary>
    /// <param name="call">The call expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The resolved return type of the invocation, or <see cref="TypeRef.Error"/> on failure.</returns>
    private TypeRef ResolveCall(CallExpression call, Scope scope, SymbolHandle containingFunction)
    {
        // 1. If callee is member access on an uncollapsed literal receiver (e.g. 10.Abs()),
        // return uncollapsed receiver type so retroactive propagation can resolve the method once typed
        if (call.Callee is MemberAccessExpression access)
        {
            var recvType = ResolveExpression(access.Expression, scope, containingFunction);
            if (recvType.IsLiteral)
            {
                return recvType;
            }
        }

        MemberAccessExpression? directMemberAccess = call.Callee as MemberAccessExpression;
        if (directMemberAccess is not null)
            directCallMemberAccesses.Add(directMemberAccess);

        TypeRef calleeType;
        try
        {
            calleeType = ResolveExpression(call.Callee, scope, containingFunction);
        }
        finally
        {
            if (directMemberAccess is not null)
                directCallMemberAccesses.Remove(directMemberAccess);
        }

        if (calleeType.IsLiteral)
        {
            if (expectedExpressionTypes.TryGetValue(call, out var expected) && !expected.IsLiteral && !expected.IsInferred)
            {
                return calleeType;
            }

            calleeType = CollapseLiteralToDefault(calleeType);
        }

        SymbolHandle calleeHandleEarly = default;
        Symbol? resolvedCalleeSym = context.ResolvedTree.TryGetReference(call.Callee, out calleeHandleEarly)
            ? context.GetSymbol(calleeHandleEarly)
            : null;

        bool hasExplicitReceiverParam = resolvedCalleeSym is MethodSymbol mRecv &&
            !mRecv.Flags.HasFlag(FunctionFlags.Static) &&
            mRecv.Parameters.Count > 0 &&
            context.GetParameterSymbol(mRecv.Parameters[0])?.IsReceiver == true;

        bool isBoundMethodCall = hasExplicitReceiverParam &&
            ((call.Callee is MemberAccessExpression maCall && context.ResolvedTree.IsBoundMethodCall(maCall)) ||
             (call.Callee is NamedExpression && call.Callee is not MemberAccessExpression and not ScopeResolutionExpression));

        bool isUnboundStaticTypeInstanceCall = hasExplicitReceiverParam &&
            !isBoundMethodCall &&
            ((call.Callee is MemberAccessExpression maStatic && maStatic.Expression is not TypeAsExpression) ||
             call.Callee is ScopeResolutionExpression);

        if (isUnboundStaticTypeInstanceCall)
            context.ResolvedTree.MarkUnboundTypeMethodCall(call);

        bool hasExplicitTypeArgs = call.Callee is GenericNameExpression ||
            (call.Callee is ScopeResolutionExpression sr && sr.Member is GenericNameExpression) ||
            (call.Callee is MemberAccessExpression maGen && maGen.GenericArguments is { Count: > 0 });

        IReadOnlyList<SymbolHandle> callableGenericParams = hasExplicitTypeArgs
            ? []
            : resolvedCalleeSym switch
            {
                FunctionSymbol fs => fs.GenericParameters,
                MethodSymbol ms => ms.GenericParameters,
                _ => []
            };

        TypeRef effectiveReturnType = TypeRef.Unresolved;
        List<TypeRef> effectiveParamTypes = [];
        List<TypeRef> argTypes;

        if (calleeType.SpecialType is FunctionSemanticType fnType)
        {
            int recvSkip = isBoundMethodCall && fnType.ParameterTypes.Count > 0 ? 1 : 0;
            effectiveReturnType = fnType.ReturnType;
            effectiveParamTypes = fnType.ParameterTypes.Skip(recvSkip).ToList();

            if (callableGenericParams.Count > 0 && resolvedCalleeSym is not null)
            {
                argTypes = ResolveGenericCallArgumentsAndInstantiate(
                    call,
                    calleeHandleEarly,
                    resolvedCalleeSym,
                    callableGenericParams,
                    fnType,
                    recvSkip,
                    scope,
                    containingFunction,
                    ref effectiveReturnType,
                    ref effectiveParamTypes);
            }
            else
            {
                for (int i = 0; i < call.Arguments.Count && i < effectiveParamTypes.Count; i++)
                    expectedExpressionTypes[call.Arguments[i]] = effectiveParamTypes[i];

                argTypes = new List<TypeRef>(call.Arguments.Count);
                foreach (var arg in call.Arguments)
                    argTypes.Add(ResolveExpression(arg, scope, containingFunction));
            }
        }
        else
        {
            if ((resolvedCalleeSym is null or not (FunctionSymbol or MethodSymbol)) &&
                TryGetCallableExpectedParameters(calleeType, out var callableExpParams))
            {
                for (int i = 0; i < call.Arguments.Count && i < callableExpParams.Count; i++)
                    expectedExpressionTypes[call.Arguments[i]] = callableExpParams[i];
            }

            argTypes = new List<TypeRef>(call.Arguments.Count);
            foreach (var arg in call.Arguments)
                argTypes.Add(ResolveExpression(arg, scope, containingFunction));
        }

        if (calleeType.IsError)
            return TypeRef.Error;
        if (calleeType.IsUnresolved)
            return calleeType;

        if (!context.DiscoveredOperators.TryGetOperators(OperatorKind.Callable, out _))
        {
            context.Diagnostics.ReportMissingIntrinsicForSyntax(call.Callee.GetSpan() ?? default, "invocation", KnownIntrinsics.OpCallable, call.GetSource());
            return TypeRef.Error;
        }

        // Function pointers, static functions, bound method calls, and unbound Type.Method(&self, ...) calls
        if (calleeType.SpecialType is FunctionSemanticType)
        {
            if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Function))
            {
                context.Diagnostics.ReportMissingIntrinsicForSyntax(call.Callee.GetSpan() ?? default, "function call", KnownIntrinsics.Function, call.GetSource());
                return TypeRef.Error;
            }

            if (isUnboundStaticTypeInstanceCall && hasExplicitReceiverParam && call.Arguments.Count == 0)
            {
                var typeSymObj = resolvedCalleeSym is MethodSymbol msOwner && msOwner.Parent is { } parentH
                    ? (Symbol?)context.GetTypeSymbol(parentH) ?? context.GetSymbol(parentH)
                    : null;
                string typeDisplay = typeSymObj?.Name.Text ?? (call.Callee is MemberAccessExpression maRecv
                    ? maRecv.Expression.ToString() ?? "Type"
                    : "Type");
                context.Diagnostics.ReportCannotAccessInstanceMemberOnType(
                    call.Callee is MemberAccessExpression maId ? maId.Identifier.Span : call.Callee.GetSpan() ?? default,
                    resolvedCalleeSym?.Name.Text ?? "Method",
                    typeDisplay,
                    call.GetSource());
                return TypeRef.Error;
            }

            return TypeCheckFunctionTypeCall(call, effectiveParamTypes, effectiveReturnType, argTypes, scope, containingFunction);
        }

        // Instances and closures invoking Callable / CallableMut / CallableOnce operators
        if (TryResolveCallableOperator(call, calleeType, argTypes, scope, containingFunction, out var callableReturn))
            return callableReturn;

        // Non-callable type error reporting: MH0611
        if (!calleeType.IsInferred)
        {
            context.Diagnostics.ReportTypeNotCallable(call.Callee.GetSpan() ?? default, calleeType.ToQualifiedString(context), labelTypeName: calleeType.ToLabelString(context, scope), source: call.GetSource());
            return TypeRef.Error;
        }

        return calleeType;
    }

    private List<TypeRef> ResolveGenericCallArgumentsAndInstantiate(
        CallExpression call,
        SymbolHandle calleeHandle,
        Symbol resolvedCalleeSym,
        IReadOnlyList<SymbolHandle> callableGenericParams,
        FunctionSemanticType fnType,
        int recvSkip,
        Scope scope,
        SymbolHandle containingFunction,
        ref TypeRef effectiveReturnType,
        ref List<TypeRef> effectiveParamTypes)
    {
        var inferredSubs = new Dictionary<SymbolHandle, TypeRef>();
        if (GetTypeFromSymbol(resolvedCalleeSym).SpecialType is FunctionSemanticType rawFn)
            UnifyTypeForCallInference(TypeRef.Function(rawFn.ReturnType, rawFn.ParameterTypes), TypeRef.Function(fnType.ReturnType, fnType.ParameterTypes), inferredSubs);

        if (expectedExpressionTypes.TryGetValue(call, out var expectedRet) && !expectedRet.IsLiteral && !expectedRet.IsInferred)
            UnifyTypeForCallInference(effectiveReturnType, expectedRet, inferredSubs);

        var genericParamSet = new HashSet<SymbolHandle>(callableGenericParams);
        var argResults = new TypeRef[call.Arguments.Count];
        var isDeferred = new bool[call.Arguments.Count];

        for (int i = 0; i < call.Arguments.Count; i++)
            isDeferred[i] = IsDeferredCallableArgument(call.Arguments[i]);

        // Pass 1: resolve non-lambda arguments so they drive type parameter inference before lambdas resolve.
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (isDeferred[i])
                continue;

            var arg = call.Arguments[i];
            if (GetCallInferenceExpectedArgumentType(effectiveParamTypes, i, inferredSubs) is { } expArg)
                expectedExpressionTypes[arg] = expArg;

            argResults[i] = ResolveExpression(arg, scope, containingFunction);
            if (i < effectiveParamTypes.Count)
            {
                var curParam = SubstituteTypeRef(effectiveParamTypes[i], inferredSubs);
                if (argResults[i].IsLiteral &&
                    curParam.Handle is { Kind: SymbolKind.GenericParameter } gpLit &&
                    genericParamSet.Contains(gpLit))
                {
                    argResults[i] = CollapseLiteralToDefault(argResults[i]);
                    CollapseExpression(arg, argResults[i], scope, containingFunction);
                }

                if (!argResults[i].IsError && !argResults[i].IsUnresolved)
                    UnifyTypeForCallInference(effectiveParamTypes[i], argResults[i], inferredSubs);
            }
        }

        // Pass 2: resolve lambdas and deferred arguments using expected callable signatures from Pass 1.
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (!isDeferred[i])
                continue;

            var arg = call.Arguments[i];
            if (GetCallInferenceExpectedArgumentType(effectiveParamTypes, i, inferredSubs) is { } expArg)
                expectedExpressionTypes[arg] = expArg;

            argResults[i] = ResolveExpression(arg, scope, containingFunction);
            if (i < effectiveParamTypes.Count && !argResults[i].IsError && !argResults[i].IsUnresolved)
                UnifyTypeForCallInference(effectiveParamTypes[i], argResults[i], inferredSubs);
        }

        var inferredArgs = new List<TypeRef>(callableGenericParams.Count);
        foreach (var gpH in callableGenericParams)
            inferredArgs.Add(inferredSubs.TryGetValue(gpH, out var infT) ? infT : TypeRef.Unresolved);

        var instantiated = ValidateAndInstantiateGenericSymbol(
            calleeHandle,
            resolvedCalleeSym,
            [],
            inferredArgs,
            TypeRef.Function(fnType.ReturnType, fnType.ParameterTypes),
            scope,
            containingFunction,
            fallbackSyntax: call,
            outerSubst: inferredSubs);

        if (instantiated.SpecialType is FunctionSemanticType instFn)
        {
            effectiveReturnType = instFn.ReturnType;
            effectiveParamTypes = instFn.ParameterTypes.Skip(recvSkip).ToList();
        }
        else if (inferredSubs.Count > 0)
        {
            effectiveReturnType = SubstituteTypeRef(effectiveReturnType, inferredSubs);
            effectiveParamTypes = effectiveParamTypes.Select(p => SubstituteTypeRef(p, inferredSubs)).ToList();
        }

        return [.. argResults];
    }

    private static bool IsDeferredCallableArgument(Expression expr)
    {
        while (expr is ParenthesizedExpression or UnaryExpression { OperatorToken.Kind: TokenKind.Ampersand })
        {
            expr = expr is ParenthesizedExpression p
                ? p.Expression
                : ((UnaryExpression)expr).Operand;
        }
        return expr is LambdaExpression;
    }

    private TypeRef? GetCallInferenceExpectedArgumentType(
        IReadOnlyList<TypeRef> expectedParams,
        int index,
        Dictionary<SymbolHandle, TypeRef> subs)
    {
        if (index >= expectedParams.Count)
            return null;

        var effParam = subs.Count > 0 ? SubstituteTypeRef(expectedParams[index], subs) : expectedParams[index];
        if (TryExtractCallableExpectation(effParam, out _, out _))
            return effParam;

        bool wrapRef = false;
        bool wrapMut = false;
        var unwrapped = UnwrapAliasAndDyn(effParam);
        if (unwrapped.SpecialType is ReferenceSemanticType rf)
        {
            wrapRef = true;
            wrapMut = rf.IsMutable;
            unwrapped = UnwrapAliasAndDyn(rf.ElementType);
        }

        if (unwrapped.Handle is { Kind: SymbolKind.GenericParameter } gpH &&
            context.GetGenericParameterSymbol(gpH) is { } gp)
        {
            foreach (var constraint in gp.Constraints)
            {
                var effConstraint = subs.Count > 0 ? SubstituteTypeRef(constraint, subs) : constraint;
                if (TryExtractCallableExpectation(effConstraint, out _, out _))
                    return wrapRef ? TypeRef.Reference(effConstraint, wrapMut) : effConstraint;
            }
        }

        return effParam;
    }

    private bool TryGetCallableExpectedParameters(TypeRef calleeType, out IReadOnlyList<TypeRef> paramTypes)
    {
        if (TryExtractCallableExpectation(calleeType, out var extracted, out _) && extracted is not null)
        {
            paramTypes = extracted;
            return true;
        }

        var unwrapped = UnwrapAliasAndDyn(calleeType);
        if (unwrapped.SpecialType is ReferenceSemanticType or PointerSemanticType)
            unwrapped = UnwrapAliasAndDyn(unwrapped.SpecialType is ReferenceSemanticType r ? r.ElementType : ((PointerSemanticType)unwrapped.SpecialType!).ElementType);

        if (unwrapped.Handle is { Kind: SymbolKind.GenericParameter } gpH &&
            context.GetGenericParameterSymbol(gpH) is { } gp)
        {
            foreach (var constraint in gp.Constraints)
            {
                if (TryExtractCallableExpectation(constraint, out var cParams, out _) && cParams is not null)
                {
                    paramTypes = cParams;
                    return true;
                }
            }
        }

        paramTypes = [];
        return false;
    }

    private void UnifyTypeForCallInference(TypeRef pattern, TypeRef actual, Dictionary<SymbolHandle, TypeRef> subs)
    {
        if (actual.IsError || actual.IsUnresolved || actual.IsLiteral || actual.IsInferred)
            return;

        pattern = TypeRef.UnwrapConformance(TypeRef.UnwrapDyn(pattern));
        var unwrappedActual = TypeRef.UnwrapConformance(TypeRef.UnwrapDyn(actual));

        if (pattern.Handle is { Kind: SymbolKind.GenericParameter } gpH)
        {
            if (unwrappedActual.Handle == gpH || actual.Handle == gpH)
                return;

            subs.TryAdd(gpH, actual);
            if (context.GetGenericParameterSymbol(gpH) is { } gp &&
                unwrappedActual.Handle is { } actH &&
                context.GetTypeSymbol(context.UnwrapAlias(actH)) is { } actType)
            {
                foreach (var constraint in gp.Constraints)
                {
                    if (constraint.Handle is not { } cH)
                        continue;
                    var reqIfaceH = context.UnwrapAlias(cH);
                    foreach (var bt in actType.BaseTypes)
                    {
                        if (bt.Handle is { } btH && context.UnwrapAlias(btH) == reqIfaceH)
                            UnifyTypeForCallInference(constraint, bt, subs);
                    }
                }
            }
            return;
        }

        if (pattern.SpecialType is ReferenceSemanticType pr && unwrappedActual.SpecialType is ReferenceSemanticType ar)
        {
            UnifyTypeForCallInference(pr.ElementType, ar.ElementType, subs);
        }
        else if (pattern.SpecialType is PointerSemanticType pp && unwrappedActual.SpecialType is PointerSemanticType ap)
        {
            UnifyTypeForCallInference(pp.ElementType, ap.ElementType, subs);
        }
        else if (pattern.SpecialType is OptionalSemanticType po && unwrappedActual.SpecialType is OptionalSemanticType ao)
        {
            UnifyTypeForCallInference(po.ElementType, ao.ElementType, subs);
        }
        else if (pattern.SpecialType is ArraySemanticType pa && unwrappedActual.SpecialType is ArraySemanticType aa)
        {
            UnifyTypeForCallInference(pa.ElementType, aa.ElementType, subs);
        }
        else if (pattern.SpecialType is SliceSemanticType ps && unwrappedActual.SpecialType is SliceSemanticType as_)
        {
            UnifyTypeForCallInference(ps.ElementType, as_.ElementType, subs);
        }
        else if (pattern.SpecialType is TupleSemanticType pt && unwrappedActual.SpecialType is TupleSemanticType at && pt.Elements.Count == at.Elements.Count)
        {
            for (int i = 0; i < pt.Elements.Count; i++)
                UnifyTypeForCallInference(pt.Elements[i].Type, at.Elements[i].Type, subs);
        }
        else if (pattern.SpecialType is FunctionSemanticType pf && unwrappedActual.SpecialType is FunctionSemanticType af && pf.ParameterTypes.Count == af.ParameterTypes.Count)
        {
            UnifyTypeForCallInference(pf.ReturnType, af.ReturnType, subs);
            for (int i = 0; i < pf.ParameterTypes.Count; i++)
                UnifyTypeForCallInference(pf.ParameterTypes[i], af.ParameterTypes[i], subs);
        }
        else if (pattern.SpecialType is GenericSemanticType pg &&
                 unwrappedActual.SpecialType is GenericSemanticType ag &&
                 context.UnwrapAlias(pg.TargetHandle) == context.UnwrapAlias(ag.TargetHandle) &&
                 pg.TypeArguments.Count == ag.TypeArguments.Count)
        {
            for (int i = 0; i < pg.TypeArguments.Count; i++)
                UnifyTypeForCallInference(pg.TypeArguments[i], ag.TypeArguments[i], subs);
        }
    }

    /// <summary>
    /// Attempts to resolve an invocation on an instance whose type implements <see cref="OperatorKind.Callable"/>,
    /// <see cref="OperatorKind.CallableMut"/>, or <see cref="OperatorKind.CallableOnce"/>.
    /// </summary>
    private bool TryResolveCallableOperator(
        CallExpression call,
        TypeRef calleeType,
        List<TypeRef> argTypes,
        Scope scope,
        SymbolHandle containingFunction,
        out TypeRef result)
    {
        result = TypeRef.Unresolved;

        var unwrapped = UnwrapAliasAndDyn(calleeType);
        bool isSharedRefAccess = false;
        bool isMutRefAccess = false;
        if (unwrapped.SpecialType is ReferenceSemanticType refCallee)
        {
            isMutRefAccess = refCallee.IsMutable;
            isSharedRefAccess = !refCallee.IsMutable;
            unwrapped = UnwrapAliasAndDyn(refCallee.ElementType);
        }

        var typeHandle = unwrapped.Handle;
        if (typeHandle is null)
        {
            if (unwrapped.Kind == TypeRefKind.Array && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var hArr))
                typeHandle = hArr;
            else if (unwrapped.Kind == TypeRefKind.Slice && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Slice, out var hSlice))
                typeHandle = hSlice;
            else if (unwrapped.Kind == TypeRefKind.Tuple && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var hTup))
                typeHandle = hTup;
            else if (unwrapped.Kind == TypeRefKind.Optional && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var hOpt))
                typeHandle = hOpt;
        }

        if (typeHandle is null)
            return false;

        var symHandle = context.UnwrapAlias(typeHandle.Value);
        var typeSym = (Symbol?)context.GetTypeSymbol(symHandle) ?? context.GetSymbol(symHandle);
        if (typeSym is null)
            return false;

        OperatorKind[] allowedKinds = isSharedRefAccess
            ? [OperatorKind.Callable]
            : isMutRefAccess
                ? [OperatorKind.Callable, OperatorKind.CallableMut]
                : [OperatorKind.Callable, OperatorKind.CallableMut, OperatorKind.CallableOnce];

        // 1. Generic parameter constrained by Callable / CallableMut / CallableOnce (e.g., where F : CallableOnce<T>)
        if (typeSym is GenericParameterSymbol gpSym)
        {
            foreach (var kind in allowedKinds)
            {
                if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
                    continue;
                foreach (var candidate in candidates)
                {
                    var reqIfaceH = context.UnwrapAlias(candidate.InterfaceHandle);
                    foreach (var constraint in gpSym.Constraints)
                    {
                        if (constraint.Handle is { } cH &&
                            context.UnwrapAlias(cH) == reqIfaceH &&
                            TryExtractCallableExpectation(constraint, out var expParams, out var expRet) &&
                            expParams is not null &&
                            expRet is not null &&
                            TryMatchCallableArguments(call, expParams, subs: null, argTypes, scope, containingFunction))
                        {
                            context.ResolvedTree.AddReference(call, candidate.MethodHandle);
                            result = expRet.Value;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // 2. Direct interface / dyn callable (e.g., dyn Callable<int32, int32>& or CallableMut<int32> mut&)
        if (typeSym is TypeSymbol { TypeKind: TypeKind.Interface } or NestedTypeSymbol { TypeKind: TypeKind.Interface })
        {
            foreach (var kind in allowedKinds)
            {
                if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
                    continue;
                foreach (var candidate in candidates)
                {
                    if (symHandle == context.UnwrapAlias(candidate.InterfaceHandle) &&
                        TryExtractCallableExpectation(unwrapped, out var expParams, out var expRet) &&
                        expParams is not null &&
                        expRet is not null &&
                        TryMatchCallableArguments(call, expParams, subs: null, argTypes, scope, containingFunction))
                    {
                        context.ResolvedTree.AddReference(call, candidate.MethodHandle);
                        result = expRet.Value;
                        return true;
                    }
                }
            }
        }

        // 3. Concrete functor / synthesized closure struct implementing Callable / CallableMut / CallableOnce
        Dictionary<SymbolHandle, TypeRef>? typeArgs = null;
        if (unwrapped.SpecialType is GenericSemanticType genSem && typeSym is TypeSymbol tsGen && tsGen.GenericParameters.Count == genSem.TypeArguments.Count)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef>(tsGen.GenericParameters.Count);
            for (int g = 0; g < tsGen.GenericParameters.Count; g++)
                typeArgs[tsGen.GenericParameters[g]] = genSem.TypeArguments[g];
        }
        else if (unwrapped.SpecialType is OptionalSemanticType optSem && typeSym is TypeSymbol ts1 && ts1.GenericParameters.Count >= 1)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef> { [ts1.GenericParameters[0]] = optSem.ElementType };
        }
        else if (unwrapped.SpecialType is ArraySemanticType arrSem && typeSym is TypeSymbol ts2 && ts2.GenericParameters.Count >= 1)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef> { [ts2.GenericParameters[0]] = arrSem.ElementType };
        }
        else if (unwrapped.SpecialType is SliceSemanticType sliceSem && typeSym is TypeSymbol ts3 && ts3.GenericParameters.Count >= 1)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef> { [ts3.GenericParameters[0]] = sliceSem.ElementType };
        }

        IReadOnlyList<TypeRef> baseTypes = typeSym switch
        {
            TypeSymbol ts => ts.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            _ => []
        };

        var typeScope = GetOwnedScope(typeSym);
        foreach (var kind in allowedKinds)
        {
            if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
                continue;

            foreach (var candidate in candidates)
            {
                var ifaceHandle = context.UnwrapAlias(candidate.InterfaceHandle);
                var ifaceSym = context.GetTypeSymbol(ifaceHandle);
                if (ifaceSym is null) continue;

                foreach (var sym in typeScope.Symbols.Values)
                {
                    if (sym is not MethodSymbol method)
                        continue;

                    bool isCandidate = false;
                    if (method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                    {
                        if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                            isCandidate = true;
                    }
                    else if (baseTypes.Any(b => b.Handle is { } bh && context.UnwrapAlias(bh) == ifaceHandle))
                    {
                        if (context.GetMethodSymbol(candidate.MethodHandle) is { } opMethod &&
                            (method.Name.Text == opMethod.Name.Text || method.Name.Text == "Invoke" || method.Name.Text == "Call"))
                        {
                            isCandidate = true;
                        }
                    }

                    if (!isCandidate)
                        continue;

                    int argumentStart = 0;
                    if (method.Parameters.Count > 0 &&
                        context.GetParameterSymbol(method.Parameters[0]) is { IsReceiver: true })
                    {
                        argumentStart = 1;
                    }

                    if (method.Parameters.Count - argumentStart != argTypes.Count)
                        continue;

                    var paramTypes = new List<TypeRef>(argTypes.Count);
                    for (int i = 0; i < argTypes.Count; i++)
                    {
                        var pType = context.GetParameterSymbol(method.Parameters[i + argumentStart])?.Type ?? TypeRef.Error;
                        paramTypes.Add(pType);
                    }

                    if (TryMatchCallableArguments(call, paramTypes, typeArgs, argTypes, scope, containingFunction))
                    {
                        context.ResolvedTree.AddReference(call, ResolutionContext.GetHandle(method));
                        var rawRet = method.ReturnType;
                        result = typeArgs is not null ? SubstituteTypeRef(rawRet, typeArgs) : rawRet;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool TryMatchCallableArguments(
        CallExpression call,
        IReadOnlyList<TypeRef> expectedParamTypes,
        Dictionary<SymbolHandle, TypeRef>? subs,
        List<TypeRef> argTypes,
        Scope scope,
        SymbolHandle containingFunction)
    {
        if (expectedParamTypes.Count != argTypes.Count)
            return false;

        for (int i = 0; i < argTypes.Count; i++)
        {
            var effParam = subs is not null ? SubstituteTypeRef(expectedParamTypes[i], subs) : expectedParamTypes[i];
            if (argTypes[i].IsLiteral)
            {
                if (!IsLiteralCompatibleWith(argTypes[i], effParam))
                    return false;
            }
            else if (!effParam.IsAssignableFrom(argTypes[i], context) &&
                     !TryResolveImplicitCast(argTypes[i], effParam, out _))
            {
                return false;
            }
        }

        for (int i = 0; i < argTypes.Count; i++)
        {
            if (argTypes[i].IsLiteral)
            {
                var effParam = subs is not null ? SubstituteTypeRef(expectedParamTypes[i], subs) : expectedParamTypes[i];
                CollapseExpression(call.Arguments[i], effParam, scope, containingFunction);
                argTypes[i] = effParam;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether a parameter is declared with a variadic modifier (<c>...</c>).
    /// </summary>
    /// <param name="p">The parameter symbol to inspect.</param>
    /// <returns><see langword="true"/> if variadic; otherwise <see langword="false"/>.</returns>
    private static bool IsVariadicParameter(ParameterSymbol p) =>
        p.Syntax?.Declarator.Type is ModifiedType m && m.Modifier is VariadicTypeModifier;

    /// <summary>
    /// Determines whether a parameter has a default initial value expression.
    /// </summary>
    /// <param name="p">The parameter symbol to inspect.</param>
    /// <returns><see langword="true"/> if optional with a default value; otherwise <see langword="false"/>.</returns>
    private static bool HasDefaultValue(ParameterSymbol p) =>
        p.Syntax?.Initializer is not null;

    /// <summary>
    /// Type-checks call arguments against expected parameters, eagerly collapses unconstrained literal arguments,
    /// verifies argument counts, and checks assignability or implicit conversions.
    /// </summary>
    /// <param name="call">The call expression syntax node.</param>
    /// <param name="paramHandles">List of parameter symbol handles for the target function or method.</param>
    /// <param name="returnType">The declared return type of the callee.</param>
    /// <param name="argTypes">The evaluated argument types.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The return type of the validated call.</returns>
    private TypeRef TypeCheckCall(CallExpression call, IReadOnlyList<SymbolHandle> paramHandles, TypeRef returnType, List<TypeRef> argTypes, Scope scope, SymbolHandle containingFunction)
    {
        var expectedParams = new List<ParameterSymbol>();
        bool isVariadic = false;
        foreach (var ph in paramHandles)
        {
            if (context.GetParameterSymbol(ph) is { } param)
            {
                expectedParams.Add(param);
                if (IsVariadicParameter(param)) isVariadic = true;
            }
        }

        int requiredCount = expectedParams.Count(p => !IsVariadicParameter(p) && !HasDefaultValue(p));
        int maxCount = isVariadic ? int.MaxValue : expectedParams.Count;

        // Verify argument count within [requiredCount, maxCount]
        if (argTypes.Count < requiredCount || argTypes.Count > maxCount)
        {
            context.Diagnostics.ReportArgumentCountMismatch(
                call.GetSpan() ?? default,
                expectedParams.Count,
                argTypes.Count,
                call.GetSource());
        }

        // Verify argument types and eagerly collapse literal arguments
        for (int i = 0; i < argTypes.Count && i < expectedParams.Count; i++)
        {
            var param = expectedParams[i];
            if (argTypes[i].IsLiteral)
            {
                CollapseExpression(call.Arguments[i], param.Type, scope, containingFunction);
                argTypes[i] = param.Type;
            }
            else if (!param.Type.IsAssignableFrom(argTypes[i], context) &&
                !TryResolveImplicitCast(argTypes[i], param.Type, out _) &&
                !argTypes[i].IsError && !argTypes[i].IsUnresolved && !argTypes[i].IsInferred &&
                !param.Type.IsError && !param.Type.IsUnresolved && !param.Type.IsInferred)
            {
                if (TypeRef.IsNonContiguousSplitBase(param.Type, argTypes[i], context, out var splitBaseName))
                {
                    context.Diagnostics.ReportNonContiguousBaseConversion(
                        call.Arguments[i].GetSpan() ?? default,
                        splitBaseName!,
                        call.GetSource());
                }
                else
                {
                    context.Diagnostics.ReportArgumentTypeMismatch(
                        call.Arguments[i].GetSpan() ?? default,
                        i + 1,
                        argTypes[i].ToQualifiedString(context),
                        param.Type.ToQualifiedString(context),
                        argLabel: argTypes[i].ToLabelString(context, scope),
                        paramLabel: param.Type.ToLabelString(context, scope),
                        source: call.GetSource());
                }
            }
            else if (!argTypes[i].IsError && !argTypes[i].IsUnresolved && !argTypes[i].IsInferred &&
                     !param.Type.IsError && !param.Type.IsUnresolved && !param.Type.IsInferred)
            {
                ValidateConformanceConversion(argTypes[i], param.Type, call.Arguments[i], scope, containingFunction);
            }
        }

        return returnType;
    }

    private TypeRef TypeCheckFunctionTypeCall(CallExpression call, IReadOnlyList<TypeRef> expectedParamTypes, TypeRef returnType, List<TypeRef> argTypes, Scope scope, SymbolHandle containingFunction)
    {
        if (argTypes.Count != expectedParamTypes.Count)
        {
            context.Diagnostics.ReportArgumentCountMismatch(
                call.GetSpan() ?? default,
                expectedParamTypes.Count,
                argTypes.Count,
                call.GetSource());
        }

        for (int i = 0; i < argTypes.Count && i < expectedParamTypes.Count; i++)
        {
            var expectedType = expectedParamTypes[i];
            if (argTypes[i].IsLiteral)
            {
                CollapseExpression(call.Arguments[i], expectedType, scope, containingFunction);
                argTypes[i] = expectedType;
            }
            else if (!expectedType.IsAssignableFrom(argTypes[i], context) &&
                !TryResolveImplicitCast(argTypes[i], expectedType, out _) &&
                !argTypes[i].IsError && !argTypes[i].IsUnresolved && !argTypes[i].IsInferred &&
                !expectedType.IsError && !expectedType.IsUnresolved && !expectedType.IsInferred)
            {
                if (TypeRef.IsNonContiguousSplitBase(expectedType, argTypes[i], context, out var splitBaseName))
                {
                    context.Diagnostics.ReportNonContiguousBaseConversion(
                        call.Arguments[i].GetSpan() ?? default,
                        splitBaseName!,
                        call.GetSource());
                }
                else
                {
                    context.Diagnostics.ReportArgumentTypeMismatch(
                        call.Arguments[i].GetSpan() ?? default,
                        i + 1,
                        argTypes[i].ToQualifiedString(context),
                        expectedType.ToQualifiedString(context),
                        argLabel: argTypes[i].ToLabelString(context, scope),
                        paramLabel: expectedType.ToLabelString(context, scope),
                        source: call.GetSource());
                }
            }
            else if (!argTypes[i].IsError && !argTypes[i].IsUnresolved && !argTypes[i].IsInferred &&
                     !expectedType.IsError && !expectedType.IsUnresolved && !expectedType.IsInferred)
            {
                ValidateConformanceConversion(argTypes[i], expectedType, call.Arguments[i], scope, containingFunction);
            }
        }

        return returnType;
    }

}
