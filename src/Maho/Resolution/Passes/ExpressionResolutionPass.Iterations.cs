using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class ExpressionResolutionPass
{
    private TypeRef ResolveFor(ForExpression forExpr, Scope scope, SymbolHandle containingFunction)
    {
        var forScope = context.GetSyntaxScope(forExpr, scope);

        // 1. Resolve the collection expression
        var colType = ResolveExpression(forExpr.Collection, scope, containingFunction);
        var unwrappedCol = colType;
        while (unwrappedCol.SpecialType is AliasSemanticType or ReferenceSemanticType or DynSemanticType)
        {
            unwrappedCol = unwrappedCol.SpecialType switch
            {
                AliasSemanticType al => al.TargetType,
                ReferenceSemanticType rf => rf.ElementType,
                DynSemanticType d => d.TargetType,
                _ => unwrappedCol
            };
        }

        // 2. Determine element/item type
        TypeRef itemType = TypeRef.Unresolved;

        var handle = unwrappedCol.Handle;
        if (!handle.HasValue)
        {
            if (unwrappedCol.SpecialType is SliceSemanticType && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Slice, out var hSlice))
                handle = hSlice;
            else if (unwrappedCol.SpecialType is ArraySemanticType && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var hArr))
                handle = hArr;
        }

        var colSym = handle.HasValue ? context.GetSymbol(handle.Value) : null;
        IReadOnlyList<TypeRef> baseTypes = colSym switch
        {
            TypeSymbol ts => ts.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            GenericParameterSymbol gp => gp.Constraints,
            _ => []
        };

        // Build substitution map if collection type is generic
        Dictionary<SymbolHandle, TypeRef> typeArgs = [];
        if (colSym is TypeSymbol typeSymbol)
        {
            if (unwrappedCol.SpecialType is ArraySemanticType arr && typeSymbol.GenericParameters.Count >= 1)
            {
                typeArgs[typeSymbol.GenericParameters[0]] = arr.ElementType;
            }
            else if (unwrappedCol.SpecialType is SliceSemanticType slice && typeSymbol.GenericParameters.Count >= 1)
            {
                typeArgs[typeSymbol.GenericParameters[0]] = slice.ElementType;
            }
            else if (unwrappedCol.SpecialType is GenericSemanticType gstCol)
            {
                for (int i = 0; i < typeSymbol.GenericParameters.Count && i < gstCol.TypeArguments.Count; i++)
                {
                    typeArgs[typeSymbol.GenericParameters[i]] = gstCol.TypeArguments[i];
                }
            }
        }

        // Check Iterable<TItem, TIterator> (OperatorKind.Iterable) across BaseTypes and extensions
        if (context.DiscoveredOperators.TryGetOperators(OperatorKind.Iterable, out var iterableCandidates) &&
            TryFindIterationItemType(unwrappedCol, handle, baseTypes, typeArgs, iterableCandidates, argIndex: 0, out var foundIterableItem))
        {
            itemType = foundIterableItem;
        }

        // Check Iterator<TItem> (OperatorKind.Iterator) across BaseTypes and extensions
        if (itemType.IsUnresolved &&
            context.DiscoveredOperators.TryGetOperators(OperatorKind.Iterator, out var iteratorCandidates) &&
            TryFindIterationItemType(unwrappedCol, handle, baseTypes, typeArgs, iteratorCandidates, argIndex: 0, out var foundIteratorItem))
        {
            itemType = foundIteratorItem;
        }

        if (itemType.IsUnresolved && !colType.IsError && !colType.IsInferred && !colType.IsUnresolved)
        {
            context.Diagnostics.ReportTypeNotIterable(
                forExpr.Collection.GetSpan() ?? forExpr.InKeyword.Span,
                colType.ToDisplayString(context),
                forExpr.GetSource());
            itemType = TypeRef.Error;
        }

        // 3. Bind the loop variable or validate assignment to existing variable
        if (forExpr.Declaration is not null)
        {
            var declarator = forExpr.Declaration.Declarator;
            if (declarator.Identifier is TupleName tupleName)
            {
                if (TryResolveDeconstructTuple(itemType, out var tupleSemantic, out var deconstructMethod))
                {
                    if (deconstructMethod != default)
                        context.ResolvedTree.AddReference(forExpr.Declaration, deconstructMethod);

                    if (tupleName.Elements.Count != tupleSemantic!.Elements.Count)
                    {
                        context.Diagnostics.ReportTupleDestructureMismatch(
                            tupleName.GetSpan() ?? default,
                            $"expected {tupleName.Elements.Count} elements, but iterator yields {tupleSemantic.Elements.Count}",
                            forExpr.GetSource());
                    }

                    for (int i = 0; i < tupleName.Elements.Count && i < tupleSemantic.Elements.Count; i++)
                    {
                        var elemName = context.GetScopedSymbolName(tupleName.Elements[i]).Last;
                        var elemSym = ResolveLocalOrFieldSymbol(forScope, elemName);
                        if (elemSym is not null && GetVariableSymbolType(elemSym)?.IsInferred == true)
                        {
                            var elemType = CollapseLiteralToDefault(tupleSemantic.Elements[i].Type);
                            if (elemType.IsResolved || elemType.IsSpecial)
                                SetVariableSymbolType(elemSym, elemType);
                        }
                    }
                }
                else if (!itemType.IsError && !itemType.IsInferred && !itemType.IsUnresolved)
                {
                    context.Diagnostics.ReportTupleDestructureMismatch(
                        forExpr.InKeyword.Span,
                        $"cannot destructure non-tuple element of type '{itemType.ToDisplayString(context)}'",
                        forExpr.GetSource());
                }
            }
            else
            {
                var name = context.GetScopedSymbolName(declarator.Identifier).Last;
                var sym = ResolveLocalOrFieldSymbol(forScope, name);
                var symType = sym is not null ? GetVariableSymbolType(sym) : null;

                if (sym is not null && symType?.IsInferred == true)
                {
                    var collapsed = CollapseLiteralToDefault(itemType);
                    if (collapsed.IsResolved || collapsed.IsSpecial)
                        SetVariableSymbolType(sym, collapsed);
                }
                else if (symType is not null && !symType.Value.IsAssignableFrom(itemType, context) &&
                         !TryResolveImplicitCast(itemType, symType.Value, out _))
                {
                    context.Diagnostics.ReportAssignmentTypeMismatch(
                        declarator.Identifier.GetSpan() ?? default,
                        itemType.ToQualifiedString(context),
                        symType.Value.ToQualifiedString(context),
                        rhsLabel: itemType.ToLabelString(context, forScope),
                        lhsLabel: symType.Value.ToLabelString(context, forScope),
                        source: forExpr.GetSource());
                }
            }
        }
        else if (forExpr.VariableExpression is not null)
        {
            var varType = ResolveExpression(forExpr.VariableExpression, scope, containingFunction);
            if (!IsExpressionLValue(forExpr.VariableExpression, varType))
            {
                context.Diagnostics.ReportCannotAssignToNonLValue(
                    forExpr.VariableExpression.GetSpan() ?? default,
                    forExpr.GetSource());
            }

            if (forExpr.VariableExpression is TupleExpression tupleLhs)
            {
                if (TryResolveDeconstructTuple(itemType, out var tupleSemantic, out var deconstructMethod))
                {
                    if (deconstructMethod != default)
                        context.ResolvedTree.AddReference(forExpr, deconstructMethod);

                    if (tupleLhs.Arguments.Count != tupleSemantic!.Elements.Count)
                    {
                        context.Diagnostics.ReportTupleDestructureMismatch(
                            tupleLhs.GetSpan() ?? default,
                            $"expected {tupleLhs.Arguments.Count} elements, but iterator yields {tupleSemantic.Elements.Count}",
                            forExpr.GetSource());
                    }
                    else
                    {
                        for (int i = 0; i < tupleLhs.Arguments.Count; i++)
                        {
                            var lhsElemExpr = tupleLhs.Arguments[i];
                            var lhsElemType = context.GetExpressionType(lhsElemExpr) ?? TypeRef.Unresolved;
                            var rhsElemType = tupleSemantic.Elements[i].Type;

                            if (!lhsElemType.IsAssignableFrom(rhsElemType, context) &&
                                !TryResolveImplicitCast(rhsElemType, lhsElemType, out _))
                            {
                                context.Diagnostics.ReportAssignmentTypeMismatch(
                                    lhsElemExpr.GetSpan() ?? default,
                                    rhsElemType.ToQualifiedString(context),
                                    lhsElemType.ToQualifiedString(context),
                                    rhsLabel: rhsElemType.ToLabelString(context, scope),
                                    lhsLabel: lhsElemType.ToLabelString(context, scope),
                                    source: forExpr.GetSource());
                            }
                        }
                    }
                }
                else if (!itemType.IsError && !itemType.IsInferred && !itemType.IsUnresolved)
                {
                    context.Diagnostics.ReportTupleDestructureMismatch(
                        forExpr.VariableExpression.GetSpan() ?? default,
                        $"cannot destructure non-tuple element of type '{itemType.ToDisplayString(context)}'",
                        forExpr.GetSource());
                }
            }
            else
            {
                if (!varType.IsAssignableFrom(itemType, context) &&
                    !TryResolveImplicitCast(itemType, varType, out _))
                {
                    context.Diagnostics.ReportAssignmentTypeMismatch(
                        forExpr.VariableExpression.GetSpan() ?? default,
                        itemType.ToQualifiedString(context),
                        varType.ToQualifiedString(context),
                        rhsLabel: itemType.ToLabelString(context, scope),
                        lhsLabel: varType.ToLabelString(context, scope),
                        source: forExpr.GetSource());
                }
            }
        }

        // 4. Resolve the loop body
        ResolveExpression(forExpr.Body, forScope, containingFunction);

        return voidType;
    }

    private void ResolveYieldStatement(LocalYieldStatement yieldStmt, Scope scope, SymbolHandle containingFunction)
    {
        var funcSym = containingFunction != default ? context.GetSymbol(containingFunction) : null;
        var returnType = funcSym switch
        {
            MethodSymbol m => m.ReturnType,
            FunctionSymbol f => f.ReturnType,
            _ => TypeRef.Unresolved
        };

        TypeRef expectedItemType = TypeRef.Unresolved;
        var unwrappedReturnType = returnType;
        while (unwrappedReturnType.SpecialType is AliasSemanticType or ReferenceSemanticType or DynSemanticType)
        {
            unwrappedReturnType = unwrappedReturnType.SpecialType switch
            {
                AliasSemanticType al => al.TargetType,
                ReferenceSemanticType rf => rf.ElementType,
                DynSemanticType d => d.TargetType,
                _ => unwrappedReturnType
            };
        }

        if (unwrappedReturnType.Handle.HasValue)
        {
            var retSym = context.GetSymbol(unwrappedReturnType.Handle.Value);
            IReadOnlyList<TypeRef> baseTypes = retSym switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                GenericParameterSymbol gp => gp.Constraints,
                _ => []
            };

            Dictionary<SymbolHandle, TypeRef> yieldTypeArgs = [];
            if (retSym is TypeSymbol retTypeSym && unwrappedReturnType.SpecialType is GenericSemanticType gstRet)
            {
                for (int i = 0; i < retTypeSym.GenericParameters.Count && i < gstRet.TypeArguments.Count; i++)
                    yieldTypeArgs[retTypeSym.GenericParameters[i]] = gstRet.TypeArguments[i];
            }

            // Check Iterator<TItem> across BaseTypes and extensions
            if (context.DiscoveredOperators.TryGetOperators(OperatorKind.Iterator, out var itCandidates) &&
                TryFindIterationItemType(unwrappedReturnType, unwrappedReturnType.Handle, baseTypes, yieldTypeArgs, itCandidates, argIndex: 0, out var foundYieldIterator))
            {
                expectedItemType = foundYieldIterator;
            }

            // Check Iterable<TItem, TIterator> across BaseTypes and extensions
            if (expectedItemType.IsUnresolved &&
                context.DiscoveredOperators.TryGetOperators(OperatorKind.Iterable, out var iterableCandidates) &&
                TryFindIterationItemType(unwrappedReturnType, unwrappedReturnType.Handle, baseTypes, yieldTypeArgs, iterableCandidates, argIndex: 0, out var foundYieldIterable))
            {
                expectedItemType = foundYieldIterable;
            }
        }

        if (expectedItemType.IsUnresolved)
        {
            context.Diagnostics.ReportInvalidYieldUsage(
                yieldStmt.Keyword.Span,
                yieldStmt.GetSource());
            return;
        }

        if (yieldStmt.IsBreak)
            return;

        if (yieldStmt.Expression is not null)
        {
            var yieldedType = ResolveExpression(yieldStmt.Expression, scope, containingFunction);
            if (yieldedType.IsLiteral)
            {
                CollapseExpression(yieldStmt.Expression, expectedItemType, scope, containingFunction);
            }
            else if (!expectedItemType.IsAssignableFrom(yieldedType, context) &&
                     !TryResolveImplicitCast(yieldedType, expectedItemType, out _))
            {
                context.Diagnostics.ReportYieldTypeMismatch(
                    yieldStmt.Expression.GetSpan() ?? yieldStmt.Keyword.Span,
                    yieldedType.ToQualifiedString(context),
                    expectedItemType.ToQualifiedString(context),
                    yieldStmt.GetSource());
            }
        }
    }

    private bool TryFindIterationItemType(
        TypeRef targetType,
        SymbolHandle? targetHandle,
        IReadOnlyList<TypeRef> baseTypes,
        Dictionary<SymbolHandle, TypeRef> typeArgs,
        IReadOnlyList<DiscoveredOperatorMethod> candidates,
        int argIndex,
        out TypeRef itemType)
    {
        foreach (var cand in candidates)
        {
            foreach (var b in baseTypes)
            {
                if (TryExtractGenericInterfaceArg(b, cand.InterfaceHandle, typeArgs, argIndex, out itemType))
                    return true;
            }
        }

        if (targetHandle is { } rawHandle)
        {
            var normalizedHandle = context.UnwrapAlias(rawHandle);
            List<InterfaceConformance> conformances = [];
            CollectTypeHierarchyConformances(normalizedHandle, conformances);

            foreach (var cand in candidates)
            {
                foreach (var conf in conformances)
                {
                    if (conf.Accessibility == Accessibility.Internal && conf.ModuleProjectId != context.ProjectId)
                        continue;

                    if (conf.InterfaceHandle != cand.InterfaceHandle &&
                        !TypeRef.IsSubtypeOf(conf.InterfaceHandle, cand.InterfaceHandle, context))
                    {
                        continue;
                    }

                    var effectiveSubst = new Dictionary<SymbolHandle, TypeRef>(typeArgs);
                    if (conf.IsExtension && context.GetSymbol(conf.OwnerHandle) is ExtensionSymbol ext)
                    {
                        if (!TryUnifyIterationExtensionTarget(ext, targetType, normalizedHandle, effectiveSubst))
                            continue;
                    }

                    if (TryExtractGenericInterfaceArg(conf.InterfaceType, cand.InterfaceHandle, effectiveSubst, argIndex, out itemType))
                        return true;
                }
            }
        }

        itemType = TypeRef.Unresolved;
        return false;
    }

    private void CollectTypeHierarchyConformances(SymbolHandle targetHandle, List<InterfaceConformance> destination)
    {
        var visited = new HashSet<SymbolHandle>();
        Walk(targetHandle);

        void Walk(SymbolHandle current)
        {
            if (!visited.Add(current))
                return;

            context.CollectConformancesForTarget(current, destination);
            if (context.GetTypeSymbol(current) is not { } typeSym)
                return;

            foreach (var b in typeSym.BaseTypes)
            {
                if (b.Handle is { } bh)
                {
                    var unwrapped = context.UnwrapAlias(bh);
                    if (context.GetTypeSymbol(unwrapped) is not { TypeKind: TypeKind.Interface })
                        Walk(unwrapped);
                }
            }
        }
    }

    private bool TryUnifyIterationExtensionTarget(
        ExtensionSymbol ext,
        TypeRef actualType,
        SymbolHandle normalizedHandle,
        Dictionary<SymbolHandle, TypeRef> extArgs)
    {
        if (ext.GenericParameters.Count == 0)
            return actualType.Handle is { } ah && context.UnwrapAlias(ah) == normalizedHandle;

        if (TryUnifyExtensionTarget(ext.TargetTypeRef, actualType, ext.GenericParameters, extArgs))
            return true;

        if (ext.TargetTypeRef.SpecialType is GenericSemanticType extGst && extGst.TypeArguments.Count >= 1)
        {
            TypeRef? elementType = actualType.SpecialType switch
            {
                ArraySemanticType arr => arr.ElementType,
                SliceSemanticType sl => sl.ElementType,
                _ => null
            };
            if (elementType is { } elem &&
                TryUnifyExtensionTarget(extGst.TypeArguments[0], elem, ext.GenericParameters, extArgs))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryExtractGenericInterfaceArg(
        TypeRef ifaceRef,
        SymbolHandle targetInterfaceHandle,
        Dictionary<SymbolHandle, TypeRef> typeArgs,
        int argIndex,
        out TypeRef extracted,
        HashSet<SymbolHandle>? visited = null)
    {
        var effMatch = typeArgs.Count > 0 ? SubstituteTypeRef(ifaceRef, typeArgs) : ifaceRef;
        if (effMatch.Handle == targetInterfaceHandle &&
            effMatch.SpecialType is GenericSemanticType gst &&
            gst.TypeArguments.Count > argIndex)
        {
            extracted = gst.TypeArguments[argIndex];
            return true;
        }

        if (effMatch.Handle is { } subHandle &&
            context.GetTypeSymbol(subHandle) is { TypeKind: TypeKind.Interface } subIface)
        {
            visited ??= [];
            if (!visited.Add(subHandle))
            {
                extracted = TypeRef.Unresolved;
                return false;
            }

            Dictionary<SymbolHandle, TypeRef> subArgs = [];
            if (effMatch.SpecialType is GenericSemanticType subGst)
            {
                for (int i = 0; i < subIface.GenericParameters.Count && i < subGst.TypeArguments.Count; i++)
                    subArgs[subIface.GenericParameters[i]] = subGst.TypeArguments[i];
            }

            foreach (var baseRef in subIface.BaseTypes)
            {
                if (TryExtractGenericInterfaceArg(baseRef, targetInterfaceHandle, subArgs, argIndex, out extracted, visited))
                    return true;
            }
        }

        extracted = TypeRef.Unresolved;
        return false;
    }
}
