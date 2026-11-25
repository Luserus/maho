using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;
using Maho.Text;

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

        ValidateExtensionConformances();
    }

    private void ValidateExtensionConformances()
    {
        foreach (var conf in context.Conformances)
        {
            if (!conf.IsExtension)
                continue;

            var extSym = context.GetSymbol(conf.OwnerHandle) as ExtensionSymbol;
            if (extSym is null || conf.TargetType.Handle is not { } targetH)
                continue;

            var unwrappedTargetH = context.UnwrapAlias(targetH);
            if (context.DiscoveredIntrinsics.TryGetIntrinsicName(unwrappedTargetH, out _))
                continue;

            var targetSym = (Symbol?)context.GetTypeSymbol(unwrappedTargetH) ?? context.GetNestedTypeSymbol(unwrappedTargetH);
            if (targetSym is null)
                continue;

            var typeSpan = extSym.Syntax?.TargetType.GetSpan() ?? extSym.Syntax?.GetSpan() ?? default;
            var typeSource = extSym.Syntax?.GetSource();

            var implemented = new List<(SymbolHandle IfaceHandle, Dictionary<SymbolHandle, TypeRef> TypeArgs)>();
            CollectInterfaceChain(conf.InterfaceType, [], [], implemented);

            foreach (var (ifaceHandle, typeArgs) in implemented)
            {
                ValidateConformanceMembers(
                    targetSym,
                    conf.OwnerHandle,
                    extSym.Scope,
                    conf,
                    ifaceHandle,
                    typeArgs,
                    extSym.Name.Text,
                    typeSpan,
                    typeSource);
            }
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
            var matchingConf = context.Conformances.FirstOrDefault(c =>
                !c.IsExtension &&
                c.OwnerHandle == typeHandle &&
                c.InterfaceHandle == ifaceHandle);

            ValidateConformanceMembers(
                typeSymbol,
                typeHandle,
                extensionScope: null,
                matchingConf,
                ifaceHandle,
                typeArgs,
                typeSymbol.Name.ToString(),
                typeSpan,
                typeSource);
        }
    }

    private void ValidateConformanceMembers(
        Symbol typeSymbol,
        SymbolHandle ownerHandle,
        Scope? extensionScope,
        InterfaceConformance? conformance,
        SymbolHandle ifaceHandle,
        Dictionary<SymbolHandle, TypeRef> typeArgs,
        string ownerName,
        TextSpan typeSpan,
        SourceText? typeSource)
    {
        var ifaceSym = context.GetTypeSymbol(ifaceHandle);
        if (ifaceSym is null)
            return;

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
                if (!IsInterfaceMethodImplemented(typeSymbol, ownerHandle, extensionScope, ifaceSym, ifaceMethod, typeArgs, out var implementingMethod))
                {
                    var ifaceMemberSpan = ifaceMethod.Syntax?.Signature.Identifier.GetSpan() ??
                                          ifaceMethod.Syntax?.GetSpan() ?? default;
                    var ifaceMemberSource = ifaceMethod.Syntax?.GetSource();

                    context.Diagnostics.ReportUnimplementedInterfaceMember(
                        typeSpan,
                        ownerName,
                        ifaceSym.Name.ToString(),
                        ifaceMethod.Name.ToString(),
                        typeSource,
                        ifaceMemberSpan,
                        ifaceMemberSource);
                }
                else if (implementingMethod is not null)
                {
                    if (conformance is not null)
                        conformance.MethodMap[ResolutionContext.GetHandle(ifaceMethod)] = ResolutionContext.GetHandle(implementingMethod);

                    if (conformance is null || !conformance.IsExtension)
                    {
                        if (HasOperatorOnInterface(ifaceHandle, OperatorKind.StaticConstructor))
                        {
                            var mHandle = ResolutionContext.GetHandle(implementingMethod);
                            if (context.StaticConstructors.Any(existing => context.GetMethodSymbol(existing)?.Parent == ownerHandle))
                            {
                                var span = implementingMethod.Syntax?.Signature.Identifier.GetSpan() ?? default;
                                context.Diagnostics.ReportDuplicateStaticConstructor(span, ownerName, implementingMethod.Syntax?.GetSource());
                            }
                            else
                            {
                                context.StaticConstructors.Add(mHandle);
                            }
                        }
                    }
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

                var (isImplemented, detail) = CheckInterfacePropertyImplemented(typeSymbol, ownerHandle, extensionScope, ifaceSym, ifaceProp, typeArgs, requiresGetter, requiresSetter, out var matchedProp);
                if (!isImplemented)
                {
                    var ifaceMemberSpan = ifaceProp.Syntax?.Identifier.GetSpan() ??
                                          ifaceProp.Syntax?.GetSpan() ?? default;
                    var ifaceMemberSource = ifaceProp.Syntax?.GetSource();

                    context.Diagnostics.ReportUnimplementedInterfaceMember(
                        typeSpan,
                        ownerName,
                        ifaceSym.Name.ToString(),
                        ifaceProp.Name.ToString(),
                        typeSource,
                        ifaceMemberSpan,
                        ifaceMemberSource,
                        detail);
                }
                else if (conformance is not null && matchedProp is not null)
                {
                    conformance.MethodMap[ResolutionContext.GetHandle(ifaceProp)] = ResolutionContext.GetHandle(matchedProp);
                }
            }
        }
    }

    private List<Scope> BuildCandidateImplementationScopes(Symbol typeSymbol, SymbolHandle ownerHandle, Scope? extensionScope, SymbolHandle ifaceHandle)
    {
        var candidateScopes = new List<Scope>();
        if (extensionScope is not null)
            candidateScopes.Add(extensionScope);

        candidateScopes.Add(GetOwnedScope(typeSymbol));

        var typeH = ResolutionContext.GetHandle(typeSymbol);
        foreach (var ext in context.ExtensionSymbols)
        {
            if (ext.TargetTypeHandle != typeH)
                continue;

            var extH = ResolutionContext.GetHandle(ext);
            if (extH == ownerHandle)
                continue;

            // Do not pull methods from extensions that declare their own separate conformance to this interface.
            bool extHasOwnConf = context.Conformances.Any(c =>
                c.IsExtension &&
                c.OwnerHandle == extH &&
                c.InterfaceHandle == ifaceHandle);
            if (!extHasOwnConf)
                candidateScopes.Add(ext.Scope);
        }

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
                candidateScopes.Add(GetOwnedScope(baseSym));
        }

        return candidateScopes;
    }

    private bool IsInterfaceMethodImplemented(
        Symbol typeSymbol,
        SymbolHandle ownerHandle,
        Scope? extensionScope,
        TypeSymbol ifaceSym,
        MethodSymbol ifaceMethod,
        Dictionary<SymbolHandle, TypeRef> typeArgs,
        out MethodSymbol? implementingMethod)
    {
        implementingMethod = null;
        var ifaceHandle = ResolutionContext.GetHandle(ifaceSym);
        var candidateScopes = BuildCandidateImplementationScopes(typeSymbol, ownerHandle, extensionScope, ifaceHandle);

        bool isStatic = ifaceMethod.Flags.HasFlag(FunctionFlags.Static);
        var expectedReturn = ifaceMethod.ReturnType.Substitute(typeArgs);

        var expectedParamTypes = new List<TypeRef>();
        foreach (var ph in ifaceMethod.Parameters)
        {
            if (context.GetParameterSymbol(ph) is { } paramSym)
            {
                var subst = paramSym.Type.Substitute(typeArgs);
                bool isVariadicParam = paramSym.Syntax?.Declarator.Type is ModifiedType m && m.Modifier is VariadicTypeModifier;
                bool isVariadicGeneric = ifaceSym.GenericParameters.Count > 0 &&
                    (context.GetSymbol(ifaceSym.GenericParameters[^1]) as GenericParameterSymbol)?.IsVariadic == true &&
                    (paramSym.Type.Handle == ifaceSym.GenericParameters[^1] ||
                     (paramSym.Syntax?.Declarator.Type is SimpleType st &&
                      context.GetSymbol(ifaceSym.GenericParameters[^1]) is GenericParameterSymbol gpSym &&
                      st.Name.Value == gpSym.Name.Text));

                if ((isVariadicParam || isVariadicGeneric) && subst.SpecialType is TupleSemanticType tup)
                {
                    foreach (var elem in tup.Elements)
                    {
                        expectedParamTypes.Add(elem.Type);
                    }
                }
                else
                {
                    expectedParamTypes.Add(subst);
                }
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

                implementingMethod = method;
                return true;
            }
        }

        return false;
    }

    private (bool IsImplemented, string? Detail) CheckInterfacePropertyImplemented(
        Symbol typeSymbol,
        SymbolHandle ownerHandle,
        Scope? extensionScope,
        TypeSymbol ifaceSym,
        PropertySymbol ifaceProp,
        Dictionary<SymbolHandle, TypeRef> typeArgs,
        bool requiresGetter,
        bool requiresSetter,
        out PropertySymbol? matchedProp)
    {
        var ifaceHandle = ResolutionContext.GetHandle(ifaceSym);
        var candidateScopes = BuildCandidateImplementationScopes(typeSymbol, ownerHandle, extensionScope, ifaceHandle);

        var expectedType = ifaceProp.Type.Substitute(typeArgs);
        matchedProp = null;

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

        IReadOnlyList<TypeRef> rootBases = typeSymbol switch
        {
            TypeSymbol ts => ts.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            _ => []
        };

        foreach (var rb in rootBases)
            CollectInterfaceChain(rb, [], visited, result);

        return result;
    }

    private void CollectInterfaceChain(
        TypeRef typeRef,
        Dictionary<SymbolHandle, TypeRef> parentTypeArgs,
        HashSet<SymbolHandle> visited,
        List<(SymbolHandle IfaceHandle, Dictionary<SymbolHandle, TypeRef> TypeArgs)> result)
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
        if (typeRef.SpecialType is GenericSemanticType gen)
        {
            bool hasVariadic = genericParams.Count > 0 &&
                (context.GetSymbol(genericParams[^1]) as GenericParameterSymbol)?.IsVariadic == true;

            if (genericParams.Count == gen.TypeArguments.Count)
            {
                for (int i = 0; i < genericParams.Count; i++)
                    currentTypeArgs[genericParams[i]] = gen.TypeArguments[i].Substitute(parentTypeArgs);
            }
            else if (hasVariadic && gen.TypeArguments.Count >= genericParams.Count - 1)
            {
                int fixedCount = genericParams.Count - 1;
                for (int i = 0; i < fixedCount; i++)
                    currentTypeArgs[genericParams[i]] = gen.TypeArguments[i].Substitute(parentTypeArgs);
                var variadicArgs = gen.TypeArguments.Skip(fixedCount).Select(a => a.Substitute(parentTypeArgs)).ToList();
                context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var tupH);
                currentTypeArgs[genericParams[^1]] = TypeRef.Tuple(variadicArgs.Select(a => new TupleElement(a, null)).ToList(), tupH != default ? tupH : null);
            }
        }

        if (ifaceKind == TypeKind.Interface)
            result.Add((unwrappedH, currentTypeArgs));

        IReadOnlyList<TypeRef> baseTypes = sym switch
        {
            TypeSymbol ts => ts.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            _ => []
        };

        foreach (var bt in baseTypes)
            CollectInterfaceChain(bt, currentTypeArgs, visited, result);
    }

    private bool IsTypeEquivalent(TypeRef a, TypeRef b)
    {
        var unwrapA = context.UnwrapAlias(a);
        var unwrapB = context.UnwrapAlias(b);
        return unwrapA.Equals(unwrapB);
    }
}
