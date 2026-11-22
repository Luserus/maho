using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class DeclarationResolutionPass
{
    private void ResolveTypeConstraints(IReadOnlyList<TypeConstraintClause> clauses, IReadOnlyList<SymbolHandle> parameters, Scope scope)
    {
        foreach (var (Kind, ID) in parameters)
            if (Kind is SymbolKind.GenericParameter)
                context.GenericParameterSymbols[ID].Constraints = [];

        foreach (var clause in clauses)
        {
            var parameter = FindGenericParameter(parameters, context.GetScopedSymbolName(clause.GenericParameter).Last);
            if (parameter is null)
                continue;

            context.ResolvedTree.AddReference(clause.GenericParameter, ResolutionContext.GetHandle(parameter));

            foreach (var constraint in clause.Constraints)
                if (constraint is TypeTypeConstraint typeConstraint)
                    parameter.Constraints.Add(ResolveType(typeConstraint.Type, scope, isTraitBoundContext: true));
        }
    }

    private GenericParameterSymbol? FindGenericParameter(IReadOnlyList<SymbolHandle> handles, SymbolPart name)
    {
        foreach (var (Kind, ID) in handles)
            if (Kind is SymbolKind.GenericParameter && context.GenericParameterSymbols[ID].Name == name)
                return context.GenericParameterSymbols[ID];

        return null;
    }

    private void ResolveGenericParameterDeclarations(NamedSyntax name, IReadOnlyList<SymbolHandle> parameters)
    {
        var genericName = name switch
        {
            GenericName generic => generic,
            QualifiedName qualified when qualified.Parts.Count > 0 => qualified.Parts[^1] as GenericName,
            _ => null
        };

        if (genericName is null)
            return;

        for (int index = 0; index < genericName.GenericParameters.Count && index < parameters.Count; index++)
            context.ResolvedTree.AddReference(genericName.GenericParameters[index], parameters[index]);
    }

    private List<SymbolHandle> ResolveAttributes(IReadOnlyList<AttributeListSyntax> attributes, Scope scope)
    {
        var result = new List<SymbolHandle>();

        foreach (var list in attributes)
            foreach (var attribute in list.Attributes)
            {
                ResolveNamed(attribute.Name, scope, attribute);

                if (ResolveSingle(scope, context.GetScopedSymbolName(attribute.Name)) is { } symbol)
                    result.Add(ResolutionContext.GetHandle(symbol));

                foreach (var argument in attribute.Arguments)
                    ResolveExpression(argument, scope, default);
            }

        return result;
    }

    private List<TypeRef> ResolveTypes(SeparatedSyntaxList<TypeSyntax> types, Scope scope, bool isTraitBoundContext = false)
    {
        var result = new List<TypeRef>(types.Count);

        foreach (var type in types)
            result.Add(ResolveType(type, scope, isTraitBoundContext: isTraitBoundContext));

        return result;
    }

    private TypeRef ResolveType(TypeSyntax syntax, Scope scope, bool reportDiagnostics = true, bool allowInference = false, bool isUntaggedContext = false, bool isTraitBoundContext = false)
    {
        if (resolvedTypes.TryGetValue((syntax, scope, allowInference, isTraitBoundContext), out var cached))
            return cached;

        var result = ResolveTypeCore(syntax, scope, reportDiagnostics, allowInference, isUntaggedContext, isTraitBoundContext);

        if (reportDiagnostics || result.IsResolved)
            resolvedTypes[(syntax, scope, allowInference, isTraitBoundContext)] = result;

        return result;
    }

    private TypeRef ResolveTypeCore(TypeSyntax syntax, Scope scope, bool reportDiagnostics = true, bool allowInference = false, bool isUntaggedContext = false, bool isTraitBoundContext = false)
    {
        if (syntax is FunctionTypeSyntax fnSyntax)
        {
            TypeRef ret;
            if (fnSyntax.ArrowToken is not null)
            {
                ret = ResolveType(fnSyntax.ReturnType, scope, reportDiagnostics);
            }
            else
            {
                if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Unit))
                {
                    if (reportDiagnostics)
                        context.Diagnostics.ReportMissingIntrinsicForSyntax(fnSyntax.FuncKeyword.Span, "omitted function pointer return type", KnownIntrinsics.Unit, fnSyntax.GetSource());
                }
                context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Unit, out var unitHandle);
                ret = unitHandle != default ? TypeRef.Resolved(unitHandle) : ResolveType(fnSyntax.ReturnType, scope, reportDiagnostics);
            }

            var paramTypes = new List<TypeRef>(fnSyntax.ParameterTypes.Count);
            foreach (var p in fnSyntax.ParameterTypes)
                paramTypes.Add(ResolveType(p, scope, reportDiagnostics));

            if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Function))
            {
                if (reportDiagnostics)
                    context.Diagnostics.ReportMissingIntrinsicForSyntax(fnSyntax.FuncKeyword.Span, "function pointer type syntax", KnownIntrinsics.Function, fnSyntax.GetSource());
            }

            return TypeRef.Function(ret, paramTypes);
        }

        if (syntax is DelegateTypeSyntax delSyntax)
        {
            TypeRef ret;
            if (delSyntax.ArrowToken is not null)
            {
                ret = ResolveType(delSyntax.ReturnType, scope, reportDiagnostics);
            }
            else
            {
                if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Unit))
                {
                    if (reportDiagnostics)
                        context.Diagnostics.ReportMissingIntrinsicForSyntax(delSyntax.DelegateKeyword.Span, "omitted delegate return type", KnownIntrinsics.Unit, delSyntax.GetSource());
                }
                context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Unit, out var unitHandle);
                ret = unitHandle != default ? TypeRef.Resolved(unitHandle) : ResolveType(delSyntax.ReturnType, scope, reportDiagnostics);
            }

            var paramTypes = new List<TypeRef>(delSyntax.ParameterTypes.Count);
            foreach (var p in delSyntax.ParameterTypes)
                paramTypes.Add(ResolveType(p, scope, reportDiagnostics));

            if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Delegate))
            {
                if (reportDiagnostics)
                    context.Diagnostics.ReportMissingIntrinsicForSyntax(delSyntax.DelegateKeyword.Span, "delegate type syntax", KnownIntrinsics.Delegate, delSyntax.GetSource());
            }

            return TypeRef.Delegate(ret, paramTypes);
        }

        if (syntax is SimpleType simple && (simple.Name.MatchingKind == MatchingKeywordKind.Var || simple.Name.Value == "var"))
        {
            if (allowInference)
                return TypeRef.Inferred;

            var varRes = ResolveTypeName(syntax, scope, reportDiagnostics);
            CheckUntaggedUsage(varRes, syntax, isUntaggedContext, reportDiagnostics);
            return varRes;
        }

        if (syntax is UnionType union)
        {
            var elements = new List<TypeRef>(union.Variants.Count);
            foreach (var elem in union.Variants)
                elements.Add(ResolveType(elem, scope, reportDiagnostics));
            return TypeRef.Union(elements, isUntagged: false);
        }

        if (syntax is TupleType tuple)
        {
            var elements = new List<TupleElement>(tuple.Elements.Count);
            foreach (var elem in tuple.Elements)
            {
                var elemType = ResolveType(elem.Type, scope, reportDiagnostics);
                elements.Add(new TupleElement(elemType, elem.Name?.Value));
            }
            if (context.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null && !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Tuple))
            {
                if (reportDiagnostics)
                    context.Diagnostics.ReportMissingIntrinsicForSyntax(tuple.GetSpan() ?? default, "tuple type syntax", KnownIntrinsics.Tuple, tuple.GetSource());
            }
            context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var tupleHandle);
            return TypeRef.Tuple(elements, tupleHandle != default ? tupleHandle : null);
        }

        if (syntax is UniformTupleType uniform)
        {
            var elemType = ResolveType(uniform.ElementType, scope, reportDiagnostics);
            var elements = new List<TupleElement>(uniform.Elements.Count);

            foreach (var elem in uniform.Elements)
                elements.Add(new TupleElement(elemType, elem.Name.Value));

            if (context.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null && !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Tuple))
            {
                if (reportDiagnostics)
                    context.Diagnostics.ReportMissingIntrinsicForSyntax(uniform.GetSpan() ?? default, "tuple type syntax", KnownIntrinsics.Tuple, uniform.GetSource());
            }
            context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var tupleHandle);
            return TypeRef.Tuple(elements, tupleHandle != default ? tupleHandle : null);
        }

        if (syntax is ModifiedType modified)
        {
            var innerType = ResolveType(modified.Type, scope, reportDiagnostics);

            return modified.Modifier switch
            {
                PointerTypeModifier ptrMod => ResolvePointerModifier(modified, innerType, ptrMod.IsMutable, reportDiagnostics),
                ReferenceTypeModifier refMod => ResolveReferenceModifier(modified, innerType, refMod.IsMutable, reportDiagnostics),
                SpanTypeModifier => ResolveSpanModifier(modified, innerType, reportDiagnostics),
                OptionalTypeModifier => ResolveOptionalModifier(modified, innerType, reportDiagnostics),
                ArrayTypeModifier => ResolveArrayModifier(modified, innerType, reportDiagnostics),
                FixedArrayTypeModifier fixedArray => ResolveFixedArrayModifier(modified, fixedArray, innerType, scope, reportDiagnostics),
                VariadicTypeModifier => innerType,
                _ => TypeRef.Error
            };
        }

        if (syntax is PrefixModifiedType prefixModified)
        {
            if (prefixModified.Modifier is SafeAliasTypeModifier safeMod)
            {
                var inner = ResolveType(prefixModified.Type, scope, reportDiagnostics, allowInference, isUntaggedContext);
                return TypeRef.Alias(inner, isMutable: safeMod.IsMutable, isUnsafe: false);
            }

            if (prefixModified.Modifier is UnsafeAliasTypeModifier unsafeMod)
            {
                var inner = ResolveType(prefixModified.Type, scope, reportDiagnostics, allowInference, isUntaggedContext);
                return TypeRef.Alias(inner, isMutable: unsafeMod.IsMutable, isUnsafe: true);
            }

            if (prefixModified.Modifier is UntaggedTypeModifier)
            {
                var inner = ResolveType(prefixModified.Type, scope, reportDiagnostics, allowInference, isUntaggedContext: true);
                if (reportDiagnostics && !inner.IsError)
                {
                    bool isSumType = inner.Kind == TypeRefKind.Union || inner.SpecialType is UnionSemanticType;
                    if (!isSumType && inner.Handle is { } h)
                    {
                        var sym = context.GetTypeSymbol(h);
                        isSumType = sym is ISumTypeSymbol;
                    }
                    if (!isSumType)
                    {
                        context.Diagnostics.ReportInvalidModifier(
                            prefixModified.Modifier.GetSpan() ?? default,
                            "untagged",
                            "type",
                            inner.ToDisplayString(context),
                            prefixModified.Modifier.GetSource());
                    }
                }
                return TypeRef.Untagged(inner);
            }

            if (prefixModified.Modifier is DynTypeModifier)
            {
                var inner = ResolveType(prefixModified.Type, scope, reportDiagnostics, allowInference, isUntaggedContext, isTraitBoundContext: true);
                return TypeRef.Dyn(inner);
            }

            return TypeRef.Error;
        }

        if (syntax is GenericType generic)
        {
            TypeRef genericTarget = ResolveTypeName(generic, scope, reportDiagnostics);
            ResolveGenericArguments(generic, genericTarget.Handle, scope);
            CheckUntaggedUsage(genericTarget, syntax, isUntaggedContext, reportDiagnostics);

            if (genericTarget.Handle is { } h && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var optH) && h == optH && generic.GenericArguments.Count == 1)
            {
                var elemType = ResolveType(generic.GenericArguments[0], scope, reportDiagnostics);
                return TypeRef.Optional(elemType, optH);
            }

            if (genericTarget.IsResolved && genericTarget.Handle is { } targetH)
            {
                var typeArgs = new List<TypeRef>(generic.GenericArguments.Count);
                foreach (var arg in generic.GenericArguments)
                    typeArgs.Add(ResolveType(arg, scope, reportDiagnostics));

                var gen = TypeRef.Generic(targetH, generic.Name.Value, typeArgs);
                if (!isTraitBoundContext && IsInterfaceSymbol(context, targetH))
                    return TypeRef.Dyn(gen);
                return gen;
            }

            return genericTarget;
        }

        if (syntax is QualifiedType qualified)
        {
            if (qualified.Right is GenericType qualifiedGeneric)
            {
                TypeRef target = ResolveTypeName(syntax, scope, reportDiagnostics);

                if (target.Handle is { } handle)
                    context.ResolvedTree.AddReference(qualifiedGeneric, handle);

                ResolveGenericArguments(qualifiedGeneric, target.Handle, scope);
                CheckUntaggedUsage(target, syntax, isUntaggedContext, reportDiagnostics);

                if (target.Handle is { } qh && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var qOptH) && qh == qOptH && qualifiedGeneric.GenericArguments.Count == 1)
                {
                    var elemType = ResolveType(qualifiedGeneric.GenericArguments[0], scope, reportDiagnostics);
                    return TypeRef.Optional(elemType, qOptH);
                }

                if (target.IsResolved && target.Handle is { } qtargetH)
                {
                    var typeArgs = new List<TypeRef>(qualifiedGeneric.GenericArguments.Count);
                    foreach (var arg in qualifiedGeneric.GenericArguments)
                        typeArgs.Add(ResolveType(arg, scope, reportDiagnostics));

                    var gen = TypeRef.Generic(qtargetH, qualifiedGeneric.Name.Value, typeArgs);
                    if (!isTraitBoundContext && IsInterfaceSymbol(context, qtargetH))
                        return TypeRef.Dyn(gen);
                    return gen;
                }

                return target;
            }

            TypeRef result = ResolveTypeName(syntax, scope, reportDiagnostics);

            if (result.Handle is { } rightHandle)
                context.ResolvedTree.AddReference(qualified.Right, rightHandle);

            CheckUntaggedUsage(result, syntax, isUntaggedContext, reportDiagnostics);
            if (!isTraitBoundContext && result.Handle is { } resH && IsInterfaceSymbol(context, resH))
                return TypeRef.Dyn(result);
            return result;
        }

        ResolveTypeChildren(syntax, scope);

        var finalRes = ResolveTypeName(syntax, scope, reportDiagnostics);
        CheckUntaggedUsage(finalRes, syntax, isUntaggedContext, reportDiagnostics);
        if (!isTraitBoundContext && finalRes.Handle is { } fHandle && IsInterfaceSymbol(context, fHandle))
            return TypeRef.Dyn(finalRes);
        return finalRes;
    }

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

    private void CheckUntaggedUsage(TypeRef typeRef, TypeSyntax syntax, bool isUntaggedContext, bool reportDiagnostics)
    {
        if (reportDiagnostics && !isUntaggedContext && typeRef.Handle is { } h)
        {
            if (context.GetTypeSymbol(h) is { } typeSym && typeSym.Flags.HasFlag(TypeFlags.Untagged))
            {
                context.Diagnostics.ReportUntaggedDefinitionTaggedUsage(syntax.GetSpan() ?? default, typeSym.Name.ToString(), syntax.GetSource());
            }
        }
    }

    private TypeRef ResolveTypeName(TypeSyntax syntax, Scope scope, bool reportDiagnostics = true)
    {
        var name = ResolutionContext.GetSymbolName(syntax);
        var symbols = scope.ResolveType(name);
        if (symbols.Count > 1 && !AreSamePartialType(symbols))
        {
            if (reportDiagnostics)
            {
                var span = syntax.GetSpan() ?? default;
                var source = syntax.GetSource();
                context.Diagnostics.ReportAmbiguousTypeReference(span, name.ToDisplayString(), source, syntax.ExpansionOrigin);
            }

            return TypeRef.Error;
        }

        if (symbols.Count == 0)
        {
            if (reportDiagnostics)
            {
                var span = syntax.GetSpan() ?? default;
                var source = syntax.GetSource();
                string? arityNote = GetArityMismatchNote(scope, name);
                context.Diagnostics.ReportUnresolvedTypeReference(span, name.ToDisplayString(), source, syntax.ExpansionOrigin, arityNote);
            }

            return TypeRef.Error;
        }

        var symbol = symbols[0];

        if (symbol is AliasSymbol aliasSymbol && aliasSymbol.IsGlobalAlias && !aliasSymbol.Target.IsResolved)
        {
            if (reportDiagnostics)
            {
                var span = syntax.GetSpan() ?? default;
                var source = syntax.GetSource();
                string? arityNote = GetArityMismatchNote(scope, name);
                context.Diagnostics.ReportUnresolvedTypeReference(span, name.ToDisplayString(), source, syntax.ExpansionOrigin, arityNote);
            }

            return TypeRef.Error;
        }

        var handle = ResolutionContext.GetHandle(symbol);
        context.ResolvedTree.AddReference(syntax, handle);

        return TypeRef.Resolved(handle);
    }

    private static string? GetArityMismatchNote(Scope scope, SymbolName name)
    {
        var targetPart = name.Last;
        if (targetPart.Arity > 0)
        {
            var nonGenericPart = new SymbolPart(targetPart.Text, 0);
            var nonGenericName = name.Count == 1
                ? new SymbolName(nonGenericPart)
                : new SymbolName([.. name.Parts.Take(name.Count - 1), nonGenericPart]);

            if (ResolveSingle(scope, nonGenericName) is not null)
                return $"non-generic type '{nonGenericName}' exists in this scope, but generic type '{name}' with {targetPart.Arity} type argument{(targetPart.Arity == 1 ? "" : "s")} was not found";
        }
        else
        {
            for (int a = 1; a <= 8; a++)
            {
                var genericPart = new SymbolPart(targetPart.Text, a);
                var genericName = name.Count == 1
                    ? new SymbolName(genericPart)
                    : new SymbolName([.. name.Parts.Take(name.Count - 1), genericPart]);

                if (ResolveSingle(scope, genericName) is not null)
                    return $"generic type '{genericName}' exists in this scope with {a} type argument{(a == 1 ? "" : "s")}, but non-generic type '{name}' was not found";
            }
        }

        return null;
    }

    private void ResolveGenericArguments(GenericType generic, SymbolHandle? target, Scope scope)
    {
        var parameters = target is { } handle ? GetGenericParameters(handle) : [];

        for (int index = 0; index < generic.GenericArguments.Count; index++)
        {
            var argument = generic.GenericArguments[index];
            var parameter = index < parameters.Count && parameters[index].Kind is SymbolKind.GenericParameter
                ? context.GenericParameterSymbols[parameters[index].ID]
                : (parameters.Count > 0 && parameters[^1].Kind is SymbolKind.GenericParameter && context.GenericParameterSymbols[parameters[^1].ID].IsVariadic
                    ? context.GenericParameterSymbols[parameters[^1].ID]
                    : null);

            switch (argument)
            {
                case LiteralGenericArgument:
                    break;
                case NamedExpressionGenericArgument named when parameter?.ParameterKind is GenericParameterKind.Type:
                    ResolveNamedArgumentAsType(named.Expression, scope);
                    break;
                case NamedExpressionGenericArgument named:
                    ResolveExpression(named.Expression, scope, default);
                    break;
                default:
                    ResolveType(argument, scope);
                    break;
            }
        }
    }

    private SymbolHandle? ResolveGenericArgumentAsType(TypeSyntax argument, Scope scope) => argument switch
    {
        NamedExpressionGenericArgument named => ResolveNamedArgumentAsType(named.Expression, scope),
        _ => ResolveType(argument, scope).Handle
    };

    private SymbolHandle? ResolveNamedArgumentAsType(IdentifierNameExpression expression, Scope scope)
    {
        if (ResolveSingle(scope, new SymbolName(new SymbolPart(expression.Identifier))) is not { } symbol)
            return null;

        var handle = ResolutionContext.GetHandle(symbol);
        context.ResolvedTree.AddReference(expression, handle);

        return handle;
    }

    private bool IsInsideIntrinsicDeclaration(string intrinsicName)
    {
        if (currentTypeSymbol is null)
            return false;

        if (context.DiscoveredIntrinsics.TryGet(intrinsicName, out var handle) &&
            ResolutionContext.GetHandle(currentTypeSymbol) == handle)
            return true;

        IReadOnlyList<AttributeListSyntax>? attrLists = currentTypeSymbol switch
        {
            TypeSymbol ts => ts.Syntax?.Attributes,
            NestedTypeSymbol nts => nts.Syntax?.Attributes,
            _ => null
        };

        if (attrLists is not null)
        {
            foreach (var list in attrLists)
            {
                foreach (var app in list.Attributes)
                {
                    if (app.Arguments.Count == 1 &&
                        app.Arguments[0] is LiteralExpression lit &&
                        lit.Literal.Kind is TokenKind.String or TokenKind.SuffixedString)
                    {
                        var val = lit.Literal.Value.Trim('"');
                        if (string.Equals(val, intrinsicName, StringComparison.Ordinal))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    private TypeRef ResolvePointerModifier(ModifiedType modified, TypeRef innerType, bool isMutable, bool reportDiagnostics)
    {
        string requiredIntrinsic = isMutable ? KnownIntrinsics.MutablePointer : KnownIntrinsics.Pointer;
        bool isDeclared = context.DiscoveredIntrinsics.Contains(requiredIntrinsic) || IsInsideIntrinsicDeclaration(requiredIntrinsic);
        if (reportDiagnostics && !isDeclared)
        {
            var span = modified.Modifier.GetSpan() ?? modified.GetSpan() ?? default;
            context.Diagnostics.ReportMissingIntrinsicForSyntax(span, isMutable ? "mutable pointer type syntax" : "pointer type syntax", requiredIntrinsic, modified.GetSource());
        }

        return TypeRef.Pointer(innerType, isMutable);
    }

    private TypeRef ResolveOptionalModifier(ModifiedType modified, TypeRef innerType, bool reportDiagnostics)
    {
        bool isDeclared = context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Optional) || IsInsideIntrinsicDeclaration(KnownIntrinsics.Optional);
        if (reportDiagnostics && !isDeclared)
        {
            var span = modified.Modifier.GetSpan() ?? modified.GetSpan() ?? default;
            context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "optional type syntax", KnownIntrinsics.Optional, modified.GetSource());
        }

        SymbolHandle? optionalHandle = null;
        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var h))
            optionalHandle = h;
        else if (currentTypeSymbol is not null && IsInsideIntrinsicDeclaration(KnownIntrinsics.Optional))
            optionalHandle = ResolutionContext.GetHandle(currentTypeSymbol);

        return TypeRef.Optional(innerType, optionalHandle);
    }

    private TypeRef ResolveReferenceModifier(ModifiedType modified, TypeRef innerType, bool isMutable, bool reportDiagnostics)
    {
        string requiredIntrinsic = isMutable ? KnownIntrinsics.MutableReference : KnownIntrinsics.Reference;
        bool isDeclared = context.DiscoveredIntrinsics.Contains(requiredIntrinsic) || IsInsideIntrinsicDeclaration(requiredIntrinsic);
        if (reportDiagnostics && !isDeclared)
        {
            var span = modified.Modifier.GetSpan() ?? modified.GetSpan() ?? default;
            context.Diagnostics.ReportMissingIntrinsicForSyntax(span, isMutable ? "mutable reference type syntax" : "reference type syntax", requiredIntrinsic, modified.GetSource());
        }

        return TypeRef.Reference(innerType, isMutable);
    }

    private TypeRef ResolveSpanModifier(ModifiedType modified, TypeRef innerType, bool reportDiagnostics)
    {
        if (reportDiagnostics && !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Span))
        {
            var span = modified.Modifier.GetSpan() ?? modified.GetSpan() ?? default;
            context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "span type syntax", KnownIntrinsics.Span, modified.GetSource());
        }

        return TypeRef.Span(innerType);
    }

    private TypeRef ResolveArrayModifier(ModifiedType modified, TypeRef innerType, bool reportDiagnostics = true)
    {
        if (reportDiagnostics && !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Array))
        {
            var span = modified.Modifier.GetSpan() ?? modified.GetSpan() ?? default;
            context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "array type syntax", KnownIntrinsics.Array, modified.GetSource());
        }

        int dimension = modified.Modifier is ArrayTypeModifier arrMod ? arrMod.Dimension : 1;
        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var arrayHandle);
        return TypeRef.Array(innerType, dimension, arrayHandle != default ? arrayHandle : null);
    }

    private TypeRef ResolveFixedArrayModifier(ModifiedType modified, FixedArrayTypeModifier fixedArray, TypeRef innerType, Scope scope, bool reportDiagnostics = true)
    {
        if (reportDiagnostics && !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.FixedArray))
        {
            var span = fixedArray.GetSpan() ?? default;
            context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "fixed-size array type syntax", KnownIntrinsics.FixedArray, modified.GetSource());
        }

        foreach (var s in fixedArray.Sizes)
            ResolveExpression(s, scope, default);

        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.FixedArray, out var fixedArrayHandle);
        return TypeRef.FixedArray(innerType, fixedArray.Sizes.ToList(), fixedArrayHandle != default ? fixedArrayHandle : null);
    }

    private void CheckInferredInitializer(Expression expr, bool isInferred)
    {
        if (expr is CollectionExpression collection && isInferred)
        {
            if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.FixedArray))
            {
                var span = collection.GetSpan() ?? default;
                context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "inferred collection expression", KnownIntrinsics.FixedArray, collection.GetSource());
            }
        }
        else if (expr is LiteralExpression literal && !literal.HasSuffix && isInferred)
        {
            CheckUnsuffixedLiteral(literal);
        }
    }

    private void CheckUnsuffixedLiteral(LiteralExpression literal)
    {
        string? required = literal.Literal.Kind switch
        {
            TokenKind.Integer => KnownIntrinsics.Int32,
            TokenKind.Float => KnownIntrinsics.Float64,
            TokenKind.String => KnownIntrinsics.Str,
            TokenKind.Char => KnownIntrinsics.Char,
            _ => null
        };

        if (required is not null && !context.DiscoveredIntrinsics.Contains(required))
        {
            string desc = literal.Literal.Kind switch
            {
                TokenKind.Integer => "unsuffixed integer literal",
                TokenKind.Float => "unsuffixed float literal",
                TokenKind.String => "unsuffixed string literal",
                TokenKind.Char => "unsuffixed char literal",
                _ => "literal"
            };

            context.Diagnostics.ReportMissingIntrinsicForSyntax(literal.Literal.Span, desc, required, literal.Literal.Source);
        }
    }

    private void ResolveTypeChildren(TypeSyntax syntax, Scope scope)
    {
        switch (syntax)
        {
            case GenericType:
                break;
            case QualifiedType qualified:
                ResolveType(qualified.Left, scope);
                ResolveType(qualified.Right, scope);
                break;
            case ModifiedType modified:
                ResolveTypeChildren(modified.Type, scope);
                break;
            case PrefixModifiedType prefixModified:
                ResolveTypeChildren(prefixModified.Type, scope);
                break;
        }
    }

}
