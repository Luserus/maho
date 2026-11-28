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
        TypeRefKind.Pointer or TypeRefKind.Reference or TypeRefKind.Slice or
        TypeRefKind.Array or TypeRefKind.Optional or TypeRefKind.Tuple or
        TypeRefKind.Union or TypeRefKind.Untagged or TypeRefKind.Dyn or TypeRefKind.Function or TypeRefKind.Alias => SpecialType is not null && SpecialType.IsResolved,
        _ => false
    };

    public bool IsUnresolved => Kind switch
    {
        TypeRefKind.Unresolved => true,
        TypeRefKind.Resolved => SpecialType is not null && !SpecialType.IsResolved && !SpecialType.IsError,
        TypeRefKind.Pointer or TypeRefKind.Reference or TypeRefKind.Slice or
        TypeRefKind.Array or TypeRefKind.Optional or TypeRefKind.Tuple or
        TypeRefKind.Union or TypeRefKind.Untagged or TypeRefKind.Dyn or TypeRefKind.Function or TypeRefKind.Alias => SpecialType is not null && !SpecialType.IsResolved && !SpecialType.IsError,
        _ => false
    };

    public bool IsInferred => Kind == TypeRefKind.Inferred;

    public bool IsNever(ResolutionContext? context = null)
    {
        if (Kind == TypeRefKind.Never)
            return true;

        if (context is not null && Handle is { } h)
        {
            if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Never, out var neverHandle) &&
                context.UnwrapAlias(h) == neverHandle)
                return true;
        }

        return false;
    }

    public bool IsLiteral => Kind == TypeRefKind.Literal;
    public CompileTimeLiteralType? AsLiteral => SpecialType as CompileTimeLiteralType;

    public bool IsDyn => Kind == TypeRefKind.Dyn;
    public DynSemanticType? AsDyn => SpecialType as DynSemanticType;

    /// <summary>
    /// Determines whether this type has a statically known compile-time size (i.e. satisfies <c>Sized</c>).
    /// Indirections (<c>&amp;T</c>, <c>T&amp;</c>, <c>T*</c>, <c>dyn &amp;T</c>, <c>dyn T&amp;</c>, <c>Box&lt;T&gt;</c>)
    /// are always sized; slices, interfaces, bare <c>dyn Interface</c>, <c>Layout</c>/<c>Unsized</c> generic
    /// parameters, and structs with an unsized tail field are unsized.
    /// </summary>
    public bool IsSized(ResolutionContext context, HashSet<SymbolHandle>? visited = null)
    {
        if (Kind is TypeRefKind.Unresolved or TypeRefKind.Error or TypeRefKind.Inferred or TypeRefKind.Never or TypeRefKind.Literal or TypeRefKind.Function)
            return true;

        if (SpecialType is not null)
        {
            switch (SpecialType)
            {
                case PointerSemanticType:
                case ReferenceSemanticType:
                case AliasSemanticType:
                case OptionalSemanticType:
                case UntaggedSemanticType:
                case UnionSemanticType:
                case FunctionSemanticType:
                case CompileTimeLiteralType:
                    return true;

                case SliceSemanticType:
                    return false;

                case ArraySemanticType arr:
                    return arr.ElementType.IsSized(context, visited);

                case TupleSemanticType tuple:
                    return tuple.Elements.Count == 0 || tuple.Elements.All(e => e.Type.IsSized(context, visited));

                case DynSemanticType dyn:
                    return dyn.TargetType.SpecialType is AliasSemanticType or ReferenceSemanticType or PointerSemanticType;

                case ConformanceSemanticType conf:
                    return conf.TargetType.IsSized(context, visited);

                case GenericSemanticType gen:
                    return IsNamedTypeSized(context.UnwrapAlias(gen.TargetHandle), gen, context, visited);
            }
        }

        if (Handle is { } rawHandle)
        {
            var handle = rawHandle.Kind == SymbolKind.GenericParameter ? rawHandle : context.UnwrapAlias(rawHandle);
            return IsNamedTypeSized(handle, null, context, visited);
        }

        return true;
    }

    /// <summary>
    /// Returns <c>true</c> only when the type is statically guaranteed to be <c>Unsized</c>
    /// (rejecting unconstrained <c>T : Layout</c> parameters which could still be instantiated with a <c>Sized</c> type).
    /// </summary>
    public bool IsKnownUnsized(ResolutionContext context)
    {
        if (IsSized(context))
            return false;

        if (Kind == TypeRefKind.Resolved &&
            SpecialType is null &&
            Handle is { Kind: SymbolKind.GenericParameter } gpH &&
            context.GetGenericParameterSymbol(gpH) is { } gp)
        {
            return gp.HasExplicitUnsized;
        }

        return true;
    }

    private static bool IsNamedTypeSized(SymbolHandle handle, GenericSemanticType? genInst, ResolutionContext context, HashSet<SymbolHandle>? visited)
    {
        if (handle.Kind == SymbolKind.GenericParameter)
            return context.GetGenericParameterSymbol(handle)?.RequiresSized(context) ?? true;

        if (IsInterfaceHandle(handle, context))
            return false;

        if (IsIntrinsicHandle(handle, context, KnownIntrinsics.Slice) ||
            IsIntrinsicHandle(handle, context, KnownIntrinsics.Str))
            return false;

        if (IsIntrinsicHandle(handle, context, KnownIntrinsics.Array))
        {
            if (genInst is { TypeArguments.Count: > 0 })
                return genInst.TypeArguments[0].IsSized(context, visited);
            return true;
        }

        var typeSym = context.GetTypeSymbol(handle);
        if (typeSym is not null && typeSym.TypeKind == TypeKind.Enum)
            return true;

        if (typeSym is not null && typeSym.TypeKind is not (TypeKind.Struct or TypeKind.Class))
            return true;

        visited ??= [];
        if (!visited.Add(handle))
            return true;

        var allFields = Maho.Resolution.Ownership.OwnershipTypeClassifier.GetAllStructFields(context, handle);
        if (allFields.Count == 0)
            return true;

        var lastField = context.GetFieldSymbol(allFields[^1]);
        if (lastField is null)
            return true;

        TypeRef tailType = lastField.Type;
        if (genInst is not null && genInst.TypeArguments.Count > 0)
        {
            IReadOnlyList<SymbolHandle> gpHandles = context.GetSymbol(handle) switch
            {
                TypeSymbol ts => ts.GenericParameters,
                NestedTypeSymbol nts => nts.GenericParameters,
                _ => []
            };
            if (gpHandles.Count > 0)
            {
                var subs = new Dictionary<SymbolHandle, TypeRef>();
                for (int i = 0; i < gpHandles.Count && i < genInst.TypeArguments.Count; i++)
                    subs[gpHandles[i]] = genInst.TypeArguments[i];
                tailType = tailType.Substitute(subs);
            }
        }

        return tailType.IsSized(context, visited);
    }

    internal static bool IsInterfaceHandle(SymbolHandle handle, ResolutionContext context)
    {
        var sym = context.GetSymbol(handle);
        return sym switch
        {
            TypeSymbol ts => ts.TypeKind == TypeKind.Interface,
            NestedTypeSymbol nts => nts.TypeKind == TypeKind.Interface,
            _ => false
        };
    }

    private static bool IsIntrinsicHandle(SymbolHandle handle, ResolutionContext context, string intrinsicName) =>
        context.DiscoveredIntrinsics.TryGet(intrinsicName, out var h) && handle == h;

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
    public static TypeRef EmptyTuple { get; } = Tuple([]);

    public bool IsEmptyTuple => Kind == TypeRefKind.Tuple && SpecialType is TupleSemanticType { Elements.Count: 0 };

    public static TypeRef Literal(CompileTimeLiteralType literal) =>
        new TypeRef(TypeRefKind.Literal, null, literal);

    public static TypeRef Literal(CompileTimeLiteralKind kind, string rawText) =>
        Literal(new CompileTimeLiteralType(kind, rawText));

    public static TypeRef Pointer(TypeRef element, bool isMutable = false) =>
        new TypeRef(TypeRefKind.Pointer, null, new PointerSemanticType(element, isMutable));

    public static TypeRef Reference(TypeRef element, bool isMutable = false) =>
        new TypeRef(TypeRefKind.Reference, null, new ReferenceSemanticType(element, isMutable));

    public static TypeRef Alias(TypeRef element, bool isMutable = false, bool isUnsafe = false) =>
        new TypeRef(TypeRefKind.Alias, null, new AliasSemanticType(element, isMutable, isUnsafe));

    public static TypeRef Slice(TypeRef element, int dimension = 1, SymbolHandle? sliceHandle = null) =>
        new TypeRef(TypeRefKind.Slice, sliceHandle, new SliceSemanticType(element, dimension));

    public static TypeRef Array(TypeRef element, IReadOnlyList<Expression> sizes, SymbolHandle? arrayHandle = null) =>
        new TypeRef(TypeRefKind.Array, arrayHandle, new ArraySemanticType(element, sizes));

    public static TypeRef Array(TypeRef element, Expression size, SymbolHandle? arrayHandle = null) =>
        new TypeRef(TypeRefKind.Array, arrayHandle, new ArraySemanticType(element, size));

    public static TypeRef Array(TypeRef element, int count, SymbolHandle? arrayHandle = null) =>
        new TypeRef(TypeRefKind.Array, arrayHandle, new ArraySemanticType(element, new LiteralExpression(new Token(new Text.SourceText(count.ToString()), default, TokenKind.Integer, [], [], MatchingKeywordKind.None, count.ToString()))));

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

    public static TypeRef Dyn(TypeRef targetType, bool isExplicitDyn = false)
    {
        if (targetType.SpecialType is DynSemanticType existing)
        {
            if (isExplicitDyn && !existing.IsExplicitDyn)
                return new TypeRef(TypeRefKind.Dyn, existing.TargetType.Handle, new DynSemanticType(existing.TargetType, isExplicitDyn: true));
            return targetType;
        }
        return new TypeRef(TypeRefKind.Dyn, targetType.Handle, new DynSemanticType(targetType, isExplicitDyn: isExplicitDyn));
    }

    public static TypeRef Dyn(TypeRef targetType, IReadOnlyList<TypeRef> composed) =>
        new TypeRef(TypeRefKind.Dyn, targetType.Handle, new DynSemanticType(targetType, composed));

    /// <summary>
    /// Validates and wraps an explicitly <c>dyn</c>-modified type syntax (<c>dyn T&amp;</c>, <c>dyn &amp;T</c>, <c>dyn T*</c>, or <c>dyn T</c>).
    /// Rejects applying <c>dyn</c> to any <c>Unsized</c> type (such as interfaces, <c>Str</c>, slices, or tail-unsized structs)
    /// because their indirections already carry fat metadata implicitly; <c>dyn</c> is strictly for fat indirections over <c>Sized</c> types.
    /// </summary>
    internal static TypeRef CreateValidatedDynType(
        TypeRef inner,
        Maho.Syntax.PrefixModifiedType dynSyntax,
        ResolutionContext context,
        bool reportDiagnostics)
    {
        if (inner.IsError || inner.IsUnresolved)
            return inner;

        TypeRef pointee = inner.SpecialType switch
        {
            AliasSemanticType al => al.TargetType,
            ReferenceSemanticType rf => rf.ElementType,
            PointerSemanticType pt => pt.ElementType,
            _ => inner
        };

        if (!pointee.IsError && !pointee.IsUnresolved && !pointee.IsSized(context))
        {
            if (reportDiagnostics)
            {
                context.Diagnostics.ReportInvalidInterfaceOrDynUsage(
                    dynSyntax.GetSpan() ?? default,
                    $"'dyn' can only wrap indirections to Sized types; '{pointee.ToDisplayString(context)}' is Unsized and already uses fat references",
                    $"'dyn' is not allowed on Unsized type '{pointee.ToDisplayString(context)}'",
                    "remove 'dyn' (interfaces, 'Str', slices, and tail-unsized types carry fat metadata implicitly)",
                    dynSyntax.GetSource());
            }
            return Error;
        }

        return Dyn(inner, isExplicitDyn: true);
    }

    public static TypeRef Conformance(TypeRef targetType, InterfaceConformance conformance) =>
        new TypeRef(TypeRefKind.Resolved, targetType.Handle, new ConformanceSemanticType(targetType, conformance));

    public static TypeRef UnwrapConformance(TypeRef type) =>
        type.SpecialType is ConformanceSemanticType conf ? conf.TargetType : type;

    public static bool TryGetExplicitConformance(TypeRef type, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out InterfaceConformance? conformance)
    {
        while (type.SpecialType is DynSemanticType or AliasSemanticType or ReferenceSemanticType or PointerSemanticType)
        {
            type = type.SpecialType switch
            {
                DynSemanticType d => d.TargetType,
                AliasSemanticType a => a.TargetType,
                ReferenceSemanticType r => r.ElementType,
                PointerSemanticType p => p.ElementType,
                _ => type
            };
        }

        if (type.SpecialType is ConformanceSemanticType conf)
        {
            conformance = conf.Conformance;
            return true;
        }
        conformance = null;
        return false;
    }

    public static TypeRef UnwrapDyn(TypeRef type)
    {
        while (type.SpecialType is DynSemanticType dyn)
            type = dyn.TargetType;
        return type;
    }

    public static bool HasExplicitDyn(TypeRef type) => type.SpecialType switch
    {
        DynSemanticType dyn => dyn.IsExplicitDyn || HasExplicitDyn(dyn.TargetType),
        AliasSemanticType al => HasExplicitDyn(al.TargetType),
        ReferenceSemanticType rf => HasExplicitDyn(rf.ElementType),
        PointerSemanticType ptr => HasExplicitDyn(ptr.ElementType),
        ConformanceSemanticType conf => HasExplicitDyn(conf.TargetType),
        _ => false
    };

    public static TypeRef Function(TypeRef returnType, IReadOnlyList<TypeRef> parameterTypes) =>
        new TypeRef(TypeRefKind.Function, null, new FunctionSemanticType(returnType, parameterTypes));

    public static TypeRef Generic(SymbolHandle targetHandle, string targetName, IReadOnlyList<TypeRef> typeArguments) =>
        new TypeRef(TypeRefKind.Resolved, targetHandle, new GenericSemanticType(targetHandle, targetName, typeArguments));

    internal static TypeRef DerivedSelf(SymbolHandle baseTypeHandle) =>
        new TypeRef(TypeRefKind.Resolved, null, new DerivedSelfSemanticType(baseTypeHandle));

    public bool IsFunction => Kind == TypeRefKind.Function;
    public FunctionSemanticType? AsFunction => SpecialType as FunctionSemanticType;

    public bool IsUntagged => Kind == TypeRefKind.Untagged || (SpecialType is UnionSemanticType u && u.IsUntagged);

    public static implicit operator TypeRef(SymbolHandle handle) => Resolved(handle);
    public static implicit operator TypeRef((SymbolKind Kind, SymbolID ID) tuple) => Resolved(new SymbolHandle(tuple.Kind, tuple.ID));

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

        if (source.IsNever(context))
            return true;

        if (IsNever(context))
            return source.IsNever(context);

        if (SpecialType is UnionSemanticType targetUnion)
        {
            if (source.SpecialType is UnionSemanticType sourceUnion)
                return sourceUnion.Variants.All(sv => targetUnion.Variants.Any(tv => tv.IsAssignableFrom(sv, context)));

            return targetUnion.Variants.Any(v => v.IsAssignableFrom(source, context));
        }

        if (SpecialType is ConformanceSemanticType targetConformance)
        {
            if (source.SpecialType is ConformanceSemanticType sourceConformance)
                return Equals(source);

            return targetConformance.TargetType.IsAssignableFrom(source, context);
        }

        if (source.SpecialType is ConformanceSemanticType srcConformance)
        {
            return IsAssignableFrom(srcConformance.TargetType, context);
        }

        if (SpecialType is DynSemanticType targetDyn)
        {
            if (TryUnwrapIndirection(targetDyn.TargetType, out var targetDynInner, out bool targetIsPtr))
            {
                if (source.SpecialType is DynSemanticType sourceDyn)
                {
                    if (TryUnwrapIndirection(sourceDyn.TargetType, out var srcDynInner, out bool srcIsPtr))
                        return targetIsPtr == srcIsPtr && IsElementAssignableWithUnsizing(targetDynInner, srcDynInner, context);

                    return !targetIsPtr && IsElementAssignableWithUnsizing(targetDynInner, sourceDyn.TargetType, context);
                }

                if (TryUnwrapIndirection(source, out var srcPlainInner, out bool srcPlainIsPtr))
                    return targetIsPtr == srcPlainIsPtr && IsElementAssignableWithUnsizing(targetDynInner, srcPlainInner, context);

                return !targetIsPtr && IsElementAssignableWithUnsizing(targetDynInner, source, context);
            }

            if (source.SpecialType is DynSemanticType sourceDynPlain)
                return targetDyn.TargetType.IsAssignableFrom(sourceDynPlain.TargetType, context);

            return targetDyn.TargetType.IsAssignableFrom(source, context);
        }

        if (SpecialType is AliasSemanticType targetAlias)
        {
            if (targetAlias.TargetType.SpecialType is DynSemanticType targetInnerDyn)
            {
                if (targetAlias.IsMutable &&
                    ((source.SpecialType is DynSemanticType srcDynMut && TryUnwrapIndirectionMutability(srcDynMut.TargetType, out bool srcDynMutFlag) && !srcDynMutFlag) ||
                     (source.SpecialType is not DynSemanticType && TryUnwrapIndirectionMutability(source, out bool srcMutFlag) && !srcMutFlag)))
                    return false;

                if (source.SpecialType is DynSemanticType srcDyn &&
                    TryUnwrapIndirection(srcDyn.TargetType, out var srcDynInner, out bool srcIsPtr) && !srcIsPtr)
                    return targetInnerDyn.TargetType.IsAssignableFrom(srcDynInner, context);

                if (TryUnwrapIndirection(source, out var srcIndirInner, out bool srcIndirPtr) && !srcIndirPtr)
                    return targetInnerDyn.TargetType.IsAssignableFrom(srcIndirInner, context);
            }

            if (source.SpecialType is AliasSemanticType sourceAlias)
            {
                if (targetAlias.IsMutable && !sourceAlias.IsMutable)
                    return false;
                return IsElementAssignableWithUnsizing(targetAlias.TargetType, sourceAlias.TargetType, context);
            }

            if (source.SpecialType is ReferenceSemanticType sourceRefAsAlias)
            {
                if (targetAlias.IsMutable && !sourceRefAsAlias.IsMutable)
                    return false;
                return IsElementAssignableWithUnsizing(targetAlias.TargetType, sourceRefAsAlias.ElementType, context);
            }

            return IsElementAssignableWithUnsizing(targetAlias.TargetType, source, context);
        }

        if (source.SpecialType is AliasSemanticType srcAlias)
            return IsAssignableFrom(srcAlias.TargetType, context);

        if (SpecialType is ReferenceSemanticType targetRef)
        {
            if (source.SpecialType is ReferenceSemanticType sourceRef)
            {
                if (targetRef.IsMutable && !sourceRef.IsMutable)
                    return false;

                if (targetRef.ElementType.SpecialType is DynSemanticType targetDynElem)
                    return targetDynElem.TargetType.IsAssignableFrom(sourceRef.ElementType, context);

                if (IsNonContiguousSplitBase(this, source, context, out _))
                    return false;

                return IsElementAssignableWithUnsizing(targetRef.ElementType, sourceRef.ElementType, context);
            }
        }

        if (SpecialType is PointerSemanticType targetPtr)
        {
            if (source.SpecialType is PointerSemanticType sourcePtr)
            {
                if (targetPtr.IsMutable && !sourcePtr.IsMutable)
                    return false;

                if (targetPtr.ElementType.SpecialType is DynSemanticType targetDynElem)
                    return targetDynElem.TargetType.IsAssignableFrom(sourcePtr.ElementType, context);

                if (IsNonContiguousSplitBase(this, source, context, out _))
                    return false;

                return IsElementAssignableWithUnsizing(targetPtr.ElementType, sourcePtr.ElementType, context);
            }
        }

        // A safe reference is an access path to its element. When the destination is an owned
        // value (rather than another reference/pointer, handled above), assignment materializes
        // that element under Copy semantics. Ownership lowering diagnoses a move-only element
        // reached through this boundary instead of silently moving it.
        if (source.SpecialType is ReferenceSemanticType sourceReference)
            return IsAssignableFrom(sourceReference.ElementType, context);

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
                if (source.IsFunction) return true;
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

                if (IsSubtypeOf(unwrappedSrcH2, unwrappedTargetH, context, superGen: SpecialType as GenericSemanticType)) return true;

                if (context.HasAnyConformance(unwrappedSrcH2, unwrappedTargetH, SpecialType as GenericSemanticType)) return true;
            }
            else if (source.SpecialType is FunctionSemanticType srcFunc &&
                     context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Function, out var fnIntrinsicH))
            {
                if (IsSubtypeOf(fnIntrinsicH, unwrappedTargetH, context, superGen: SpecialType as GenericSemanticType))
                    return true;

                var fnAsGeneric = Generic(fnIntrinsicH, KnownIntrinsics.Function, [srcFunc.ReturnType, .. srcFunc.ParameterTypes]);
                var lookup = context.ResolveInterfaceConformance(fnAsGeneric, this, null, null, default);
                if (lookup.Status == ResolutionContext.ConformanceLookupStatus.Found)
                    return true;
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
        return context.DiscoveredIntrinsics.TryGetIntrinsicName(handle, out var intrinsicName) && KnownIntrinsics.IsIntegerType(intrinsicName);
    }

    private static bool IsFloatTypeSymbol(SymbolHandle handle, ResolutionContext context)
    {
        handle = context.UnwrapAlias(handle);
        return context.DiscoveredIntrinsics.TryGetIntrinsicName(handle, out var intrinsicName) && KnownIntrinsics.IsFloatType(intrinsicName);
    }

    private static bool IsStringTypeSymbol(SymbolHandle handle, ResolutionContext context)
    {
        handle = context.UnwrapAlias(handle);
        return context.DiscoveredIntrinsics.TryGetIntrinsicName(handle, out var intrinsicName) && intrinsicName == KnownIntrinsics.Str;
    }

    private static bool IsCharTypeSymbol(SymbolHandle handle, ResolutionContext context)
    {
        handle = context.UnwrapAlias(handle);
        return context.DiscoveredIntrinsics.TryGetIntrinsicName(handle, out var intrinsicName) && intrinsicName == KnownIntrinsics.Char;
    }

    internal static bool IsSubtypeOf(SymbolHandle subHandle, SymbolHandle superHandle, ResolutionContext context, HashSet<SymbolHandle>? visited = null, GenericSemanticType? superGen = null)
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
                    var unwrappedBHandle = context.UnwrapAlias(bHandle);
                    if (unwrappedBHandle == superHandle)
                    {
                        if (superGen is not null && b.SpecialType is GenericSemanticType baseGen)
                        {
                            if (superGen.TypeArguments.Count == baseGen.TypeArguments.Count)
                            {
                                bool match = true;
                                for (int i = 0; i < superGen.TypeArguments.Count; i++)
                                {
                                    if (!superGen.TypeArguments[i].IsAssignableFrom(baseGen.TypeArguments[i], context))
                                    {
                                        match = false;
                                        break;
                                    }
                                }
                                if (match) return true;
                            }
                            continue;
                        }
                        return true;
                    }

                    if (IsSubtypeOf(bHandle, superHandle, context, visited, superGen))
                        return true;
                }
            }
        }

        return false;
    }

    private static bool TryUnwrapIndirection(TypeRef type, out TypeRef element, out bool isPointer)
    {
        if (type.SpecialType is AliasSemanticType alias)
        {
            element = alias.TargetType;
            isPointer = false;
            return true;
        }

        if (type.SpecialType is ReferenceSemanticType rf)
        {
            element = rf.ElementType;
            isPointer = false;
            return true;
        }

        if (type.SpecialType is PointerSemanticType ptr)
        {
            element = ptr.ElementType;
            isPointer = true;
            return true;
        }

        element = default;
        isPointer = false;
        return false;
    }

    private static bool TryUnwrapIndirectionMutability(TypeRef type, out bool isMutable)
    {
        if (type.SpecialType is AliasSemanticType al)
        {
            isMutable = al.IsMutable;
            return true;
        }
        if (type.SpecialType is ReferenceSemanticType rf)
        {
            isMutable = rf.IsMutable;
            return true;
        }
        if (type.SpecialType is PointerSemanticType pt)
        {
            isMutable = pt.IsMutable;
            return true;
        }
        isMutable = false;
        return false;
    }

    private static bool IsElementAssignableWithUnsizing(TypeRef targetElem, TypeRef sourceElem, ResolutionContext context)
    {
        if (targetElem.IsAssignableFrom(sourceElem, context))
            return true;

        return IsUnsizingCompatible(targetElem, sourceElem, context);
    }

    private static bool IsUnsizingCompatible(TypeRef target, TypeRef source, ResolutionContext context)
    {
        if (target.SpecialType is SliceSemanticType targetSlice && source.SpecialType is ArraySemanticType sourceArr)
            return targetSlice.Dimension == sourceArr.Dimension && targetSlice.ElementType.Equals(sourceArr.ElementType);

        if (target.SpecialType is DynSemanticType dynInner)
            return IsElementAssignableWithUnsizing(dynInner.TargetType, source, context);

        if (target.SpecialType is GenericSemanticType tGen && source.SpecialType is GenericSemanticType sGen)
        {
            var tHandle = context.UnwrapAlias(tGen.TargetHandle);
            if (tHandle != context.UnwrapAlias(sGen.TargetHandle) || tGen.TypeArguments.Count != sGen.TypeArguments.Count)
                return false;

            IReadOnlyList<SymbolHandle> gpHandles = context.GetSymbol(tHandle) switch
            {
                TypeSymbol ts => ts.GenericParameters,
                NestedTypeSymbol nts => nts.GenericParameters,
                _ => []
            };
            if (gpHandles.Count == 0)
                return false;

            var allFields = Maho.Resolution.Ownership.OwnershipTypeClassifier.GetAllStructFields(context, tHandle);
            if (allFields.Count == 0)
                return false;

            var tSubs = new Dictionary<SymbolHandle, TypeRef>();
            var sSubs = new Dictionary<SymbolHandle, TypeRef>();
            for (int i = 0; i < gpHandles.Count && i < tGen.TypeArguments.Count; i++)
            {
                tSubs[gpHandles[i]] = tGen.TypeArguments[i];
                sSubs[gpHandles[i]] = sGen.TypeArguments[i];
            }

            for (int fIdx = 0; fIdx < allFields.Count; fIdx++)
            {
                var field = context.GetFieldSymbol(allFields[fIdx]);
                if (field is null)
                    return false;

                var tFieldType = field.Type.Substitute(tSubs);
                var sFieldType = field.Type.Substitute(sSubs);

                if (fIdx < allFields.Count - 1)
                {
                    if (!tFieldType.Equals(sFieldType))
                        return false;
                }
                else
                {
                    if (tFieldType.IsSized(context))
                        return false;
                    if (!tFieldType.Equals(sFieldType) && !IsUnsizingCompatible(tFieldType, sFieldType, context))
                        return false;
                }
            }

            return true;
        }

        return false;
    }

    public static bool IsNonContiguousSplitBase(TypeRef target, TypeRef source, ResolutionContext context, out string? baseName)
    {
        baseName = null;
        if (HasExplicitDyn(target))
            return false;

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

        return TransformTypeRef(
            leaf => leaf.Handle is { } h && typeArgs.TryGetValue(h, out var substituted) ? substituted : null,
            typeArgs);
    }

    internal TypeRef SubstituteDerivedSelf(SymbolHandle originBaseHandle, TypeRef replacement) =>
        TransformTypeRef(
            leaf => leaf.SpecialType is DerivedSelfSemanticType derived && derived.BaseTypeHandle == originBaseHandle
                ? replacement
                : null,
            tupleFlattenArgs: null);

    private TypeRef TransformTypeRef(
        Func<TypeRef, TypeRef?> mapLeaf,
        Dictionary<SymbolHandle, TypeRef>? tupleFlattenArgs)
    {
        if (mapLeaf(this) is { } direct)
            return direct;

        if (SpecialType is OptionalSemanticType o)
            return Optional(o.ElementType.TransformTypeRef(mapLeaf, tupleFlattenArgs), Handle);

        if (SpecialType is UnionSemanticType u)
            return Union([.. u.Variants.Select(v => v.TransformTypeRef(mapLeaf, tupleFlattenArgs))], u.IsUntagged);

        if (SpecialType is PointerSemanticType p)
        {
            var elem = p.ElementType.TransformTypeRef(mapLeaf, tupleFlattenArgs);
            return elem.SpecialType is DynSemanticType d
                ? Dyn(Pointer(d.TargetType, p.IsMutable), d.IsExplicitDyn)
                : Pointer(elem, p.IsMutable);
        }

        if (SpecialType is ReferenceSemanticType r)
        {
            var elem = r.ElementType.TransformTypeRef(mapLeaf, tupleFlattenArgs);
            return elem.SpecialType is DynSemanticType d
                ? Dyn(Reference(d.TargetType, r.IsMutable), d.IsExplicitDyn)
                : Reference(elem, r.IsMutable);
        }

        if (SpecialType is AliasSemanticType al)
        {
            var target = al.TargetType.TransformTypeRef(mapLeaf, tupleFlattenArgs);
            return target.SpecialType is DynSemanticType d
                ? Dyn(Alias(d.TargetType, al.IsMutable, al.IsUnsafe), d.IsExplicitDyn)
                : Alias(target, al.IsMutable, al.IsUnsafe);
        }

        if (SpecialType is SliceSemanticType s)
            return Slice(s.ElementType.TransformTypeRef(mapLeaf, tupleFlattenArgs), s.Dimension, Handle);

        if (SpecialType is ArraySemanticType a)
            return Array(a.ElementType.TransformTypeRef(mapLeaf, tupleFlattenArgs), a.Sizes, Handle);

        if (SpecialType is DynSemanticType dyn)
            return Dyn(dyn.TargetType.TransformTypeRef(mapLeaf, tupleFlattenArgs), dyn.IsExplicitDyn);

        if (SpecialType is ConformanceSemanticType conf)
            return Conformance(conf.TargetType.TransformTypeRef(mapLeaf, tupleFlattenArgs), conf.Conformance);

        if (SpecialType is TupleSemanticType t)
        {
            var newElements = new List<TupleElement>();
            foreach (var elem in t.Elements)
            {
                var sub = elem.Type.TransformTypeRef(mapLeaf, tupleFlattenArgs);
                if (tupleFlattenArgs is not null &&
                    sub.SpecialType is TupleSemanticType subTuple &&
                    elem.Type.Handle is { } eh &&
                    tupleFlattenArgs.ContainsKey(eh))
                {
                    newElements.AddRange(subTuple.Elements);
                }
                else
                {
                    newElements.Add(new TupleElement(sub, elem.Name));
                }
            }
            return Tuple(newElements, Handle);
        }

        if (SpecialType is UntaggedSemanticType untagged)
            return Untagged(untagged.InnerType.TransformTypeRef(mapLeaf, tupleFlattenArgs));

        if (SpecialType is GenericSemanticType gen)
        {
            var newArgs = gen.TypeArguments.Select(arg => arg.TransformTypeRef(mapLeaf, tupleFlattenArgs)).ToList();
            return Generic(gen.TargetHandle, gen.TargetName, newArgs);
        }

        if (SpecialType is FunctionSemanticType fn)
            return Function(fn.ReturnType.TransformTypeRef(mapLeaf, tupleFlattenArgs), [.. fn.ParameterTypes.Select(pt => pt.TransformTypeRef(mapLeaf, tupleFlattenArgs))]);

        return this;
    }

    public override string ToString() => Kind switch
    {
        TypeRefKind.Resolved => Handle.ToString() ?? "Resolved",
        TypeRefKind.Inferred => "var",
        TypeRefKind.Never => "Never",
        TypeRefKind.Error => "<error>",
        TypeRefKind.Literal => SpecialType?.ToString() ?? "<literal>",
        TypeRefKind.Pointer or TypeRefKind.Reference or TypeRefKind.Slice or
        TypeRefKind.Array or TypeRefKind.Optional or TypeRefKind.Tuple or
        TypeRefKind.Union or TypeRefKind.Untagged or TypeRefKind.Dyn or TypeRefKind.Function or TypeRefKind.Alias => SpecialType?.ToString() ?? Kind.ToString(),
        _ => "<unresolved>"
    };

    public string ToDisplayString(ResolutionContext? context = null)
    {
        if (Kind == TypeRefKind.Never)
            return "Never";

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
        {
            if (context is not null && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Never, out var neverH))
                return ResolutionContext.GetQualifiedSymbolName(neverH, context);
            return "Std.Never";
        }

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

        if (SpecialType is ArraySemanticType arr)
        {
            string qualifiedArrName = (context != null && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var arrH))
                ? ResolutionContext.GetQualifiedSymbolName(arrH, context)
                : KnownIntrinsics.Array;
            return $"{qualifiedArrName}<{arr.ElementType.ToDisplayString(context)}, {arr.Size}>";
        }

        if (SpecialType is SliceSemanticType slice)
        {
            string qualifiedSliceName = (context != null && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Slice, out var sliceH))
                ? ResolutionContext.GetQualifiedSymbolName(sliceH, context)
                : KnownIntrinsics.Slice;
            return slice.Dimension > 1
                ? $"{qualifiedSliceName}<{slice.ElementType.ToDisplayString(context)}, {slice.Dimension}>"
                : $"{qualifiedSliceName}<{slice.ElementType.ToDisplayString(context)}>";
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
            return "Never";

        if (Kind == TypeRefKind.Error)
            return "<error>";

        if (Kind == TypeRefKind.Inferred)
            return "var";

        if (Kind == TypeRefKind.Literal)
            return SpecialType?.ToDisplayString(context) ?? "literal";

        if (SpecialType is FunctionSemanticType fn)
            return $"func({string.Join(", ", fn.ParameterTypes.Select(p => p.ToLabelString(context, scope)))}) -> {fn.ReturnType.ToLabelString(context, scope)}";

        if (SpecialType is GenericSemanticType gen)
        {
            var targetLabel = ResolutionContext.GetLabelSymbolName(gen.TargetHandle, context, scope);
            return $"{targetLabel}<{string.Join(", ", gen.TypeArguments.Select(a => a.ToLabelString(context, scope)))}>";
        }

        if (SpecialType is OptionalSemanticType opt)
            return $"{opt.ElementType.ToLabelString(context, scope)}?";

        if (SpecialType is ArraySemanticType arr)
            return $"{arr.ElementType.ToLabelString(context, scope)}[{arr.Size}]";

        if (SpecialType is SliceSemanticType slice)
            return slice.Dimension == 1
                ? $"{slice.ElementType.ToLabelString(context, scope)}[]"
                : $"{slice.ElementType.ToLabelString(context, scope)}[{new string(',', slice.Dimension - 1)}]";

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
