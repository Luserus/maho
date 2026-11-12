global using SymbolHandle = (Maho.Resolution.SymbolKind Kind, Maho.Resolution.SymbolID ID);

using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>A full-fidelity representation of a type reference across symbols and declarations.</summary>
internal readonly struct TypeRef : IEquatable<TypeRef>
{
    public TypeRefKind Kind { get; }
    public SymbolHandle? Handle { get; }
    public SemanticType? SpecialType { get; }

    public bool IsSpecial => SpecialType is not null;

    public bool IsResolved => Kind switch
    {
        TypeRefKind.Resolved or TypeRefKind.Literal => true,
        TypeRefKind.Pointer or TypeRefKind.Reference or TypeRefKind.Array or
        TypeRefKind.FixedArray or TypeRefKind.Span or TypeRefKind.Optional or TypeRefKind.Tuple or
        TypeRefKind.Union or TypeRefKind.Untagged or TypeRefKind.Function or TypeRefKind.Delegate => SpecialType is not null && SpecialType.IsResolved,
        _ => false
    };

    public bool IsUnresolved => Kind switch
    {
        TypeRefKind.Unresolved => true,
        TypeRefKind.Pointer or TypeRefKind.Reference or TypeRefKind.Array or
        TypeRefKind.FixedArray or TypeRefKind.Span or TypeRefKind.Optional or TypeRefKind.Tuple or
        TypeRefKind.Union or TypeRefKind.Untagged or TypeRefKind.Function or TypeRefKind.Delegate => SpecialType is not null && !SpecialType.IsResolved && !SpecialType.IsError,
        _ => false
    };

    public bool IsInferred => Kind == TypeRefKind.Inferred;

    public bool IsLiteral => Kind == TypeRefKind.Literal;
    public CompileTimeLiteralType? AsLiteral => SpecialType as CompileTimeLiteralType;

    public bool IsError => Kind == TypeRefKind.Error || (SpecialType is not null && SpecialType.IsError);

    private TypeRef(TypeRefKind kind, SymbolHandle? handle, SemanticType? specialType = null)
    {
        Kind = kind;
        Handle = handle;
        SpecialType = specialType;
    }

    public static TypeRef Unresolved => new TypeRef(TypeRefKind.Unresolved, null);
    public static TypeRef Inferred => new TypeRef(TypeRefKind.Inferred, null);
    public static TypeRef Error => new TypeRef(TypeRefKind.Error, null);
    public static TypeRef Resolved(SymbolHandle handle) => new TypeRef(TypeRefKind.Resolved, handle);

    public static TypeRef Literal(CompileTimeLiteralType literal) =>
        new TypeRef(TypeRefKind.Literal, null, literal);

    public static TypeRef Literal(CompileTimeLiteralKind kind, string rawText) =>
        Literal(new CompileTimeLiteralType(kind, rawText));

    public static TypeRef Pointer(TypeRef element) =>
        new TypeRef(TypeRefKind.Pointer, null, new PointerSemanticType(element));

    public static TypeRef Reference(TypeRef element) =>
        new TypeRef(TypeRefKind.Reference, null, new ReferenceSemanticType(element));

    public static TypeRef Array(TypeRef element) =>
        new TypeRef(TypeRefKind.Array, null, new ArraySemanticType(element));

    public static TypeRef FixedArray(TypeRef element, Expression size) =>
        new TypeRef(TypeRefKind.FixedArray, null, new FixedArraySemanticType(element, size));

    public static TypeRef Span(TypeRef element) =>
        new TypeRef(TypeRefKind.Span, null, new SpanSemanticType(element));

    public static TypeRef Optional(TypeRef element) =>
        new TypeRef(TypeRefKind.Optional, null, new OptionalSemanticType(element));

    public static TypeRef Tuple(IReadOnlyList<TupleElement> elements) =>
        new TypeRef(TypeRefKind.Tuple, null, new TupleSemanticType(elements));

    public static TypeRef Union(IReadOnlyList<TypeRef> constituents, bool isUntagged = false)
    {
        var semantic = new UnionSemanticType(constituents, isUntagged);
        return new TypeRef(TypeRefKind.Union, null, semantic);
    }

    public static TypeRef Untagged(TypeRef inner)
    {
        if (inner.SpecialType is UnionSemanticType u)
            return Union(u.Variants, isUntagged: true);

        if (inner.Kind == TypeRefKind.Untagged)
            return inner;

        return new TypeRef(TypeRefKind.Untagged, null, new UntaggedSemanticType(inner));
    }

    public static TypeRef Function(TypeRef returnType, IReadOnlyList<TypeRef> parameterTypes) =>
        new TypeRef(TypeRefKind.Function, null, new FunctionSemanticType(returnType, parameterTypes));

    public static TypeRef Delegate(TypeRef returnType, IReadOnlyList<TypeRef> parameterTypes) =>
        new TypeRef(TypeRefKind.Delegate, null, new DelegateSemanticType(returnType, parameterTypes));

    public bool IsFunction => Kind == TypeRefKind.Function;
    public bool IsDelegate => Kind == TypeRefKind.Delegate;
    public FunctionSemanticType? AsFunction => SpecialType as FunctionSemanticType;
    public DelegateSemanticType? AsDelegate => SpecialType as DelegateSemanticType;

    public bool IsUntagged => Kind == TypeRefKind.Untagged || (SpecialType is UnionSemanticType u && u.IsUntagged);

    public static implicit operator TypeRef(SymbolHandle handle) => Resolved(handle);

    public bool Equals(TypeRef other)
    {
        if (Kind != other.Kind)
            return false;

        if (IsSpecial)
            return Equals(SpecialType, other.SpecialType);

        return Nullable.Equals(Handle, other.Handle);
    }

    public override bool Equals(object? obj) => obj is TypeRef other && Equals(other);

    public override int GetHashCode() => IsSpecial ? HashCode.Combine(Kind, SpecialType) : HashCode.Combine(Kind, Handle);

    public static bool operator ==(TypeRef left, TypeRef right) => left.Equals(right);
    public static bool operator !=(TypeRef left, TypeRef right) => !left.Equals(right);

    public static bool operator ==(TypeRef left, SymbolHandle right) => left.Kind == TypeRefKind.Resolved && left.Handle == right;
    public static bool operator !=(TypeRef left, SymbolHandle right) => !(left == right);

    public static bool operator ==(SymbolHandle left, TypeRef right) => right == left;
    public static bool operator !=(SymbolHandle left, TypeRef right) => !(right == left);

    /// <summary>
    /// Gets the underlying type <see cref="SymbolHandle"/> (global or nested) referenced by this <see cref="TypeRef"/>,
    /// unwrapping any aliases transitively. Returns <c>null</c> if unresolved, a special semantic type, or not a type.
    /// </summary>
    public SymbolHandle? GetType(ResolutionContext context) => context.GetType(this);

    /// <summary>
    /// Unwraps any aliases transitively, returning the underlying <see cref="TypeRef"/>.
    /// </summary>
    public TypeRef UnwrapAlias(ResolutionContext context) => context.UnwrapAlias(this);

    public bool IsAssignableFrom(TypeRef source, ResolutionContext context)
    {
        if (Equals(source))
            return true;

        if (SpecialType is UnionSemanticType targetUnion)
        {
            if (source.SpecialType is UnionSemanticType sourceUnion)
                return sourceUnion.Variants.All(sv => targetUnion.Variants.Any(tv => tv.IsAssignableFrom(sv, context)));

            return targetUnion.Variants.Any(v => v.IsAssignableFrom(source, context));
        }

        if (SpecialType is DelegateSemanticType targetDel)
        {
            if (source.SpecialType is FunctionSemanticType srcFn)
            {
                if (targetDel.ParameterTypes.Count == srcFn.ParameterTypes.Count &&
                    targetDel.ReturnType.IsAssignableFrom(srcFn.ReturnType, context))
                {
                    bool match = true;
                    for (int i = 0; i < targetDel.ParameterTypes.Count; i++)
                    {
                        if (!srcFn.ParameterTypes[i].IsAssignableFrom(targetDel.ParameterTypes[i], context))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return true;
                }
            }
            else if (source.SpecialType is DelegateSemanticType srcDel)
            {
                if (targetDel.ParameterTypes.Count == srcDel.ParameterTypes.Count &&
                    targetDel.ReturnType.IsAssignableFrom(srcDel.ReturnType, context))
                {
                    bool match = true;
                    for (int i = 0; i < targetDel.ParameterTypes.Count; i++)
                    {
                        if (!srcDel.ParameterTypes[i].IsAssignableFrom(targetDel.ParameterTypes[i], context))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return true;
                }
            }
        }

        if (SpecialType is FunctionSemanticType targetFn && source.SpecialType is FunctionSemanticType srcFn2)
        {
            if (targetFn.ParameterTypes.Count == srcFn2.ParameterTypes.Count &&
                targetFn.ReturnType.IsAssignableFrom(srcFn2.ReturnType, context))
            {
                bool match = true;
                for (int i = 0; i < targetFn.ParameterTypes.Count; i++)
                {
                    if (!srcFn2.ParameterTypes[i].IsAssignableFrom(targetFn.ParameterTypes[i], context))
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true;
            }
        }

        if (Handle is { } thisHandle && context.GetTypeSymbol(thisHandle) is ISumTypeSymbol sumType && sumType.UnionConstituents.Count > 0)
        {
            return sumType.UnionConstituents.Any(c => c.IsAssignableFrom(source, context));
        }

        if (Handle is { } targetH)
        {
            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.ValueType, out var valTypeH) && targetH == valTypeH)
            {
                if (source.IsFunction || source.IsDelegate) return true;
                if (source.Handle is { } srcH)
                {
                    if (srcH == valTypeH) return true;
                    if (IsSubtypeOf(srcH, valTypeH, context)) return true;
                }
            }

            if (source.Handle is { } srcH2)
            {
                if (srcH2 == targetH) return true;
                if (IsSubtypeOf(srcH2, targetH, context)) return true;
            }
        }

        if (source.IsLiteral && source.AsLiteral is { } lit)
        {
            if (Handle is { } targetHandle)
            {
                if (lit.LiteralKind == CompileTimeLiteralKind.Int)
                {
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int32, out var h32) && targetHandle == h32) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int64, out var h64) && targetHandle == h64) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int8, out var h8) && targetHandle == h8) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int16, out var h16) && targetHandle == h16) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int128, out var h128) && targetHandle == h128) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.NInt, out var hn) && targetHandle == hn) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.UInt8, out var uh8) && targetHandle == uh8) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.UInt16, out var uh16) && targetHandle == uh16) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.UInt32, out var uh32) && targetHandle == uh32) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.UInt64, out var uh64) && targetHandle == uh64) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.UInt128, out var uh128) && targetHandle == uh128) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.NUInt, out var uhn) && targetHandle == uhn) return true;

                    var sym = context.GetSymbol(targetHandle);
                    if (sym is TypeSymbol or NestedTypeSymbol)
                    {
                        var name = sym.Name.Text;
                        if (name.Equals("int", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("int32", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("int64", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("int8", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("int16", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("int128", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("uint8", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("uint16", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("uint32", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("uint64", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("uint128", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                else if (lit.LiteralKind == CompileTimeLiteralKind.Float)
                {
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Float64, out var f64) && targetHandle == f64) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Float32, out var f32) && targetHandle == f32) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Float16, out var f16) && targetHandle == f16) return true;
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Float128, out var f128) && targetHandle == f128) return true;

                    var sym = context.GetSymbol(targetHandle);
                    if (sym is TypeSymbol or NestedTypeSymbol)
                    {
                        var name = sym.Name.Text;
                        if (name.Equals("float", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("float32", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("float64", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("float16", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("float128", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                else if (lit.LiteralKind == CompileTimeLiteralKind.Str)
                {
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Str, out var strHandle) && targetHandle == strHandle) return true;

                    var sym = context.GetSymbol(targetHandle);
                    if (sym is TypeSymbol or NestedTypeSymbol)
                    {
                        var name = sym.Name.Text;
                        if (name.Equals("str", StringComparison.OrdinalIgnoreCase) || name.Equals("string", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                else if (lit.LiteralKind == CompileTimeLiteralKind.Char)
                {
                    if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Char, out var charHandle) && targetHandle == charHandle) return true;

                    var sym = context.GetSymbol(targetHandle);
                    if (sym is TypeSymbol or NestedTypeSymbol)
                    {
                        var name = sym.Name.Text;
                        if (name.Equals("char", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsSubtypeOf(SymbolHandle subHandle, SymbolHandle superHandle, ResolutionContext context, HashSet<SymbolHandle>? visited = null)
    {
        if (subHandle == superHandle)
            return true;

        visited ??= [];
        if (!visited.Add(subHandle))
            return false;

        var sym = context.GetSymbol(subHandle);
        IReadOnlyList<TypeRef>? bases = sym switch
        {
            TypeSymbol ts => ts.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            _ => null
        };

        if (bases is not null)
        {
            foreach (var b in bases)
            {
                if (b.Handle is { } bHandle)
                {
                    if (IsSubtypeOf(bHandle, superHandle, context, visited))
                        return true;
                }
            }
        }

        return false;
    }

    public override string ToString() => Kind switch
    {
        TypeRefKind.Resolved => Handle.ToString() ?? "Resolved",
        TypeRefKind.Inferred => "var",
        TypeRefKind.Error => "<error>",
        TypeRefKind.Literal => SpecialType?.ToString() ?? "<literal>",
        TypeRefKind.Pointer or TypeRefKind.Reference or TypeRefKind.Array or
        TypeRefKind.FixedArray or TypeRefKind.Span or TypeRefKind.Optional or TypeRefKind.Tuple or
        TypeRefKind.Union or TypeRefKind.Untagged or TypeRefKind.Function or TypeRefKind.Delegate => SpecialType?.ToString() ?? Kind.ToString(),
        _ => "<unresolved>"
    };

    public string ToDisplayString(ResolutionContext? context = null)
    {
        if (context is not null && Kind == TypeRefKind.Resolved && Handle is { } handle)
        {
            if (context.GetTypeSymbol(handle) is { } typeSym)
                return typeSym.Name.Text;
            if (context.GetGenericParameterSymbol(handle) is { } gpSym)
                return gpSym.Name.Text;
            if (context.GetAliasSymbol(handle) is { } aliasSym)
                return aliasSym.Name.Text;
        }

        if (SpecialType is not null)
            return SpecialType.ToDisplayString(context);

        return ToString();
    }
}