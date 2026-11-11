using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

internal sealed class IntrinsicDiscoveryPass : ResolutionPass
{
    private ResolutionContext context = null!;

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

    private void ImportReferencedIntrinsics()
    {
        foreach (var project in context.ReferencedProjects)
        {
            if (context.DiscoveredIntrinsics.IntrinsicTypeAttribute is null && project.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null)
                context.DiscoveredIntrinsics.IntrinsicTypeAttribute = project.DiscoveredIntrinsics.IntrinsicTypeAttribute;

            foreach (var (name, handle) in project.DiscoveredIntrinsics.All)
            {
                project.DiscoveredIntrinsics.TryGetDeclarationInfo(name, out var span, out var source);

                if (context.DiscoveredIntrinsics.Contains(name))
                {
                    context.DiscoveredIntrinsics.TryGetDeclarationInfo(name, out var existingSpan, out var existingSource);
                    context.Diagnostics.ReportDuplicateIntrinsicType(name, span, existingSpan, source, existingSource);
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
                if (paramCount > 1)
                {
                    if (isMarkedIntrinsic || paramCount > 0)
                    {
                        var span = attr.Syntax?.GetSpan() ?? default;
                        context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(span, "must accept at most one parameter", attr.Syntax?.GetSource(), KnownIntrinsics.IntrinsicFunctionAttribute);
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

        if (context.DiscoveredOperators.IntrinsicFunctionAttribute is { Parameters.Count: 1 } ifnAttr)
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
        ValidatePointerLike(KnownIntrinsics.Reference, typesByIntrinsic);

        // Group 3: Str (requires Int8, Reference, Int64)
        ValidateStr(typesByIntrinsic);

        // Group 4: Array (requires Pointer, Int64) and Span (requires Reference, Int64)
        ValidateArray(typesByIntrinsic);
        ValidateSpan(typesByIntrinsic);
    }

    private bool VerifyZeroFieldPrimitiveContract(Symbol symbol, string intrinsicName)
    {
        var syntax = GetSyntax(symbol);
        var instanceFields = GetInstanceFields(symbol);

        if (instanceFields.Count > 0)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, intrinsicName, "must have zero instance fields", syntax?.GetSource());
            return false;
        }

        if (intrinsicName != KnownIntrinsics.FixedArray && GetGenericParameterCount(symbol) > 0)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, intrinsicName, "cannot have generic parameters", syntax?.GetSource());
            return false;
        }

        return true;
    }

    private void ValidatePointerLike(string intrinsicName, Dictionary<string, List<(Symbol Symbol, TextSpan Span, SourceText? Source)>> typesByIntrinsic)
    {
        if (!typesByIntrinsic.TryGetValue(intrinsicName, out var list) || list.Count != 1 || context.DiscoveredIntrinsics.Contains(intrinsicName))
            return;

        var entry = list[0];
        var symbol = entry.Symbol;
        var syntax = GetSyntax(symbol);

        if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.NUInt))
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, intrinsicName, $"requires intrinsic '{KnownIntrinsics.NUInt}' to be declared first", syntax?.GetSource());
            return;
        }

        if (GetGenericParameterCount(symbol) != 1)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, intrinsicName, "must have exactly 1 generic parameter", syntax?.GetSource());
            return;
        }

        var instanceFields = GetInstanceFields(symbol);
        if (instanceFields.Count != 1)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, intrinsicName, "must have exactly 1 instance field", syntax?.GetSource());
            return;
        }

        var fieldSymbol = instanceFields[0];
        var fieldType = fieldSymbol.Syntax?.Type;
        var resolvedFieldHandle = ResolveTypeSymbol(fieldType, fieldSymbol.EnclosingScope);

        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.NUInt, out var nuintHandle);

        if (resolvedFieldHandle != nuintHandle)
        {
            var span = fieldSymbol.Syntax?.Type.GetSpan() ?? fieldSymbol.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, intrinsicName, $"instance field must be of type '{KnownIntrinsics.NUInt}'", fieldSymbol.Syntax?.GetSource());
            return;
        }

        context.DiscoveredIntrinsics.Register(intrinsicName, ResolutionContext.GetHandle(symbol), entry.Span, entry.Source);
    }

    private void ValidateStr(Dictionary<string, List<(Symbol Symbol, TextSpan Span, SourceText? Source)>> typesByIntrinsic)
    {
        if (!typesByIntrinsic.TryGetValue(KnownIntrinsics.Str, out var list) || list.Count != 1 || context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Str))
            return;

        var entry = list[0];
        var symbol = entry.Symbol;
        var syntax = GetSyntax(symbol);

        if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Int8) ||
            !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Reference) ||
            !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Int64))
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Str, $"requires '{KnownIntrinsics.Int8}', '{KnownIntrinsics.Reference}', and '{KnownIntrinsics.Int64}' to be declared first", syntax?.GetSource());
            return;
        }

        if (GetGenericParameterCount(symbol) != 0)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Str, "cannot have generic parameters", syntax?.GetSource());
            return;
        }

        var instanceFields = GetInstanceFields(symbol);

        if (instanceFields.Count != 2)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Str, "must have exactly 2 instance fields (Int8& and Int64)", syntax?.GetSource());
            return;
        }

        // Field 1: Int8& or Reference<Int8>
        var field1 = instanceFields[0];
        var field1Type = field1.Syntax?.Type;
        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int8, out var int8Handle);

        if (!IsReferenceTo(field1Type, field1.EnclosingScope, int8Handle))
        {
            var span = field1.Syntax?.Type.GetSpan() ?? field1.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Str, "first field must be a reference type pointing to 'Int8' (e.g. Int8& or Reference<Int8>)", field1.Syntax?.GetSource());
            return;
        }

        // Field 2: Int64
        var field2 = instanceFields[1];
        var field2Type = field2.Syntax?.Type;
        var field2Handle = ResolveTypeSymbol(field2Type, field2.EnclosingScope);
        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int64, out var int64Handle);

        if (field2Handle != int64Handle)
        {
            var span = field2Type?.GetSpan() ?? field2.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Str, $"second field must be of type '{KnownIntrinsics.Int64}'", field2.Syntax?.GetSource());
            return;
        }

        context.DiscoveredIntrinsics.Register(KnownIntrinsics.Str, ResolutionContext.GetHandle(symbol), entry.Span, entry.Source);
    }

    private void ValidateArray(Dictionary<string, List<(Symbol Symbol, TextSpan Span, SourceText? Source)>> typesByIntrinsic)
    {
        if (!typesByIntrinsic.TryGetValue(KnownIntrinsics.Array, out var list) || list.Count != 1 || context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Array))
            return;

        var entry = list[0];
        var symbol = entry.Symbol;
        var syntax = GetSyntax(symbol);

        if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Pointer) ||
            !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Int64))
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Array, $"requires '{KnownIntrinsics.Pointer}' and '{KnownIntrinsics.Int64}' to be declared first", syntax?.GetSource());
            return;
        }

        if (GetGenericParameterCount(symbol) != 1)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Array, "must have exactly 1 generic parameter", syntax?.GetSource());
            return;
        }

        var instanceFields = GetInstanceFields(symbol);
        if (instanceFields.Count != 2)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Array, "must have exactly 2 instance fields (T* and Int64)", syntax?.GetSource());
            return;
        }

        var genericParamHandle = GetFirstGenericParameter(symbol);
        var genericParamSymbol = genericParamHandle is { } gph ? context.GetGenericParameterSymbol(gph) : null;

        if (genericParamSymbol is null)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Array, "generic parameter could not be resolved", syntax?.GetSource());
            return;
        }

        // Field 1: T* or Pointer<T>
        var field1 = instanceFields[0];
        var field1Type = field1.Syntax?.Type;
        var genericParamTargetHandle = ResolutionContext.GetHandle(genericParamSymbol);

        if (!IsPointerTo(field1Type, field1.EnclosingScope, genericParamTargetHandle, genericParamSymbol))
        {
            var span = field1.Syntax?.Type.GetSpan() ?? field1.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Array, "first field must be a pointer type pointing to generic parameter 'T' (e.g. T* or Pointer<T>)", field1.Syntax?.GetSource());
            return;
        }

        // Field 2: Int64
        var field2 = instanceFields[1];
        var field2Type = field2.Syntax?.Type;
        var field2Handle = ResolveTypeSymbol(field2Type, field2.EnclosingScope);
        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int64, out var int64Handle);

        if (field2Handle != int64Handle)
        {
            var span = field2Type?.GetSpan() ?? field2.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Array, $"second field must be of type '{KnownIntrinsics.Int64}'", field2.Syntax?.GetSource());
            return;
        }

        context.DiscoveredIntrinsics.Register(KnownIntrinsics.Array, ResolutionContext.GetHandle(symbol), entry.Span, entry.Source);
    }

    private void ValidateSpan(Dictionary<string, List<(Symbol Symbol, TextSpan Span, SourceText? Source)>> typesByIntrinsic)
    {
        if (!typesByIntrinsic.TryGetValue(KnownIntrinsics.Span, out var list) || list.Count != 1 || context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Span))
            return;

        var entry = list[0];
        var symbol = entry.Symbol;
        var syntax = GetSyntax(symbol);

        if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Reference) ||
            !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Int64))
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Span, $"requires '{KnownIntrinsics.Reference}' and '{KnownIntrinsics.Int64}' to be declared first", syntax?.GetSource());
            return;
        }

        if (GetGenericParameterCount(symbol) != 1)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Span, "must have exactly 1 generic parameter", syntax?.GetSource());
            return;
        }

        var instanceFields = GetInstanceFields(symbol);
        if (instanceFields.Count != 2)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Span, "must have exactly 2 instance fields (T& and Int64)", syntax?.GetSource());
            return;
        }

        var genericParamHandle = GetFirstGenericParameter(symbol);
        var genericParamSymbol = genericParamHandle is { } gph ? context.GetGenericParameterSymbol(gph) : null;

        if (genericParamSymbol is null)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Span, "generic parameter could not be resolved", syntax?.GetSource());
            return;
        }

        // Field 1: T& or Reference<T>
        var field1 = instanceFields[0];
        var field1Type = field1.Syntax?.Type;
        var genericParamTargetHandle = ResolutionContext.GetHandle(genericParamSymbol);

        if (!IsReferenceTo(field1Type, field1.EnclosingScope, genericParamTargetHandle, genericParamSymbol))
        {
            var span = field1.Syntax?.Type.GetSpan() ?? field1.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Span, "first field must be a reference type pointing to generic parameter 'T' (e.g. T& or Reference<T>)", field1.Syntax?.GetSource());
            return;
        }

        // Field 2: Int64
        var field2 = instanceFields[1];
        var field2Type = field2.Syntax?.Type;
        var field2Handle = ResolveTypeSymbol(field2Type, field2.EnclosingScope);
        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int64, out var int64Handle);

        if (field2Handle != int64Handle)
        {
            var span = field2Type?.GetSpan() ?? field2.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Span, $"second field must be of type '{KnownIntrinsics.Int64}'", field2.Syntax?.GetSource());
            return;
        }

        context.DiscoveredIntrinsics.Register(KnownIntrinsics.Span, ResolutionContext.GetHandle(symbol), entry.Span, entry.Source);
    }

    private bool IsReferenceTo(TypeSyntax? syntax, Scope scope, SymbolHandle expectedTargetHandle, GenericParameterSymbol? genericParam = null)
    {
        if (syntax is null)
            return false;

        // Case 1: T& syntax (ModifiedType with ReferenceTypeModifier)
        if (syntax is ModifiedType { Modifier: ReferenceTypeModifier } refType)
        {
            if (genericParam is not null && refType.Type is SimpleType simple && simple.Name.Value == genericParam.Name.Text)
                return true;

            var innerHandle = ResolveGenericOrTypeSymbol(refType.Type, scope);
            return innerHandle == expectedTargetHandle;
        }

        // Case 2: Reference<T> syntax (GenericType)
        if (syntax is GenericType gen && gen.GenericArguments.Count == 1)
        {
            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Reference, out var refHandle))
            {
                var baseHandle = ResolveTypeSymbolByName(gen.Name.Value, scope, gen.GenericArguments.Count);
                var targetSym = context.GetSymbol(refHandle);

                if (baseHandle == refHandle || (targetSym is not null && gen.Name.Value == targetSym.Name.Text))
                {
                    var argName = GetGenericArgumentName(gen.GenericArguments[0]);

                    if (genericParam is not null && argName == genericParam.Name.Text)
                        return true;

                    var innerHandle = ResolveGenericOrTypeSymbol(gen.GenericArguments[0], scope);
                    return innerHandle == expectedTargetHandle;
                }
            }
        }

        return false;
    }

    private bool IsPointerTo(TypeSyntax? syntax, Scope scope, SymbolHandle expectedTargetHandle, GenericParameterSymbol? genericParam = null)
    {
        if (syntax is null)
            return false;

        // Case 1: T* syntax (ModifiedType with PointerTypeModifier)
        if (syntax is ModifiedType { Modifier: PointerTypeModifier } ptrType)
        {
            if (genericParam is not null && ptrType.Type is SimpleType simple && simple.Name.Value == genericParam.Name.Text)
                return true;

            var innerHandle = ResolveGenericOrTypeSymbol(ptrType.Type, scope);
            return innerHandle == expectedTargetHandle;
        }

        // Case 2: Pointer<T> syntax (GenericType)
        if (syntax is GenericType gen && gen.GenericArguments.Count == 1)
        {
            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Pointer, out var pointerHandle))
            {
                var baseHandle = ResolveTypeSymbolByName(gen.Name.Value, scope, gen.GenericArguments.Count);
                var targetSym = context.GetSymbol(pointerHandle);

                if (baseHandle == pointerHandle || (targetSym is not null && gen.Name.Value == targetSym.Name.Text))
                {
                    var argName = GetGenericArgumentName(gen.GenericArguments[0]);

                    if (genericParam is not null && argName == genericParam.Name.Text)
                        return true;

                    var innerHandle = ResolveGenericOrTypeSymbol(gen.GenericArguments[0], scope);
                    return innerHandle == expectedTargetHandle;
                }
            }
        }

        return false;
    }

    private static string? GetGenericArgumentName(TypeSyntax syntax) => syntax switch
    {
        SimpleType simple => simple.Name.Value,
        NamedExpressionGenericArgument named => named.Expression.Identifier.Value,
        _ => null
    };

    private SymbolHandle? ResolveTypeSymbolByName(string name, Scope scope, int arity = 0)
    {
        var symPart = new SymbolPart(name, arity);
        var symbols = scope.GetSymbols(symPart);

        if (symbols.Count == 1)
        {
            var sym = symbols[0];

            if (sym is TypeSymbol or NestedTypeSymbol)
                return ResolutionContext.GetHandle(sym);

            if (sym is AliasSymbol alias)
            {
                EnsureAliasResolved(alias);

                if (alias.Target.Handle is { } targetHandle)
                    return targetHandle;
            }
        }

        return null;
    }

    private static Scope? GetOwnedScope(Symbol symbol) =>
        symbol.EnclosingScope.ChildScopes.TryGetValue(ResolutionContext.GetHandle(symbol), out var s) ? s : null;

    private static List<FieldSymbol> GetInstanceFields(Symbol symbol)
    {
        var scope = GetOwnedScope(symbol);

        if (scope is null)
            return [];

        return scope.Symbols.Values.OfType<FieldSymbol>()
            .Where(static f => !f.Flags.HasFlag(VariableFlags.Static))
            .ToList();
    }

    private static int GetGenericParameterCount(Symbol symbol) => symbol switch
    {
        TypeSymbol ts => ts.GenericParameters.Count,
        NestedTypeSymbol nts => nts.GenericParameters.Count,
        _ => 0
    };

    private static SymbolHandle? GetFirstGenericParameter(Symbol symbol) => symbol switch
    {
        TypeSymbol ts => ts.GenericParameters.Count > 0 ? ts.GenericParameters[0] : null,
        NestedTypeSymbol nts => nts.GenericParameters.Count > 0 ? nts.GenericParameters[0] : null,
        _ => null
    };

    private static TypeDeclaration? GetSyntax(Symbol symbol) => symbol switch
    {
        TypeSymbol ts => ts.Syntax,
        NestedTypeSymbol nts => nts.Syntax,
        _ => null
    };

    private List<(Symbol Symbol, string IntrinsicLiteral, TextSpan ArgumentSpan, TextSpan Span, SourceText? Source)> CollectIntrinsicApplications(AttributeSymbol? candidateAttr)
    {
        var result = new List<(Symbol Symbol, string IntrinsicLiteral, TextSpan ArgumentSpan, TextSpan Span, SourceText? Source)>();
        var allTypes = context.TypeSymbols.Cast<Symbol>().Concat(context.NestedTypeSymbols);

        foreach (var symbol in allTypes)
        {
            var syntax = GetSyntax(symbol);

            if (syntax?.Attributes is not { Count: > 0 } attrLists)
                continue;

            foreach (var list in attrLists)
            {
                foreach (var app in list.Attributes)
                {
                    if (!MatchesIntrinsicTypeAttribute(app, candidateAttr, symbol.EnclosingScope))
                        continue;

                    if (app.Arguments.Count != 1)
                    {
                        var span = app.GetSpan() ?? default;
                        context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(span, "requires exactly one string literal argument", syntax.GetSource());
                        continue;
                    }

                    var arg = app.Arguments[0];
                    string? literalValue = ExtractLiteralString(arg);

                    if (literalValue is null)
                    {
                        var span = arg.GetSpan() ?? default;
                        context.Diagnostics.ReportUnknownIntrinsicType(span, arg.ToString() ?? "", syntax.GetSource());
                        continue;
                    }

                    result.Add((symbol, literalValue, arg.GetSpan() ?? default, syntax.Name.GetSpan() ?? default, syntax.GetSource()));
                }
            }
        }

        return result;
    }

    private List<(Symbol Symbol, AttributeApplication Application)> FindTypesApplyingIntrinsic(string literal, AttributeSymbol candidateAttr)
    {
        var result = new List<(Symbol Symbol, AttributeApplication Application)>();
        var allTypes = context.TypeSymbols.Cast<Symbol>().Concat(context.NestedTypeSymbols);

        foreach (var symbol in allTypes)
        {
            var syntax = GetSyntax(symbol);

            if (syntax?.Attributes is not { Count: > 0 } attrLists)
                continue;

            foreach (var list in attrLists)
            {
                foreach (var app in list.Attributes)
                {
                    if (!MatchesIntrinsicTypeAttribute(app, candidateAttr, symbol.EnclosingScope))
                        continue;

                    if (app.Arguments.Count == 1 && ExtractLiteralString(app.Arguments[0]) == literal)
                        result.Add((symbol, app));
                }
            }
        }

        return result;
    }

    private static bool MatchesIntrinsicTypeAttribute(AttributeApplication app, AttributeSymbol? candidateAttr, Scope scope)
    {
        ReadOnlySpan<char> nameSpan = app.Name switch
        {
            SimpleName simple => simple.Name.Source.AsSpan(simple.Name.Span),
            QualifiedName qual when qual.Parts.Count > 0 && qual.Parts[^1] is SimpleName lastSimple => lastSimple.Name.Source.AsSpan(lastSimple.Name.Span),
            _ => default
        };

        if (nameSpan.SequenceEqual(KnownIntrinsics.IntrinsicTypeAttribute.AsSpan()))
            return true;

        if (candidateAttr is not null && nameSpan.SequenceEqual(candidateAttr.Name.AsSpan()))
            return true;

        return false;
    }

    private static bool TryExtractLiteralSpan(Expression expression, out ReadOnlySpan<char> span)
    {
        if (expression is LiteralExpression lit && (lit.Literal.Kind is TokenKind.String or TokenKind.SuffixedString))
        {
            var rawSpan = lit.Literal.Source.AsSpan(lit.Literal.Span);
            if (rawSpan.Length >= 2 && rawSpan[0] == '"' && rawSpan[^1] == '"')
            {
                span = rawSpan.Slice(1, rawSpan.Length - 2);
                return true;
            }

            span = rawSpan;
            return true;
        }

        if (expression is NameofExpression nameofExpr && nameofExpr.Argument is NamedExpression named)
        {
            span = named.Identifier.Source.AsSpan(named.Identifier.Span);
            return true;
        }

        span = default;
        return false;
    }

    private static string? ExtractLiteralString(Expression expression) =>
        TryExtractLiteralSpan(expression, out var span) ? span.ToString() : null;

    private SymbolHandle? ResolveTypeSymbol(TypeSyntax? syntax, Scope scope)
    {
        if (syntax is null)
            return null;

        var symbolName = ResolutionContext.GetSymbolName(syntax);
        var symbols = scope[symbolName];

        if (symbols.Count == 1)
        {
            var sym = symbols[0];

            if (sym is TypeSymbol or NestedTypeSymbol)
                return ResolutionContext.GetHandle(sym);

            if (sym is AliasSymbol alias)
            {
                EnsureAliasResolved(alias);

                if (alias.Target.Handle is { } targetHandle)
                {
                    var targetSym = context.GetSymbol(targetHandle);

                    if (targetSym is TypeSymbol or NestedTypeSymbol)
                        return targetHandle;
                }
            }
        }

        return null;
    }

    private SymbolHandle? ResolveGenericOrTypeSymbol(TypeSyntax? syntax, Scope scope)
    {
        if (syntax is NamedExpressionGenericArgument named)
        {
            var symbols = scope[new SymbolName(new SymbolPart(named.Expression.Identifier.Value))];

            if (symbols.Count == 1)
            {
                var sym = symbols[0];

                if (sym is GenericParameterSymbol or TypeSymbol or NestedTypeSymbol)
                    return ResolutionContext.GetHandle(sym);

                if (sym is AliasSymbol alias)
                {
                    EnsureAliasResolved(alias);
                    return alias.Target.Handle;
                }
            }
        }
        else if (syntax is SimpleType simple)
        {
            var name = ResolutionContext.GetSymbolName(simple);
            var symbols = scope[name];

            if (symbols.Count == 1)
            {
                var sym = symbols[0];

                if (sym is GenericParameterSymbol or TypeSymbol or NestedTypeSymbol)
                    return ResolutionContext.GetHandle(sym);
            }
        }

        return ResolveTypeSymbol(syntax, scope);
    }

    private void EnsureAliasResolved(AliasSymbol alias)
    {
        if (alias.Target.IsResolved || alias.Syntax is null)
            return;

        var targetHandle = ResolveTypeSymbol(alias.Syntax.Target, alias.EnclosingScope);

        if (targetHandle is { } h)
            alias.Target = TypeRef.Resolved(h);
    }

    private void DiscoverAndValidateOperatorFunctions()
    {
        var allMethods = context.MethodSymbols;

        foreach (var method in allMethods)
        {
            if (method.Syntax?.Attributes is not { Count: > 0 } attrLists)
                continue;

            foreach (var list in attrLists)
            {
                foreach (var app in list.Attributes)
                {
                    if (!MatchesAttributeName(app, KnownIntrinsics.OperatorFunctionAttribute, context.DiscoveredOperators.OperatorFunctionAttribute))
                        continue;

                    ValidateAndRegisterOperatorFunction(method, app);
                }
            }
        }
    }

    private void ValidateAndRegisterOperatorFunction(MethodSymbol method, AttributeApplication app)
    {
        if (method.Parent is null)
            return;

        var parentSym = context.GetSymbol(method.Parent.Value);
        if (parentSym is not TypeSymbol parentType || parentType.TypeKind != TypeKind.Interface)
        {
            var span = app.GetSpan() ?? method.Syntax?.Signature.Identifier.GetSpan() ?? default;
            context.Diagnostics.ReportInvalidOperatorFunctionSignature(span, "OperatorFunction", "operator functions must be declared in an interface", method.Syntax?.GetSource());
            return;
        }

        if (app.Arguments.Count != 1)
        {
            var span = app.GetSpan() ?? default;
            context.Diagnostics.ReportInvalidOperatorFunctionSignature(span, "OperatorFunction", "requires exactly one string literal argument", method.Syntax?.GetSource());
            return;
        }

        if (!TryExtractLiteralSpan(app.Arguments[0], out var nameSpan) || !KnownIntrinsics.TryGetOperatorKind(nameSpan, out var opKind))
        {
            var span = app.Arguments[0].GetSpan() ?? default;
            string displayVal = nameSpan.Length > 0 ? nameSpan.ToString() : (app.Arguments[0].ToString() ?? "");
            context.Diagnostics.ReportInvalidOperatorFunctionSignature(span, displayVal, $"unknown operator function '{displayVal}'", method.Syntax?.GetSource());
            return;
        }

        string fullName = KnownIntrinsics.GetOperatorName(opKind);
        bool isStatic = method.Flags.HasFlag(FunctionFlags.Static);
        int paramCount = method.Syntax?.Signature.Parameters.Count ?? method.Parameters.Count;
        var returnTypeSyntax = method.Syntax?.Signature.ReturnType;
        bool isVoidReturn = returnTypeSyntax is not null && IsVoidType(returnTypeSyntax, method.EnclosingScope);
        bool isRefReturn = returnTypeSyntax is not null && IsReferenceType(returnTypeSyntax, method.EnclosingScope);
        var sigSpan = method.Syntax?.Signature.Identifier.GetSpan() ?? app.GetSpan() ?? default;
        var source = method.Syntax?.GetSource();

        switch (opKind)
        {
            // Binary operators
            case OperatorKind.Addition:
            case OperatorKind.Subtraction:
            case OperatorKind.Multiplication:
            case OperatorKind.Division:
            case OperatorKind.Remainder:
            case OperatorKind.BitwiseAnd:
            case OperatorKind.BitwiseOr:
            case OperatorKind.BitwiseXor:
            case OperatorKind.LeftShift:
            case OperatorKind.RightShift:
            case OperatorKind.RotateLeft:
            case OperatorKind.RotateRight:
            case OperatorKind.Equality:
            case OperatorKind.Inequality:
            case OperatorKind.LessThan:
            case OperatorKind.LessThanOrEqual:
            case OperatorKind.GreaterThan:
            case OperatorKind.GreaterThanOrEqual:
            case OperatorKind.LogicalAnd:
            case OperatorKind.LogicalOr:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (paramCount != 2)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept exactly 2 parameters", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // Unary non-mutating
            case OperatorKind.UnaryNegation:
            case OperatorKind.UnaryPositive:
            case OperatorKind.LogicalNot:
            case OperatorKind.BitwiseNot:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (paramCount != 1)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept exactly 1 parameter", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // Cast operators
            case OperatorKind.ImplicitCast:
            case OperatorKind.ExplicitCast:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (paramCount != 1)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept exactly 1 parameter", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // Constructor
            case OperatorKind.Constructor:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "constructors cannot be declared static", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "constructors must return void", source);
                    return;
                }
                break;

            // Destructor
            case OperatorKind.Destructor:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "destructors cannot be declared static", source);
                    return;
                }
                if (paramCount != 0)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "destructors cannot accept parameters", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "destructors must return void", source);
                    return;
                }
                break;

            // AddressOf
            case OperatorKind.AddressOf:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount != 0)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "cannot accept parameters", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // Dereference & IndirectMemberAccess
            case OperatorKind.Dereference:
            case OperatorKind.IndirectMemberAccess:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount != 0)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "cannot accept parameters", source);
                    return;
                }
                if (!isRefReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type must be a reference (&)", source);
                    return;
                }
                break;

            // Increment & Decrement
            case OperatorKind.PrefixIncrement:
            case OperatorKind.PostfixIncrement:
            case OperatorKind.PrefixDecrement:
            case OperatorKind.PostfixDecrement:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount != 0)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "cannot accept parameters", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // IndexerGetter
            case OperatorKind.IndexerGetter:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount < 1)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept at least 1 index parameter", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // IndexerSetter
            case OperatorKind.IndexerSetter:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount < 2)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept a value parameter and at least 1 index parameter", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type must be void", source);
                    return;
                }
                break;

            // Assignment & Compound Assignments
            case OperatorKind.Assignment:
            case OperatorKind.AdditionAssignment:
            case OperatorKind.SubtractionAssignment:
            case OperatorKind.MultiplicationAssignment:
            case OperatorKind.DivisionAssignment:
            case OperatorKind.RemainderAssignment:
            case OperatorKind.BitwiseAndAssignment:
            case OperatorKind.BitwiseOrAssignment:
            case OperatorKind.BitwiseXorAssignment:
            case OperatorKind.LeftShiftAssignment:
            case OperatorKind.RightShiftAssignment:
            case OperatorKind.RotateLeftAssignment:
            case OperatorKind.RotateRightAssignment:
                if (isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared as an instance method", source);
                    return;
                }
                if (paramCount != 1)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept exactly 1 parameter", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type must be void", source);
                    return;
                }
                break;

            // New
            case OperatorKind.New:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type cannot be void", source);
                    return;
                }
                break;

            // Put
            case OperatorKind.Put:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (paramCount < 1)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept at least 1 destination parameter", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type must be void", source);
                    return;
                }
                break;

            // Copy
            case OperatorKind.Copy:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (paramCount != 2)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept exactly 2 parameters (dest, source)", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type must be void", source);
                    return;
                }
                break;

            // Move
            case OperatorKind.Move:
                if (!isStatic)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must be declared static", source);
                    return;
                }
                if (paramCount != 2)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "must accept exactly 2 parameters (dest, source)", source);
                    return;
                }
                if (!isVoidReturn)
                {
                    context.Diagnostics.ReportInvalidOperatorFunctionSignature(sigSpan, fullName, "return type must be void", source);
                    return;
                }
                break;

            // Callable
            case OperatorKind.Callable:
                break;
        }

        var methodHandle = ResolutionContext.GetHandle(method);
        var interfaceHandle = method.Parent!.Value;
        context.DiscoveredOperators.Register(opKind, interfaceHandle, methodHandle, sigSpan, source);
    }

    private void DiscoverAndValidateIntrinsicFunctions()
    {
        var allMethods = context.MethodSymbols;

        foreach (var method in allMethods)
        {
            if (method.Syntax?.Attributes is not { Count: > 0 } attrLists)
                continue;

            foreach (var list in attrLists)
            {
                foreach (var app in list.Attributes)
                {
                    if (!MatchesAttributeName(app, KnownIntrinsics.IntrinsicFunctionAttribute, context.DiscoveredOperators.IntrinsicFunctionAttribute))
                        continue;

                    if (method.Parent is null)
                        continue;

                    var parentSym = context.GetSymbol(method.Parent.Value);
                    bool isIntrinsicType = false;

                    if (parentSym is TypeSymbol)
                    {
                        foreach (var (_, handle) in context.DiscoveredIntrinsics.All)
                        {
                            if (handle == method.Parent.Value)
                            {
                                isIntrinsicType = true;
                                break;
                            }
                        }
                    }

                    if (!isIntrinsicType)
                    {
                        var span = app.GetSpan() ?? default;
                        context.Diagnostics.ReportInvalidIntrinsicAttributeSignature(
                            span,
                            $"method '{method.Name.Text}' marked with [IntrinsicFunction] must be declared in an intrinsic type",
                            method.Syntax?.GetSource(),
                            KnownIntrinsics.IntrinsicFunctionAttribute);
                        continue;
                    }

                    method.Flags |= FunctionFlags.Intrinsic;
                }
            }
        }
    }

    private bool IsVoidType(TypeSyntax type, Scope scope)
    {
        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Void, out var voidHandle))
        {
            var resolved = ResolveTypeSymbol(type, scope);
            if (resolved == voidHandle)
                return true;
        }

        if (type is SimpleType simple && simple.Name.Value.AsSpan().Equals("void".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private bool IsReferenceType(TypeSyntax type, Scope scope)
    {
        if (type is ModifiedType modified && modified.Modifier is ReferenceTypeModifier)
            return true;

        if (type is PrefixModifiedType prefix && prefix.Modifier is SpanTypeModifier)
            return true;

        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Reference, out var refHandle))
        {
            var resolved = ResolveTypeSymbol(type, scope);
            if (resolved == refHandle)
                return true;
        }

        if (type is GenericType gen && gen.Name.Value.AsSpan().SequenceEqual(KnownIntrinsics.Reference.AsSpan()))
            return true;

        return false;
    }

    private static bool MatchesAttributeName(AttributeApplication app, string expectedName, AttributeSymbol? candidateAttr)
    {
        ReadOnlySpan<char> nameSpan = app.Name switch
        {
            SimpleName simple => simple.Name.Source.AsSpan(simple.Name.Span),
            QualifiedName qual when qual.Parts.Count > 0 && qual.Parts[^1] is SimpleName lastSimple => lastSimple.Name.Source.AsSpan(lastSimple.Name.Span),
            _ => default
        };

        if (nameSpan.SequenceEqual(expectedName.AsSpan()))
            return true;

        if (candidateAttr is not null && nameSpan.SequenceEqual(candidateAttr.Name.AsSpan()))
            return true;

        return false;
    }
}
