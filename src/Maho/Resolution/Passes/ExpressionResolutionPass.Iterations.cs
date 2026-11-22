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

        // 2. Determine element/item type
        TypeRef itemType = TypeRef.Unresolved;

        var handle = colType.Handle;
        if (!handle.HasValue)
        {
            if (colType.SpecialType is ArraySemanticType && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var hArr))
                handle = hArr;
            else if (colType.SpecialType is FixedArraySemanticType && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.FixedArray, out var hFix))
                handle = hFix;
            else if (colType.SpecialType is SpanSemanticType && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Span, out var hSpan))
                handle = hSpan;
        }

        var colSym = handle.HasValue ? context.GetSymbol(handle.Value) : null;
        IReadOnlyList<TypeRef> baseTypes = colSym switch
        {
            TypeSymbol ts => ts.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            _ => []
        };

        // Build substitution map if collection type is generic
        Dictionary<SymbolHandle, TypeRef> typeArgs = [];
        if (colSym is TypeSymbol typeSymbol)
        {
            if (colType.SpecialType is FixedArraySemanticType fix && typeSymbol.GenericParameters.Count >= 1)
            {
                typeArgs[typeSymbol.GenericParameters[0]] = fix.ElementType;
            }
            else if (colType.SpecialType is ArraySemanticType arr && typeSymbol.GenericParameters.Count >= 1)
            {
                typeArgs[typeSymbol.GenericParameters[0]] = arr.ElementType;
            }
            else if (colType.SpecialType is SpanSemanticType sp && typeSymbol.GenericParameters.Count >= 1)
            {
                typeArgs[typeSymbol.GenericParameters[0]] = sp.ElementType;
            }
            else if (colType.SpecialType is GenericSemanticType gstCol)
            {
                for (int i = 0; i < typeSymbol.GenericParameters.Count && i < gstCol.TypeArguments.Count; i++)
                {
                    typeArgs[typeSymbol.GenericParameters[i]] = gstCol.TypeArguments[i];
                }
            }
        }

        // Check Iterable<TItem, TIterator> (OperatorKind.Iterable)
        if (context.DiscoveredOperators.TryGetOperators(OperatorKind.Iterable, out var iterableCandidates))
        {
            foreach (var cand in iterableCandidates)
            {
                var match = baseTypes.FirstOrDefault(b => b.Handle == cand.InterfaceHandle);
                if (match.Handle.HasValue)
                {
                    var effMatch = typeArgs.Count > 0 ? SubstituteTypeRef(match, typeArgs) : match;
                    if (effMatch.SpecialType is GenericSemanticType gst && gst.TypeArguments.Count >= 1)
                    {
                        itemType = gst.TypeArguments[0];
                        break;
                    }
                }
            }
        }

        // Check Iterator<TItem> (OperatorKind.Iterator)
        if (itemType.IsUnresolved && context.DiscoveredOperators.TryGetOperators(OperatorKind.Iterator, out var iteratorCandidates))
        {
            foreach (var cand in iteratorCandidates)
            {
                var match = baseTypes.FirstOrDefault(b => b.Handle == cand.InterfaceHandle);
                if (match.Handle.HasValue)
                {
                    var effMatch = typeArgs.Count > 0 ? SubstituteTypeRef(match, typeArgs) : match;
                    if (effMatch.SpecialType is GenericSemanticType gst && gst.TypeArguments.Count >= 1)
                    {
                        itemType = gst.TypeArguments[0];
                        break;
                    }
                }
            }
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
        if (returnType.Handle.HasValue)
        {
            var retSym = context.GetSymbol(returnType.Handle.Value);
            IReadOnlyList<TypeRef> baseTypes = retSym switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                _ => []
            };

            // Check Iterator<TItem>
            if (context.DiscoveredOperators.TryGetOperators(OperatorKind.Iterator, out var itCandidates))
            {
                foreach (var cand in itCandidates)
                {
                    var match = baseTypes.FirstOrDefault(b => b.Handle == cand.InterfaceHandle);
                    if (match.Handle.HasValue && match.SpecialType is GenericSemanticType gst && gst.TypeArguments.Count >= 1)
                    {
                        expectedItemType = gst.TypeArguments[0];
                        break;
                    }
                }
            }

            // Check Iterable<TItem, TIterator>
            if (expectedItemType.IsUnresolved && context.DiscoveredOperators.TryGetOperators(OperatorKind.Iterable, out var iterableCandidates))
            {
                foreach (var cand in iterableCandidates)
                {
                    var match = baseTypes.FirstOrDefault(b => b.Handle == cand.InterfaceHandle);
                    if (match.Handle.HasValue && match.SpecialType is GenericSemanticType gst && gst.TypeArguments.Count >= 1)
                    {
                        expectedItemType = gst.TypeArguments[0];
                        break;
                    }
                }
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
}
