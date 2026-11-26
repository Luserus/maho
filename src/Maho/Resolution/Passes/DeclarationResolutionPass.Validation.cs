using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Resolution.Layout;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

internal sealed partial class DeclarationResolutionPass
{
    private void CheckDuplicateTypeDeclarations()
    {
        // 1. Top-level types
        var topGroups = new Dictionary<(NamespaceTrieNode?, SymbolPart), List<TypeSymbol>>();

        foreach (var type in context.TypeSymbols)
        {
            if (type.Name.Text == "_")
                continue;

            var key = (type.ContainingNamespace, type.Name);

            if (!topGroups.TryGetValue(key, out var list))
            {
                list = [];
                topGroups[key] = list;
            }

            list.Add(type);
        }

        foreach (var (_, group) in topGroups)
        {
            if (group.Count <= 1)
                continue;

            CheckTypeGroup(group);
        }

        // 2. Nested and local types
        var nestedGroups = new Dictionary<(Scope, SymbolPart), List<NestedTypeSymbol>>();
        foreach (var type in context.NestedTypeSymbols)
        {
            if (type.Name.Text == "_")
                continue;

            var key = (type.EnclosingScope, type.Name);

            if (!nestedGroups.TryGetValue(key, out var list))
            {
                list = [];
                nestedGroups[key] = list;
            }

            list.Add(type);
        }

        foreach (var (_, group) in nestedGroups)
        {
            if (group.Count <= 1)
                continue;

            CheckNestedTypeGroup(group);
        }

        // 3. Types vs Aliases in same namespace
        foreach (var alias in context.AliasSymbols)
        {
            if (alias.ContainingNamespace != null)
            {
                var matchingTypes = alias.ContainingNamespace.GetSymbols(alias.Name);

                foreach (var sym in matchingTypes)
                {
                    if (sym is TypeSymbol typeSym)
                    {
                        var span = alias.Syntax?.Name.GetSpan() ?? alias.Syntax?.GetSpan() ?? default;
                        var firstSpan = typeSym.Syntax?.Name.GetSpan() ?? typeSym.Syntax?.GetSpan() ?? default;
                        context.Diagnostics.ReportDuplicateAliasDeclaration(
                            alias.Name.ToString(),
                            span,
                            firstSpan,
                            alias.Syntax?.GetSource(),
                            typeSym.Syntax?.GetSource());
                    }
                }
            }
        }
    }

    private void CheckTypeGroup(List<TypeSymbol> group)
    {
        bool allPartial = group.All(static t => t.Flags.HasFlag(TypeFlags.Partial));

        if (allPartial)
        {
            var first = group[0];

            for (int i = 1; i < group.Count; i++)
            {
                var cur = group[i];

                if (cur.TypeKind != first.TypeKind)
                {
                    var span = cur.Syntax?.Name.GetSpan() ?? cur.Syntax?.GetSpan() ?? default;
                    var firstSpan = first.Syntax?.Name.GetSpan() ?? first.Syntax?.GetSpan() ?? default;
                    context.Diagnostics.ReportConflictingPartialTypeKinds(
                        cur.Name.ToString(),
                        first.TypeKind.ToString().ToLowerInvariant(),
                        cur.TypeKind.ToString().ToLowerInvariant(),
                        span,
                        firstSpan,
                        cur.Syntax?.GetSource(),
                        first.Syntax?.GetSource());
                }
            }

            return;
        }

        var firstDecl = group[0];
        var firstDeclSpan = firstDecl.Syntax?.Name.GetSpan() ?? firstDecl.Syntax?.GetSpan() ?? default;

        for (int i = 1; i < group.Count; i++)
        {
            var redecl = group[i];
            var redeclSpan = redecl.Syntax?.Name.GetSpan() ?? redecl.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportDuplicateTypeDeclaration(
                redecl.Name.ToString(),
                redeclSpan,
                firstDeclSpan,
                redecl.Syntax?.GetSource(),
                firstDecl.Syntax?.GetSource());
        }
    }

    private void CheckNestedTypeGroup(List<NestedTypeSymbol> group)
    {
        var allPartial = group.All(static t => t.Flags.HasFlag(TypeFlags.Partial));

        if (allPartial)
        {
            var first = group[0];
            for (int i = 1; i < group.Count; i++)
            {
                var cur = group[i];

                if (cur.TypeKind != first.TypeKind)
                {
                    var span = cur.Syntax?.Name.GetSpan() ?? cur.Syntax?.GetSpan() ?? default;
                    var firstSpan = first.Syntax?.Name.GetSpan() ?? first.Syntax?.GetSpan() ?? default;
                    context.Diagnostics.ReportConflictingPartialTypeKinds(
                        cur.Name.ToString(),
                        first.TypeKind.ToString().ToLowerInvariant(),
                        cur.TypeKind.ToString().ToLowerInvariant(),
                        span,
                        firstSpan,
                        cur.Syntax?.GetSource(),
                        first.Syntax?.GetSource());
                }
            }

            return;
        }

        var firstDecl = group[0];
        var firstDeclSpan = firstDecl.Syntax?.Name.GetSpan() ?? firstDecl.Syntax?.GetSpan() ?? default;

        for (int i = 1; i < group.Count; i++)
        {
            var redecl = group[i];
            var redeclSpan = redecl.Syntax?.Name.GetSpan() ?? redecl.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportDuplicateTypeDeclaration(
                redecl.Name.ToString(),
                redeclSpan,
                firstDeclSpan,
                redecl.Syntax?.GetSource(),
                firstDecl.Syntax?.GetSource());
        }
    }

    private void CheckCyclicTypeHierarchies()
    {
        var reported = new HashSet<SymbolHandle>();

        foreach (var type in context.TypeSymbols)
        {
            var handle = ResolutionContext.GetHandle(type);

            if (!reported.Contains(handle) && HasInheritanceCycle(handle, [handle]))
            {
                reported.Add(handle);
                var span = type.Syntax?.Name.GetSpan() ?? type.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportCyclicTypeHierarchy(span, type.Name.ToString(), type.Syntax?.GetSource());
            }
        }

        foreach (var type in context.NestedTypeSymbols)
        {
            var handle = ResolutionContext.GetHandle(type);

            if (!reported.Contains(handle) && HasInheritanceCycle(handle, [handle]))
            {
                reported.Add(handle);
                var span = type.Syntax?.Name.GetSpan() ?? type.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportCyclicTypeHierarchy(span, type.Name.ToString(), type.Syntax?.GetSource());
            }
        }
    }

    private void LinearizeAndDeduplicateAllBases()
    {
        TypeContiguityAnalyzer.ClearCache();

        foreach (var type in context.TypeSymbols)
        {
            LinearizeType(type, ResolutionContext.GetHandle(type), b => type.LinearizedBases = b);
        }

        foreach (var type in context.NestedTypeSymbols)
        {
            LinearizeType(type, ResolutionContext.GetHandle(type), b => type.LinearizedBases = b);
        }
    }

    private void LinearizeType(Symbol typeSymbol, SymbolHandle handle, Action<List<SymbolHandle>> setLinearizedBases)
    {
        if (C3Linearizer.TryLinearize(handle, context, out var linearization))
        {
            setLinearizedBases(linearization);
        }
        else
        {
            var span = (typeSymbol as TypeSymbol)?.Syntax?.Name.GetSpan() ??
                       (typeSymbol as NestedTypeSymbol)?.Syntax?.Name.GetSpan() ?? default;
            var source = (typeSymbol as TypeSymbol)?.Syntax?.GetSource() ??
                         (typeSymbol as NestedTypeSymbol)?.Syntax?.GetSource();
            context.Diagnostics.ReportInconsistentInheritanceOrder(span, typeSymbol.Name.ToString(), source);
        }
    }

    private void ValidateConstructorBaseInitializers()
    {
        // 1. Top-level functions cannot have base initializers
        foreach (var function in context.FunctionSymbols)
        {
            if (function.Syntax?.Signature.BaseClause is { } baseClause)
            {
                context.Diagnostics.ReportInvalidBaseInitializerOnOrdinaryFunction(
                    baseClause.GetSpan() ?? default,
                    function.Syntax.GetSource());
            }
        }

        // 2. Member methods
        foreach (var method in context.MethodSymbols)
        {
            if (method.Syntax is null)
                continue;

            bool isConstructor = method.Name.Text == "Construct";
            var baseClause = method.Syntax.Signature.BaseClause;

            if (!isConstructor)
            {
                if (baseClause is not null)
                {
                    context.Diagnostics.ReportInvalidBaseInitializerOnOrdinaryFunction(
                        baseClause.GetSpan() ?? default,
                        method.Syntax.GetSource());
                }
                continue;
            }

            // Method IS a constructor
            if (method.Parent is not { } parentHandle)
                continue;

            var parentSym = context.GetSymbol(parentHandle);
            string parentName = parentSym?.Name.Text ?? "Type";
            IReadOnlyList<SymbolHandle> linearizedBases = parentSym switch
            {
                TypeSymbol ts => ts.LinearizedBases,
                NestedTypeSymbol nts => nts.LinearizedBases,
                _ => []
            };

            var validBaseHandles = new HashSet<SymbolHandle>(linearizedBases.Skip(1));
            var initializedBases = new HashSet<SymbolHandle>();

            if (baseClause is not null)
            {
                var scope = GetOwnedScope(method);
                foreach (var init in baseClause.Initializers)
                {
                    var targetType = ResolveType(init.BaseType, scope, reportDiagnostics: true);
                    if (targetType.Handle is { } targetHandle)
                    {
                        targetHandle = context.UnwrapAlias(targetHandle);
                        if (!validBaseHandles.Contains(targetHandle))
                        {
                            var targetName = context.GetTypeSymbol(targetHandle)?.Name.Text ?? targetType.ToString();
                            context.Diagnostics.ReportTypeIsNotBaseOf(
                                init.BaseType.GetSpan() ?? default,
                                targetName,
                                parentName,
                                method.Syntax.GetSource());
                        }
                        else
                        {
                            if (!initializedBases.Add(targetHandle))
                            {
                                var targetName = context.GetTypeSymbol(targetHandle)?.Name.Text ?? targetType.ToString();
                                context.Diagnostics.ReportDuplicateBaseInitializer(
                                    init.BaseType.GetSpan() ?? default,
                                    targetName,
                                    method.Syntax.GetSource());
                            }
                        }
                    }
                }
            }

            context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.ValueType, out var valTypeH);
            context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Struct, out var structH);
            context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Enum, out var enumH);

            // Check if any base in the hierarchy requires explicit initialization
            foreach (var bHandle in validBaseHandles)
            {
                // Skip built-in ValType / Struct / Enum
                if (bHandle == valTypeH || bHandle == structH || bHandle == enumH)
                    continue;

                if (context.GetSymbol(bHandle) is not { } bSym)
                    continue;

                // Check if bSym has constructors
                var bScope = GetOwnedScope(bSym);
                var constructors = bScope.Symbols.Values
                    .OfType<MethodSymbol>()
                    .Where(m => m.Name.Text == "Construct")
                    .ToList();

                // If bSym declares at least one constructor, and none of them is parameterless (0 parameters),
                // then it requires explicit initialization!
                if (constructors.Count > 0 && !constructors.Any(m => m.Parameters.Count == 0))
                {
                    if (!initializedBases.Contains(bHandle))
                    {
                        var span = baseClause?.GetSpan() ??
                                   method.Syntax.Signature.Identifier.GetSpan() ??
                                   method.Syntax.GetSpan() ?? default;
                        context.Diagnostics.ReportBaseConstructorRequiresExplicitInit(
                            span,
                            bSym.Name.Text,
                            method.Syntax.GetSource());
                    }
                }
            }
        }
    }

    private bool HasInheritanceCycle(SymbolHandle current, HashSet<SymbolHandle> path)
    {
        IReadOnlyList<TypeRef> baseTypes;

        if (current.Kind == SymbolKind.Type && context.GetTypeSymbol(current) is { } typeSym)
            baseTypes = typeSym.BaseTypes;
        else if (current.Kind == SymbolKind.NestedType && context.GetNestedTypeSymbol(current) is { } nestedTypeSym)
            baseTypes = nestedTypeSym.BaseTypes;
        else
            return false;

        foreach (var baseType in baseTypes)
        {
            var underlying = context.GetType(baseType);

            if (underlying is not { } baseHandle)
                continue;

            if (path.Contains(baseHandle))
                return true;

            path.Add(baseHandle);

            if (HasInheritanceCycle(baseHandle, path))
                return true;

            path.Remove(baseHandle);
        }

        return false;
    }

    private void CheckDuplicateAndPartialFunctions()
    {
        // 1. Top-level functions
        var topGroups = new Dictionary<(NamespaceTrieNode?, SymbolPart), List<FunctionSignatureInfo>>();

        foreach (var fn in context.FunctionSymbols)
        {
            if (fn.Name.Text == "_")
                continue;

            var key = (fn.ContainingNamespace, fn.Name);

            if (!topGroups.TryGetValue(key, out var list))
            {
                list = [];
                topGroups[key] = list;
            }

            list.Add(new FunctionSignatureInfo(fn, fn.Flags, fn.Syntax, fn.GenericParameters, fn.Parameters, fn.ReturnType));
        }

        foreach (var (_, group) in topGroups)
            ValidateFunctionSignatureGroup(group);

        // 2. Member & Local methods
        var methodGroups = new Dictionary<(Scope, SymbolPart, SymbolName?), List<FunctionSignatureInfo>>();

        foreach (var m in context.MethodSymbols)
        {
            if (m.Name.Text == "_")
                continue;

            var key = (m.EnclosingScope, m.Name, m.ExplicitInterfaceName);

            if (!methodGroups.TryGetValue(key, out var list))
            {
                list = [];
                methodGroups[key] = list;
            }

            list.Add(new FunctionSignatureInfo(m, m.Flags, m.Syntax, m.GenericParameters, m.Parameters, m.ReturnType));
        }

        foreach (var (_, group) in methodGroups)
            ValidateFunctionSignatureGroup(group);
    }

    private void ValidateFunctionSignatureGroup(List<FunctionSignatureInfo> group)
    {
        if (group.Count <= 1)
            return;

        // Check for conflicting methods differing solely by receiver
        for (int i = 0; i < group.Count; i++)
        {
            var fnA = group[i];
            bool hasReceiverA = fnA.Parameters.Count > 0 &&
                                context.GetParameterSymbol(fnA.Parameters[0])?.Syntax?.Declarator.IsReceiver == true;
            if (!hasReceiverA)
                continue;

            var nonReceiverA = fnA.Parameters.Skip(1).ToList();

            for (int j = i + 1; j < group.Count; j++)
            {
                var fnB = group[j];
                bool hasReceiverB = fnB.Parameters.Count > 0 &&
                                    context.GetParameterSymbol(fnB.Parameters[0])?.Syntax?.Declarator.IsReceiver == true;
                if (!hasReceiverB)
                    continue;

                var nonReceiverB = fnB.Parameters.Skip(1).ToList();

                if (SignaturesMatch(fnA.GenericParameters, nonReceiverA, fnB.GenericParameters, nonReceiverB))
                {
                    var recvA = context.GetParameterSymbol(fnA.Parameters[0])?.Syntax?.Declarator.ReceiverKind;
                    var recvB = context.GetParameterSymbol(fnB.Parameters[0])?.Syntax?.Declarator.ReceiverKind;

                    if (recvA != recvB)
                    {
                        var firstSpan = fnA.Syntax?.Signature.Identifier.GetSpan() ?? fnA.Syntax?.GetSpan() ?? default;
                        var redeclSpan = fnB.Syntax?.Signature.Identifier.GetSpan() ?? fnB.Syntax?.GetSpan() ?? default;
                        context.Diagnostics.ReportMethodOverloadSolelyByReceiver(
                            fnB.Symbol.Name.ToString(),
                            redeclSpan,
                            firstSpan,
                            fnB.Syntax?.GetSource(),
                            fnA.Syntax?.GetSource());
                    }
                }
            }
        }

        // Partition into sub-groups by parameter signature
        var subGroups = new List<List<FunctionSignatureInfo>>();

        foreach (var fn in group)
        {
            bool added = false;

            foreach (var sub in subGroups)
            {
                if (SignaturesMatch(sub[0].GenericParameters, sub[0].Parameters, fn.GenericParameters, fn.Parameters))
                {
                    sub.Add(fn);
                    added = true;
                    break;
                }
            }

            if (!added)
                subGroups.Add([fn]);
        }

        foreach (var subGroup in subGroups)
        {
            if (subGroup.Count <= 1)
                continue;

            bool allPartial = subGroup.All(static f => f.Flags.HasFlag(FunctionFlags.Partial));

            if (allPartial)
            {
                // Partial function: unlimited without body allowed. At most one with body.
                var withBodies = new List<FunctionSignatureInfo>();
                foreach (var fn in subGroup)
                    if (fn.Syntax?.Body is not null and not FunctionEmptyBody)
                        withBodies.Add(fn);

                if (withBodies.Count > 1)
                {
                    var firstBody = withBodies[0];
                    var firstSpan = firstBody.Syntax?.Body.GetSpan() ?? firstBody.Syntax?.GetSpan() ?? default;

                    for (int i = 1; i < withBodies.Count; i++)
                    {
                        var redecl = withBodies[i];
                        var redeclSpan = redecl.Syntax?.Body.GetSpan() ?? redecl.Syntax?.GetSpan() ?? default;
                        var fnName = redecl.Symbol.Name.ToString();
                        context.Diagnostics.ReportMultiplePartialFunctionBodies(
                            fnName,
                            redeclSpan,
                            firstSpan,
                            redecl.Syntax?.GetSource(),
                            firstBody.Syntax?.GetSource());
                    }
                }

                // Check return type consistency
                var firstReturn = subGroup[0].ReturnType;
                var firstSyntax = subGroup[0].Syntax;

                for (int i = 1; i < subGroup.Count; i++)
                {
                    var cur = subGroup[i];
                    if (firstReturn != cur.ReturnType && (firstReturn.IsResolved || cur.ReturnType.IsResolved))
                    {
                        var span = cur.Syntax?.Signature.ReturnType.GetSpan() ?? cur.Syntax?.GetSpan() ?? default;
                        var firstSpan = firstSyntax?.Signature.ReturnType.GetSpan() ?? firstSyntax?.GetSpan() ?? default;
                        var fnName = cur.Symbol.Name.ToString();
                        context.Diagnostics.ReportConflictingPartialFunctionReturnTypes(
                            fnName,
                            span,
                            firstSpan,
                            cur.Syntax?.GetSource(),
                            firstSyntax?.GetSource());
                    }
                }
            }
            else
            {
                // Not all partial -> duplicate function declaration error
                var first = subGroup[0];
                var firstSpan = first.Syntax?.Signature.Identifier.GetSpan() ?? first.Syntax?.GetSpan() ?? default;

                for (int i = 1; i < subGroup.Count; i++)
                {
                    var redecl = subGroup[i];
                    var fnName = redecl.Symbol.Name.ToString();

                    if (fnName == "Main")
                    {
                        var earlierInSameFile = subGroup.Take(i).FirstOrDefault(prev => prev.Syntax?.GetSource() == redecl.Syntax?.GetSource());
                        if (earlierInSameFile.Symbol is null)
                        {
                            // First declaration of 'Main' in this file; multi-file Main is disambiguated by ValidateMainEntryPoints.
                            continue;
                        }
                    }

                    var redeclSpan = redecl.Syntax?.Signature.Identifier.GetSpan() ?? redecl.Syntax?.GetSpan() ?? default;
                    context.Diagnostics.ReportDuplicateFunctionDeclaration(
                        fnName,
                        redeclSpan,
                        firstSpan,
                        redecl.Syntax?.GetSource(),
                        first.Syntax?.GetSource());
                }
            }
        }
    }

    private bool SignaturesMatch(IReadOnlyList<SymbolHandle> genericParamsA, List<SymbolHandle> paramsA, IReadOnlyList<SymbolHandle> genericParamsB, List<SymbolHandle> paramsB)
    {
        if (genericParamsA.Count != genericParamsB.Count)
            return false;

        if (paramsA.Count != paramsB.Count)
            return false;

        for (int i = 0; i < paramsA.Count; i++)
        {
            var paramA = context.ParameterSymbols[paramsA[i].ID];
            var paramB = context.ParameterSymbols[paramsB[i].ID];

            if (!ParameterTypesMatch(paramA.Type, genericParamsA, paramB.Type, genericParamsB, paramA.Syntax, paramB.Syntax))
                return false;
        }

        return true;
    }

    private bool ParameterTypesMatch(TypeRef typeA, IReadOnlyList<SymbolHandle> genericParamsA, TypeRef typeB, IReadOnlyList<SymbolHandle> genericParamsB, Parameter? syntaxA, Parameter? syntaxB)
    {
        var unwrappedA = context.UnwrapAlias(typeA);
        var unwrappedB = context.UnwrapAlias(typeB);

        int genIndexA = -1;

        if (unwrappedA.IsResolved && unwrappedA.Handle is { } hA)
        {
            for (int k = 0; k < genericParamsA.Count; k++)
                if (genericParamsA[k] == hA)
                {
                    genIndexA = k;
                    break;
                }
        }

        int genIndexB = -1;

        if (unwrappedB.IsResolved && unwrappedB.Handle is { } hB)
        {
            for (int k = 0; k < genericParamsB.Count; k++)
                if (genericParamsB[k] == hB)
                {
                    genIndexB = k;
                    break;
                }
        }

        if (genIndexA >= 0 || genIndexB >= 0)
            return genIndexA == genIndexB;

        // An immutable safe alias &T at call-site Foo(v) can refer to both T and &T, causing ambiguity.
        if (unwrappedA.SpecialType is AliasSemanticType aliasA && !aliasA.IsMutable)
        {
            if (unwrappedB.SpecialType is AliasSemanticType aliasB)
            {
                if (aliasA.IsMutable == aliasB.IsMutable)
                    return ParameterTypesMatch(aliasA.TargetType, genericParamsA, aliasB.TargetType, genericParamsB, null, null);
                return false;
            }
            return ParameterTypesMatch(aliasA.TargetType, genericParamsA, unwrappedB, genericParamsB, null, null);
        }

        if (unwrappedB.SpecialType is AliasSemanticType aliasB2 && !aliasB2.IsMutable)
        {
            return ParameterTypesMatch(unwrappedA, genericParamsA, aliasB2.TargetType, genericParamsB, null, null);
        }

        if (unwrappedA.IsSpecial || unwrappedB.IsSpecial)
        {
            if (unwrappedA.Kind != unwrappedB.Kind)
                return false;

            return (unwrappedA.SpecialType, unwrappedB.SpecialType) switch
            {
                (PointerSemanticType pA, PointerSemanticType pB) =>
                    pA.IsMutable == pB.IsMutable && ParameterTypesMatch(pA.ElementType, genericParamsA, pB.ElementType, genericParamsB, null, null),
                (ReferenceSemanticType rA, ReferenceSemanticType rB) =>
                    rA.IsMutable == rB.IsMutable && ParameterTypesMatch(rA.ElementType, genericParamsA, rB.ElementType, genericParamsB, null, null),
                (SliceSemanticType sA, SliceSemanticType sB) =>
                    sA.Dimension == sB.Dimension && ParameterTypesMatch(sA.ElementType, genericParamsA, sB.ElementType, genericParamsB, null, null),
                (ArraySemanticType aA, ArraySemanticType aB) =>
                    aA.Dimension == aB.Dimension &&
                    ParameterTypesMatch(aA.ElementType, genericParamsA, aB.ElementType, genericParamsB, null, null) &&
                    (aA.Size == aB.Size || Nullable.Equals(aA.Size.GetSpan(), aB.Size.GetSpan())),
                (OptionalSemanticType oA, OptionalSemanticType oB) =>
                    ParameterTypesMatch(oA.ElementType, genericParamsA, oB.ElementType, genericParamsB, null, null),
                (TupleSemanticType tA, TupleSemanticType tB) when tA.Elements.Count == tB.Elements.Count =>
                    tA.Elements.Zip(tB.Elements).All(pair =>
                        ParameterTypesMatch(pair.First.Type, genericParamsA, pair.Second.Type, genericParamsB, null, null) &&
                        string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal)),
                (AliasSemanticType alA, AliasSemanticType alB) =>
                    alA.IsMutable == alB.IsMutable && ParameterTypesMatch(alA.TargetType, genericParamsA, alB.TargetType, genericParamsB, null, null),
                _ => unwrappedA.Equals(unwrappedB)
            };
        }

        if (unwrappedA.IsResolved && unwrappedB.IsResolved)
        {
            var targetA = context.GetType(unwrappedA) ?? unwrappedA.Handle;
            var targetB = context.GetType(unwrappedB) ?? unwrappedB.Handle;

            return targetA == targetB;
        }

        if (syntaxA?.Declarator.Type is { } sTypeA && syntaxB?.Declarator.Type is { } sTypeB)
        {
            var textA = sTypeA.GetSpan() is { } spA && sTypeA.GetSource() is { } srcA ? srcA.ToString(spA) : null;
            var textB = sTypeB.GetSpan() is { } spB && sTypeB.GetSource() is { } srcB ? srcB.ToString(spB) : null;

            if (textA != null && textB != null)
                return string.Equals(textA, textB, StringComparison.Ordinal);
        }

        return typeA == typeB;
    }

    private void CheckDuplicateVariables()
    {
        // 1. Global variables
        var globalGroups = new Dictionary<(NamespaceTrieNode?, SymbolPart), List<GlobalVariableSymbol>>();

        foreach (var global in context.GlobalVariableSymbols)
        {
            if (global.Name.Text == "_")
                continue;

            var key = (global.ContainingNamespace, global.Name);

            if (!globalGroups.TryGetValue(key, out var list))
            {
                list = [];
                globalGroups[key] = list;
            }

            list.Add(global);
        }

        foreach (var (_, group) in globalGroups)
        {
            if (group.Count <= 1)
                continue;

            var first = group[0];
            var firstSpan = GetVariableIdentifierSpan(first.Syntax, first.Name);

            for (int i = 1; i < group.Count; i++)
            {
                var redecl = group[i];
                var redeclSpan = GetVariableIdentifierSpan(redecl.Syntax, redecl.Name);
                context.Diagnostics.ReportDuplicateVariableDeclaration(
                    redecl.Name.ToString(),
                    redeclSpan,
                    firstSpan,
                    redecl.Syntax?.GetSource(),
                    first.Syntax?.GetSource());
            }
        }

        // 2. Fields in product types
        var fieldGroups = new Dictionary<(SymbolHandle?, SymbolPart), List<FieldSymbol>>();

        foreach (var field in context.FieldSymbols)
        {
            if (field.Name.Text == "_")
                continue;

            var key = (field.Parent, field.Name);
            if (!fieldGroups.TryGetValue(key, out var list))
            {
                list = [];
                fieldGroups[key] = list;
            }

            list.Add(field);
        }

        foreach (var (_, group) in fieldGroups)
        {
            if (group.Count <= 1)
                continue;

            var first = group[0];
            var firstSpan = GetVariableIdentifierSpan(first.Syntax, first.Name);

            for (int i = 1; i < group.Count; i++)
            {
                var redecl = group[i];
                var redeclSpan = GetVariableIdentifierSpan(redecl.Syntax, redecl.Name);
                context.Diagnostics.ReportDuplicateVariableDeclaration(
                    redecl.Name.ToString(),
                    redeclSpan,
                    firstSpan,
                    redecl.Syntax?.GetSource(),
                    first.Syntax?.GetSource());
            }
        }
    }

    private TextSpan GetVariableIdentifierSpan(VariableDeclaration? syntax, SymbolPart name)
    {
        if (syntax != null)
        {
            var declarator = syntax.Declarator;
            if (declarator.Identifier is TupleName tupleName)
            {
                foreach (var elem in tupleName.Elements)
                    if (context.GetScopedSymbolName(elem).Last == name)
                        return elem.GetSpan() ?? declarator.GetSpan() ?? syntax.GetSpan() ?? default;
            }
            else if (context.GetScopedSymbolName(declarator.Identifier).Last == name)
                return declarator.Identifier.GetSpan() ?? declarator.GetSpan() ?? syntax.GetSpan() ?? default;

            return syntax.GetSpan() ?? default;
        }

        return default;
    }

    private void CheckDuplicateProperties()
    {
        var propGroups = new Dictionary<(Scope, SymbolPart), List<PropertySymbol>>();

        foreach (var prop in context.PropertySymbols)
        {
            if (prop.Name.Text == "_")
                continue;

            var key = (prop.EnclosingScope, prop.Name);

            if (!propGroups.TryGetValue(key, out var list))
            {
                list = [];
                propGroups[key] = list;
            }

            list.Add(prop);
        }

        foreach (var (_, group) in propGroups)
        {
            if (group.Count <= 1)
                continue;

            var first = group[0];
            var firstSpan = first.Syntax?.Identifier.GetSpan() ?? first.Syntax?.GetSpan() ?? default;

            for (int i = 1; i < group.Count; i++)
            {
                var redecl = group[i];
                var redeclSpan = redecl.Syntax?.Identifier.GetSpan() ?? redecl.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportDuplicatePropertyDeclaration(
                    redecl.Name.ToString(),
                    redeclSpan,
                    firstSpan,
                    redecl.Syntax?.GetSource(),
                    first.Syntax?.GetSource());
            }
        }
    }

    private void ValidateMainEntryPoints()
    {
        // 1. Ensure 'Main' is not declared on any type (method)
        foreach (var m in context.MethodSymbols)
        {
            if (m.Name.Text == "Main" && m.Syntax is not null)
            {
                var span = m.Syntax.Signature.Identifier.GetSpan() ?? m.Syntax.GetSpan() ?? default;
                context.Diagnostics.ReportMainMustBeTopLevel(span, m.Syntax.GetSource());
            }
        }

        // 2. Discover and validate top-level Main functions
        var validMainCandidates = new List<FunctionSymbol>();

        foreach (var fn in context.FunctionSymbols)
        {
            if (fn.Name.Text != "Main")
                continue;

            // Synthetic top-level statements Main (syntax is null)
            if (fn.Syntax is null)
                continue;

            var span = fn.Syntax.Signature.Identifier.GetSpan() ?? fn.Syntax.GetSpan() ?? default;
            var source = fn.Syntax.GetSource();

            // Must not be generic
            if (fn.GenericParameters.Count > 0)
            {
                context.Diagnostics.ReportInvalidMainSignature(span, source);
                continue;
            }

            // Return type must be void (Unit) or int32 (Int32)
            bool isValidReturn = IsVoidType(fn.ReturnType) || IsInt32Type(fn.ReturnType);
            if (!isValidReturn)
            {
                context.Diagnostics.ReportInvalidMainSignature(span, source);
                continue;
            }

            // Parameters: 0 or 1 parameter of Array<Str> / Span<Str>
            if (fn.Parameters.Count == 0)
            {
                validMainCandidates.Add(fn);
            }
            else if (fn.Parameters.Count == 1)
            {
                var param = context.GetParameterSymbol(fn.Parameters[0]);
                var paramType = param?.Type ?? TypeRef.Error;
                if (IsValidStringCollectionParam(paramType))
                {
                    validMainCandidates.Add(fn);
                }
                else
                {
                    context.Diagnostics.ReportInvalidMainSignature(span, source);
                }
            }
            else
            {
                context.Diagnostics.ReportInvalidMainSignature(span, source);
            }
        }

        // 3. Disambiguate entry point
        if (validMainCandidates.Count == 1)
        {
            context.EntryPoint = ResolutionContext.GetHandle(validMainCandidates[0]);
        }
        else if (validMainCandidates.Count > 1)
        {
            var entryFileOption = context.Options.EntryFile;
            if (entryFileOption is not null)
            {
                var matching = validMainCandidates.Where(fn => MatchesEntryFile(fn, entryFileOption)).ToList();
                if (matching.Count == 1)
                {
                    context.EntryPoint = ResolutionContext.GetHandle(matching[0]);
                }
                else if (matching.Count > 1)
                {
                    var span = matching[0].Syntax?.Signature.Identifier.GetSpan() ?? default;
                    context.Diagnostics.ReportAmbiguousMainEntryPoint(
                        span,
                        $"ambiguous entry point: multiple 'Main' functions found in entry file '{entryFileOption}'",
                        matching[0].Syntax?.GetSource());
                }
                else
                {
                    var span = validMainCandidates[0].Syntax?.Signature.Identifier.GetSpan() ?? default;
                    context.Diagnostics.ReportAmbiguousMainEntryPoint(
                        span,
                        $"entry file '{entryFileOption}' does not declare a valid 'Main' entry point",
                        validMainCandidates[0].Syntax?.GetSource());
                }
            }
            else
            {
                var span = validMainCandidates[0].Syntax?.Signature.Identifier.GetSpan() ?? default;
                context.Diagnostics.ReportAmbiguousMainEntryPoint(
                    span,
                    "ambiguous entry point: multiple valid 'Main' functions found across compilation units; specify an entry file using the EntryFile configuration",
                    validMainCandidates[0].Syntax?.GetSource());
            }
        }
    }

    private bool IsVoidType(TypeRef t) => t.IsEmptyTuple;

    private bool IsInt32Type(TypeRef t)
    {
        if (t.Handle is { } h)
        {
            var unwrapped = context.UnwrapAlias(h);
            if (context.DiscoveredIntrinsics.TryGetIntrinsicName(unwrapped, out var intrinsicName) &&
                (intrinsicName == KnownIntrinsics.Int32 || intrinsicName == "int32"))
                return true;
            if (context.GetTypeSymbol(unwrapped) is { } ts &&
                (ts.Name.Text == "Int32" || ts.Name.Text == "int32" || ts.Name.Text == "int"))
                return true;
        }
        return false;
    }

    private bool IsValidStringCollectionParam(TypeRef paramType)
    {
        TypeRef inner = paramType.SpecialType is DynSemanticType d ? d.TargetType : paramType;

        TypeRef? collectionTarget = inner.SpecialType switch
        {
            AliasSemanticType al => al.TargetType,
            ReferenceSemanticType rf => rf.ElementType,
            _ => null
        };

        if (collectionTarget is not { } target)
            return false;

        if (target.SpecialType is SliceSemanticType { Dimension: 1 } slice)
            return IsStrAliasType(slice.ElementType);

        if (target.SpecialType is GenericSemanticType gen &&
            context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Slice, out var sliceH) &&
            context.UnwrapAlias(gen.TargetHandle) == sliceH &&
            gen.TypeArguments.Count >= 1)
            return IsStrAliasType(gen.TypeArguments[0]);

        return false;
    }

    private bool IsStrAliasType(TypeRef type) =>
        type.SpecialType is AliasSemanticType al && IsStrType(al.TargetType);

    private bool IsStrType(TypeRef t)
    {
        if (t.Handle is { } h)
        {
            var unwrapped = context.UnwrapAlias(h);
            if (context.DiscoveredIntrinsics.TryGetIntrinsicName(unwrapped, out var name) &&
                (name == KnownIntrinsics.Str || name == "str"))
                return true;
            if (context.GetTypeSymbol(unwrapped) is { } ts &&
                (ts.Name.Text == "Str" || ts.Name.Text == "str"))
                return true;
        }
        return t.SpecialType?.ToString() == "Str" || t.SpecialType?.ToString() == "str";
    }

    private static bool MatchesEntryFile(FunctionSymbol fn, string entryOption)
    {
        var path = fn.Syntax?.GetSource()?.FilePath;
        if (path is null)
            return false;

        return string.Equals(path, entryOption, System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(System.IO.Path.GetFileName(path), entryOption, System.StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateThisGenericParameters()
    {
        foreach (var gp in context.GenericParameterSymbols)
        {
            if (gp.ParameterKind is not GenericParameterKind.This)
                continue;

            bool validOwner = gp.GenericSymbol switch
            {
                TypeSymbol ts => ts.TypeKind == TypeKind.Interface,
                NestedTypeSymbol nts => nts.TypeKind == TypeKind.Interface,
                MethodSymbol => true,
                _ => false
            };

            if (!validOwner)
            {
                var span = gp.Syntax?.ValueKindToken?.Span ?? gp.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportInvalidThisGenericParameterPlacement(span, gp.Name.Text, gp.Syntax?.GetSource());
            }
        }
    }

    private void ValidateGenericParameterDefaults()
    {
        foreach (var gp in context.GenericParameterSymbols)
        {
            if (gp.DefaultType is not { } def || !def.IsResolved)
                continue;

            var span = gp.Syntax?.DefaultType?.GetSpan() ?? gp.Syntax?.GetSpan() ?? default;
            if (gp.ParameterKind is GenericParameterKind.Type or GenericParameterKind.This && gp.RequiresSized(context) && !def.IsSized(context))
            {
                context.Diagnostics.ReportGenericConstraintNotSized(
                    span,
                    def.ToDisplayString(context),
                    gp.Name.ToString(),
                    gp.GenericSymbol.Name.ToString(),
                    "Sized",
                    gp.Syntax?.GetSource());
            }

            if (gp.ParameterKind is GenericParameterKind.Type or GenericParameterKind.This && gp.RequiresUnsized(context) && !def.IsKnownUnsized(context))
            {
                context.Diagnostics.ReportGenericConstraintNotSized(
                    span,
                    def.ToDisplayString(context),
                    gp.Name.ToString(),
                    gp.GenericSymbol.Name.ToString(),
                    "Unsized",
                    gp.Syntax?.GetSource());
            }

            foreach (var constraint in gp.Constraints)
            {
                if (constraint.Handle is { } ch && context.IsBuiltInLayoutInterface(ch))
                    continue;

                if (constraint.Handle is { } trCh &&
                    DeclarationResolutionPass.TrySatisfiesTransferHierarchyConstraint(context, def, trCh, out bool trSatisfied))
                {
                    if (!trSatisfied)
                    {
                        context.Diagnostics.ReportDefaultGenericConstraintViolation(
                            span,
                            gp.Name.ToString(),
                            def.ToDisplayString(context),
                            constraint.ToDisplayString(context),
                            gp.Syntax?.GetSource());
                    }
                    continue;
                }

                if (!constraint.IsAssignableFrom(def, context))
                {
                    context.Diagnostics.ReportDefaultGenericConstraintViolation(
                        span,
                        gp.Name.ToString(),
                        def.ToDisplayString(context),
                        constraint.ToDisplayString(context),
                        gp.Syntax?.GetSource());
                }
                else
                {
                    var lookup = context.ResolveInterfaceConformance(def, constraint, null, gp.EnclosingScope, default);
                    if (lookup.Status == ResolutionContext.ConformanceLookupStatus.Inaccessible)
                    {
                        context.Diagnostics.ReportInaccessibleInterfaceConformance(
                            span,
                            def.ToDisplayString(context),
                            lookup.Conformance!.InterfaceType.ToDisplayString(context),
                            lookup.Conformance.Accessibility.ToString().ToLowerInvariant(),
                            gp.Syntax?.GetSource());
                    }
                    else if (lookup.Status == ResolutionContext.ConformanceLookupStatus.Ambiguous)
                    {
                        context.Diagnostics.ReportAmbiguousInterfaceConformance(
                            span,
                            def.ToDisplayString(context),
                            constraint.ToDisplayString(context),
                            lookup.AmbiguousCandidates!.Select(c => c.ModuleName).Distinct().ToList(),
                            gp.Syntax?.GetSource());
                    }
                }
            }
        }
    }

    private void ValidateTypeLayoutsAndSizedness()
    {
        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Sized, out var sizedHandle);

        foreach (var type in context.TypeSymbols)
        {
            ValidateTypeSymbolLayout(type, ResolutionContext.GetHandle(type), sizedHandle);
        }

        foreach (var nested in context.NestedTypeSymbols)
        {
            ValidateTypeSymbolLayout(nested, ResolutionContext.GetHandle(nested), sizedHandle);
        }

        foreach (var global in context.GlobalVariableSymbols)
        {
            if (!global.Type.IsError && !global.Type.IsUnresolved && !global.Type.IsSized(context))
            {
                var span = global.Syntax?.Type.GetSpan() ?? global.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportUnsizedTypeNotAllowed(span, global.Type.ToDisplayString(context), "as a global variable", global.Syntax?.GetSource());
            }
        }

        foreach (var constant in context.ConstantSymbols)
        {
            if (!constant.Type.IsError && !constant.Type.IsUnresolved && !constant.Type.IsInferred && !constant.Type.IsSized(context))
            {
                var span = constant.Syntax?.Type?.GetSpan() ?? constant.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportUnsizedTypeNotAllowed(span, constant.Type.ToDisplayString(context), "as a constant", constant.Syntax?.GetSource());
            }
        }

        foreach (var fn in context.FunctionSymbols)
        {
            ValidateCallableSizedness(fn.Parameters, fn.ReturnType, fn.Syntax?.Signature.ReturnType, fn.Syntax?.GetSource());
        }

        foreach (var method in context.MethodSymbols)
        {
            ValidateCallableSizedness(method.Parameters, method.ReturnType, method.Syntax?.Signature.ReturnType, method.Syntax?.GetSource());
        }

        foreach (var prop in context.PropertySymbols)
        {
            if (!prop.Type.IsError && !prop.Type.IsUnresolved && !prop.Type.IsSized(context))
            {
                var span = prop.Syntax?.Type.GetSpan() ?? prop.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportUnsizedTypeNotAllowed(span, prop.Type.ToDisplayString(context), "as a property type", prop.Syntax?.GetSource());
            }
        }

        foreach (var attr in context.AttributeSymbols)
            ValidateParameterListSizedness(attr.Parameters, "as an attribute parameter", attr.Syntax?.GetSource());

        foreach (var nestedAttr in context.NestedAttributeSymbols)
            ValidateParameterListSizedness(nestedAttr.Parameters, "as an attribute parameter", nestedAttr.Syntax?.GetSource());
    }

    private void ValidateCallableSizedness(
        IReadOnlyList<SymbolHandle> paramHandles,
        TypeRef returnType,
        TypeSyntax? returnSyntax,
        SourceText? source)
    {
        ValidateParameterListSizedness(paramHandles, "as a parameter", source);

        if (!returnType.IsError && !returnType.IsUnresolved && !returnType.IsSized(context))
        {
            var span = returnSyntax?.GetSpan() ?? default;
            context.Diagnostics.ReportUnsizedTypeNotAllowed(span, returnType.ToDisplayString(context), "as a return type", returnSyntax?.GetSource() ?? source);
        }
    }

    private void ValidateParameterListSizedness(
        IReadOnlyList<SymbolHandle> paramHandles,
        string contextDescription,
        SourceText? fallbackSource)
    {
        foreach (var ph in paramHandles)
        {
            var param = context.GetParameterSymbol(ph);
            if (param is not null && !param.Type.IsError && !param.Type.IsUnresolved && !param.Type.IsSized(context))
            {
                var span = param.Syntax?.Declarator.Type.GetSpan() ?? param.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportUnsizedTypeNotAllowed(span, param.Type.ToDisplayString(context), contextDescription, param.Syntax?.GetSource() ?? fallbackSource);
            }
        }
    }

    private void ValidateTypeSymbolLayout(Symbol typeSym, SymbolHandle handle, SymbolHandle sizedHandle)
    {
        TypeKind kind = typeSym switch
        {
            TypeSymbol ts => ts.TypeKind,
            NestedTypeSymbol nts => nts.TypeKind,
            _ => TypeKind.Struct
        };

        if (typeSym is ISumTypeSymbol sumType)
        {
            foreach (var vh in sumType.EnumVariants)
            {
                if (context.GetSymbol(vh) is EnumVariantSymbol ev)
                {
                    foreach (var ph in ev.Parameters)
                    {
                        var param = context.GetParameterSymbol(ph);
                        if (param is not null && !param.Type.IsError && !param.Type.IsUnresolved && !param.Type.IsSized(context))
                        {
                            var span = param.Syntax?.Declarator.Type.GetSpan() ?? param.Syntax?.GetSpan() ?? ev.Syntax?.GetSpan() ?? default;
                            context.Diagnostics.ReportUnsizedTypeNotAllowed(span, param.Type.ToDisplayString(context), "as an enum variant payload", param.Syntax?.GetSource() ?? ev.Syntax?.GetSource());
                        }
                    }
                    foreach (var fh in ev.Fields)
                    {
                        var f = context.GetFieldSymbol(fh);
                        if (f is not null && !f.Type.IsError && !f.Type.IsUnresolved && !f.Type.IsSized(context))
                        {
                            var span = f.Syntax?.Type.GetSpan() ?? f.Syntax?.GetSpan() ?? ev.Syntax?.GetSpan() ?? default;
                            context.Diagnostics.ReportUnsizedTypeNotAllowed(span, f.Type.ToDisplayString(context), "as an enum variant payload", f.Syntax?.GetSource() ?? ev.Syntax?.GetSource());
                        }
                    }
                }
            }
            return;
        }

        if (kind is not (TypeKind.Struct or TypeKind.Class))
            return;

        IReadOnlyList<TypeRef> baseTypes = typeSym switch
        {
            TypeSymbol ts => ts.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            _ => []
        };

        IReadOnlyList<SymbolHandle> fieldHandles = typeSym switch
        {
            ProductTypeSymbol p => p.Fields,
            MemberProductTypeSymbol mp => mp.Fields,
            _ => []
        };

        var syntax = (typeSym as TypeSymbol)?.Syntax ?? (typeSym as NestedTypeSymbol)?.Syntax;
        var source = syntax?.GetSource();

        var instanceFields = new List<FieldSymbol>();
        foreach (var fh in fieldHandles)
        {
            var field = context.GetFieldSymbol(fh);
            if (field is null) continue;

            if (field.Flags.HasFlag(VariableFlags.Static))
            {
                if (!field.Type.IsError && !field.Type.IsUnresolved && !field.Type.IsSized(context))
                {
                    var span = field.Syntax?.Type.GetSpan() ?? field.Syntax?.GetSpan() ?? default;
                    context.Diagnostics.ReportUnsizedTypeNotAllowed(span, field.Type.ToDisplayString(context), "as a static field", field.Syntax?.GetSource());
                }
            }
            else
            {
                instanceFields.Add(field);
            }
        }

        for (int i = 0; i < instanceFields.Count - 1; i++)
        {
            var field = instanceFields[i];
            if (!field.Type.IsError && !field.Type.IsUnresolved && !field.Type.IsSized(context))
            {
                var span = field.Syntax?.Type.GetSpan() ?? field.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportUnsizedTypeNotAllowed(span, field.Type.ToDisplayString(context), "as a non-tail field (only the final field of a struct may be unsized)", field.Syntax?.GetSource());
            }
        }

        var unsizedBases = new List<TypeRef>();
        foreach (var b in baseTypes)
        {
            if (b.IsError || b.IsUnresolved || b.Handle is not { } bh)
                continue;

            var unwrappedB = context.UnwrapAlias(bh);
            var bSym = context.GetSymbol(unwrappedB);
            TypeKind bKind = bSym switch
            {
                TypeSymbol bts => bts.TypeKind,
                NestedTypeSymbol bnts => bnts.TypeKind,
                _ => TypeKind.Struct
            };

            if (bKind is TypeKind.Interface)
            {
                if (sizedHandle != default &&
                    !TypeRef.Resolved(handle).IsSized(context) &&
                    DeclarationResolutionPass.SatisfiesConstraint(context, unwrappedB, sizedHandle, []))
                {
                    var span = syntax?.Base?.GetSpan() ?? syntax?.Name.GetSpan() ?? default;
                    context.Diagnostics.ReportGenericConstraintNotSized(span, typeSym.Name.ToString(), "Self", b.ToDisplayString(context), "Sized", source);
                }
            }
            else if (!b.IsSized(context))
            {
                unsizedBases.Add(b);
            }
        }

        if (unsizedBases.Count > 1)
        {
            var span = syntax?.Base?.GetSpan() ?? syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportDerivedTypeAddsFieldsAfterUnsizedBase(
                span,
                typeSym.Name.ToString(),
                unsizedBases[1].ToDisplayString(context),
                "a type cannot inherit from multiple unsized base types",
                source);
        }
        else if (unsizedBases.Count == 1 && instanceFields.Count > 0)
        {
            var span = instanceFields[0].Syntax?.GetSpan() ?? syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportDerivedTypeAddsFieldsAfterUnsizedBase(
                span,
                typeSym.Name.ToString(),
                unsizedBases[0].ToDisplayString(context),
                "derived types cannot declare additional instance fields after an unsized base",
                instanceFields[0].Syntax?.GetSource() ?? source);
        }
    }
}
