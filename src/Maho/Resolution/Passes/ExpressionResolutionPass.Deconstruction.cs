using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class ExpressionResolutionPass
{
    internal bool TryResolveDeconstructTuple(
        TypeRef sourceType,
        out TupleSemanticType? resultTuple,
        out SymbolHandle deconstructMethod,
        bool requireMutable = false)
    {
        resultTuple = null;
        deconstructMethod = default;

        var handle = sourceType.Handle;
        if (!handle.HasValue && (sourceType.SpecialType is TupleSemanticType || sourceType.Kind == TypeRefKind.Tuple))
        {
            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var hTuple))
                handle = hTuple;
        }

        // Check if sourceType implements Deconstruct<T...> or DeconstructMut<T...>
        var opKinds = requireMutable
            ? new[] { OperatorKind.DeconstructMut }
            : new[] { OperatorKind.Deconstruct, OperatorKind.DeconstructMut };

        foreach (var opKind in opKinds)
        {
            if (!context.DiscoveredOperators.TryGetOperators(opKind, out var candidates) || candidates.Count == 0)
                continue;

            var sym = handle.HasValue ? context.GetSymbol(handle.Value) : null;
            IReadOnlyList<TypeRef> baseTypes = sym switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                _ => []
            };

            foreach (var cand in candidates)
            {
                var match = baseTypes.FirstOrDefault(b => b.Handle == cand.InterfaceHandle);
                if (!match.Handle.HasValue)
                    continue;

                // Match found!
                var ifaceSym = context.GetTypeSymbol(cand.InterfaceHandle);
                var typeScope = sym switch
                {
                    TypeSymbol ts => GetOwnedScope(ts),
                    NestedTypeSymbol nts => GetOwnedScope(nts),
                    _ => null
                };

                MethodSymbol? matchedMethod = null;
                if (typeScope is not null)
                {
                    foreach (var s in typeScope.Symbols.Values)
                    {
                        if (s is MethodSymbol m)
                        {
                            if (m.ExplicitInterfaceName is { } ein && ifaceSym is not null &&
                                (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                            {
                                matchedMethod = m;
                                break;
                            }
                            if (cand.MethodHandle != default && m.Name.Text == context.GetSymbol(cand.MethodHandle)?.Name.Text)
                            {
                                matchedMethod = m;
                                break;
                            }
                        }
                    }
                }

                if (matchedMethod is not null)
                    deconstructMethod = ResolutionContext.GetHandle(matchedMethod);
                else if (cand.MethodHandle != default)
                    deconstructMethod = cand.MethodHandle;

                if (sourceType.SpecialType is TupleSemanticType tupleSem)
                {
                    resultTuple = tupleSem;
                    return true;
                }

                if (matchedMethod is not null && matchedMethod.ReturnType.SpecialType is TupleSemanticType mTuple)
                {
                    resultTuple = mTuple;
                    return true;
                }

                if (match.SpecialType is GenericSemanticType matchGst && matchGst.TypeArguments.Count > 0)
                {
                    resultTuple = new TupleSemanticType(matchGst.TypeArguments.Select(t => new TupleElement(t, null)).ToList());
                    return true;
                }

                if (matchedMethod is not null && matchedMethod.ReturnType.Kind == TypeRefKind.Tuple && matchedMethod.ReturnType.SpecialType is GenericSemanticType retGst)
                {
                    resultTuple = new TupleSemanticType(retGst.TypeArguments.Select(t => new TupleElement(t, null)).ToList());
                    return true;
                }
            }
        }

        return false;
    }
}
