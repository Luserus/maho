using System;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class IntrinsicDiscoveryPass
{
    private void DiscoverAndValidateOperatorFunctions()
    {
        var allMethods = context.MethodSymbols;

        foreach (var method in allMethods)
        {
            if (method.Syntax?.Attributes is not { Count: > 0 } attrLists)
                continue;

            foreach (var list in attrLists)
            {
                foreach (var app in list.Attributes)
                {
                    if (!MatchesAttributeName(app, KnownIntrinsics.OperatorFunctionAttribute, context.DiscoveredOperators.OperatorFunctionAttribute))
                        continue;

                    ValidateAndRegisterOperatorFunction(method, app);
                }
            }
        }
    }

    private void ValidateAndRegisterOperatorFunction(MethodSymbol method, AttributeApplication app)
    {
        if (method.Parent is null)
            return;

        var parentSym = context.GetSymbol(method.Parent.Value);
        if (parentSym is not TypeSymbol parentType || parentType.TypeKind != TypeKind.Interface)
        {
            var span = app.GetSpan() ?? method.Syntax?.Signature.Identifier.GetSpan() ?? default;
            context.Diagnostics.ReportInvalidOperatorFunctionSignature(span, "OperatorFunction", "operator functions must be declared in an interface", method.Syntax?.GetSource());
            return;
        }

        if (app.Arguments.Count != 1)
        {
            var span = app.GetSpan() ?? default;
            context.Diagnostics.ReportInvalidOperatorFunctionSignature(span, "OperatorFunction", "requires exactly one string literal argument", method.Syntax?.GetSource());
            return;
        }

        if (!TryExtractLiteralSpan(app.Arguments[0], out var nameSpan) || !KnownIntrinsics.TryGetOperatorKind(nameSpan, out var opKind))
        {
            var span = app.Arguments[0].GetSpan() ?? default;
            string displayVal = nameSpan.Length > 0 ? nameSpan.ToString() : (app.Arguments[0].ToString() ?? "");
            context.Diagnostics.ReportInvalidOperatorFunctionSignature(span, displayVal, $"unknown operator function '{displayVal}'", method.Syntax?.GetSource());
            return;
        }

        string fullName = KnownIntrinsics.GetOperatorName(opKind);
        bool hasReceiver = method.Syntax?.Signature.Parameters.Any(p => p.Declarator.IsReceiver) ?? false;
        bool isStatic = !hasReceiver && method.Flags.HasFlag(FunctionFlags.Static);
        int totalParamCount = method.Syntax?.Signature.Parameters.Count ?? method.Parameters.Count;
        int paramCount = hasReceiver ? totalParamCount - 1 : totalParamCount;
        var returnTypeSyntax = method.Syntax?.Signature.ReturnType;
        bool isVoidReturn = returnTypeSyntax is not null && IsVoidType(returnTypeSyntax, method.EnclosingScope);
        bool isRefReturn = returnTypeSyntax is not null && IsReferenceType(returnTypeSyntax, method.EnclosingScope);
        var sigSpan = method.Syntax?.Signature.Identifier.GetSpan() ?? app.GetSpan() ?? default;
        var source = method.Syntax?.GetSource();

        switch (opKind)
        {
            // Binary operators
            case OperatorKind.Addition:
            case OperatorKind.Subtraction:
            case OperatorKind.Multiplication:
            case OperatorKind.Division:
            case OperatorKind.Remainder:
            case OperatorKind.BitwiseAnd:
            case OperatorKind.BitwiseOr:
            case OperatorKind.BitwiseXor:
            case OperatorKind.LeftShift:
            case OperatorKind.RightShift:
            case OperatorKind.RotateLeft:
            case OperatorKind.RotateRight:
            case OperatorKind.Equality:
            case OperatorKind.Inequality:
            case OperatorKind.LessThan:
            case OperatorKind.LessThanOrEqual:
            case OperatorKind.GreaterThan:
            case OperatorKind.GreaterThanOrEqual:
            case OperatorKind.LogicalAnd:
            case OperatorKind.LogicalOr:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (paramCount != 2)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept exactly 2 parameters", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // Unary non-mutating
            case OperatorKind.UnaryNegation:
            case OperatorKind.UnaryPositive:
            case OperatorKind.LogicalNot:
            case OperatorKind.BitwiseNot:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (paramCount != 1)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept exactly 1 parameter", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // Cast operators
            case OperatorKind.ImplicitCast:
            case OperatorKind.ExplicitCast:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (paramCount != 1)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept exactly 1 parameter", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // Constructor
            case OperatorKind.Constructor:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "constructors cannot be declared static", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "constructors must return void", source);
                    return;
                }
                break;

            // Destructor
            case OperatorKind.Destructor:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "destructors cannot be declared static", source);
                    return;
                }
                if (paramCount != 0)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "destructors cannot accept parameters", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "destructors must return void", source);
                    return;
                }
                break;

            // AddressOf
            case OperatorKind.AddressOf:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount != 0)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "cannot accept parameters", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // Dereference & IndirectMemberAccess
            case OperatorKind.Dereference:
            case OperatorKind.IndirectMemberAccess:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount != 0)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "cannot accept parameters", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // Increment & Decrement
            case OperatorKind.PrefixIncrement:
            case OperatorKind.PostfixIncrement:
            case OperatorKind.PrefixDecrement:
            case OperatorKind.PostfixDecrement:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount != 0)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "cannot accept parameters", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // IndexerGetter
            case OperatorKind.IndexerGetter:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount < 1)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept at least 1 index parameter", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // IndexerSetter
            case OperatorKind.IndexerSetter:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount < 2)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept a value parameter and at least 1 index parameter", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type must be void", source);
                    return;
                }
                break;

            // Assignment & Compound Assignments
            case OperatorKind.Assignment:
            case OperatorKind.AdditionAssignment:
            case OperatorKind.SubtractionAssignment:
            case OperatorKind.MultiplicationAssignment:
            case OperatorKind.DivisionAssignment:
            case OperatorKind.RemainderAssignment:
            case OperatorKind.BitwiseAndAssignment:
            case OperatorKind.BitwiseOrAssignment:
            case OperatorKind.BitwiseXorAssignment:
            case OperatorKind.LeftShiftAssignment:
            case OperatorKind.RightShiftAssignment:
            case OperatorKind.RotateLeftAssignment:
            case OperatorKind.RotateRightAssignment:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount != 1)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept exactly 1 parameter", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type must be void", source);
                    return;
                }
                break;

            // New
            case OperatorKind.New:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // Put
            case OperatorKind.Put:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (paramCount < 1)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept at least 1 destination parameter", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type must be void", source);
                    return;
                }
                break;

            // Copy
            case OperatorKind.Copy:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (paramCount != 2)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept exactly 2 parameters (dest, source)", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type must be void", source);
                    return;
                }
                break;

            // Move
            case OperatorKind.Move:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (paramCount != 2)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept exactly 2 parameters (dest, source)", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type must be void", source);
                    return;
                }
                break;

            // Callable
            case OperatorKind.Callable:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                break;

            // Unwrap
            case OperatorKind.Unwrap:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount != 0)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "cannot accept parameters", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // Try
            case OperatorKind.Try:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount != 0)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "cannot accept parameters", source);
                    return;
                }
                if (isVoidReturn || returnTypeSyntax is not UnionType tryUnion || tryUnion.Variants.Count != 2)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type must be a two-variant union '(TReturn | TFail)'", source);
                    return;
                }
                break;
        }

        var methodHandle = ResolutionContext.GetHandle(method);
        var interfaceHandle = method.Parent.Value;
        context.DiscoveredOperators.Register(opKind, interfaceHandle, methodHandle, sigSpan, source);
    }

    /// <summary>
    /// Discovers and validates methods marked with <c>[IntrinsicFunction]</c>.
    /// Validates that they are declared inside an intrinsic type and accept exactly one string literal argument
    /// describing the member signature (e.g. "Int32.Add(Int32, Int32)", "Int32.&lt;i32&gt;"). Sets <see cref="FunctionFlags.Intrinsic"/> on valid methods.
    /// </summary>
    private void DiscoverAndValidateIntrinsicFunctions()
    {
        var allMethods = context.MethodSymbols;

        foreach (var method in allMethods)
        {
            if (method.Syntax?.Attributes is not { Count: > 0 } attrLists)
                continue;

            foreach (var list in attrLists)
            {
                foreach (var app in list.Attributes)
                {
                    if (!MatchesAttributeName(app, KnownIntrinsics.IntrinsicFunctionAttribute, context.DiscoveredOperators.IntrinsicFunctionAttribute))
                        continue;

                    if (app.Arguments.Count != 1)
                    {
                        var span = app.GetSpan() ?? default;
                        context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                            span,
                            "attribute 'IntrinsicFunction' requires exactly one argument",
                            method.Syntax?.GetSource(),
                            KnownIntrinsics.IntrinsicFunctionAttribute);
                        continue;
                    }

                    var argExpr = app.Arguments[0];
                    if (argExpr is not LiteralExpression lit || lit.Literal.Kind is not (TokenKind.String or TokenKind.SuffixedString))
                    {
                        var span = argExpr.GetSpan() ?? default;
                        context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                            span,
                            "argument to 'IntrinsicFunction' must be a string literal describing the member",
                            method.Syntax?.GetSource(),
                            KnownIntrinsics.IntrinsicFunctionAttribute);
                        continue;
                    }

                    if (method.Parent is null)
                        continue;

                    var parentSym = context.GetSymbol(method.Parent.Value);
                    bool isIntrinsicType = false;

                    if (parentSym is TypeSymbol)
                    {
                        foreach (var (_, handle) in context.DiscoveredIntrinsics.All)
                        {
                            if (handle == method.Parent.Value)
                            {
                                isIntrinsicType = true;
                                break;
                            }
                        }
                    }

                    if (!isIntrinsicType)
                    {
                        var span = app.GetSpan() ?? default;
                        context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                            span,
                            $"method '{method.Name.Text}' marked with [IntrinsicFunction] must be declared in an intrinsic type",
                            method.Syntax?.GetSource(),
                            KnownIntrinsics.IntrinsicFunctionAttribute);
                        continue;
                    }

                    method.Flags |= FunctionFlags.Intrinsic;
                    method.IntrinsicSignature = ExtractLiteralString(lit) ?? lit.Literal.Value;
                }
            }
        }

        foreach (var function in context.FunctionSymbols)
        {
            if (function.Syntax?.Attributes is not { Count: > 0 } attrLists)
                continue;

            foreach (var list in attrLists)
            {
                foreach (var app in list.Attributes)
                {
                    if (!MatchesAttributeName(app, KnownIntrinsics.IntrinsicFunctionAttribute, context.DiscoveredOperators.IntrinsicFunctionAttribute))
                        continue;

                    if (app.Arguments.Count != 1)
                    {
                        var span = app.GetSpan() ?? default;
                        context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                            span,
                            "attribute 'IntrinsicFunction' requires exactly one argument",
                            function.Syntax?.GetSource(),
                            KnownIntrinsics.IntrinsicFunctionAttribute);
                        continue;
                    }

                    var argExpr = app.Arguments[0];
                    if (argExpr is not LiteralExpression lit || lit.Literal.Kind is not (TokenKind.String or TokenKind.SuffixedString))
                    {
                        var span = argExpr.GetSpan() ?? default;
                        context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                            span,
                            "argument to 'IntrinsicFunction' must be a string literal describing the member",
                            function.Syntax?.GetSource(),
                            KnownIntrinsics.IntrinsicFunctionAttribute);
                        continue;
                    }

                    function.Flags |= FunctionFlags.Intrinsic;
                    function.IntrinsicSignature = ExtractLiteralString(lit) ?? lit.Literal.Value;
                }
            }
        }
    }

    private bool IsVoidType(TypeSyntax type, Scope scope)
    {
        if (type is UnionType)
            return false;

        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Unit, out var unitHandle))
        {
            var resolved = ResolveTypeSymbol(type, scope);
            if (resolved == unitHandle)
                return true;
        }

        return false;
    }

    private bool IsReferenceType(TypeSyntax type, Scope scope)
    {
        if (type is ModifiedType modified && modified.Modifier is ReferenceTypeModifier or SpanTypeModifier)
            return true;

        if (type is PrefixModifiedType prefix && prefix.Modifier is SafeAliasTypeModifier or UnsafeAliasTypeModifier)
            return true;

        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Reference, out var refHandle))
        {
            var resolved = ResolveTypeSymbol(type, scope);
            if (resolved == refHandle)
                return true;
        }

        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.MutableReference, out var mutRefHandle))
        {
            var resolved = ResolveTypeSymbol(type, scope);
            if (resolved == mutRefHandle)
                return true;
        }

        if (type is GenericType gen && (gen.Name.Value.AsSpan().SequenceEqual(KnownIntrinsics.Reference.AsSpan()) || gen.Name.Value.AsSpan().SequenceEqual(KnownIntrinsics.MutableReference.AsSpan())))
            return true;

        return false;
    }

    private static bool MatchesAttributeName(AttributeApplication app, string expectedName, AttributeSymbol? candidateAttr)
    {
        ReadOnlySpan<char> nameSpan = app.Name switch
        {
            SimpleName simple => simple.Name.Source.AsSpan(simple.Name.Span),
            QualifiedName qual when qual.Parts.Count > 0 && qual.Parts[^1] is SimpleName lastSimple => lastSimple.Name.Source.AsSpan(lastSimple.Name.Span),
            _ => default
        };

        if (nameSpan.SequenceEqual(expectedName.AsSpan()))
            return true;

        if (candidateAttr is not null && nameSpan.SequenceEqual(candidateAttr.Name.AsSpan()))
            return true;

        return false;
    }
}
