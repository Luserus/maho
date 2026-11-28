using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

internal sealed partial class IntrinsicDiscoveryPass
{
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

        if (intrinsicName != KnownIntrinsics.Array && intrinsicName != KnownIntrinsics.Slice && intrinsicName != KnownIntrinsics.Tuple && intrinsicName != KnownIntrinsics.Metadata && GetGenericParameterCount(symbol) > 0)
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
        bool hasValidMetadataField = false;
        if (instanceFields.Count == 2)
        {
            var secondFieldType = instanceFields[1].Syntax?.Type;
            if (secondFieldType is not null &&
                context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Metadata, out var metaHandle))
            {
                var resolvedSecond = ResolveTypeSymbol(secondFieldType, instanceFields[1].EnclosingScope);
                if (resolvedSecond == metaHandle)
                    hasValidMetadataField = true;
            }
        }

        if (instanceFields.Count != 1 && !hasValidMetadataField)
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
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, intrinsicName, $"first instance field must be of type '{KnownIntrinsics.NUInt}'", fieldSymbol.Syntax?.GetSource());
            return;
        }

        context.DiscoveredIntrinsics.Register(intrinsicName, ResolutionContext.GetHandle(symbol), entry.Span, entry.Source);
    }

    private void ValidateLayoutInterfaces(Dictionary<string, List<(Symbol Symbol, TextSpan Span, SourceText? Source)>> typesByIntrinsic)
    {
        if (typesByIntrinsic.TryGetValue(KnownIntrinsics.Layout, out var layoutList) &&
            layoutList.Count == 1 &&
            !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Layout))
        {
            var entry = layoutList[0];
            if (ValidateMarkerInterfaceContract(entry.Symbol, KnownIntrinsics.Layout, requireLayoutBase: false))
                context.DiscoveredIntrinsics.Register(KnownIntrinsics.Layout, ResolutionContext.GetHandle(entry.Symbol), entry.Span, entry.Source);
        }

        foreach (string name in (ReadOnlySpan<string>)[KnownIntrinsics.Sized, KnownIntrinsics.Unsized])
        {
            if (typesByIntrinsic.TryGetValue(name, out var list) &&
                list.Count == 1 &&
                !context.DiscoveredIntrinsics.Contains(name))
            {
                var entry = list[0];
                if (ValidateMarkerInterfaceContract(entry.Symbol, name, requireLayoutBase: true))
                    context.DiscoveredIntrinsics.Register(name, ResolutionContext.GetHandle(entry.Symbol), entry.Span, entry.Source);
            }
        }
    }

    private void ValidateTransferInterfaces(Dictionary<string, List<(Symbol Symbol, TextSpan Span, SourceText? Source)>> typesByIntrinsic)
    {
        if (typesByIntrinsic.TryGetValue(KnownIntrinsics.Transfer, out var transferList) &&
            transferList.Count == 1 &&
            !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Transfer))
        {
            var entry = transferList[0];
            if (ValidateMarkerInterfaceContract(entry.Symbol, KnownIntrinsics.Transfer, requireLayoutBase: false))
                context.DiscoveredIntrinsics.Register(KnownIntrinsics.Transfer, ResolutionContext.GetHandle(entry.Symbol), entry.Span, entry.Source);
        }

        foreach (string name in (ReadOnlySpan<string>)[KnownIntrinsics.Move, KnownIntrinsics.Copy])
        {
            if (!typesByIntrinsic.TryGetValue(name, out var list) || list.Count != 1 || context.DiscoveredIntrinsics.Contains(name))
                continue;

            var entry = list[0];
            var symbol = entry.Symbol;
            var syntax = GetSyntax(symbol);
            var span = syntax?.Name.GetSpan() ?? default;
            var source = syntax?.GetSource();

            var typeKind = symbol switch
            {
                TypeSymbol ts => ts.TypeKind,
                NestedTypeSymbol nts => nts.TypeKind,
                _ => TypeKind.Struct
            };

            if (typeKind != TypeKind.Interface)
            {
                context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, name, "must be declared as an interface", source);
                continue;
            }

            if (GetGenericParameterCount(symbol) > 1)
            {
                context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, name, "can have at most 1 ': this' generic parameter", source);
                continue;
            }

            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Transfer, out var transferHandle))
            {
                bool inheritsTransfer = syntax?.Base is { } baseList &&
                    baseList.BaseTypes.Any(b => ResolveTypeSymbol(b.Type, symbol.EnclosingScope) == transferHandle);
                if (!inheritsTransfer)
                {
                    context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, name, $"must inherit from '{KnownIntrinsics.Transfer}'", source);
                    continue;
                }
            }

            context.DiscoveredIntrinsics.Register(name, ResolutionContext.GetHandle(symbol), entry.Span, entry.Source);
        }
    }

    private bool ValidateMarkerInterfaceContract(Symbol symbol, string intrinsicName, bool requireLayoutBase)
    {
        var syntax = GetSyntax(symbol);
        var span = syntax?.Name.GetSpan() ?? default;
        var source = syntax?.GetSource();

        var typeKind = symbol switch
        {
            TypeSymbol ts => ts.TypeKind,
            NestedTypeSymbol nts => nts.TypeKind,
            _ => TypeKind.Struct
        };

        if (typeKind != TypeKind.Interface)
        {
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, intrinsicName, "must be declared as an interface", source);
            return false;
        }

        if (GetGenericParameterCount(symbol) > 0)
        {
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, intrinsicName, "cannot have generic parameters", source);
            return false;
        }

        var ownedScope = GetOwnedScope(symbol);
        if (GetInstanceFields(symbol).Count > 0 ||
            (ownedScope is not null && ownedScope.Symbols.Values.Any(s => s is MethodSymbol or PropertySymbol)))
        {
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, intrinsicName, "marker interface must have zero members", source);
            return false;
        }

        if (requireLayoutBase)
        {
            if (!context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Layout, out var layoutHandle))
            {
                context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, intrinsicName, $"requires intrinsic interface '{KnownIntrinsics.Layout}' to be declared first", source);
                return false;
            }

            bool inheritsLayout = false;
            if (syntax?.Base is { } baseList)
            {
                foreach (var baseItem in baseList.BaseTypes)
                {
                    if (ResolveTypeSymbol(baseItem.Type, symbol.EnclosingScope) == layoutHandle)
                    {
                        inheritsLayout = true;
                        break;
                    }
                }
            }

            if (!inheritsLayout)
            {
                context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, intrinsicName, $"must inherit from '{KnownIntrinsics.Layout}'", source);
                return false;
            }
        }

        return true;
    }

    private void ValidateStr(Dictionary<string, List<(Symbol Symbol, TextSpan Span, SourceText? Source)>> typesByIntrinsic)
    {
        if (!typesByIntrinsic.TryGetValue(KnownIntrinsics.Str, out var list) || list.Count != 1 || context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Str))
            return;

        var entry = list[0];
        var symbol = entry.Symbol;
        var syntax = GetSyntax(symbol);

        if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.UInt8) ||
            !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Slice))
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Str, $"requires '{KnownIntrinsics.UInt8}' and '{KnownIntrinsics.Slice}' to be declared first", syntax?.GetSource());
            return;
        }

        if (GetGenericParameterCount(symbol) != 0)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Str, "cannot have generic parameters", syntax?.GetSource());
            return;
        }

        var instanceFields = GetInstanceFields(symbol);
        if (instanceFields.Count != 1)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Str, "must have exactly 1 instance field of type 'UInt8[]' (or 'Slice<UInt8>')", syntax?.GetSource());
            return;
        }

        var field1 = instanceFields[0];
        var field1Type = field1.Syntax?.Type;
        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.UInt8, out var uint8Handle);

        if (!IsByteSliceType(field1Type, field1.EnclosingScope, uint8Handle))
        {
            var span = field1.Syntax?.Type.GetSpan() ?? field1.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Str, "field must be of type 'UInt8[]' (or 'Slice<UInt8>')", field1.Syntax?.GetSource());
            return;
        }

        context.DiscoveredIntrinsics.Register(KnownIntrinsics.Str, ResolutionContext.GetHandle(symbol), entry.Span, entry.Source);
    }

    private void ValidateSlice(Dictionary<string, List<(Symbol Symbol, TextSpan Span, SourceText? Source)>> typesByIntrinsic)
    {
        if (!typesByIntrinsic.TryGetValue(KnownIntrinsics.Slice, out var list) || list.Count != 1 || context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Slice))
            return;

        var entry = list[0];
        var symbol = entry.Symbol;
        var syntax = GetSyntax(symbol);

        var instanceFields = GetInstanceFields(symbol);
        if (instanceFields.Count > 0)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Slice, "must have zero instance fields", syntax?.GetSource());
            return;
        }

        var gpHandles = GetGenericParameters(symbol);
        if (gpHandles.Count is not (1 or 2))
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Slice, "must have 1 or 2 generic parameters (<T> or <T, D : int = 1>)", syntax?.GetSource());
            return;
        }

        if (gpHandles.Count == 2 && context.GetGenericParameterSymbol(gpHandles[1]) is { ParameterKind: not GenericParameterKind.Integer } dimParam)
        {
            var span = dimParam.Syntax?.GetSpan() ?? syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Slice, "dimension generic parameter must be constrained with ': int'", dimParam.Syntax?.GetSource() ?? syntax?.GetSource());
            return;
        }

        context.DiscoveredIntrinsics.Register(KnownIntrinsics.Slice, ResolutionContext.GetHandle(symbol), entry.Span, entry.Source);
    }

    private void ValidateArray(Dictionary<string, List<(Symbol Symbol, TextSpan Span, SourceText? Source)>> typesByIntrinsic)
    {
        if (!typesByIntrinsic.TryGetValue(KnownIntrinsics.Array, out var list) || list.Count != 1 || context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Array))
            return;

        var entry = list[0];
        var symbol = entry.Symbol;
        var syntax = GetSyntax(symbol);

        var instanceFields = GetInstanceFields(symbol);
        if (instanceFields.Count > 0)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Array, "must have zero instance fields", syntax?.GetSource());
            return;
        }

        var gpHandles = GetGenericParameters(symbol);
        if (gpHandles.Count is not (2 or 3))
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Array, "must have 2 or 3 generic parameters (<T, N : int> or <T, N : int, D : int = 1>)", syntax?.GetSource());
            return;
        }

        for (int i = 1; i < gpHandles.Count; i++)
        {
            if (context.GetGenericParameterSymbol(gpHandles[i]) is { ParameterKind: not GenericParameterKind.Integer } gp)
            {
                var span = gp.Syntax?.GetSpan() ?? syntax?.Name.GetSpan() ?? default;
                context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Array, "size and dimension generic parameters must be constrained with ': int'", gp.Syntax?.GetSource() ?? syntax?.GetSource());
                return;
            }
        }

        context.DiscoveredIntrinsics.Register(KnownIntrinsics.Array, ResolutionContext.GetHandle(symbol), entry.Span, entry.Source);
    }

    private void ValidateFunction(Dictionary<string, List<(Symbol Symbol, TextSpan Span, SourceText? Source)>> typesByIntrinsic)
    {
        if (!typesByIntrinsic.TryGetValue(KnownIntrinsics.Function, out var list) || list.Count != 1 || context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Function))
            return;

        var entry = list[0];
        var symbol = entry.Symbol;
        var syntax = GetSyntax(symbol);

        if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.NUInt))
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Function, $"requires intrinsic '{KnownIntrinsics.NUInt}' to be declared first", syntax?.GetSource());
            return;
        }

        if (GetGenericParameterCount(symbol) < 1)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Function, "must have at least 1 generic parameter", syntax?.GetSource());
            return;
        }

        var instanceFields = GetInstanceFields(symbol);
        if (instanceFields.Count != 1)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Function, "must have exactly 1 instance field", syntax?.GetSource());
            return;
        }

        var fieldSymbol = instanceFields[0];
        var fieldType = fieldSymbol.Syntax?.Type;
        var resolvedFieldHandle = ResolveTypeSymbol(fieldType, fieldSymbol.EnclosingScope);

        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.NUInt, out var nuintHandle);

        if (resolvedFieldHandle != nuintHandle)
        {
            var span = fieldSymbol.Syntax?.Type.GetSpan() ?? fieldSymbol.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Function, $"instance field must be of type '{KnownIntrinsics.NUInt}'", fieldSymbol.Syntax?.GetSource());
            return;
        }

        context.DiscoveredIntrinsics.Register(KnownIntrinsics.Function, ResolutionContext.GetHandle(symbol), entry.Span, entry.Source);
    }

    private void ValidateOptional(Dictionary<string, List<(Symbol Symbol, TextSpan Span, SourceText? Source)>> typesByIntrinsic)
    {
        if (!typesByIntrinsic.TryGetValue(KnownIntrinsics.Optional, out var list) || list.Count != 1 || context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Optional))
            return;

        var entry = list[0];
        var symbol = entry.Symbol;
        var syntax = GetSyntax(symbol);

        if (symbol is not ISumTypeSymbol sumType || (symbol is TypeSymbol ts && ts.TypeKind != TypeKind.Enum))
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Optional, "must be declared as an enum", syntax?.GetSource());
            return;
        }

        if (GetGenericParameterCount(symbol) != 1)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Optional, "must have exactly 1 generic parameter", syntax?.GetSource());
            return;
        }

        var variants = sumType.EnumVariants;
        if (variants.Count != 2)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Optional, "must have exactly 2 variants: 'Some(T)' and 'None'", syntax?.GetSource());
            return;
        }

        EnumVariantSymbol? someVariant = null;
        EnumVariantSymbol? noneVariant = null;
        foreach (var vHandle in variants)
        {
            var vSym = context.GetSymbol(vHandle) as EnumVariantSymbol;
            if (vSym is null) continue;
            if (string.Equals(vSym.Name.Text, "Some", StringComparison.OrdinalIgnoreCase))
                someVariant = vSym;
            else if (string.Equals(vSym.Name.Text, "None", StringComparison.OrdinalIgnoreCase))
                noneVariant = vSym;
        }

        if (someVariant is null || noneVariant is null || someVariant.VariantKind != EnumVariantKind.Tuple || (noneVariant.VariantKind != EnumVariantKind.Unit && noneVariant.VariantKind != EnumVariantKind.Tuple))
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Optional, "must have variants 'Some(T)' and 'None'", syntax?.GetSource());
            return;
        }

        context.DiscoveredIntrinsics.Register(KnownIntrinsics.Optional, ResolutionContext.GetHandle(symbol), entry.Span, entry.Source);
    }

    /// <summary>
    /// Checks whether the given type syntax resolves to either a pointer or reference to the expected target type.
    /// </summary>
    private bool IsPointerOrReferenceTo(TypeSyntax? syntax, Scope scope, SymbolHandle expectedTargetHandle, GenericParameterSymbol? genericParam = null) =>
        IsReferenceTo(syntax, scope, expectedTargetHandle, genericParam) || IsPointerTo(syntax, scope, expectedTargetHandle, genericParam);

    /// <summary>
    /// Checks whether the given type syntax is any pointer or reference type (e.g. <c>T*</c>, <c>T&amp;</c>, <c>Pointer&lt;T&gt;</c>, <c>Reference&lt;T&gt;</c>).
    /// </summary>
    private bool IsAnyPointerOrReference(TypeSyntax? syntax, Scope scope)
    {
        if (syntax is null) return false;
        if (syntax is ModifiedType { Modifier: PointerTypeModifier or ReferenceTypeModifier })
            return true;
        if (syntax is GenericType gen && gen.GenericArguments.Count == 1)
        {
            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Pointer, out var ptrHandle))
            {
                var baseHandle = ResolveTypeSymbolByName(gen.Name.Value, scope, 1);
                var targetSym = context.GetSymbol(ptrHandle);
                if (baseHandle == ptrHandle || (targetSym is not null && gen.Name.Value == targetSym.Name.Text))
                    return true;
            }
            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.MutablePointer, out var mutPtrHandle))
            {
                var baseHandle = ResolveTypeSymbolByName(gen.Name.Value, scope, 1);
                var targetSym = context.GetSymbol(mutPtrHandle);
                if (baseHandle == mutPtrHandle || (targetSym is not null && gen.Name.Value == targetSym.Name.Text))
                    return true;
            }
            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Reference, out var refHandle))
            {
                var baseHandle = ResolveTypeSymbolByName(gen.Name.Value, scope, 1);
                var targetSym = context.GetSymbol(refHandle);
                if (baseHandle == refHandle || (targetSym is not null && gen.Name.Value == targetSym.Name.Text))
                    return true;
            }
            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.MutableReference, out var mutRefHandle))
            {
                var baseHandle = ResolveTypeSymbolByName(gen.Name.Value, scope, 1);
                var targetSym = context.GetSymbol(mutRefHandle);
                if (baseHandle == mutRefHandle || (targetSym is not null && gen.Name.Value == targetSym.Name.Text))
                    return true;
            }
        }
        return false;
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

        // Case 2: Reference<T> or MutableReference<T> syntax (GenericType)
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

            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.MutableReference, out var mutRefHandle))
            {
                var baseHandle = ResolveTypeSymbolByName(gen.Name.Value, scope, gen.GenericArguments.Count);
                var targetSym = context.GetSymbol(mutRefHandle);

                if (baseHandle == mutRefHandle || (targetSym is not null && gen.Name.Value == targetSym.Name.Text))
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

            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.MutablePointer, out var mutPtrHandle))
            {
                var baseHandle = ResolveTypeSymbolByName(gen.Name.Value, scope, gen.GenericArguments.Count);
                var targetSym = context.GetSymbol(mutPtrHandle);

                if (baseHandle == mutPtrHandle || (targetSym is not null && gen.Name.Value == targetSym.Name.Text))
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

    private bool IsReferenceToType(TypeSyntax? syntax, Scope scope, SymbolHandle expectedTargetHandle)
    {
        while (syntax is GroupedType grouped)
            syntax = grouped.InnerType;

        if (syntax is not ModifiedType { Modifier: ReferenceTypeModifier { IsMutable: false } } refMod)
            return false;

        var inner = refMod.Type;
        while (inner is GroupedType innerGrouped)
            inner = innerGrouped.InnerType;

        return ResolveTypeSymbol(inner, scope) == expectedTargetHandle;
    }

    private bool IsByteSliceType(TypeSyntax? syntax, Scope scope, SymbolHandle uint8Handle)
    {
        while (syntax is GroupedType grouped)
            syntax = grouped.InnerType;

        if (syntax is ModifiedType { Modifier: SliceTypeModifier { Dimension: 1 } } sliceMod)
        {
            var elem = sliceMod.Type;
            while (elem is GroupedType elemGrouped)
                elem = elemGrouped.InnerType;

            return ResolveTypeSymbol(elem, scope) == uint8Handle;
        }

        if (syntax is GenericType gen &&
            gen.GenericArguments.Count is (1 or 2) &&
            context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Slice, out var sliceHandle))
        {
            var baseHandle = ResolveTypeSymbolByName(gen.Name.Value, scope, gen.GenericArguments.Count);
            if (baseHandle != sliceHandle)
            {
                // Also allow defaulting D = 1 if declared with arity 2 but written Slice<UInt8>.
                var arity2Handle = ResolveTypeSymbolByName(gen.Name.Value, scope, 2);
                if (arity2Handle != sliceHandle)
                    return false;
            }

            if (ResolveGenericOrTypeSymbol(gen.GenericArguments[0], scope) != uint8Handle)
                return false;

            if (gen.GenericArguments.Count == 2 &&
                gen.GenericArguments[1] is LiteralGenericArgument lit &&
                lit.Expression.Literal.Value != "1")
                return false;

            return true;
        }

        return false;
    }

    private static IReadOnlyList<SymbolHandle> GetGenericParameters(Symbol symbol) => symbol switch
    {
        TypeSymbol ts => ts.GenericParameters,
        NestedTypeSymbol nts => nts.GenericParameters,
        _ => []
    };

    private static int GetGenericParameterCount(Symbol symbol) => GetGenericParameters(symbol).Count;

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

    private static string? ExtractLiteralString(Expression expression)
    {
        if (expression is LiteralExpression lit && lit.Literal.Kind is TokenKind.String or TokenKind.SuffixedString)
            return LiteralEscapeParser.Parse(lit.Literal).DecodedValue;

        if (expression is NameofExpression nameofExpr && nameofExpr.Argument is NamedExpression named)
            return named.Identifier.Value;

        return null;
    }

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

    private void ValidateBox(Dictionary<string, List<(Symbol Symbol, TextSpan Span, SourceText? Source)>> typesByIntrinsic)
    {
        if (!typesByIntrinsic.TryGetValue(KnownIntrinsics.Box, out var list) || list.Count != 1 || context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Box))
            return;

        var entry = list[0];
        var symbol = entry.Symbol;
        var syntax = GetSyntax(symbol);

        int gpCount = GetGenericParameterCount(symbol);
        if (gpCount != 1)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Box, "must have exactly 1 generic parameter (<T>)", syntax?.GetSource());
            return;
        }

        context.DiscoveredIntrinsics.Register(KnownIntrinsics.Box, ResolutionContext.GetHandle(symbol), entry.Span, entry.Source);
    }
}
