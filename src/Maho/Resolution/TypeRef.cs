global using SymbolHandle = (Maho.Resolution.SymbolKind Kind, Maho.Resolution.SymbolID ID);

using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Resolution.Layout;
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
        TypeRefKind.Resolved => SpecialType is null || SpecialType.IsResolved,
        TypeRefKind.Literal or TypeRefKind.Never => true,
        TypeRefKind.Pointer or TypeRefKind.Reference or TypeRefKind.Array or
        TypeRefKind.FixedArray or TypeRefKind.Span or TypeRefKind.Optional or TypeRefKind.Tuple or
        TypeRefKind.Union or TypeRefKind.Untagged or TypeRefKind.Dyn or TypeRefKind.Function or TypeRefKind.Delegate or TypeRefKind.Alias => SpecialType is not null && SpecialType.IsResolved,
        _ => false
    };

    public bool IsUnresolved => Kind switch
    {
        TypeRefKind.Unresolved => true,
        TypeRefKind.Resolved => SpecialType is not null && !SpecialType.IsResolved && !SpecialType.IsError,
        TypeRefKind.Pointer or TypeRefKind.Reference or TypeRefKind.Array or
        TypeRefKind.FixedArray or TypeRefKind.Span or TypeRefKind.Optional or TypeRefKind.Tuple or
        TypeRefKind.Union or TypeRefKind.Untagged or TypeRefKind.Dyn or TypeRefKind.Function or TypeRefKind.Delegate or TypeRefKind.Alias => SpecialType is not null && !SpecialType.IsResolved && !SpecialType.IsError,
        _ => false
    };

    public bool IsInferred => Kind == TypeRefKind.Inferred;

    public bool IsNever => Kind == TypeRefKind.Never;

    public bool IsLiteral => Kind == TypeRefKind.Literal;
    public CompileTimeLiteralType? AsLiteral => SpecialType as CompileTimeLiteralType;

    public bool IsDyn => Kind == TypeRefKind.Dyn;
    public DynSemanticType? AsDyn => SpecialType as DynSemanticType;

    public bool IsSized => SpecialType is not DynSemanticType;

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
    public static TypeRef Never => new TypeRef(TypeRefKind.Never, null);
    public static TypeRef Resolved(SymbolHandle handle) => new TypeRef(TypeRefKind.Resolved, handle);

    public static TypeRef Literal(CompileTimeLiteralType literal) =>
        new TypeRef(TypeRefKind.Literal, null, literal);

    public static TypeRef Literal(CompileTimeLiteralKind kind, string rawText) =>
        Literal(new CompileTimeLiteralType(kind, rawText));

    public static TypeRef Pointer(TypeRef element) =>
        new TypeRef(TypeRefKind.Pointer, null, new PointerSemanticType(element));

    public static TypeRef Reference(TypeRef element) =>
        new TypeRef(TypeRefKind.Reference, null, new ReferenceSemanticType(element));

    public static TypeRef Alias(TypeRef element, bool isMutable = false) =>
        new TypeRef(TypeRefKind.Alias, null, new AliasSemanticType(element, isMutable));

    public static TypeRef Array(TypeRef element) =>
        new TypeRef(TypeRefKind.Array, null, new ArraySemanticType(element));

    public static TypeRef FixedArray(TypeRef element, Expression size, SymbolHandle? fixedArrayHandle = null) =>
        new TypeRef(TypeRefKind.FixedArray, fixedArrayHandle, new FixedArraySemanticType(element, size));

    public static TypeRef FixedArray(TypeRef element, int count, SymbolHandle? fixedArrayHandle = null) =>
        new TypeRef(TypeRefKind.FixedArray, fixedArrayHandle, new FixedArraySemanticType(element, new LiteralExpression(new Token(new Text.SourceText(count.ToString()), default, TokenKind.Integer, [], [], MatchingKeywordKind.None, count.ToString()))));

    public static TypeRef Span(TypeRef element) =>
        new TypeRef(TypeRefKind.Span, null, new SpanSemanticType(element));

    public static TypeRef Optional(TypeRef element, SymbolHandle? optionalHandle = null) =>
        new TypeRef(TypeRefKind.Optional, optionalHandle, new OptionalSemanticType(element));

    /// <summary>
    /// Creates a tuple <see cref="TypeRef"/> holding the element types and optional intrinsic <c>Tuple</c> symbol handle.
    /// </summary>
    public static TypeRef Tuple(IReadOnlyList<TupleElement> elements, SymbolHandle? tupleHandle = null) =>
        new TypeRef(TypeRefKind.Tuple, tupleHandle, new TupleSemanticType(elements));

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

    public static TypeRef Dyn(TypeRef targetType) =>
        new TypeRef(TypeRefKind.Dyn, targetType.Handle, new DynSemanticType(targetType));

    public static TypeRef Dyn(TypeRef targetType, IReadOnlyList<TypeRef> composed) =>
        new TypeRef(TypeRefKind.Dyn, targetType.Handle, new DynSemanticType(targetType, composed));

    public static TypeRef Function(TypeRef returnType, IReadOnlyList<TypeRef> parameterTypes) =>
        new TypeRef(TypeRefKind.Function, null, new FunctionSemanticType(returnType, parameterTypes));

    public static TypeRef Delegate(TypeRef returnType, IReadOnlyList<TypeRef> parameterTypes) =>
        new TypeRef(TypeRefKind.Delegate, null, new DelegateSemanticType(returnType, parameterTypes));

    public static TypeRef Generic(SymbolHandle targetHandle, string targetName, IReadOnlyList<TypeRef> typeArguments) =>
        new TypeRef(TypeRefKind.Resolved, targetHandle, new GenericSemanticType(targetHandle, targetName, typeArguments));

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

        if (SpecialType is not null && other.SpecialType is not null)
            return Equals(SpecialType, other.SpecialType);

        if (SpecialType is GenericSemanticType gen)
            return Nullable.Equals(gen.TargetHandle, other.Handle);

        if (other.SpecialType is GenericSemanticType otherGen)
            return Nullable.Equals(otherGen.TargetHandle, Handle);

        if (IsSpecial || other.IsSpecial)
            return false;

        return Nullable.Equals(Handle, other.Handle);
    }

    public override bool Equals(object? obj) => obj is TypeRef other && Equals(other);

    public override int GetHashCode()
    {
        if (SpecialType is GenericSemanticType gen)
            return HashCode.Combine(Kind, gen.TargetHandle);

        return IsSpecial ? HashCode.Combine(Kind, SpecialType) : HashCode.Combine(Kind, Handle);
    }

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

        if (source.IsNever)
            return true;

        if (IsNever)
            return source.IsNever;

        if (SpecialType is UnionSemanticType targetUnion)
        {
            if (source.SpecialType is UnionSemanticType sourceUnion)
                return sourceUnion.Variants.All(sv => targetUnion.Variants.Any(tv => tv.IsAssignableFrom(sv, context)));

            return targetUnion.Variants.Any(v => v.IsAssignableFrom(source, context));
        }

        if (SpecialType is DynSemanticType targetDyn)
        {
            if (source.SpecialType is DynSemanticType sourceDyn)
                return targetDyn.TargetType.IsAssignableFrom(sourceDyn.TargetType, context);

            return targetDyn.TargetType.IsAssignableFrom(source, context);
        }

        if (SpecialType is AliasSemanticType targetAlias)
        {
            if (source.SpecialType is AliasSemanticType sourceAlias)
                return targetAlias.TargetType.IsAssignableFrom(sourceAlias.TargetType, context);

            return targetAlias.TargetType.IsAssignableFrom(source, context);
        }

        if (source.SpecialType is AliasSemanticType srcAlias)
            return IsAssignableFrom(srcAlias.TargetType, context);

        if (SpecialType is ReferenceSemanticType targetRef)
        {
            if (source.SpecialType is ReferenceSemanticType sourceRef)
            {
                if (targetRef.ElementType.SpecialType is DynSemanticType targetDynElem)
                    return targetDynElem.TargetType.IsAssignableFrom(sourceRef.ElementType, context);

                if (IsNonContiguousSplitBase(this, source, context, out _))
                    return false;

                return targetRef.ElementType.IsAssignableFrom(sourceRef.ElementType, context);
            }
        }

        if (SpecialType is PointerSemanticType targetPtr)
        {
            if (source.SpecialType is PointerSemanticType sourcePtr)
            {
                if (targetPtr.ElementType.SpecialType is DynSemanticType targetDynElem)
                    return targetDynElem.TargetType.IsAssignableFrom(sourcePtr.ElementType, context);

                if (IsNonContiguousSplitBase(this, source, context, out _))
                    return false;

                return targetPtr.ElementType.IsAssignableFrom(sourcePtr.ElementType, context);
            }
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
            var unwrappedTargetH = context.UnwrapAlias(targetH);
            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.ValueType, out var valTypeH) && unwrappedTargetH == valTypeH)
            {
                if (source.IsFunction || source.IsDelegate) return true;
                if (source.Handle is { } srcH)
                {
                    var unwrappedSrcH = context.UnwrapAlias(srcH);
                    if (unwrappedSrcH == valTypeH) return true;
                    if (IsSubtypeOf(unwrappedSrcH, valTypeH, context)) return true;
                }
            }

            if (source.Handle is { } srcH2)
            {
                var unwrappedSrcH2 = context.UnwrapAlias(srcH2);
                if (unwrappedSrcH2 == unwrappedTargetH)
                {
                    if (SpecialType is GenericSemanticType targetGen && source.SpecialType is GenericSemanticType srcGen)
                    {
                        if (targetGen.TypeArguments.Count == srcGen.TypeArguments.Count)
                        {
                            bool match = true;
                            for (int i = 0; i < targetGen.TypeArguments.Count; i++)
                            {
                                if (!targetGen.TypeArguments[i].IsAssignableFrom(srcGen.TypeArguments[i], context))
                                {
                                    match = false;
                                    break;
                                }
                            }
                            if (match) return true;
                        }
                        return false;
                    }
                    return true;
                }

                if (IsSubtypeOf(unwrappedSrcH2, unwrappedTargetH, context)) return true;
            }
        }

        if (SpecialType is GenericSemanticType genTarget)
        {
            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Function, out var fnH) && context.UnwrapAlias(genTarget.TargetHandle) == fnH &&
                source.SpecialType is FunctionSemanticType srcFn)
            {
                if (genTarget.TypeArguments.Count == srcFn.ParameterTypes.Count + 1 &&
                    genTarget.TypeArguments[0].IsAssignableFrom(srcFn.ReturnType, context))
                {
                    bool match = true;
                    for (int i = 0; i < srcFn.ParameterTypes.Count; i++)
                    {
                        if (!srcFn.ParameterTypes[i].IsAssignableFrom(genTarget.TypeArguments[i + 1], context))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return true;
                }
            }
            else if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Delegate, out var delH) && context.UnwrapAlias(genTarget.TargetHandle) == delH &&
                     source.SpecialType is DelegateSemanticType srcDel)
            {
                if (genTarget.TypeArguments.Count == srcDel.ParameterTypes.Count + 1 &&
                    genTarget.TypeArguments[0].IsAssignableFrom(srcDel.ReturnType, context))
                {
                    bool match = true;
                    for (int i = 0; i < srcDel.ParameterTypes.Count; i++)
                    {
                        if (!srcDel.ParameterTypes[i].IsAssignableFrom(genTarget.TypeArguments[i + 1], context))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return true;
                }
            }
        }

        if (source.SpecialType is GenericSemanticType genSource)
        {
            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Function, out var fnH2) && context.UnwrapAlias(genSource.TargetHandle) == fnH2 &&
                SpecialType is FunctionSemanticType targetFn2)
            {
                if (genSource.TypeArguments.Count == targetFn2.ParameterTypes.Count + 1 &&
                    targetFn2.ReturnType.IsAssignableFrom(genSource.TypeArguments[0], context))
                {
                    bool match = true;
                    for (int i = 0; i < targetFn2.ParameterTypes.Count; i++)
                    {
                        if (!targetFn2.ParameterTypes[i].IsAssignableFrom(genSource.TypeArguments[i + 1], context))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return true;
                }
            }
            else if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Delegate, out var delH2) && context.UnwrapAlias(genSource.TargetHandle) == delH2 &&
                     SpecialType is DelegateSemanticType targetDel2)
            {
                if (genSource.TypeArguments.Count == targetDel2.ParameterTypes.Count + 1 &&
                    targetDel2.ReturnType.IsAssignableFrom(genSource.TypeArguments[0], context))
                {
                    bool match = true;
                    for (int i = 0; i < targetDel2.ParameterTypes.Count; i++)
                    {
                        if (!targetDel2.ParameterTypes[i].IsAssignableFrom(genSource.TypeArguments[i + 1], context))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return true;
                }
            }
        }

        if (source.IsLiteral && source.AsLiteral is { } lit)
        {
            if (Handle is { } targetHandle)
            {
                return lit.LiteralKind switch
                {
                    CompileTimeLiteralKind.Int => IsIntegerTypeSymbol(targetHandle, context),
                    CompileTimeLiteralKind.Float => IsFloatTypeSymbol(targetHandle, context),
                    CompileTimeLiteralKind.Str => IsStringTypeSymbol(targetHandle, context),
                    CompileTimeLiteralKind.Char => IsCharTypeSymbol(targetHandle, context),
                    _ => false
                };
            }
        }

        if (IsLiteral && AsLiteral is { } targetLit)
        {
            if (source.IsLiteral && source.AsLiteral is { } srcLit)
                return targetLit.LiteralKind == srcLit.LiteralKind;

            if (source.Handle is { } srcHandle)
            {
                return targetLit.LiteralKind switch
                {
                    CompileTimeLiteralKind.Int => IsIntegerTypeSymbol(srcHandle, context),
                    CompileTimeLiteralKind.Float => IsFloatTypeSymbol(srcHandle, context),
                    CompileTimeLiteralKind.Str => IsStringTypeSymbol(srcHandle, context),
                    CompileTimeLiteralKind.Char => IsCharTypeSymbol(srcHandle, context),
                    _ => false
                };
            }
        }

        return false;
    }

    private static bool IsIntegerTypeSymbol(SymbolHandle handle, ResolutionContext context)
    {
        handle = context.UnwrapAlias(handle);
        if (context.DiscoveredIntrinsics.TryGetIntrinsicName(handle, out var intrinsicName))
            return KnownIntrinsics.IsIntegerType(intrinsicName);

        var sym = context.GetSymbol(handle);
        return sym is not null && KnownIntrinsics.IsIntegerType(sym.Name.Text);
    }

    private static bool IsFloatTypeSymbol(SymbolHandle handle, ResolutionContext context)
    {
        handle = context.UnwrapAlias(handle);
        if (context.DiscoveredIntrinsics.TryGetIntrinsicName(handle, out var intrinsicName))
            return KnownIntrinsics.IsFloatType(intrinsicName);

        var sym = context.GetSymbol(handle);
        return sym is not null && KnownIntrinsics.IsFloatType(sym.Name.Text);
    }

    private static bool IsStringTypeSymbol(SymbolHandle handle, ResolutionContext context)
    {
        handle = context.UnwrapAlias(handle);
        if (context.DiscoveredIntrinsics.TryGetIntrinsicName(handle, out var intrinsicName))
            return intrinsicName == KnownIntrinsics.Str;

        var sym = context.GetSymbol(handle);
        return sym is not null && (sym.Name.Text.Equals(KnownIntrinsics.Str, StringComparison.OrdinalIgnoreCase) || sym.Name.Text.Equals("string", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCharTypeSymbol(SymbolHandle handle, ResolutionContext context)
    {
        handle = context.UnwrapAlias(handle);
        if (context.DiscoveredIntrinsics.TryGetIntrinsicName(handle, out var intrinsicName))
            return intrinsicName == KnownIntrinsics.Char;

        var sym = context.GetSymbol(handle);
        return sym is not null && (sym.Name.Text.Equals(KnownIntrinsics.Char, StringComparison.OrdinalIgnoreCase) || sym.Name.Text.Equals("char", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsSubtypeOf(SymbolHandle subHandle, SymbolHandle superHandle, ResolutionContext context, HashSet<SymbolHandle>? visited = null)
    {
        subHandle = context.UnwrapAlias(subHandle);
        superHandle = context.UnwrapAlias(superHandle);
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

    public static bool IsNonContiguousSplitBase(TypeRef target, TypeRef source, ResolutionContext context, out string? baseName)
    {
        baseName = null;
        TypeRef targetElem;
        TypeRef sourceElem;

        if (target.SpecialType is ReferenceSemanticType targetRef && source.SpecialType is ReferenceSemanticType sourceRef)
        {
            targetElem = targetRef.ElementType;
            sourceElem = sourceRef.ElementType;
        }
        else if (target.SpecialType is PointerSemanticType targetPtr && source.SpecialType is PointerSemanticType sourcePtr)
        {
            targetElem = targetPtr.ElementType;
            sourceElem = sourcePtr.ElementType;
        }
        else
            return false;

        if (targetElem.IsDyn)
            return false;

        if (targetElem.Handle is { } targetHandle && sourceElem.Handle is { } sourceHandle)
        {
            targetHandle = context.UnwrapAlias(targetHandle);
            sourceHandle = context.UnwrapAlias(sourceHandle);

            if (targetHandle != sourceHandle && IsSubtypeOf(sourceHandle, targetHandle, context))
            {
                if (!TypeContiguityAnalyzer.IsBaseContiguous(sourceHandle, targetHandle, context))
                {
                    baseName = context.GetTypeSymbol(targetHandle)?.Name.Text ?? "Base";
                    return true;
                }
            }
        }

        return false;
    }

    public TypeRef Substitute(Dictionary<SymbolHandle, TypeRef> typeArgs)
    {
        if (typeArgs.Count == 0)
            return this;

        if (Handle is { } h && typeArgs.TryGetValue(h, out var substituted))
            return substituted;

        if (SpecialType is OptionalSemanticType o)
            return Optional(o.ElementType.Substitute(typeArgs), Handle);

        if (SpecialType is UnionSemanticType u)
            return Union([.. u.Variants.Select(v => v.Substitute(typeArgs))], u.IsUntagged);

        if (SpecialType is PointerSemanticType p)
            return Pointer(p.ElementType.Substitute(typeArgs));

        if (SpecialType is ReferenceSemanticType r)
            return Reference(r.ElementType.Substitute(typeArgs));

        if (SpecialType is AliasSemanticType al)
            return Alias(al.TargetType.Substitute(typeArgs), al.IsMutable);

        if (SpecialType is ArraySemanticType a)
            return Array(a.ElementType.Substitute(typeArgs));

        if (SpecialType is SpanSemanticType s)
            return Span(s.ElementType.Substitute(typeArgs));

        if (SpecialType is FixedArraySemanticType f)
            return FixedArray(f.ElementType.Substitute(typeArgs), f.Size, Handle);

        if (SpecialType is DynSemanticType dyn)
            return Dyn(dyn.TargetType.Substitute(typeArgs));

        if (SpecialType is UntaggedSemanticType untagged)
            return Untagged(untagged.InnerType.Substitute(typeArgs));

        if (SpecialType is GenericSemanticType gen)
        {
            var newArgs = gen.TypeArguments.Select(arg => arg.Substitute(typeArgs)).ToList();
            return Generic(gen.TargetHandle, gen.TargetName, newArgs);
        }

        return this;
    }

    public override string ToString() => Kind switch
    {
        TypeRefKind.Resolved => Handle.ToString() ?? "Resolved",
        TypeRefKind.Inferred => "var",
        TypeRefKind.Never => "!",
        TypeRefKind.Error => "<error>",
        TypeRefKind.Literal => SpecialType?.ToString() ?? "<literal>",
        TypeRefKind.Pointer or TypeRefKind.Reference or TypeRefKind.Array or
        TypeRefKind.FixedArray or TypeRefKind.Span or TypeRefKind.Optional or TypeRefKind.Tuple or
        TypeRefKind.Union or TypeRefKind.Untagged or TypeRefKind.Dyn or TypeRefKind.Function or TypeRefKind.Delegate or TypeRefKind.Alias => SpecialType?.ToString() ?? Kind.ToString(),
        _ => "<unresolved>"
    };

    public string ToDisplayString(ResolutionContext? context = null)
    {
        if (Kind == TypeRefKind.Never)
            return "!";

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

    public string ToQualifiedString(ResolutionContext? context = null)
    {
        if (Kind == TypeRefKind.Never)
            return "!";

        if (Kind == TypeRefKind.Error)
            return "<error>";

        if (Kind == TypeRefKind.Inferred)
            return "var";

        if (SpecialType is FunctionSemanticType fn)
        {
            string qualifiedFnName = (context != null && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Function, out var fnH))
                ? ResolutionContext.GetQualifiedSymbolName(fnH, context)
                : KnownIntrinsics.Function;

            var retStr = fn.ReturnType.ToDisplayString(context);
            var paramStrs = fn.ParameterTypes.Count > 0 ? ", " + string.Join(", ", fn.ParameterTypes.Select(p => p.ToDisplayString(context))) : "";
            return $"{qualifiedFnName}<{retStr}{paramStrs}>";
        }

        if (SpecialType is DelegateSemanticType del)
        {
            string qualifiedDelName = (context != null && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Delegate, out var delH))
                ? ResolutionContext.GetQualifiedSymbolName(delH, context)
                : KnownIntrinsics.Delegate;

            var retStr = del.ReturnType.ToDisplayString(context);
            var paramStrs = del.ParameterTypes.Count > 0 ? ", " + string.Join(", ", del.ParameterTypes.Select(p => p.ToDisplayString(context))) : "";
            return $"{qualifiedDelName}<{retStr}{paramStrs}>";
        }

        if (SpecialType is GenericSemanticType gen)
        {
            var targetName = ResolutionContext.GetQualifiedSymbolName(gen.TargetHandle, context);
            return $"{targetName}<{string.Join(", ", gen.TypeArguments.Select(a => a.ToDisplayString(context)))}>";
        }

        if (SpecialType is OptionalSemanticType opt)
        {
            string qualifiedOptName = (context != null && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var optH))
                ? ResolutionContext.GetQualifiedSymbolName(optH, context)
                : KnownIntrinsics.Optional;
            return $"{qualifiedOptName}<{opt.ElementType.ToDisplayString(context)}>";
        }

        if (SpecialType is FixedArraySemanticType fix)
        {
            string qualifiedArrName = (context != null && (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.FixedArray, out var arrH) || context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out arrH)))
                ? ResolutionContext.GetQualifiedSymbolName(arrH, context)
                : KnownIntrinsics.Array;
            return $"{qualifiedArrName}<{fix.ElementType.ToDisplayString(context)}, {fix.Size}>";
        }

        if (SpecialType is ArraySemanticType arr)
        {
            string qualifiedArrName = (context != null && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var arrH))
                ? ResolutionContext.GetQualifiedSymbolName(arrH, context)
                : KnownIntrinsics.Array;
            return $"{qualifiedArrName}<{arr.ElementType.ToDisplayString(context)}>";
        }

        if (SpecialType is SpanSemanticType span)
        {
            string qualifiedSpanName = (context != null && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Span, out var spanH))
                ? ResolutionContext.GetQualifiedSymbolName(spanH, context)
                : KnownIntrinsics.Span;
            return $"{qualifiedSpanName}<{span.ElementType.ToDisplayString(context)}>";
        }

        if (SpecialType is PointerSemanticType ptr)
        {
            string qualifiedPtrName = (context != null && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Pointer, out var ptrH))
                ? ResolutionContext.GetQualifiedSymbolName(ptrH, context)
                : KnownIntrinsics.Pointer;
            return $"{qualifiedPtrName}<{ptr.ElementType.ToDisplayString(context)}>";
        }

        if (SpecialType is ReferenceSemanticType refType)
        {
            string qualifiedRefName = (context != null && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Reference, out var refH))
                ? ResolutionContext.GetQualifiedSymbolName(refH, context)
                : KnownIntrinsics.Reference;
            return $"{qualifiedRefName}<{refType.ElementType.ToDisplayString(context)}>";
        }

        if (SpecialType is TupleSemanticType tup)
            return $"({string.Join(", ", tup.Elements.Select(e => e.ToDisplayString(context)))})";

        if (SpecialType is UnionSemanticType union)
            return union.ToDisplayString(context);

        if (SpecialType is UntaggedSemanticType untagged)
            return $"untagged {untagged.InnerType.ToQualifiedString(context)}";

        if (SpecialType is DynSemanticType dynType)
            return $"dyn {dynType.TargetType.ToQualifiedString(context)}";

        if (Handle is { } handle)
            return ResolutionContext.GetQualifiedSymbolName(handle, context);

        if (SpecialType is not null)
            return SpecialType.ToDisplayString(context);

        return ToString();
    }

    public string ToLabelString(ResolutionContext? context = null, Scope? scope = null)
    {
        if (Kind == TypeRefKind.Never)
            return "!";

        if (Kind == TypeRefKind.Error)
            return "<error>";

        if (Kind == TypeRefKind.Inferred)
            return "var";

        if (Kind == TypeRefKind.Literal)
            return SpecialType?.ToString() ?? "<literal>";

        if (SpecialType is FunctionSemanticType fn)
            return $"func({string.Join(", ", fn.ParameterTypes.Select(p => p.ToLabelString(context, scope)))}) -> {fn.ReturnType.ToLabelString(context, scope)}";

        if (SpecialType is DelegateSemanticType del)
            return $"delegate({string.Join(", ", del.ParameterTypes.Select(p => p.ToLabelString(context, scope)))}) -> {del.ReturnType.ToLabelString(context, scope)}";

        if (SpecialType is GenericSemanticType gen)
        {
            var targetLabel = ResolutionContext.GetLabelSymbolName(gen.TargetHandle, context, scope);
            return $"{targetLabel}<{string.Join(", ", gen.TypeArguments.Select(a => a.ToLabelString(context, scope)))}>";
        }

        if (SpecialType is OptionalSemanticType opt)
            return $"{opt.ElementType.ToLabelString(context, scope)}?";

        if (SpecialType is FixedArraySemanticType fix)
            return $"{fix.ElementType.ToLabelString(context, scope)}[{fix.Size}]";

        if (SpecialType is ArraySemanticType arr)
            return $"{arr.ElementType.ToLabelString(context, scope)}[]";

        if (SpecialType is SpanSemanticType span)
            return $"&{span.ElementType.ToLabelString(context, scope)}";

        if (SpecialType is PointerSemanticType ptr)
            return $"{ptr.ElementType.ToLabelString(context, scope)}*";

        if (SpecialType is ReferenceSemanticType refType)
            return $"{refType.ElementType.ToLabelString(context, scope)}&";

        if (SpecialType is AliasSemanticType aliasLabel)
            return aliasLabel.IsMutable ? $"mut ref {aliasLabel.TargetType.ToLabelString(context, scope)}" : $"ref {aliasLabel.TargetType.ToLabelString(context, scope)}";

        if (SpecialType is TupleSemanticType tup)
            return $"({string.Join(", ", tup.Elements.Select(e => e.Type.ToLabelString(context, scope) + (e.Name is not null ? " " + e.Name : ""))!)})";

        if (SpecialType is UnionSemanticType union)
            return $"({string.Join(" | ", union.Variants.Select(v => v.ToLabelString(context, scope)))})";

        if (SpecialType is UntaggedSemanticType untagged)
            return $"untagged {untagged.InnerType.ToLabelString(context, scope)}";

        if (SpecialType is DynSemanticType dynLabel)
            return $"dyn {dynLabel.TargetType.ToLabelString(context, scope)}";

        if (Handle is { } handle)
            return ResolutionContext.GetLabelSymbolName(handle, context, scope);

        if (SpecialType is not null)
            return SpecialType.ToDisplayString(context);

        return ToString();
    }
}
