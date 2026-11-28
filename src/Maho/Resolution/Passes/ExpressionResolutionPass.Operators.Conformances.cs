using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class ExpressionResolutionPass
{
    /// <summary>
    /// Resolves a binary operator against type conformances (including <c>derived</c>-synthesized conformances)
    /// and generic parameter constraints when direct lexical scopes do not declare a concrete method.
    /// </summary>
    private bool TryResolveBinaryOperatorViaConformances(
        OperatorKind opKind,
        TypeRef lhs,
        TypeRef rhs,
        BinaryExpression binary,
        out TypeRef resultType)
    {
        resultType = TypeRef.Unresolved;

        if (!context.DiscoveredOperators.TryGetOperators(opKind, out var candidates) || candidates.Count == 0)
            return false;

        foreach (var operand in new[] { lhs, rhs })
        {
            var concreteOperand = operand;
            while (concreteOperand.SpecialType is AliasSemanticType al)
                concreteOperand = al.TargetType;

            if (concreteOperand.Handle is not { } rawH)
                continue;

            var unwrappedH = context.UnwrapAlias(rawH);

            if (context.GetGenericParameterSymbol(unwrappedH) is { } gpSym)
            {
                if (TryMatchBinaryConstraints(candidates, gpSym.Constraints, lhs, rhs, binary, out resultType))
                    return true;
                continue;
            }

            var confs = new List<InterfaceConformance>();
            context.CollectConformancesForTarget(unwrappedH, confs);
            if (TryMatchBinaryConformances(candidates, confs, concreteOperand, lhs, rhs, binary, out resultType))
                return true;
        }

        return false;
    }

    private bool TryMatchBinaryConstraints(
        IReadOnlyList<DiscoveredOperatorMethod> candidates,
        IReadOnlyList<TypeRef> constraints,
        TypeRef lhs,
        TypeRef rhs,
        BinaryExpression binary,
        out TypeRef resultType)
    {
        foreach (var candidate in candidates)
        {
            if (context.GetMethodSymbol(candidate.MethodHandle) is not { } ifaceMethod ||
                ifaceMethod.Parameters.Count != 2)
                continue;

            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null)
                continue;

            foreach (var constraint in constraints)
            {
                if (constraint.Handle is not { } cH || context.UnwrapAlias(cH) != candidate.InterfaceHandle)
                    continue;

                var typeArgs = BuildInterfaceSubstitutionMap(ifaceSym, constraint);
                if (MatchesBinaryInterfaceSignature(ifaceMethod, typeArgs, lhs, rhs, out var resolvedRet))
                {
                    context.ResolvedTree.AddReference(binary, candidate.MethodHandle);
                    resultType = resolvedRet;
                    return true;
                }
            }
        }

        resultType = TypeRef.Unresolved;
        return false;
    }

    private bool TryMatchBinaryConformances(
        IReadOnlyList<DiscoveredOperatorMethod> candidates,
        IReadOnlyList<InterfaceConformance> conformances,
        TypeRef operandType,
        TypeRef lhs,
        TypeRef rhs,
        BinaryExpression binary,
        out TypeRef resultType)
    {
        foreach (var candidate in candidates)
        {
            if (context.GetMethodSymbol(candidate.MethodHandle) is not { } ifaceMethod ||
                ifaceMethod.Parameters.Count != 2)
                continue;

            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null)
                continue;

            foreach (var conf in conformances)
            {
                if (conf.InterfaceHandle != candidate.InterfaceHandle)
                    continue;

                var instantiatedIfaceType = conf.InterfaceType;
                if (operandType.SpecialType is GenericSemanticType opGen &&
                    conf.TargetType.Handle is { } ctH)
                {
                    var ctGps = GetTypeGenericParameters(context.UnwrapAlias(ctH));
                    if (ctGps.Count == opGen.TypeArguments.Count && ctGps.Count > 0)
                    {
                        var ownerSubst = new Dictionary<SymbolHandle, TypeRef>(ctGps.Count);
                        for (int i = 0; i < ctGps.Count; i++)
                            ownerSubst[ctGps[i]] = opGen.TypeArguments[i];
                        instantiatedIfaceType = instantiatedIfaceType.Substitute(ownerSubst);
                    }
                }

                var typeArgs = BuildInterfaceSubstitutionMap(ifaceSym, instantiatedIfaceType);
                if (MatchesBinaryInterfaceSignature(ifaceMethod, typeArgs, lhs, rhs, out var resolvedRet))
                {
                    var targetMethodHandle = conf.MethodMap.TryGetValue(candidate.MethodHandle, out var mappedH)
                        ? mappedH
                        : candidate.MethodHandle;
                    context.ResolvedTree.AddReference(binary, targetMethodHandle);
                    resultType = resolvedRet;
                    return true;
                }
            }
        }

        resultType = TypeRef.Unresolved;
        return false;
    }

    private Dictionary<SymbolHandle, TypeRef> BuildInterfaceSubstitutionMap(TypeSymbol ifaceSym, TypeRef ifaceType)
    {
        var map = new Dictionary<SymbolHandle, TypeRef>();
        if (ifaceType.SpecialType is GenericSemanticType gst &&
            gst.TypeArguments.Count == ifaceSym.GenericParameters.Count)
        {
            for (int i = 0; i < ifaceSym.GenericParameters.Count; i++)
                map[ifaceSym.GenericParameters[i]] = gst.TypeArguments[i];
        }
        return map;
    }

    private bool MatchesBinaryInterfaceSignature(
        MethodSymbol ifaceMethod,
        Dictionary<SymbolHandle, TypeRef> typeArgs,
        TypeRef lhs,
        TypeRef rhs,
        out TypeRef returnType)
    {
        returnType = TypeRef.Unresolved;

        var p0 = context.GetParameterSymbol(ifaceMethod.Parameters[0])?.Type ?? TypeRef.Unresolved;
        var p1 = context.GetParameterSymbol(ifaceMethod.Parameters[1])?.Type ?? TypeRef.Unresolved;
        if (typeArgs.Count > 0)
        {
            p0 = p0.Substitute(typeArgs);
            p1 = p1.Substitute(typeArgs);
        }

        if (!IsBinaryOperandCompatible(lhs, p0) || !IsBinaryOperandCompatible(rhs, p1))
            return false;

        returnType = typeArgs.Count > 0 ? ifaceMethod.ReturnType.Substitute(typeArgs) : ifaceMethod.ReturnType;
        return true;
    }

    private bool IsBinaryOperandCompatible(TypeRef actual, TypeRef expected)
    {
        while (actual.SpecialType is AliasSemanticType al)
            actual = al.TargetType;

        if (actual.Equals(expected) || expected.IsAssignableFrom(actual, context))
            return true;

        return TryResolveImplicitCast(actual, expected, out _);
    }

    private IReadOnlyList<SymbolHandle> GetTypeGenericParameters(SymbolHandle typeHandle) => context.GetSymbol(typeHandle) switch
    {
        TypeSymbol ts => ts.GenericParameters,
        NestedTypeSymbol nts => nts.GenericParameters,
        _ => []
    };
}
