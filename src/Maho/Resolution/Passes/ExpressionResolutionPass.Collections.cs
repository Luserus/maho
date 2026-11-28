using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class ExpressionResolutionPass
{
    /// <summary>
    /// Resolves a collection literal expression (<c>[a, b, c]</c>) and types it as an array.
    /// </summary>
    /// <param name="collection">The collection expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>An array <see cref="TypeRef"/> wrapping the inferred element type.</returns>
    private TypeRef ResolveCollection(CollectionExpression collection, Scope scope, SymbolHandle containingFunction)
    {
        int expectedTerms = -1;
        TypeRef elemType = TypeRef.Unresolved;

        foreach (var item in collection.Expressions)
        {
            int termCount = item is MultiTermExpression m ? m.Terms.Count : 1;
            if (expectedTerms == -1)
                expectedTerms = termCount;
            else if (expectedTerms != termCount)
            {
                context.Diagnostics.ReportInconsistentCollectionTermCount(item.GetSpan() ?? default, termCount, expectedTerms, collection.GetSource());
            }

            var itemType = ResolveExpression(item, scope, containingFunction);
            if (elemType.IsUnresolved)
                elemType = itemType;
        }

        foreach (var modifier in collection.Modifiers)
        {
            if (modifier is CollectionConstructorModifier constructor)
            {
                foreach (var arg in constructor.Arguments)
                    ResolveExpression(arg, scope, containingFunction);
            }
        }

        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var arrayHandle);
        return TypeRef.Array(elemType, collection.Expressions.Count, arrayHandle != default ? arrayHandle : null);
    }

    private void TypeCheckCollectionExpression(CollectionExpression collection, TypeRef targetType, Scope scope, SymbolHandle containingFunction, SyntaxNode diagnosticAnchor)
    {
        // 1. Resolve arguments in with(...) trailer
        CollectionConstructorModifier? withMod = null;
        foreach (var mod in collection.Modifiers)
        {
            if (mod is CollectionConstructorModifier cMod)
            {
                withMod = cMod;
                foreach (var arg in cMod.Arguments)
                    ResolveExpression(arg, scope, containingFunction);
            }
        }

        // 2. Validate with(...) contract on targetType
        if (withMod is not null)
        {
            bool hasCollectionWith = false;
            if (context.DiscoveredOperators.TryGetOperators(OperatorKind.CollectionWith, out var withCandidates))
            {
                var targetSym = targetType.Handle.HasValue ? context.GetSymbol(targetType.Handle.Value) : null;
                IReadOnlyList<TypeRef> baseTypes = targetSym switch
                {
                    TypeSymbol ts => ts.BaseTypes,
                    NestedTypeSymbol nts => nts.BaseTypes,
                    _ => []
                };

                foreach (var cand in withCandidates)
                {
                    if (baseTypes.Any(b => b.Handle == cand.InterfaceHandle))
                    {
                        hasCollectionWith = true;
                        break;
                    }
                }
            }

            if (!hasCollectionWith)
            {
                context.Diagnostics.ReportCollectionMissingWithContract(
                    withMod.WithKeyword.Span,
                    targetType.ToDisplayString(context),
                    collection.GetSource());
            }
        }

        // 3. Check Collection<T...> contract or Array / Slice
        int expectedTerms = 1;
        IReadOnlyList<TypeRef>? expectedTermTypes = null;
        bool hasCollectionAddContract = context.DiscoveredOperators.TryGetOperators(OperatorKind.CollectionAdd, out var addCandidates) && addCandidates.Count > 0;

        // Check if targetType implements Collection<T...> (OperatorKind.CollectionAdd)
        if (hasCollectionAddContract)
        {
            var targetSym = targetType.Handle.HasValue ? context.GetSymbol(targetType.Handle.Value) : null;
            IReadOnlyList<TypeRef> baseTypes = targetSym switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                _ => []
            };

            foreach (var cand in addCandidates)
            {
                var match = baseTypes.FirstOrDefault(b => b.Handle == cand.InterfaceHandle);
                if (match.Handle.HasValue)
                {
                    if (match.SpecialType is GenericSemanticType gst)
                    {
                        expectedTerms = gst.TypeArguments.Count;
                        expectedTermTypes = gst.TypeArguments;
                    }
                    break;
                }
            }
        }

        if (expectedTermTypes is null)
        {
            if (targetType.SpecialType is SliceSemanticType sliceSem)
            {
                expectedTerms = 1;
                expectedTermTypes = [sliceSem.ElementType];
            }
            else if (targetType.SpecialType is ArraySemanticType arraySem)
            {
                expectedTerms = 1;
                expectedTermTypes = [arraySem.ElementType];
            }
            else if (!hasCollectionAddContract)
            {
                expectedTerms = collection.Expressions.Count > 0 && collection.Expressions[0] is MultiTermExpression m0 ? m0.Terms.Count : 1;
            }
        }

        if (expectedTermTypes is null && hasCollectionAddContract)
        {
            context.Diagnostics.ReportCollectionMissingContract(
                collection.LeftBracket.Span,
                targetType.ToDisplayString(context),
                collection.Expressions.Count > 0 && collection.Expressions[0] is MultiTermExpression m0 ? m0.Terms.Count : 1,
                collection.GetSource());
            return;
        }

        // 4. Type check elements and terms against expectedTermTypes
        foreach (var item in collection.Expressions)
        {
            IReadOnlyList<Expression> terms = item is MultiTermExpression m ? [.. m.Terms] : [item];
            if (terms.Count != expectedTerms)
            {
                context.Diagnostics.ReportInconsistentCollectionTermCount(
                    item.GetSpan() ?? default,
                    terms.Count,
                    expectedTerms,
                    collection.GetSource());
                continue;
            }

            for (int i = 0; i < terms.Count && (expectedTermTypes is null || i < expectedTermTypes.Count); i++)
            {
                var termExpr = terms[i];
                var expectedType = expectedTermTypes is not null ? expectedTermTypes[i] : TypeRef.Unresolved;
                var termType = ResolveExpression(termExpr, scope, containingFunction);

                if (expectedTermTypes is not null)
                {
                    if (termType.IsLiteral)
                    {
                        CollapseExpression(termExpr, expectedType, scope, containingFunction);
                    }
                    else if (!expectedType.IsAssignableFrom(termType, context) &&
                             !TryResolveImplicitCast(termType, expectedType, out _))
                    {
                        context.Diagnostics.ReportAssignmentTypeMismatch(
                            termExpr.GetSpan() ?? default,
                            termType.ToQualifiedString(context),
                            expectedType.ToQualifiedString(context),
                            rhsLabel: termType.ToLabelString(context, scope),
                            lhsLabel: expectedType.ToLabelString(context, scope),
                            source: collection.GetSource());
                    }
                }
            }
        }
    }
}
