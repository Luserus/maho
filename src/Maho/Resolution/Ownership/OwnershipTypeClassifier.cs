using System.Collections.Generic;
using System.Linq;

namespace Maho.Resolution.Ownership;

/// <summary>
/// Computes the language's implicit copy-or-move decision.  User code never selects this at a
/// use site: the lowering phase emits a copy for structurally copyable values and a move for
/// values with move/drop semantics.
/// </summary>
internal sealed class OwnershipTypeClassifier
{
    private readonly ResolutionContext context;
    private readonly Dictionary<TypeRef, OwnershipKind> cache = [];
    private readonly HashSet<TypeRef> visiting = [];
    private readonly Dictionary<TypeRef, bool> dropCache = [];
    private readonly HashSet<TypeRef> visitingDrop = [];

    public OwnershipTypeClassifier(ResolutionContext context) => this.context = context;

    /// <summary>
    /// Classifies a value use as implicit Copy or Move.
    /// </summary>
    /// <remarks>
    /// Results are cached. The recursion guard only breaks structural cycles during classification;
    /// concrete fields and explicit lifecycle contracts still determine the enclosing result.
    /// </remarks>
    public OwnershipKind Classify(TypeRef type)
    {
        if (cache.TryGetValue(type, out var known))
            return known;

        // Recursive aggregates are handled by their explicit lifecycle contract.  If no such
        // contract exists, treating the in-progress edge as Copy prevents a false cycle while
        // the enclosing type still examines all concrete fields.
        if (!visiting.Add(type))
            return OwnershipKind.Copy;

        OwnershipKind result = Compute(type);
        visiting.Remove(type);
        cache[type] = result;
        return result;
    }

    /// <summary>
    /// Whether normal scope exit or replacement must invoke cleanup for this value.  This is
    /// distinct from move-only: a manually movable POD need not have a destructor.
    /// </summary>
    public bool RequiresDrop(TypeRef type)
    {
        if (dropCache.TryGetValue(type, out bool known))
            return known;

        // Recursive structural types can occur in the unresolved/intrinsic model even when no
        // finite by-value layout exists yet.  An in-progress edge contributes no new destructor;
        // the enclosing concrete fields and explicit Destructor contract decide the result.
        if (!visitingDrop.Add(type))
            return false;

        bool result = ComputeRequiresDrop(type);
        visitingDrop.Remove(type);
        dropCache[type] = result;
        return result;
    }

    /// <summary>
    /// Checks whether a type implements an explicit lifecycle contract (Move or Destructor).
    /// Types with explicit lifecycle contracts have monolithic ownership and forbid partial moves.
    /// </summary>
    public bool HasExplicitLifecycle(TypeRef type)
    {
        if (type.Handle is not { } handle)
            return false;

        handle = context.UnwrapAlias(handle);
        return HasLifecycleOperator(handle, OperatorKind.Destructor) || HasLifecycleOperator(handle, OperatorKind.Move);
    }

    /// <summary>
    /// Checks whether a type implements an explicit Destructor contract.
    /// </summary>
    public bool HasExplicitDestructor(TypeRef type)
    {
        if (type.Handle is not { } handle)
            return false;

        handle = context.UnwrapAlias(handle);
        return HasLifecycleOperator(handle, OperatorKind.Destructor);
    }

    /// <summary>
    /// Checks whether a type implements the <see cref="OperatorKind.OwningDeref"/> interface contract,
    /// indicating that it is an owning container/smart pointer that exclusively owns its pointee.
    /// </summary>
    public bool IsOwningIndirection(TypeRef type, out TypeRef pointeeType)
    {
        pointeeType = TypeRef.Unresolved;
        if (type.Handle is not { } handle)
            return false;

        var unwrap = context.UnwrapAlias(handle);
        if (context.DiscoveredOperators.TryGetOperators(OperatorKind.OwningDeref, out var candidates))
        {
            var sym = context.GetSymbol(unwrap);
            IReadOnlyList<TypeRef> baseTypes = sym switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                _ => []
            };

            foreach (var cand in candidates)
            {
                var match = baseTypes.FirstOrDefault(b => b.Handle == cand.InterfaceHandle);
                if (match.Handle.HasValue)
                {
                    if (match.SpecialType is GenericSemanticType gst && gst.TypeArguments.Count >= 1)
                    {
                        var retType = gst.TypeArguments[0];
                        if (type.SpecialType is GenericSemanticType containerGst && sym is TypeSymbol containerSym)
                        {
                            Dictionary<SymbolHandle, TypeRef> subst = [];
                            for (int i = 0; i < containerSym.GenericParameters.Count && i < containerGst.TypeArguments.Count; i++)
                            {
                                subst[containerSym.GenericParameters[i]] = containerGst.TypeArguments[i];
                            }
                            retType = retType.Substitute(subst);
                        }

                        if (retType.SpecialType is ReferenceSemanticType refType)
                            pointeeType = refType.ElementType;
                        else if (retType.SpecialType is PointerSemanticType ptrType)
                            pointeeType = ptrType.ElementType;
                        else if (retType.SpecialType is AliasSemanticType aliasType)
                            pointeeType = aliasType.TargetType;
                        else
                            pointeeType = retType;

                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Computes destructor requirement after recursion/cache checks have been performed by
    /// <see cref="RequiresDrop"/>.
    /// </summary>
    /// <remarks>
    /// Pointer-like handles never own their referent. Structural aggregates require drop when any
    /// contained value does; opaque allocation/capture forms are conservatively treated as owning.
    /// </remarks>
    private bool ComputeRequiresDrop(TypeRef type)
    {
        if (type.IsError || type.IsUnresolved || type.IsInferred || type.IsLiteral || type.IsNever())
            return false;

        switch (type.SpecialType)
        {
            case ReferenceSemanticType:
            case PointerSemanticType:
            case AliasSemanticType:
            case FunctionSemanticType:
                return false;
            case TupleSemanticType tuple:
                return tuple.Elements.Any(element => RequiresDrop(element.Type));
            case FixedArraySemanticType fixedArray:
                return RequiresDrop(fixedArray.ElementType);
            case UnionSemanticType union:
                return union.Variants.Any(RequiresDrop);
            case ArraySemanticType:
            case DelegateSemanticType:
            case DynSemanticType:
                return true;
        }

        if (type.Handle is not { } handle)
            return false;

        handle = context.UnwrapAlias(handle);
        if (handle.Kind == SymbolKind.GenericParameter)
            return true;
        if (HasLifecycleOperator(handle, OperatorKind.Destructor))
            return true;

        IEnumerable<SymbolHandle> fields = context.GetSymbol(handle) switch
        {
            ProductTypeSymbol product => product.Fields,
            MemberProductTypeSymbol nestedProduct => nestedProduct.Fields,
            _ => []
        };
        return fields.Any(field => context.GetFieldSymbol(field) is { } symbol && RequiresDrop(symbol.Type));
    }

    /// <summary>
    /// Implements the uncached ownership rules for semantic wrappers, generic parameters,
    /// lifecycle contracts, and structural fields.
    /// </summary>
    /// <remarks>
    /// Checking Destructor/Move before Copy makes destructive or explicitly movable types move-only
    /// even if an inherited interface list also contains Copy through an invalid declaration.
    /// </remarks>
    private OwnershipKind Compute(TypeRef type)
    {
        if (type.IsError || type.IsUnresolved || type.IsInferred || type.IsLiteral || type.IsNever())
            return OwnershipKind.Copy;

        switch (type.SpecialType)
        {
            // These values are handles, never the referent itself.
            case ReferenceSemanticType:
            case PointerSemanticType:
            case AliasSemanticType:
            case FunctionSemanticType:
                return OwnershipKind.Copy;

            case DynSemanticType:
                // A bare dyn value is rejected by type/layout checking.  Any supported dyn
                // form is behind one of the pointer-like cases above.
                return OwnershipKind.Move;

            case TupleSemanticType tuple:
                return tuple.Elements.All(e => Classify(e.Type) == OwnershipKind.Copy)
                    ? OwnershipKind.Copy : OwnershipKind.Move;

            case FixedArraySemanticType fixedArray:
                return Classify(fixedArray.ElementType);

            case UnionSemanticType union:
                return union.Variants.All(v => Classify(v) == OwnershipKind.Copy)
                    ? OwnershipKind.Copy : OwnershipKind.Move;

            case ArraySemanticType:
            case DelegateSemanticType:
                // Arrays and closure environments may own an allocation/captured Drop value.
                // Treat them conservatively until their intrinsic ownership contracts exist.
                return OwnershipKind.Move;
        }

        if (type.Handle is not { } handle)
            return OwnershipKind.Copy;

        handle = context.UnwrapAlias(handle);
        if (handle.Kind == SymbolKind.GenericParameter)
            return OwnershipKind.Move;

        if (HasLifecycleOperator(handle, OperatorKind.Destructor) || HasLifecycleOperator(handle, OperatorKind.Move))
            return OwnershipKind.Move;

        // An explicit Copy implementation documents the answer even if the physical layout is
        // opaque to this compilation (for example, a referenced project type).
        if (HasLifecycleOperator(handle, OperatorKind.Copy))
            return OwnershipKind.Copy;

        IEnumerable<SymbolHandle> fields = context.GetSymbol(handle) switch
        {
            ProductTypeSymbol product => product.Fields,
            MemberProductTypeSymbol nestedProduct => nestedProduct.Fields,
            _ => []
        };

        foreach (var fieldHandle in fields)
        {
            if (context.GetFieldSymbol(fieldHandle) is { } field && Classify(field.Type) == OwnershipKind.Move)
                return OwnershipKind.Move;
        }

        return OwnershipKind.Copy;
    }

    /// <summary>
    /// Tests whether a type implements a lifecycle operator interface directly or through C3
    /// linearized inheritance.
    /// </summary>
    /// <remarks>
    /// Operator discovery owns the mapping from Copy/Move/Destructor to their canonical interface
    /// symbols. This keeps ownership analysis independent of user spelling or interface aliases.
    /// </remarks>
    private bool HasLifecycleOperator(SymbolHandle typeHandle, OperatorKind kind)
    {
        var interfaces = context.DiscoveredOperators.GetOperators(kind)
            .Select(static op => op.InterfaceHandle)
            .ToHashSet();

        if (interfaces.Count == 0)
            return false;

        var type = context.GetSymbol(typeHandle);
        IEnumerable<TypeRef> bases = type switch
        {
            TypeSymbol symbol => symbol.BaseTypes,
            NestedTypeSymbol symbol => symbol.BaseTypes,
            _ => []
        };

        if (bases.Any(baseType => baseType.Handle is { } baseHandle && interfaces.Contains(context.UnwrapAlias(baseHandle))))
            return true;

        // C3 linearization contains inherited interfaces as well as direct bases.  Lifecycle
        // semantics follow that complete hierarchy, not merely the spelling on the immediate
        // declaration.
        IEnumerable<SymbolHandle> linearized = type switch
        {
            TypeSymbol symbol => symbol.LinearizedBases,
            NestedTypeSymbol symbol => symbol.LinearizedBases,
            _ => []
        };
        return linearized.Any(baseHandle => interfaces.Contains(context.UnwrapAlias(baseHandle)));
    }
}
