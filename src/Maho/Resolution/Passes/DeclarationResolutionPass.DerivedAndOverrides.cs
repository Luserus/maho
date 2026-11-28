using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

internal sealed partial class DeclarationResolutionPass
{
    private (SymbolHandle OwnerHandle, TypeRef OwnerSelf)? activeDerivedBaseContext;

    private static bool ContainsDerivedGenericArgument(TypeSyntax syntax) => syntax switch
    {
        DerivedGenericArgumentSyntax => true,
        GroupedType g => ContainsDerivedGenericArgument(g.InnerType),
        GenericType gen => gen.GenericArguments.Any(ContainsDerivedGenericArgument),
        QualifiedType q => ContainsDerivedGenericArgument(q.Left) || ContainsDerivedGenericArgument(q.Right),
        ScopeQualifiedType sq => ContainsDerivedGenericArgument(sq.MemberType),
        ConformanceQualifiedType cq => ContainsDerivedGenericArgument(cq.TargetType) || ContainsDerivedGenericArgument(cq.InterfaceType),
        ModifiedType m => ContainsDerivedGenericArgument(m.Type),
        PrefixModifiedType pm => ContainsDerivedGenericArgument(pm.Type),
        TupleType tup => tup.Elements.Any(e => ContainsDerivedGenericArgument(e.Type)),
        UnionType un => un.Variants.Any(ContainsDerivedGenericArgument),
        UniformTupleType ut => ContainsDerivedGenericArgument(ut.ElementType),
        FunctionTypeSyntax fn => ContainsDerivedGenericArgument(fn.ReturnType) || fn.ParameterTypes.Any(ContainsDerivedGenericArgument),
        _ => false
    };

    private static TypeRef BuildDerivedInterfaceTemplate(TypeSyntax syntax, TypeRef baseRef, SymbolHandle originBaseHandle)
    {
        switch (syntax)
        {
            case GroupedType g:
                return BuildDerivedInterfaceTemplate(g.InnerType, baseRef, originBaseHandle);

            case DerivedGenericArgumentSyntax:
                return TypeRef.DerivedSelf(originBaseHandle);

            case QualifiedType q:
                return BuildDerivedInterfaceTemplate(q.Right, baseRef, originBaseHandle);

            case ScopeQualifiedType sq:
                return BuildDerivedInterfaceTemplate(sq.MemberType, baseRef, originBaseHandle);

            case ConformanceQualifiedType cq when baseRef.SpecialType is ConformanceSemanticType conf:
                return TypeRef.Conformance(
                    BuildDerivedInterfaceTemplate(cq.TargetType, conf.TargetType, originBaseHandle),
                    conf.Conformance);

            case GenericType gen when baseRef.SpecialType is GenericSemanticType semGen:
            {
                var newArgs = new List<TypeRef>(semGen.TypeArguments.Count);
                for (int i = 0; i < semGen.TypeArguments.Count; i++)
                {
                    newArgs.Add(i < gen.GenericArguments.Count
                        ? BuildDerivedInterfaceTemplate(gen.GenericArguments[i], semGen.TypeArguments[i], originBaseHandle)
                        : semGen.TypeArguments[i]);
                }
                return TypeRef.Generic(semGen.TargetHandle, semGen.TargetName, newArgs);
            }

            case ModifiedType m:
                return baseRef.SpecialType switch
                {
                    PointerSemanticType p => TypeRef.Pointer(BuildDerivedInterfaceTemplate(m.Type, p.ElementType, originBaseHandle), p.IsMutable),
                    ReferenceSemanticType r => TypeRef.Reference(BuildDerivedInterfaceTemplate(m.Type, r.ElementType, originBaseHandle), r.IsMutable),
                    OptionalSemanticType o => TypeRef.Optional(BuildDerivedInterfaceTemplate(m.Type, o.ElementType, originBaseHandle), baseRef.Handle),
                    SliceSemanticType s => TypeRef.Slice(BuildDerivedInterfaceTemplate(m.Type, s.ElementType, originBaseHandle), s.Dimension, baseRef.Handle),
                    ArraySemanticType a => TypeRef.Array(BuildDerivedInterfaceTemplate(m.Type, a.ElementType, originBaseHandle), a.Sizes, baseRef.Handle),
                    _ => baseRef
                };

            case PrefixModifiedType pm:
                return baseRef.SpecialType switch
                {
                    DynSemanticType dyn when pm.Modifier is DynTypeModifier => TypeRef.Dyn(BuildDerivedInterfaceTemplate(pm.Type, dyn.TargetType, originBaseHandle), dyn.IsExplicitDyn),
                    DynSemanticType dyn when pm.Modifier is SafeAliasTypeModifier or UnsafeAliasTypeModifier =>
                        TypeRef.Dyn(BuildDerivedInterfaceTemplate(pm.Type, dyn.TargetType, originBaseHandle), dyn.IsExplicitDyn),
                    UntaggedSemanticType un => TypeRef.Untagged(BuildDerivedInterfaceTemplate(pm.Type, un.InnerType, originBaseHandle)),
                    ReferenceSemanticType r => TypeRef.Reference(BuildDerivedInterfaceTemplate(pm.Type, r.ElementType, originBaseHandle), r.IsMutable),
                    PointerSemanticType p => TypeRef.Pointer(BuildDerivedInterfaceTemplate(pm.Type, p.ElementType, originBaseHandle), p.IsMutable),
                    _ => baseRef
                };

            case TupleType tup when baseRef.SpecialType is TupleSemanticType semTup && tup.Elements.Count == semTup.Elements.Count:
            {
                var newElems = new List<TupleElement>(semTup.Elements.Count);
                for (int i = 0; i < tup.Elements.Count; i++)
                {
                    newElems.Add(new TupleElement(
                        BuildDerivedInterfaceTemplate(tup.Elements[i].Type, semTup.Elements[i].Type, originBaseHandle),
                        semTup.Elements[i].Name));
                }
                return TypeRef.Tuple(newElems, baseRef.Handle);
            }

            default:
                return baseRef;
        }
    }

    private void SynthesizeDerivedConformances()
    {
        foreach (var type in context.TypeSymbols)
            SynthesizeDerivedConformancesForType(type, ResolutionContext.GetHandle(type));

        foreach (var type in context.NestedTypeSymbols)
            SynthesizeDerivedConformancesForType(type, ResolutionContext.GetHandle(type));
    }

    private void SynthesizeDerivedConformancesForType(Symbol typeSymbol, SymbolHandle typeHandle)
    {
        typeHandle = context.UnwrapAlias(typeHandle);
        if (typeSymbol.Name.Text == "_")
            return;

        if (!TryGetNonInterfaceTypeMetadata(typeSymbol, out var genericParams, out var baseTypes, out var linearizedBases))
            return;

        if (linearizedBases.Count <= 1)
            return;

        var ownerSelf = BuildSelfTypeRef(typeHandle, typeSymbol.Name.Text, genericParams);
        var (baseTypeArgs, baseInstantiations) = CollectBaseStructMetadata(baseTypes);

        foreach (var rawBaseH in linearizedBases.Skip(1))
        {
            var baseH = context.UnwrapAlias(rawBaseH);
            var baseSym = context.GetSymbol(baseH);
            if (baseSym is null || IsInterfaceSymbol(baseSym))
                continue;

            var baseConfs = new List<InterfaceConformance>();
            context.CollectConformancesForTarget(baseH, baseConfs);

            foreach (var conf in baseConfs)
            {
                if (conf.IsExtension ||
                    conf.InheritedFromTypeHandle is not null ||
                    conf.DerivedInterfaceTemplate is not { } template ||
                    context.UnwrapAlias(conf.OwnerHandle) != baseH)
                {
                    continue;
                }

                if (conf.Accessibility == Accessibility.Private)
                    continue;

                var instantiatedTemplate = baseTypeArgs.TryGetValue(baseH, out var subst) && subst.Count > 0
                    ? template.Substitute(subst)
                    : template;

                var reboundIface = instantiatedTemplate.SubstituteDerivedSelf(baseH, ownerSelf);

                // Respect explicit conformances already declared on this derived type (or an earlier derived template).
                bool alreadyExists = context.Conformances.Any(existing =>
                    !existing.IsExtension &&
                    context.UnwrapAlias(existing.OwnerHandle) == typeHandle &&
                    existing.InterfaceHandle == conf.InterfaceHandle &&
                    existing.InterfaceType.Equals(reboundIface));

                if (alreadyExists)
                    continue;

                context.RegisterConformance(
                    ownerSelf,
                    reboundIface,
                    conf.InterfaceHandle,
                    conf.Accessibility,
                    typeHandle,
                    isExtension: false,
                    conf.Syntax,
                    derivedInterfaceTemplate: instantiatedTemplate,
                    inheritedFromTypeHandle: baseH);
            }
        }
    }

    private (Dictionary<SymbolHandle, Dictionary<SymbolHandle, TypeRef>> TypeArgs, Dictionary<SymbolHandle, TypeRef> Instantiations)
        CollectBaseStructMetadata(IReadOnlyList<TypeRef> rootBases)
    {
        var argsMap = new Dictionary<SymbolHandle, Dictionary<SymbolHandle, TypeRef>>();
        var instMap = new Dictionary<SymbolHandle, TypeRef>();
        var visited = new HashSet<SymbolHandle>();

        void Visit(TypeRef typeRef, Dictionary<SymbolHandle, TypeRef> parentArgs)
        {
            if (typeRef.Handle is not { } rawH)
                return;

            var h = context.UnwrapAlias(rawH);
            var sym = context.GetSymbol(h);
            if (sym is null || IsInterfaceSymbol(sym) || !visited.Add(h))
                return;

            var subst = new Dictionary<SymbolHandle, TypeRef>(parentArgs);
            IReadOnlyList<SymbolHandle> gps = GetGenericParameters(h);
            if (typeRef.SpecialType is GenericSemanticType gen && gps.Count == gen.TypeArguments.Count)
            {
                for (int i = 0; i < gps.Count; i++)
                    subst[gps[i]] = gen.TypeArguments[i].Substitute(parentArgs);
            }

            argsMap[h] = subst;
            instMap[h] = parentArgs.Count > 0 ? typeRef.Substitute(parentArgs) : typeRef;

            IReadOnlyList<TypeRef> nextBases = sym switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                _ => []
            };
            foreach (var nb in nextBases)
                Visit(nb, subst);
        }

        foreach (var rb in rootBases)
            Visit(rb, []);

        return (argsMap, instMap);
    }

    private void ValidateMethodOverrides()
    {
        foreach (var method in context.MethodSymbols)
        {
            if (!method.Flags.HasFlag(FunctionFlags.Override) || method.Syntax is null)
                continue;

            if (method.Parent is not { } rawParentH)
                continue;

            var parentH = context.UnwrapAlias(rawParentH);
            var parentSym = context.GetSymbol(parentH);
            if (parentSym is null || IsInterfaceSymbol(parentSym))
                continue;

            var span = method.Syntax.Signature.Identifier.GetSpan() ?? method.Syntax.GetSpan() ?? default;
            var source = method.Syntax.GetSource();

            if (method.Flags.HasFlag(FunctionFlags.Static))
            {
                context.Diagnostics.ReportInheritanceOrOverrideError(
                    span,
                    $"cannot mark static method '{method.Name.Text}' as 'override'",
                    source);
                continue;
            }

            if (!TryGetNonInterfaceTypeMetadata(parentSym, out _, out var baseTypes, out var linearizedBases))
                continue;

            var (baseTypeArgs, _) = CollectBaseStructMetadata(baseTypes);
            var (overridableBase, nonVirtualCandidate) = FindBaseMethodToOverride(method, linearizedBases);

            if (overridableBase is null)
            {
                if (nonVirtualCandidate is not null)
                {
                    var ownerName = nonVirtualCandidate.Parent is { } bh ? context.GetSymbol(bh)?.Name.Text ?? "base" : "base";
                    context.Diagnostics.ReportInheritanceOrOverrideError(
                        span,
                        $"cannot override non-virtual method '{ownerName}.{nonVirtualCandidate.Name.Text}'",
                        source,
                        nonVirtualCandidate.Syntax?.Signature.Identifier.GetSpan(),
                        nonVirtualCandidate.Syntax?.GetSource(),
                        "base method is not marked 'virtual'",
                        $"mark '{ownerName}.{nonVirtualCandidate.Name.Text}' as 'virtual' if overriding is intended");
                }
                else
                {
                    context.Diagnostics.ReportInheritanceOrOverrideError(
                        span,
                        $"method '{parentSym.Name.Text}.{method.Name.Text}' is marked 'override' cleanly, but no matching 'virtual' method was found on any base type",
                        source,
                        help: "remove 'override' or verify the method name, receiver, and parameter count match a base 'virtual' method");
                }
                continue;
            }

            if (!IsMethodOverrideCompatible(method, overridableBase, baseTypeArgs, out var mismatchReason))
            {
                var baseOwnerName = overridableBase.Parent is { } bh ? context.GetSymbol(bh)?.Name.Text ?? "base" : "base";
                context.Diagnostics.ReportInheritanceOrOverrideError(
                    span,
                    $"'{parentSym.Name.Text}.{method.Name.Text}' cannot override '{baseOwnerName}.{overridableBase.Name.Text}': {mismatchReason}",
                    source,
                    overridableBase.Syntax?.Signature.Identifier.GetSpan(),
                    overridableBase.Syntax?.GetSource(),
                    "overridden 'virtual' method declared here");
            }
        }
    }

    private (MethodSymbol? Overridable, MethodSymbol? NonVirtualMatch) FindBaseMethodToOverride(
        MethodSymbol derivedMethod,
        IReadOnlyList<SymbolHandle> linearizedBases)
    {
        MethodSymbol? nonVirtualFallback = null;
        bool hasReceiver = HasReceiverParam(derivedMethod);
        var derivedReceiverKind = hasReceiver ? context.GetParameterSymbol(derivedMethod.Parameters[0])?.Syntax?.Declarator.ReceiverKind : null;

        foreach (var rawBaseH in linearizedBases.Skip(1))
        {
            var baseH = context.UnwrapAlias(rawBaseH);
            var baseSym = context.GetSymbol(baseH);
            if (baseSym is null || IsInterfaceSymbol(baseSym))
                continue;

            var baseScope = GetOwnedScope(baseSym);
            foreach (var sym in baseScope.Symbols.Values)
            {
                if (sym is not MethodSymbol candidate || candidate.Flags.HasFlag(FunctionFlags.Static))
                    continue;

                if (!MethodNameAndScopeMatch(derivedMethod, candidate))
                    continue;

                if (candidate.GenericParameters.Count != derivedMethod.GenericParameters.Count ||
                    candidate.Parameters.Count != derivedMethod.Parameters.Count)
                    continue;

                if (HasReceiverParam(candidate) != hasReceiver)
                    continue;

                if (hasReceiver)
                {
                    var baseReceiverKind = context.GetParameterSymbol(candidate.Parameters[0])?.Syntax?.Declarator.ReceiverKind;
                    if (baseReceiverKind != derivedReceiverKind)
                        continue;
                }

                if (candidate.Flags.HasFlag(FunctionFlags.Virtual) || candidate.Flags.HasFlag(FunctionFlags.Override))
                    return (candidate, nonVirtualFallback);

                nonVirtualFallback ??= candidate;
            }
        }

        return (null, nonVirtualFallback);
    }

    private bool MethodNameAndScopeMatch(MethodSymbol a, MethodSymbol b)
    {
        if (a.Name.Text != b.Name.Text)
            return false;

        if (a.Flags.HasFlag(FunctionFlags.InterfaceScoped) != b.Flags.HasFlag(FunctionFlags.InterfaceScoped))
            return false;

        if (a.Flags.HasFlag(FunctionFlags.InterfaceScoped))
            return a.ExplicitInterfaceName?.Last.Text == b.ExplicitInterfaceName?.Last.Text;

        return true;
    }

    private bool HasReceiverParam(MethodSymbol method) =>
        method.Parameters.Count > 0 &&
        context.GetParameterSymbol(method.Parameters[0])?.Syntax?.Declarator.IsReceiver == true;

    private bool IsMethodOverrideCompatible(
        MethodSymbol derivedMethod,
        MethodSymbol baseMethod,
        Dictionary<SymbolHandle, Dictionary<SymbolHandle, TypeRef>> baseTypeArgs,
        out string mismatchReason)
    {
        var subst = new Dictionary<SymbolHandle, TypeRef>();
        if (baseMethod.Parent is { } rawBaseH &&
            baseTypeArgs.TryGetValue(context.UnwrapAlias(rawBaseH), out var ownerSubst))
        {
            foreach (var (k, v) in ownerSubst)
                subst[k] = v;
        }

        for (int g = 0; g < baseMethod.GenericParameters.Count; g++)
        {
            var baseGp = context.GetGenericParameterSymbol(baseMethod.GenericParameters[g]);
            var derivedGp = context.GetGenericParameterSymbol(derivedMethod.GenericParameters[g]);
            if (baseGp?.ParameterKind != derivedGp?.ParameterKind)
            {
                mismatchReason = "generic parameter kinds do not match";
                return false;
            }
            subst[baseMethod.GenericParameters[g]] = TypeRef.Resolved(derivedMethod.GenericParameters[g]);
        }

        int startIdx = HasReceiverParam(derivedMethod) ? 1 : 0;
        for (int i = startIdx; i < derivedMethod.Parameters.Count; i++)
        {
            var actualParam = context.GetParameterSymbol(derivedMethod.Parameters[i])?.Type ?? TypeRef.Unresolved;
            var expectedBaseParam = (context.GetParameterSymbol(baseMethod.Parameters[i])?.Type ?? TypeRef.Unresolved).Substitute(subst);

            if (!IsTypeEquivalent(actualParam, expectedBaseParam) && !actualParam.IsAssignableFrom(expectedBaseParam, context))
            {
                mismatchReason = $"parameter #{i + 1 - startIdx} type '{actualParam.ToDisplayString(context)}' is not compatible with overridden parameter type '{expectedBaseParam.ToDisplayString(context)}'";
                return false;
            }
        }

        var actualReturn = derivedMethod.ReturnType;
        var expectedBaseReturn = baseMethod.ReturnType.Substitute(subst);
        if (!IsTypeEquivalent(actualReturn, expectedBaseReturn) && !expectedBaseReturn.IsAssignableFrom(actualReturn, context))
        {
            mismatchReason = $"return type '{actualReturn.ToDisplayString(context)}' is not covariant with overridden return type '{expectedBaseReturn.ToDisplayString(context)}'";
            return false;
        }

        mismatchReason = string.Empty;
        return true;
    }

    private bool HasAddedInstanceFieldsBetween(SymbolHandle derivedHandle, SymbolHandle originBaseHandle)
    {
        derivedHandle = context.UnwrapAlias(derivedHandle);
        originBaseHandle = context.UnwrapAlias(originBaseHandle);
        if (derivedHandle == originBaseHandle)
            return false;

        var derivedSym = context.GetSymbol(derivedHandle);
        IReadOnlyList<SymbolHandle> linearized = derivedSym switch
        {
            TypeSymbol ts => ts.LinearizedBases,
            NestedTypeSymbol nts => nts.LinearizedBases,
            _ => []
        };

        var checkHandles = linearized.Count > 0 ? linearized : [derivedHandle];
        foreach (var rawH in checkHandles)
        {
            var h = context.UnwrapAlias(rawH);
            if (h == originBaseHandle)
                break;

            var sym = context.GetSymbol(h);
            if (sym is null || IsInterfaceSymbol(sym))
                continue;

            var scope = GetOwnedScope(sym);
            foreach (var member in scope.Symbols.Values)
            {
                if (member is FieldSymbol field && !field.Flags.HasFlag(VariableFlags.Static))
                    return true;
            }
        }

        return false;
    }

    private enum DerivedMethodMatchResult : byte
    {
        Incompatible = 0,
        Compatible = 1,
        NeedsOverrideForSelfReturn = 2
    }

    private DerivedMethodMatchResult CheckDerivedInheritedMethodCompatibility(
        Symbol targetTypeSymbol,
        SymbolHandle targetTypeHandle,
        MethodSymbol ancestorMethod,
        IReadOnlyList<TypeRef> expectedParamTypes,
        TypeRef expectedReturn,
        Dictionary<SymbolHandle, TypeRef>? methodGenericSubst,
        int startIdx,
        SymbolHandle derivedOriginHandle)
    {
        if (ancestorMethod.Parent is not { } rawAncestorH)
            return DerivedMethodMatchResult.Incompatible;

        var ancestorH = context.UnwrapAlias(rawAncestorH);
        targetTypeHandle = context.UnwrapAlias(targetTypeHandle);

        IReadOnlyList<TypeRef> targetRootBases = targetTypeSymbol switch
        {
            TypeSymbol ts => ts.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            _ => []
        };
        IReadOnlyList<SymbolHandle> targetGenericParams = GetGenericParameters(targetTypeHandle);

        var (baseTypeArgs, baseInstantiations) = CollectBaseStructMetadata(targetRootBases);
        var ancestorType = baseInstantiations.TryGetValue(ancestorH, out var inst)
            ? inst
            : TypeRef.Resolved(ancestorH);
        var targetSelfType = BuildSelfTypeRef(targetTypeHandle, targetTypeSymbol.Name.Text, targetGenericParams);

        Dictionary<SymbolHandle, TypeRef>? ancestorSubst = baseTypeArgs.TryGetValue(ancestorH, out var aSubst) && aSubst.Count > 0
            ? new Dictionary<SymbolHandle, TypeRef>(aSubst)
            : null;

        if (methodGenericSubst is not null)
        {
            ancestorSubst ??= [];
            foreach (var (k, v) in methodGenericSubst)
                ancestorSubst[k] = v;
        }

        bool addsInstanceFields = HasAddedInstanceFieldsBetween(targetTypeHandle, derivedOriginHandle);

        TypeRef ApplySubst(TypeRef t) => ancestorSubst is not null ? t.Substitute(ancestorSubst) : t;

        for (int i = startIdx; i < expectedParamTypes.Count; i++)
        {
            var actualParam = ApplySubst(context.GetParameterSymbol(ancestorMethod.Parameters[i])?.Type ?? TypeRef.Unresolved);
            var expectedParam = expectedParamTypes[i];

            if (!IsDerivedInputParamCompatible(actualParam, expectedParam, ancestorType, targetSelfType, addsInstanceFields))
                return DerivedMethodMatchResult.Incompatible;
        }

        var actualReturn = ApplySubst(ancestorMethod.ReturnType);
        if (IsTypeEquivalent(actualReturn, expectedReturn))
            return DerivedMethodMatchResult.Compatible;

        if (MatchesWithAncestorToDerivedReplacement(actualReturn, expectedReturn, ancestorType, targetSelfType))
        {
            return addsInstanceFields
                ? DerivedMethodMatchResult.NeedsOverrideForSelfReturn
                : DerivedMethodMatchResult.Compatible;
        }

        return DerivedMethodMatchResult.Incompatible;
    }

    private bool IsDerivedInputParamCompatible(
        TypeRef actualParam,
        TypeRef expectedParam,
        TypeRef ancestorType,
        TypeRef derivedSelfType,
        bool addsInstanceFields)
    {
        if (IsTypeEquivalent(actualParam, expectedParam))
            return true;

        if (actualParam.SpecialType is ReferenceSemanticType actualRef &&
            expectedParam.SpecialType is ReferenceSemanticType expectedRef)
        {
            if (actualRef.IsMutable && !expectedRef.IsMutable)
                return false;

            return IsDerivedInputParamCompatible(actualRef.ElementType, expectedRef.ElementType, ancestorType, derivedSelfType, addsInstanceFields: false);
        }

        if (actualParam.SpecialType is PointerSemanticType actualPtr &&
            expectedParam.SpecialType is PointerSemanticType expectedPtr)
        {
            if (actualPtr.IsMutable && !expectedPtr.IsMutable)
                return false;

            return IsDerivedInputParamCompatible(actualPtr.ElementType, expectedPtr.ElementType, ancestorType, derivedSelfType, addsInstanceFields: false);
        }

        if (addsInstanceFields)
            return false;

        return MatchesWithAncestorToDerivedReplacement(actualParam, expectedParam, ancestorType, derivedSelfType);
    }

    private bool MatchesWithAncestorToDerivedReplacement(
        TypeRef actualType,
        TypeRef expectedType,
        TypeRef ancestorType,
        TypeRef derivedSelfType)
    {
        var unwrappedAncestor = context.UnwrapAlias(ancestorType);
        var unwrappedActual = context.UnwrapAlias(actualType);
        var unwrappedExpected = context.UnwrapAlias(expectedType);

        if (IsTypeEquivalent(unwrappedActual, unwrappedAncestor) &&
            IsTypeEquivalent(unwrappedExpected, derivedSelfType))
            return true;

        if (unwrappedActual.SpecialType is ReferenceSemanticType ar && unwrappedExpected.SpecialType is ReferenceSemanticType er)
            return ar.IsMutable == er.IsMutable && MatchesWithAncestorToDerivedReplacement(ar.ElementType, er.ElementType, unwrappedAncestor, derivedSelfType);

        if (unwrappedActual.SpecialType is PointerSemanticType ap && unwrappedExpected.SpecialType is PointerSemanticType ep)
            return ap.IsMutable == ep.IsMutable && MatchesWithAncestorToDerivedReplacement(ap.ElementType, ep.ElementType, unwrappedAncestor, derivedSelfType);

        if (unwrappedActual.SpecialType is OptionalSemanticType ao && unwrappedExpected.SpecialType is OptionalSemanticType eo)
            return MatchesWithAncestorToDerivedReplacement(ao.ElementType, eo.ElementType, unwrappedAncestor, derivedSelfType);

        if (unwrappedActual.SpecialType is GenericSemanticType ag &&
            unwrappedExpected.SpecialType is GenericSemanticType eg &&
            context.UnwrapAlias(ag.TargetHandle) == context.UnwrapAlias(eg.TargetHandle) &&
            ag.TypeArguments.Count == eg.TypeArguments.Count)
        {
            for (int i = 0; i < ag.TypeArguments.Count; i++)
            {
                if (!IsTypeEquivalent(ag.TypeArguments[i], eg.TypeArguments[i]) &&
                    !MatchesWithAncestorToDerivedReplacement(ag.TypeArguments[i], eg.TypeArguments[i], unwrappedAncestor, derivedSelfType))
                {
                    return false;
                }
            }
            return true;
        }

        if (unwrappedActual.SpecialType is TupleSemanticType at &&
            unwrappedExpected.SpecialType is TupleSemanticType et &&
            at.Elements.Count == et.Elements.Count)
        {
            for (int i = 0; i < at.Elements.Count; i++)
            {
                if (!IsTypeEquivalent(at.Elements[i].Type, et.Elements[i].Type) &&
                    !MatchesWithAncestorToDerivedReplacement(at.Elements[i].Type, et.Elements[i].Type, unwrappedAncestor, derivedSelfType))
                {
                    return false;
                }
            }
            return true;
        }

        return false;
    }

    private static bool TryGetNonInterfaceTypeMetadata(
        Symbol sym,
        out IReadOnlyList<SymbolHandle> genericParams,
        out IReadOnlyList<TypeRef> baseTypes,
        out IReadOnlyList<SymbolHandle> linearizedBases)
    {
        switch (sym)
        {
            case TypeSymbol ts when ts.TypeKind != TypeKind.Interface:
                genericParams = ts.GenericParameters;
                baseTypes = ts.BaseTypes;
                linearizedBases = ts.LinearizedBases;
                return true;
            case NestedTypeSymbol nts when nts.TypeKind != TypeKind.Interface:
                genericParams = nts.GenericParameters;
                baseTypes = nts.BaseTypes;
                linearizedBases = nts.LinearizedBases;
                return true;
            default:
                genericParams = [];
                baseTypes = [];
                linearizedBases = [];
                return false;
        }
    }

    private static bool IsInterfaceSymbol(Symbol sym) => sym switch
    {
        TypeSymbol ts => ts.TypeKind == TypeKind.Interface,
        NestedTypeSymbol nts => nts.TypeKind == TypeKind.Interface,
        _ => false
    };
}
