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

        var calleeType = ResolveExpression(call.Callee, scope, containingFunction);

        if (calleeType.IsLiteral)
        {
            if (expectedExpressionTypes.TryGetValue(call, out var expected) && !expected.IsLiteral && !expected.IsInferred)
            {
                return calleeType;
            }

            calleeType = CollapseLiteralToDefault(calleeType);
        }

        // Pre-propagate expected parameter types into argument expressions (for lambdas)
        if (calleeType.SpecialType is FunctionSemanticType fT)
        {
            for (int i = 0; i < call.Arguments.Count && i < fT.ParameterTypes.Count; i++)
                expectedExpressionTypes[call.Arguments[i]] = fT.ParameterTypes[i];
        }
        else if (calleeType.SpecialType is DelegateSemanticType dT)
        {
            for (int i = 0; i < call.Arguments.Count && i < dT.ParameterTypes.Count; i++)
                expectedExpressionTypes[call.Arguments[i]] = dT.ParameterTypes[i];
        }
        else if (context.ResolvedTree.TryGetReference(call.Callee, out var calleeHandleEarly))
        {
            var sym = context.GetSymbol(calleeHandleEarly);
            IReadOnlyList<SymbolHandle>? paramHandles = sym switch
            {
                FunctionSymbol fn => fn.Parameters,
                MethodSymbol m => m.Parameters,
                _ => null
            };
            if (paramHandles != null)
            {
                for (int i = 0; i < call.Arguments.Count && i < paramHandles.Count; i++)
                {
                    if (context.GetParameterSymbol(paramHandles[i]) is { } p)
                        expectedExpressionTypes[call.Arguments[i]] = p.Type;
                }
            }
        }

        var argTypes = new List<TypeRef>(call.Arguments.Count);
        foreach (var arg in call.Arguments)
            argTypes.Add(ResolveExpression(arg, scope, containingFunction));

        if (calleeType.IsError)
            return TypeRef.Error;
        if (calleeType.IsUnresolved)
            return calleeType;

        // All calls route through Callable operator
        if (!context.DiscoveredOperators.TryGetOperators(OperatorKind.Callable, out _))
        {
            context.Diagnostics.ReportMissingIntrinsicForSyntax(call.Callee.GetSpan() ?? default, "invocation", KnownIntrinsics.OpCallable, call.GetSource());
            return TypeRef.Error;
        }

        // 2. Function pointer / static function invocation (routes through Callable.Invoke)
        if (calleeType.SpecialType is FunctionSemanticType fnType)
        {
            if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Function))
            {
                context.Diagnostics.ReportMissingIntrinsicForSyntax(call.Callee.GetSpan() ?? default, "function call", KnownIntrinsics.Function, call.GetSource());
                return TypeRef.Error;
            }
            return TypeCheckFunctionTypeCall(call, fnType.ParameterTypes, fnType.ReturnType, argTypes, scope, containingFunction);
        }

        // 3. Delegate / instance method invocation (routes through Callable.Invoke)
        if (calleeType.SpecialType is DelegateSemanticType delType)
        {
            if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Delegate))
            {
                context.Diagnostics.ReportMissingIntrinsicForSyntax(call.Callee.GetSpan() ?? default, "delegate call", KnownIntrinsics.Delegate, call.GetSource());
                return TypeRef.Error;
            }

            return TypeCheckFunctionTypeCall(call, delType.ParameterTypes, delType.ReturnType, argTypes, scope, containingFunction);
        }

        // 4. Instance invoking user-defined Callable operator: T(...)
        if (TryResolveCallableOperator(call, calleeType, argTypes, out var callableReturn))
            return callableReturn;

        // 5. Non-callable type error reporting: MH0611
        if (!calleeType.IsInferred)
        {
            context.Diagnostics.ReportTypeNotCallable(call.Callee.GetSpan() ?? default, calleeType.ToQualifiedString(context), labelTypeName: calleeType.ToLabelString(context, scope), source: call.GetSource());
            return TypeRef.Error;
        }

        return calleeType;
    }

    /// <summary>
    /// Attempts to resolve an invocation on an instance whose type implements the <see cref="OperatorKind.Callable"/> operator.
    /// </summary>
    /// <remarks>
    /// The call target supplies an explicit first receiver parameter when the Callable contract
    /// declares one. Only the remaining parameters are matched against the arguments inside
    /// <c>callee(...)</c>; treating the receiver as a user argument would make an explicit
    /// <c>Callable.Invoke(&amp;this, TArgs...)</c> impossible to call.
    /// </remarks>
    /// <param name="call">The call expression syntax node.</param>
    /// <param name="calleeType">The semantic type of the receiver/callee object.</param>
    /// <param name="argTypes">The evaluated argument types.</param>
    /// <param name="result">The return type of the matching Callable method if found.</param>
    /// <returns><see langword="true"/> if a matching Callable operator was resolved; otherwise <see langword="false"/>.</returns>
    private bool TryResolveCallableOperator(CallExpression call, TypeRef calleeType, List<TypeRef> argTypes, out TypeRef result)
    {
        result = TypeRef.Unresolved;
        var typeHandle = calleeType.Handle;
        if (typeHandle is null)
        {
            if (calleeType.Kind == TypeRefKind.FixedArray && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.FixedArray, out var hFix))
                typeHandle = hFix;
            else if (calleeType.Kind == TypeRefKind.Array && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var hArr))
                typeHandle = hArr;
            else if (calleeType.Kind == TypeRefKind.Span && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Span, out var hSpan))
                typeHandle = hSpan;
            else if (calleeType.Kind == TypeRefKind.Tuple && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var hTup))
                typeHandle = hTup;
            else if (calleeType.Kind == TypeRefKind.Optional && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var hOpt))
                typeHandle = hOpt;
        }

        if (typeHandle is null)
            return false;

        var typeSym = (Symbol?)context.GetTypeSymbol(typeHandle.Value) ?? context.GetSymbol(typeHandle.Value);
        if (typeSym is null)
            return false;

        if (!context.DiscoveredOperators.TryGetOperators(OperatorKind.Callable, out var candidates))
            return false;

        // Build generic mapping if calleeType is generic (e.g. Optional<T>, FixedArray<T, N>)
        Dictionary<SymbolHandle, TypeRef>? typeArgs = null;
        if (calleeType.SpecialType is OptionalSemanticType optSem && typeSym is TypeSymbol ts1 && ts1.GenericParameters.Count >= 1)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef> { [ts1.GenericParameters[0]] = optSem.ElementType };
        }
        else if (calleeType.SpecialType is FixedArraySemanticType fixSem && typeSym is TypeSymbol ts2 && ts2.GenericParameters.Count >= 1)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef> { [ts2.GenericParameters[0]] = fixSem.ElementType };
        }

        var typeScope = GetOwnedScope(typeSym);
        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null) continue;

            IReadOnlyList<TypeRef> baseTypes = typeSym switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                _ => []
            };

            foreach (var sym in typeScope.Symbols.Values)
            {
                if (sym is MethodSymbol method)
                {
                    bool isCandidate = false;
                    if (method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                    {
                        if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                            isCandidate = true;
                    }
                    else if (baseTypes.Any(b => b.Handle == candidate.InterfaceHandle))
                    {
                        if (context.GetMethodSymbol(candidate.MethodHandle) is { } opMethod &&
                            (method.Name.Text == opMethod.Name.Text || method.Name.Text == "Invoke" || method.Name.Text == "Call"))
                        {
                            isCandidate = true;
                        }
                    }

                    if (isCandidate)
                    {
                        // The first receiver is supplied by the call target (`callee(...)`), rather
                        // than by the parenthesized user arguments. This keeps Callable aligned
                        // with ordinary instance methods after receiver spelling became explicit.
                        int argumentStart = 0;
                        if (method.Parameters.Count > 0 &&
                            context.GetParameterSymbol(method.Parameters[0]) is { } firstParameter &&
                            firstParameter.Syntax?.Declarator.IsReceiver == true)
                        {
                            argumentStart = 1;
                        }

                        if (method.Parameters.Count - argumentStart == argTypes.Count)
                        {
                            bool matches = true;
                            for (int i = 0; i < argTypes.Count; i++)
                            {
                                if (context.GetParameterSymbol(method.Parameters[i + argumentStart]) is { } param)
                                {
                                    var effParam = typeArgs is not null ? SubstituteTypeRef(param.Type, typeArgs) : param.Type;
                                    if (!effParam.IsAssignableFrom(argTypes[i], context))
                                    {
                                        matches = false;
                                        break;
                                    }
                                }
                            }
                            if (matches)
                            {
                                context.ResolvedTree.AddReference(call, ResolutionContext.GetHandle(method));
                                var rawRet = method.ReturnType;
                                result = typeArgs is not null ? SubstituteTypeRef(rawRet, typeArgs) : rawRet;
                                return true;
                            }
                        }
                    }
                }
            }
        }

        return false;
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
