using System.Collections.Generic;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

internal sealed partial class DeclarationResolutionPass
{
    private enum ModifierCategory : byte
    {
        None = 0,
        Visibility = 1,
        Linkage = 2,
        Storage = 3,
        Polymorphism = 4,
        Safety = 5,
        Partial = 6,
        Mutability = 7,
        Layout = 8
    }

    private static ModifierCategory GetModifierCategory(MatchingKeywordKind kind) => kind switch
    {
        MatchingKeywordKind.Public or MatchingKeywordKind.Internal or MatchingKeywordKind.Protected or MatchingKeywordKind.Private => ModifierCategory.Visibility,
        MatchingKeywordKind.Extern or MatchingKeywordKind.Intrinsic => ModifierCategory.Linkage,
        MatchingKeywordKind.Static => ModifierCategory.Storage,
        MatchingKeywordKind.Virtual or MatchingKeywordKind.Override or MatchingKeywordKind.Unsealed => ModifierCategory.Polymorphism,
        MatchingKeywordKind.Unsafe => ModifierCategory.Safety,
        MatchingKeywordKind.Partial => ModifierCategory.Partial,
        MatchingKeywordKind.Const or MatchingKeywordKind.Mut => ModifierCategory.Mutability,
        MatchingKeywordKind.Untagged => ModifierCategory.Layout,
        _ => ModifierCategory.None
    };

    private const string CanonicalOrderString = "[visibility] [extern|intrinsic] [static] [virtual|override|unsealed] [unsafe] [partial] [const|mut] [untagged]";

    private void ValidateModifiers()
    {
        // 1. Top-level types
        foreach (var type in context.TypeSymbols)
        {
            if (type.Syntax is not null)
            {
                bool isSumType = type.TypeKind is TypeKind.Enum or TypeKind.Union;
                var allowed = new HashSet<MatchingKeywordKind>
                {
                    MatchingKeywordKind.Public, MatchingKeywordKind.Internal, MatchingKeywordKind.Static,
                    MatchingKeywordKind.Unsealed, MatchingKeywordKind.Unsafe, MatchingKeywordKind.Partial
                };
                if (isSumType)
                    allowed.Add(MatchingKeywordKind.Untagged);

                string itemKind = isSumType ? (type.TypeKind == TypeKind.Enum ? "enum" : "union") : (type.TypeKind == TypeKind.Struct ? "struct" : type.TypeKind == TypeKind.Class ? "class" : "interface");

                ValidateModifierList(
                    type.Syntax.Modifiers,
                    allowed: allowed,
                    itemKind: itemKind,
                    name: type.Name.Text,
                    span: type.Syntax.Name.GetSpan() ?? default,
                    source: type.Syntax.GetSource());
            }
        }

        // 2. Nested types
        foreach (var type in context.NestedTypeSymbols)
        {
            if (type.Syntax is not null)
            {
                bool isSumType = type.TypeKind is TypeKind.Enum or TypeKind.Union;
                var allowed = new HashSet<MatchingKeywordKind>
                {
                    MatchingKeywordKind.Public, MatchingKeywordKind.Internal, MatchingKeywordKind.Protected, MatchingKeywordKind.Private,
                    MatchingKeywordKind.Static, MatchingKeywordKind.Unsealed, MatchingKeywordKind.Unsafe, MatchingKeywordKind.Partial
                };
                if (isSumType)
                    allowed.Add(MatchingKeywordKind.Untagged);

                string itemKind = isSumType ? (type.TypeKind == TypeKind.Enum ? "nested enum" : "nested union") : (type.TypeKind == TypeKind.Struct ? "nested struct" : type.TypeKind == TypeKind.Class ? "nested class" : "nested interface");

                ValidateModifierList(
                    type.Syntax.Modifiers,
                    allowed: allowed,
                    itemKind: itemKind,
                    name: type.Name.Text,
                    span: type.Syntax.Name.GetSpan() ?? default,
                    source: type.Syntax.GetSource());
            }
        }

        // 3. Top-level functions
        foreach (var fn in context.FunctionSymbols)
        {
            if (fn.Syntax is not null)
            {
                ValidateModifierList(
                    fn.Syntax.Signature.Modifiers,
                    allowed: [MatchingKeywordKind.Public, MatchingKeywordKind.Internal, MatchingKeywordKind.Extern, MatchingKeywordKind.Unsafe, MatchingKeywordKind.Partial],
                    itemKind: "function",
                    name: fn.Name.Text,
                    span: fn.Syntax.Signature.Identifier.GetSpan() ?? default,
                    source: fn.Syntax.GetSource());
            }
        }

        // 4. Methods
        foreach (var method in context.MethodSymbols)
        {
            if (method.Syntax is not null)
            {
                var parentSym = method.Parent is { } pH ? context.GetSymbol(pH) : null;
                bool isInterface = (parentSym as TypeSymbol)?.TypeKind == TypeKind.Interface || (parentSym as NestedTypeSymbol)?.TypeKind == TypeKind.Interface;
                if (isInterface)
                {
                    ValidateModifierList(
                        method.Syntax.Signature.Modifiers,
                        allowed: [MatchingKeywordKind.Unsafe],
                        itemKind: "interface method",
                        name: method.Name.Text,
                        span: method.Syntax.Signature.Identifier.GetSpan() ?? default,
                        source: method.Syntax.GetSource());
                }
                else
                {
                    bool isExplicitInterfaceImpl = method.ExplicitInterfaceName is not null;
                    ValidateModifierList(
                        method.Syntax.Signature.Modifiers,
                        allowed: isExplicitInterfaceImpl
                            ? [MatchingKeywordKind.Extern, MatchingKeywordKind.Virtual, MatchingKeywordKind.Override, MatchingKeywordKind.Unsealed, MatchingKeywordKind.Unsafe, MatchingKeywordKind.Partial]
                            : [MatchingKeywordKind.Public, MatchingKeywordKind.Internal, MatchingKeywordKind.Protected, MatchingKeywordKind.Private, MatchingKeywordKind.Extern, MatchingKeywordKind.Virtual, MatchingKeywordKind.Override, MatchingKeywordKind.Unsealed, MatchingKeywordKind.Unsafe, MatchingKeywordKind.Partial],
                        itemKind: isExplicitInterfaceImpl ? "explicit interface method implementation" : "method",
                        name: method.Name.Text,
                        span: method.Syntax.Signature.Identifier.GetSpan() ?? default,
                        source: method.Syntax.GetSource());
                }
            }
        }

        // 5. Properties
        foreach (var prop in context.PropertySymbols)
        {
            if (prop.Syntax is not null)
            {
                var parentSym = prop.Parent is { } pH ? context.GetSymbol(pH) : null;
                bool isInterface = (parentSym as TypeSymbol)?.TypeKind == TypeKind.Interface || (parentSym as NestedTypeSymbol)?.TypeKind == TypeKind.Interface;
                bool isExplicitInterfaceProp = prop.ExplicitInterfaceName is not null;
                if (isInterface)
                {
                    ValidateModifierList(
                        prop.Syntax.Modifiers,
                        allowed: [MatchingKeywordKind.Unsafe, MatchingKeywordKind.Static],
                        itemKind: "interface property",
                        name: prop.Name.Text,
                        span: prop.Syntax.Identifier.GetSpan() ?? default,
                        source: prop.Syntax.GetSource());
                }
                else
                {
                    ValidateModifierList(
                        prop.Syntax.Modifiers,
                        allowed: isExplicitInterfaceProp
                            ? [MatchingKeywordKind.Virtual, MatchingKeywordKind.Unsafe]
                            : [MatchingKeywordKind.Public, MatchingKeywordKind.Internal, MatchingKeywordKind.Protected, MatchingKeywordKind.Private, MatchingKeywordKind.Virtual, MatchingKeywordKind.Unsafe],
                        itemKind: isExplicitInterfaceProp ? "explicit interface property implementation" : "property",
                        name: prop.Name.Text,
                        span: prop.Syntax.Identifier.GetSpan() ?? default,
                        source: prop.Syntax.GetSource());
                }

                bool disallowVisibilityOnAccessors = isInterface || isExplicitInterfaceProp;
                foreach (var acc in prop.Syntax.Body.Accessors)
                {
                    ValidateModifierList(
                        acc.Modifiers,
                        allowed: disallowVisibilityOnAccessors ? [] : [MatchingKeywordKind.Public, MatchingKeywordKind.Internal, MatchingKeywordKind.Protected, MatchingKeywordKind.Private],
                        itemKind: isInterface ? "interface property accessor" : isExplicitInterfaceProp ? "explicit interface property accessor" : "property accessor",
                        name: acc.Kind == PropertyAccessorKind.Get ? "get" : "set",
                        span: acc.GetSpan() ?? default,
                        source: acc.GetSource());
                }
            }
        }

        // 6. Global variables
        foreach (var global in context.GlobalVariableSymbols)
        {
            if (global.Syntax is not null)
            {
                ValidateModifierList(
                    global.Syntax.Modifiers,
                    allowed: [MatchingKeywordKind.Public, MatchingKeywordKind.Internal, MatchingKeywordKind.Static, MatchingKeywordKind.Const, MatchingKeywordKind.Mut, MatchingKeywordKind.Unsafe],
                    itemKind: "global variable",
                    name: global.Name.Text,
                    span: global.Syntax.Declarator.Identifier.GetSpan() ?? default,
                    source: global.Syntax.GetSource(),
                    requireStatic: true);
            }
        }

        // 7. Fields
        foreach (var field in context.FieldSymbols)
        {
            if (field.Syntax is not null)
            {
                ValidateModifierList(
                    field.Syntax.Modifiers,
                    allowed: [MatchingKeywordKind.Public, MatchingKeywordKind.Internal, MatchingKeywordKind.Protected, MatchingKeywordKind.Private, MatchingKeywordKind.Ext, MatchingKeywordKind.Static, MatchingKeywordKind.Const, MatchingKeywordKind.Mut, MatchingKeywordKind.Unsafe],
                    itemKind: "field",
                    name: field.Name.Text,
                    span: field.Syntax.Declarator.Identifier.GetSpan() ?? default,
                    source: field.Syntax.GetSource(),
                    isField: true);
            }
        }

        // 7b. Extensions
        foreach (var ext in context.ExtensionSymbols)
        {
            if (ext.Syntax is not null)
            {
                ValidateModifierList(
                    ext.Syntax.Modifiers,
                    allowed: [MatchingKeywordKind.Public, MatchingKeywordKind.Internal, MatchingKeywordKind.Unsafe],
                    itemKind: "extension",
                    name: ext.Name.Text,
                    span: ext.Syntax.Keyword.Span,
                    source: ext.Syntax.GetSource());
            }
        }

        // 7b. Constants
        foreach (var constant in context.ConstantSymbols)
        {
            if (constant.Syntax is not null)
            {
                ValidateModifierList(
                    constant.Syntax.Modifiers,
                    allowed: [MatchingKeywordKind.Public, MatchingKeywordKind.Internal, MatchingKeywordKind.Protected, MatchingKeywordKind.Private, MatchingKeywordKind.Static, MatchingKeywordKind.Unsafe],
                    itemKind: "constant",
                    name: constant.Name.Text,
                    span: constant.Syntax.Identifier.GetSpan() ?? default,
                    source: constant.Syntax.GetSource());
            }
        }

        // 8. Attributes
        foreach (var attr in context.AttributeSymbols)
        {
            if (attr.Syntax is not null)
            {
                ValidateModifierList(
                    attr.Syntax.Modifiers,
                    allowed: [MatchingKeywordKind.Intrinsic, MatchingKeywordKind.Public, MatchingKeywordKind.Internal],
                    itemKind: "attribute",
                    name: attr.Name.Text,
                    span: attr.Syntax.Name.GetSpan() ?? default,
                    source: attr.Syntax.GetSource());
            }
        }

        // 9. Parameters
        foreach (var param in context.ParameterSymbols)
        {
            if (param.Syntax?.Declarator.Modifiers is { Count: > 0 } pMods)
            {
                bool isReceiver = param.Syntax.Declarator.IsReceiver;
                var parentMethod = param.ContainingSymbol is { Kind: SymbolKind.Method } ph ? context.GetMethodSymbol(ph) : null;
                var parentType = parentMethod?.Parent is { } ptH ? context.GetSymbol(ptH) : null;
                bool isInsideSumType = (parentType as TypeSymbol)?.TypeKind is TypeKind.Enum or TypeKind.Union
                                    || (parentType as NestedTypeSymbol)?.TypeKind is TypeKind.Enum or TypeKind.Union;

                var allowed = new HashSet<MatchingKeywordKind> { MatchingKeywordKind.Mut };
                if (isReceiver && isInsideSumType)
                {
                    allowed.Add(MatchingKeywordKind.Untagged);
                }

                ValidateModifierList(
                    pMods,
                    allowed: allowed,
                    itemKind: isReceiver ? "receiver parameter" : "parameter",
                    name: param.Name.Text,
                    span: param.Syntax.Declarator.Identifier.GetSpan() ?? default,
                    source: param.Syntax.GetSource());
            }
        }
    }

    private void ValidateModifierList(
        IReadOnlyList<Token> modifiers,
        HashSet<MatchingKeywordKind> allowed,
        string itemKind,
        string name,
        TextSpan span,
        SourceText? source,
        bool requireStatic = false,
        bool isField = false)
    {
        if (requireStatic)
        {
            bool hasStatic = false;
            foreach (var mod in modifiers)
            {
                if (mod.MatchingKind == MatchingKeywordKind.Static)
                {
                    hasStatic = true;
                    break;
                }
            }
            if (!hasStatic)
            {
                context.Diagnostics.ReportGlobalMissingStatic(span, name, source);
            }
        }

        if (modifiers.Count == 0)
            return;

        int extIndex = -1;
        if (isField)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].MatchingKind == MatchingKeywordKind.Ext)
                {
                    extIndex = i;
                    break;
                }
            }
        }

        var seenKinds = new HashSet<MatchingKeywordKind>();
        Token? seenVisibilityBeforeExt = null;
        Token? seenVisibilityAfterExt = null;
        Token? seenConst = null;
        Token? seenMut = null;
        Token? seenVirtual = null;
        Token? seenOverride = null;
        Token? seenUnsealed = null;

        var lastCategory = ModifierCategory.None;

        for (int i = 0; i < modifiers.Count; i++)
        {
            var mod = modifiers[i];
            var kind = mod.MatchingKind;

            // 1. Check if allowed
            if (!allowed.Contains(kind))
            {
                context.Diagnostics.ReportInvalidModifier(mod.Span, mod.Value, itemKind, name, source);
                continue;
            }

            // 2. Check duplicate
            var category = GetModifierCategory(kind);
            if (isField && extIndex >= 0 && category == ModifierCategory.Visibility)
            {
                // Visibility on a field with 'ext' can appear before and after 'ext'
                if (i < extIndex)
                {
                    if (seenVisibilityBeforeExt is not null)
                    {
                        context.Diagnostics.ReportConflictingModifiers(mod.Span, seenVisibilityBeforeExt.Value, mod.Value, name, source);
                        continue;
                    }
                    seenVisibilityBeforeExt = mod;
                }
                else
                {
                    if (seenVisibilityAfterExt is not null)
                    {
                        context.Diagnostics.ReportConflictingModifiers(mod.Span, seenVisibilityAfterExt.Value, mod.Value, name, source);
                        continue;
                    }
                    seenVisibilityAfterExt = mod;
                }
            }
            else
            {
                if (!seenKinds.Add(kind))
                {
                    context.Diagnostics.ReportDuplicateModifier(mod.Span, mod.Value, name, source);
                    continue;
                }

                // 3. Check conflicts
                if (category == ModifierCategory.Visibility)
                {
                    if (seenVisibilityBeforeExt is not null)
                        context.Diagnostics.ReportConflictingModifiers(mod.Span, seenVisibilityBeforeExt.Value, mod.Value, name, source);
                    else
                        seenVisibilityBeforeExt = mod;
                }
            }

            if (kind == MatchingKeywordKind.Const)
            {
                if (seenMut is not null)
                    context.Diagnostics.ReportConflictingModifiers(mod.Span, seenMut.Value, mod.Value, name, source);
                else
                    seenConst = mod;
            }
            else if (kind == MatchingKeywordKind.Mut)
            {
                if (seenConst is not null)
                    context.Diagnostics.ReportConflictingModifiers(mod.Span, seenConst.Value, mod.Value, name, source);
                else
                    seenMut = mod;
            }
            else if (kind == MatchingKeywordKind.Virtual)
            {
                if (seenUnsealed is not null)
                    context.Diagnostics.ReportConflictingModifiers(mod.Span, seenUnsealed.Value, mod.Value, name, source);
                else if (seenOverride is not null)
                    context.Diagnostics.ReportConflictingModifiers(mod.Span, seenOverride.Value, mod.Value, name, source);
                else
                    seenVirtual = mod;
            }
            else if (kind == MatchingKeywordKind.Override)
            {
                if (seenUnsealed is not null)
                    context.Diagnostics.ReportConflictingModifiers(mod.Span, seenUnsealed.Value, mod.Value, name, source);
                else if (seenVirtual is not null)
                    context.Diagnostics.ReportConflictingModifiers(mod.Span, seenVirtual.Value, mod.Value, name, source);
                else
                    seenOverride = mod;
            }
            else if (kind == MatchingKeywordKind.Unsealed)
            {
                if (seenVirtual is not null)
                    context.Diagnostics.ReportConflictingModifiers(mod.Span, seenVirtual.Value, mod.Value, name, source);
                else if (seenOverride is not null)
                    context.Diagnostics.ReportConflictingModifiers(mod.Span, seenOverride.Value, mod.Value, name, source);
                else
                    seenUnsealed = mod;
            }

            // 4. Check canonical ordering
            if (category != ModifierCategory.None)
            {
                if (category < lastCategory)
                {
                    context.Diagnostics.ReportModifierOutOfOrder(mod.Span, mod.Value, name, CanonicalOrderString, source);
                }
                else
                {
                    lastCategory = category;
                }
            }
        }
    }
}
