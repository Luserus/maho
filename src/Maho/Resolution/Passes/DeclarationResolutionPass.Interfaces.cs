using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class DeclarationResolutionPass
{
    private void ValidateInterfaceImplementations()
    {
        var validatedTypes = new HashSet<SymbolHandle>();

        foreach (var type in context.TypeSymbols)
        {
            ValidateTypeInterfaceImplementations(type, ResolutionContext.GetHandle(type), validatedTypes);
        }

        foreach (var type in context.NestedTypeSymbols)
        {
            ValidateTypeInterfaceImplementations(type, ResolutionContext.GetHandle(type), validatedTypes);
        }
    }

    private void ValidateTypeInterfaceImplementations(Symbol typeSymbol, SymbolHandle typeHandle, HashSet<SymbolHandle> validatedTypes)
    {
        typeHandle = context.UnwrapAlias(typeHandle);
        if (!validatedTypes.Add(typeHandle))
            return;

        if (typeSymbol.Name.Text == "_")
            return;

        TypeKind typeKind = typeSymbol switch
        {
            TypeSymbol ts => ts.TypeKind,
            NestedTypeSymbol nts => nts.TypeKind,
            _ => TypeKind.Struct
        };

        if (typeKind == TypeKind.Interface)
            return;

        // Skip intrinsics
        if (context.DiscoveredIntrinsics.TryGetIntrinsicName(typeHandle, out _))
            return;

        var implementedInterfaces = GetImplementedInterfaces(typeSymbol);
        if (implementedInterfaces.Count == 0)
            return;

        var typeSpan = (typeSymbol as TypeSymbol)?.Syntax?.Name.GetSpan() ??
                       (typeSymbol as NestedTypeSymbol)?.Syntax?.Name.GetSpan() ?? default;
        var typeSource = (typeSymbol as TypeSymbol)?.Syntax?.GetSource() ??
                         (typeSymbol as NestedTypeSymbol)?.Syntax?.GetSource();

        foreach (var (ifaceHandle, typeArgs) in implementedInterfaces)
        {
            var ifaceSym = context.GetTypeSymbol(ifaceHandle);
            if (ifaceSym is null)
                continue;

            var ifaceScope = GetOwnedScope(ifaceSym);

            // 1. Validate Methods
            foreach (var sym in ifaceScope.Symbols.Values)
            {
                if (sym is MethodSymbol ifaceMethod)
                {
                    // Skip if default implementation exists
                    if (ifaceMethod.Syntax?.Body is not null && ifaceMethod.Syntax.Body is not FunctionEmptyBody)
                        continue;

                    // Abstract method: must be implemented
                    if (!IsInterfaceMethodImplemented(typeSymbol, ifaceSym, ifaceMethod, typeArgs))
                    {
                        var ifaceMemberSpan = ifaceMethod.Syntax?.Signature.Identifier.GetSpan() ??
                                              ifaceMethod.Syntax?.GetSpan() ?? default;
                        var ifaceMemberSource = ifaceMethod.Syntax?.GetSource();

                        context.Diagnostics.ReportUnimplementedInterfaceMember(
                            typeSpan,
                            typeSymbol.Name.ToString(),
                            ifaceSym.Name.ToString(),
                            ifaceMethod.Name.ToString(),
                            typeSource,
                            ifaceMemberSpan,
                            ifaceMemberSource);
                    }
                }
                else if (sym is PropertySymbol ifaceProp)
                {
                    // 2. Validate Properties
                    bool requiresGetter = false;
                    bool requiresSetter = false;

                    if (ifaceProp.Syntax is { } propSyntax)
                    {
                        foreach (var acc in propSyntax.Body.Accessors)
                        {
                            if (acc.Kind == PropertyAccessorKind.Get)
                            {
                                if (acc.Body is FunctionEmptyBody)
                                    requiresGetter = true;
                            }
                            else if (acc.Kind == PropertyAccessorKind.Set)
                            {
                                if (acc.Body is FunctionEmptyBody)
                                    requiresSetter = true;
                            }
                        }
                    }

                    // If neither getter nor setter is abstract (all have default implementations), skip
                    if (!requiresGetter && !requiresSetter)
                        continue;

                    var (isImplemented, detail) = CheckInterfacePropertyImplemented(typeSymbol, ifaceSym, ifaceProp, typeArgs, requiresGetter, requiresSetter);
                    if (!isImplemented)
                    {
                        var ifaceMemberSpan = ifaceProp.Syntax?.Identifier.GetSpan() ??
                                              ifaceProp.Syntax?.GetSpan() ?? default;
                        var ifaceMemberSource = ifaceProp.Syntax?.GetSource();

                        context.Diagnostics.ReportUnimplementedInterfaceMember(
                            typeSpan,
                            typeSymbol.Name.ToString(),
                            ifaceSym.Name.ToString(),
                            ifaceProp.Name.ToString(),
                            typeSource,
                            ifaceMemberSpan,
                            ifaceMemberSource,
                            detail);
                    }
                }
            }
        }
    }

    private bool IsInterfaceMethodImplemented(
        Symbol typeSymbol,
        TypeSymbol ifaceSym,
        MethodSymbol ifaceMethod,
        Dictionary<SymbolHandle, TypeRef> typeArgs)
    {
        var candidateScopes = new List<Scope> { GetOwnedScope(typeSymbol) };

        IReadOnlyList<SymbolHandle> bases = typeSymbol switch
        {
            TypeSymbol ts => ts.LinearizedBases,
            NestedTypeSymbol nts => nts.LinearizedBases,
            _ => []
        };

        foreach (var bh in bases.Skip(1))
        {
            var baseSym = context.GetTypeSymbol(bh);
            if (baseSym is not null && baseSym.TypeKind != TypeKind.Interface)
            {
                candidateScopes.Add(GetOwnedScope(baseSym));
            }
        }

        bool isStatic = ifaceMethod.Flags.HasFlag(FunctionFlags.Static);
        var expectedReturn = ifaceMethod.ReturnType.Substitute(typeArgs);

        var expectedParamTypes = new List<TypeRef>(ifaceMethod.Parameters.Count);
        foreach (var ph in ifaceMethod.Parameters)
        {
            if (context.GetParameterSymbol(ph) is { } paramSym)
            {
                expectedParamTypes.Add(paramSym.Type.Substitute(typeArgs));
            }
            else
            {
                expectedParamTypes.Add(TypeRef.Unresolved);
            }
        }

        foreach (var scope in candidateScopes)
        {
            foreach (var s in scope.Symbols.Values)
            {
                if (s is not MethodSymbol method)
                    continue;

                // Match static-ness
                if (method.Flags.HasFlag(FunctionFlags.Static) != isStatic)
                    continue;

                // Match name
                bool nameMatches = false;
                if (method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                {
                    if (method.ExplicitInterfaceName is { } ein &&
                        (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text) &&
                        method.Name.Text == ifaceMethod.Name.Text)
                    {
                        nameMatches = true;
                    }
                }
                else
                {
                    if (method.Name.Text == ifaceMethod.Name.Text)
                    {
                        nameMatches = true;
                    }
                }

                if (!nameMatches)
                    continue;

                // Check receiver parameter compatibility
                bool ifaceHasReceiver = ifaceMethod.Parameters.Count > 0 &&
                                        context.GetParameterSymbol(ifaceMethod.Parameters[0])?.Syntax?.Declarator.IsReceiver == true;
                bool methodHasReceiver = method.Parameters.Count > 0 &&
                                         context.GetParameterSymbol(method.Parameters[0])?.Syntax?.Declarator.IsReceiver == true;

                if (ifaceHasReceiver != methodHasReceiver)
                    continue;

                if (ifaceHasReceiver)
                {
                    var ifaceReceiverKind = context.GetParameterSymbol(ifaceMethod.Parameters[0])?.Syntax?.Declarator.ReceiverKind;
                    var methodReceiverKind = context.GetParameterSymbol(method.Parameters[0])?.Syntax?.Declarator.ReceiverKind;
                    if (ifaceReceiverKind != methodReceiverKind)
                        continue;
                }

                // Check parameter count and generic arity
                if (method.Parameters.Count != expectedParamTypes.Count)
                    continue;

                if (method.GenericParameters.Count != ifaceMethod.GenericParameters.Count)
                    continue;

                // Check parameter types (skipping receiver parameter if present)
                int startIdx = ifaceHasReceiver ? 1 : 0;
                bool paramsMatch = true;
                for (int i = startIdx; i < expectedParamTypes.Count; i++)
                {
                    var actualParamType = context.GetParameterSymbol(method.Parameters[i])?.Type ?? TypeRef.Unresolved;
                    if (!IsTypeEquivalent(actualParamType, expectedParamTypes[i]))
                    {
                        paramsMatch = false;
                        break;
                    }
                }

                if (!paramsMatch)
                    continue;

                // Check return type
                if (!IsTypeEquivalent(method.ReturnType, expectedReturn))
                    continue;

                // Must have a body or be intrinsic
                if (method.Syntax?.Body is FunctionEmptyBody && !method.Flags.HasFlag(FunctionFlags.Intrinsic))
                    continue;

                return true;
            }
        }

        return false;
    }

    private (bool IsImplemented, string? Detail) CheckInterfacePropertyImplemented(
        Symbol typeSymbol,
        TypeSymbol ifaceSym,
        PropertySymbol ifaceProp,
        Dictionary<SymbolHandle, TypeRef> typeArgs,
        bool requiresGetter,
        bool requiresSetter)
    {
        var candidateScopes = new List<Scope> { GetOwnedScope(typeSymbol) };

        IReadOnlyList<SymbolHandle> bases = typeSymbol switch
        {
            TypeSymbol ts => ts.LinearizedBases,
            NestedTypeSymbol nts => nts.LinearizedBases,
            _ => []
        };

        foreach (var bh in bases.Skip(1))
        {
            var baseSym = context.GetTypeSymbol(bh);
            if (baseSym is not null && baseSym.TypeKind != TypeKind.Interface)
            {
                candidateScopes.Add(GetOwnedScope(baseSym));
            }
        }

        var expectedType = ifaceProp.Type.Substitute(typeArgs);
        PropertySymbol? matchedProp = null;

        foreach (var scope in candidateScopes)
        {
            foreach (var s in scope.Symbols.Values)
            {
                if (s is not PropertySymbol prop)
                    continue;

                bool nameMatches = false;
                if (prop.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                {
                    if (prop.ExplicitInterfaceName is { } ein &&
                        (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text) &&
                        prop.Name.Text == ifaceProp.Name.Text)
                    {
                        nameMatches = true;
                    }
                }
                else
                {
                    if (prop.Name.Text == ifaceProp.Name.Text)
                    {
                        nameMatches = true;
                    }
                }

                if (!nameMatches)
                    continue;

                if (!IsTypeEquivalent(prop.Type, expectedType))
                    continue;

                matchedProp = prop;
                break;
            }

            if (matchedProp is not null)
                break;
        }

        if (matchedProp is null)
            return (false, null);

        // Check required accessors
        bool hasGetter = false;
        bool hasSetter = false;

        if (matchedProp.Syntax is { } syntax)
        {
            foreach (var acc in syntax.Body.Accessors)
            {
                if (acc.Kind == PropertyAccessorKind.Get)
                    hasGetter = true;
                else if (acc.Kind == PropertyAccessorKind.Set)
                    hasSetter = true;
            }
        }

        if (requiresGetter && !hasGetter)
            return (false, "missing getter accessor");

        if (requiresSetter && !hasSetter)
            return (false, "missing setter accessor");

        return (true, null);
    }

    private List<(SymbolHandle IfaceHandle, Dictionary<SymbolHandle, TypeRef> TypeArgs)> GetImplementedInterfaces(Symbol typeSymbol)
    {
        var result = new List<(SymbolHandle, Dictionary<SymbolHandle, TypeRef>)>();
        var visited = new HashSet<SymbolHandle>();

        void CollectRecursive(TypeRef typeRef, Dictionary<SymbolHandle, TypeRef> parentTypeArgs)
        {
            if (typeRef.Handle is not { } h)
                return;

            var unwrappedH = context.UnwrapAlias(h);
            var sym = (Symbol?)context.GetTypeSymbol(unwrappedH) ?? context.GetNestedTypeSymbol(unwrappedH);
            if (sym is null || !visited.Add(unwrappedH))
                return;

            TypeKind? ifaceKind = sym switch
            {
                TypeSymbol ts => ts.TypeKind,
                NestedTypeSymbol nts => nts.TypeKind,
                _ => null
            };

            IReadOnlyList<SymbolHandle> genericParams = sym switch
            {
                TypeSymbol ts => ts.GenericParameters,
                NestedTypeSymbol nts => nts.GenericParameters,
                _ => []
            };

            var currentTypeArgs = new Dictionary<SymbolHandle, TypeRef>(parentTypeArgs);
            if (typeRef.SpecialType is GenericSemanticType gen && genericParams.Count == gen.TypeArguments.Count)
            {
                for (int i = 0; i < genericParams.Count; i++)
                {
                    var substitutedArg = gen.TypeArguments[i].Substitute(parentTypeArgs);
                    currentTypeArgs[genericParams[i]] = substitutedArg;
                }
            }

            if (ifaceKind == TypeKind.Interface)
            {
                result.Add((unwrappedH, currentTypeArgs));
            }

            IReadOnlyList<TypeRef> baseTypes = sym switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                _ => []
            };

            foreach (var bt in baseTypes)
            {
                CollectRecursive(bt, currentTypeArgs);
            }
        }

        IReadOnlyList<TypeRef> rootBases = typeSymbol switch
        {
            TypeSymbol ts => ts.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            _ => []
        };

        foreach (var rb in rootBases)
        {
            CollectRecursive(rb, []);
        }

        return result;
    }

    private bool IsTypeEquivalent(TypeRef a, TypeRef b)
    {
        var unwrapA = context.UnwrapAlias(a);
        var unwrapB = context.UnwrapAlias(b);
        return unwrapA.Equals(unwrapB);
    }
}
