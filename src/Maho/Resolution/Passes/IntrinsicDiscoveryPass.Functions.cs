using System;
using System.Linq;
using Maho.Syntax;
using Maho.Text;

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

        string? opName = ExtractLiteralString(app.Arguments[0]);
        if (opName is null || !KnownIntrinsics.TryGetOperatorKind(opName.AsSpan(), out var opKind))
        {
            var span = app.Arguments[0].GetSpan() ?? default;
            string displayVal = opName ?? app.Arguments[0].ToString() ?? "";
            context.Diagnostics.ReportInvalidOperatorFunctionSignature(span, displayVal, $"unknown operator function '{displayVal}'", method.Syntax?.GetSource());
            return;
        }

        string fullName = KnownIntrinsics.GetOperatorName(opKind);
        bool hasReceiver = method.Syntax?.Signature.Parameters.Any(p => p.Declarator.IsReceiver) ?? false;
        bool isStatic = !hasReceiver && method.Flags.HasFlag(FunctionFlags.Static);
        int totalParamCount = method.Syntax?.Signature.Parameters.Count ?? method.Parameters.Count;
        int paramCount = hasReceiver ? totalParamCount - 1 : totalParamCount;
        var returnTypeSyntax = method.Syntax?.Signature.ReturnType;
        bool isVoidReturn = method.Syntax?.Signature.ArrowToken is null || (returnTypeSyntax is not null && IsVoidType(returnTypeSyntax, method.EnclosingScope));
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
                if (totalParamCount != 1)
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
                if (method.Syntax?.Signature.Parameters is not [{ Declarator.ReceiverKind: ReceiverKind.MutReference }])
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "destructors must take a 'mut &this' receiver", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "destructors must return void", source);
                    return;
                }
                break;

            // StaticConstructor
            case OperatorKind.StaticConstructor:
                if (hasReceiver)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "static constructors cannot have a receiver parameter", source);
                    return;
                }
                if (totalParamCount != 0)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "static constructors cannot accept parameters", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "static constructors must return void", source);
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

            // Copy
            case OperatorKind.Copy:
            // Move
            case OperatorKind.Move:
                if (totalParamCount != 2)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept exactly 2 parameters (dest, source)", source);
                    return;
                }
                if (hasReceiver && method.Syntax?.Signature.Parameters[0].Declarator.ReceiverKind is not ReceiverKind.MutReference)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, $"instance '{fullName}' operator must take a 'mut &this' receiver", source);
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
            case OperatorKind.CallableMut:
            case OperatorKind.CallableOnce:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (opKind == OperatorKind.CallableMut &&
                    method.Syntax?.Signature.Parameters is not [{ Declarator.ReceiverKind: ReceiverKind.MutReference }, ..])
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "receiver parameter must be 'this mut&'", source);
                    return;
                }
                if (opKind == OperatorKind.CallableOnce &&
                    method.Syntax?.Signature.Parameters is not [{ Declarator.ReceiverKind: ReceiverKind.Value }, ..])
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "receiver parameter must be 'this'", source);
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

            case OperatorKind.CollectionAdd:
            case OperatorKind.CollectionWith:
            case OperatorKind.Deconstruct:
            case OperatorKind.DeconstructMut:
            case OperatorKind.Iterable:
            case OperatorKind.Iterator:
            case OperatorKind.OwningDeref:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                break;
        }

        if (context.DiscoveredOperators.TryGetOperators(opKind, out var existing) && existing.Count > 0)
        {
            var first = existing[0];
            context.Diagnostics.ReportDuplicateOperatorFunction(fullName, sigSpan, first.Span, source, first.Source);
            return;
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
    private bool TryExtractIntrinsicSignature(
        AttributeApplication app,
        string attributeName,
        SourceText? source,
        out string signature)
    {
        signature = string.Empty;
        if (app.Arguments.Count != 1)
        {
            var span = app.GetSpan() ?? default;
            context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                span,
                $"attribute '{attributeName}' requires exactly one argument",
                source,
                attributeName);
            return false;
        }

        var argExpr = app.Arguments[0];
        if (argExpr is not LiteralExpression lit || lit.Literal.Kind is not (TokenKind.String or TokenKind.SuffixedString))
        {
            var span = argExpr.GetSpan() ?? default;
            context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                span,
                $"argument to '{attributeName}' must be a string literal describing the member",
                source,
                attributeName);
            return false;
        }

        signature = ExtractLiteralString(lit) ?? lit.Literal.Value;
        return true;
    }

    private bool IsDeclaredInsideIntrinsicType(SymbolHandle parentHandle)
    {
        if (context.GetSymbol(parentHandle) is not TypeSymbol)
            return false;

        foreach (var (_, handle) in context.DiscoveredIntrinsics.All)
        {
            if (handle == parentHandle)
                return true;
        }

        return false;
    }

    private void DiscoverAndValidateIntrinsicFunctions()
    {
        foreach (var method in context.MethodSymbols)
        {
            if (method.Syntax?.Attributes is not { Count: > 0 } attrLists)
                continue;

            foreach (var list in attrLists)
            {
                foreach (var app in list.Attributes)
                {
                    if (!MatchesAttributeName(app, KnownIntrinsics.IntrinsicFunctionAttribute, context.DiscoveredOperators.IntrinsicFunctionAttribute))
                        continue;

                    if (!TryExtractIntrinsicSignature(app, KnownIntrinsics.IntrinsicFunctionAttribute, method.Syntax?.GetSource(), out var sig))
                        continue;

                    if (method.Parent is null)
                        continue;

                    if (!IsDeclaredInsideIntrinsicType(method.Parent.Value))
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
                    method.IntrinsicSignature = sig;
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

                    if (!TryExtractIntrinsicSignature(app, KnownIntrinsics.IntrinsicFunctionAttribute, function.Syntax?.GetSource(), out var sig))
                        continue;

                    function.Flags |= FunctionFlags.Intrinsic;
                    function.IntrinsicSignature = sig;
                }
            }
        }
    }

    private void DiscoverAndValidateIntrinsicProperties()
    {
        foreach (var prop in context.PropertySymbols)
        {
            if (prop.Syntax?.Attributes is not { Count: > 0 } attrLists)
                continue;

            foreach (var list in attrLists)
            {
                foreach (var app in list.Attributes)
                {
                    if (!MatchesAttributeName(app, KnownIntrinsics.IntrinsicPropertyAttribute, context.DiscoveredOperators.IntrinsicPropertyAttribute))
                        continue;

                    if (!TryExtractIntrinsicSignature(app, KnownIntrinsics.IntrinsicPropertyAttribute, prop.Syntax?.GetSource(), out var sig))
                        continue;

                    if (prop.Parent is null || !IsDeclaredInsideIntrinsicType(prop.Parent.Value))
                    {
                        var span = app.GetSpan() ?? default;
                        context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                            span,
                            $"property '{prop.Name.Text}' marked with [IntrinsicProperty] must be declared in an intrinsic type",
                            prop.Syntax?.GetSource(),
                            KnownIntrinsics.IntrinsicPropertyAttribute);
                        continue;
                    }

                    prop.Flags |= FunctionFlags.Intrinsic;
                    prop.IntrinsicSignature = sig;
                    prop.HasBacking = false;
                }
            }
        }
    }

    private bool IsVoidType(TypeSyntax type, Scope scope)
    {
        if (type is TupleType tuple && tuple.Elements.Count == 0)
            return true;

        return false;
    }

    private bool IsReferenceType(TypeSyntax type, Scope scope)
    {
        while (type is GroupedType grouped)
            type = grouped.InnerType;

        if (type is ModifiedType modified && modified.Modifier is ReferenceTypeModifier)
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
