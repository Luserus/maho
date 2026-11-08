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

            if (isMarkedIntrinsic && attr.Name.Text != KnownIntrinsics.IntrinsicTypeAttribute)
            {
                var span = attr.Syntax?.Name.GetSpan() ?? default;
                context.Diagnostics.ReportUnknownIntrinsicAttribute(span, attr.Name.Text, attr.Syntax?.GetSource());
                continue;
            }

            if (attr.Name.Text == KnownIntrinsics.IntrinsicTypeAttribute)
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

        // Group 3: Str (requires Int8, Pointer, Int64)
        ValidateStr(typesByIntrinsic);

        // Group 4: Array (requires Pointer, Int64)
        ValidateArray(typesByIntrinsic);
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
            !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Pointer) ||
            !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Int64))
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Str, $"requires '{KnownIntrinsics.Int8}', '{KnownIntrinsics.Pointer}', and '{KnownIntrinsics.Int64}' to be declared first", syntax?.GetSource());
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
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Str, "must have exactly 2 instance fields (Int8* and Int64)", syntax?.GetSource());
            return;
        }

        // Field 1: Int8* or Pointer<Int8>
        var field1 = instanceFields[0];
        var field1Type = field1.Syntax?.Type;
        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int8, out var int8Handle);

        if (!IsPointerTo(field1Type, field1.EnclosingScope, int8Handle))
        {
            var span = field1.Syntax?.Type.GetSpan() ?? field1.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, symbol.Name.Text, KnownIntrinsics.Str, "first field must be a pointer type pointing to 'Int8' (e.g. Int8* or Pointer<Int8>)", field1.Syntax?.GetSource());
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
        string name = app.Name switch
        {
            SimpleName simple => simple.Name.Value,
            QualifiedName qual when qual.Parts.Count > 0 && qual.Parts[^1] is SimpleName lastSimple => lastSimple.Name.Value,
            _ => app.Name.ToString() ?? ""
        };

        if (name == KnownIntrinsics.IntrinsicTypeAttribute)
            return true;

        if (candidateAttr is not null && name == candidateAttr.Name.Text)
            return true;

        return false;
    }

    private static string? ExtractLiteralString(Expression expression)
    {
        if (expression is LiteralExpression lit && (lit.Literal.Kind is TokenKind.String or TokenKind.SuffixedString))
        {
            string val = lit.Literal.Value;

            if (val.Length >= 2 && val.StartsWith('"') && val.EndsWith('"'))
                return val.Substring(1, val.Length - 2);

            return val;
        }

        if (expression is NameofExpression nameofExpr && nameofExpr.Argument is NamedExpression named)
            return named.Identifier.Value;

        return null;
    }

    private SymbolHandle? ResolveTypeSymbol(TypeSyntax? syntax, Scope scope)
    {
        if (syntax is null)
            return null;

        SymbolName? symbolName = null;

        if (syntax is SimpleType simple)
            symbolName = ResolutionContext.GetSymbolName(simple);
        else if (syntax is QualifiedType qualified)
            symbolName = ResolutionContext.GetSymbolName(qualified);

        if (symbolName is not null)
        {
            var symbols = scope[symbolName.Value];

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
        }

        return null;
    }

    private SymbolHandle? ResolveGenericOrTypeSymbol(TypeSyntax? syntax, Scope scope)
    {
        if (syntax is SimpleType simple)
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
}
