using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Text;

namespace Maho.Resolution;

/// <summary>
/// Resolution Pass 2: Intrinsic and Operator Discovery.
/// <para>
/// Responsible for:
/// 1. Inspecting imported libraries/projects and merging declared intrinsics and operators.
/// 2. Discovering and validating compiler-recognized attribute declarations (<c>IntrinsicType</c>, <c>OperatorFunction</c>, <c>IntrinsicFunction</c>).
/// 3. Discovering types decorated with <c>[IntrinsicType("...")]</c> and enforcing strict topological ABI layout contracts
///    (e.g., zero instance fields on primitive scalars; pointer/reference requiring <c>nuint</c> field; <c>Str</c> requiring <c>Int8&amp;</c> and <c>Int64</c> length).
/// 4. Discovering and cataloging operator interfaces and their methods in <see cref="DiscoveredOperators"/>.
/// 5. Discovering compiler intrinsic functions.
/// </para>
/// </summary>
internal sealed partial class IntrinsicDiscoveryPass : ResolutionPass
{
    private ResolutionContext context = null!;

    /// <summary>
    /// Executes the intrinsic and operator discovery pass over the compilation context.
    /// </summary>
    /// <param name="context">The shared compilation resolution context.</param>
    public override void Resolve(ResolutionContext context)
    {
        this.context = context;

        // 1. Linked metadata inspection
        ImportReferencedIntrinsics();

        // 2. Discover & retroactively verify IntrinsicType attribute
        var candidateAttr = DiscoverAndValidateIntrinsicAttribute();

        // 3. Discover all types applying IntrinsicType
        DiscoverAndValidateIntrinsicTypes(candidateAttr);

        // 4. Discover and validate operator functions on interfaces
        DiscoverAndValidateOperatorFunctions();

        // 5. Discover and validate intrinsic functions
        DiscoverAndValidateIntrinsicFunctions();
    }

    /// <summary>
    /// Imports intrinsic attributes, types, and operator registrations from referenced external projects.
    /// </summary>
    private void ImportReferencedIntrinsics()
    {
        foreach (var project in context.ReferencedProjects)
        {
            if (context.DiscoveredIntrinsics.IntrinsicTypeAttribute is null && project.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null)
                context.DiscoveredIntrinsics.IntrinsicTypeAttribute = project.DiscoveredIntrinsics.IntrinsicTypeAttribute;

            foreach (var (name, handle) in project.DiscoveredIntrinsics.All)
            {
                project.DiscoveredIntrinsics.TryGetDeclarationInfo(name, out var span, out var source);

                if (context.DiscoveredIntrinsics.TryGet(name, out var existingHandle))
                {
                    if (existingHandle != handle)
                    {
                        context.DiscoveredIntrinsics.TryGetDeclarationInfo(name, out var existingSpan, out var existingSource);
                        context.Diagnostics.ReportDuplicateIntrinsicType(name, span, existingSpan, source, existingSource);
                    }
                    continue;
                }

                context.DiscoveredIntrinsics.Register(name, handle, span, source);
            }

            if (context.DiscoveredOperators.OperatorFunctionAttribute is null && project.DiscoveredOperators.OperatorFunctionAttribute is not null)
                context.DiscoveredOperators.OperatorFunctionAttribute = project.DiscoveredOperators.OperatorFunctionAttribute;

            if (context.DiscoveredOperators.IntrinsicFunctionAttribute is null && project.DiscoveredOperators.IntrinsicFunctionAttribute is not null)
                context.DiscoveredOperators.IntrinsicFunctionAttribute = project.DiscoveredOperators.IntrinsicFunctionAttribute;

            foreach (var (opKind, list) in project.DiscoveredOperators.AllByKind)
            {
                foreach (var op in list)
                    context.DiscoveredOperators.Register(op.Kind, op.InterfaceHandle, op.MethodHandle, op.Span, op.Source);
            }
        }
    }

    private AttributeSymbol? DiscoverAndValidateIntrinsicAttribute()
    {
        AttributeSymbol? candidateAttr = null;

        // Check all attribute symbols (and nested)
        var allAttributes = context.AttributeSymbols.Cast<Symbol>().Concat(context.NestedAttributeSymbols);

        foreach (var symbol in allAttributes)
        {
            if (symbol is not AttributeSymbol attr)
                continue;

            bool isMarkedIntrinsic = attr.Flags.HasFlag(AttributeFlags.Intrinsic);

            if (isMarkedIntrinsic &&
                attr.Name.Text != KnownIntrinsics.IntrinsicTypeAttribute &&
                attr.Name.Text != KnownIntrinsics.OperatorFunctionAttribute &&
                attr.Name.Text != KnownIntrinsics.IntrinsicFunctionAttribute)
            {
                var span = attr.Syntax?.Name.GetSpan() ?? default;
                context.Diagnostics.ReportUnknownIntrinsicAttribute(span, attr.Name.Text, attr.Syntax?.GetSource());
                continue;
            }

            if (attr.Name.Text == KnownIntrinsics.OperatorFunctionAttribute)
            {
                int paramCount = attr.Syntax?.Parameters?.Parameters.Count ?? 0;
                if (paramCount != 1)
                {
                    if (isMarkedIntrinsic || paramCount > 0)
                    {
                        var span = attr.Syntax?.GetSpan() ?? default;
                        context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(span, "must accept exactly one parameter", attr.Syntax?.GetSource(), KnownIntrinsics.OperatorFunctionAttribute);
                    }
                    continue;
                }

                context.DiscoveredOperators.OperatorFunctionAttribute = attr;
            }
            else if (attr.Name.Text == KnownIntrinsics.IntrinsicFunctionAttribute)
            {
                int paramCount = attr.Syntax?.Parameters?.Parameters.Count ?? 0;
                if (paramCount != 1)
                {
                    if (isMarkedIntrinsic || paramCount > 0)
                    {
                        var span = attr.Syntax?.GetSpan() ?? default;
                        context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(span, "must accept exactly one parameter", attr.Syntax?.GetSource(), KnownIntrinsics.IntrinsicFunctionAttribute);
                    }
                    continue;
                }

                context.DiscoveredOperators.IntrinsicFunctionAttribute = attr;
            }
            else if (attr.Name.Text == KnownIntrinsics.IntrinsicTypeAttribute)
            {
                if (context.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null && context.DiscoveredIntrinsics.IntrinsicTypeAttribute != attr)
                {
                    var redeclSpan = attr.Syntax?.Name.GetSpan() ?? default;
                    var firstSpan = context.DiscoveredIntrinsics.IntrinsicTypeAttribute.Syntax?.Name.GetSpan() ?? default;
                    context.Diagnostics.ReportDuplicateDeclaration("attribute", attr.Name.Text, redeclSpan, firstSpan, attr.Syntax?.GetSource(), context.DiscoveredIntrinsics.IntrinsicTypeAttribute.Syntax?.GetSource());
                    continue;
                }

                if (candidateAttr is not null && candidateAttr != attr)
                {
                    var redeclSpan = attr.Syntax?.Name.GetSpan() ?? default;
                    var firstSpan = candidateAttr.Syntax?.Name.GetSpan() ?? default;
                    context.Diagnostics.ReportDuplicateDeclaration("attribute", attr.Name.Text, redeclSpan, firstSpan, attr.Syntax?.GetSource(), candidateAttr.Syntax?.GetSource());
                    continue;
                }

                int paramCount = attr.Syntax?.Parameters?.Parameters.Count ?? 0;

                if (paramCount != 1)
                {
                    if (isMarkedIntrinsic || paramCount > 0)
                    {
                        var span = attr.Syntax?.GetSpan() ?? default;
                        context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(span, "must accept exactly one parameter", attr.Syntax?.GetSource());
                    }

                    continue;
                }

                candidateAttr = attr;
            }
        }

        if (candidateAttr is null)
            return context.DiscoveredIntrinsics.IntrinsicTypeAttribute;

        // Find candidate Str type applying [IntrinsicType("Str")]
        var strCandidates = FindTypesApplyingIntrinsic(KnownIntrinsics.Str, candidateAttr);

        if (strCandidates.Count > 1)
        {
            var first = strCandidates[0];
            var second = strCandidates[1];
            context.Diagnostics.ReportDuplicateIntrinsicType(
                KnownIntrinsics.Str,
                GetSyntax(second.Symbol)?.GetSpan() ?? default,
                GetSyntax(first.Symbol)?.GetSpan() ?? default,
                GetSyntax(second.Symbol)?.GetSource(),
                GetSyntax(first.Symbol)?.GetSource());

            return null;
        }

        if (strCandidates.Count == 0)
        {
            // If IntrinsicType was marked with 'intrinsic' modifier, its parameter cannot be verified
            if (candidateAttr.Flags.HasFlag(AttributeFlags.Intrinsic))
            {
                var span = candidateAttr.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                    span,
                    "primitive 'Str' type marked with [IntrinsicType(\"Str\")] was not found",
                    candidateAttr.Syntax?.GetSource());
            }

            return null;
        }

        var candidateStr = strCandidates[0];
        var candidateStrHandle = ResolutionContext.GetHandle(candidateStr.Symbol);

        // Retroactively verify candidateAttr's parameter type matches candidateStr
        if (candidateAttr.Parameters.Count == 1)
        {
            var paramSymbol = context.GetParameterSymbol(candidateAttr.Parameters[0]);
            var paramTypeSyntax = paramSymbol?.Syntax?.Declarator.Type;
            var resolvedParamHandle = ResolveTypeSymbol(paramTypeSyntax, candidateAttr.EnclosingScope);

            if (resolvedParamHandle != candidateStrHandle)
            {
                var span = candidateAttr.Syntax?.Parameters?.Parameters[0].GetSpan() ?? candidateAttr.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                    span,
                    $"parameter type must match the primitive 'Str' type marked with [IntrinsicType(\"Str\")]",
                    candidateAttr.Syntax?.GetSource());

                return null;
            }
        }

        if (context.DiscoveredOperators.OperatorFunctionAttribute is { Parameters.Count: 1 } opAttr)
        {
            var paramSymbol = context.GetParameterSymbol(opAttr.Parameters[0]);
            var paramTypeSyntax = paramSymbol?.Syntax?.Declarator.Type;
            var resolvedParamHandle = ResolveTypeSymbol(paramTypeSyntax, opAttr.EnclosingScope);

            if (resolvedParamHandle != candidateStrHandle)
            {
                var span = opAttr.Syntax?.Parameters?.Parameters[0].GetSpan() ?? opAttr.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                    span,
                    $"parameter type must match the primitive 'Str' type marked with [IntrinsicType(\"Str\")]",
                    opAttr.Syntax?.GetSource(),
                    KnownIntrinsics.OperatorFunctionAttribute);
            }
        }

        if (context.DiscoveredOperators.IntrinsicFunctionAttribute is { } ifnAttr)
        {
            if (ifnAttr.Parameters.Count != 1)
            {
                var span = ifnAttr.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                    span,
                    "must accept exactly one parameter",
                    ifnAttr.Syntax?.GetSource(),
                    KnownIntrinsics.IntrinsicFunctionAttribute);
            }
            else
            {
                var paramSymbol = context.GetParameterSymbol(ifnAttr.Parameters[0]);
                var paramTypeSyntax = paramSymbol?.Syntax?.Declarator.Type;
                var resolvedParamHandle = ResolveTypeSymbol(paramTypeSyntax, ifnAttr.EnclosingScope);

                if (resolvedParamHandle != candidateStrHandle)
                {
                    var span = ifnAttr.Syntax?.Parameters?.Parameters[0].GetSpan() ?? ifnAttr.Syntax?.GetSpan() ?? default;
                    context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                        span,
                        $"parameter type must match the primitive 'Str' type marked with [IntrinsicType(\"Str\")]",
                        ifnAttr.Syntax?.GetSource(),
                        KnownIntrinsics.IntrinsicFunctionAttribute);
                }
            }
        }

        context.DiscoveredIntrinsics.IntrinsicTypeAttribute = candidateAttr;
        return candidateAttr;
    }

    private void DiscoverAndValidateIntrinsicTypes(AttributeSymbol? intrinsicAttr)
    {
        if (intrinsicAttr is null)
            return;

        // Collect all type declarations that apply an intrinsic attribute
        var applications = CollectIntrinsicApplications(intrinsicAttr);

        // Track declared intrinsic types in current compilation
        var typesByIntrinsic = new Dictionary<string, List<(Symbol Symbol, TextSpan Span, SourceText? Source)>>(StringComparer.Ordinal);

        foreach (var app in applications)
        {
            if (!KnownIntrinsics.AllKnownTypes.Contains(app.IntrinsicLiteral))
            {
                context.Diagnostics.ReportUnknownIntrinsicType(app.ArgumentSpan, app.IntrinsicLiteral, app.Source);
                continue;
            }

            if (!typesByIntrinsic.TryGetValue(app.IntrinsicLiteral, out var list))
            {
                list = [];
                typesByIntrinsic[app.IntrinsicLiteral] = list;
            }

            list.Add((app.Symbol, app.Span, app.Source));
        }

        // Check for duplicates
        foreach (var (name, list) in typesByIntrinsic)
        {
            if (context.DiscoveredIntrinsics.Contains(name))
            {
                context.DiscoveredIntrinsics.TryGetDeclarationInfo(name, out var existingSpan, out var existingSource);

                foreach (var entry in list)
                    context.Diagnostics.ReportDuplicateIntrinsicType(name, entry.Span, existingSpan, entry.Source, existingSource);

                continue;
            }

            if (list.Count > 1)
            {
                var first = list[0];

                for (int i = 1; i < list.Count; i++)
                {
                    var redecl = list[i];
                    context.Diagnostics.ReportDuplicateIntrinsicType(name, redecl.Span, first.Span, redecl.Source, first.Source);
                }
            }
        }

        // Topological ABI verification:
        // Group 1: Zero-instance-field primitives
        foreach (var name in KnownIntrinsics.ZeroInstanceFieldPrimitives)
        {
            if (typesByIntrinsic.TryGetValue(name, out var list) && list.Count == 1 && !context.DiscoveredIntrinsics.Contains(name))
            {
                var entry = list[0];

                if (VerifyZeroFieldPrimitiveContract(entry.Symbol, name))
                    context.DiscoveredIntrinsics.Register(name, ResolutionContext.GetHandle(entry.Symbol), entry.Span, entry.Source);
            }
        }

        // Group 2: Pointer and Reference (requires NUInt)
        ValidatePointerLike(KnownIntrinsics.Pointer, typesByIntrinsic);
        ValidatePointerLike(KnownIntrinsics.MutablePointer, typesByIntrinsic);
        ValidatePointerLike(KnownIntrinsics.Reference, typesByIntrinsic);
        ValidatePointerLike(KnownIntrinsics.MutableReference, typesByIntrinsic);

        // Group 3: Str (requires Int8, Reference, NUInt)
        ValidateStr(typesByIntrinsic);

        // Group 4: Array (requires Pointer, NUInt) and Span (requires Reference, NUInt)
        ValidateArray(typesByIntrinsic);
        ValidateFixedArray(typesByIntrinsic);
        ValidateSpan(typesByIntrinsic);
        ValidateBox(typesByIntrinsic);

        // Group 5: Function (requires NUInt) and Delegate (requires NUInt, Pointer)
        ValidateFunction(typesByIntrinsic);
        ValidateDelegate(typesByIntrinsic);

        // Group 6: Optional
        ValidateOptional(typesByIntrinsic);
    }
}
