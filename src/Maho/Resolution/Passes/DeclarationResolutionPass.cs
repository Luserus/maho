using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

/// <summary>Resolves declaration-owned references after every declaration is known.</summary>
internal sealed class DeclarationResolutionPass : ResolutionPass
{
    private record struct FunctionSignatureInfo(Symbol Symbol, FunctionFlags Flags, FunctionDeclaration? Syntax, IReadOnlyList<SymbolHandle> GenericParameters, List<SymbolHandle> Parameters, TypeRef ReturnType);

    private ResolutionContext context = null!;
    private readonly HashSet<SyntaxNode> resolvedBodies = [];
    private readonly Dictionary<(TypeSyntax, Scope, bool), TypeRef> resolvedTypes = [];
    private readonly HashSet<VariableDeclarator> resolvedDeclarators = [];
    private readonly HashSet<ParameterSymbol> resolvedParameters = [];
    private readonly Dictionary<(VariableDeclaration, Scope), List<SymbolHandle>> resolvedVariableAttributes = [];

    public override void Resolve(ResolutionContext context)
    {
        this.context = context;
        resolvedBodies.Clear();
        resolvedTypes.Clear();
        resolvedDeclarators.Clear();
        resolvedParameters.Clear();
        resolvedVariableAttributes.Clear();

        foreach (var attribute in context.AttributeSymbols)
            ResolveAttributeDeclaration(attribute, attribute.Syntax);

        foreach (var attribute in context.NestedAttributeSymbols)
            ResolveAttributeDeclaration(attribute, attribute.Syntax);

        foreach (var type in context.TypeSymbols)
            ResolveType(type, type.Syntax);

        foreach (var type in context.NestedTypeSymbols)
            ResolveType(type, type.Syntax);

        foreach (var alias in context.AliasSymbols)
            ResolveAlias(alias);

        foreach (var function in context.FunctionSymbols)
            ResolveFunction(function);

        foreach (var method in context.MethodSymbols)
            ResolveMethod(method);

        foreach (var parameter in context.ParameterSymbols)
            ResolveParameter(parameter);

        foreach (var global in context.GlobalVariableSymbols)
            ResolveVariable(global, global.Syntax);

        foreach (var field in context.FieldSymbols)
            ResolveVariable(field, field.Syntax);

        foreach (var local in context.LocalVariableSymbols)
            ResolveVariable(local, local.Syntax);

        foreach (var property in context.PropertySymbols)
            ResolveProperty(property);

        // Resolve throwaway sink declarations so their signatures and bodies resolve error-free
        foreach (var attribute in context.SinkSymbols.AttributeSymbols)
            ResolveAttributeDeclaration(attribute, attribute.Syntax);

        foreach (var attribute in context.SinkSymbols.NestedAttributeSymbols)
            ResolveAttributeDeclaration(attribute, attribute.Syntax);

        foreach (var type in context.SinkSymbols.TypeSymbols)
            ResolveType(type, type.Syntax);

        foreach (var type in context.SinkSymbols.NestedTypeSymbols)
            ResolveType(type, type.Syntax);

        foreach (var alias in context.SinkSymbols.AliasSymbols)
            ResolveAlias(alias);

        foreach (var function in context.SinkSymbols.FunctionSymbols)
            ResolveFunction(function);

        foreach (var method in context.SinkSymbols.MethodSymbols)
            ResolveMethod(method);

        foreach (var parameter in context.SinkSymbols.ParameterSymbols)
            ResolveParameter(parameter);

        foreach (var global in context.SinkSymbols.GlobalVariableSymbols)
            ResolveVariable(global, global.Syntax);

        foreach (var field in context.SinkSymbols.FieldSymbols)
            ResolveVariable(field, field.Syntax);

        foreach (var local in context.SinkLocalVariableSymbols)
            ResolveVariable(local, local.Syntax);

        foreach (var property in context.SinkSymbols.PropertySymbols)
            ResolveProperty(property);

        foreach (var root in context.SyntaxTree.Roots)
            ResolveTopLevelStatements(root);

        CheckDuplicateTypeDeclarations();
        CheckCyclicTypeHierarchies();
        CheckDuplicateAndPartialFunctions();
        CheckDuplicateVariables();
        CheckDuplicateProperties();
    }

    private void ResolveTopLevelStatements(CompilationUnit root)
    {
        var scope = context.GetSyntaxScope(root, context.GlobalScope);
        SymbolHandle? main = null;

        foreach (var function in context.FunctionSymbols)
        {
            if (function.Syntax is null && ReferenceEquals(GetOwnedScope(function), scope))
            {
                main = ResolutionContext.GetHandle(function);
                break;
            }
        }

        if (main is null)
            return;

        foreach (var member in root.Members)
            ResolveTopLevel(member, scope, main.Value);
    }

    private void ResolveTopLevel(TopLevel node, Scope scope, SymbolHandle containingFunction)
    {
        switch (node)
        {
            case TopLevelBlockDeclaration block:
                {
                    var blockScope = context.GetSyntaxScope(block, scope);
                    foreach (var member in block.Members)
                        ResolveTopLevel(member, blockScope, containingFunction);
                    break;
                }
            case NamespaceDeclaration declaration when declaration.Body is NamespaceBlockBody body:
                {
                    var blockScope = context.GetSyntaxScope(body, scope);
                    foreach (var member in body.Members)
                        ResolveTopLevel(member, blockScope, containingFunction);
                    break;
                }
            case TopLevelExpressionStatement statement:
                ResolveExpression(statement.Expression, scope, containingFunction);
                break;
            case TopLevelReturnStatement statement:
                ResolveExpression(statement.Statement.Expression, scope, containingFunction);
                break;
            case TopLevelGotoStatement statement:
                ResolveLabelReference(statement, new SymbolPart(statement.Identifier), containingFunction);
                break;
            case TopLevelIfStatement statement:
                Scope ifScope = statement.Declaration is not null ? context.CreateScope(scope) : scope;
                if (statement.Declaration is not null)
                {
                    ResolveDeclarationSyntax(statement.Declaration, ifScope, containingFunction);
                }
                ResolveExpression(statement.Condition, ifScope, containingFunction);
                ResolveTopLevel(statement.ThenStatement, ifScope, containingFunction);

                if (statement.ElseStatement is not null)
                    ResolveTopLevel(statement.ElseStatement.Statement, ifScope, containingFunction);
                break;
            case TopLevelWhileStatement statement:
                Scope whileScope = statement.Declaration is not null ? context.CreateScope(scope) : scope;
                if (statement.Declaration is not null)
                {
                    ResolveDeclarationSyntax(statement.Declaration, whileScope, containingFunction);
                }
                ResolveExpression(statement.Condition, whileScope, containingFunction);
                ResolveTopLevel(statement.Statement, whileScope, containingFunction);
                break;
        }
    }

    private void ResolveAttributeDeclaration(AttributeSymbol symbol, AttributeSignature? syntax)
    {
        if (syntax is null)
            return;

        symbol.Attributes = ResolveAttributes(syntax.Attributes, symbol.EnclosingScope);

        foreach (var handle in symbol.Parameters)
        {
            if (context.GetParameterSymbol(handle) is { } parameter)
                ResolveParameter(parameter);
        }
    }

    private void ResolveAttributeDeclaration(NestedAttributeSymbol symbol, AttributeSignature? syntax)
    {
        if (syntax is null)
            return;

        symbol.Attributes = ResolveAttributes(syntax.Attributes, symbol.EnclosingScope);

        foreach (var handle in symbol.Parameters)
        {
            if (context.GetParameterSymbol(handle) is { } parameter)
                ResolveParameter(parameter);
        }
    }

    private void ResolveType(TypeSymbol symbol, TypeDeclaration? syntax)
    {
        if (syntax is null)
            return;

        var scope = GetOwnedScope(symbol);
        symbol.Attributes = ResolveAttributes(syntax.Attributes, symbol.EnclosingScope);
        symbol.BaseTypes = ResolveTypes(syntax.Base?.BaseTypes ?? new SeparatedSyntaxList<TypeSyntax>([]), scope);

        ResolveTypeConstraints(syntax.Constraints, symbol.GenericParameters, scope);
        ResolveGenericParameterDeclarations(syntax.Name, symbol.GenericParameters);
        PopulateProductMembers(symbol);
    }

    private void ResolveType(NestedTypeSymbol symbol, TypeDeclaration? syntax)
    {
        if (syntax is null)
            return;

        var scope = GetOwnedScope(symbol);
        symbol.Attributes = ResolveAttributes(syntax.Attributes, symbol.EnclosingScope);
        symbol.BaseTypes = ResolveTypes(syntax.Base?.BaseTypes ?? new SeparatedSyntaxList<TypeSyntax>([]), scope);

        ResolveTypeConstraints(syntax.Constraints, symbol.GenericParameters, scope);
        ResolveGenericParameterDeclarations(syntax.Name, symbol.GenericParameters);
        PopulateProductMembers(symbol);
    }

    private void ResolveAlias(AliasSymbol symbol)
    {
        if (symbol.Syntax is null)
            return;

        var scope = GetOwnedScope(symbol);

        if (scope.Parent == context.GlobalScope && scope.UsingNamespaces.Count == 0)
        {
            foreach (var childNs in context.GlobalNamespace.Next.Values)
                scope.UsingNamespaces.Add(childNs);
        }

        ResolveTypeConstraints(symbol.Syntax.Constraints, symbol.GenericParameters, scope);
        ResolveGenericParameterDeclarations(symbol.Syntax.Name, symbol.GenericParameters);
        var target = ResolveType(symbol.Syntax.Target, scope, reportDiagnostics: !symbol.IsGlobalAlias);

        symbol.HasCompatibleConstraints = target.IsResolved &&
            (target.IsSpecial || (target.Handle is { } handle && AliasConstraintsAreCompatible(handle, symbol.Syntax.Target, scope)));
        symbol.Target = symbol.HasCompatibleConstraints ? target : (symbol.IsGlobalAlias && !target.IsResolved ? TypeRef.Unresolved : TypeRef.Error);
    }

    private bool AliasConstraintsAreCompatible(SymbolHandle target, TypeSyntax targetSyntax, Scope scope)
    {
        var targetParameters = GetGenericParameters(target);
        var targetArguments = GetGenericArguments(targetSyntax);

        if (targetParameters.Count == 0)
            return true;
        if (targetArguments.Count != targetParameters.Count)
            return false;

        for (int index = 0; index < targetParameters.Count; index++)
        {
            if (targetParameters[index].Kind is not SymbolKind.GenericParameter)
                return false;

            GenericParameterSymbol parameter = context.GenericParameterSymbols[targetParameters[index].ID];

            if (parameter.ParameterKind is not GenericParameterKind.Type || targetArguments[index] is LiteralGenericArgument)
                continue;

            SymbolHandle? argument = ResolveGenericArgumentAsType(targetArguments[index], scope);

            if (argument is null || !SatisfiesAllConstraints(argument.Value, parameter.Constraints))
                return false;
        }

        return true;
    }

    private IReadOnlyList<SymbolHandle> GetGenericParameters(SymbolHandle handle) => handle.Kind switch
    {
        SymbolKind.Type => context.GetTypeSymbol(handle)?.GenericParameters ?? [],
        SymbolKind.NestedType => context.GetNestedTypeSymbol(handle)?.GenericParameters ?? [],
        SymbolKind.Alias => context.GetAliasSymbol(handle)?.GenericParameters ?? [],
        _ => []
    };

    private static IReadOnlyList<TypeSyntax> GetGenericArguments(TypeSyntax syntax)
    {
        switch (syntax)
        {
            case GenericType generic:
                var arguments = new List<TypeSyntax>(generic.GenericArguments.Count);

                foreach (var argument in generic.GenericArguments)
                    arguments.Add(argument);

                return arguments;
            case QualifiedType qualified:
                return GetGenericArguments(qualified.Right);
            case ModifiedType modified:
                return GetGenericArguments(modified.Type);
            default:
                return [];
        }
    }

    private bool SatisfiesAllConstraints(SymbolHandle candidate, IReadOnlyList<TypeRef> required)
    {
        foreach (var constraint in required)
        {
            if (constraint.IsError)
                continue;

            if (constraint.IsResolved && constraint.Handle is { } constraintHandle && !SatisfiesConstraint(candidate, constraintHandle, []))
                return false;
        }

        return true;
    }

    private bool SatisfiesConstraint(SymbolHandle candidate, SymbolHandle required, HashSet<SymbolHandle> visited)
    {
        if (candidate == required)
            return true;

        if (!visited.Add(candidate))
            return false;

        switch (candidate.Kind)
        {
            case SymbolKind.GenericParameter:
                if (context.GetGenericParameterSymbol(candidate) is { } gp)
                {
                    foreach (var constraint in gp.Constraints)
                        if (constraint.IsResolved && constraint.Handle is { } constraintHandle && SatisfiesConstraint(constraintHandle, required, visited))
                            return true;
                }
                break;
            case SymbolKind.Type:
                if (context.GetTypeSymbol(candidate) is { } typeSym)
                {
                    foreach (var baseType in typeSym.BaseTypes)
                        if (baseType.IsResolved && baseType.Handle is { } baseHandle && SatisfiesConstraint(baseHandle, required, visited))
                            return true;
                }
                break;
            case SymbolKind.NestedType:
                if (context.GetNestedTypeSymbol(candidate) is { } nestedTypeSym)
                {
                    foreach (var baseType in nestedTypeSym.BaseTypes)
                        if (baseType.IsResolved && baseType.Handle is { } baseHandle && SatisfiesConstraint(baseHandle, required, visited))
                            return true;
                }
                break;
            case SymbolKind.Alias when context.GetAliasSymbol(candidate)?.Target is { IsResolved: true, Handle: { } target }:
                return SatisfiesConstraint(target, required, visited);
        }

        return false;
    }

    private void ResolveFunction(FunctionSymbol symbol)
    {
        if (symbol.Syntax is null)
            return;

        var scope = GetOwnedScope(symbol);
        symbol.Attributes = ResolveAttributes(symbol.Syntax.Attributes, symbol.EnclosingScope);
        symbol.ReturnType = ResolveType(symbol.Syntax.Signature.ReturnType, scope, allowInference: true);

        ResolveTypeConstraints(symbol.Syntax.Signature.Constraints, symbol.GenericParameters, scope);
        ResolveGenericParameterDeclarations(symbol.Syntax.Signature.Identifier, symbol.GenericParameters);
        ResolveBodyOnce(symbol.Syntax.Body, scope, ResolutionContext.GetHandle(symbol));
    }

    private void ResolveMethod(MethodSymbol symbol)
    {
        if (symbol.Syntax is null)
            return;

        var scope = GetOwnedScope(symbol);
        symbol.Attributes = ResolveAttributes(symbol.Syntax.Attributes, symbol.EnclosingScope);
        symbol.ReturnType = ResolveType(symbol.Syntax.Signature.ReturnType, scope, allowInference: true);

        ResolveTypeConstraints(symbol.Syntax.Signature.Constraints, symbol.GenericParameters, scope);
        ResolveGenericParameterDeclarations(symbol.Syntax.Signature.Identifier, symbol.GenericParameters);
        ResolveBodyOnce(symbol.Syntax.Body, scope, ResolutionContext.GetHandle(symbol));
    }

    private void ResolveParameter(ParameterSymbol symbol)
    {
        if (symbol.Syntax is null || !resolvedParameters.Add(symbol))
            return;

        symbol.Attributes = ResolveAttributes(symbol.Syntax.Attributes, symbol.EnclosingScope);

        symbol.Type = ResolveType(symbol.Syntax.Declarator.Type, symbol.EnclosingScope);

        ResolveExpression(symbol.Syntax.Initializer?.Initializer, symbol.EnclosingScope, symbol.ContainingSymbol ?? default);
    }

    private void ResolveVariable(GlobalVariableSymbol symbol, VariableDeclaration? syntax) =>
        ResolveVariableCore(symbol, syntax, value => symbol.Type = value, value => symbol.Attributes = value, allowInference: true);

    private void ResolveVariable(FieldSymbol symbol, VariableDeclaration? syntax) =>
        ResolveVariableCore(symbol, syntax, value => symbol.Type = value, value => symbol.Attributes = value, allowInference: false);

    private void ResolveVariable(LocalVariableSymbol symbol, VariableDeclaration? syntax) =>
        ResolveVariableCore(symbol, syntax, value => symbol.Type = value, value => symbol.Attributes = value, allowInference: true);

    private void ResolveVariableCore(Symbol symbol, VariableDeclaration? syntax, Action<TypeRef> setType, Action<List<SymbolHandle>> setAttributes, bool allowInference = false)
    {
        if (syntax is null)
            return;

        if (!resolvedVariableAttributes.TryGetValue((syntax, symbol.EnclosingScope), out var attributeHandles))
        {
            attributeHandles = ResolveAttributes(syntax.Attributes, symbol.EnclosingScope);
            resolvedVariableAttributes[(syntax, symbol.EnclosingScope)] = attributeHandles;
        }
        setAttributes(attributeHandles);

        TypeRef? resolvedType = null;
        bool isTupleDestructuring = syntax.Declarators.Any(d => d.Identifier is TupleName);
        if (isTupleDestructuring && syntax.Type is TupleType tupleType)
        {
            var matchedElement = tupleType.Elements.FirstOrDefault(e => e.Name != null && context.GetScopedSymbolName(new SimpleName(e.Name)).Last == symbol.Name);
            if (matchedElement != null)
                resolvedType = ResolveType(matchedElement.Type, symbol.EnclosingScope, allowInference: false);
        }

        setType(resolvedType ?? ResolveType(syntax.Type, symbol.EnclosingScope, allowInference: allowInference));

        foreach (var declarator in syntax.Declarators)
        {
            if (declarator.Identifier is TupleName tupleName)
            {
                if (tupleName.Elements.Any(e => context.GetScopedSymbolName(e).Last == symbol.Name))
                {
                    if (declarator.Initializer?.Initializer is { } initExpr && resolvedDeclarators.Add(declarator))
                        ResolveExpression(initExpr, symbol.EnclosingScope, GetContainingFunction(symbol));
                }
            }
            else if (context.GetScopedSymbolName(declarator.Identifier).Last == symbol.Name)
            {
                if (declarator.Initializer?.Initializer is { } initExpr && resolvedDeclarators.Add(declarator))
                    ResolveExpression(initExpr, symbol.EnclosingScope, GetContainingFunction(symbol));
            }
        }
    }

    private void ResolveProperty(PropertySymbol symbol)
    {
        if (symbol.Syntax is null)
            return;

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
            case LocalBlockStatement block:
                Scope blockScope = context.GetSyntaxScope(block, scope);
                foreach (var child in block.Locals)
                    ResolveLocal(child, blockScope, containingFunction);
                break;
            case LocalExpressionStatement statement:
                ResolveExpression(statement.Expression, scope, containingFunction);
                break;
            case LocalReturnStatement statement:
                ResolveExpression(statement.Statement.Expression, scope, containingFunction);
                break;
            case LocalIfStatement statement:
                Scope ifScope = statement.Declaration is not null ? context.GetSyntaxScope(statement, scope) : scope;
                if (statement.Declaration is not null)
                {
                    ResolveDeclarationSyntax(statement.Declaration, ifScope, containingFunction);
                }
                ResolveExpression(statement.Condition, ifScope, containingFunction);
                ResolveLocal(statement.ThenStatement, ifScope, containingFunction);
                if (statement.ElseStatement is not null)
                    ResolveLocal(statement.ElseStatement.Statement, ifScope, containingFunction);
                break;
            case LocalWhileStatement statement:
                Scope whileScope = statement.Declaration is not null ? context.GetSyntaxScope(statement, scope) : scope;
                if (statement.Declaration is not null)
                {
                    ResolveDeclarationSyntax(statement.Declaration, whileScope, containingFunction);
                }
                ResolveExpression(statement.Condition, whileScope, containingFunction);
                ResolveLocal(statement.Body, whileScope, containingFunction);
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
        ResolveType(syntax.Type, scope, allowInference: true);

        if (!resolvedVariableAttributes.TryGetValue((syntax, scope), out _))
        {
            foreach (var attribute in syntax.Attributes)
                foreach (var application in attribute.Attributes)
                    ResolveNamed(application.Name, scope, application);
        }

        foreach (var declarator in syntax.Declarators)
        {
            if (declarator.Initializer?.Initializer is { } initExpr && resolvedDeclarators.Add(declarator))
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

    private void ResolveTypeConstraints(IReadOnlyList<TypeConstraintClause> clauses, IReadOnlyList<SymbolHandle> parameters, Scope scope)
    {
        foreach (var (Kind, ID) in parameters)
            if (Kind is SymbolKind.GenericParameter)
                context.GenericParameterSymbols[ID].Constraints = [];

        foreach (var clause in clauses)
        {
            var parameter = FindGenericParameter(parameters, context.GetScopedSymbolName(clause.GenericParameter).Last);
            if (parameter is null)
                continue;

            context.ResolvedTree.AddReference(clause.GenericParameter, ResolutionContext.GetHandle(parameter));

            foreach (var constraint in clause.Constraints)
                if (constraint is TypeTypeConstraint typeConstraint)
                    parameter.Constraints.Add(ResolveType(typeConstraint.Type, scope));
        }
    }

    private GenericParameterSymbol? FindGenericParameter(IReadOnlyList<SymbolHandle> handles, SymbolPart name)
    {
        foreach (var (Kind, ID) in handles)
            if (Kind is SymbolKind.GenericParameter && context.GenericParameterSymbols[ID].Name == name)
                return context.GenericParameterSymbols[ID];

        return null;
    }

    private void ResolveGenericParameterDeclarations(NamedSyntax name, IReadOnlyList<SymbolHandle> parameters)
    {
        var genericName = name switch
        {
            GenericName generic => generic,
            QualifiedName qualified when qualified.Parts.Count > 0 => qualified.Parts[^1] as GenericName,
            _ => null
        };

        if (genericName is null)
            return;

        for (int index = 0; index < genericName.GenericParameters.Count && index < parameters.Count; index++)
            context.ResolvedTree.AddReference(genericName.GenericParameters[index], parameters[index]);
    }

    private List<SymbolHandle> ResolveAttributes(IReadOnlyList<AttributeListSyntax> attributes, Scope scope)
    {
        var result = new List<SymbolHandle>();

        foreach (var list in attributes)
            foreach (var attribute in list.Attributes)
            {
                ResolveNamed(attribute.Name, scope, attribute);

                if (ResolveSingle(scope, context.GetScopedSymbolName(attribute.Name)) is { } symbol)
                    result.Add(ResolutionContext.GetHandle(symbol));

                foreach (var argument in attribute.Arguments)
                    ResolveExpression(argument, scope, default);
            }

        return result;
    }

    private List<TypeRef> ResolveTypes(SeparatedSyntaxList<TypeSyntax> types, Scope scope)
    {
        var result = new List<TypeRef>(types.Count);

        foreach (var type in types)
            result.Add(ResolveType(type, scope));

        return result;
    }

    private TypeRef ResolveType(TypeSyntax syntax, Scope scope, bool reportDiagnostics = true, bool allowInference = false)
    {
        if (resolvedTypes.TryGetValue((syntax, scope, allowInference), out var cached))
            return cached;

        var result = ResolveTypeCore(syntax, scope, reportDiagnostics, allowInference);
        if (reportDiagnostics || result.IsResolved)
            resolvedTypes[(syntax, scope, allowInference)] = result;

        return result;
    }

    private TypeRef ResolveTypeCore(TypeSyntax syntax, Scope scope, bool reportDiagnostics = true, bool allowInference = false)
    {
        if (syntax is SimpleType simple && (simple.Name.MatchingKind == MatchingKeywordKind.Var || simple.Name.Value == "var"))
        {
            if (allowInference)
                return TypeRef.Inferred;

            return ResolveTypeName(syntax, scope, reportDiagnostics);
        }

        if (syntax is TupleType tuple)
        {
            var elements = new List<TupleElement>(tuple.Elements.Count);
            foreach (var elem in tuple.Elements)
            {
                var elemType = ResolveType(elem.Type, scope, reportDiagnostics);
                elements.Add(new TupleElement(elemType, elem.Name?.Value));
            }
            return TypeRef.Tuple(elements);
        }

        if (syntax is UniformTupleType uniform)
        {
            var elemType = ResolveType(uniform.ElementType, scope, reportDiagnostics);
            var elements = new List<TupleElement>(uniform.Elements.Count);
            foreach (var elem in uniform.Elements)
                elements.Add(new TupleElement(elemType, elem.Name.Value));
            return TypeRef.Tuple(elements);
        }

        if (syntax is ModifiedType modified)
        {
            var innerType = ResolveType(modified.Type, scope, reportDiagnostics);
            return modified.Modifier switch
            {
                PointerTypeModifier => TypeRef.Pointer(innerType),
                ReferenceTypeModifier => TypeRef.Reference(innerType),
                OptionalTypeModifier => TypeRef.Optional(innerType),
                ArrayTypeModifier array => ResolveArrayOrSpanModifier(array, innerType, scope),
                _ => TypeRef.Error
            };
        }

        if (syntax is GenericType generic)
        {
            TypeRef genericTarget = ResolveTypeName(generic, scope, reportDiagnostics);
            ResolveGenericArguments(generic, genericTarget.Handle, scope);

            return genericTarget;
        }

        if (syntax is QualifiedType qualified)
        {
            if (qualified.Right is GenericType qualifiedGeneric)
            {
                TypeRef target = ResolveTypeName(syntax, scope, reportDiagnostics);

                if (target.Handle is { } handle)
                    context.ResolvedTree.AddReference(qualifiedGeneric, handle);

                ResolveGenericArguments(qualifiedGeneric, target.Handle, scope);
                return target;
            }

            TypeRef result = ResolveTypeName(syntax, scope, reportDiagnostics);

            if (result.Handle is { } rightHandle)
                context.ResolvedTree.AddReference(qualified.Right, rightHandle);

            return result;
        }

        ResolveTypeChildren(syntax, scope);

        return ResolveTypeName(syntax, scope, reportDiagnostics);
    }

    private TypeRef ResolveTypeName(TypeSyntax syntax, Scope scope, bool reportDiagnostics = true)
    {
        var name = ResolutionContext.GetSymbolName(syntax);
        var symbols = scope[name];
        if (symbols.Count > 1 && !AreSamePartialType(symbols))
        {
            if (reportDiagnostics)
            {
                var span = syntax.GetSpan() ?? default;
                var source = syntax.GetSource();
                context.Diagnostics.ReportAmbiguousTypeReference(span, name.ToDisplayString(), source, syntax.ExpansionOrigin);
            }

            return TypeRef.Error;
        }

        if (ResolveSingle(scope, name) is not { } symbol)
        {
            if (reportDiagnostics)
            {
                var span = syntax.GetSpan() ?? default;
                var source = syntax.GetSource();
                string? arityNote = GetArityMismatchNote(scope, name);
                context.Diagnostics.ReportUnresolvedTypeReference(span, name.ToDisplayString(), source, syntax.ExpansionOrigin, arityNote);
            }

            return TypeRef.Error;
        }

        if (symbol is AliasSymbol aliasSymbol && aliasSymbol.IsGlobalAlias && !aliasSymbol.Target.IsResolved)
        {
            if (reportDiagnostics)
            {
                var span = syntax.GetSpan() ?? default;
                var source = syntax.GetSource();
                string? arityNote = GetArityMismatchNote(scope, name);
                context.Diagnostics.ReportUnresolvedTypeReference(span, name.ToDisplayString(), source, syntax.ExpansionOrigin, arityNote);
            }

            return TypeRef.Error;
        }

        var handle = ResolutionContext.GetHandle(symbol);
        context.ResolvedTree.AddReference(syntax, handle);

        return TypeRef.Resolved(handle);
    }

    private static string? GetArityMismatchNote(Scope scope, SymbolName name)
    {
        var targetPart = name.Last;
        if (targetPart.Arity > 0)
        {
            var nonGenericPart = new SymbolPart(targetPart.Text, 0);
            var nonGenericName = name.Count == 1
                ? new SymbolName(nonGenericPart)
                : new SymbolName([.. name.Parts.Take(name.Count - 1), nonGenericPart]);

            if (ResolveSingle(scope, nonGenericName) is not null)
            {
                return $"non-generic type '{nonGenericName}' exists in this scope, but generic type '{name}' with {targetPart.Arity} type argument{(targetPart.Arity == 1 ? "" : "s")} was not found";
            }
        }
        else
        {
            for (int a = 1; a <= 8; a++)
            {
                var genericPart = new SymbolPart(targetPart.Text, a);
                var genericName = name.Count == 1
                    ? new SymbolName(genericPart)
                    : new SymbolName([.. name.Parts.Take(name.Count - 1), genericPart]);

                if (ResolveSingle(scope, genericName) is not null)
                {
                    return $"generic type '{genericName}' exists in this scope with {a} type argument{(a == 1 ? "" : "s")}, but non-generic type '{name}' was not found";
                }
            }
        }

        return null;
    }

    private void ResolveGenericArguments(GenericType generic, SymbolHandle? target, Scope scope)
    {
        var parameters = target is { } handle ? GetGenericParameters(handle) : [];

        for (int index = 0; index < generic.GenericArguments.Count; index++)
        {
            var argument = generic.GenericArguments[index];
            var parameter = index < parameters.Count && parameters[index].Kind is SymbolKind.GenericParameter
                ? context.GenericParameterSymbols[parameters[index].ID]
                : null;

            switch (argument)
            {
                case LiteralGenericArgument:
                    break;
                case NamedExpressionGenericArgument named when parameter?.ParameterKind is GenericParameterKind.Type:
                    ResolveNamedArgumentAsType(named.Expression, scope);
                    break;
                case NamedExpressionGenericArgument named:
                    ResolveExpression(named.Expression, scope, default);
                    break;
                default:
                    ResolveType(argument, scope);
                    break;
            }
        }
    }

    private SymbolHandle? ResolveGenericArgumentAsType(TypeSyntax argument, Scope scope) => argument switch
    {
        NamedExpressionGenericArgument named => ResolveNamedArgumentAsType(named.Expression, scope),
        _ => ResolveType(argument, scope).Handle
    };

    private SymbolHandle? ResolveNamedArgumentAsType(IdentifierNameExpression expression, Scope scope)
    {
        if (ResolveSingle(scope, new SymbolName(new SymbolPart(expression.Identifier))) is not { } symbol)
            return null;

        var handle = ResolutionContext.GetHandle(symbol);
        context.ResolvedTree.AddReference(expression, handle);

        return handle;
    }

    private TypeRef ResolveArrayOrSpanModifier(ArrayTypeModifier array, TypeRef innerType, Scope scope)
    {
        if (array.Size is { } size)
        {
            ResolveExpression(size, scope, default);
            return TypeRef.Array(innerType, size);
        }

        return TypeRef.Span(innerType);
    }

    private void ResolveTypeChildren(TypeSyntax syntax, Scope scope)
    {
        switch (syntax)
        {
            case GenericType:
                break;
            case QualifiedType qualified:
                ResolveType(qualified.Left, scope);
                ResolveType(qualified.Right, scope);
                break;
        }
    }

    private void ResolveExpression(Expression? expression, Scope scope, SymbolHandle containingSymbol)
    {
        if (expression is null)
            return;

        switch (expression)
        {
            case NamedExpression named:
                ResolveNamedExpression(named, scope);
                break;
            case BinaryExpression binary:
                ResolveExpression(binary.LeftExpression, scope, containingSymbol);
                ResolveExpression(binary.RightExpression, scope, containingSymbol);
                break;
            case AssignmentExpression assignment:
                ResolveExpression(assignment.LhsExpression, scope, containingSymbol);
                ResolveExpression(assignment.RhsExpression, scope, containingSymbol);
                break;
            case UnaryExpression unary:
                ResolveExpression(unary.Operand, scope, containingSymbol);
                break;
            case ParenthesizedExpression parenthesized:
                ResolveExpression(parenthesized.Expression, scope, containingSymbol);
                break;
            case AsExpression asExpr:
                ResolveType(asExpr.Type, scope);
                ResolveExpression(asExpr.Expression, scope, containingSymbol);
                break;
            case TupleExpression tuple:
                foreach (var argument in tuple.Arguments)
                    ResolveExpression(argument, scope, containingSymbol);
                break;
            case CallExpression call:
                ResolveExpression(call.Callee, scope, containingSymbol);
                foreach (var argument in call.Arguments) ResolveExpression(argument, scope, containingSymbol);
                break;
            case IndexExpression index:
                ResolveExpression(index.Expression, scope, containingSymbol);
                ResolveExpression(index.Index, scope, containingSymbol);
                break;
            case MemberAccessExpression access:
                ResolveExpression(access.Expression, scope, containingSymbol);
                break;
            case NameofExpression nameofExpr:
                ResolveExpression(nameofExpr.Argument, scope, containingSymbol);
                break;
            case NamedArgumentExpression argument:
                ResolveExpression(argument.Value, scope, containingSymbol);
                break;
            case IfExpression conditional:
                ResolveExpression(conditional.Condition, scope, containingSymbol);
                ResolveExpression(conditional.ThenExpression, scope, containingSymbol);
                ResolveExpression(conditional.ElseExpression, scope, containingSymbol);
                break;
            case ElseExpression @else:
                ResolveExpression(@else.Expression, scope, containingSymbol);
                break;
            case BlockExpression block:
                Scope blockScope = context.GetSyntaxScope(block, scope);

                foreach (var local in block.Locals)
                    ResolveLocal(local, blockScope, containingSymbol);

                ResolveExpression(block.FinalExpression, blockScope, containingSymbol);
                break;
            case CollectionExpression collection:
                foreach (var item in collection.Expressions)
                    ResolveExpression(item, scope, containingSymbol);

                foreach (var modifier in collection.Modifiers)
                    if (modifier is CollectionConstructorModifier constructor)
                        foreach (var argument in constructor.Arguments)
                            ResolveExpression(argument, scope, containingSymbol);
                break;
            case ConstructorCallExpression constructor:
                ResolveType(constructor.Type, scope);

                foreach (var argument in constructor.Arguments)
                    ResolveExpression(argument, scope, containingSymbol);

                ResolveInitializer(constructor.WithClause?.Initializer, scope, containingSymbol);
                break;
            case ArrayCreationExpression array:
                ResolveType(array.Type, scope);
                ResolveExpression(array.Size, scope, containingSymbol);
                ResolveInitializer(array.Initializer, scope, containingSymbol);
                ResolveInitializer(array.WithClause?.Initializer, scope, containingSymbol);
                break;
        }
    }

    private void ResolveInitializer(TypeInitializer? initializer, Scope scope, SymbolHandle containingFunction)
    {
        if (initializer is not null)
            foreach (var expression in initializer.Expressions)
                ResolveExpression(expression, scope, containingFunction);
    }

    private void ResolveNamedExpression(NamedExpression syntax, Scope scope)
    {
        var hygiene = context.GetHygiene(syntax);
        var name = syntax is GenericNameExpression generic
            ? new SymbolPart(generic.Identifier, generic.GenericArguments.Count, hygiene)
            : new SymbolPart(syntax.Identifier, 0, hygiene);

        if (ResolveSingle(scope, new SymbolName(name)) is { } symbol)
            context.ResolvedTree.AddReference(syntax, ResolutionContext.GetHandle(symbol));

        if (syntax is GenericNameExpression genericName)
            foreach (var argument in genericName.GenericArguments)
                ResolveType(argument, scope);
    }

    private void ResolveNamed(NamedSyntax syntax, Scope scope, SyntaxNode reference)
    {
        if (ResolveSingle(scope, context.GetScopedSymbolName(syntax)) is { } symbol)
            context.ResolvedTree.AddReference(reference, ResolutionContext.GetHandle(symbol));
    }

    private static Symbol? ResolveSingle(Scope scope, SymbolName name)
    {
        var symbols = scope[name];

        if (symbols.Count is 1)
            return symbols[0];

        if (symbols.Count > 1 && AreSamePartialType(symbols))
            return symbols[0];

        if (name.IsQualified)
        {
            var unqualified = scope[name.Last];
            if (unqualified.Count is 1)
                return unqualified[0];
            if (unqualified.Count > 1 && AreSamePartialType(unqualified))
                return unqualified[0];
        }

        return null;
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

    private static bool AreSamePartialType(IReadOnlyList<Symbol> symbols)
    {
        if (symbols.Count <= 1)
            return false;

        var first = symbols[0];

        if (first is TypeSymbol firstType)
        {
            if (!firstType.Flags.HasFlag(TypeFlags.Partial))
                return false;

            for (int i = 1; i < symbols.Count; i++)
                if (symbols[i] is not TypeSymbol other || other.Name != firstType.Name || other.ContainingNamespace != firstType.ContainingNamespace || !other.Flags.HasFlag(TypeFlags.Partial))
                    return false;

            return true;
        }

        if (first is NestedTypeSymbol firstNested)
        {
            if (!firstNested.Flags.HasFlag(TypeFlags.Partial))
                return false;

            for (int i = 1; i < symbols.Count; i++)
                if (symbols[i] is not NestedTypeSymbol other || other.Name != firstNested.Name || other.Parent != firstNested.Parent || !other.Flags.HasFlag(TypeFlags.Partial))
                    return false;

            return true;
        }

        return false;
    }

    private void CheckDuplicateTypeDeclarations()
    {
        // 1. Top-level types
        var topGroups = new Dictionary<(NamespaceTrieNode?, SymbolPart), List<TypeSymbol>>();

        foreach (var type in context.TypeSymbols)
        {
            if (type.Name.Text == "_")
                continue;

            var key = (type.ContainingNamespace, type.Name);

            if (!topGroups.TryGetValue(key, out var list))
            {
                list = [];
                topGroups[key] = list;
            }

            list.Add(type);
        }

        foreach (var (_, group) in topGroups)
        {
            if (group.Count <= 1)
                continue;

            CheckTypeGroup(group);
        }

        // 2. Nested and local types
        var nestedGroups = new Dictionary<(Scope, SymbolPart), List<NestedTypeSymbol>>();
        foreach (var type in context.NestedTypeSymbols)
        {
            if (type.Name.Text == "_")
                continue;

            var key = (type.EnclosingScope, type.Name);
            if (!nestedGroups.TryGetValue(key, out var list))
            {
                list = [];
                nestedGroups[key] = list;
            }
            list.Add(type);
        }

        foreach (var (_, group) in nestedGroups)
        {
            if (group.Count <= 1)
                continue;

            CheckNestedTypeGroup(group);
        }

        // 3. Types vs Aliases in same namespace
        foreach (var alias in context.AliasSymbols)
        {
            if (alias.ContainingNamespace != null)
            {
                var matchingTypes = alias.ContainingNamespace.GetSymbols(alias.Name);

                foreach (var sym in matchingTypes)
                {
                    if (sym is TypeSymbol typeSym)
                    {
                        var span = alias.Syntax?.Name.GetSpan() ?? alias.Syntax?.GetSpan() ?? default;
                        var firstSpan = typeSym.Syntax?.Name.GetSpan() ?? typeSym.Syntax?.GetSpan() ?? default;
                        context.Diagnostics.ReportDuplicateAliasDeclaration(
                            alias.Name.ToString(),
                            span,
                            firstSpan,
                            alias.Syntax?.GetSource(),
                            typeSym.Syntax?.GetSource());
                    }
                }
            }
        }
    }

    private void CheckTypeGroup(List<TypeSymbol> group)
    {
        bool allPartial = group.All(static t => t.Flags.HasFlag(TypeFlags.Partial));

        if (allPartial)
        {
            var first = group[0];

            for (int i = 1; i < group.Count; i++)
            {
                var cur = group[i];
                if (cur.TypeKind != first.TypeKind)
                {
                    var span = cur.Syntax?.Name.GetSpan() ?? cur.Syntax?.GetSpan() ?? default;
                    var firstSpan = first.Syntax?.Name.GetSpan() ?? first.Syntax?.GetSpan() ?? default;
                    context.Diagnostics.ReportConflictingPartialTypeKinds(
                        cur.Name.ToString(),
                        first.TypeKind.ToString().ToLowerInvariant(),
                        cur.TypeKind.ToString().ToLowerInvariant(),
                        span,
                        firstSpan,
                        cur.Syntax?.GetSource(),
                        first.Syntax?.GetSource());
                }
            }

            return;
        }

        var firstDecl = group[0];
        var firstDeclSpan = firstDecl.Syntax?.Name.GetSpan() ?? firstDecl.Syntax?.GetSpan() ?? default;

        for (int i = 1; i < group.Count; i++)
        {
            var redecl = group[i];
            var redeclSpan = redecl.Syntax?.Name.GetSpan() ?? redecl.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportDuplicateTypeDeclaration(
                redecl.Name.ToString(),
                redeclSpan,
                firstDeclSpan,
                redecl.Syntax?.GetSource(),
                firstDecl.Syntax?.GetSource());
        }
    }

    private void CheckNestedTypeGroup(List<NestedTypeSymbol> group)
    {
        var allPartial = group.All(static t => t.Flags.HasFlag(TypeFlags.Partial));

        if (allPartial)
        {
            var first = group[0];
            for (int i = 1; i < group.Count; i++)
            {
                var cur = group[i];
                if (cur.TypeKind != first.TypeKind)
                {
                    var span = cur.Syntax?.Name.GetSpan() ?? cur.Syntax?.GetSpan() ?? default;
                    var firstSpan = first.Syntax?.Name.GetSpan() ?? first.Syntax?.GetSpan() ?? default;
                    context.Diagnostics.ReportConflictingPartialTypeKinds(
                        cur.Name.ToString(),
                        first.TypeKind.ToString().ToLowerInvariant(),
                        cur.TypeKind.ToString().ToLowerInvariant(),
                        span,
                        firstSpan,
                        cur.Syntax?.GetSource(),
                        first.Syntax?.GetSource());
                }
            }

            return;
        }

        var firstDecl = group[0];
        var firstDeclSpan = firstDecl.Syntax?.Name.GetSpan() ?? firstDecl.Syntax?.GetSpan() ?? default;

        for (int i = 1; i < group.Count; i++)
        {
            var redecl = group[i];
            var redeclSpan = redecl.Syntax?.Name.GetSpan() ?? redecl.Syntax?.GetSpan() ?? default;
            context.Diagnostics.ReportDuplicateTypeDeclaration(
                redecl.Name.ToString(),
                redeclSpan,
                firstDeclSpan,
                redecl.Syntax?.GetSource(),
                firstDecl.Syntax?.GetSource());
        }
    }

    private void CheckCyclicTypeHierarchies()
    {
        var reported = new HashSet<SymbolHandle>();

        foreach (var type in context.TypeSymbols)
        {
            var handle = ResolutionContext.GetHandle(type);

            if (!reported.Contains(handle) && HasInheritanceCycle(handle, [handle]))
            {
                reported.Add(handle);
                var span = type.Syntax?.Name.GetSpan() ?? type.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportCyclicTypeHierarchy(span, type.Name.ToString(), type.Syntax?.GetSource());
            }
        }

        foreach (var type in context.NestedTypeSymbols)
        {
            var handle = ResolutionContext.GetHandle(type);

            if (!reported.Contains(handle) && HasInheritanceCycle(handle, [handle]))
            {
                reported.Add(handle);
                var span = type.Syntax?.Name.GetSpan() ?? type.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportCyclicTypeHierarchy(span, type.Name.ToString(), type.Syntax?.GetSource());
            }
        }
    }

    private bool HasInheritanceCycle(SymbolHandle current, HashSet<SymbolHandle> path)
    {
        IReadOnlyList<TypeRef> baseTypes;

        if (current.Kind == SymbolKind.Type && context.GetTypeSymbol(current) is { } typeSym)
            baseTypes = typeSym.BaseTypes;
        else if (current.Kind == SymbolKind.NestedType && context.GetNestedTypeSymbol(current) is { } nestedTypeSym)
            baseTypes = nestedTypeSym.BaseTypes;
        else
            return false;

        foreach (var baseType in baseTypes)
        {
            var underlying = context.GetType(baseType);

            if (underlying is not { } baseHandle)
                continue;

            if (path.Contains(baseHandle))
                return true;

            path.Add(baseHandle);

            if (HasInheritanceCycle(baseHandle, path))
                return true;

            path.Remove(baseHandle);
        }

        return false;
    }

    private void CheckDuplicateAndPartialFunctions()
    {
        // 1. Top-level functions
        var topGroups = new Dictionary<(NamespaceTrieNode?, SymbolPart), List<FunctionSignatureInfo>>();

        foreach (var fn in context.FunctionSymbols)
        {
            if (fn.Name.Text == "_")
                continue;

            var key = (fn.ContainingNamespace, fn.Name);

            if (!topGroups.TryGetValue(key, out var list))
            {
                list = [];
                topGroups[key] = list;
            }

            list.Add(new FunctionSignatureInfo(fn, fn.Flags, fn.Syntax, fn.GenericParameters, fn.Parameters, fn.ReturnType));
        }

        foreach (var (_, group) in topGroups)
            ValidateFunctionSignatureGroup(group);

        // 2. Member & Local methods
        var methodGroups = new Dictionary<(Scope, SymbolPart), List<FunctionSignatureInfo>>();

        foreach (var m in context.MethodSymbols)
        {
            if (m.Name.Text == "_")
                continue;

            var key = (m.EnclosingScope, m.Name);
            if (!methodGroups.TryGetValue(key, out var list))
            {
                list = [];
                methodGroups[key] = list;
            }

            list.Add(new FunctionSignatureInfo(m, m.Flags, m.Syntax, m.GenericParameters, m.Parameters, m.ReturnType));
        }

        foreach (var (_, group) in methodGroups)
            ValidateFunctionSignatureGroup(group);
    }

    private void ValidateFunctionSignatureGroup(List<FunctionSignatureInfo> group)
    {
        if (group.Count <= 1)
            return;

        // Partition into sub-groups by parameter signature
        var subGroups = new List<List<FunctionSignatureInfo>>();

        foreach (var fn in group)
        {
            bool added = false;

            foreach (var sub in subGroups)
            {
                if (SignaturesMatch(sub[0].GenericParameters, sub[0].Parameters, fn.GenericParameters, fn.Parameters))
                {
                    sub.Add(fn);
                    added = true;
                    break;
                }
            }

            if (!added)
                subGroups.Add([fn]);
        }

        foreach (var subGroup in subGroups)
        {
            if (subGroup.Count <= 1)
                continue;

            bool allPartial = subGroup.All(static f => f.Flags.HasFlag(FunctionFlags.Partial));

            if (allPartial)
            {
                // Partial function: unlimited without body allowed. At most one with body.
                var withBodies = new List<FunctionSignatureInfo>();
                foreach (var fn in subGroup)
                    if (fn.Syntax?.Body is not null and not FunctionEmptyBody)
                        withBodies.Add(fn);

                if (withBodies.Count > 1)
                {
                    var firstBody = withBodies[0];
                    var firstSpan = firstBody.Syntax?.Body.GetSpan() ?? firstBody.Syntax?.GetSpan() ?? default;

                    for (int i = 1; i < withBodies.Count; i++)
                    {
                        var redecl = withBodies[i];
                        var redeclSpan = redecl.Syntax?.Body.GetSpan() ?? redecl.Syntax?.GetSpan() ?? default;
                        var fnName = redecl.Symbol.Name.ToString();
                        context.Diagnostics.ReportMultiplePartialFunctionBodies(
                            fnName,
                            redeclSpan,
                            firstSpan,
                            redecl.Syntax?.GetSource(),
                            firstBody.Syntax?.GetSource());
                    }
                }

                // Check return type consistency
                var firstReturn = subGroup[0].ReturnType;
                var firstSyntax = subGroup[0].Syntax;

                for (int i = 1; i < subGroup.Count; i++)
                {
                    var cur = subGroup[i];
                    if (firstReturn != cur.ReturnType && (firstReturn.IsResolved || cur.ReturnType.IsResolved))
                    {
                        var span = cur.Syntax?.Signature.ReturnType.GetSpan() ?? cur.Syntax?.GetSpan() ?? default;
                        var firstSpan = firstSyntax?.Signature.ReturnType.GetSpan() ?? firstSyntax?.GetSpan() ?? default;
                        var fnName = cur.Symbol.Name.ToString();
                        context.Diagnostics.ReportConflictingPartialFunctionReturnTypes(
                            fnName,
                            span,
                            firstSpan,
                            cur.Syntax?.GetSource(),
                            firstSyntax?.GetSource());
                    }
                }
            }
            else
            {
                // Not all partial -> duplicate function declaration error
                var first = subGroup[0];
                var firstSpan = first.Syntax?.Signature.Identifier.GetSpan() ?? first.Syntax?.GetSpan() ?? default;

                for (int i = 1; i < subGroup.Count; i++)
                {
                    var redecl = subGroup[i];
                    var redeclSpan = redecl.Syntax?.Signature.Identifier.GetSpan() ?? redecl.Syntax?.GetSpan() ?? default;
                    var fnName = redecl.Symbol.Name.ToString();
                    context.Diagnostics.ReportDuplicateFunctionDeclaration(
                        fnName,
                        redeclSpan,
                        firstSpan,
                        redecl.Syntax?.GetSource(),
                        first.Syntax?.GetSource());
                }
            }
        }
    }

    private bool SignaturesMatch(IReadOnlyList<SymbolHandle> genericParamsA, List<SymbolHandle> paramsA, IReadOnlyList<SymbolHandle> genericParamsB, List<SymbolHandle> paramsB)
    {
        if (genericParamsA.Count != genericParamsB.Count)
            return false;

        if (paramsA.Count != paramsB.Count)
            return false;

        for (int i = 0; i < paramsA.Count; i++)
        {
            var paramA = context.ParameterSymbols[paramsA[i].ID];
            var paramB = context.ParameterSymbols[paramsB[i].ID];

            if (!ParameterTypesMatch(paramA.Type, genericParamsA, paramB.Type, genericParamsB, paramA.Syntax, paramB.Syntax))
                return false;
        }

        return true;
    }

    private bool ParameterTypesMatch(TypeRef typeA, IReadOnlyList<SymbolHandle> genericParamsA, TypeRef typeB, IReadOnlyList<SymbolHandle> genericParamsB, Parameter? syntaxA, Parameter? syntaxB)
    {
        var unwrappedA = context.UnwrapAlias(typeA);
        var unwrappedB = context.UnwrapAlias(typeB);

        int genIndexA = -1;

        if (unwrappedA.IsResolved && unwrappedA.Handle is { } hA)
        {
            for (int k = 0; k < genericParamsA.Count; k++)
                if (genericParamsA[k] == hA)
                {
                    genIndexA = k;
                    break;
                }
        }

        int genIndexB = -1;

        if (unwrappedB.IsResolved && unwrappedB.Handle is { } hB)
        {
            for (int k = 0; k < genericParamsB.Count; k++)
                if (genericParamsB[k] == hB)
                {
                    genIndexB = k;
                    break;
                }
        }

        if (genIndexA >= 0 || genIndexB >= 0)
            return genIndexA == genIndexB;

        if (unwrappedA.IsSpecial || unwrappedB.IsSpecial)
        {
            if (unwrappedA.Kind != unwrappedB.Kind)
                return false;

            return (unwrappedA.SpecialType, unwrappedB.SpecialType) switch
            {
                (PointerSemanticType pA, PointerSemanticType pB) =>
                    ParameterTypesMatch(pA.ElementType, genericParamsA, pB.ElementType, genericParamsB, null, null),
                (ReferenceSemanticType rA, ReferenceSemanticType rB) =>
                    ParameterTypesMatch(rA.ElementType, genericParamsA, rB.ElementType, genericParamsB, null, null),
                (SpanSemanticType sA, SpanSemanticType sB) =>
                    ParameterTypesMatch(sA.ElementType, genericParamsA, sB.ElementType, genericParamsB, null, null),
                (ArraySemanticType aA, ArraySemanticType aB) =>
                    ParameterTypesMatch(aA.ElementType, genericParamsA, aB.ElementType, genericParamsB, null, null) &&
                    (aA.Size == aB.Size || Nullable.Equals(aA.Size.GetSpan(), aB.Size.GetSpan())),
                (OptionalSemanticType oA, OptionalSemanticType oB) =>
                    ParameterTypesMatch(oA.ElementType, genericParamsA, oB.ElementType, genericParamsB, null, null),
                (TupleSemanticType tA, TupleSemanticType tB) when tA.Elements.Count == tB.Elements.Count =>
                    tA.Elements.Zip(tB.Elements).All(pair =>
                        ParameterTypesMatch(pair.First.Type, genericParamsA, pair.Second.Type, genericParamsB, null, null) &&
                        string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal)),
                _ => unwrappedA.Equals(unwrappedB)
            };
        }

        if (unwrappedA.IsResolved && unwrappedB.IsResolved)
        {
            var targetA = context.GetType(unwrappedA) ?? unwrappedA.Handle;
            var targetB = context.GetType(unwrappedB) ?? unwrappedB.Handle;

            return targetA == targetB;
        }

        if (syntaxA?.Declarator.Type is { } sTypeA && syntaxB?.Declarator.Type is { } sTypeB)
        {
            var textA = sTypeA.GetSpan() is { } spA && sTypeA.GetSource() is { } srcA ? srcA.ToString(spA) : null;
            var textB = sTypeB.GetSpan() is { } spB && sTypeB.GetSource() is { } srcB ? srcB.ToString(spB) : null;

            if (textA != null && textB != null)
                return string.Equals(textA, textB, StringComparison.Ordinal);
        }

        return typeA == typeB;
    }

    private void CheckDuplicateVariables()
    {
        // 1. Global variables
        var globalGroups = new Dictionary<(NamespaceTrieNode?, SymbolPart), List<GlobalVariableSymbol>>();

        foreach (var global in context.GlobalVariableSymbols)
        {
            if (global.Name.Text == "_")
                continue;

            var key = (global.ContainingNamespace, global.Name);

            if (!globalGroups.TryGetValue(key, out var list))
            {
                list = [];
                globalGroups[key] = list;
            }

            list.Add(global);
        }

        foreach (var (_, group) in globalGroups)
        {
            if (group.Count <= 1)
                continue;

            var first = group[0];
            var firstSpan = GetVariableIdentifierSpan(first.Syntax, first.Name);

            for (int i = 1; i < group.Count; i++)
            {
                var redecl = group[i];
                var redeclSpan = GetVariableIdentifierSpan(redecl.Syntax, redecl.Name);
                context.Diagnostics.ReportDuplicateVariableDeclaration(
                    redecl.Name.ToString(),
                    redeclSpan,
                    firstSpan,
                    redecl.Syntax?.GetSource(),
                    first.Syntax?.GetSource());
            }
        }

        // 2. Fields in product types
        var fieldGroups = new Dictionary<(SymbolHandle?, SymbolPart), List<FieldSymbol>>();

        foreach (var field in context.FieldSymbols)
        {
            if (field.Name.Text == "_")
                continue;

            var key = (field.Parent, field.Name);
            if (!fieldGroups.TryGetValue(key, out var list))
            {
                list = [];
                fieldGroups[key] = list;
            }

            list.Add(field);
        }

        foreach (var (_, group) in fieldGroups)
        {
            if (group.Count <= 1)
                continue;

            var first = group[0];
            var firstSpan = GetVariableIdentifierSpan(first.Syntax, first.Name);

            for (int i = 1; i < group.Count; i++)
            {
                var redecl = group[i];
                var redeclSpan = GetVariableIdentifierSpan(redecl.Syntax, redecl.Name);
                context.Diagnostics.ReportDuplicateVariableDeclaration(
                    redecl.Name.ToString(),
                    redeclSpan,
                    firstSpan,
                    redecl.Syntax?.GetSource(),
                    first.Syntax?.GetSource());
            }
        }
    }

    private TextSpan GetVariableIdentifierSpan(VariableDeclaration? syntax, SymbolPart name)
    {
        if (syntax != null)
        {
            foreach (var declarator in syntax.Declarators)
            {
                if (declarator.Identifier is TupleName tupleName)
                {
                    foreach (var elem in tupleName.Elements)
                    {
                        if (context.GetScopedSymbolName(elem).Last == name)
                            return elem.GetSpan() ?? declarator.GetSpan() ?? syntax.GetSpan() ?? default;
                    }
                }
                else if (context.GetScopedSymbolName(declarator.Identifier).Last == name)
                    return declarator.Identifier.GetSpan() ?? declarator.GetSpan() ?? syntax.GetSpan() ?? default;
            }

            return syntax.GetSpan() ?? default;
        }

        return default;
    }

    private void CheckDuplicateProperties()
    {
        var propGroups = new Dictionary<(Scope, SymbolPart), List<PropertySymbol>>();

        foreach (var prop in context.PropertySymbols)
        {
            if (prop.Name.Text == "_")
                continue;

            var key = (prop.EnclosingScope, prop.Name);
            if (!propGroups.TryGetValue(key, out var list))
            {
                list = [];
                propGroups[key] = list;
            }

            list.Add(prop);
        }

        foreach (var (_, group) in propGroups)
        {
            if (group.Count <= 1)
                continue;

            var first = group[0];
            var firstSpan = first.Syntax?.Identifier.GetSpan() ?? first.Syntax?.GetSpan() ?? default;

            for (int i = 1; i < group.Count; i++)
            {
                var redecl = group[i];
                var redeclSpan = redecl.Syntax?.Identifier.GetSpan() ?? redecl.Syntax?.GetSpan() ?? default;
                context.Diagnostics.ReportDuplicatePropertyDeclaration(
                    redecl.Name.ToString(),
                    redeclSpan,
                    firstSpan,
                    redecl.Syntax?.GetSource(),
                    first.Syntax?.GetSource());
            }
        }
    }
}