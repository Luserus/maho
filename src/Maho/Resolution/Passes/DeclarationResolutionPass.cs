using System;
using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>Resolves declaration-owned references after every declaration is known.</summary>
internal sealed class DeclarationResolutionPass : ResolutionPass
{
    private ResolutionContext context = null!;
    private readonly HashSet<SyntaxNode> resolvedBodies = [];

    public override void Resolve(ResolutionContext context)
    {
        this.context = context;

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
        foreach (var root in context.SyntaxTree.Roots)
            ResolveTopLevelStatements(root);
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
                foreach (var member in block.Members) ResolveTopLevel(member, scope, containingFunction);
                break;
            case NamespaceDeclaration declaration when declaration.Body is NamespaceBlockBody body:
                foreach (var member in body.Members) ResolveTopLevel(member, scope, containingFunction);
                break;
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
                ResolveExpression(statement.Condition, scope, containingFunction);
                ResolveTopLevel(statement.ThenStatement, scope, containingFunction);
                if (statement.ElseStatement is not null)
                    ResolveTopLevel(statement.ElseStatement.Statement, scope, containingFunction);
                break;
            case TopLevelWhileStatement statement:
                ResolveExpression(statement.Condition, scope, containingFunction);
                ResolveTopLevel(statement.Statement, scope, containingFunction);
                break;
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
        ResolveTypeConstraints(symbol.Syntax.Constraints, symbol.GenericParameters, scope);
        ResolveGenericParameterDeclarations(symbol.Syntax.Name, symbol.GenericParameters);
        var target = ResolveType(symbol.Syntax.Target, scope);

        symbol.HasCompatibleConstraints = target is { } handle &&
            AliasConstraintsAreCompatible(handle, symbol.Syntax.Target, scope);
        symbol.Target = symbol.HasCompatibleConstraints ? target : null;
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
        SymbolKind.Type => context.TypeSymbols[handle.ID].GenericParameters,
        SymbolKind.NestedType => context.NestedTypeSymbols[handle.ID].GenericParameters,
        SymbolKind.Alias => context.AliasSymbols[handle.ID].GenericParameters,
        _ => []
    };

    private static IReadOnlyList<TypeSyntax> GetGenericArguments(TypeSyntax syntax)
    {
        switch (syntax)
        {
            case GenericType generic:
            {
                var arguments = new List<TypeSyntax>(generic.GenericArguments.Count);
                foreach (var argument in generic.GenericArguments)
                    arguments.Add(argument);
                return arguments;
            }
            case QualifiedType qualified:
                return GetGenericArguments(qualified.Right);
            case ModifiedType modified:
                return GetGenericArguments(modified.Type);
            default:
                return [];
        }
    }

    private bool SatisfiesAllConstraints(SymbolHandle candidate, IReadOnlyList<SymbolHandle> required)
    {
        foreach (var constraint in required)
            if (!SatisfiesConstraint(candidate, constraint, []))
                return false;
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
                foreach (var constraint in context.GenericParameterSymbols[candidate.ID].Constraints)
                    if (SatisfiesConstraint(constraint, required, visited))
                        return true;
                break;
            case SymbolKind.Type:
                foreach (var baseType in context.TypeSymbols[candidate.ID].BaseTypes)
                    if (SatisfiesConstraint(baseType, required, visited))
                        return true;
                break;
            case SymbolKind.NestedType:
                foreach (var baseType in context.NestedTypeSymbols[candidate.ID].BaseTypes)
                    if (SatisfiesConstraint(baseType, required, visited))
                        return true;
                break;
            case SymbolKind.Alias when context.AliasSymbols[candidate.ID].Target is { } target:
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
        symbol.ReturnType = ResolveType(symbol.Syntax.Signature.ReturnType, scope);
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
        symbol.ReturnType = ResolveType(symbol.Syntax.Signature.ReturnType, scope);
        ResolveTypeConstraints(symbol.Syntax.Signature.Constraints, symbol.GenericParameters, scope);
        ResolveGenericParameterDeclarations(symbol.Syntax.Signature.Identifier, symbol.GenericParameters);
        ResolveBodyOnce(symbol.Syntax.Body, scope, ResolutionContext.GetHandle(symbol));
    }

    private void ResolveParameter(ParameterSymbol symbol)
    {
        if (symbol.ContainingFunction is not { } containing)
            return;

        var signature = containing.Kind switch
        {
            SymbolKind.Function => context.FunctionSymbols[containing.ID].Syntax?.Signature,
            SymbolKind.Method => context.MethodSymbols[containing.ID].Syntax?.Signature,
            _ => null
        };
        if (signature is null)
            return;

        foreach (var parameter in signature.Parameters)
        {
            if (ResolutionContext.GetSymbolName(parameter.Declarator.Identifier).Last != symbol.Name)
                continue;

            symbol.Type = ResolveType(parameter.Declarator.Type, symbol.EnclosingScope);
            ResolveExpression(parameter.Initializer?.Initializer, symbol.EnclosingScope, containing);
            return;
        }
    }

    private void ResolveVariable(GlobalVariableSymbol symbol, VariableDeclaration? syntax) =>
        ResolveVariableCore(symbol, syntax, value => symbol.Type = value, value => symbol.Attributes = value);

    private void ResolveVariable(FieldSymbol symbol, VariableDeclaration? syntax) =>
        ResolveVariableCore(symbol, syntax, value => symbol.Type = value, value => symbol.Attributes = value);

    private void ResolveVariable(LocalVariableSymbol symbol, VariableDeclaration? syntax) =>
        ResolveVariableCore(symbol, syntax, value => symbol.Type = value, value => symbol.Attributes = value);

    private void ResolveVariableCore(Symbol symbol, VariableDeclaration? syntax, Action<SymbolHandle?> setType, Action<List<SymbolHandle>> setAttributes)
    {
        if (syntax is null)
            return;

        setAttributes(ResolveAttributes(syntax.Attributes, symbol.EnclosingScope));
        setType(ResolveType(syntax.Type, symbol.EnclosingScope));

        foreach (var declarator in syntax.Declarators)
            if (ResolutionContext.GetSymbolName(declarator.Identifier).Last == symbol.Name)
                ResolveExpression(declarator.Initializer?.Initializer, symbol.EnclosingScope, GetContainingFunction(symbol));
    }

    private void ResolveProperty(PropertySymbol symbol)
    {
        if (symbol.Syntax is null)
            return;

        symbol.Attributes = ResolveAttributes(symbol.Syntax.Attributes, symbol.EnclosingScope);
        symbol.Type = ResolveType(symbol.Syntax.Type, symbol.EnclosingScope);
        foreach (var accessor in symbol.Syntax.Body.Accessors)
        {
            Scope scope = context.GetSyntaxScope(accessor.Body, symbol.EnclosingScope);
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
                ResolveExpression(statement.Condition, scope, containingFunction);
                ResolveLocal(statement.ThenStatement, scope, containingFunction);
                if (statement.ElseStatement is not null)
                    ResolveLocal(statement.ElseStatement.Statement, scope, containingFunction);
                break;
            case LocalWhileStatement statement:
                ResolveExpression(statement.Condition, scope, containingFunction);
                ResolveLocal(statement.Body, scope, containingFunction);
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
        ResolveType(syntax.Type, scope);
        foreach (var attribute in syntax.Attributes)
            foreach (var application in attribute.Attributes)
                ResolveNamed(application.Name, scope, application);
        foreach (var declarator in syntax.Declarators)
            ResolveExpression(declarator.Initializer?.Initializer, scope, containingFunction);
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
        foreach (var handle in parameters)
            if (handle.Kind is SymbolKind.GenericParameter)
                context.GenericParameterSymbols[handle.ID].Constraints = [];

        foreach (var clause in clauses)
        {
            var parameter = FindGenericParameter(parameters, ResolutionContext.GetSymbolName(clause.GenericParameter).Last);
            if (parameter is null)
                continue;

            context.ResolvedTree.AddReference(clause.GenericParameter, ResolutionContext.GetHandle(parameter));
            foreach (var constraint in clause.Constraints)
                if (constraint is TypeTypeConstraint typeConstraint && ResolveType(typeConstraint.Type, scope) is { } resolved)
                    parameter.Constraints.Add(resolved);
        }
    }

    private GenericParameterSymbol? FindGenericParameter(IReadOnlyList<SymbolHandle> handles, SymbolPart name)
    {
        foreach (var handle in handles)
            if (handle.Kind is SymbolKind.GenericParameter && context.GenericParameterSymbols[handle.ID].Name == name)
                return context.GenericParameterSymbols[handle.ID];
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
                if (ResolveSingle(scope, ResolutionContext.GetSymbolName(attribute.Name)) is { } symbol)
                    result.Add(ResolutionContext.GetHandle(symbol));
                foreach (var argument in attribute.Arguments)
                    ResolveExpression(argument, scope, default);
            }
        return result;
    }

    private List<SymbolHandle> ResolveTypes(SeparatedSyntaxList<TypeSyntax> types, Scope scope)
    {
        var result = new List<SymbolHandle>(types.Count);
        foreach (var type in types)
            if (ResolveType(type, scope) is { } resolved)
                result.Add(resolved);
        return result;
    }

    private SymbolHandle? ResolveType(TypeSyntax syntax, Scope scope)
    {
        if (syntax is GenericType generic)
        {
            SymbolHandle? genericTarget = ResolveTypeName(generic, scope);
            ResolveGenericArguments(generic, genericTarget, scope);
            return genericTarget;
        }

        ResolveTypeChildren(syntax, scope);
        return ResolveTypeName(syntax, scope);
    }

    private SymbolHandle? ResolveTypeName(TypeSyntax syntax, Scope scope)
    {
        if (ResolveSingle(scope, ResolutionContext.GetSymbolName(syntax)) is not { } symbol)
            return null;

        var handle = ResolutionContext.GetHandle(symbol);
        context.ResolvedTree.AddReference(syntax, handle);
        return handle;
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
        _ => ResolveType(argument, scope)
    };

    private SymbolHandle? ResolveNamedArgumentAsType(IdentifierNameExpression expression, Scope scope)
    {
        if (ResolveSingle(scope, new SymbolName(new SymbolPart(expression.Identifier))) is not { } symbol)
            return null;

        var handle = ResolutionContext.GetHandle(symbol);
        context.ResolvedTree.AddReference(expression, handle);
        return handle;
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
            case ModifiedType modified when modified.Modifier is ArrayTypeModifier array:
                ResolveExpression(array.Size, scope, default);
                break;
        }
    }

    private void ResolveExpression(Expression? expression, Scope scope, SymbolHandle containingFunction)
    {
        if (expression is null)
            return;

        switch (expression)
        {
            case NamedExpression named:
                ResolveNamedExpression(named, scope);
                break;
            case BinaryExpression binary:
                ResolveExpression(binary.LeftExpression, scope, containingFunction);
                ResolveExpression(binary.RightExpression, scope, containingFunction);
                break;
            case AssignmentExpression assignment:
                ResolveExpression(assignment.LhsExpression, scope, containingFunction);
                ResolveExpression(assignment.RhsExpression, scope, containingFunction);
                break;
            case UnaryExpression unary:
                ResolveExpression(unary.Operand, scope, containingFunction);
                break;
            case ParenthesizedExpression parenthesized:
                ResolveExpression(parenthesized.Expression, scope, containingFunction);
                break;
            case CastExpression cast:
                ResolveType(cast.Type, scope);
                ResolveExpression(cast.Expression, scope, containingFunction);
                break;
            case AmbiguousCastOrParenthesizedExpression ambiguous:
                ResolveExpression(ambiguous.CastExpression, scope, containingFunction);
                ResolveExpression(ambiguous.ParenthesizedExpression, scope, containingFunction);
                break;
            case CallExpression call:
                ResolveExpression(call.Callee, scope, containingFunction);
                foreach (var argument in call.Arguments) ResolveExpression(argument, scope, containingFunction);
                break;
            case IndexExpression index:
                ResolveExpression(index.Expression, scope, containingFunction);
                ResolveExpression(index.Index, scope, containingFunction);
                break;
            case MemberAccessExpression access:
                ResolveExpression(access.Expression, scope, containingFunction);
                break;
            case NamedArgumentExpression argument:
                ResolveExpression(argument.Value, scope, containingFunction);
                break;
            case IfExpression conditional:
                ResolveExpression(conditional.Condition, scope, containingFunction);
                ResolveExpression(conditional.ThenExpression, scope, containingFunction);
                ResolveExpression(conditional.ElseExpression, scope, containingFunction);
                break;
            case ElseExpression @else:
                ResolveExpression(@else.Expression, scope, containingFunction);
                break;
            case BlockExpression block:
                Scope blockScope = context.GetSyntaxScope(block, scope);
                foreach (var local in block.Locals) ResolveLocal(local, blockScope, containingFunction);
                ResolveExpression(block.FinalExpression, blockScope, containingFunction);
                break;
            case CollectionExpression collection:
                foreach (var item in collection.Expressions) ResolveExpression(item, scope, containingFunction);
                foreach (var modifier in collection.Modifiers)
                    if (modifier is CollectionConstructorModifier constructor)
                        foreach (var argument in constructor.Arguments) ResolveExpression(argument, scope, containingFunction);
                break;
            case ConstructorCallExpression constructor:
                ResolveType(constructor.Type, scope);
                foreach (var argument in constructor.Arguments) ResolveExpression(argument, scope, containingFunction);
                ResolveInitializer(constructor.WithClause?.Initializer, scope, containingFunction);
                break;
            case ArrayCreationExpression array:
                ResolveType(array.Type, scope);
                ResolveExpression(array.Size, scope, containingFunction);
                ResolveInitializer(array.Initializer, scope, containingFunction);
                ResolveInitializer(array.WithClause?.Initializer, scope, containingFunction);
                break;
        }
    }

    private void ResolveInitializer(CollectionInitializer? initializer, Scope scope, SymbolHandle containingFunction)
    {
        if (initializer is not null)
            foreach (var expression in initializer.Expressions)
                ResolveExpression(expression, scope, containingFunction);
    }

    private void ResolveNamedExpression(NamedExpression syntax, Scope scope)
    {
        var name = syntax is GenericNameExpression generic
            ? new SymbolPart(generic.Identifier, generic.GenericArguments.Count)
            : new SymbolPart(syntax.Identifier);
        if (ResolveSingle(scope, new SymbolName(name)) is { } symbol)
            context.ResolvedTree.AddReference(syntax, ResolutionContext.GetHandle(symbol));
        if (syntax is GenericNameExpression genericName)
            foreach (var argument in genericName.GenericArguments)
                ResolveType(argument, scope);
    }

    private void ResolveNamed(NamedSyntax syntax, Scope scope, SyntaxNode reference)
    {
        if (ResolveSingle(scope, ResolutionContext.GetSymbolName(syntax)) is { } symbol)
            context.ResolvedTree.AddReference(reference, ResolutionContext.GetHandle(symbol));
    }

    private static Symbol? ResolveSingle(Scope scope, SymbolName name)
    {
        var symbols = scope[name];
        if (symbols.Count is 1)
            return symbols[0];
        return name.IsQualified && scope[name.Last].Count is 1 ? scope[name.Last][0] : null;
    }

    private static Scope GetOwnedScope(Symbol symbol) =>
        symbol.EnclosingScope.GetChildScope(ResolutionContext.GetHandle(symbol)) ?? symbol.EnclosingScope;

    private static SymbolHandle GetContainingFunction(Symbol symbol) => symbol switch
    {
        ParameterSymbol parameter when parameter.ContainingFunction is { } handle => handle,
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
        fields.Clear(); properties.Clear(); methods.Clear(); nestedTypes.Clear();
        foreach (var symbol in scope.Symbols.Values)
        {
            var handle = ResolutionContext.GetHandle(symbol);
            switch (symbol.Kind)
            {
                case SymbolKind.Field: fields.Add(handle); break;
                case SymbolKind.Property: properties.Add(handle); break;
                case SymbolKind.Method: methods.Add(handle); break;
                case SymbolKind.NestedType: nestedTypes.Add(handle); break;
            }
        }
    }
}
