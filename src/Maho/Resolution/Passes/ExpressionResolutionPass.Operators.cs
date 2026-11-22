using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class ExpressionResolutionPass
{
    /// <summary>
    /// Resolves the semantic type of a binary operator expression.
    /// <para>
    /// Resolution proceeds through five sequential tiers:
    /// 1. Uncollapsed literals on both sides (e.g. <c>10 + 20</c>): comparisons yield <c>bool</c>;
    ///    arithmetic preserves the uncollapsed literal kind.
    /// 2. Canonical boolean operations (<c>&amp;&amp;</c>, <c>||</c>) yielding <c>bool</c>.
    /// 3. Mixed binary expressions with one literal and one concrete operand (e.g. <c>10 + myInt64</c>):
    ///    dispatches to <see cref="TryResolveOneLiteralBinary"/> to find an operator overload on the concrete type
    ///    and collapse the literal to the operator parameter's expected type.
    /// 4. Concrete operand operator overload lookup via <see cref="DiscoveredOperators"/>
    ///    by inspecting explicit interface implementations (<c>&lt;T as Add&gt;::Add</c>) on either operand type.
    /// 5. Diagnostic reporting: emits <c>MH2001</c> if no compatible operator overload exists.
    /// </para>
    /// </summary>
    /// <param name="binary">The binary expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The resolved semantic type of the binary operation, or <see cref="TypeRef.Error"/> on failure.</returns>
    private TypeRef ResolveBinary(BinaryExpression binary, Scope scope, SymbolHandle containingFunction)
    {
        var lhs = ResolveExpression(binary.LeftExpression, scope, containingFunction);
        var rhs = ResolveExpression(binary.RightExpression, scope, containingFunction);

        var opKind = GetBinaryOperatorKind(binary.OperatorToken.Kind);
        if (opKind == OperatorKind.None)
            return TypeRef.Error;

        // 1. Uncollapsed literals on both sides:
        // Comparison operators evaluate to canonical bool; arithmetic preserves uncollapsed literal type
        if (lhs.IsLiteral && rhs.IsLiteral)
        {
            var lLit = lhs.AsLiteral!;
            var rLit = rhs.AsLiteral!;
            if (lLit.LiteralKind == rLit.LiteralKind)
            {
                if (opKind is OperatorKind.Equality or OperatorKind.Inequality or
                    OperatorKind.LessThan or OperatorKind.LessThanOrEqual or
                    OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual)
                {
                    return boolType;
                }

                return TypeRef.Literal(lLit.LiteralKind, "");
            }
        }

        // 2. Canonical bool operations (&&, ||)
        if ((opKind == OperatorKind.LogicalAnd || opKind == OperatorKind.LogicalOr) && IsBool(lhs) && IsBool(rhs))
            return boolType;

        // 3. One literal operand and one concrete operand (e.g. 10 + myInt64 or myInt64 + 10)
        // Collapses the literal operand directly to the operator parameter type without falling back to Int32
        if (lhs.IsLiteral && !rhs.IsLiteral && !rhs.IsUnresolved && !rhs.IsError)
        {
            if (TryResolveOneLiteralBinary(opKind, binary, binary.LeftExpression, lhs, rhs, isLhsLiteral: true, scope, containingFunction, out var res))
                return res;
        }
        else if (rhs.IsLiteral && !lhs.IsLiteral && !lhs.IsUnresolved && !lhs.IsError)
        {
            if (TryResolveOneLiteralBinary(opKind, binary, binary.RightExpression, rhs, lhs, isLhsLiteral: false, scope, containingFunction, out var res))
                return res;
        }

        // 4. Operator overload lookup through DiscoveredOperators for concrete operands
        if (TryResolveOperatorOverload(opKind, lhs, rhs, binary, out var overloadResult))
            return overloadResult;

        // 5. No compatible operator overload found: report MH2001
        if (!lhs.IsUnresolved && !lhs.IsError && !lhs.IsInferred && !rhs.IsUnresolved && !rhs.IsError && !rhs.IsInferred)
        {
            var opName = KnownIntrinsics.GetOperatorName(opKind);
            context.Diagnostics.ReportTypeMismatch(
                binary.OperatorToken.Span,
                binary.OperatorToken.Value,
                binary.LeftExpression.GetSpan() ?? default,
                lhs.ToQualifiedString(context),
                binary.RightExpression.GetSpan() ?? default,
                rhs.ToQualifiedString(context),
                lhsLabel: lhs.ToLabelString(context, scope),
                rhsLabel: rhs.ToLabelString(context, scope),
                opName: opName,
                source: binary.GetSource());
        }

        return TypeRef.Error;
    }

    /// <summary>
    /// Attempts to resolve a binary operator where one operand is an unconstrained compile-time literal
    /// and the other is a concrete type.
    /// <para>
    /// Searches candidate operator interface implementations registered on the concrete operand type.
    /// If an operator method is found where the concrete parameter matches and the literal is compatible
    /// with the literal parameter, the literal operand is eagerly collapsed to the parameter type and bound.
    /// </para>
    /// </summary>
    /// <param name="kind">The binary operator kind.</param>
    /// <param name="binary">The binary expression syntax node.</param>
    /// <param name="literalExpr">The literal expression AST node.</param>
    /// <param name="literalType">The uncollapsed literal type of the literal operand.</param>
    /// <param name="concreteType">The concrete semantic type of the other operand.</param>
    /// <param name="isLhsLiteral"><see langword="true"/> if LHS is the literal; <see langword="false"/> if RHS is.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <param name="result">The resulting return type of the operator overload if found.</param>
    /// <returns><see langword="true"/> if an operator overload was found and bound; otherwise <see langword="false"/>.</returns>
    private bool TryResolveOneLiteralBinary(
        OperatorKind kind,
        BinaryExpression binary,
        Expression literalExpr,
        TypeRef literalType,
        TypeRef concreteType,
        bool isLhsLiteral,
        Scope scope,
        SymbolHandle containingFunction,
        out TypeRef result)
    {
        result = TypeRef.Unresolved;
        if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
            return false;

        if (concreteType.Handle is not { } cHandle || context.GetTypeSymbol(cHandle) is not { } cTypeSym)
            return false;

        var typeScope = GetOwnedScope(cTypeSym);
        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null) continue;

            foreach (var sym in typeScope.Symbols.Values)
            {
                if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                {
                    if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                    {
                        if (method.Parameters.Count == 2)
                        {
                            var p0 = context.GetParameterSymbol(method.Parameters[0]);
                            var p1 = context.GetParameterSymbol(method.Parameters[1]);
                            if (p0 is null || p1 is null) continue;

                            var litParam = isLhsLiteral ? p0.Type : p1.Type;
                            var concParam = isLhsLiteral ? p1.Type : p0.Type;

                            if (concParam.IsAssignableFrom(concreteType, context) && IsLiteralCompatibleWith(literalType, litParam))
                            {
                                CollapseExpression(literalExpr, litParam, scope, containingFunction);
                                context.ResolvedTree.AddReference(binary, ResolutionContext.GetHandle(method));
                                result = method.ReturnType;
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
    /// Maps a Maho binary operator token kind to its corresponding semantic <see cref="OperatorKind"/>.
    /// </summary>
    /// <param name="tokenKind">The token kind from the lexer.</param>
    /// <returns>The mapped <see cref="OperatorKind"/>, or <see cref="OperatorKind.None"/> if not an operator.</returns>
    private static OperatorKind GetBinaryOperatorKind(TokenKind tokenKind) => tokenKind switch
    {
        TokenKind.Plus => OperatorKind.Addition,
        TokenKind.Minus => OperatorKind.Subtraction,
        TokenKind.Asterisk => OperatorKind.Multiplication,
        TokenKind.ForwardSlash => OperatorKind.Division,
        TokenKind.Percentage => OperatorKind.Remainder,
        TokenKind.Ampersand => OperatorKind.BitwiseAnd,
        TokenKind.VerticalBar => OperatorKind.BitwiseOr,
        TokenKind.Caret => OperatorKind.BitwiseXor,
        TokenKind.LessThanLessThanSigns => OperatorKind.LeftShift,
        TokenKind.GreaterThanGreaterThanSigns => OperatorKind.RightShift,
        TokenKind.LessThanLessThanLessThanSigns => OperatorKind.RotateLeft,
        TokenKind.EqualsEquals => OperatorKind.Equality,
        TokenKind.ExclamationEquals => OperatorKind.Inequality,
        TokenKind.LessThanSign => OperatorKind.LessThan,
        TokenKind.LessThanEquals => OperatorKind.LessThanOrEqual,
        TokenKind.GreaterThanSign => OperatorKind.GreaterThan,
        TokenKind.GreaterThanEquals => OperatorKind.GreaterThanOrEqual,
        TokenKind.AmpersandAmpersand => OperatorKind.LogicalAnd,
        TokenKind.VerticalBarVerticalBar => OperatorKind.LogicalOr,
        _ => OperatorKind.None
    };

    /// <summary>
    /// Attempts to find a user-defined binary operator overload implemented on either operand type.
    /// <para>
    /// In Maho, operators are declared as explicit interface implementations (<c>&lt;T as Add&gt;::Add</c>).
    /// This method searches candidate operator methods registered in <see cref="DiscoveredOperators"/>
    /// that are implemented in the owned scope of either the LHS or RHS operand type.
    /// </para>
    /// </summary>
    /// <param name="kind">The operator kind to look up.</param>
    /// <param name="lhs">The semantic type of the left operand.</param>
    /// <param name="rhs">The semantic type of the right operand.</param>
    /// <param name="syntax">The syntax node to bind in the resolution tree if matched.</param>
    /// <param name="result">The return type of the matching operator method.</param>
    /// <returns><see langword="true"/> if a matching operator overload was found and bound; otherwise <see langword="false"/>.</returns>
    private bool TryResolveOperatorOverload(
        OperatorKind kind,
        TypeRef lhs,
        TypeRef rhs,
        SyntaxNode syntax,
        out TypeRef result)
    {
        result = TypeRef.Unresolved;

        if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
            return false;

        // Operators must resolve to explicit Interface.Operator methods on operand type (LHS or RHS)
        var operandHandles = new List<SymbolHandle>();
        if (lhs.Handle is { } lh) operandHandles.Add(lh);
        if (rhs.Handle is { } rh && !operandHandles.Contains(rh)) operandHandles.Add(rh);

        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null)
                continue;

            foreach (var opHandle in operandHandles)
            {
                var operandTypeSym = context.GetTypeSymbol(opHandle);
                if (operandTypeSym is null)
                    continue;

                var scope = GetOwnedScope(operandTypeSym);
                foreach (var sym in scope.Symbols.Values)
                {
                    if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                    {
                        if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                        {
                            // Verify parameter types match operands
                            if (method.Parameters.Count == 2)
                            {
                                var p0 = context.GetParameterSymbol(method.Parameters[0]);
                                var p1 = context.GetParameterSymbol(method.Parameters[1]);

                                if (p0 is not null && p1 is not null &&
                                    p0.Type.IsAssignableFrom(lhs, context) &&
                                    p1.Type.IsAssignableFrom(rhs, context))
                                {
                                    context.ResolvedTree.AddReference(syntax, ResolutionContext.GetHandle(method));
                                    result = method.ReturnType;
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
    /// Resolves the semantic type of a unary operator expression.
    /// <para>
    /// Supports built-in pointer and reference operators (<c>&amp;</c> address-of, <c>*</c> dereference),
    /// canonical boolean negation (<c>!bool</c>), uncollapsed compile-time literals (preserving uncollapsed type),
    /// and user-defined unary operator overloads via <see cref="DiscoveredOperators"/>.
    /// Emits <c>MH2002</c> if no compatible unary operator exists.
    /// </para>
    /// </summary>
    /// <param name="unary">The unary expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The resolved semantic type of the unary operation, or <see cref="TypeRef.Error"/> on failure.</returns>
    private TypeRef ResolveUnary(UnaryExpression unary, Scope scope, SymbolHandle containingFunction)
    {
        var operand = ResolveExpression(unary.Operand, scope, containingFunction);
        var opKind = GetUnaryOperatorKind(unary.OperatorToken.Kind, unary.Position);

        if (opKind == OperatorKind.None)
            return TypeRef.Error;

        if (operand.SpecialType is AliasSemanticType operandAlias)
            operand = operandAlias.TargetType;

        // 1. Built-in language pointer/reference operations
        if (opKind == OperatorKind.AddressOf)
        {
            if (!IsExpressionLValue(unary.Operand, operand))
            {
                context.Diagnostics.ReportInvalidReceiverForByRefThis(
                    unary.Operand.GetSpan() ?? default,
                    "&",
                    "AddressOf",
                    unary.Operand.GetSource());
                return TypeRef.Error;
            }

            if (TryResolveUnaryOverload(OperatorKind.AddressOf, operand, unary, out var addrResult))
                return addrResult;

            return TypeRef.Reference(operand);
        }

        if (opKind == OperatorKind.Dereference)
        {
            if (TryResolveUnaryOverload(OperatorKind.Dereference, operand, unary, out var derefResult))
                return derefResult;

            if (operand.SpecialType is ReferenceSemanticType refType)
                return TypeRef.Alias(refType.ElementType, isMutable: refType.IsMutable);
            if (operand.SpecialType is PointerSemanticType ptrType)
                return TypeRef.Alias(ptrType.ElementType, isMutable: ptrType.IsMutable);
            return TypeRef.Error;
        }

        // 2. Increment and decrement operators require an assignable L-value operand
        if (opKind is OperatorKind.PrefixIncrement or OperatorKind.PostfixIncrement or OperatorKind.PrefixDecrement or OperatorKind.PostfixDecrement)
        {
            if (!IsExpressionLValue(unary.Operand, operand))
            {
                context.Diagnostics.ReportCannotAssignToNonLValue(
                    unary.Operand.GetSpan() ?? default,
                    unary.Operand.GetSource());
                return TypeRef.Error;
            }
        }

        // 3. Canonical bool negation
        if (opKind == OperatorKind.LogicalNot && IsBool(operand))
            return boolType;

        // 4. Uncollapsed literal unary (e.g. -10 maintains uncollapsed Int literal)
        if (operand.IsLiteral)
            return operand;

        // 4. Overload lookup through DiscoveredOperators for user-defined types
        if (opKind == OperatorKind.Try)
        {
            if (TryResolveUnaryOverload(opKind, operand, unary, out var tryResult))
            {
                if (tryResult.SpecialType is UnionSemanticType union && union.Variants.Count == 2)
                {
                    var returnPayloadType = union.Variants[0];
                    var failType = union.Variants[1];

                    // Check enclosing function return type
                    var enclosingReturn = GetEnclosingFunctionReturnType(containingFunction);
                    if (enclosingReturn is { IsError: false, IsUnresolved: false } encRet)
                    {
                        if (!encRet.IsAssignableFrom(failType, context) &&
                            !TryResolveImplicitCast(failType, encRet, out _))
                        {
                            context.Diagnostics.ReportReturnTypeMismatch(
                                unary.OperatorToken.Span,
                                failType.ToQualifiedString(context),
                                encRet.ToQualifiedString(context),
                                exprLabel: failType.ToLabelString(context, scope),
                                returnLabel: encRet.ToLabelString(context, scope),
                                source: unary.GetSource());
                        }
                    }

                    return returnPayloadType;
                }
                return tryResult;
            }
        }
        else if (TryResolveUnaryOverload(opKind, operand, unary, out var overloadResult))
        {
            return overloadResult;
        }

        // 5. No unary overload found: report MH2002
        if (!operand.IsUnresolved && !operand.IsError && !operand.IsInferred)
        {
            var opName = KnownIntrinsics.GetOperatorName(opKind);
            context.Diagnostics.ReportUnaryTypeMismatch(
                unary.OperatorToken.Span,
                unary.OperatorToken.Value,
                unary.Operand.GetSpan() ?? default,
                operand.ToQualifiedString(context),
                operandLabel: operand.ToLabelString(context, scope),
                opName: opName,
                source: unary.GetSource());
        }

        return TypeRef.Error;
    }

    /// <summary>
    /// Maps a unary token kind and position (prefix vs. postfix) to its corresponding <see cref="OperatorKind"/>.
    /// </summary>
    /// <param name="tokenKind">The operator token kind.</param>
    /// <param name="position">Whether the unary operator appears as a prefix or postfix.</param>
    /// <returns>The mapped <see cref="OperatorKind"/>, or <see cref="OperatorKind.None"/> if unmapped.</returns>
    private static OperatorKind GetUnaryOperatorKind(TokenKind tokenKind, UnaryPosition position) => tokenKind switch
    {
        TokenKind.Minus => OperatorKind.UnaryNegation,
        TokenKind.Plus => OperatorKind.UnaryPositive,
        TokenKind.ExclamationMark => position == UnaryPosition.Prefix ? OperatorKind.LogicalNot : OperatorKind.Unwrap,
        TokenKind.QuestionMark => position == UnaryPosition.Postfix ? OperatorKind.Try : OperatorKind.None,
        TokenKind.Tilde => OperatorKind.BitwiseNot,
        TokenKind.PlusPlus => position == UnaryPosition.Prefix ? OperatorKind.PrefixIncrement : OperatorKind.PostfixIncrement,
        TokenKind.MinusMinus => position == UnaryPosition.Prefix ? OperatorKind.PrefixDecrement : OperatorKind.PostfixDecrement,
        TokenKind.Ampersand => OperatorKind.AddressOf,
        TokenKind.Asterisk => OperatorKind.Dereference,
        _ => OperatorKind.None
    };

    /// <summary>
    /// Attempts to find a user-defined unary operator overload implemented on the operand type.
    /// </summary>
    /// <param name="kind">The unary operator kind.</param>
    /// <param name="operand">The semantic type of the operand.</param>
    /// <param name="syntax">The syntax node to bind in the resolution tree if matched.</param>
    /// <param name="result">The return type of the matching unary operator method.</param>
    /// <returns><see langword="true"/> if a matching unary operator was found; otherwise <see langword="false"/>.</returns>
    private bool TryResolveUnaryOverload(OperatorKind kind, TypeRef operand, SyntaxNode syntax, out TypeRef result)
    {
        result = TypeRef.Unresolved;

        if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
            return false;

        var opHandle = operand.Handle;
        if (opHandle is null)
        {
            if (operand.SpecialType is ReferenceSemanticType refSem)
            {
                string req = refSem.IsMutable ? KnownIntrinsics.MutableReference : KnownIntrinsics.Reference;
                if (context.DiscoveredIntrinsics.TryGet(req, out var refH))
                    opHandle = refH;
            }
            else if (operand.SpecialType is PointerSemanticType ptrSem)
            {
                string req = ptrSem.IsMutable ? KnownIntrinsics.MutablePointer : KnownIntrinsics.Pointer;
                if (context.DiscoveredIntrinsics.TryGet(req, out var ptrH))
                    opHandle = ptrH;
            }
            else if (operand.Kind == TypeRefKind.Optional && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var hOpt))
                opHandle = hOpt;
            else if (operand.Kind == TypeRefKind.FixedArray && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.FixedArray, out var hFix))
                opHandle = hFix;
            else if (operand.Kind == TypeRefKind.Array && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var hArr))
                opHandle = hArr;
            else if (operand.Kind == TypeRefKind.Span && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Span, out var hSpan))
                opHandle = hSpan;
            else if (operand.Kind == TypeRefKind.Tuple && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var hTup))
                opHandle = hTup;
        }

        if (opHandle is null)
            return false;

        var operandTypeSym = (Symbol?)context.GetTypeSymbol(opHandle.Value) ?? context.GetSymbol(opHandle.Value);
        if (operandTypeSym is null)
            return false;

        // Build generic parameter mapping if operand is generic (e.g. Optional<T>, Reference<T>, Pointer<T>)
        Dictionary<SymbolHandle, TypeRef>? typeArgs = null;
        if (operandTypeSym is TypeSymbol ts && ts.GenericParameters.Count == 1)
        {
            if (operand.SpecialType is OptionalSemanticType optSem)
                typeArgs = new Dictionary<SymbolHandle, TypeRef> { [ts.GenericParameters[0]] = optSem.ElementType };
            else if (operand.SpecialType is ReferenceSemanticType refSem)
                typeArgs = new Dictionary<SymbolHandle, TypeRef> { [ts.GenericParameters[0]] = refSem.ElementType };
            else if (operand.SpecialType is PointerSemanticType ptrSem)
                typeArgs = new Dictionary<SymbolHandle, TypeRef> { [ts.GenericParameters[0]] = ptrSem.ElementType };
        }
        else if (operand.SpecialType is GenericSemanticType gst && operandTypeSym is TypeSymbol tsGen && tsGen.GenericParameters.Count == gst.TypeArguments.Count)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef>();
            for (int i = 0; i < tsGen.GenericParameters.Count; i++)
                typeArgs[tsGen.GenericParameters[i]] = gst.TypeArguments[i];
        }

        IReadOnlyList<TypeRef> baseTypes = operandTypeSym switch
        {
            TypeSymbol tSym => tSym.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            _ => []
        };

        var scope = GetOwnedScope(operandTypeSym);
        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null)
                continue;

            foreach (var sym in scope.Symbols.Values)
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
                            method.Name.Text == opMethod.Name.Text)
                        {
                            isCandidate = true;
                        }
                    }

                    if (isCandidate)
                    {
                        // Check if instance method on receiver or static unary accepting operand
                        if (method.Parameters.Count == 0)
                        {
                            context.ResolvedTree.AddReference(syntax, ResolutionContext.GetHandle(method));
                            var rawReturn = method.ReturnType;
                            result = typeArgs is not null ? SubstituteTypeRef(rawReturn, typeArgs) : rawReturn;
                            return true;
                        }
                        else if (method.Parameters.Count == 1 && context.GetParameterSymbol(method.Parameters[0]) is { } p)
                        {
                            if (p.Syntax?.Declarator.IsReceiver == true)
                            {
                                bool requiresMut = p.Syntax.Declarator.ReceiverKind is ReceiverKind.MutAlias or ReceiverKind.MutReference or ReceiverKind.MutPointer or ReceiverKind.MutUnsafeAlias;
                                if (requiresMut)
                                {
                                    bool isOperandMut = false;
                                    if (operand.SpecialType is ReferenceSemanticType rSem)
                                        isOperandMut = rSem.IsMutable;
                                    else if (operand.SpecialType is PointerSemanticType pSem)
                                        isOperandMut = pSem.IsMutable;
                                    else if (operand.SpecialType is AliasSemanticType aSem)
                                        isOperandMut = aSem.IsMutable;
                                    else if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.MutablePointer, out var mpH) && operand.Handle == mpH)
                                        isOperandMut = true;
                                    else if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.MutableReference, out var mrH) && operand.Handle == mrH)
                                        isOperandMut = true;

                                    if (!isOperandMut)
                                        continue;
                                }

                                context.ResolvedTree.AddReference(syntax, ResolutionContext.GetHandle(method));
                                var rawReturn = method.ReturnType;
                                result = typeArgs is not null ? SubstituteTypeRef(rawReturn, typeArgs) : rawReturn;
                                return true;
                            }
                            else if (p.Type.IsAssignableFrom(operand, context))
                            {
                                context.ResolvedTree.AddReference(syntax, ResolutionContext.GetHandle(method));
                                var rawReturn = method.ReturnType;
                                result = typeArgs is not null ? SubstituteTypeRef(rawReturn, typeArgs) : rawReturn;
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
    /// Resolves an assignment expression (<c>=</c>, <c>+=</c>, <c>-=</c>, etc.).
    /// <para>
    /// Verifies that the LHS denotes a valid assignable L-value (<c>MH2003</c>).
    /// For simple assignment (<c>=</c>), if the RHS is an uncollapsed literal, immediately collapses
    /// the RHS (and its retroactive subtree) to the LHS type.
    /// Checks type assignability and implicit conversion operators (<see cref="TryResolveImplicitCast"/>),
    /// reporting <c>MH2004</c> on mismatch.
    /// </para>
    /// </summary>
    /// <param name="assignment">The assignment expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The resolved type of the assignment (the LHS type), or <see cref="TypeRef.Error"/> on failure.</returns>
    private TypeRef ResolveAssignment(AssignmentExpression assignment, Scope scope, SymbolHandle containingFunction)
    {
        var lhs = ResolveExpression(assignment.LhsExpression, scope, containingFunction);
        var targetType = lhs;
        if (lhs.SpecialType is AliasSemanticType lhsAlias)
        {
            if (!lhsAlias.IsMutable)
            {
                context.Diagnostics.ReportCannotAssignToNonLValue(assignment.LhsExpression.GetSpan() ?? default, assignment.GetSource());
                return TypeRef.Error;
            }
            targetType = lhsAlias.TargetType;
        }

        if (!targetType.IsInferred && !targetType.IsUnresolved && !targetType.IsError && !targetType.IsLiteral)
            expectedExpressionTypes[assignment.RhsExpression] = targetType;
        var rhs = ResolveExpression(assignment.RhsExpression, scope, containingFunction);

        // 1. Verify that the LHS expression denotes a valid writable L-value
        if (!IsExpressionLValue(assignment.LhsExpression, lhs))
        {
            context.Diagnostics.ReportCannotAssignToNonLValue(assignment.LhsExpression.GetSpan() ?? default, assignment.GetSource());
            return TypeRef.Error;
        }

        // Cannot assign to Never
        if (targetType.IsNever(context))
        {
            context.Diagnostics.ReportAssignmentTypeMismatch(
                assignment.EqualsOperator.Span,
                rhs.ToQualifiedString(context),
                targetType.ToQualifiedString(context),
                rhsLabel: rhs.ToLabelString(context, scope),
                lhsLabel: targetType.ToLabelString(context, scope),
                source: assignment.GetSource());
            return TypeRef.Error;
        }

        // Bottom type semantics: if rhs is Never, it is unconditionally assignable to any target
        if (rhs.IsNever(context))
        {
            return targetType;
        }

        var opToken = assignment.EqualsOperator.Kind;
        if (opToken == TokenKind.Equals)
        {
            // 2a. If LHS is an unconstrained literal (e.g. var x = 100; x = Some();), collapse LHS to RHS type if compatible
            if (targetType.IsLiteral && !rhs.IsLiteral && !rhs.IsError && !rhs.IsUnresolved && !rhs.IsInferred)
            {
                if (IsLiteralCompatibleWith(targetType, rhs))
                {
                    var collapsed = CollapseExpression(assignment.LhsExpression, rhs, scope, containingFunction);
                    return collapsed.IsError ? TypeRef.Error : rhs;
                }
            }

            // 2b. If RHS is an unconstrained literal, eagerly collapse it to the target LHS type
            if (rhs.IsLiteral && !targetType.IsLiteral && !targetType.IsError && !targetType.IsUnresolved && !targetType.IsInferred)
            {
                var collapsed = CollapseExpression(assignment.RhsExpression, targetType, scope, containingFunction);
                return collapsed.IsError ? TypeRef.Error : targetType;
            }

            // 3. Verify type compatibility: assignability or registered implicit conversion operator
            if (!targetType.IsAssignableFrom(rhs, context) &&
                !TryResolveImplicitCast(rhs, targetType, out _) &&
                !targetType.IsError && !targetType.IsUnresolved && !targetType.IsInferred &&
                !rhs.IsError && !rhs.IsUnresolved && !rhs.IsInferred &&
                assignment.RhsExpression is not CollectionExpression)
            {
                if (TypeRef.IsNonContiguousSplitBase(targetType, rhs, context, out var splitBaseName))
                {
                    context.Diagnostics.ReportNonContiguousBaseConversion(
                        assignment.RhsExpression.GetSpan() ?? assignment.EqualsOperator.Span,
                        splitBaseName!,
                        assignment.GetSource());
                }
                else
                {
                    context.Diagnostics.ReportAssignmentTypeMismatch(
                        assignment.EqualsOperator.Span,
                        rhs.ToQualifiedString(context),
                        targetType.ToQualifiedString(context),
                        rhsLabel: rhs.ToLabelString(context, scope),
                        lhsLabel: targetType.ToLabelString(context, scope),
                        source: assignment.GetSource());
                }
            }

            return targetType;
        }

        // Compound assignments (+=, -=, etc.) return LHS type
        if (lhs.IsLiteral && !rhs.IsLiteral && !rhs.IsError && !rhs.IsUnresolved && !rhs.IsInferred)
        {
            if (IsLiteralCompatibleWith(lhs, rhs))
            {
                var collapsed = CollapseExpression(assignment.LhsExpression, rhs, scope, containingFunction);
                return collapsed.IsError ? TypeRef.Error : rhs;
            }
        }

        return lhs;
    }

    /// <summary>
    /// Determines whether the given expression denotes a valid L-value (assignable and addressable storage location).
    /// In Maho, any expression returning a mutable alias (<c>mut &amp;T</c> or <c>mut *T</c>) or reference is a valid L-value,
    /// as well as variables, parameters, fields, index expressions, pointer dereferences, and member access on L-values.
    /// </summary>
    /// <param name="expr">The AST expression node to inspect.</param>
    /// <param name="resolvedType">The resolved semantic type of the expression.</param>
    /// <returns><see langword="true"/> if the expression is an L-value; otherwise <see langword="false"/>.</returns>
    internal bool IsExpressionLValue(Expression expr, TypeRef resolvedType)
    {
        while (expr is ParenthesizedExpression p)
            expr = p.Expression;

        if (resolvedType.SpecialType is AliasSemanticType { IsMutable: true } or ReferenceSemanticType)
            return true;

        if (expr is UnaryExpression u && u.OperatorToken.Kind == TokenKind.Asterisk)
            return true;

        if (expr is IndexExpression)
            return true;

        if (expr is MemberAccessExpression member)
        {
            if (member.Dot.Kind == TokenKind.MinusGreaterThan)
                return true;
            var recvType = context.GetExpressionType(member.Expression) ?? TypeRef.Unresolved;
            return IsExpressionLValue(member.Expression, recvType);
        }

        if (expr is NamedExpression named)
        {
            if (named.Identifier.MatchingKind is MatchingKeywordKind.True or MatchingKeywordKind.False)
                return false;

            if (named.Identifier.MatchingKind is MatchingKeywordKind.This)
                return true;

            if (context.ResolvedTree.TryGetReference(named, out var h))
            {
                if (h.Kind is SymbolKind.Variable or SymbolKind.Parameter or SymbolKind.Field or SymbolKind.GlobalVariable)
                    return true;

                if (h.Kind == SymbolKind.Property && context.GetPropertySymbol(h) is { } prop && !prop.Flags.HasFlag(FunctionFlags.Static))
                    return true;

                return false;
            }

            return true;
        }

        return false;
    }
}
