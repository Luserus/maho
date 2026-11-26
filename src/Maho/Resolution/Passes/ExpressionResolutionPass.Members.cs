using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class ExpressionResolutionPass
{
    /// <summary>
    /// Resolves a member access expression (<c>receiver.member</c>).
    /// <para>
    /// Handles static vs instance member access, sum type safety rules (preventing ordinary instance method
    /// invocations on untagged sum types <c>MH0608</c> and variant-level method calls on sum types <c>MH0609</c>),
    /// and hides interface-scoped operator methods from standard instance access.
    /// Emits <c>MH2008</c> if the member is not found.
    /// </para>
    /// </summary>
    /// <param name="access">The member access expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The resolved type of the member, or <see cref="TypeRef.Error"/>/<see cref="TypeRef.Unresolved"/> on failure.</returns>
    private TypeRef ResolveMemberAccess(MemberAccessExpression access, Scope scope, SymbolHandle containingFunction)
    {
        if (context.ResolvedTree.TryGetReference(access, out var existingRef))
        {
            var existingSym = context.GetSymbol(existingRef);
            if (existingSym is not null)
                return GetTypeFromSymbol(existingSym);
        }

        var receiverType = ResolveExpression(access.Expression, scope, containingFunction);
        var memberName = access.Identifier.Value;

        // Module-qualified namespace chain: ModA::Ns.Sub.Symbol or global::Ns.Symbol
        if (scopedNamespaceChains.TryGetValue(access.Expression, out var nsMatches))
        {
            var part = new SymbolPart(access.Identifier);
            var matchedModules = new List<(ResolutionContext Mod, Symbol Sym)>();
            var deeperNamespaces = new List<(ResolutionContext Mod, NamespaceTrieNode Node)>();

            foreach (var (mod, nsNode) in nsMatches)
            {
                bool foundInThisMod = false;
                foreach (var s in nsNode.GetSymbols(part))
                {
                    if (s.ProjectId == mod.ProjectId)
                    {
                        matchedModules.Add((mod, s));
                        foundInThisMod = true;
                        break;
                    }
                }

                if (!foundInThisMod && nsNode.Next.TryGetValue(part, out var nextNs) &&
                    ResolutionContext.HasOwnedSymbolsInSubtree(nextNs, mod.ProjectId))
                {
                    deeperNamespaces.Add((mod, nextNs));
                }
            }

            if (matchedModules.Count > 1)
            {
                context.Diagnostics.ReportAmbiguousModuleMember(
                    access.Identifier.Span,
                    access.Expression is ScopeResolutionExpression sr ? sr.Qualifier.Value : memberName,
                    memberName,
                    access.GetSource());
                return TypeRef.Error;
            }

            if (matchedModules.Count == 1)
            {
                var sym = matchedModules[0].Sym;
                var h = ResolutionContext.GetHandle(sym);
                context.ResolvedTree.AddReference(access, h);
                if (sym is ConstantSymbol c && c.Value.Kind != ConstantValueKind.None)
                    context.SetConstantValue(access, c.Value);
                return GetTypeFromSymbol(sym);
            }

            if (deeperNamespaces.Count > 0)
            {
                scopedNamespaceChains[access] = deeperNamespaces;
                return TypeRef.Unresolved;
            }

            context.Diagnostics.ReportMemberNotFound(
                access.Identifier.Span,
                memberName,
                nsMatches[0].Node.Name.Text,
                source: access.GetSource());
            return TypeRef.Error;
        }

        // If receiver is an uncollapsed literal, defer resolution until collapsed
        if (receiverType.IsLiteral)
        {
            return receiverType;
        }

        bool isArrowAccess = access.Dot.Kind == TokenKind.MinusGreaterThan;
        TypeRef originalReceiverType = receiverType;

        if (isArrowAccess)
        {
            if (TryResolveIndirectMemberAccessOperator(receiverType, out var opReturnType))
            {
                if (access.Identifier.Kind is TokenKind.MinusGreaterThan)
                {
                    return opReturnType;
                }

                while (opReturnType.SpecialType is AliasSemanticType or DynSemanticType)
                    opReturnType = opReturnType.SpecialType is AliasSemanticType al ? al.TargetType : ((DynSemanticType)opReturnType.SpecialType!).TargetType;
                receiverType = opReturnType;
            }
            else
            {
                context.Diagnostics.ReportMemberNotFound(
                    access.Dot.Span,
                    "->",
                    receiverType.ToQualifiedString(context),
                    labelTypeName: receiverType.ToLabelString(context, scope),
                    source: access.GetSource());
                return TypeRef.Error;
            }
        }
        else
        {
            while (receiverType.SpecialType is AliasSemanticType or DynSemanticType)
            {
                receiverType = receiverType.SpecialType is AliasSemanticType alias
                    ? alias.TargetType
                    : ((DynSemanticType)receiverType.SpecialType!).TargetType;
            }
        }

        // Check if access.Expression is a TypeSymbol (static access / enum variant)
        bool isStaticAccessOnType = access.Expression is TypeAsExpression;
        if (!isStaticAccessOnType)
        {
            if (access.Expression is NamedExpression namedExpr && namedExpr.Identifier.MatchingKind is not MatchingKeywordKind.This)
            {
                if (context.ResolvedTree.TryGetReference(namedExpr, out var namedH) && context.GetSymbol(namedH) is TypeSymbol or NestedTypeSymbol)
                    isStaticAccessOnType = true;
                else if (receiverType.Kind == TypeRefKind.Resolved && receiverType.Handle is { } rh && context.GetTypeSymbol(rh) is { } ts && namedExpr.Identifier.Value == ts.Name.Text)
                    isStaticAccessOnType = true;
            }
            else if (access.Expression is ScopeResolutionExpression or MemberAccessExpression &&
                     context.ResolvedTree.TryGetReference(access.Expression, out var exprH) &&
                     context.GetSymbol(exprH) is TypeSymbol or NestedTypeSymbol)
            {
                isStaticAccessOnType = true;
            }
        }

        // Untagged union and sum type member safety checks (instance members only!)
        if (!isStaticAccessOnType)
        {
            var unwrapped = receiverType;
            bool isUntagged = unwrapped.IsUntagged;
            ISumTypeSymbol? sumType = null;
            UnionSemanticType? unionSemantic = unwrapped.SpecialType as UnionSemanticType;

            if (unwrapped.Kind == TypeRefKind.Untagged && unwrapped.SpecialType is UntaggedSemanticType untagged)
                unwrapped = untagged.InnerType;

            if (unwrapped.Handle is { } handle && context.GetTypeSymbol(handle) is ISumTypeSymbol s)
                sumType = s;

            if (sumType is not null || unionSemantic is not null)
            {
                MethodSymbol? declaredMethod = null;
                if (sumType is not null)
                {
                    foreach (var methodHandle in sumType.Methods)
                    {
                        if (context.GetMethodSymbol(methodHandle) is { } m && m.Name.Text == memberName)
                        {
                            declaredMethod = m;
                            break;
                        }
                    }
                }

                if (declaredMethod is not null)
                {
                    if (declaredMethod.Flags.HasFlag(FunctionFlags.Static))
                    {
                        context.Diagnostics.ReportCannotAccessStaticMemberOnInstance(
                            access.Identifier.Span,
                            memberName,
                            (sumType as Symbol)?.Name.Text ?? receiverType.ToDisplayString(context),
                            access.GetSource());
                        return TypeRef.Error;
                    }

                    // MH0608: Calling ordinary instance method on untagged sum type without @untagged flag
                    if (isUntagged && !declaredMethod.Flags.HasFlag(FunctionFlags.Untagged))
                    {
                        context.Diagnostics.ReportUntaggedMethodMismatch(
                            access.Identifier.Span,
                            memberName,
                            receiverType.ToDisplayString(context),
                            access.GetSource());
                    }

                    context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(declaredMethod));
                    return GetTypeFromSymbol(declaredMethod);
                }

                // MH0609: Calling variant-level method directly on sum type instance
                bool foundOnVariant = false;
                if (sumType is not null)
                {
                    if (sumType.UnionConstituents.Count > 0)
                    {
                        foreach (var constituent in sumType.UnionConstituents)
                        {
                            if (constituent.Handle is { } ch && context.GetTypeSymbol(ch) is { } cTypeSym)
                            {
                                var cScope = GetOwnedScope(cTypeSym);
                                if (cScope[new SymbolName(new SymbolPart(memberName))].Count > 0)
                                {
                                    foundOnVariant = true;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (var vHandle in sumType.EnumVariants)
                        {
                            if (context.GetEnumVariantSymbol(vHandle) is { } vSym)
                            {
                                var vScope = GetOwnedScope(vSym);
                                if (vScope[new SymbolName(new SymbolPart(memberName))].Count > 0)
                                {
                                    foundOnVariant = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                else if (unionSemantic is not null)
                {
                    foreach (var constituent in unionSemantic.Variants)
                    {
                        if (constituent.Handle is { } ch && context.GetTypeSymbol(ch) is { } cTypeSym)
                        {
                            var cScope = GetOwnedScope(cTypeSym);
                            if (cScope[new SymbolName(new SymbolPart(memberName))].Count > 0)
                            {
                                foundOnVariant = true;
                                break;
                            }
                        }
                    }
                }

                if (foundOnVariant)
                {
                    context.Diagnostics.ReportDirectVariantMethodInvocation(
                        access.Identifier.Span,
                        memberName,
                        receiverType.ToDisplayString(context),
                        access.GetSource());
                }
            }
        }

        if (TypeRef.TryGetExplicitConformance(receiverType, out var explicitConf))
        {
            int memberGenericArity = access.GenericArguments?.Count ?? 0;
            if (explicitConf.InterfaceType.Handle is { } confIfaceH)
            {
                var (ifaceMember, ifaceAmbiguous, _) = FindMemberInTypeHierarchy(confIfaceH, memberName, isExplicitInterfaceAccess: true, genericArity: memberGenericArity);
                if (!ifaceAmbiguous && ifaceMember is not null)
                {
                    var ifaceMemberHandle = ResolutionContext.GetHandle(ifaceMember);
                    Symbol targetMember = ifaceMember;
                    if (explicitConf.MethodMap.TryGetValue(ifaceMemberHandle, out var mappedImplH) &&
                        context.GetSymbol(mappedImplH) is { } mappedImpl)
                    {
                        targetMember = mappedImpl;
                    }

                    context.ResolvedTree.AddConformance(access, explicitConf);
                    context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(targetMember));
                    var retType = GetTypeFromSymbol(targetMember);
                    if (explicitConf.InterfaceType.SpecialType is GenericSemanticType ifaceGst &&
                        context.GetSymbol(confIfaceH) is TypeSymbol ifaceTs &&
                        ifaceTs.GenericParameters.Count == ifaceGst.TypeArguments.Count)
                    {
                        Dictionary<SymbolHandle, TypeRef> subst = [];
                        for (int i = 0; i < ifaceTs.GenericParameters.Count; i++)
                            subst[ifaceTs.GenericParameters[i]] = ifaceGst.TypeArguments[i];
                        retType = SubstituteTypeRef(retType, subst);
                    }
                    if (access.GenericArguments is { Count: > 0 } expGenArgs)
                    {
                        var resolvedArgs = new List<TypeRef>(expGenArgs.Count);
                        foreach (var arg in expGenArgs)
                            resolvedArgs.Add(ResolveTypeSyntax(arg, scope));
                        retType = ValidateAndInstantiateGenericSymbol(
                            ResolutionContext.GetHandle(targetMember),
                            targetMember,
                            [.. expGenArgs],
                            resolvedArgs,
                            retType,
                            scope,
                            containingFunction);
                    }
                    return retType;
                }
            }

            receiverType = TypeRef.UnwrapConformance(receiverType);
        }

        // Look up member on receiver type (filtering out InterfaceScoped methods)
        SymbolHandle? recvHandle = receiverType.Handle;
        if (recvHandle is null)
        {
            if (receiverType.SpecialType is ReferenceSemanticType)
            {
                if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Reference, out var refH))
                    recvHandle = refH;
            }
            else if (receiverType.SpecialType is PointerSemanticType)
            {
                if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Pointer, out var ptrH))
                    recvHandle = ptrH;
            }
            else if (receiverType.SpecialType is FunctionSemanticType)
            {
                if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Function, out var fnH))
                    recvHandle = fnH;
                else if (context.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null)
                    context.Diagnostics.ReportMissingIntrinsicForSyntax(access.Identifier.Span, "function member access", KnownIntrinsics.Function, access.GetSource());
            }
            else if (receiverType.SpecialType is DelegateSemanticType)
            {
                if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Delegate, out var delH))
                    recvHandle = delH;
                else if (context.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null)
                    context.Diagnostics.ReportMissingIntrinsicForSyntax(access.Identifier.Span, "delegate member access", KnownIntrinsics.Delegate, access.GetSource());
            }
        }

        if (recvHandle is not null)
        {
            bool isExplicitInterfaceAccess = access.Expression is TypeAsExpression;
            int genericArity = access.GenericArguments?.Count ?? 0;
            var (member, isAmbiguous, _) = FindMemberInTypeHierarchy(recvHandle.Value, memberName, isExplicitInterfaceAccess, genericArity);

            if (isAmbiguous)
            {
                var typeSymObj = (Symbol?)context.GetTypeSymbol(recvHandle.Value) ?? context.GetSymbol(recvHandle.Value);
                context.Diagnostics.ReportAmbiguousMemberAccess(
                    access.Identifier.Span,
                    memberName,
                    typeSymObj?.Name.Text ?? receiverType.ToString(),
                    access.GetSource());
                return TypeRef.Error;
            }

            if (member is not null)
            {
                if (member is MethodSymbol method)
                {
                    if (originalReceiverType.SpecialType is DynSemanticType dynRecv &&
                        method.GenericParameters.Any(gpH => context.GetGenericParameterSymbol(gpH)?.ParameterKind == GenericParameterKind.This))
                    {
                        var ifaceSym = dynRecv.TargetType.Handle is { } ih ? context.GetSymbol(context.UnwrapAlias(ih)) : null;
                        context.Diagnostics.ReportDynThisGenericMethodCallNotAllowed(
                            access.Identifier.Span,
                            memberName,
                            ifaceSym?.Name.Text ?? dynRecv.ToDisplayString(context),
                            access.GetSource());
                        return TypeRef.Error;
                    }

                    bool isMethodStatic = method.Flags.HasFlag(FunctionFlags.Static);
                    if (!ValidateStaticAccess(isMethodStatic))
                        return TypeRef.Error;
                }
                else if (member is FieldSymbol field)
                {
                    if (!ValidateStaticAccess(field.Flags.HasFlag(VariableFlags.Static)))
                        return TypeRef.Error;
                }
                else if (member is PropertySymbol prop)
                {
                    if (!ValidateStaticAccess(prop.Flags.HasFlag(FunctionFlags.Static)))
                        return TypeRef.Error;
                }

                bool ValidateStaticAccess(bool isMemberStatic)
                {
                    if (isStaticAccessOnType && !isMemberStatic)
                    {
                        var typeSymObj = (Symbol?)context.GetTypeSymbol(recvHandle.Value) ?? context.GetSymbol(recvHandle.Value);
                        context.Diagnostics.ReportCannotAccessInstanceMemberOnType(
                            access.Identifier.Span,
                            memberName,
                            typeSymObj?.Name.Text ?? receiverType.ToString(),
                            access.GetSource());
                        return false;
                    }
                    if (!isStaticAccessOnType && isMemberStatic)
                    {
                        var typeSymObj = (Symbol?)context.GetTypeSymbol(recvHandle.Value) ?? context.GetSymbol(recvHandle.Value);
                        context.Diagnostics.ReportCannotAccessStaticMemberOnInstance(
                            access.Identifier.Span,
                            memberName,
                            typeSymObj?.Name.Text ?? receiverType.ToString(),
                            access.GetSource());
                        return false;
                    }
                    return true;
                }

                context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(member));
                var memberResultType = GetTypeFromSymbol(member);
                if (receiverType.SpecialType is GenericSemanticType recvGst &&
                    context.GetSymbol(recvHandle.Value) is TypeSymbol recvTs &&
                    recvTs.GenericParameters.Count == recvGst.TypeArguments.Count)
                {
                    Dictionary<SymbolHandle, TypeRef> subst = [];
                    for (int i = 0; i < recvTs.GenericParameters.Count; i++)
                        subst[recvTs.GenericParameters[i]] = recvGst.TypeArguments[i];
                    memberResultType = SubstituteTypeRef(memberResultType, subst);
                }
                else if (context.GetGenericParameterSymbol(recvHandle.Value) is { } gpRecv)
                {
                    var subst = BuildConstraintMemberSubstitutions(gpRecv);
                    if (subst.Count > 0)
                        memberResultType = SubstituteTypeRef(memberResultType, subst);
                }
                if (access.GenericArguments is { Count: > 0 } memGenArgs)
                {
                    var resolvedArgs = new List<TypeRef>(memGenArgs.Count);
                    foreach (var arg in memGenArgs)
                        resolvedArgs.Add(ResolveTypeSyntax(arg, scope));
                    memberResultType = ValidateAndInstantiateGenericSymbol(
                        ResolutionContext.GetHandle(member),
                        member,
                        [.. memGenArgs],
                        resolvedArgs,
                        memberResultType,
                        scope,
                        containingFunction);
                }
                return memberResultType;
            }

            if (!isStaticAccessOnType)
            {
                var ownership = new Maho.Resolution.Ownership.OwnershipTypeClassifier(context);
                if (ownership.IsOwningIndirection(receiverType, out var pointeeType) && pointeeType.Handle is { } pointeeH)
                {
                    var (pointeeMember, pointeeAmbiguous, _) = FindMemberInTypeHierarchy(pointeeH, memberName, isExplicitInterfaceAccess: false);
                    if (pointeeAmbiguous)
                    {
                        var typeSymObj = (Symbol?)context.GetTypeSymbol(pointeeH) ?? context.GetSymbol(pointeeH);
                        context.Diagnostics.ReportAmbiguousMemberAccess(
                            access.Identifier.Span,
                            memberName,
                            typeSymObj?.Name.Text ?? pointeeType.ToString(),
                            access.GetSource());
                        return TypeRef.Error;
                    }

                    if (pointeeMember is not null)
                    {
                        if (pointeeMember is MethodSymbol pMethod && pMethod.Flags.HasFlag(FunctionFlags.Static))
                        {
                            var typeSymObj = (Symbol?)context.GetTypeSymbol(pointeeH) ?? context.GetSymbol(pointeeH);
                            context.Diagnostics.ReportCannotAccessStaticMemberOnInstance(
                                access.Identifier.Span,
                                memberName,
                                typeSymObj?.Name.Text ?? pointeeType.ToString(),
                                access.GetSource());
                            return TypeRef.Error;
                        }

                        context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(pointeeMember));
                        var pType = GetTypeFromSymbol(pointeeMember);
                        if (pointeeType.SpecialType is GenericSemanticType pGst &&
                            context.GetSymbol(pointeeH) is TypeSymbol pTs &&
                            pTs.GenericParameters.Count == pGst.TypeArguments.Count)
                        {
                            Dictionary<SymbolHandle, TypeRef> subst = [];
                            for (int i = 0; i < pTs.GenericParameters.Count; i++)
                                subst[pTs.GenericParameters[i]] = pGst.TypeArguments[i];
                            pType = SubstituteTypeRef(pType, subst);
                        }
                        return pType;
                    }
                }

                var extResult = TryResolveExtensionMember(access, receiverType, memberName, isStaticAccessOnType, scope);
                if (extResult is not null)
                    return extResult.Value;

                context.Diagnostics.ReportMemberNotFound(
                    access.Identifier.Span,
                    memberName,
                    originalReceiverType.ToQualifiedString(context),
                    labelTypeName: originalReceiverType.ToLabelString(context, scope),
                    source: access.GetSource());
                return TypeRef.Error;
            }
            else
            {
                var extResult = TryResolveExtensionMember(access, receiverType, memberName, isStaticAccessOnType, scope);
                if (extResult is not null)
                    return extResult.Value;

                context.Diagnostics.ReportMemberNotFound(
                    access.Identifier.Span,
                    memberName,
                    originalReceiverType.ToQualifiedString(context),
                    labelTypeName: originalReceiverType.ToLabelString(context, scope),
                    source: access.GetSource());
                return TypeRef.Error;
            }
        }

        var extFallback = TryResolveExtensionMember(access, receiverType, memberName, isStaticAccessOnType, scope);
        if (extFallback is not null)
            return extFallback.Value;

        return TypeRef.Unresolved;
    }

    private TypeRef? TryResolveExtensionMember(
        MemberAccessExpression access,
        TypeRef receiverType,
        string memberName,
        bool isStaticAccessOnType,
        Scope scope)
    {
        List<(ExtensionSymbol Extension, Symbol Member, Dictionary<SymbolHandle, TypeRef> TypeArgs)> matching = [];

        foreach (var ext in context.ExtensionSymbols)
        {
            if (ext.Accessibility == Accessibility.Internal && !context.IsDeclaredInCurrentProject(ResolutionContext.GetHandle(ext)))
                continue;

            Dictionary<SymbolHandle, TypeRef> typeArgs = [];
            if (!TryUnifyExtensionTarget(ext.TargetTypeRef, receiverType, ext.GenericParameters, typeArgs))
                continue;

            Symbol? candidate = null;
            foreach (var mH in ext.Methods)
            {
                if (context.GetMethodSymbol(mH) is { } m && m.Name.Text == memberName)
                {
                    if (m.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                        continue;

                    bool hasReceiver = m.Parameters.Count > 0 &&
                        context.GetParameterSymbol(m.Parameters[0])?.Syntax?.Declarator.ReceiverKind is not null &&
                        context.GetParameterSymbol(m.Parameters[0])!.Syntax!.Declarator.ReceiverKind != ReceiverKind.None &&
                        !m.Flags.HasFlag(FunctionFlags.Static);

                    if (isStaticAccessOnType && !hasReceiver)
                    {
                        candidate = m;
                        break;
                    }
                    else if (!isStaticAccessOnType && hasReceiver)
                    {
                        candidate = m;
                        break;
                    }
                }
            }

            if (candidate is null)
            {
                foreach (var pH in ext.Properties)
                {
                    if (context.GetPropertySymbol(pH) is { } p && p.Name.Text == memberName)
                    {
                        if (p.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                            continue;

                        bool isPropStatic = p.Flags.HasFlag(FunctionFlags.Static);
                        if (isStaticAccessOnType == isPropStatic)
                        {
                            candidate = p;
                            break;
                        }
                    }
                }
            }

            if (candidate is not null)
            {
                matching.Add((ext, candidate, typeArgs));
            }
        }

        if (matching.Count > 1)
        {
            context.Diagnostics.ReportAmbiguousExtensionMethod(access.Identifier.Span, memberName, access.GetSource());
            return TypeRef.Error;
        }

        if (matching.Count == 1)
        {
            var (matchedExt, matchedMember, matchedArgs) = matching[0];
            context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(matchedMember));
            var memberType = GetTypeFromSymbol(matchedMember);
            if (matchedArgs.Count > 0)
                memberType = SubstituteTypeRef(memberType, matchedArgs);
            return memberType;
        }

        return null;
    }

    private bool TryUnifyExtensionTarget(
        TypeRef targetPattern,
        TypeRef actualType,
        IReadOnlyList<SymbolHandle> genericParams,
        Dictionary<SymbolHandle, TypeRef> typeArgs)
    {
        if (targetPattern.Handle is { } th && genericParams.Contains(th))
        {
            if (typeArgs.TryGetValue(th, out var existing))
                return existing.Equals(actualType);
            typeArgs[th] = actualType;
            return true;
        }

        if (targetPattern.SpecialType is GenericSemanticType targetGst &&
            actualType.SpecialType is GenericSemanticType actualGst)
        {
            if (targetPattern.Handle != actualType.Handle)
                return false;
            if (targetGst.TypeArguments.Count != actualGst.TypeArguments.Count)
                return false;

            for (int i = 0; i < targetGst.TypeArguments.Count; i++)
            {
                if (!TryUnifyExtensionTarget(targetGst.TypeArguments[i], actualGst.TypeArguments[i], genericParams, typeArgs))
                    return false;
            }

            foreach (var gpH in genericParams)
            {
                if (typeArgs.TryGetValue(gpH, out var boundArg))
                {
                    if (context.GetGenericParameterSymbol(gpH) is { } gpSym)
                    {
                        foreach (var constraint in gpSym.Constraints)
                        {
                            if (constraint.IsResolved && constraint.Handle is { } cH)
                            {
                                if (boundArg.Handle is not { } bH || !DeclarationResolutionPass.SatisfiesConstraint(context, bH, cH, []))
                                    return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        if (targetPattern.Handle is { } targetH && actualType.Handle is { } actualH)
        {
            if (targetH == actualH)
            {
                foreach (var gpH in genericParams)
                {
                    if (typeArgs.TryGetValue(gpH, out var boundArg))
                    {
                        if (context.GetGenericParameterSymbol(gpH) is { } gpSym)
                        {
                            foreach (var constraint in gpSym.Constraints)
                            {
                                if (constraint.IsResolved && constraint.Handle is { } cH)
                                {
                                    if (boundArg.Handle is not { } bH || !DeclarationResolutionPass.SatisfiesConstraint(context, bH, cH, []))
                                        return false;
                                }
                            }
                        }
                    }
                }
                return true;
            }
            return false;
        }

        return targetPattern.Equals(actualType);
    }

    private bool TryResolveIndirectMemberAccessOperator(TypeRef type, out TypeRef elemType)
    {
        elemType = TypeRef.Unresolved;
        type = TypeRef.UnwrapDyn(type);
        var handle = type.Handle;
        if (handle is null)
        {
            if (type.SpecialType is ReferenceSemanticType rSem)
            {
                string req = rSem.IsMutable ? KnownIntrinsics.MutableReference : KnownIntrinsics.Reference;
                if (context.DiscoveredIntrinsics.TryGet(req, out var refH))
                    handle = refH;
            }
            else if (type.SpecialType is PointerSemanticType pSem)
            {
                string req = pSem.IsMutable ? KnownIntrinsics.MutablePointer : KnownIntrinsics.Pointer;
                if (context.DiscoveredIntrinsics.TryGet(req, out var ptrH))
                    handle = ptrH;
            }
        }

        if (handle is null)
            return false;

        var operandTypeSym = (Symbol?)context.GetTypeSymbol(handle.Value) ?? context.GetSymbol(handle.Value);
        if (operandTypeSym is null)
            return false;

        Dictionary<SymbolHandle, TypeRef>? typeArgs = null;
        if (type.SpecialType is ReferenceSemanticType refSem && operandTypeSym is TypeSymbol tsRef && tsRef.GenericParameters.Count == 1)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef> { [tsRef.GenericParameters[0]] = refSem.ElementType };
        }
        else if (type.SpecialType is PointerSemanticType ptrSem && operandTypeSym is TypeSymbol tsPtr && tsPtr.GenericParameters.Count == 1)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef> { [tsPtr.GenericParameters[0]] = ptrSem.ElementType };
        }
        else if (type.SpecialType is GenericSemanticType gst && operandTypeSym is TypeSymbol tsGen && tsGen.GenericParameters.Count == gst.TypeArguments.Count)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef>();
            for (int i = 0; i < tsGen.GenericParameters.Count; i++)
                typeArgs[tsGen.GenericParameters[i]] = gst.TypeArguments[i];
        }

        if (context.DiscoveredOperators.TryGetOperators(OperatorKind.IndirectMemberAccess, out var candidates))
        {
            var typeScope = GetOwnedScope(operandTypeSym);
            IReadOnlyList<TypeRef> baseTypes = operandTypeSym switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                _ => []
            };

            foreach (var cand in candidates)
            {
                var ifaceSym = context.GetTypeSymbol(cand.InterfaceHandle);
                if (ifaceSym is null)
                    continue;

                // 1. Search method on operandTypeSym
                foreach (var sym in typeScope.Symbols.Values)
                {
                    if (sym is MethodSymbol method)
                    {
                        bool isCandidate = false;
                        if (method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                        {
                            if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                                isCandidate = true;
                        }
                        else if (baseTypes.Any(b => b.Handle == cand.InterfaceHandle))
                        {
                            if (context.GetMethodSymbol(cand.MethodHandle) is { } opMethod &&
                                method.Name.Text == opMethod.Name.Text)
                            {
                                isCandidate = true;
                            }
                        }

                        if (isCandidate)
                        {
                            if (method.Parameters.Count > 0 && context.GetParameterSymbol(method.Parameters[0]) is { } p && p.Syntax?.Declarator.IsReceiver == true)
                            {
                                bool requiresMut = p.Syntax.Declarator.ReceiverKind is ReceiverKind.MutAlias or ReceiverKind.MutReference or ReceiverKind.MutPointer or ReceiverKind.MutUnsafeAlias;
                                if (requiresMut)
                                {
                                    bool isOperandMut = false;
                                    if (type.SpecialType is ReferenceSemanticType rSem)
                                        isOperandMut = rSem.IsMutable;
                                    else if (type.SpecialType is PointerSemanticType pSem)
                                        isOperandMut = pSem.IsMutable;
                                    else if (type.SpecialType is AliasSemanticType aSem)
                                        isOperandMut = aSem.IsMutable;
                                    else if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.MutablePointer, out var mpH) && type.Handle == mpH)
                                        isOperandMut = true;
                                    else if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.MutableReference, out var mrH) && type.Handle == mrH)
                                        isOperandMut = true;

                                    if (!isOperandMut)
                                        continue;
                                }
                            }

                            var rawRet = method.ReturnType;
                            elemType = typeArgs is not null ? SubstituteTypeRef(rawRet, typeArgs) : rawRet;
                            return true;
                        }
                    }
                }

                // 2. If implemented via interface / inheritance, resolve using cand.MethodHandle and map interface type arguments
                if (TypeRef.IsSubtypeOf(handle.Value, cand.InterfaceHandle, context))
                {
                    if (context.GetMethodSymbol(cand.MethodHandle) is { } m)
                    {
                        Dictionary<SymbolHandle, TypeRef>? candTypeArgs = typeArgs;
                        if (ifaceSym.GenericParameters.Count > 0)
                        {
                            candTypeArgs = new Dictionary<SymbolHandle, TypeRef>();
                            var matchingBase = baseTypes.FirstOrDefault(b => b.Handle == cand.InterfaceHandle);
                            if (matchingBase.SpecialType is GenericSemanticType bGen && bGen.TypeArguments.Count == ifaceSym.GenericParameters.Count)
                            {
                                for (int i = 0; i < ifaceSym.GenericParameters.Count; i++)
                                {
                                    var substituted = typeArgs is not null ? SubstituteTypeRef(bGen.TypeArguments[i], typeArgs) : bGen.TypeArguments[i];
                                    candTypeArgs[ifaceSym.GenericParameters[i]] = substituted;
                                }
                            }
                            else if (typeArgs is not null)
                            {
                                candTypeArgs = typeArgs;
                            }
                        }

                        var rawRet = m.ReturnType;
                        elemType = candTypeArgs is not null ? SubstituteTypeRef(rawRet, candTypeArgs) : rawRet;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private (Symbol? Member, bool IsAmbiguous, List<Symbol>? Ambiguities) FindMemberInTypeHierarchy(
        SymbolHandle typeHandle,
        string memberName,
        bool isExplicitInterfaceAccess,
        int genericArity = 0)
    {
        typeHandle = context.UnwrapAlias(typeHandle);
        var typeSymObj = (Symbol?)context.GetTypeSymbol(typeHandle) ?? context.GetSymbol(typeHandle);
        if (typeSymObj is null)
            return (null, false, null);

        var recvScope = GetOwnedScope(typeSymObj);
        var directSyms = recvScope.GetLocalSymbols(new SymbolPart(memberName, genericArity));
        if (directSyms.Count == 0 && genericArity > 0)
            directSyms = recvScope.GetLocalSymbols(new SymbolPart(memberName));
        var directAccessible = directSyms.Where(cand =>
        {
            if (cand is MethodSymbol m)
            {
                if (isExplicitInterfaceAccess)
                    return true;
                return !m.Flags.HasFlag(FunctionFlags.InterfaceScoped);
            }
            if (cand is PropertySymbol p)
            {
                if (isExplicitInterfaceAccess)
                    return true;
                return !p.Flags.HasFlag(FunctionFlags.InterfaceScoped);
            }
            return !isExplicitInterfaceAccess;
        }).ToList();

        if (directAccessible.Count > 0)
            return (directAccessible[0], false, null);

        IReadOnlyList<SymbolHandle> bases = typeSymObj switch
        {
            TypeSymbol ts => ts.LinearizedBases,
            NestedTypeSymbol nts => nts.LinearizedBases,
            _ => []
        };

        if (bases.Count <= 1)
        {
            IReadOnlyList<TypeRef> fallbackBases = typeSymObj switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                _ => []
            };

            if (fallbackBases.Count > 0)
            {
                var fbList = new List<SymbolHandle> { typeHandle };
                foreach (var fb in fallbackBases)
                {
                    if (fb.Handle is { } fbh)
                        fbList.Add(context.UnwrapAlias(fbh));
                }
                bases = fbList;
            }
        }

        if (bases.Count <= 1 && typeSymObj is GenericParameterSymbol gpSym)
        {
            var gpBases = new List<SymbolHandle> { typeHandle };
            void AddInterfaceAndLinearBases(SymbolHandle ifaceH)
            {
                var unwrapped = context.UnwrapAlias(ifaceH);
                if (!gpBases.Contains(unwrapped))
                    gpBases.Add(unwrapped);
                IReadOnlyList<SymbolHandle> lin = context.GetSymbol(unwrapped) switch
                {
                    TypeSymbol ts => ts.LinearizedBases,
                    NestedTypeSymbol nts => nts.LinearizedBases,
                    _ => []
                };
                foreach (var lb in lin)
                {
                    var uLb = context.UnwrapAlias(lb);
                    if (!gpBases.Contains(uLb))
                        gpBases.Add(uLb);
                }
            }

            if (gpSym.ParameterKind == GenericParameterKind.This &&
                gpSym.GenericSymbol is TypeSymbol { TypeKind: TypeKind.Interface } or NestedTypeSymbol { TypeKind: TypeKind.Interface })
            {
                AddInterfaceAndLinearBases(ResolutionContext.GetHandle(gpSym.GenericSymbol));
            }
            foreach (var c in gpSym.Constraints)
            {
                if (c.Handle is { } ch)
                    AddInterfaceAndLinearBases(ch);
            }
            bases = gpBases;
        }

        if (bases.Count <= 1)
            return (null, false, null);

        bool allowInterfaceBaseLookup = isExplicitInterfaceAccess ||
            typeSymObj is GenericParameterSymbol ||
            typeSymObj is TypeSymbol { TypeKind: TypeKind.Interface } ||
            typeSymObj is NestedTypeSymbol { TypeKind: TypeKind.Interface };

        var candidateBases = new List<(SymbolHandle BaseHandle, Symbol Member)>();
        foreach (var bh in bases.Skip(1))
        {
            var unwrappedBh = context.UnwrapAlias(bh);
            var bSym = (Symbol?)context.GetTypeSymbol(unwrappedBh) ?? context.GetSymbol(unwrappedBh);
            if (bSym is null)
                continue;

            bool isInterfaceBase = bSym is TypeSymbol { TypeKind: TypeKind.Interface } ||
                                   bSym is NestedTypeSymbol { TypeKind: TypeKind.Interface };
            if (isInterfaceBase && !allowInterfaceBaseLookup)
                continue;

            var bScope = GetOwnedScope(bSym);
            var bSyms = bScope.GetLocalSymbols(new SymbolPart(memberName, genericArity));
            if (bSyms.Count == 0 && genericArity > 0)
                bSyms = bScope.GetLocalSymbols(new SymbolPart(memberName));
            var bAccessible = bSyms.Where(cand =>
            {
                if (cand is MethodSymbol m)
                {
                    if (isExplicitInterfaceAccess)
                        return true;
                    return !m.Flags.HasFlag(FunctionFlags.InterfaceScoped);
                }
                if (cand is PropertySymbol p)
                {
                    if (isExplicitInterfaceAccess)
                        return true;
                    return !p.Flags.HasFlag(FunctionFlags.InterfaceScoped);
                }
                return !isExplicitInterfaceAccess;
            }).ToList();

            if (bAccessible.Count > 0)
            {
                candidateBases.Add((unwrappedBh, bAccessible[0]));
            }
        }

        if (candidateBases.Count == 0)
            return (null, false, null);

        if (candidateBases.Count == 1)
            return (candidateBases[0].Member, false, null);

        // Filter out candidates where BaseHandle is a supertype of another candidate's base handle
        var nonShadowed = candidateBases.Where(c1 =>
            !candidateBases.Any(c2 => c1.BaseHandle != c2.BaseHandle && TypeRef.IsSubtypeOf(c2.BaseHandle, c1.BaseHandle, context))
        ).ToList();

        // Deduplicate identical member handles (e.g. from shared root base)
        var uniqueMembers = nonShadowed
            .GroupBy(c => ResolutionContext.GetHandle(c.Member))
            .Select(g => g.First())
            .ToList();

        if (uniqueMembers.Count == 1)
            return (uniqueMembers[0].Member, false, null);

        return (null, true, uniqueMembers.Select(c => c.Member).ToList());
    }

    private Dictionary<SymbolHandle, TypeRef> BuildConstraintMemberSubstitutions(GenericParameterSymbol gp)
    {
        var subst = new Dictionary<SymbolHandle, TypeRef>();
        foreach (var c in gp.Constraints)
        {
            if (c.SpecialType is GenericSemanticType gst && c.Handle is { } ch)
            {
                IReadOnlyList<SymbolHandle> gps = context.GetSymbol(context.UnwrapAlias(ch)) switch
                {
                    TypeSymbol ts => ts.GenericParameters,
                    NestedTypeSymbol nts => nts.GenericParameters,
                    _ => []
                };
                for (int i = 0; i < gps.Count && i < gst.TypeArguments.Count; i++)
                    subst[gps[i]] = gst.TypeArguments[i];
            }
        }
        return subst;
    }

    /// <summary>
    /// Resolves the semantic type of an indexer expression (<c>target[index]</c>).
    /// Supports built-in indexing for arrays, spans, and pointers, as well as user-defined indexer operators.
    /// </summary>
    /// <param name="index">The index expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The element type of the indexed target, or <see cref="TypeRef.Unresolved"/> on failure.</returns>
    private TypeRef ResolveIndex(IndexExpression index, Scope scope, SymbolHandle containingFunction)
    {
        var targetType = ResolveExpression(index.Expression, scope, containingFunction);
        foreach (var arg in index.Arguments)
            ResolveExpression(arg, scope, containingFunction);

        var unwrapped = targetType;
        while (unwrapped.SpecialType is AliasSemanticType or ReferenceSemanticType or DynSemanticType)
        {
            unwrapped = unwrapped.SpecialType switch
            {
                AliasSemanticType al => al.TargetType,
                ReferenceSemanticType rf => rf.ElementType,
                DynSemanticType d => d.TargetType,
                _ => unwrapped
            };
        }

        if (unwrapped.SpecialType is SliceSemanticType sliceSemantic)
            return sliceSemantic.ElementType;
        if (unwrapped.SpecialType is ArraySemanticType arraySemantic)
            return arraySemantic.ElementType;
        if (unwrapped.SpecialType is PointerSemanticType ptrSemantic)
            return ptrSemantic.ElementType;

        // User-defined indexer (including Str, collections, and custom types)
        if (TryResolveIndexer(targetType, index, out var indexerResult))
            return indexerResult;

        return TypeRef.Unresolved;
    }

    /// <summary>
    /// Attempts to resolve a user-defined indexer getter operator on the target type.
    /// </summary>
    /// <param name="targetType">The type being indexed.</param>
    /// <param name="index">The index expression syntax node.</param>
    /// <param name="result">The return type of the indexer method.</param>
    /// <returns><see langword="true"/> if a matching indexer was found; otherwise <see langword="false"/>.</returns>
    private bool TryResolveIndexer(TypeRef targetType, IndexExpression index, out TypeRef result)
    {
        result = TypeRef.Unresolved;
        var unwrapped = targetType;
        while (unwrapped.SpecialType is AliasSemanticType or ReferenceSemanticType or DynSemanticType)
        {
            unwrapped = unwrapped.SpecialType switch
            {
                AliasSemanticType al => al.TargetType,
                ReferenceSemanticType rf => rf.ElementType,
                DynSemanticType d => d.TargetType,
                _ => unwrapped
            };
        }

        if (unwrapped.Handle is not { } handle)
            return false;

        var unwrappedH = context.UnwrapAlias(handle);
        var typeSym = (Symbol?)context.GetTypeSymbol(unwrappedH) ?? context.GetSymbol(unwrappedH);
        if (typeSym is null)
            return false;

        if (!context.DiscoveredOperators.TryGetOperators(OperatorKind.IndexerGetter, out var candidates))
            return false;

        Dictionary<SymbolHandle, TypeRef>? typeArgs = null;
        if (unwrapped.SpecialType is GenericSemanticType gst && typeSym is TypeSymbol ts && ts.GenericParameters.Count == gst.TypeArguments.Count)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef>(ts.GenericParameters.Count);
            for (int i = 0; i < ts.GenericParameters.Count; i++)
                typeArgs[ts.GenericParameters[i]] = gst.TypeArguments[i];
        }

        var candidateScopes = new List<Scope> { GetOwnedScope(typeSym) };
        foreach (var conf in context.Conformances)
        {
            if (conf.TargetType.Handle is { } cth && context.UnwrapAlias(cth) == unwrappedH &&
                context.GetSymbol(conf.OwnerHandle) is ExtensionSymbol ext)
            {
                candidateScopes.Add(GetOwnedScope(ext));
            }
        }

        foreach (var scope in candidateScopes)
        {
            foreach (var sym in scope.Symbols.Values)
            {
                if (sym is not MethodSymbol method)
                    continue;

                foreach (var cand in candidates)
                {
                    var ifaceSym = context.GetTypeSymbol(cand.InterfaceHandle);
                    if (ifaceSym is null)
                        continue;

                    bool isMatch = method.Flags.HasFlag(FunctionFlags.InterfaceScoped)
                        ? method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text)
                        : context.GetMethodSymbol(cand.MethodHandle) is { } opM && method.Name.Text == opM.Name.Text &&
                          TypeRef.IsSubtypeOf(unwrappedH, cand.InterfaceHandle, context);
                    if (!isMatch)
                        continue;

                    context.ResolvedTree.AddReference(index, ResolutionContext.GetHandle(method));
                    result = typeArgs is not null ? SubstituteTypeRef(method.ReturnType, typeArgs) : method.ReturnType;
                    return true;
                }
            }
        }

        return false;
    }

}
