using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Diagnostics;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class DeclarationResolutionPass
{
    /// <summary>
    /// Resolves the signature, explicit return type, parameter types, generic constraints, and body of a top-level function.
    /// Emits MH0240 if intrinsic <c>Function</c> or <c>Callable</c> is missing, and reports MH0124 if a non-partial/non-intrinsic function lacks a body.
    /// </summary>
    private void ResolveFunction(FunctionSymbol symbol)
    {
        if (symbol.Syntax is null)
            return;

        currentTypeSymbol = null;

        var scope = GetOwnedScope(symbol);
        symbol.Attributes = ResolveAttributes(symbol.Syntax.Attributes, symbol.EnclosingScope);

        if (symbol.Syntax.Signature.ArrowToken is null)
        {
            symbol.ReturnType = TypeRef.EmptyTuple;
        }
        else
        {
            symbol.ReturnType = ResolveType(symbol.Syntax.Signature.ReturnType, scope, allowInference: false);
        }

        ResolveTypeConstraints(symbol.Syntax.Signature.Constraints, symbol.GenericParameters, scope);
        ResolveGenericParameterDeclarations(symbol.Syntax.Signature.Identifier, symbol.GenericParameters, scope);

        foreach (var handle in symbol.Parameters)
        {
            if (context.GetParameterSymbol(handle) is { } parameter)
                ResolveParameter(parameter);
        }

        var fnParamTypes = new List<TypeRef>(symbol.Parameters.Count);
        foreach (var handle in symbol.Parameters)
        {
            if (context.GetParameterSymbol(handle) is { } parameter)
            {
                if (symbol.Flags.HasFlag(FunctionFlags.Static) || parameter.Syntax?.Declarator.ReceiverKind == ReceiverKind.None)
                    fnParamTypes.Add(parameter.Type);
            }
        }
        symbol.Type = TypeRef.Function(symbol.ReturnType, fnParamTypes);

        bool isCallableDeclared = context.DiscoveredOperators.TryGetOperators(OperatorKind.Callable, out _);
        bool isFunctionDeclared = context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Function);
        if (!isFunctionDeclared)
        {
            var span = symbol.Syntax.Signature.Identifier.GetSpan() ?? default;
            context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "function declaration", KnownIntrinsics.Function, symbol.Syntax.GetSource());
        }
        if (!isCallableDeclared)
        {
            var span = symbol.Syntax.Signature.Identifier.GetSpan() ?? default;
            context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "function declaration", KnownIntrinsics.OpCallable, symbol.Syntax.GetSource());
        }

        if (symbol.Syntax?.Body is FunctionEmptyBody)
        {
            if (!symbol.Flags.HasFlag(FunctionFlags.Partial) &&
                !symbol.Flags.HasFlag(FunctionFlags.Intrinsic))
            {
                var span = symbol.Syntax.Body.GetSpan() ?? symbol.Syntax.GetSpan() ?? default;
                context.Diagnostics.ReportExpectedBody(span, "a function body", DiagnosticText.Literal(";"), "for non-partial function", symbol.Syntax.GetSource());
            }
        }

        if (symbol.Syntax?.Body is not null)
            ResolveBodyOnce(symbol.Syntax.Body, scope, ResolutionContext.GetHandle(symbol));
    }

    /// <summary>
    /// Resolves the signature, explicit return type, parameter types, generic constraints, and body of a type member method.
    /// Handles <see cref="SpecialFunctionKind.Literal"/> by setting return type to the parent struct and requiring a body unless marked with [IntrinsicFunction].
    /// </summary>
    private void ResolveMethod(MethodSymbol symbol)
    {
        if (symbol.Syntax is null)
            return;

        currentTypeSymbol = symbol.Parent is { } ph ? context.GetSymbol(ph) : null;

        var scope = GetOwnedScope(symbol);
        symbol.Attributes = ResolveAttributes(symbol.Syntax.Attributes, symbol.EnclosingScope);

        if (symbol.SpecialKind is SpecialFunctionKind.Literal)
        {
            symbol.Flags |= FunctionFlags.Static | FunctionFlags.Literal;
            symbol.ReturnType = symbol.Parent is { } parentHandle ? TypeRef.Resolved(parentHandle) : TypeRef.Error;
        }
        else if (symbol.Syntax.Signature.ArrowToken is null)
        {
            symbol.ReturnType = TypeRef.EmptyTuple;
        }
        else
        {
            symbol.ReturnType = ResolveType(symbol.Syntax.Signature.ReturnType, scope, allowInference: false);
        }

        ResolveTypeConstraints(symbol.Syntax.Signature.Constraints, symbol.GenericParameters, scope);
        ResolveGenericParameterDeclarations(symbol.Syntax.Signature.Identifier, symbol.GenericParameters, scope);

        foreach (var handle in symbol.Parameters)
        {
            if (context.GetParameterSymbol(handle) is { } parameter)
                ResolveParameter(parameter);
        }

        var fnParamTypes = new List<TypeRef>(symbol.Parameters.Count);
        foreach (var handle in symbol.Parameters)
        {
            if (context.GetParameterSymbol(handle) is { } parameter)
            {
                if (symbol.Flags.HasFlag(FunctionFlags.Static) || parameter.Syntax?.Declarator.ReceiverKind == ReceiverKind.None)
                    fnParamTypes.Add(parameter.Type);
            }
        }


        if (symbol.Flags.HasFlag(FunctionFlags.Static))
            symbol.Type = TypeRef.Function(symbol.ReturnType, fnParamTypes);
        else
            symbol.Type = TypeRef.Delegate(symbol.ReturnType, fnParamTypes);

        var parentSym = symbol.Parent is { } pH ? context.GetSymbol(pH) : null;
        if (parentSym is ExtensionSymbol)
        {
            bool hasReceiver = symbol.Parameters.Count > 0 &&
                context.GetParameterSymbol(symbol.Parameters[0])?.Syntax?.Declarator.ReceiverKind != ReceiverKind.None;
            if (!hasReceiver)
            {
                symbol.Flags |= FunctionFlags.Static;
                symbol.Type = TypeRef.Function(symbol.ReturnType, fnParamTypes);
            }
        }
        bool isParentInterface = (parentSym as TypeSymbol)?.TypeKind == TypeKind.Interface || (parentSym as NestedTypeSymbol)?.TypeKind == TypeKind.Interface;
        bool isParentIntrinsic = false;
        if (symbol.Parent is { } pHandle)
        {
            foreach (var (_, h) in context.DiscoveredIntrinsics.All)
            {
                if (h == pHandle) { isParentIntrinsic = true; break; }
            }
        }

        if (!isParentInterface && !isParentIntrinsic && context.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null)
        {
            bool isCallableDeclared = context.DiscoveredOperators.TryGetOperators(OperatorKind.Callable, out _);
            if (symbol.Flags.HasFlag(FunctionFlags.Static))
            {
                if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Function))
                {
                    var span = symbol.Syntax.Signature.Identifier.GetSpan() ?? default;
                    context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "function declaration", KnownIntrinsics.Function, symbol.Syntax.GetSource());
                }
            }
            else
            {
                if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Delegate))
                {
                    var span = symbol.Syntax.Signature.Identifier.GetSpan() ?? default;
                    context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "method declaration", KnownIntrinsics.Delegate, symbol.Syntax.GetSource());
                }
            }

            if (!isCallableDeclared)
            {
                var span = symbol.Syntax.Signature.Identifier.GetSpan() ?? default;
                context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "method declaration", KnownIntrinsics.OpCallable, symbol.Syntax.GetSource());
            }
        }

        if (symbol.Syntax?.Body is FunctionEmptyBody && !isParentInterface)
        {
            if (!symbol.Flags.HasFlag(FunctionFlags.Partial) &&
                !symbol.Flags.HasFlag(FunctionFlags.Intrinsic))
            {
                var span = symbol.Syntax.Body.GetSpan() ?? symbol.Syntax.GetSpan() ?? default;
                context.Diagnostics.ReportExpectedBody(span, "a function body", DiagnosticText.Literal(";"), "for non-partial method", symbol.Syntax.GetSource());
            }
        }

        if (symbol.Syntax?.Body is not null)
            ResolveBodyOnce(symbol.Syntax.Body, scope, ResolutionContext.GetHandle(symbol));
    }

    private void ResolveParameter(ParameterSymbol symbol)
    {
        if (symbol.Syntax is null || !resolvedParameters.Add(symbol))
            return;

        symbol.Attributes = ResolveAttributes(symbol.Syntax.Attributes, symbol.EnclosingScope);

        if (symbol.Syntax.Declarator.ReceiverKind != ReceiverKind.None)
        {
            var containingMethod = symbol.ContainingSymbol is { } cs ? context.GetSymbol(cs) : null;
            SymbolHandle? parentHandle = containingMethod switch
            {
                MethodSymbol m => m.Parent,
                _ => null
            };

            if (parentHandle is not null)
            {
                TypeRef baseType;
                var parentSym = context.GetSymbol(parentHandle.Value);
                if (parentSym is ExtensionSymbol extSym)
                {
                    if (extSym.TargetTypeRef.Kind == TypeRefKind.Unresolved && extSym.Syntax is not null)
                    {
                        extSym.TargetTypeRef = ResolveType(extSym.Syntax.TargetType, extSym.Scope, allowInference: false);
                        extSym.TargetTypeHandle = extSym.TargetTypeRef.Handle;
                    }
                    baseType = extSym.TargetTypeRef;
                }
                else
                {
                    baseType = TypeRef.Resolved(parentHandle.Value);
                    IReadOnlyList<SymbolHandle>? genParams = parentSym switch
                    {
                        TypeSymbol ts => ts.GenericParameters,
                        NestedTypeSymbol nts => nts.GenericParameters,
                        _ => null
                    };
                    bool isInterfaceWithThisGp = false;
                    TypeKind parentKind = parentSym switch
                    {
                        TypeSymbol tsKind => tsKind.TypeKind,
                        NestedTypeSymbol ntsKind => ntsKind.TypeKind,
                        _ => TypeKind.Struct
                    };
                    if (parentKind == TypeKind.Interface && genParams is not null)
                    {
                        foreach (var gpH in genParams)
                        {
                            if (context.GetGenericParameterSymbol(gpH) is { ParameterKind: GenericParameterKind.This })
                            {
                                baseType = TypeRef.Resolved(gpH);
                                isInterfaceWithThisGp = true;
                                break;
                            }
                        }
                    }

                    if (!isInterfaceWithThisGp && genParams is not null && genParams.Count > 0)
                    {
                        baseType = TypeRef.Generic(parentHandle.Value, parentSym?.Name.Text ?? "", genParams.Select(TypeRef.Resolved).ToList());
                    }
                }

                bool isUntaggedReceiver = symbol.Syntax.Declarator.Modifiers.Any(m => m.MatchingKind == MatchingKeywordKind.Untagged);
                if (isUntaggedReceiver)
                {
                    baseType = TypeRef.Untagged(baseType);
                }

                symbol.Type = symbol.Syntax.Declarator.ReceiverKind switch
                {
                    ReceiverKind.Value => baseType,
                    ReceiverKind.Alias => TypeRef.Alias(baseType, isMutable: false, isUnsafe: false),
                    ReceiverKind.MutAlias => TypeRef.Alias(baseType, isMutable: true, isUnsafe: false),
                    ReceiverKind.UnsafeAlias => TypeRef.Alias(baseType, isMutable: false, isUnsafe: true),
                    ReceiverKind.MutUnsafeAlias => TypeRef.Alias(baseType, isMutable: true, isUnsafe: true),
                    ReceiverKind.Reference => TypeRef.Reference(baseType, isMutable: false),
                    ReceiverKind.MutReference => TypeRef.Reference(baseType, isMutable: true),
                    ReceiverKind.Pointer => TypeRef.Pointer(baseType, isMutable: false),
                    ReceiverKind.MutPointer => TypeRef.Pointer(baseType, isMutable: true),
                    _ => baseType
                };
                return;
            }
        }

        bool isUntagged = symbol.Syntax.Declarator.Modifiers.Any(m => m.MatchingKind == MatchingKeywordKind.Untagged);

        var paramType = ResolveType(symbol.Syntax.Declarator.Type, symbol.EnclosingScope, isUntaggedContext: isUntagged);
        if (isUntagged)
            paramType = TypeRef.Untagged(paramType);
        if (paramType.SpecialType is AliasSemanticType aliasParam &&
            symbol.Syntax.Declarator.Modifiers.Any(m => m.MatchingKind == MatchingKeywordKind.Mut))
        {
            paramType = TypeRef.Alias(aliasParam.TargetType, isMutable: true, isUnsafe: aliasParam.IsUnsafe);
        }
        symbol.Type = paramType;

        ResolveExpression(symbol.Syntax.Initializer?.Initializer, symbol.EnclosingScope, symbol.ContainingSymbol ?? default);
    }

    private void ResolveVariable(GlobalVariableSymbol symbol, VariableDeclaration? syntax) =>
        ResolveVariableCore(symbol, syntax, value => symbol.Type = value, value => symbol.Attributes = value, allowInference: true);

    private void ResolveVariable(FieldSymbol symbol, VariableDeclaration? syntax) =>
        ResolveVariableCore(symbol, syntax, value => symbol.Type = value, value => symbol.Attributes = value, allowInference: false);

    private void ResolveVariable(LocalVariableSymbol symbol, VariableDeclaration? syntax) =>
        ResolveVariableCore(symbol, syntax, value => symbol.Type = value, value => symbol.Attributes = value, allowInference: true);

    private void ResolveConstant(ConstantSymbol symbol)
    {
        if (symbol.Syntax is not { } syntax)
            return;

        if (syntax.Type is not null)
            symbol.Type = ResolveType(syntax.Type, symbol.EnclosingScope);
        else
            symbol.Type = TypeRef.Unresolved;

        symbol.Attributes = ResolveAttributes(syntax.Attributes, symbol.EnclosingScope);
    }

    private void ResolveVariableCore(Symbol symbol, VariableDeclaration? syntax, Action<TypeRef> setType, Action<List<SymbolHandle>> setAttributes, bool allowInference = false)
    {
        if (syntax is null)
            return;

        currentTypeSymbol = (symbol as FieldSymbol)?.Parent is { } ph ? context.GetSymbol(ph) : null;

        if (!resolvedVariableAttributes.TryGetValue((syntax, symbol.EnclosingScope), out var attributeHandles))
        {
            attributeHandles = ResolveAttributes(syntax.Attributes, symbol.EnclosingScope);
            resolvedVariableAttributes[(syntax, symbol.EnclosingScope)] = attributeHandles;
        }
        setAttributes(attributeHandles);

        bool isUntagged = syntax.Modifiers.Any(m => m.MatchingKind == MatchingKeywordKind.Untagged) ||
                          (symbol is FieldSymbol fs && fs.Flags.HasFlag(VariableFlags.Untagged)) ||
                          (symbol is LocalVariableSymbol ls && ls.Flags.HasFlag(VariableFlags.Untagged)) ||
                          (symbol is GlobalVariableSymbol gs && gs.Flags.HasFlag(VariableFlags.Untagged));

        TypeRef? resolvedType = null;
        bool isTupleDestructuring = syntax.Declarator.Identifier is TupleName;
        if (isTupleDestructuring && syntax.Type is TupleType tupleType)
        {
            var matchedElement = tupleType.Elements.FirstOrDefault(e => e.Name != null && context.GetScopedSymbolName(new SimpleName(e.Name)).Last == symbol.Name);

            if (matchedElement != null)
                resolvedType = ResolveType(matchedElement.Type, symbol.EnclosingScope, allowInference: false, isUntaggedContext: isUntagged);
        }

        var actualType = resolvedType ?? ResolveType(syntax.Type, symbol.EnclosingScope, allowInference: allowInference, isUntaggedContext: isUntagged);
        if (isUntagged)
            actualType = TypeRef.Untagged(actualType);
        setType(actualType);

        bool isInferred = actualType.IsInferred;

        var declarator = syntax.Declarator;
        if (actualType.SpecialType is AliasSemanticType aliasSym)
        {
            if (syntax.Modifiers.Any(m => m.MatchingKind == MatchingKeywordKind.Mut))
            {
                actualType = TypeRef.Alias(aliasSym.TargetType, isMutable: true, isUnsafe: aliasSym.IsUnsafe);
                setType(actualType);
            }

            if (declarator.Initializer?.Initializer is null)
            {
                var nameText = declarator.Identifier is SimpleName sn ? sn.Name.Value : symbol.Name.Text;
                context.Diagnostics.ReportAliasMustBeInitialized(declarator.Identifier.GetSpan() ?? default, nameText, syntax.GetSource());
            }
        }

        if (declarator.Identifier is TupleName dTupleName)
        {
            if (dTupleName.Elements.Any(e => context.GetScopedSymbolName(e).Last == symbol.Name))
                if (declarator.Initializer?.Initializer is { } initExpr && resolvedDeclarators.Add(declarator))
                {
                    CheckInferredInitializer(initExpr, isInferred);
                    ResolveExpression(initExpr, symbol.EnclosingScope, GetContainingFunction(symbol));
                }
        }
        else if (context.GetScopedSymbolName(declarator.Identifier).Last == symbol.Name)
            if (declarator.Initializer?.Initializer is { } initExpr && resolvedDeclarators.Add(declarator))
            {
                CheckInferredInitializer(initExpr, isInferred);
                ResolveExpression(initExpr, symbol.EnclosingScope, GetContainingFunction(symbol));
            }
    }

    private void ResolveProperty(PropertySymbol symbol)
    {
        if (symbol.Syntax is null)
            return;

        currentTypeSymbol = symbol.Parent is { } ph ? context.GetSymbol(ph) : null;

        symbol.Attributes = ResolveAttributes(symbol.Syntax.Attributes, symbol.EnclosingScope);
        symbol.Type = ResolveType(symbol.Syntax.Type, symbol.EnclosingScope);

        foreach (var accessor in symbol.Syntax.Body.Accessors)
        {
            var scope = context.GetSyntaxScope(accessor.Body, symbol.EnclosingScope);
            var attributes = ResolveAttributes(accessor.Attributes, symbol.EnclosingScope);

            if (accessor.Kind is PropertyAccessorKind.Get)
                symbol.GetterAttributes = attributes;
            else
                symbol.SetterAttributes = attributes;

            ResolveBodyOnce(accessor.Body, scope, ResolutionContext.GetHandle(symbol));
        }
    }

    private void ResolveBodyOnce(FunctionBody body, Scope scope, SymbolHandle containingFunction)
    {
        if (!resolvedBodies.Add(body))
            return;

        if (body is FunctionBlockBody block)
            foreach (var local in block.Locals)
                ResolveLocal(local, scope, containingFunction);
        else if (body is FunctionLambdaBody lambda)
            ResolveLocal(lambda.Statement, scope, containingFunction);
    }

    private void ResolveLocal(Local local, Scope scope, SymbolHandle containingFunction)
    {
        switch (local)
        {
            case LocalExpressionStatement statement:
                ResolveExpression(statement.Expression, scope, containingFunction);
                break;
            case LocalReturnStatement statement:
                ResolveExpression(statement.Statement.Expression, scope, containingFunction);
                break;
            case LocalYieldStatement statement:
                if (statement.Expression is not null)
                    ResolveExpression(statement.Expression, scope, containingFunction);
                break;
            case LocalGotoStatement statement:
                ResolveLabelReference(statement, new SymbolPart(statement.Identifier), containingFunction);
                break;
            case LocalVariableDeclarationStatement statement:
                ResolveDeclarationSyntax(statement.Declaration, scope, containingFunction);
                break;
        }
    }

    private void ResolveDeclarationSyntax(VariableDeclaration syntax, Scope scope, SymbolHandle containingFunction)
    {
        var resolvedType = ResolveType(syntax.Type, scope, allowInference: true);
        bool isInferred = resolvedType.IsInferred;

        if (!resolvedVariableAttributes.TryGetValue((syntax, scope), out _))
        {
            foreach (var attribute in syntax.Attributes)
                foreach (var application in attribute.Attributes)
                    ResolveNamed(application.Name, scope, application);
        }

        var declarator = syntax.Declarator;
        if (resolvedType.SpecialType is AliasSemanticType aliasSym)
        {
            if (declarator.Initializer?.Initializer is null)
            {
                var nameText = declarator.Identifier is SimpleName sn ? sn.Name.Value : declarator.Identifier.ToString() ?? "";
                context.Diagnostics.ReportAliasMustBeInitialized(declarator.Identifier.GetSpan() ?? default, nameText, syntax.GetSource());
            }
        }

        if (declarator.Initializer?.Initializer is { } initExpr && resolvedDeclarators.Add(declarator))
        {
            CheckInferredInitializer(initExpr, isInferred);
            ResolveExpression(initExpr, scope, containingFunction);
        }
    }

    private void ResolveLabelReference(SyntaxNode syntax, SymbolPart name, SymbolHandle containingFunction)
    {
        LabelSymbol? found = null;

        foreach (var label in context.LabelSymbols)
        {
            if (label.ContainingFunction != containingFunction || label.Name != name)
                continue;
            if (found is not null)
                return;

            found = label;
        }

        if (found is not null)
            context.ResolvedTree.AddReference(syntax, ResolutionContext.GetHandle(found));
    }

    private static Scope GetOwnedScope(Symbol symbol) =>
        symbol.EnclosingScope.GetChildScope(ResolutionContext.GetHandle(symbol)) ?? symbol.EnclosingScope;


    private static SymbolHandle GetContainingFunction(Symbol symbol) => symbol switch
    {
        ParameterSymbol parameter when parameter.ContainingSymbol is { } handle => handle,
        LocalVariableSymbol local when local.Parent is { } handle => handle,
        _ => default
    };

    private static void PopulateProductMembers(TypeSymbol symbol)
    {
        if (symbol is ProductTypeSymbol product)
            PopulateProductMembers(product.Fields, product.Properties, product.Methods, product.NestedTypes, GetOwnedScope(product));
    }

    private static void PopulateProductMembers(NestedTypeSymbol symbol)
    {
        if (symbol is MemberProductTypeSymbol member)
            PopulateProductMembers(member.Fields, member.Properties, member.Methods, member.NestedTypes, GetOwnedScope(member));
        else if (symbol is LocalProductTypeSymbol local)
            PopulateProductMembers(local.Fields, local.Properties, local.Methods, local.NestedTypes, GetOwnedScope(local));
    }

    private static void PopulateProductMembers(List<SymbolHandle> fields, List<SymbolHandle> properties, List<SymbolHandle> methods, List<SymbolHandle> nestedTypes, Scope scope)
    {
        fields.Clear();
        properties.Clear();
        methods.Clear();
        nestedTypes.Clear();

        foreach (var symbol in scope.Symbols.Values)
        {
            var handle = ResolutionContext.GetHandle(symbol);

            switch (symbol.Kind)
            {
                case SymbolKind.Field:
                    fields.Add(handle);
                    break;
                case SymbolKind.Property:
                    properties.Add(handle);
                    break;
                case SymbolKind.Method:
                    methods.Add(handle);
                    break;
                case SymbolKind.NestedType:
                    nestedTypes.Add(handle);
                    break;
            }
        }
    }

    private static void PopulateSumTypeMembers(ISumTypeSymbol sumType, Scope scope)
    {
        sumType.Methods.Clear();
        sumType.NestedTypes.Clear();

        foreach (var symbol in scope.Symbols.Values)
        {
            var handle = ResolutionContext.GetHandle(symbol);

            switch (symbol.Kind)
            {
                case SymbolKind.Method:
                    sumType.Methods.Add(handle);
                    break;
                case SymbolKind.NestedType:
                    sumType.NestedTypes.Add(handle);
                    break;
            }
        }
    }

}
