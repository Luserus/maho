using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class DeclarationResolutionPass
{
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
            case TypeAsExpression typeAs:
                ResolveType(typeAs.TargetType, scope);
                ResolveType(typeAs.InterfaceType, scope);
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
                foreach (var arg in index.Arguments)
                    ResolveExpression(arg, scope, containingSymbol);
                break;
            case MemberAccessExpression access:
                ResolveMemberAccess(access, scope, containingSymbol);
                break;
            case NameofExpression nameofExpr:
                ResolveExpression(nameofExpr.Argument, scope, containingSymbol);
                break;
            case NamedArgumentExpression argument:
                ResolveExpression(argument.Value, scope, containingSymbol);
                break;
            case IfExpression conditional:
                Scope ifScope = conditional.Declaration is not null ? context.GetSyntaxScope(conditional, scope) : scope;
                if (conditional.Declaration is not null)
                {
                    ResolveDeclarationSyntax(conditional.Declaration, ifScope, containingSymbol);
                }
                ResolveExpression(conditional.Condition, ifScope, containingSymbol);
                ResolveExpression(conditional.ThenExpression, ifScope, containingSymbol);
                ResolveExpression(conditional.ElseExpression, ifScope, containingSymbol);
                break;
            case WhileExpression loop:
                Scope whileScope = loop.Declaration is not null ? context.GetSyntaxScope(loop, scope) : scope;
                if (loop.Declaration is not null)
                {
                    ResolveDeclarationSyntax(loop.Declaration, whileScope, containingSymbol);
                }
                ResolveExpression(loop.Condition, whileScope, containingSymbol);
                ResolveExpression(loop.Body, whileScope, containingSymbol);
                break;
            case ForExpression forExpr:
                Scope forScope = context.GetSyntaxScope(forExpr, scope);
                ResolveExpression(forExpr.Collection, scope, containingSymbol);
                if (forExpr.Declaration is not null)
                {
                    ResolveDeclarationSyntax(forExpr.Declaration, forScope, containingSymbol);
                }
                if (forExpr.VariableExpression is not null)
                {
                    ResolveExpression(forExpr.VariableExpression, scope, containingSymbol);
                }
                ResolveExpression(forExpr.Body, forScope, containingSymbol);
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
            case MultiTermExpression multiTerm:
                foreach (var term in multiTerm.Terms)
                    ResolveExpression(term, scope, containingSymbol);
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
            case VariantCreationExpression variantCreation:
                ResolveExpression(variantCreation.Target, scope, containingSymbol);
                ResolveInitializer(variantCreation.Initializer, scope, containingSymbol);
                break;
            case MatchExpression match:
                ResolveMatchExpression(match, scope, containingSymbol);
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

    private void ResolveMemberAccess(MemberAccessExpression access, Scope scope, SymbolHandle containingSymbol)
    {
        ResolveExpression(access.Expression, scope, containingSymbol);

        if (access.Expression is TypeAsExpression typeAs)
        {
            TypeSymbol? asTargetType = null;
            TypeSymbol? asInterfaceType = null;

            var targetRef = ResolveType(typeAs.TargetType, scope, reportDiagnostics: false);
            if (targetRef is { Handle: { } targetHandle } && context.GetTypeSymbol(targetHandle) is TypeSymbol ts)
                asTargetType = ts;

            var ifaceRef = ResolveType(typeAs.InterfaceType, scope, reportDiagnostics: false);
            if (ifaceRef is { Handle: { } ifaceHandle } && context.GetTypeSymbol(ifaceHandle) is TypeSymbol isym)
                asInterfaceType = isym;

            if (asTargetType is not null && asInterfaceType is not null)
            {
                var targetScope = GetOwnedScope(asTargetType);
                if (targetScope is not null)
                {
                    var memberName = new SymbolPart(access.Identifier);
                    var candidates = targetScope[new SymbolName(memberName)];

                    MethodSymbol? chosen = null;

                    // Priority 1: interface-scoped method matching interface name
                    foreach (var cand in candidates)
                    {
                        if (cand is MethodSymbol m && m.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                        {
                            if (m.ExplicitInterfaceName is { } ein && ein.Last.Equals(asInterfaceType.Name))
                            {
                                chosen = m;
                                break;
                            }
                        }
                    }

                    // Priority 2: ordinary method
                    if (chosen is null)
                    {
                        foreach (var cand in candidates)
                        {
                            if (cand is MethodSymbol m && !m.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                            {
                                chosen = m;
                                break;
                            }
                        }
                    }

                    if (chosen is not null)
                    {
                        context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(chosen));
                        return;
                    }
                }
            }
        }

        if (access.Expression is NamedExpression named)
        {
            var sym = ResolveSingle(scope, new SymbolName(new SymbolPart(named.Identifier.Value)));
            if (sym is TypeSymbol typeSym)
            {
                var typeScope = GetOwnedScope(typeSym);
                if (typeScope is not null)
                {
                    var memberName = new SymbolPart(access.Identifier);
                    var variantSyms = typeScope[new SymbolName(memberName)];
                    var accessible = variantSyms.Where(cand =>
                    {
                        if (cand is MethodSymbol m)
                            return !m.Flags.HasFlag(FunctionFlags.InterfaceScoped) && m.Flags.HasFlag(FunctionFlags.Static);
                        if (cand is PropertySymbol p)
                            return !p.Flags.HasFlag(FunctionFlags.InterfaceScoped) && p.Flags.HasFlag(FunctionFlags.Static);
                        if (cand is FieldSymbol)
                            return false;
                        return true;
                    }).ToList();
                    if (accessible.Count > 0)
                    {
                        context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(accessible[0]));
                        return;
                    }
                }
            }
        }
    }

    private void ResolveMatchExpression(MatchExpression match, Scope scope, SymbolHandle containingSymbol)
    {
        ResolveExpression(match.Expression, scope, containingSymbol);

        foreach (var arm in match.Arms)
        {
            var armScope = context.CreateScope(scope);
            context.RegisterSyntaxScope(arm, armScope);

            DeclarePatternVariables(arm.Pattern, armScope);
            ResolveExpression(arm.Expression, armScope, containingSymbol);
        }
    }

    private void DeclarePatternVariables(PatternSyntax pattern, Scope armScope)
    {
        switch (pattern)
        {
            case VariablePattern varPat:
                context.CreateLocalVariableSymbol(
                    armScope,
                    context.GetScopedSymbolName(varPat.Name).Last,
                    default,
                    null);
                break;

            case LiteralPattern lit:
                ResolveExpression(lit.Literal, armScope, default);
                break;

            case EnumVariantPattern variantPat:
                if (variantPat.SubPatterns is { } subPatterns)
                {
                    foreach (var sub in subPatterns)
                        DeclarePatternVariables(sub, armScope);
                }

                if (variantPat.FieldPatterns is { } fieldPatterns)
                {
                    foreach (var fp in fieldPatterns)
                        DeclarePatternVariables(fp.Pattern, armScope);
                }
                break;

            case TypePattern typePat:
                if (typePat.Variable is not null)
                {
                    var resolvedType = ResolveType(typePat.Type, armScope, reportDiagnostics: false);
                    var varSym = context.CreateLocalVariableSymbol(
                        armScope,
                        context.GetScopedSymbolName(typePat.Variable).Last,
                        default,
                        null);
                    varSym.Type = resolvedType;
                }
                break;
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

}
