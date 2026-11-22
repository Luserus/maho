using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

internal sealed partial class ExpressionResolutionPass
{
    /// <summary>
    /// Fallback resolution invoked at the end of a block or function scope:
    /// collapses any local variables still holding unconstrained compile-time literal types
    /// to their canonical intrinsic types (<c>Int32</c>, <c>Float64</c>, <c>String</c>, <c>Char</c>)
    /// and retroactively propagates that type into their initializer expression trees.
    /// </summary>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    private void CollapseRemainingLocals(Scope scope, SymbolHandle containingFunction)
    {
        if (uncollapsedLocals.Count == 0)
            return;

        var snapshot = uncollapsedLocals.ToList();
        foreach (var (local, initNode) in snapshot)
        {
            if (uncollapsedLocals.ContainsKey(local))
            {
                var defaultType = CollapseLiteralToDefault(local.Type);
                uncollapsedLocals.Remove(local);
                local.Type = defaultType;
                CollapseExpression(initNode, defaultType, scope, containingFunction);
            }
        }
    }

    /// <summary>
    /// Core sequential eager collapsing and retroactive literal resolution engine.
    /// <para>
    /// When an uncollapsed literal expression tree encounters a concrete target type
    /// (such as through assignment, parameter passing, or explicit variable type annotation),
    /// this method propagates the target type downward through the expression tree:
    /// <list type="bullet">
    /// <item><description>Updates local variable symbols and collapses their initializer trees.</description></item>
    /// <item><description>Validates literal conversions and reports ambiguity (<c>MH2014</c>).</description></item>
    /// <item><description>Re-resolves binary operator overloads on the target type and re-binds method references.</description></item>
    /// <item><description>Re-resolves unary operator overloads on the target type.</description></item>
    /// <item><description>Re-resolves member access and call invocations (e.g. <c>10.Abs()</c>) against the target type,
    /// reporting member not found (<c>MH2008</c>) if missing.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="node">The root syntax node of the expression subtree being collapsed.</param>
    /// <param name="targetType">The concrete target semantic type to collapse into.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The collapsed semantic <see cref="TypeRef"/>, or <see cref="TypeRef.Error"/> on failure.</returns>
    private TypeRef CollapseExpression(
        SyntaxNode node,
        TypeRef targetType,
        Scope scope,
        SymbolHandle containingFunction)
    {
        if (targetType.IsLiteral || targetType.IsInferred || targetType.IsError || targetType.IsUnresolved)
            return targetType;

        switch (node)
        {
            case NamedExpression named:
                // Named expression: collapse referencing local variable symbol and its initializer tree
                LocalVariableSymbol? local = null;
                var namedPart = new SymbolPart(named.Identifier, 0, context.GetHygiene(named));
                var namedSyms = scope[new SymbolName(namedPart)];
                if (namedSyms.Count > 0 && namedSyms[0] is LocalVariableSymbol ls)
                    local = ls;
                else if (context.ResolvedTree.TryGetReference(named, out var handle) &&
                         context.GetSymbol(handle) is LocalVariableSymbol lh)
                    local = lh;

                if (local is not null)
                {
                    if (uncollapsedLocals.TryGetValue(local, out var initNode))
                    {
                        uncollapsedLocals.Remove(local);
                        local.Type = targetType;
                        CollapseExpression(initNode, targetType, scope, containingFunction);
                    }
                    else
                    {
                        local.Type = targetType;
                    }
                }
                context.SetExpressionType(named, targetType);
                return targetType;

            case LiteralExpression lit:
                // Boolean literals always remain canonical bool
                if (lit.Literal.MatchingKind is MatchingKeywordKind.True or MatchingKeywordKind.False)
                    return boolType;

                var literalKind = lit.Literal.Kind is TokenKind.Float ? CompileTimeLiteralKind.Float :
                                  lit.Literal.Kind is TokenKind.String ? CompileTimeLiteralKind.Str :
                                  lit.Literal.Kind is TokenKind.Char ? CompileTimeLiteralKind.Char : CompileTimeLiteralKind.Int;
                var literalOverloads = GetMatchingLiteralMethods(targetType, literalKind);
                if (literalOverloads.Count > 1)
                {
                    // Ambiguous literal conversion: multiple literal methods match
                    var overloadNames = string.Join(", ", literalOverloads.Select(m => m.Name.Text));
                    context.Diagnostics.ReportAmbiguousLiteralConversion(
                        lit.GetSpan() ?? default,
                        targetType.ToQualifiedString(context),
                        overloadNames,
                        labelTypeName: targetType.ToLabelString(context, scope),
                        source: lit.GetSource());
                    context.SetExpressionType(lit, TypeRef.Error);
                    return TypeRef.Error;
                }
                if (literalOverloads.Count == 1)
                {
                    context.ResolvedTree.AddReference(lit, ResolutionContext.GetHandle(literalOverloads[0]));
                    context.SetExpressionType(lit, targetType);
                    CheckLiteralRangeOverflow(lit, targetType);
                    return targetType;
                }

                if (!IsLiteralCompatibleWith(lit, targetType))
                {
                    return TypeRef.Error;
                }
                context.SetExpressionType(lit, targetType);
                CheckLiteralRangeOverflow(lit, targetType);
                return targetType;

            case BinaryExpression binary:
                // Re-resolve binary operator overload against the concrete target type
                var opKind = GetBinaryOperatorKind(binary.OperatorToken.Kind);
                if (TryFindOperatorForTarget(opKind, targetType, out var opMethod, out var lhsType, out var rhsType) && opMethod != null)
                {
                    CollapseExpression(binary.LeftExpression, lhsType, scope, containingFunction);
                    CollapseExpression(binary.RightExpression, rhsType, scope, containingFunction);
                    context.ResolvedTree.AddReference(binary, ResolutionContext.GetHandle(opMethod));
                    context.SetExpressionType(binary, opMethod.ReturnType);
                    return opMethod.ReturnType;
                }
                else
                {
                    var opName = KnownIntrinsics.GetOperatorName(opKind);
                    context.Diagnostics.ReportTypeMismatch(
                        binary.OperatorToken.Span,
                        binary.OperatorToken.Value,
                        binary.LeftExpression.GetSpan() ?? default,
                        targetType.ToQualifiedString(context),
                        binary.RightExpression.GetSpan() ?? default,
                        targetType.ToQualifiedString(context),
                        lhsLabel: targetType.ToLabelString(context, scope),
                        rhsLabel: targetType.ToLabelString(context, scope),
                        opName: opName,
                        source: binary.GetSource());
                    context.SetExpressionType(binary, TypeRef.Error);
                    return TypeRef.Error;
                }

            case UnaryExpression unary:
                // Re-resolve unary operator overload against the concrete target type
                var uOpKind = GetUnaryOperatorKind(unary.OperatorToken.Kind, unary.Position);
                if (TryFindUnaryOperatorForTarget(uOpKind, targetType, out var uOpMethod, out var opParamType) && uOpMethod != null)
                {
                    CollapseExpression(unary.Operand, opParamType, scope, containingFunction);
                    context.ResolvedTree.AddReference(unary, ResolutionContext.GetHandle(uOpMethod));
                    context.SetExpressionType(unary, uOpMethod.ReturnType);
                    CheckLiteralRangeOverflow(unary, targetType);
                    return uOpMethod.ReturnType;
                }
                else
                {
                    var opName = KnownIntrinsics.GetOperatorName(uOpKind);
                    context.Diagnostics.ReportUnaryTypeMismatch(
                        unary.OperatorToken.Span,
                        unary.OperatorToken.Value,
                        unary.Operand.GetSpan() ?? default,
                        targetType.ToQualifiedString(context),
                        operandLabel: targetType.ToLabelString(context, scope),
                        opName: opName,
                        source: unary.GetSource());
                    context.SetExpressionType(unary, TypeRef.Error);
                    return TypeRef.Error;
                }

            case CallExpression call:
                // Re-resolve member call (e.g. 10.Abs()) against the collapsed target type
                if (call.Callee is MemberAccessExpression access)
                {
                    CollapseExpression(access.Expression, targetType, scope, containingFunction);
                    var memberSym = LookupMemberOnType(targetType, access.Identifier.Value);
                    if (memberSym is MethodSymbol m)
                    {
                        if (m.Parameters.Count > 0 &&
                            context.GetParameterSymbol(m.Parameters[0]) is { } firstP &&
                            firstP.Syntax?.Declarator.IsReceiver == true)
                        {
                            var rk = firstP.Syntax.Declarator.ReceiverKind;
                            if (rk is not ReceiverKind.Value && rk is not ReceiverKind.None)
                            {
                                if (!IsExpressionLValue(access.Expression, targetType))
                                {
                                    context.Diagnostics.ReportInvalidReceiverForByRefThis(
                                        access.Expression.GetSpan() ?? default,
                                        m.Name.Text,
                                        rk.ToString(),
                                        access.Expression.GetSource());
                                    context.SetExpressionType(call, TypeRef.Error);
                                    return TypeRef.Error;
                                }
                            }
                        }
                        context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(m));
                        context.ResolvedTree.AddReference(call, ResolutionContext.GetHandle(m));
                        context.SetExpressionType(call, m.ReturnType);
                        return m.ReturnType;
                    }
                    else
                    {
                        context.Diagnostics.ReportMemberNotFound(
                            access.Identifier.Span,
                            access.Identifier.Value,
                            targetType.ToQualifiedString(context),
                            labelTypeName: targetType.ToLabelString(context, scope),
                            source: access.GetSource());
                        context.SetExpressionType(call, TypeRef.Error);
                        return TypeRef.Error;
                    }
                }
                else if (call.Callee is LiteralExpression litCallee)
                {
                    var argTypes = new List<TypeRef>(call.Arguments.Count);
                    foreach (var arg in call.Arguments)
                        argTypes.Add(ResolveExpression(arg, scope, containingFunction));

                    MethodSymbol? cand1 = null;
                    if (targetType.Handle is { } targetH && context.GetTypeSymbol(targetH) is { } targetTypeSym)
                    {
                        var litKind = litCallee.Literal.Kind is TokenKind.Float ? CompileTimeLiteralKind.Float :
                                      litCallee.Literal.Kind is TokenKind.String ? CompileTimeLiteralKind.Str :
                                      litCallee.Literal.Kind is TokenKind.Char ? CompileTimeLiteralKind.Char : CompileTimeLiteralKind.Int;
                        var litOverloads = GetMatchingLiteralMethods(targetType, litKind);
                        if (litOverloads.Count > 1)
                        {
                            var overloadNames = string.Join(", ", litOverloads.Select(m => m.Name.Text));
                            context.Diagnostics.ReportAmbiguousLiteralConversion(
                                litCallee.GetSpan() ?? default,
                                targetType.ToQualifiedString(context),
                                overloadNames,
                                labelTypeName: targetType.ToLabelString(context, scope),
                                source: call.GetSource());
                            context.SetExpressionType(call, TypeRef.Error);
                            return TypeRef.Error;
                        }
                        if (litOverloads.Count == 1)
                        {
                            if (TryResolveCallableOperator(call, targetType, argTypes, out var ret1) && targetType.IsAssignableFrom(ret1, context))
                            {
                                if (context.ResolvedTree.TryGetReference(call, out var mHandle) && context.GetMethodSymbol(mHandle) is { } m1)
                                    cand1 = m1;
                            }
                        }
                    }

                    MethodSymbol? cand2 = null;
                    var defaultType = CollapseLiteralToDefault(TypeRef.Literal(CompileTimeLiteralKind.Int, litCallee.Literal.Value));
                    if (TryResolveCallableOperator(call, defaultType, argTypes, out var ret2) && targetType.IsAssignableFrom(ret2, context))
                    {
                        if (context.ResolvedTree.TryGetReference(call, out var mHandle2) && context.GetMethodSymbol(mHandle2) is { } m2)
                            cand2 = m2;
                    }

                    if (cand1 != null && cand2 != null && cand1 != cand2)
                    {
                        var span = litCallee.GetSpan() ?? default;
                        context.Diagnostics.ReportAmbiguousLiteralConversion(span, targetType.ToQualifiedString(context), litCallee.Literal.Value, labelTypeName: targetType.ToLabelString(context, scope), source: call.GetSource());
                        context.SetExpressionType(call, TypeRef.Error);
                        return TypeRef.Error;
                    }

                    var chosenMethod = cand1 ?? cand2;
                    if (chosenMethod != null)
                    {
                        context.ResolvedTree.AddReference(call, ResolutionContext.GetHandle(chosenMethod));
                        context.SetExpressionType(call, chosenMethod.ReturnType);
                        return chosenMethod.ReturnType;
                    }

                    if (TryResolveCallableOperator(call, defaultType, argTypes, out var defRet))
                    {
                        context.SetExpressionType(call, defRet);
                        return defRet;
                    }

                    context.Diagnostics.ReportTypeNotCallable(litCallee.GetSpan() ?? default, defaultType.ToQualifiedString(context), labelTypeName: defaultType.ToLabelString(context, scope), source: call.GetSource());
                    context.SetExpressionType(call, TypeRef.Error);
                    return TypeRef.Error;
                }
                return TypeRef.Unresolved;

            case ParenthesizedExpression paren:
                var res = CollapseExpression(paren.Expression, targetType, scope, containingFunction);
                context.SetExpressionType(paren, res);
                return res;

            default:
                return targetType;
        }
    }

    /// <summary>
    /// Searches for a binary operator implementation on the target type matching the specified operator kind.
    /// </summary>
    /// <param name="kind">The binary operator kind.</param>
    /// <param name="targetType">The concrete target type implementing the operator.</param>
    /// <param name="opMethod">The matching operator method symbol if found.</param>
    /// <param name="lhsType">The required left-hand parameter type.</param>
    /// <param name="rhsType">The required right-hand parameter type.</param>
    /// <returns><see langword="true"/> if a matching operator was found; otherwise <see langword="false"/>.</returns>
    private bool TryFindOperatorForTarget(
        OperatorKind kind,
        TypeRef targetType,
        out MethodSymbol? opMethod,
        out TypeRef lhsType,
        out TypeRef rhsType)
    {
        opMethod = null;
        lhsType = TypeRef.Unresolved;
        rhsType = TypeRef.Unresolved;

        if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
            return false;

        if (targetType.Handle is not { } targetHandle)
            return false;

        var targetSym = (Symbol?)context.GetTypeSymbol(targetHandle) ?? context.GetSymbol(targetHandle);
        if (targetSym is null)
            return false;

        var scope = GetOwnedScope(targetSym);
        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null)
                continue;

            foreach (var sym in scope.Symbols.Values)
            {
                if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                {
                    if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                    {
                        if (method.Parameters.Count == 2)
                        {
                            var p0 = context.GetParameterSymbol(method.Parameters[0])?.Type ?? TypeRef.Unresolved;
                            var p1 = context.GetParameterSymbol(method.Parameters[1])?.Type ?? TypeRef.Unresolved;
                            opMethod = method;
                            lhsType = p0;
                            rhsType = p1;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Searches for a unary operator implementation on the target type matching the specified operator kind.
    /// </summary>
    /// <param name="kind">The unary operator kind.</param>
    /// <param name="targetType">The concrete target type implementing the operator.</param>
    /// <param name="opMethod">The matching unary operator method symbol if found.</param>
    /// <param name="operandType">The required operand parameter type.</param>
    /// <returns><see langword="true"/> if a matching operator was found; otherwise <see langword="false"/>.</returns>
    private bool TryFindUnaryOperatorForTarget(
        OperatorKind kind,
        TypeRef targetType,
        out MethodSymbol? opMethod,
        out TypeRef operandType)
    {
        opMethod = null;
        operandType = TypeRef.Unresolved;

        if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
            return false;

        if (targetType.Handle is not { } targetHandle)
            return false;

        var targetSym = (Symbol?)context.GetTypeSymbol(targetHandle) ?? context.GetSymbol(targetHandle);
        if (targetSym is null)
            return false;

        var scope = GetOwnedScope(targetSym);
        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null)
                continue;

            foreach (var sym in scope.Symbols.Values)
            {
                if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                {
                    if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                    {
                        if (method.Parameters.Count == 1)
                        {
                            var p0 = context.GetParameterSymbol(method.Parameters[0])?.Type ?? TypeRef.Unresolved;
                            opMethod = method;
                            operandType = p0;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to find an implicit conversion operator (<see cref="OperatorKind.ImplicitCast"/>)
    /// converting from <paramref name="from"/> type to <paramref name="to"/> type.
    /// </summary>
    /// <param name="from">The source semantic type.</param>
    /// <param name="to">The destination semantic type.</param>
    /// <param name="castMethod">The matching implicit cast method if found.</param>
    /// <returns><see langword="true"/> if a matching implicit conversion was found; otherwise <see langword="false"/>.</returns>
    private bool TryResolveImplicitCast(TypeRef from, TypeRef to, out MethodSymbol? castMethod)
    {
        castMethod = null;
        if (!context.DiscoveredOperators.TryGetOperators(OperatorKind.ImplicitCast, out var candidates))
            return false;

        var operandHandles = new List<SymbolHandle>();
        if (from.Handle is { } fh) operandHandles.Add(fh);
        else if (from.Kind is TypeRefKind.Optional && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var optFromH)) operandHandles.Add(optFromH);

        if (to.Handle is { } th && !operandHandles.Contains(th)) operandHandles.Add(th);
        else if (to.Kind is TypeRefKind.Optional && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var optToH) && !operandHandles.Contains(optToH)) operandHandles.Add(optToH);

        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null)
                continue;

            foreach (var opHandle in operandHandles)
            {
                var operandTypeSym = (Symbol?)context.GetTypeSymbol(opHandle) ?? context.GetSymbol(opHandle);
                if (operandTypeSym is null)
                    continue;

                Dictionary<SymbolHandle, TypeRef>? typeArgs = null;
                if (to.SpecialType is OptionalSemanticType optTo && operandTypeSym is TypeSymbol toSym && toSym.GenericParameters.Count == 1)
                {
                    typeArgs = new Dictionary<SymbolHandle, TypeRef> { [toSym.GenericParameters[0]] = optTo.ElementType };
                }
                else if (from.SpecialType is OptionalSemanticType optFrom && operandTypeSym is TypeSymbol fromSym && fromSym.GenericParameters.Count == 1)
                {
                    typeArgs = new Dictionary<SymbolHandle, TypeRef> { [fromSym.GenericParameters[0]] = optFrom.ElementType };
                }

                var scope = GetOwnedScope(operandTypeSym);
                foreach (var sym in scope.Symbols.Values)
                {
                    if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                    {
                        if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                        {
                            if (method.Parameters.Count == 1)
                            {
                                var paramType = context.GetParameterSymbol(method.Parameters[0])?.Type ?? TypeRef.Unresolved;
                                var effectiveParam = typeArgs is not null ? SubstituteTypeRef(paramType, typeArgs) : paramType;
                                var effectiveReturn = typeArgs is not null ? SubstituteTypeRef(method.ReturnType, typeArgs) : method.ReturnType;

                                if ((effectiveParam.Equals(from) || effectiveParam.IsAssignableFrom(from, context)) &&
                                    (to.Equals(effectiveReturn) || to.IsAssignableFrom(effectiveReturn, context)))
                                {
                                    castMethod = method;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Searches for a publicly accessible member on the specified type by name,
    /// filtering out interface-scoped operator methods.
    /// </summary>
    /// <param name="targetType">The type to query.</param>
    /// <param name="memberName">The name of the member to find.</param>
    /// <returns>The matching symbol if found; otherwise <see langword="null"/>.</returns>
    private Symbol? LookupMemberOnType(TypeRef targetType, string memberName)
    {
        if (targetType.Handle is not { } handle)
            return null;

        var sym = (Symbol?)context.GetTypeSymbol(handle) ?? context.GetSymbol(handle);
        if (sym is not null)
        {
            var scope = GetOwnedScope(sym);
            var found = scope[new SymbolName(new SymbolPart(memberName))];
            var accessible = found.Where(cand => cand is not MethodSymbol m || !m.Flags.HasFlag(FunctionFlags.InterfaceScoped)).ToList();
            if (accessible.Count > 0)
                return accessible[0];
        }

        return null;
    }

    /// <summary>
    /// Checks whether a literal expression syntax node is type-compatible with a destination target type.
    /// </summary>
    /// <param name="lit">The literal expression syntax node.</param>
    /// <param name="targetType">The destination type.</param>
    /// <returns><see langword="true"/> if compatible; otherwise <see langword="false"/>.</returns>
    private bool IsLiteralCompatibleWith(LiteralExpression lit, TypeRef targetType)
    {
        if (targetType.IsLiteral)
            return true;

        if (lit.Literal.Kind is TokenKind.Integer)
        {
            if (IsNumeric(targetType)) return true;
        }
        else if (lit.Literal.Kind is TokenKind.Float)
        {
            if (IsNumeric(targetType)) return true;
        }
        else if (lit.Literal.Kind is TokenKind.String)
        {
            if (hStr is { } hs && targetType.Handle == hs) return true;
        }
        else if (lit.Literal.Kind is TokenKind.Char)
        {
            if (hChar is { } hc && targetType.Handle == hc) return true;
        }

        if (targetType.Handle is { } th)
        {
            var typeSym = (Symbol?)context.GetTypeSymbol(th) ?? context.GetSymbol(th);
            if (typeSym is not null)
            {
                var scope = GetOwnedScope(typeSym);
                foreach (var sym in scope.Symbols.Values)
                {
                    if (sym is MethodSymbol m && m.Flags.HasFlag(FunctionFlags.Literal))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether an uncollapsed compile-time literal semantic type is compatible with a destination target type.
    /// </summary>
    /// <param name="litType">The uncollapsed literal <see cref="TypeRef"/>.</param>
    /// <param name="targetType">The destination type.</param>
    /// <returns><see langword="true"/> if compatible; otherwise <see langword="false"/>.</returns>
    private bool IsLiteralCompatibleWith(TypeRef litType, TypeRef targetType)
    {
        if (targetType.IsLiteral)
            return true;

        if (litType.SpecialType is CompileTimeLiteralType lit)
        {
            if (lit.LiteralKind is CompileTimeLiteralKind.Int && IsNumeric(targetType)) return true;
            if (lit.LiteralKind is CompileTimeLiteralKind.Float && IsNumeric(targetType)) return true;
            if (lit.LiteralKind is CompileTimeLiteralKind.Str && hStr is { } hs && targetType.Handle == hs) return true;
            if (lit.LiteralKind is CompileTimeLiteralKind.Char && hChar is { } hc && targetType.Handle == hc) return true;
        }

        if (targetType.Handle is { } th)
        {
            var typeSym = (Symbol?)context.GetTypeSymbol(th) ?? context.GetSymbol(th);
            if (typeSym is not null)
            {
                var scope = GetOwnedScope(typeSym);
                foreach (var sym in scope.Symbols.Values)
                {
                    if (sym is MethodSymbol m && m.Flags.HasFlag(FunctionFlags.Literal))
                        return true;
                }
            }
        }

        return false;
    }

    private static TypeRef SubstituteTypeRef(TypeRef type, Dictionary<SymbolHandle, TypeRef> typeArgs) =>
        type.Substitute(typeArgs);

    private TypeRef? GetEnclosingFunctionReturnType(SymbolHandle containingFunction)
    {
        if (containingFunction != default)
        {
            var sym = context.GetSymbol(containingFunction);
            if (sym is FunctionSymbol fn) return fn.ReturnType;
            if (sym is MethodSymbol m) return m.ReturnType;
        }
        return null;
    }

    private void CheckLiteralRangeOverflow(Expression expr, TypeRef targetType)
    {
        if (targetType.Handle is not { } th)
            return;

        var unwrapped = context.UnwrapAlias(th);
        if (!context.DiscoveredIntrinsics.TryGetIntrinsicName(unwrapped, out var intrinsicName))
            return;

        System.Numerics.BigInteger? val = null;
        string? text = null;
        TextSpan span = expr.GetSpan() ?? default;

        if (expr is LiteralExpression lit && lit.Literal.Kind is TokenKind.Integer or TokenKind.SuffixedInteger)
        {
            text = lit.Literal.ValueWithoutSuffix;
            if (System.Numerics.BigInteger.TryParse(text, out var bi))
                val = bi;
        }
        else if (expr is UnaryExpression u && u.OperatorToken.Kind is TokenKind.Minus &&
                 u.Operand is LiteralExpression uLit && uLit.Literal.Kind is TokenKind.Integer or TokenKind.SuffixedInteger)
        {
            text = "-" + uLit.Literal.ValueWithoutSuffix;
            if (System.Numerics.BigInteger.TryParse(uLit.Literal.ValueWithoutSuffix, out var bi))
                val = -bi;
        }

        if (val is { } bigInt && text is not null)
        {
            if (!FitsInIntegerType(bigInt, intrinsicName, out var min, out var max))
            {
                context.Diagnostics.ReportLiteralOverflowWarning(
                    span,
                    text,
                    intrinsicName,
                    min,
                    max,
                    expr.GetSource());
            }
        }
    }

    private static bool FitsInIntegerType(System.Numerics.BigInteger val, string intrinsicName, out string min, out string max)
    {
        switch (intrinsicName)
        {
            case KnownIntrinsics.Int8:
                min = "-128"; max = "127";
                return val >= -128 && val <= 127;
            case KnownIntrinsics.UInt8:
                min = "0"; max = "255";
                return val >= 0 && val <= 255;
            case KnownIntrinsics.Int16:
                min = "-32768"; max = "32767";
                return val >= -32768 && val <= 32767;
            case KnownIntrinsics.UInt16:
                min = "0"; max = "65535";
                return val >= 0 && val <= 65535;
            case KnownIntrinsics.Int32:
                min = "-2147483648"; max = "2147483647";
                return val >= int.MinValue && val <= int.MaxValue;
            case KnownIntrinsics.UInt32:
                min = "0"; max = "4294967295";
                return val >= 0 && val <= uint.MaxValue;
            case KnownIntrinsics.Int64:
            case KnownIntrinsics.NInt:
                min = "-9223372036854775808"; max = "9223372036854775807";
                return val >= long.MinValue && val <= long.MaxValue;
            case KnownIntrinsics.UInt64:
            case KnownIntrinsics.NUInt:
                min = "0"; max = "18446744073709551615";
                return val >= 0 && val <= ulong.MaxValue;
            case KnownIntrinsics.Int128:
                min = System.Int128.MinValue.ToString(); max = System.Int128.MaxValue.ToString();
                return val >= (System.Numerics.BigInteger)System.Int128.MinValue && val <= (System.Numerics.BigInteger)System.Int128.MaxValue;
            case KnownIntrinsics.UInt128:
                min = "0"; max = System.UInt128.MaxValue.ToString();
                return val >= 0 && val <= (System.Numerics.BigInteger)System.UInt128.MaxValue;
            default:
                min = ""; max = "";
                return true;
        }
    }
}
