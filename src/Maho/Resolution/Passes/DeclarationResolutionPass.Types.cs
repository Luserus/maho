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
        {
            if (Kind is SymbolKind.GenericParameter)
            {
                var gp = context.GenericParameterSymbols[ID];
                gp.Constraints = [];
                gp.HasExplicitLayout = false;
                gp.HasExplicitSized = false;
                gp.HasExplicitUnsized = false;
            }
        }

        foreach (var clause in clauses)
        {
            var parameter = FindGenericParameter(parameters, context.GetScopedSymbolName(clause.GenericParameter).Last);
            if (parameter is null)
                continue;

            context.ResolvedTree.AddReference(clause.GenericParameter, ResolutionContext.GetHandle(parameter));

            foreach (var constraint in clause.Constraints)
            {
                if (constraint is TypeTypeConstraint typeConstraint)
                {
                    var resolved = ResolveType(typeConstraint.Type, scope, isTraitBoundContext: true);
                    parameter.Constraints.Add(resolved);
                    if (resolved.Handle is { } ch)
                    {
                        var unwrapped = context.UnwrapAlias(ch);
                        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Layout, out var layoutH) && unwrapped == layoutH)
                            parameter.HasExplicitLayout = true;
                        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Sized, out var sizedH) && unwrapped == sizedH)
                            parameter.HasExplicitSized = true;
                        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Unsized, out var unsizedH) && unwrapped == unsizedH)
                            parameter.HasExplicitUnsized = true;
                    }
                }
            }
        }
    }

    private GenericParameterSymbol? FindGenericParameter(IReadOnlyList<SymbolHandle> handles, SymbolPart name)
    {
        foreach (var (Kind, ID) in handles)
            if (Kind is SymbolKind.GenericParameter && context.GenericParameterSymbols[ID].Name == name)
                return context.GenericParameterSymbols[ID];

        return null;
    }

    private void ResolveGenericParameterDeclarations(NamedSyntax name, IReadOnlyList<SymbolHandle> parameters, Scope scope)
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
        {
            var handle = parameters[index];
            var paramSyntax = genericName.GenericParameters[index];
            context.ResolvedTree.AddReference(paramSyntax, handle);
            if (paramSyntax.DefaultType is { } defaultTypeSyntax)
            {
                var defType = ResolveType(defaultTypeSyntax, scope);
                context.GenericParameterSymbols[handle.ID].DefaultType = defType;
            }
        }
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

    private TypeRef ResolveType(TypeSyntax syntax, Scope scope, bool reportDiagnostics = true, bool allowInference = false, bool isUntaggedContext = false, bool isTraitBoundContext = false, bool isDynTargetContext = false)
    {
        if (resolvedTypes.TryGetValue((syntax, scope, allowInference, isTraitBoundContext || isDynTargetContext), out var cached))
            return cached;

        var result = ResolveTypeCore(syntax, scope, reportDiagnostics, allowInference, isUntaggedContext, isTraitBoundContext, isDynTargetContext);

        if (reportDiagnostics || result.IsResolved)
            resolvedTypes[(syntax, scope, allowInference, isTraitBoundContext || isDynTargetContext)] = result;

        return result;
    }

    private TypeRef ResolveTypeCore(TypeSyntax syntax, Scope scope, bool reportDiagnostics = true, bool allowInference = false, bool isUntaggedContext = false, bool isTraitBoundContext = false, bool isDynTargetContext = false)
    {
        if (syntax is GroupedType grouped)
        {
            return ResolveType(grouped.InnerType, scope, reportDiagnostics, allowInference, isUntaggedContext, isTraitBoundContext, isDynTargetContext);
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
            TypeRef ret;
            if (fnSyntax.ArrowToken is not null)
                ret = ResolveType(fnSyntax.ReturnType, scope, reportDiagnostics);
            else
                ret = TypeRef.EmptyTuple;

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
                ret = ResolveType(delSyntax.ReturnType, scope, reportDiagnostics);
            else
                ret = TypeRef.EmptyTuple;

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
            {
                var elemType = ResolveType(elem, scope, reportDiagnostics);
                if (reportDiagnostics && !elemType.IsError && !elemType.IsSized(context))
                    context.Diagnostics.ReportUnsizedTypeNotAllowed(elem.GetSpan() ?? default, elemType.ToDisplayString(context), "as a union variant", elem.GetSource());
                elements.Add(elemType);
            }
            return TypeRef.Union(elements, isUntagged: false);
        }

        if (syntax is TupleType tuple)
        {
            if (tuple.Elements.Count == 0)
                return TypeRef.EmptyTuple;

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
            if (modified.Modifier is PointerTypeModifier ptrMod)
            {
                if (ReportInvalidDynIndirectionIfWrapped(modified.Type, modified, reportDiagnostics))
                    return TypeRef.Error;
                var inner = ResolveType(modified.Type, scope, reportDiagnostics, isDynTargetContext: isDynTargetContext);
                return ResolvePointerModifier(modified, inner, ptrMod.IsMutable, reportDiagnostics);
            }

            if (modified.Modifier is ReferenceTypeModifier refMod)
            {
                if (ReportInvalidDynIndirectionIfWrapped(modified.Type, modified, reportDiagnostics))
                    return TypeRef.Error;
                var inner = ResolveType(modified.Type, scope, reportDiagnostics, isDynTargetContext: isDynTargetContext);
                return ResolveReferenceModifier(modified, inner, refMod.IsMutable, reportDiagnostics);
            }

            var innerType = ResolveType(modified.Type, scope, reportDiagnostics);

            return modified.Modifier switch
            {
                OptionalTypeModifier => ResolveOptionalModifier(modified, innerType, reportDiagnostics),
                SliceTypeModifier sliceMod => ResolveSliceModifier(modified, sliceMod, innerType, reportDiagnostics),
                ArrayTypeModifier arrayMod => ResolveArrayModifier(modified, arrayMod, innerType, scope, reportDiagnostics),
                VariadicTypeModifier => innerType,
                _ => TypeRef.Error
            };
        }

        if (syntax is PrefixModifiedType prefixModified)
        {
            if (prefixModified.Modifier is SafeAliasTypeModifier safeMod)
            {
                if (ReportInvalidDynIndirectionIfWrapped(prefixModified.Type, prefixModified, reportDiagnostics))
                    return TypeRef.Error;
                var inner = ResolveType(prefixModified.Type, scope, reportDiagnostics, allowInference, isUntaggedContext, isDynTargetContext: isDynTargetContext);
                return TypeRef.Alias(inner, isMutable: safeMod.IsMutable, isUnsafe: false);
            }

            if (prefixModified.Modifier is UnsafeAliasTypeModifier unsafeMod)
            {
                if (ReportInvalidDynIndirectionIfWrapped(prefixModified.Type, prefixModified, reportDiagnostics))
                    return TypeRef.Error;
                var inner = ResolveType(prefixModified.Type, scope, reportDiagnostics, allowInference, isUntaggedContext, isDynTargetContext: isDynTargetContext);
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
                var inner = ResolveType(prefixModified.Type, scope, reportDiagnostics, allowInference, isUntaggedContext, isDynTargetContext: true);
                return TypeRef.Dyn(inner, isExplicitDyn: true);
            }

            return TypeRef.Error;
        }

        if (syntax is GenericType generic)
        {
            TypeRef genericTarget = ResolveTypeName(generic, scope, reportDiagnostics);
            ResolveGenericArguments(generic, genericTarget.Handle, scope);
            CheckUntaggedUsage(genericTarget, syntax, isUntaggedContext, reportDiagnostics);

            if (genericTarget.Handle is { } ih && IsInterfaceSymbol(context, ih) && !isTraitBoundContext && !isDynTargetContext)
            {
                if (reportDiagnostics)
                    ReportMissingDynForInterface(generic, context.GetSymbol(context.UnwrapAlias(ih))?.Name.Text ?? generic.Name.Value);
                return TypeRef.Error;
            }

            if (genericTarget.Handle is { } h && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var optH) && h == optH && generic.GenericArguments.Count == 1)
            {
                var elemType = ResolveType(generic.GenericArguments[0], scope, reportDiagnostics);
                if (reportDiagnostics && !elemType.IsError && !elemType.IsSized(context))
                    context.Diagnostics.ReportGenericConstraintNotSized(generic.GenericArguments[0].GetSpan() ?? default, elemType.ToDisplayString(context), "T", KnownIntrinsics.Optional, "Sized", generic.GetSource());
                return TypeRef.Optional(elemType, optH);
            }

            if (genericTarget.IsResolved && genericTarget.Handle is { } targetH)
            {
                var typeArgs = new List<TypeRef>(generic.GenericArguments.Count);
                foreach (var arg in generic.GenericArguments)
                    typeArgs.Add(ResolveType(arg, scope, reportDiagnostics));

                var genericParams = GetGenericParameters(targetH);
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
                    context.ValidateGenericTypeArgumentConformances(targetH, generic.GenericArguments.ToList(), typeArgs, scope, default, reportDiagnostics);

                return InstantiateSpecialOrGenericType(targetH, generic.Name.Value, typeArgs, generic.GenericArguments);
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

                if (target.Handle is { } qih && IsInterfaceSymbol(context, qih) && !isTraitBoundContext && !isDynTargetContext)
                {
                    if (reportDiagnostics)
                        ReportMissingDynForInterface(qualifiedGeneric, context.GetSymbol(context.UnwrapAlias(qih))?.Name.Text ?? qualifiedGeneric.Name.Value);
                    return TypeRef.Error;
                }

                if (target.Handle is { } qh && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var qOptH) && qh == qOptH && qualifiedGeneric.GenericArguments.Count == 1)
                {
                    var elemType = ResolveType(qualifiedGeneric.GenericArguments[0], scope, reportDiagnostics);
                    if (reportDiagnostics && !elemType.IsError && !elemType.IsSized(context))
                        context.Diagnostics.ReportGenericConstraintNotSized(qualifiedGeneric.GenericArguments[0].GetSpan() ?? default, elemType.ToDisplayString(context), "T", KnownIntrinsics.Optional, "Sized", qualifiedGeneric.GetSource());
                    return TypeRef.Optional(elemType, qOptH);
                }

                if (target.IsResolved && target.Handle is { } qtargetH)
                {
                    var typeArgs = new List<TypeRef>(qualifiedGeneric.GenericArguments.Count);
                    foreach (var arg in qualifiedGeneric.GenericArguments)
                        typeArgs.Add(ResolveType(arg, scope, reportDiagnostics));

                    var genericParams = GetGenericParameters(qtargetH);
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
                        context.ValidateGenericTypeArgumentConformances(qtargetH, qualifiedGeneric.GenericArguments.ToList(), typeArgs, scope, default, reportDiagnostics);

                    return InstantiateSpecialOrGenericType(qtargetH, qualifiedGeneric.Name.Value, typeArgs, qualifiedGeneric.GenericArguments);
                }

                return target;
            }

            TypeRef result = ResolveTypeName(syntax, scope, reportDiagnostics);

            if (result.Handle is { } rightHandle)
                context.ResolvedTree.AddReference(qualified.Right, rightHandle);

            CheckUntaggedUsage(result, syntax, isUntaggedContext, reportDiagnostics);
            if (!isTraitBoundContext && !isDynTargetContext && result.Handle is { } resH && IsInterfaceSymbol(context, resH))
            {
                if (reportDiagnostics)
                    ReportMissingDynForInterface(qualified, context.GetSymbol(context.UnwrapAlias(resH))?.Name.Text ?? result.ToDisplayString(context));
                return TypeRef.Error;
            }
            return result;
        }

        if (syntax is ScopeQualifiedType scopeQualified)
        {
            return ResolveScopeQualifiedType(scopeQualified, scope, reportDiagnostics, isUntaggedContext, isTraitBoundContext, isDynTargetContext);
        }

        if (syntax is ConformanceQualifiedType confQualified)
        {
            return context.ResolveConformanceQualifiedType(
                confQualified,
                scope,
                default,
                (ts, sc) => ResolveType(ts, sc, reportDiagnostics, allowInference: false, isUntaggedContext: false, isTraitBoundContext: true),
                (ts, sc) => ResolveType(ts, sc, reportDiagnostics, allowInference, isUntaggedContext, isTraitBoundContext, isDynTargetContext),
                reportDiagnostics);
        }

        ResolveTypeChildren(syntax, scope);

        var finalRes = ResolveTypeName(syntax, scope, reportDiagnostics);
        CheckUntaggedUsage(finalRes, syntax, isUntaggedContext, reportDiagnostics);
        if (!isTraitBoundContext && !isDynTargetContext && finalRes.Handle is { } fHandle && IsInterfaceSymbol(context, fHandle))
        {
            if (reportDiagnostics)
                ReportMissingDynForInterface(syntax, context.GetSymbol(context.UnwrapAlias(fHandle))?.Name.Text ?? finalRes.ToDisplayString(context));
            return TypeRef.Error;
        }
        return finalRes;
    }

    private TypeRef InstantiateSpecialOrGenericType(SymbolHandle targetH, string targetName, IReadOnlyList<TypeRef> typeArgs, SeparatedSyntaxList<TypeSyntax> argSyntaxes)
    {
        var unwrapped = context.UnwrapAlias(targetH);
        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Slice, out var sliceH) && unwrapped == sliceH && typeArgs.Count >= 1)
        {
            int dim = 1;
            if (argSyntaxes.Count >= 2 && argSyntaxes[1] is LiteralGenericArgument lit && int.TryParse(lit.Expression.Literal.Value, out int parsedDim) && parsedDim >= 1)
                dim = parsedDim;
            return TypeRef.Slice(typeArgs[0], dim, sliceH);
        }

        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var arrH) && unwrapped == arrH && typeArgs.Count >= 2 && argSyntaxes.Count >= 2 && argSyntaxes[1] is LiteralGenericArgument litSize)
        {
            var sizes = new List<Expression> { litSize.Expression };
            for (int i = 2; i < argSyntaxes.Count; i++)
            {
                if (argSyntaxes[i] is LiteralGenericArgument l)
                    sizes.Add(l.Expression);
            }
            return TypeRef.Array(typeArgs[0], sizes, arrH);
        }

        return TypeRef.Generic(targetH, targetName, typeArgs);
    }

    private bool ReportInvalidDynIndirectionIfWrapped(TypeSyntax innerSyntax, TypeSyntax outerSyntax, bool reportDiagnostics)
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

    private void ReportMissingDynForInterface(TypeSyntax syntax, string ifaceName)
    {
        context.Diagnostics.ReportInvalidInterfaceOrDynUsage(
            syntax.GetSpan() ?? default,
            $"interface '{ifaceName}' cannot be used as a type without 'dyn'",
            $"missing 'dyn' before interface '{ifaceName}'",
            $"use 'dyn &{ifaceName}' (or 'dyn {ifaceName}&') for references/aliases, or 'Box<dyn {ifaceName}>' for owned existentials",
            syntax.GetSource());
    }

    private TypeRef ResolveScopeQualifiedType(
        ScopeQualifiedType scopeQualified,
        Scope scope,
        bool reportDiagnostics,
        bool isUntaggedContext,
        bool isTraitBoundContext,
        bool isDynTargetContext)
    {
        var memberPath = ResolutionContext.GetSymbolName(scopeQualified.MemberType);
        GenericType? leafGeneric = FindLeafGenericType(scopeQualified.MemberType);
        SymbolHandle handle;
        if (context.ResolvedTree.TryGetReference(scopeQualified, out var preboundH))
        {
            handle = preboundH;
        }
        else
        {
            var syms = context.ResolveScopedSymbols(
                scopeQualified.Qualifier,
                memberPath,
                scope,
                typesOnly: true,
                scopeQualified.GetSpan() ?? default,
                scopeQualified.GetSource(),
                reportAmbiguity: reportDiagnostics,
                out bool isAmbiguousAcrossModules);

            if (syms.Count == 0)
            {
                if (leafGeneric is not null)
                    ResolveGenericArguments(leafGeneric, null, scope);

                if (reportDiagnostics && !isAmbiguousAcrossModules)
                {
                    context.Diagnostics.ReportUnresolvedTypeReference(
                        scopeQualified.GetSpan() ?? default,
                        $"{scopeQualified.Qualifier.Value}::{memberPath.ToDisplayString()}",
                        scopeQualified.GetSource(),
                        scopeQualified.ExpansionOrigin);
                }
                return TypeRef.Error;
            }

            handle = ResolutionContext.GetHandle(syms[0]);
            context.ResolvedTree.AddReference(scopeQualified, handle);
            context.ResolvedTree.AddReference(scopeQualified.MemberType, handle);
        }

        if (leafGeneric is not null)
        {
            context.ResolvedTree.AddReference(leafGeneric, handle);
            ResolveGenericArguments(leafGeneric, handle, scope);

            if (!isTraitBoundContext && !isDynTargetContext && IsInterfaceSymbol(context, handle))
            {
                if (reportDiagnostics)
                    ReportMissingDynForInterface(scopeQualified, context.GetSymbol(context.UnwrapAlias(handle))?.Name.Text ?? leafGeneric.Name.Value);
                return TypeRef.Error;
            }

            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var optH) &&
                handle == optH &&
                leafGeneric.GenericArguments.Count == 1)
            {
                var optElem = ResolveType(leafGeneric.GenericArguments[0], scope, reportDiagnostics);
                if (reportDiagnostics && !optElem.IsError && !optElem.IsSized(context))
                    context.Diagnostics.ReportGenericConstraintNotSized(leafGeneric.GenericArguments[0].GetSpan() ?? default, optElem.ToDisplayString(context), "T", KnownIntrinsics.Optional, "Sized", scopeQualified.GetSource());
                return TypeRef.Optional(optElem, optH);
            }

            var typeArgs = new List<TypeRef>(leafGeneric.GenericArguments.Count);
            foreach (var arg in leafGeneric.GenericArguments)
                typeArgs.Add(ResolveType(arg, scope, reportDiagnostics));

            var genericParams = GetGenericParameters(handle);
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
                context.ValidateGenericTypeArgumentConformances(handle, leafGeneric.GenericArguments.ToList(), typeArgs, scope, default, reportDiagnostics);

            return InstantiateSpecialOrGenericType(handle, leafGeneric.Name.Value, typeArgs, leafGeneric.GenericArguments);
        }

        if (scopeQualified.MemberType is QualifiedType qt)
            context.ResolvedTree.AddReference(qt.Right, handle);

        var res = TypeRef.Resolved(handle);
        CheckUntaggedUsage(res, scopeQualified, isUntaggedContext, reportDiagnostics);
        if (!isTraitBoundContext && !isDynTargetContext && IsInterfaceSymbol(context, handle))
        {
            if (reportDiagnostics)
                ReportMissingDynForInterface(scopeQualified, context.GetSymbol(context.UnwrapAlias(handle))?.Name.Text ?? res.ToDisplayString(context));
            return TypeRef.Error;
        }
        return res;
    }

    private static GenericType? FindLeafGenericType(TypeSyntax syntax) => syntax switch
    {
        GenericType gt => gt,
        QualifiedType qt => FindLeafGenericType(qt.Right),
        _ => null
    };

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
                ? context.GetGenericParameterSymbol(parameters[index])
                : (parameters.Count > 0 && parameters[^1].Kind is SymbolKind.GenericParameter && context.GetGenericParameterSymbol(parameters[^1]) is { IsVariadic: true } variadicParam
                    ? variadicParam
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

    private TypeRef ResolveSliceModifier(ModifiedType modified, SliceTypeModifier sliceMod, TypeRef innerType, bool reportDiagnostics = true)
    {
        if (reportDiagnostics && !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Slice))
        {
            var span = modified.Modifier.GetSpan() ?? modified.GetSpan() ?? default;
            context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "slice type syntax", KnownIntrinsics.Slice, modified.GetSource());
        }

        if (reportDiagnostics && !innerType.IsError && !innerType.IsSized(context))
        {
            var span = modified.Type.GetSpan() ?? modified.GetSpan() ?? default;
            context.Diagnostics.ReportGenericConstraintNotSized(span, innerType.ToDisplayString(context), "T", KnownIntrinsics.Slice, "Sized", modified.GetSource());
        }

        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Slice, out var sliceHandle);
        return TypeRef.Slice(innerType, sliceMod.Dimension, sliceHandle != default ? sliceHandle : null);
    }

    private TypeRef ResolveArrayModifier(ModifiedType modified, ArrayTypeModifier arrayMod, TypeRef innerType, Scope scope, bool reportDiagnostics = true)
    {
        if (reportDiagnostics && !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Array))
        {
            var span = arrayMod.GetSpan() ?? default;
            context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "array type syntax", KnownIntrinsics.Array, modified.GetSource());
        }

        if (reportDiagnostics && !innerType.IsError && !innerType.IsSized(context))
        {
            var span = modified.Type.GetSpan() ?? modified.GetSpan() ?? default;
            context.Diagnostics.ReportGenericConstraintNotSized(span, innerType.ToDisplayString(context), "T", KnownIntrinsics.Array, "Sized", modified.GetSource());
        }

        foreach (var s in arrayMod.Sizes)
            ResolveExpression(s, scope, default);

        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var arrayHandle);
        return TypeRef.Array(innerType, arrayMod.Sizes.ToList(), arrayHandle != default ? arrayHandle : null);
    }

    private void CheckInferredInitializer(Expression expr, bool isInferred)
    {
        if (expr is CollectionExpression collection && isInferred)
        {
            if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Array))
            {
                var span = collection.GetSpan() ?? default;
                context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "inferred collection expression", KnownIntrinsics.Array, collection.GetSource());
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
            case GroupedType grouped:
                ResolveTypeChildren(grouped.InnerType, scope);
                break;
        }
    }

}
