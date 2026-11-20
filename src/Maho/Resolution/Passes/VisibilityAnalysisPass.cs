using System.Collections.Generic;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution.Passes;

/// <summary>
/// Executes after all semantic typing, CFG analysis, and lifetime verification to enforce access control
/// boundaries (<c>private</c>, <c>protected</c>, <c>internal</c>, <c>public</c>) across member accesses,
/// function calls, properties, and type references.
/// </summary>
internal sealed class VisibilityAnalysisPass : ResolutionPass
{
    private ResolutionContext context = null!;
    private SymbolHandle? currentType;
    private SymbolHandle? currentCallable;
    private Scope currentScope = null!;
    private SourceText? currentSource;

    public override void Resolve(ResolutionContext context)
    {
        this.context = context;

        // 1. Validate top-level types
        foreach (var type in context.TypeSymbols)
        {
            if (type.Syntax is not null)
            {
                currentType = ResolutionContext.GetHandle(type);
                currentScope = GetOwnedScope(type);
                currentSource = type.Syntax.GetSource();

                // Check base types
                foreach (var baseRef in type.BaseTypes)
                    CheckTypeRefAccessibility(baseRef, type.Syntax.Name.GetSpan() ?? default);

                // Check members
                CheckTypeMembers(type.Syntax);
            }
        }

        // 2. Validate nested types
        foreach (var type in context.NestedTypeSymbols)
        {
            if (type.Syntax is not null)
            {
                currentType = ResolutionContext.GetHandle(type);
                currentScope = GetOwnedScope(type);
                currentSource = type.Syntax.GetSource();

                foreach (var baseRef in type.BaseTypes)
                    CheckTypeRefAccessibility(baseRef, type.Syntax.Name.GetSpan() ?? default);

                CheckTypeMembers(type.Syntax);
            }
        }

        // 3. Validate top-level functions
        foreach (var fn in context.FunctionSymbols)
        {
            if (fn.Syntax is not null)
            {
                currentType = null;
                currentCallable = ResolutionContext.GetHandle(fn);
                currentScope = GetOwnedScope(fn);
                currentSource = fn.Syntax.GetSource();

                CheckFunctionSignature(fn.Syntax.Signature);
                if (fn.Syntax.Body is { } body)
                    CheckBody(body);
            }
        }

        // 4. Validate methods
        foreach (var method in context.MethodSymbols)
        {
            if (method.Syntax is not null)
            {
                currentType = method.Parent;
                currentCallable = ResolutionContext.GetHandle(method);
                currentScope = GetOwnedScope(method);
                currentSource = method.Syntax.GetSource();

                CheckFunctionSignature(method.Syntax.Signature);
                if (method.Syntax.Body is { } body)
                    CheckBody(body);
            }
        }

        // 5. Validate properties
        foreach (var prop in context.PropertySymbols)
        {
            if (prop.Syntax is not null)
            {
                currentType = prop.Parent;
                currentCallable = ResolutionContext.GetHandle(prop);
                currentScope = prop.EnclosingScope;
                currentSource = prop.Syntax.GetSource();

                CheckTypeSyntaxAccessibility(prop.Syntax.Type);

                foreach (var acc in prop.Syntax.Body.Accessors)
                {
                    if (acc.Body is { } body)
                    {
                        var accScope = context.GetSyntaxScope(acc.Body, currentScope);
                        var prevScope = currentScope;
                        currentScope = accScope;
                        CheckBody(body);
                        currentScope = prevScope;
                    }
                }
            }
        }

        // 6. Validate global variables
        foreach (var global in context.GlobalVariableSymbols)
        {
            if (global.Syntax is not null)
            {
                currentType = null;
                currentCallable = null;
                currentScope = global.EnclosingScope;
                currentSource = global.Syntax.GetSource();

                if (global.Syntax.Type is not null)
                    CheckTypeSyntaxAccessibility(global.Syntax.Type);

                if (global.Syntax.Declarator.Initializer?.Initializer is { } initExpr)
                    CheckExpression(initExpr, isAssignmentLhs: false);
            }
        }
    }

    private void CheckTypeMembers(TypeDeclaration typeDecl)
    {
        if (typeDecl.Body is TypeBlockBody blockBody)
        {
            foreach (var member in blockBody.Members)
            {
                if (member is MemberFieldDeclaration memberVar)
                {
                    if (memberVar.Declaration.Type is not null)
                        CheckTypeSyntaxAccessibility(memberVar.Declaration.Type);

                    if (memberVar.Declaration.Declarator.Initializer?.Initializer is { } initExpr)
                        CheckExpression(initExpr, isAssignmentLhs: false);
                }
            }
        }
    }

    private void CheckFunctionSignature(FunctionSignature signature)
    {
        if (signature.ReturnType is not null)
            CheckTypeSyntaxAccessibility(signature.ReturnType);

        foreach (var p in signature.Parameters)
        {
            if (p.Declarator.Type is not null)
                CheckTypeSyntaxAccessibility(p.Declarator.Type);
        }
    }

    private void CheckBody(FunctionBody body)
    {
        switch (body)
        {
            case FunctionBlockBody block:
                CheckLocals(block.Locals);
                break;
            case FunctionLambdaBody lambda:
                CheckLocals([lambda.Statement]);
                break;
        }
    }

    private void CheckLocals(IReadOnlyList<Local> locals)
    {
        foreach (var local in locals)
        {
            switch (local)
            {
                case LocalExpressionStatement exprStmt:
                    CheckExpression(exprStmt.Expression, isAssignmentLhs: false);
                    break;
                case LocalVariableDeclarationStatement varStmt:
                    if (varStmt.Declaration.Type is not null)
                        CheckTypeSyntaxAccessibility(varStmt.Declaration.Type);
                    if (varStmt.Declaration.Declarator.Initializer?.Initializer is { } initExpr)
                        CheckExpression(initExpr, isAssignmentLhs: false);
                    break;
                case LocalReturnStatement retStmt:
                    if (retStmt.Statement.Expression is { } retExpr)
                        CheckExpression(retExpr, isAssignmentLhs: false);
                    break;
            }
        }
    }

    private void CheckExpression(Expression expr, bool isAssignmentLhs)
    {
        switch (expr)
        {
            case MemberAccessExpression access:
                CheckMemberAccess(access, isAssignmentLhs);
                CheckExpression(access.Expression, isAssignmentLhs: false);
                break;

            case AssignmentExpression assignment:
                CheckExpression(assignment.LhsExpression, isAssignmentLhs: true);
                CheckExpression(assignment.RhsExpression, isAssignmentLhs: false);
                break;

            case CallExpression call:
                if (context.ResolvedTree.TryGetReference(call, out var callTargetHandle))
                {
                    if (context.GetSymbol(callTargetHandle) is { } targetCallable)
                        CheckSymbolAccessibility(targetCallable, call.GetSpan() ?? default);
                }
                CheckExpression(call.Callee, isAssignmentLhs: false);
                foreach (var arg in call.Arguments)
                    CheckExpression(arg, isAssignmentLhs: false);
                break;

            case BinaryExpression binary:
                if (binary.OperatorToken.Kind == TokenKind.Equals)
                {
                    CheckExpression(binary.LeftExpression, isAssignmentLhs: true);
                    CheckExpression(binary.RightExpression, isAssignmentLhs: false);
                }
                else
                {
                    CheckExpression(binary.LeftExpression, isAssignmentLhs: false);
                    CheckExpression(binary.RightExpression, isAssignmentLhs: false);
                }
                break;

            case UnaryExpression unary:
                CheckExpression(unary.Operand, isAssignmentLhs: false);
                break;

            case ParenthesizedExpression paren:
                CheckExpression(paren.Expression, isAssignmentLhs);
                break;

            case IfExpression ifExpr:
                CheckExpression(ifExpr.Condition, isAssignmentLhs: false);
                CheckExpression(ifExpr.ThenExpression, isAssignmentLhs: false);
                if (ifExpr.ElseExpression is { } elseExpr)
                    CheckExpression(elseExpr.Expression, isAssignmentLhs: false);
                break;

            case WhileExpression whileExpr:
                CheckExpression(whileExpr.Condition, isAssignmentLhs: false);
                CheckExpression(whileExpr.Body, isAssignmentLhs: false);
                break;

            case BlockExpression blockExpr:
                CheckLocals(blockExpr.Locals);
                break;

            case AsExpression asExpr:
                CheckExpression(asExpr.Expression, isAssignmentLhs: false);
                CheckTypeSyntaxAccessibility(asExpr.Type);
                break;

            case NamedExpression named:
                if (context.ResolvedTree.TryGetReference(named, out var namedH))
                {
                    var sym = context.GetSymbol(namedH);
                    if (sym is FieldSymbol or GlobalVariableSymbol)
                        CheckSymbolAccessibility(sym, named.GetSpan() ?? default);
                }
                break;
        }
    }

    private void CheckMemberAccess(MemberAccessExpression access, bool isAssignmentLhs)
    {
        if (!context.ResolvedTree.TryGetReference(access, out var memberHandle))
            return;

        var memberSymbol = context.GetSymbol(memberHandle);
        if (memberSymbol is null)
            return;

        var span = access.Identifier.Span;

        if (memberSymbol is PropertySymbol prop)
        {
            var flags = isAssignmentLhs ? prop.SetterFlags : prop.GetterFlags;
            var acc = GetFunctionAccessibility(flags, isTopLevel: false);
            var accessorName = isAssignmentLhs ? $"{prop.Name.Text}.set" : $"{prop.Name.Text}.get";
            CheckAccessibility(prop, acc, span, accessorName);
            return;
        }

        CheckSymbolAccessibility(memberSymbol, span);
    }

    private void CheckSymbolAccessibility(Symbol targetSymbol, TextSpan span)
    {
        var acc = GetAccessibility(targetSymbol, context);
        CheckAccessibility(targetSymbol, acc, span);
    }

    private void CheckAccessibility(Symbol targetSymbol, Accessibility acc, TextSpan span, string? memberNameOverride = null)
    {
        if (acc == Accessibility.Public)
            return;

        var memberName = memberNameOverride ?? targetSymbol.Name.Text;
        var declaringTypeName = GetDeclaringTypeName(targetSymbol);

        if (acc == Accessibility.Internal)
        {
            var handle = ResolutionContext.GetHandle(targetSymbol);
            if (!context.IsDeclaredInCurrentProject(handle))
            {
                context.Diagnostics.ReportCannotAccessInternalMember(span, memberName, declaringTypeName, currentSource);
            }
            return;
        }

        if (acc == Accessibility.Protected)
        {
            var declTypeHandle = GetDeclaringTypeHandle(targetSymbol);
            if (declTypeHandle is null)
                return;

            if (currentType is null || !IsOrDerivesFrom(currentType.Value, declTypeHandle.Value))
            {
                var curTypeName = currentType is { } cth ? context.GetSymbol(cth)?.Name.Text ?? "<unknown>" : "<top-level>";
                context.Diagnostics.ReportCannotAccessProtectedMember(span, memberName, declaringTypeName, curTypeName, currentSource);
            }
            return;
        }

        if (acc == Accessibility.Private)
        {
            var declTypeHandle = GetDeclaringTypeHandle(targetSymbol);
            if (declTypeHandle is null)
                return;

            if (currentType is null || !IsWithinTypeHierarchy(currentType.Value, declTypeHandle.Value))
            {
                context.Diagnostics.ReportCannotAccessPrivateMember(span, memberName, declaringTypeName, currentSource);
            }
        }
    }

    private void CheckTypeRefAccessibility(TypeRef typeRef, TextSpan span)
    {
        if (typeRef.Handle is { } th)
        {
            var sym = context.GetSymbol(th);
            if (sym is not null)
            {
                var acc = GetAccessibility(sym, context);
                if (acc != Accessibility.Public)
                {
                    var typeName = sym.Name.Text;
                    if (acc == Accessibility.Internal && !context.IsDeclaredInCurrentProject(th))
                    {
                        context.Diagnostics.ReportCannotAccessInaccessibleType(span, typeName, "internal", currentSource);
                    }
                    else if (acc == Accessibility.Protected)
                    {
                        var declTypeHandle = GetDeclaringTypeHandle(sym);
                        if (declTypeHandle is not null && (currentType is null || !IsOrDerivesFrom(currentType.Value, declTypeHandle.Value)))
                            context.Diagnostics.ReportCannotAccessInaccessibleType(span, typeName, "protected", currentSource);
                    }
                    else if (acc == Accessibility.Private)
                    {
                        var declTypeHandle = GetDeclaringTypeHandle(sym);
                        if (declTypeHandle is not null && (currentType is null || !IsWithinTypeHierarchy(currentType.Value, declTypeHandle.Value)))
                            context.Diagnostics.ReportCannotAccessInaccessibleType(span, typeName, "private", currentSource);
                    }
                }
            }
        }

        if (typeRef.SpecialType is GenericSemanticType gen)
        {
            foreach (var arg in gen.TypeArguments)
                CheckTypeRefAccessibility(arg, span);
        }
    }

    private void CheckTypeSyntaxAccessibility(TypeSyntax syntax)
    {
        if (context.ResolvedTree.TryGetReference(syntax, out var th))
            CheckTypeRefAccessibility(TypeRef.Resolved(th), syntax.GetSpan() ?? default);
    }

    private bool IsOrDerivesFrom(SymbolHandle typeH, SymbolHandle targetBaseH)
    {
        if (typeH == targetBaseH)
            return true;

        var sym = (Symbol?)context.GetTypeSymbol(typeH) ?? context.GetNestedTypeSymbol(typeH);
        if (sym is TypeSymbol ts)
        {
            if (ts.LinearizedBases.Contains(targetBaseH))
                return true;
        }
        else if (sym is NestedTypeSymbol nts)
        {
            if (nts.LinearizedBases.Contains(targetBaseH))
                return true;
            if (nts.Parent is { } pH && IsOrDerivesFrom(pH, targetBaseH))
                return true;
        }

        return false;
    }

    private bool IsWithinTypeHierarchy(SymbolHandle currentH, SymbolHandle declaringH)
    {
        if (currentH == declaringH)
            return true;

        // Check if currentH is enclosed in declaringH
        for (var cur = currentH; ;)
        {
            var sym = (Symbol?)context.GetTypeSymbol(cur) ?? context.GetNestedTypeSymbol(cur);
            if (sym is NestedTypeSymbol nts && nts.Parent is { } pH)
            {
                if (pH == declaringH) return true;
                cur = pH;
            }
            else
            {
                break;
            }
        }

        // Check if declaringH is enclosed in currentH
        for (var decl = declaringH; ;)
        {
            var sym = (Symbol?)context.GetTypeSymbol(decl) ?? context.GetNestedTypeSymbol(decl);
            if (sym is NestedTypeSymbol nts && nts.Parent is { } pH)
            {
                if (pH == currentH) return true;
                decl = pH;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    private SymbolHandle? GetDeclaringTypeHandle(Symbol symbol) => symbol switch
    {
        MethodSymbol m => m.Parent,
        FieldSymbol f => f.Parent,
        PropertySymbol p => p.Parent,
        NestedTypeSymbol nt => nt.Parent,
        EnumVariantSymbol ev => ev.ContainingType,
        _ => null
    };

    private string GetDeclaringTypeName(Symbol symbol)
    {
        var parentH = GetDeclaringTypeHandle(symbol);
        if (parentH is { } pH && context.GetSymbol(pH) is { } parentSym)
            return parentSym.Name.Text;

        return symbol.Name.Text;
    }

    private static Accessibility GetAccessibility(Symbol symbol, ResolutionContext context)
    {
        if (symbol is MethodSymbol ms && ms.Parent is { } ifaceH)
        {
            var ifaceSym = context.GetSymbol(ifaceH);
            if (ifaceSym is TypeSymbol { TypeKind: TypeKind.Interface } ts)
                return GetTypeAccessibility(ts.Flags, isTopLevel: true);
            if (ifaceSym is NestedTypeSymbol { TypeKind: TypeKind.Interface } nts)
                return GetTypeAccessibility(nts.Flags, isTopLevel: false);
        }

        if (symbol is PropertySymbol ifaceProp && ifaceProp.Parent is { } ifacePropH)
        {
            var ifacePropSym = context.GetSymbol(ifacePropH);
            if (ifacePropSym is TypeSymbol { TypeKind: TypeKind.Interface } ts)
                return GetTypeAccessibility(ts.Flags, isTopLevel: true);
            if (ifacePropSym is NestedTypeSymbol { TypeKind: TypeKind.Interface } nts)
                return GetTypeAccessibility(nts.Flags, isTopLevel: false);
        }

        return symbol switch
        {
            FunctionSymbol fn => GetFunctionAccessibility(fn.Flags, isTopLevel: true),
            MethodSymbol m => GetFunctionAccessibility(m.Flags, isTopLevel: false),
            FieldSymbol f => GetVariableAccessibility(f.Flags),
            PropertySymbol p => GetFunctionAccessibility(p.Flags, isTopLevel: false),
            TypeSymbol t => GetTypeAccessibility(t.Flags, isTopLevel: true),
            NestedTypeSymbol nt => GetTypeAccessibility(nt.Flags, isTopLevel: false),
            GlobalVariableSymbol g => GetVariableAccessibility(g.Flags, isTopLevel: true),
            EnumVariantSymbol ev => (context.GetSymbol(ev.ContainingType)) is { } pSym
                ? (pSym is TypeSymbol pts ? GetTypeAccessibility(pts.Flags, true) : (pSym is NestedTypeSymbol pnts ? GetTypeAccessibility(pnts.Flags, false) : Accessibility.Public))
                : Accessibility.Public,
            _ => Accessibility.Public
        };
    }

    private static Accessibility GetTypeAccessibility(TypeFlags flags, bool isTopLevel) =>
        AccessibilityExtensions.FromTypeFlags(flags, isTopLevel);

    private static Accessibility GetFunctionAccessibility(FunctionFlags flags, bool isTopLevel) =>
        AccessibilityExtensions.FromFunctionFlags(flags, isTopLevel);

    private static Accessibility GetVariableAccessibility(VariableFlags flags, bool isTopLevel = false) =>
        AccessibilityExtensions.FromVariableFlags(flags, isTopLevel);

    private static Scope GetOwnedScope(Symbol symbol) => symbol switch
    {
        TypeSymbol t => t.EnclosingScope.GetChildScope(ResolutionContext.GetHandle(t)) ?? t.EnclosingScope,
        NestedTypeSymbol nt => nt.EnclosingScope.GetChildScope(ResolutionContext.GetHandle(nt)) ?? nt.EnclosingScope,
        FunctionSymbol f => f.EnclosingScope.GetChildScope(ResolutionContext.GetHandle(f)) ?? f.EnclosingScope,
        MethodSymbol m => m.EnclosingScope.GetChildScope(ResolutionContext.GetHandle(m)) ?? m.EnclosingScope,
        _ => symbol.EnclosingScope
    };
}
