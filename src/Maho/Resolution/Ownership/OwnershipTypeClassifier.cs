using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

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
    public bool IsOwningIndirection(TypeRef type, out TypeRef pointeeType) =>
        TryResolveDerefOperator(type, OperatorKind.OwningDeref, out pointeeType, out _);

    /// <summary>
    /// Determines whether <paramref name="callableHandle"/> implements an allocation operator
    /// (<see cref="OperatorKind.New"/>) and yields its target type.
    /// </summary>
    public bool IsAllocationOperatorMethod(SymbolHandle callableHandle, out TypeRef targetType)
    {
        targetType = TypeRef.Unresolved;
        var callableSym = context.GetSymbol(callableHandle);
        if (callableSym is not MethodSymbol method)
            return false;

        var allocOps = context.DiscoveredOperators.GetOperators(OperatorKind.New).ToList();
        if (allocOps.Count == 0)
            return false;

        var allocMethodHandles = allocOps.Select(static op => op.MethodHandle).ToHashSet();

        foreach (var cand in allocOps)
        {
            if (cand.MethodHandle == callableHandle)
            {
                targetType = ResolveAllocationTargetFromParent(cand.InterfaceHandle);
                return true;
            }
        }

        foreach (var conf in context.Conformances)
        {
            foreach (var kv in conf.MethodMap)
            {
                if (kv.Value == callableHandle && allocMethodHandles.Contains(kv.Key))
                {
                    targetType = conf.TargetType;
                    return true;
                }
            }
        }

        if (method.Parent is { } parentH)
        {
            var unwrapParent = context.UnwrapAlias(parentH);
            var parentSym = context.GetSymbol(unwrapParent);
            foreach (var cand in allocOps)
            {
                bool conforms = parentSym is ExtensionSymbol ext
                    ? ext.BaseInterfaces.Any(b => b.Handle is { } bh && (context.UnwrapAlias(bh) == cand.InterfaceHandle || TypeRef.IsSubtypeOf(context.UnwrapAlias(bh), cand.InterfaceHandle, context)))
                    : TypeRef.IsSubtypeOf(unwrapParent, cand.InterfaceHandle, context);
                if (!conforms)
                    continue;

                if (MatchesOperatorMethodSignature(method, cand))
                {
                    targetType = ResolveAllocationTargetFromParent(unwrapParent);
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryResolveDerefOperator(
        TypeRef type,
        OperatorKind opKind,
        out TypeRef pointeeType,
        out MethodSymbol? implementingMethod)
    {
        pointeeType = TypeRef.Unresolved;
        implementingMethod = null;
        if (type.Handle is not { } handle)
            return false;

        if (!context.DiscoveredOperators.TryGetOperators(opKind, out var candidates))
            return false;

        var unwrap = context.UnwrapAlias(handle);
        var sym = context.GetSymbol(unwrap);
        IReadOnlyList<TypeRef> baseTypes = sym switch
        {
            TypeSymbol ts => ts.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            _ => []
        };
        var containerSubst = BuildTypeSubstitutionMap(type, sym);

        foreach (var cand in candidates)
        {
            var match = baseTypes.FirstOrDefault(b => b.Handle == cand.InterfaceHandle);
            InterfaceConformance? matchedConf = null;
            foreach (var conf in context.Conformances)
            {
                if (conf.TargetType.Handle is { } cth &&
                    context.UnwrapAlias(cth) == unwrap &&
                    (conf.InterfaceHandle == cand.InterfaceHandle ||
                     TypeRef.IsSubtypeOf(conf.InterfaceHandle, cand.InterfaceHandle, context)))
                {
                    matchedConf = conf;
                    if (!match.Handle.HasValue && conf.InterfaceHandle == cand.InterfaceHandle)
                        match = conf.InterfaceType;
                    break;
                }
            }

            if (!match.Handle.HasValue &&
                matchedConf is null &&
                !TypeRef.IsSubtypeOf(unwrap, cand.InterfaceHandle, context))
            {
                continue;
            }

            if (matchedConf is not null &&
                matchedConf.MethodMap.TryGetValue(cand.MethodHandle, out var mappedH) &&
                context.GetSymbol(mappedH) is MethodSymbol mappedMethod)
            {
                implementingMethod = mappedMethod;
            }
            else
            {
                implementingMethod = FindDirectOperatorMethod(sym, cand);
            }

            TypeRef retType = TypeRef.Unresolved;
            if (match.Handle.HasValue && match.SpecialType is GenericSemanticType gst && gst.TypeArguments.Count >= 1)
                retType = gst.TypeArguments[0];
            else if (implementingMethod is not null)
                retType = implementingMethod.ReturnType;
            else if (context.GetSymbol(cand.MethodHandle) is MethodSymbol opSym)
                retType = opSym.ReturnType;

            if (retType.IsUnresolved || retType.IsError)
                continue;

            if (containerSubst.Count > 0)
                retType = retType.Substitute(containerSubst);

            pointeeType = UnwrapIndirectionType(retType);
            return true;
        }

        return false;
    }

    private static TypeRef UnwrapIndirectionType(TypeRef retType)
    {
        if (retType.SpecialType is ReferenceSemanticType refType)
            return refType.ElementType;
        if (retType.SpecialType is PointerSemanticType ptrType)
            return ptrType.ElementType;
        if (retType.SpecialType is AliasSemanticType aliasType)
            return aliasType.TargetType;
        return retType;
    }

    private Dictionary<SymbolHandle, TypeRef> BuildTypeSubstitutionMap(TypeRef type, Symbol? sym)
    {
        Dictionary<SymbolHandle, TypeRef> subst = [];
        if (type.SpecialType is not GenericSemanticType containerGst)
            return subst;

        IReadOnlyList<SymbolHandle> genParams = sym switch
        {
            TypeSymbol ts => ts.GenericParameters,
            NestedTypeSymbol nts => nts.GenericParameters,
            _ => []
        };
        for (int i = 0; i < genParams.Count && i < containerGst.TypeArguments.Count; i++)
            subst[genParams[i]] = containerGst.TypeArguments[i];
        return subst;
    }

    private MethodSymbol? FindDirectOperatorMethod(Symbol? ownerSym, DiscoveredOperatorMethod cand)
    {
        IReadOnlyList<SymbolHandle> methods = ownerSym switch
        {
            ProductTypeSymbol p => p.Methods,
            MemberProductTypeSymbol mp => mp.Methods,
            LocalProductTypeSymbol lp => lp.Methods,
            SumTypeSymbol s => s.Methods,
            MemberSumTypeSymbol ms => ms.Methods,
            LocalSumTypeSymbol ls => ls.Methods,
            ExtensionSymbol ext => ext.Methods,
            _ => []
        };

        foreach (var mh in methods)
        {
            if (context.GetSymbol(mh) is MethodSymbol m && MatchesOperatorMethodSignature(m, cand))
                return m;
        }

        return context.GetSymbol(cand.MethodHandle) as MethodSymbol;
    }

    private bool MatchesOperatorMethodSignature(MethodSymbol method, DiscoveredOperatorMethod cand)
    {
        if (cand.MethodHandle == ResolutionContext.GetHandle(method))
            return true;

        var ifaceSym = context.GetSymbol(cand.InterfaceHandle);
        if (method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
        {
            return method.ExplicitInterfaceName is { } ein &&
                   ifaceSym is not null &&
                   (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text);
        }

        return context.GetSymbol(cand.MethodHandle) is MethodSymbol opMethod &&
               method.Name.Text == opMethod.Name.Text;
    }

    private TypeRef ResolveAllocationTargetFromParent(SymbolHandle parentHandle)
    {
        var sym = context.GetSymbol(parentHandle);
        if (sym is ExtensionSymbol ext)
            return ext.TargetTypeRef;

        IReadOnlyList<SymbolHandle> genParams = sym switch
        {
            TypeSymbol ts => ts.GenericParameters,
            NestedTypeSymbol nts => nts.GenericParameters,
            _ => []
        };
        TypeKind? kind = sym switch
        {
            TypeSymbol ts => ts.TypeKind,
            NestedTypeSymbol nts => nts.TypeKind,
            _ => null
        };
        if (kind == TypeKind.Interface)
        {
            foreach (var gpH in genParams)
            {
                if (context.GetGenericParameterSymbol(gpH) is { ParameterKind: GenericParameterKind.This })
                    return TypeRef.Resolved(gpH);
            }
        }

        return genParams.Count > 0
            ? TypeRef.Generic(parentHandle, sym?.Name.Text ?? string.Empty, [.. genParams.Select(TypeRef.Resolved)])
            : TypeRef.Resolved(parentHandle);
    }

    private void CollectReturnedFieldHandles(SyntaxNode? node, SymbolHandle containerHandle, HashSet<SymbolHandle> fields)
    {
        switch (node)
        {
            case null:
                return;
            case FunctionBlockBody block:
                foreach (var local in block.Locals)
                    CollectReturnedFieldHandles(local, containerHandle, fields);
                return;
            case FunctionLambdaBody lambda:
                CollectReturnedFieldHandles(lambda.Statement, containerHandle, fields);
                return;
            case LocalReturnStatement retStmt:
                CollectReturnedFieldHandlesFromExpr(retStmt.Statement.Expression, containerHandle, fields);
                return;
            case ReturnStatement ret:
                CollectReturnedFieldHandlesFromExpr(ret.Expression, containerHandle, fields);
                return;
            case LocalExpressionStatement exprStmt:
                CollectReturnedFieldHandlesFromExpr(exprStmt.Expression, containerHandle, fields);
                return;
        }
    }

    private void CollectReturnedFieldHandlesFromExpr(Expression? expr, SymbolHandle containerHandle, HashSet<SymbolHandle> fields)
    {
        while (expr is not null)
        {
            switch (expr)
            {
                case ParenthesizedExpression paren:
                    expr = paren.Expression;
                    continue;
                case UnaryExpression unary when unary.OperatorToken.Kind == TokenKind.Ampersand:
                    expr = unary.Operand;
                    continue;
                case BlockExpression block:
                    foreach (var local in block.Locals)
                        CollectReturnedFieldHandles(local, containerHandle, fields);
                    expr = block.FinalExpression;
                    continue;
                case IfExpression ifExpr:
                    CollectReturnedFieldHandlesFromExpr(ifExpr.ThenExpression, containerHandle, fields);
                    CollectReturnedFieldHandlesFromExpr(ifExpr.ElseExpression?.Expression, containerHandle, fields);
                    return;
                case MemberAccessExpression ma:
                    if (context.ResolvedTree.TryGetReference(ma, out var symH) &&
                        context.GetFieldSymbol(symH) is not null &&
                        RootsAtContainerReceiver(ma, containerHandle))
                    {
                        fields.Add(symH);
                    }
                    return;
                default:
                    return;
            }
        }
    }

    private bool RootsAtContainerReceiver(MemberAccessExpression ma, SymbolHandle containerHandle)
    {
        Expression cur = ma.Expression;
        while (true)
        {
            if (cur is ParenthesizedExpression paren)
            {
                cur = paren.Expression;
                continue;
            }
            if (cur is MemberAccessExpression inner)
            {
                cur = inner.Expression;
                continue;
            }
            break;
        }

        if (cur is LiteralExpression lit && lit.Literal.MatchingKind == MatchingKeywordKind.This)
            return true;
        if (cur is IdentifierNameExpression id && id.Identifier.Value == "this")
            return true;
        if (context.ResolvedTree.TryGetReference(cur, out var recvH) &&
            context.GetParameterSymbol(recvH) is { } param &&
            param.Syntax?.Declarator.IsReceiver == true)
        {
            return true;
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
    /// <summary>
    /// Returns true if <paramref name="type"/> is a zero-sized empty struct (no own or inherited
    /// instance fields and not a storage-bearing scalar/primitive/opaque intrinsic, enum, or union)
    /// or the empty unit tuple <c>()</c>.
    /// </summary>
    public bool IsZeroSizedEmptyStruct(TypeRef type)
    {
        if (type.IsError || type.IsUnresolved || type.IsInferred || type.IsLiteral || !type.IsSized(context))
            return false;

        if (type.SpecialType is TupleSemanticType tuple)
            return tuple.Elements.Count == 0;

        if (type.SpecialType is not null and not GenericSemanticType)
            return false;

        if (type.Handle is not { } handle)
            return false;

        handle = context.UnwrapAlias(handle);
        if (handle.Kind == SymbolKind.GenericParameter)
            return false;

        var sym = context.GetSymbol(handle);
        TypeKind? typeKind = sym switch
        {
            TypeSymbol ts => ts.TypeKind,
            NestedTypeSymbol nts => nts.TypeKind,
            _ => null
        };
        if (typeKind != TypeKind.Struct)
            return false;

        if (context.DiscoveredIntrinsics.TryGetIntrinsicName(handle, out _))
            return false;

        if (GetAllStructFields(type).Count > 0)
            return false;

        IReadOnlyList<SymbolHandle> bases = sym switch
        {
            TypeSymbol ts => ts.LinearizedBases,
            NestedTypeSymbol nts => nts.LinearizedBases,
            _ => []
        };
        foreach (var bh in bases)
        {
            var baseUnwrapped = context.UnwrapAlias(bh);
            var baseSym = context.GetSymbol(baseUnwrapped);
            TypeKind? baseKind = baseSym switch
            {
                TypeSymbol bts => bts.TypeKind,
                NestedTypeSymbol bnts => bnts.TypeKind,
                _ => null
            };
            if (baseKind == TypeKind.Struct &&
                context.DiscoveredIntrinsics.TryGetIntrinsicName(baseUnwrapped, out var baseIntr) &&
                baseIntr is not (KnownIntrinsics.ValueType or KnownIntrinsics.Struct))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Collects all instance fields declared directly on or inherited by <paramref name="type"/>.
    /// </summary>
    public IReadOnlyList<SymbolHandle> GetAllStructFields(TypeRef type)
    {
        if (type.Handle is not { } h)
            return [];

        return GetAllStructFields(context, h);
    }

    /// <summary>
    /// Collects all instance fields declared directly on or inherited by <paramref name="typeHandle"/>.
    /// </summary>
    public static IReadOnlyList<SymbolHandle> GetAllStructFields(ResolutionContext context, SymbolHandle typeHandle)
    {
        var unwrapped = context.UnwrapAlias(typeHandle);
        var sym = context.GetSymbol(unwrapped);

        var result = new List<SymbolHandle>();
        var seenTypes = new HashSet<SymbolHandle>();

        void AddFieldsFrom(SymbolHandle th)
        {
            if (!seenTypes.Add(th))
                return;

            IReadOnlyList<SymbolHandle> fields = context.GetSymbol(th) switch
            {
                ProductTypeSymbol p => p.Fields,
                MemberProductTypeSymbol mp => mp.Fields,
                _ => []
            };
            foreach (var fh in fields)
            {
                if (context.GetFieldSymbol(fh) is { } fs && !fs.Flags.HasFlag(VariableFlags.Static))
                    result.Add(fh);
            }
        }

        IReadOnlyList<SymbolHandle> hierarchy = sym switch
        {
            TypeSymbol ts => ts.LinearizedBases,
            NestedTypeSymbol nts => nts.LinearizedBases,
            _ => []
        };
        foreach (var bh in hierarchy)
            AddFieldsFrom(context.UnwrapAlias(bh));

        AddFieldsFrom(unwrapped);
        return result;
    }

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
            case ArraySemanticType fixedArray:
                return RequiresDrop(fixedArray.ElementType);
            case UnionSemanticType union:
                return union.Variants.Any(RequiresDrop);
            case SliceSemanticType slice:
                return RequiresDrop(slice.ElementType);
            case DynSemanticType dyn:
                return dyn.TargetType.SpecialType is not (ReferenceSemanticType or AliasSemanticType or PointerSemanticType);
        }

        if (type.Handle is not { } handle)
            return false;

        handle = context.UnwrapAlias(handle);
        if (handle.Kind == SymbolKind.GenericParameter)
            return context.GetGenericParameterSymbol(handle)?.RequiresCopy(context) != true;
        if (HasLifecycleOperator(handle, OperatorKind.Destructor))
            return true;

        IEnumerable<SymbolHandle> fields = context.GetSymbol(handle) switch
        {
            ProductTypeSymbol product => product.Fields,
            MemberProductTypeSymbol nestedProduct => nestedProduct.Fields,
            _ => []
        };
        return fields.Any(field =>
            context.GetFieldSymbol(field) is { } symbol &&
            !symbol.Flags.HasFlag(VariableFlags.Static) &&
            RequiresDrop(symbol.Type));
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

            case DynSemanticType dyn:
                // Any supported dyn indirection inherits Copy/Move from its underlying pointer-like wrapper.
                return dyn.TargetType.SpecialType is ReferenceSemanticType or AliasSemanticType or PointerSemanticType
                    ? Classify(dyn.TargetType)
                    : OwnershipKind.Move;

            case TupleSemanticType tuple:
                return tuple.Elements.All(e => Classify(e.Type) == OwnershipKind.Copy)
                    ? OwnershipKind.Copy : OwnershipKind.Move;

            case ArraySemanticType fixedArray:
                return Classify(fixedArray.ElementType);

            case UnionSemanticType union:
                return union.Variants.All(v => Classify(v) == OwnershipKind.Copy)
                    ? OwnershipKind.Copy : OwnershipKind.Move;

            case SliceSemanticType slice:
                return Classify(slice.ElementType);
        }

        if (type.Handle is not { } handle)
            return OwnershipKind.Copy;

        handle = context.UnwrapAlias(handle);
        if (handle.Kind == SymbolKind.GenericParameter)
            return context.GetGenericParameterSymbol(handle)?.RequiresCopy(context) == true ? OwnershipKind.Copy : OwnershipKind.Move;

        if (HasLifecycleOperator(handle, OperatorKind.Destructor) ||
            HasLifecycleOperator(handle, OperatorKind.Move) ||
            HasIntrinsicLifecycleInterface(handle, KnownIntrinsics.Move))
            return OwnershipKind.Move;

        // An explicit Copy implementation documents the answer even if the physical layout is
        // opaque to this compilation (for example, a referenced project type).
        if (HasLifecycleOperator(handle, OperatorKind.Copy) ||
            HasIntrinsicLifecycleInterface(handle, KnownIntrinsics.Copy))
            return OwnershipKind.Copy;

        IEnumerable<SymbolHandle> fields = context.GetSymbol(handle) switch
        {
            ProductTypeSymbol product => product.Fields,
            MemberProductTypeSymbol nestedProduct => nestedProduct.Fields,
            _ => []
        };

        foreach (var fieldHandle in fields)
        {
            if (context.GetFieldSymbol(fieldHandle) is { } field &&
                !field.Flags.HasFlag(VariableFlags.Static) &&
                Classify(field.Type) == OwnershipKind.Move)
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
    private bool HasIntrinsicLifecycleInterface(SymbolHandle typeHandle, string intrinsicName)
    {
        if (!context.DiscoveredIntrinsics.TryGet(intrinsicName, out var targetH))
            return false;

        if (TypeRef.IsSubtypeOf(typeHandle, targetH, context))
            return true;

        var sym = context.GetSymbol(typeHandle);
        IReadOnlyList<TypeRef> bases = sym switch
        {
            TypeSymbol ts => ts.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            _ => []
        };
        foreach (var b in bases)
        {
            if (b.Handle is { } bh && TypeRef.IsSubtypeOf(context.UnwrapAlias(bh), targetH, context))
                return true;
        }

        foreach (var conf in context.Conformances)
        {
            if (conf.TargetType.Handle is { } cth && context.UnwrapAlias(cth) == typeHandle &&
                (conf.InterfaceHandle == targetH || TypeRef.IsSubtypeOf(conf.InterfaceHandle, targetH, context)))
                return true;
        }

        return false;
    }

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
