using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Resolution.Ownership;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

internal sealed partial class ExpressionResolutionPass
{
    private enum CallableCapability : byte
    {
        Shared = 0,
        Mutable = 1,
        Once = 2
    }

    private int nextClosureId;
    private readonly HashSet<MemberAccessExpression> directCallMemberAccesses = [];

    private static TypeRef UnwrapAliasAndDyn(TypeRef type)
    {
        while (type.SpecialType is AliasSemanticType or DynSemanticType)
        {
            type = type.SpecialType is AliasSemanticType al
                ? al.TargetType
                : ((DynSemanticType)type.SpecialType!).TargetType;
        }
        return type;
    }

    private bool TryExtractCallableExpectation(
        TypeRef expectedType,
        out IReadOnlyList<TypeRef>? parameterTypes,
        out TypeRef? returnType)
    {
        parameterTypes = null;
        returnType = null;

        var unwrapped = UnwrapAliasAndDyn(expectedType);
        if (unwrapped.SpecialType is ReferenceSemanticType or PointerSemanticType)
            unwrapped = UnwrapAliasAndDyn(unwrapped.SpecialType is ReferenceSemanticType r ? r.ElementType : ((PointerSemanticType)unwrapped.SpecialType!).ElementType);

        if (unwrapped.SpecialType is FunctionSemanticType fn)
        {
            parameterTypes = fn.ParameterTypes;
            returnType = fn.ReturnType;
            return true;
        }

        if (unwrapped.SpecialType is GenericSemanticType gen && gen.TypeArguments.Count >= 1)
        {
            var ifaceH = context.UnwrapAlias(gen.TargetHandle);
            if (IsCallableInterfaceHandle(ifaceH))
            {
                returnType = gen.TypeArguments[0];
                if (gen.TypeArguments.Count == 2 && gen.TypeArguments[1].SpecialType is TupleSemanticType tup)
                    parameterTypes = tup.Elements.Select(e => e.Type).ToList();
                else
                    parameterTypes = gen.TypeArguments.Skip(1).ToList();
                return true;
            }
        }

        return false;
    }

    private bool IsCallableInterfaceHandle(SymbolHandle ifaceHandle)
    {
        ifaceHandle = context.UnwrapAlias(ifaceHandle);
        foreach (var kind in new[] { OperatorKind.Callable, OperatorKind.CallableMut, OperatorKind.CallableOnce })
        {
            if (context.DiscoveredOperators.TryGetOperators(kind, out var list))
            {
                foreach (var op in list)
                {
                    if (context.UnwrapAlias(op.InterfaceHandle) == ifaceHandle)
                        return true;
                }
            }
        }
        return false;
    }

    private TypeRef FinalizeMethodMemberAccess(
        MemberAccessExpression access,
        Symbol? member,
        TypeRef receiverType,
        TypeRef resolvedType,
        bool isTypeAccess,
        Scope scope)
    {
        if (member is not MethodSymbol method || isTypeAccess || method.Flags.HasFlag(FunctionFlags.Static))
            return resolvedType;

        if (directCallMemberAccesses.Contains(access))
        {
            context.ResolvedTree.MarkBoundMethodCall(access);
            return resolvedType;
        }

        return SynthesizeBoundMethodClosure(access, method, receiverType, resolvedType, scope);
    }

    private TypeRef SynthesizeBoundMethodClosure(
        MemberAccessExpression access,
        MethodSymbol method,
        TypeRef receiverType,
        TypeRef resolvedMethodType,
        Scope scope)
    {
        var recvParam = method.Parameters.Count > 0 ? context.GetParameterSymbol(method.Parameters[0]) : null;
        var recvKind = recvParam?.ReceiverKind ?? ReceiverKind.Reference;

        var concreteRecv = UnwrapAliasAndDyn(receiverType);
        TypeRef captureFieldType = recvKind switch
        {
            ReceiverKind.Value => concreteRecv,
            ReceiverKind.MutReference or ReceiverKind.MutAlias => TypeRef.Reference(concreteRecv, isMutable: true),
            ReceiverKind.Pointer => TypeRef.Pointer(concreteRecv, isMutable: false),
            ReceiverKind.MutPointer => TypeRef.Pointer(concreteRecv, isMutable: true),
            _ => TypeRef.Reference(concreteRecv, isMutable: false)
        };

        var capability = recvKind switch
        {
            ReceiverKind.Value => CallableCapability.Once,
            ReceiverKind.MutReference or ReceiverKind.MutAlias or ReceiverKind.MutPointer => CallableCapability.Mutable,
            _ => CallableCapability.Shared
        };

        var fnType = resolvedMethodType.SpecialType as FunctionSemanticType;
        var returnType = fnType?.ReturnType ?? method.ReturnType;
        var paramTypes = fnType is not null && fnType.ParameterTypes.Count > 0 && recvParam?.IsReceiver == true
            ? fnType.ParameterTypes.Skip(1).ToList()
            : (fnType?.ParameterTypes.ToList() ?? []);

        bool needsDrop = recvKind == ReceiverKind.Value && RequiresDropForClosureField(concreteRecv);
        var closureSym = SynthesizeClosureStructSymbol(
            scope,
            returnType,
            paramTypes,
            capability,
            [("__self", captureFieldType, needsDrop)]);

        var closureHandle = ResolutionContext.GetHandle(closureSym);
        context.ResolvedTree.SetBoundMethodClosure(
            access,
            new ResolvedTree.BoundMethodClosureInfo(method, recvKind, concreteRecv, closureHandle));

        return TypeRef.Resolved(closureHandle);
    }

    private TypeRef SynthesizeLambdaClosureType(
        LambdaExpression lambda,
        Scope lambdaScope,
        TypeRef returnType,
        IReadOnlyList<TypeRef> parameterTypes,
        IReadOnlyList<ResolvedTree.LambdaCaptureInfo> captures)
    {
        var classifier = new OwnershipTypeClassifier(context);
        var capability = CallableCapability.Shared;
        var fields = new List<(string Name, TypeRef FieldType, bool HasDropOrMove)>(captures.Count);

        foreach (var cap in captures)
        {
            TypeRef rawCapType = GetTypeFromSymbol(cap.Symbol);
            while (rawCapType.SpecialType is AliasSemanticType al)
                rawCapType = al.IsUnsafe
                    ? TypeRef.Pointer(al.TargetType, al.IsMutable && cap.IsMutable)
                    : TypeRef.Reference(al.TargetType, al.IsMutable && cap.IsMutable);

            if (cap.IsMove)
            {
                capability = CallableCapability.Once;
                bool hasDrop = RequiresDropForClosureField(rawCapType);
                fields.Add((cap.Symbol.Name.Text, rawCapType, hasDrop));
            }
            else if (cap.IsMutable)
            {
                if (capability == CallableCapability.Shared)
                    capability = CallableCapability.Mutable;
                var capRef = rawCapType.SpecialType is ReferenceSemanticType
                    ? rawCapType
                    : TypeRef.Reference(rawCapType, isMutable: true);
                fields.Add((cap.Symbol.Name.Text, capRef, false));
            }
            else
            {
                var capRef = rawCapType.SpecialType is ReferenceSemanticType r
                    ? TypeRef.Reference(r.ElementType, isMutable: false)
                    : TypeRef.Reference(rawCapType, isMutable: false);
                fields.Add((cap.Symbol.Name.Text, capRef, false));
            }
        }

        var closureSym = SynthesizeClosureStructSymbol(lambdaScope, returnType, parameterTypes, capability, fields);
        var closureType = TypeRef.Resolved(ResolutionContext.GetHandle(closureSym));
        context.ResolvedTree.SetLambdaClosureType(lambda, closureType);
        return closureType;
    }

    private bool RequiresDropForClosureField(TypeRef type)
    {
        var classifier = new OwnershipTypeClassifier(context);
        return classifier.Classify(type) == OwnershipKind.Move;
    }

    private TypeSymbol SynthesizeClosureStructSymbol(
        Scope parentScope,
        TypeRef returnType,
        IReadOnlyList<TypeRef> parameterTypes,
        CallableCapability capability,
        IReadOnlyList<(string Name, TypeRef FieldType, bool HasDropOrMove)> captures)
    {
        var closureSym = context.CreateTypeSymbol(
            context.GlobalScope,
            new SymbolPart($"__Closure_{++nextClosureId}"),
            TypeKind.Struct,
            containingNamespace: null,
            syntax: null);

        closureSym.Flags = TypeFlags.Public | TypeFlags.Sealed;
        var closureHandle = ResolutionContext.GetHandle(closureSym);
        var closureSelf = TypeRef.Resolved(closureHandle);
        var closureScope = context.CreateScope(parentScope);
        ResolutionContext.BindChildScope(context.GlobalScope, closureSym, closureScope);

        var baseTypes = new List<TypeRef>();
        var linearizedBases = new List<SymbolHandle> { closureHandle };

        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Struct, out var structH))
        {
            baseTypes.Add(TypeRef.Resolved(structH));
            linearizedBases.Add(structH);
        }
        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.ValueType, out var valTypeH))
            linearizedBases.Add(valTypeH);
        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Move, out var moveH))
        {
            baseTypes.Add(TypeRef.Resolved(moveH));
            linearizedBases.Add(moveH);
        }

        if (closureSym is ProductTypeSymbol product)
        {
            for (int i = 0; i < captures.Count; i++)
            {
                var (capName, capType, _) = captures[i];
                var fieldSym = context.CreateFieldSymbol(
                    closureScope,
                    new SymbolPart($"_cap_{i}_{capName}"),
                    closureHandle,
                    syntax: null);
                fieldSym.Flags = VariableFlags.Private;
                fieldSym.Type = capType;
                product.Fields.Add(ResolutionContext.GetHandle(fieldSym));
            }
        }

        if (capability == CallableCapability.Shared)
        {
            RegisterClosureCallable(closureHandle, closureScope, closureSelf, OperatorKind.Callable, ReceiverKind.Reference, returnType, parameterTypes, baseTypes, linearizedBases);
        }
        if (capability <= CallableCapability.Mutable)
        {
            RegisterClosureCallable(closureHandle, closureScope, closureSelf, OperatorKind.CallableMut, ReceiverKind.MutReference, returnType, parameterTypes, baseTypes, linearizedBases);
        }
        RegisterClosureCallable(closureHandle, closureScope, closureSelf, OperatorKind.CallableOnce, ReceiverKind.Value, returnType, parameterTypes, baseTypes, linearizedBases);

        if (capability == CallableCapability.Once &&
            captures.Any(c => c.HasDropOrMove) &&
            context.DiscoveredOperators.TryGetOperators(OperatorKind.Destructor, out var dtorOps) &&
            dtorOps.Count > 0)
        {
            var dtorOp = dtorOps[0];
            var dtorIfaceSym = context.GetTypeSymbol(dtorOp.InterfaceHandle);
            if (dtorIfaceSym is not null)
            {
                var dtorType = TypeRef.Resolved(dtorOp.InterfaceHandle);
                baseTypes.Add(dtorType);
                linearizedBases.Add(dtorOp.InterfaceHandle);

                var dropMethod = context.CreateMemberMethodSymbol(closureScope, new SymbolPart(context.GetMethodSymbol(dtorOp.MethodHandle)?.Name.Text ?? "Drop"), closureHandle, syntax: null);
                dropMethod.Flags = FunctionFlags.Public | FunctionFlags.InterfaceScoped;
                dropMethod.ExplicitInterfaceName = new SymbolName(dtorIfaceSym.Name);
                dropMethod.ReturnType = TypeRef.EmptyTuple;
                var dropMethodH = ResolutionContext.GetHandle(dropMethod);
                var dropRecv = context.CreateParameterSymbol(closureScope, new SymbolPart("this"), dropMethodH, syntax: null);
                dropRecv.SynthesizedReceiverKind = ReceiverKind.MutReference;
                dropRecv.Type = TypeRef.Reference(closureSelf, isMutable: true);
                dropMethod.Parameters.Add(ResolutionContext.GetHandle(dropRecv));

                var dtorConf = context.RegisterConformance(closureSelf, dtorType, dtorOp.InterfaceHandle, Accessibility.Public, closureHandle, isExtension: false, syntax: null);
                dtorConf.MethodMap[dtorOp.MethodHandle] = dropMethodH;
            }
        }

        closureSym.BaseTypes = baseTypes;
        closureSym.LinearizedBases = linearizedBases;
        return closureSym;
    }

    private void RegisterClosureCallable(
        SymbolHandle closureHandle,
        Scope closureScope,
        TypeRef closureSelf,
        OperatorKind opKind,
        ReceiverKind recvKind,
        TypeRef returnType,
        IReadOnlyList<TypeRef> parameterTypes,
        List<TypeRef> baseTypes,
        List<SymbolHandle> linearizedBases)
    {
        if (!context.DiscoveredOperators.TryGetOperators(opKind, out var opList) || opList.Count == 0)
            return;

        var op = opList[0];
        var ifaceSym = context.GetTypeSymbol(op.InterfaceHandle);
        var opMethod = context.GetMethodSymbol(op.MethodHandle);
        if (ifaceSym is null || opMethod is null)
            return;

        var ifaceArgs = new List<TypeRef>(1 + parameterTypes.Count) { returnType };
        ifaceArgs.AddRange(parameterTypes);
        var ifaceType = ifaceSym.GenericParameters.Count > 0
            ? TypeRef.Generic(op.InterfaceHandle, ifaceSym.Name.Text, ifaceArgs)
            : TypeRef.Resolved(op.InterfaceHandle);

        baseTypes.Add(ifaceType);
        linearizedBases.Add(op.InterfaceHandle);

        var invokeMethod = context.CreateMemberMethodSymbol(closureScope, new SymbolPart(opMethod.Name.Text), closureHandle, syntax: null);
        invokeMethod.Flags = FunctionFlags.Public | FunctionFlags.InterfaceScoped;
        invokeMethod.ExplicitInterfaceName = new SymbolName(ifaceSym.Name);
        invokeMethod.ReturnType = returnType;

        var invokeHandle = ResolutionContext.GetHandle(invokeMethod);
        var recvParam = context.CreateParameterSymbol(closureScope, new SymbolPart("this"), invokeHandle, syntax: null);
        recvParam.SynthesizedReceiverKind = recvKind;
        recvParam.Type = recvKind switch
        {
            ReceiverKind.Reference => TypeRef.Reference(closureSelf, isMutable: false),
            ReceiverKind.MutReference => TypeRef.Reference(closureSelf, isMutable: true),
            _ => closureSelf
        };
        invokeMethod.Parameters.Add(ResolutionContext.GetHandle(recvParam));

        for (int i = 0; i < parameterTypes.Count; i++)
        {
            var argParam = context.CreateParameterSymbol(closureScope, new SymbolPart($"_arg{i}"), invokeHandle, syntax: null);
            argParam.Type = parameterTypes[i];
            invokeMethod.Parameters.Add(ResolutionContext.GetHandle(argParam));
        }

        var fnParams = new List<TypeRef>(1 + parameterTypes.Count) { recvParam.Type };
        fnParams.AddRange(parameterTypes);
        invokeMethod.Type = TypeRef.Function(returnType, fnParams);

        var conf = context.RegisterConformance(
            closureSelf,
            ifaceType,
            op.InterfaceHandle,
            Accessibility.Public,
            closureHandle,
            isExtension: false,
            syntax: null);
        conf.MethodMap[op.MethodHandle] = invokeHandle;
    }

    private bool IsSymbolMovedInLambda(LambdaExpression lambda, Symbol targetSym, TypeRef targetType)
    {
        if (new OwnershipTypeClassifier(context).Classify(targetType) != OwnershipKind.Move)
            return false;

        var targetH = ResolutionContext.GetHandle(targetSym);
        bool moved = false;

        bool IsDirectTargetId(Expression expr)
        {
            while (expr is ParenthesizedExpression p)
                expr = p.Expression;
            return expr is IdentifierNameExpression id &&
                   context.ResolvedTree.TryGetReference(id, out var h) &&
                   h == targetH;
        }

        void Walk(SyntaxNode? node, SyntaxNode? parent)
        {
            if (node is null || moved)
                return;

            if (node is LambdaExpression nested && !ReferenceEquals(nested, lambda))
            {
                if (context.ResolvedTree.TryGetLambdaCaptures(nested, out var nestedCaps) &&
                    nestedCaps.Any(c => c.Symbol == targetSym && c.IsMove))
                {
                    moved = true;
                }
                return;
            }

            if (node is IdentifierNameExpression id &&
                context.ResolvedTree.TryGetReference(id, out var h) &&
                h == targetH)
            {
                SyntaxNode? effectiveParent = parent;
                Expression wrapped = id;
                if (effectiveParent is ParenthesizedExpression)
                {
                    // conservatively inspect outer AST below if parenthesized
                    moved = true;
                    return;
                }

                switch (effectiveParent)
                {
                    case UnaryExpression u when u.OperatorToken.Kind is TokenKind.Ampersand or TokenKind.Asterisk:
                        break;

                    case MemberAccessExpression m when ReferenceEquals(m.Expression, id):
                        break;

                    case LocalReturnStatement:
                        moved = true;
                        break;

                    case CallExpression call:
                        if (!IsBorrowedCallArgument(call, id))
                            moved = true;
                        break;

                    default:
                        if (effectiveParent is not BinaryExpression && effectiveParent is not AssignmentExpression)
                            moved = true;
                        else if (effectiveParent is AssignmentExpression ae && ReferenceEquals(ae.RhsExpression, id))
                            moved = true;
                        break;
                }
                return;
            }

            if (node is MemberAccessExpression member && IsDirectTargetId(member.Expression))
            {
                if (parent is CallExpression call && ReferenceEquals(call.Callee, member))
                {
                    if (context.ResolvedTree.TryGetReference(member, out var mH) &&
                        context.GetSymbol(mH) is MethodSymbol mSym &&
                        mSym.Parameters.Count > 0 &&
                        context.GetParameterSymbol(mSym.Parameters[0]) is { } p0 &&
                        p0.ReceiverKind == ReceiverKind.Value)
                    {
                        moved = true;
                        return;
                    }
                }
            }

            if (node is LambdaExpression rootLambda && ReferenceEquals(rootLambda, lambda))
            {
                if (rootLambda.Body is not BlockExpression && IsDirectTargetId(rootLambda.Body))
                {
                    moved = true;
                    return;
                }
                Walk(rootLambda.Body, rootLambda);
                return;
            }

            foreach (var (_, child) in SyntaxSpan.GetChildren(node))
                Walk(child, node);
        }

        Walk(lambda, null);
        return moved;
    }

    private bool IsBorrowedCallArgument(CallExpression call, Expression argExpr)
    {
        if (!context.ResolvedTree.TryGetReference(call.Callee, out var h))
            return false;

        IReadOnlyList<SymbolHandle> paramHandles = context.GetSymbol(h) switch
        {
            FunctionSymbol f => f.Parameters,
            MethodSymbol m => m.Parameters,
            _ => []
        };
        int offset = context.GetSymbol(h) is MethodSymbol ms &&
                     !ms.Flags.HasFlag(FunctionFlags.Static) &&
                     call.Callee is MemberAccessExpression ma &&
                     context.ResolvedTree.IsBoundMethodCall(ma)
            ? 1
            : 0;

        for (int i = 0; i < call.Arguments.Count && i + offset < paramHandles.Count; i++)
        {
            if (ReferenceEquals(call.Arguments[i], argExpr))
            {
                var pt = context.GetParameterSymbol(paramHandles[i + offset])?.Type ?? TypeRef.Unresolved;
                return UnwrapAliasAndDyn(pt).SpecialType is ReferenceSemanticType or AliasSemanticType or PointerSemanticType;
            }
        }

        return false;
    }
}
