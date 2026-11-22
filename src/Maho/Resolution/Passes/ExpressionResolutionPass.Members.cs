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

                if (opReturnType.SpecialType is AliasSemanticType alias)
                    receiverType = alias.TargetType;
                else
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
            if (receiverType.SpecialType is AliasSemanticType alias)
            {
                receiverType = alias.TargetType;
            }
        }

        if (receiverType.SpecialType is DynSemanticType dynReceiver)
        {
            receiverType = dynReceiver.TargetType;
        }

        // Check if access.Expression is a TypeSymbol (static access / enum variant)
        bool isStaticAccessOnType = access.Expression is TypeAsExpression;
        if (!isStaticAccessOnType && access.Expression is NamedExpression namedExpr && namedExpr.Identifier.MatchingKind is not MatchingKeywordKind.This)
        {
            if (context.ResolvedTree.TryGetReference(namedExpr, out var namedH) && context.GetSymbol(namedH) is TypeSymbol)
                isStaticAccessOnType = true;
            else if (receiverType.Kind == TypeRefKind.Resolved && receiverType.Handle is { } rh && context.GetTypeSymbol(rh) is { } ts && namedExpr.Identifier.Value == ts.Name.Text)
                isStaticAccessOnType = true;
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
            var (member, isAmbiguous, _) = FindMemberInTypeHierarchy(recvHandle.Value, memberName, isExplicitInterfaceAccess);

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
                    bool isMethodStatic = method.Flags.HasFlag(FunctionFlags.Static);
                    if (isStaticAccessOnType && !isMethodStatic)
                    {
                        var typeSymObj = (Symbol?)context.GetTypeSymbol(recvHandle.Value) ?? context.GetSymbol(recvHandle.Value);
                        context.Diagnostics.ReportCannotAccessInstanceMemberOnType(
                            access.Identifier.Span,
                            memberName,
                            typeSymObj?.Name.Text ?? receiverType.ToString(),
                            access.GetSource());
                        return TypeRef.Error;
                    }
                    else if (!isStaticAccessOnType && isMethodStatic)
                    {
                        var typeSymObj = (Symbol?)context.GetTypeSymbol(recvHandle.Value) ?? context.GetSymbol(recvHandle.Value);
                        context.Diagnostics.ReportCannotAccessStaticMemberOnInstance(
                            access.Identifier.Span,
                            memberName,
                            typeSymObj?.Name.Text ?? receiverType.ToString(),
                            access.GetSource());
                        return TypeRef.Error;
                    }
                }
                else if (member is FieldSymbol)
                {
                    if (isStaticAccessOnType)
                    {
                        var typeSymObj = (Symbol?)context.GetTypeSymbol(recvHandle.Value) ?? context.GetSymbol(recvHandle.Value);
                        context.Diagnostics.ReportCannotAccessInstanceMemberOnType(
                            access.Identifier.Span,
                            memberName,
                            typeSymObj?.Name.Text ?? receiverType.ToString(),
                            access.GetSource());
                        return TypeRef.Error;
                    }
                }
                else if (member is PropertySymbol prop)
                {
                    bool isPropStatic = prop.Flags.HasFlag(FunctionFlags.Static);
                    if (isStaticAccessOnType && !isPropStatic)
                    {
                        var typeSymObj = (Symbol?)context.GetTypeSymbol(recvHandle.Value) ?? context.GetSymbol(recvHandle.Value);
                        context.Diagnostics.ReportCannotAccessInstanceMemberOnType(
                            access.Identifier.Span,
                            memberName,
                            typeSymObj?.Name.Text ?? receiverType.ToString(),
                            access.GetSource());
                        return TypeRef.Error;
                    }
                    else if (!isStaticAccessOnType && isPropStatic)
                    {
                        var typeSymObj = (Symbol?)context.GetTypeSymbol(recvHandle.Value) ?? context.GetSymbol(recvHandle.Value);
                        context.Diagnostics.ReportCannotAccessStaticMemberOnInstance(
                            access.Identifier.Span,
                            memberName,
                            typeSymObj?.Name.Text ?? receiverType.ToString(),
                            access.GetSource());
                        return TypeRef.Error;
                    }
                }

                context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(member));
                return GetTypeFromSymbol(member);
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

                context.Diagnostics.ReportMemberNotFound(
                    access.Identifier.Span,
                    memberName,
                    originalReceiverType.ToQualifiedString(context),
                    labelTypeName: originalReceiverType.ToLabelString(context, scope),
                    source: access.GetSource());
                return TypeRef.Error;
            }
        }

        return TypeRef.Unresolved;
    }

    private bool TryResolveIndirectMemberAccessOperator(TypeRef type, out TypeRef elemType)
    {
        elemType = TypeRef.Unresolved;
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
        bool isExplicitInterfaceAccess)
    {
        typeHandle = context.UnwrapAlias(typeHandle);
        var typeSymObj = (Symbol?)context.GetTypeSymbol(typeHandle) ?? context.GetSymbol(typeHandle);
        if (typeSymObj is null)
            return (null, false, null);

        var recvScope = GetOwnedScope(typeSymObj);
        var directSyms = recvScope[new SymbolName(new SymbolPart(memberName))];
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

        if (bases.Count <= 1)
            return (null, false, null);

        var candidateBases = new List<(SymbolHandle BaseHandle, Symbol Member)>();
        foreach (var bh in bases.Skip(1))
        {
            var unwrappedBh = context.UnwrapAlias(bh);
            var bSym = (Symbol?)context.GetTypeSymbol(unwrappedBh) ?? context.GetSymbol(unwrappedBh);
            if (bSym is null)
                continue;

            var bScope = GetOwnedScope(bSym);
            var bSyms = bScope[new SymbolName(new SymbolPart(memberName))];
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

        if (targetType.SpecialType is ArraySemanticType arraySemantic)
            return arraySemantic.ElementType;
        if (targetType.SpecialType is FixedArraySemanticType fixedArraySemantic)
            return fixedArraySemantic.ElementType;
        if (targetType.SpecialType is SpanSemanticType spanSemantic)
            return spanSemantic.ElementType;
        if (targetType.SpecialType is PointerSemanticType ptrSemantic)
            return ptrSemantic.ElementType;

        // User-defined indexer
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

        if (targetType.Handle is not { } handle)
            return false;

        var typeSym = context.GetTypeSymbol(handle);
        if (typeSym is null)
            return false;

        var scope = GetOwnedScope(typeSym);
        foreach (var sym in scope.Symbols.Values)
        {
            if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
            {
                if (context.DiscoveredOperators.TryGetOperators(OperatorKind.IndexerGetter, out var candidates))
                {
                    if (candidates.Any(c => method.ExplicitInterfaceName?.Last.Equals(context.GetTypeSymbol(c.InterfaceHandle)?.Name) == true ||
                                            method.ExplicitInterfaceName?.Last.Text == context.GetTypeSymbol(c.InterfaceHandle)?.Name.Text))
                    {
                        context.ResolvedTree.AddReference(index, ResolutionContext.GetHandle(method));
                        result = method.ReturnType;
                        return true;
                    }
                }
            }
        }

        return false;
    }

}
