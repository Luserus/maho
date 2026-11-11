using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

internal sealed class ExpressionResolutionPass : ResolutionPass
{
    private ResolutionContext context = null!;

    // Cached primitive handles for zero-allocation checks
    private SymbolHandle? hInt8, hInt16, hInt32, hInt64, hInt128, hNInt;
    private SymbolHandle? hUInt8, hUInt16, hUInt32, hUInt64, hUInt128, hNUInt;
    private SymbolHandle? hFloat16, hFloat32, hFloat64, hFloat128;
    private SymbolHandle? hBool, hChar, hStr, hVoid;

    private TypeRef voidType = TypeRef.Unresolved;
    private TypeRef boolType = TypeRef.Unresolved;

    private readonly HashSet<FunctionBody> visitedBodies = [];
    private readonly HashSet<VariableDeclaration> visitedDeclarations = [];
    private readonly Dictionary<LocalVariableSymbol, SyntaxNode> uncollapsedLocals = new();

    public override void Resolve(ResolutionContext context)
    {
        this.context = context;
        CacheIntrinsicHandles();

        // 1. Resolve top-level statements / main scope locals
        ResolveTopLevelStatements();

        // 2. Resolve global variable initializers
        foreach (var global in context.GlobalVariableSymbols)
        {
            if (global.Syntax is { } varDecl && visitedDeclarations.Add(varDecl))
                ResolveVariableDeclaration(varDecl, global.EnclosingScope, default);
        }

        // 3. Resolve field initializers
        foreach (var field in context.FieldSymbols)
        {
            if (field.Syntax is { } varDecl && visitedDeclarations.Add(varDecl))
                ResolveVariableDeclaration(varDecl, field.EnclosingScope, default);
        }

        // 4. Resolve function bodies
        foreach (var function in context.FunctionSymbols)
        {
            if (function.Syntax is { } syntax && visitedBodies.Add(syntax.Body))
            {
                var scope = context.GetSyntaxScope(syntax.Body, GetOwnedScope(function));
                ResolveFunctionBody(syntax.Body, scope, ResolutionContext.GetHandle(function), function.ReturnType);
            }
        }

        // 5. Resolve method bodies
        foreach (var method in context.MethodSymbols)
        {
            if (method.Syntax is { } syntax && visitedBodies.Add(syntax.Body))
            {
                var scope = context.GetSyntaxScope(syntax.Body, GetOwnedScope(method));
                ResolveFunctionBody(syntax.Body, scope, ResolutionContext.GetHandle(method), method.ReturnType);
            }
        }

        // 6. Resolve property accessors
        foreach (var property in context.PropertySymbols)
        {
            if (property.Syntax is { } syntax)
            {
                foreach (var accessor in syntax.Body.Accessors)
                {
                    if (visitedBodies.Add(accessor.Body))
                    {
                        var scope = context.GetSyntaxScope(accessor.Body, property.EnclosingScope);
                        var expectedReturn = accessor.Kind is PropertyAccessorKind.Get ? property.Type : voidType;
                        ResolveFunctionBody(accessor.Body, scope, ResolutionContext.GetHandle(property), expectedReturn);
                    }
                }
            }
        }
    }

    private void CacheIntrinsicHandles()
    {
        var intrinsics = context.DiscoveredIntrinsics;
        if (intrinsics.TryGet(KnownIntrinsics.Int8, out var h1)) hInt8 = h1;
        if (intrinsics.TryGet(KnownIntrinsics.Int16, out var h2)) hInt16 = h2;
        if (intrinsics.TryGet(KnownIntrinsics.Int32, out var h3)) hInt32 = h3;
        if (intrinsics.TryGet(KnownIntrinsics.Int64, out var h4)) hInt64 = h4;
        if (intrinsics.TryGet(KnownIntrinsics.Int128, out var h5)) hInt128 = h5;
        if (intrinsics.TryGet(KnownIntrinsics.NInt, out var h6)) hNInt = h6;

        if (intrinsics.TryGet(KnownIntrinsics.UInt8, out var uh1)) hUInt8 = uh1;
        if (intrinsics.TryGet(KnownIntrinsics.UInt16, out var uh2)) hUInt16 = uh2;
        if (intrinsics.TryGet(KnownIntrinsics.UInt32, out var uh3)) hUInt32 = uh3;
        if (intrinsics.TryGet(KnownIntrinsics.UInt64, out var uh4)) hUInt64 = uh4;
        if (intrinsics.TryGet(KnownIntrinsics.UInt128, out var uh5)) hUInt128 = uh5;
        if (intrinsics.TryGet(KnownIntrinsics.NUInt, out var uh6)) hNUInt = uh6;

        if (intrinsics.TryGet(KnownIntrinsics.Float16, out var f1)) hFloat16 = f1;
        if (intrinsics.TryGet(KnownIntrinsics.Float32, out var f2)) hFloat32 = f2;
        if (intrinsics.TryGet(KnownIntrinsics.Float64, out var f3)) hFloat64 = f3;
        if (intrinsics.TryGet(KnownIntrinsics.Float128, out var f4)) hFloat128 = f4;

        if (intrinsics.TryGet(KnownIntrinsics.Bool, out var b))
        {
            hBool = b;
            boolType = TypeRef.Resolved(b);
        }

        if (intrinsics.TryGet(KnownIntrinsics.Char, out var c)) hChar = c;
        if (intrinsics.TryGet(KnownIntrinsics.Str, out var s)) hStr = s;

        if (intrinsics.TryGet(KnownIntrinsics.Void, out var v))
        {
            hVoid = v;
            voidType = TypeRef.Resolved(v);
        }

        // Fallback for mock primitives in unit tests without explicit intrinsic registrations
        hInt8 ??= FindFallbackTypeHandle("int8");
        hInt16 ??= FindFallbackTypeHandle("int16");
        hInt32 ??= FindFallbackTypeHandle("int32");
        hInt64 ??= FindFallbackTypeHandle("int64");
        hInt128 ??= FindFallbackTypeHandle("int128");
        hNInt ??= FindFallbackTypeHandle("nint");

        hUInt8 ??= FindFallbackTypeHandle("uint8");
        hUInt16 ??= FindFallbackTypeHandle("uint16");
        hUInt32 ??= FindFallbackTypeHandle("uint32");
        hUInt64 ??= FindFallbackTypeHandle("uint64");
        hUInt128 ??= FindFallbackTypeHandle("uint128");
        hNUInt ??= FindFallbackTypeHandle("nuint");

        hFloat16 ??= FindFallbackTypeHandle("float16");
        hFloat32 ??= FindFallbackTypeHandle("float32");
        hFloat64 ??= FindFallbackTypeHandle("float64");
        hFloat128 ??= FindFallbackTypeHandle("float128");

        if (hBool is null && FindFallbackTypeHandle("bool") is { } fb)
        {
            hBool = fb;
            boolType = TypeRef.Resolved(fb);
        }

        hChar ??= FindFallbackTypeHandle("char");
        hStr ??= FindFallbackTypeHandle("str") ?? FindFallbackTypeHandle("string");

        if (hVoid is null && FindFallbackTypeHandle("void") is { } fv)
        {
            hVoid = fv;
            voidType = TypeRef.Resolved(fv);
        }
    }

    private SymbolHandle? FindFallbackTypeHandle(string name)
    {
        foreach (var sym in context.TypeSymbols)
        {
            if (sym.Name.Text.Equals(name, StringComparison.OrdinalIgnoreCase))
                return ResolutionContext.GetHandle(sym);
        }
        foreach (var sym in context.NestedTypeSymbols)
        {
            if (sym.Name.Text.Equals(name, StringComparison.OrdinalIgnoreCase))
                return ResolutionContext.GetHandle(sym);
        }
        return null;
    }

    private bool IsSignedInteger(TypeRef t) =>
        t.Handle is { } h && (h == hInt8 || h == hInt16 || h == hInt32 || h == hInt64 || h == hInt128 || h == hNInt);

    private bool IsUnsignedInteger(TypeRef t) =>
        t.Handle is { } h && (h == hUInt8 || h == hUInt16 || h == hUInt32 || h == hUInt64 || h == hUInt128 || h == hNUInt);

    private bool IsInteger(TypeRef t) => IsSignedInteger(t) || IsUnsignedInteger(t);

    private bool IsFloatingPoint(TypeRef t) =>
        t.Handle is { } h && (h == hFloat16 || h == hFloat32 || h == hFloat64 || h == hFloat128);

    private bool IsNumeric(TypeRef t) => IsInteger(t) || IsFloatingPoint(t);

    private bool IsBool(TypeRef t) => t.Handle is { } h && h == hBool;

    private bool IsVoidType(TypeRef t)
    {
        if (t.Handle is { } h)
        {
            if (h == hVoid)
                return true;
            if (context.GetTypeSymbol(h) is { } typeSym && typeSym.Name.Text.Equals("void", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static Scope GetOwnedScope(Symbol symbol) =>
        symbol.EnclosingScope.GetChildScope(ResolutionContext.GetHandle(symbol)) ?? symbol.EnclosingScope;

    private void ResolveTopLevelStatements()
    {
        foreach (var tree in context.SyntaxTrees)
        {
            if (tree.Root is CompilationUnit unit && unit.EnablesTopLevelStatements)
            {
                var scope = context.GetSyntaxScope(unit, context.GlobalScope);
                SymbolHandle? main = null;

                foreach (var function in context.FunctionSymbols)
                {
                    if (function.Syntax is null && ReferenceEquals(GetOwnedScope(function), scope))
                    {
                        main = ResolutionContext.GetHandle(function);
                        break;
                    }
                }

                ResolveTopLevelMembers(unit.Members, scope, main ?? default);
                CollapseRemainingLocals(scope, main ?? default);
            }
        }
    }

    private void ResolveTopLevelMembers(IReadOnlyList<TopLevel> members, Scope scope, SymbolHandle main)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case TopLevelBlockDeclaration block:
                    ResolveTopLevelMembers(block.Members, context.GetSyntaxScope(block, scope), main);
                    break;
                case NamespaceDeclaration ns when ns.Body is NamespaceBlockBody body:
                    ResolveTopLevelMembers(body.Members, context.GetSyntaxScope(body, scope), main);
                    break;
                case TopLevelVariableDeclaration varDecl:
                    ResolveVariableDeclaration(varDecl.Declaration, scope, main);
                    break;
                case TopLevelExpressionStatement exprStmt:
                    ResolveExpression(exprStmt.Expression, scope, main);
                    break;
                case TopLevelReturnStatement retStmt:
                    if (retStmt.Statement.Expression is { } retExpr)
                        ResolveExpression(retExpr, scope, main);
                    break;
            }
        }
    }

    private void ResolveFunctionBody(FunctionBody body, Scope scope, SymbolHandle containingFunction, TypeRef expectedReturnType)
    {
        if (body is FunctionBlockBody block)
        {
            foreach (var local in block.Locals)
                ResolveLocal(local, scope, containingFunction, expectedReturnType);
        }
        else if (body is FunctionLambdaBody lambda)
        {
            ResolveLocal(lambda.Statement, scope, containingFunction, expectedReturnType);
        }

        CollapseRemainingLocals(scope, containingFunction);
    }

    private void ResolveLocal(Local local, Scope scope, SymbolHandle containingFunction, TypeRef expectedReturnType)
    {
        switch (local)
        {
            case LocalExpressionStatement exprStmt:
                var t = ResolveExpression(exprStmt.Expression, scope, containingFunction);
                if (t.IsLiteral)
                {
                    var targetType = exprStmt.IsFinalExpression && !expectedReturnType.IsInferred && !expectedReturnType.IsUnresolved && !expectedReturnType.IsError
                        ? expectedReturnType
                        : CollapseLiteralToDefault(t);
                    t = CollapseExpression(exprStmt.Expression, targetType, scope, containingFunction);
                }
                if (exprStmt.IsFinalExpression && !expectedReturnType.IsInferred && !expectedReturnType.IsUnresolved && !expectedReturnType.IsError && !t.IsUnresolved && !t.IsError)
                {
                    if (!expectedReturnType.IsAssignableFrom(t, context))
                    {
                        context.Diagnostics.ReportReturnTypeMismatch(
                            exprStmt.Expression.GetSpan() ?? default,
                            t.ToDisplayString(context),
                            expectedReturnType.ToDisplayString(context),
                            exprStmt.GetSource());
                    }
                }
                break;

            case LocalReturnStatement returnStmt:
                ResolveReturnStatement(returnStmt, scope, containingFunction, expectedReturnType);
                break;

            case LocalVariableDeclarationStatement varStmt:
                ResolveVariableDeclaration(varStmt.Declaration, scope, containingFunction);
                break;

            case LocalGotoStatement:
                break;
        }
    }

    private void ResolveReturnStatement(LocalReturnStatement returnStmt, Scope scope, SymbolHandle containingFunction, TypeRef expectedReturnType)
    {
        TextSpan span;

        if (returnStmt.Statement.Expression is { } expr)
        {
            var returnedType = ResolveExpression(expr, scope, containingFunction);
            span = expr.GetSpan() ?? returnStmt.Statement.Keyword.Span;

            if (!expectedReturnType.IsInferred && !expectedReturnType.IsUnresolved && !expectedReturnType.IsError)
            {
                if (returnedType.IsLiteral)
                {
                    var collapsed = CollapseExpression(expr, expectedReturnType, scope, containingFunction);
                    if (collapsed.IsError)
                    {
                        context.Diagnostics.ReportReturnTypeMismatch(
                            span,
                            returnedType.ToDisplayString(context),
                            expectedReturnType.ToDisplayString(context),
                            returnStmt.GetSource());
                    }
                    returnedType = collapsed;
                }
                else if (!expectedReturnType.IsAssignableFrom(returnedType, context) &&
                         !TryResolveImplicitCast(returnedType, expectedReturnType, out _))
                {
                    context.Diagnostics.ReportReturnTypeMismatch(
                        span,
                        returnedType.ToDisplayString(context),
                        expectedReturnType.ToDisplayString(context),
                        returnStmt.GetSource());
                }
            }
        }
        else
        {
            // Bare return;
            span = returnStmt.Statement.Keyword.Span;

            if (!expectedReturnType.IsInferred && !expectedReturnType.IsUnresolved && !expectedReturnType.IsError)
            {
                if (!IsVoidType(expectedReturnType))
                {
                    context.Diagnostics.ReportReturnTypeMismatch(
                        span,
                        "void",
                        expectedReturnType.ToDisplayString(context),
                        returnStmt.GetSource());
                }
            }
        }
    }

    private TypeRef CollapseLiteralToDefault(TypeRef type)
    {
        if (type.IsLiteral && type.SpecialType is CompileTimeLiteralType lit)
        {
            return lit.LiteralKind switch
            {
                CompileTimeLiteralKind.Int => hInt32 is { } h ? TypeRef.Resolved(h) : TypeRef.Inferred,
                CompileTimeLiteralKind.Float => hFloat64 is { } h ? TypeRef.Resolved(h) : TypeRef.Inferred,
                CompileTimeLiteralKind.Str => hStr is { } h ? TypeRef.Resolved(h) : TypeRef.Inferred,
                CompileTimeLiteralKind.Char => hChar is { } h ? TypeRef.Resolved(h) : TypeRef.Inferred,
                _ => TypeRef.Inferred
            };
        }
        return type;
    }

    private void ResolveVariableDeclaration(VariableDeclaration declaration, Scope scope, SymbolHandle containingFunction)
    {
        foreach (var declarator in declaration.Declarators)
        {
            if (declarator.Identifier is TupleName tupleName)
            {
                // Tuple destructuring declaration: var (a, b) = expr;
                if (declarator.Initializer?.Initializer is { } initExpr)
                {
                    var initType = ResolveExpression(initExpr, scope, containingFunction);
                    if (initType.SpecialType is TupleSemanticType tupleSemantic)
                    {
                        if (tupleName.Elements.Count != tupleSemantic.Elements.Count)
                        {
                            context.Diagnostics.ReportTupleDestructureMismatch(
                                tupleName.GetSpan() ?? default,
                                $"expected {tupleName.Elements.Count} elements, but initializer provides {tupleSemantic.Elements.Count}",
                                declaration.GetSource());
                        }

                        for (int i = 0; i < tupleName.Elements.Count && i < tupleSemantic.Elements.Count; i++)
                        {
                            var elemName = context.GetScopedSymbolName(tupleName.Elements[i]).Last;
                            var elemSym = ResolveLocalOrFieldSymbol(scope, elemName);
                            if (elemSym is not null && GetVariableSymbolType(elemSym)?.IsInferred == true)
                            {
                                var elemType = CollapseLiteralToDefault(tupleSemantic.Elements[i].Type);
                                if (elemType.IsResolved || elemType.IsSpecial)
                                    SetVariableSymbolType(elemSym, elemType);
                            }
                        }
                    }
                    else if (!initType.IsError && !initType.IsInferred && !initType.IsUnresolved)
                    {
                        context.Diagnostics.ReportTupleDestructureMismatch(
                            declarator.Initializer.GetSpan() ?? default,
                            $"cannot destructure non-tuple expression of type '{initType.ToDisplayString(context)}'",
                            declaration.GetSource());
                    }
                }
            }
            else
            {
                var name = context.GetScopedSymbolName(declarator.Identifier).Last;
                var sym = ResolveLocalOrFieldSymbol(scope, name);
                var symType = sym is not null ? GetVariableSymbolType(sym) : null;

                if (declarator.Initializer?.Initializer is { } initExpr)
                {
                    var initType = ResolveExpression(initExpr, scope, containingFunction);
                    if (sym is not null && symType?.IsInferred == true)
                    {
                        if (initType.IsLiteral)
                        {
                            SetVariableSymbolType(sym, initType);
                            if (sym is LocalVariableSymbol localSym)
                                uncollapsedLocals[localSym] = initExpr;
                        }
                        else if (initType.IsResolved || initType.IsSpecial)
                        {
                            SetVariableSymbolType(sym, initType);
                        }
                    }
                    else if (sym is not null && symType is { } st && !st.IsInferred && !st.IsUnresolved && !st.IsError &&
                             !initType.IsInferred && !initType.IsUnresolved && !initType.IsError &&
                             initExpr is not CollectionExpression)
                    {
                        if (initType.IsLiteral)
                        {
                            var collapsed = CollapseExpression(initExpr, st, scope, containingFunction);
                            if (collapsed.IsError)
                            {
                                context.Diagnostics.ReportAssignmentTypeMismatch(
                                    initExpr.GetSpan() ?? default,
                                    initType.ToDisplayString(context),
                                    st.ToDisplayString(context),
                                    declaration.GetSource());
                            }
                        }
                        else
                        {
                            // Type check explicit variable initialization
                            if (!st.IsAssignableFrom(initType, context) &&
                                !TryResolveImplicitCast(initType, st, out _))
                            {
                                context.Diagnostics.ReportAssignmentTypeMismatch(
                                    initExpr.GetSpan() ?? default,
                                    initType.ToDisplayString(context),
                                    st.ToDisplayString(context),
                                    declaration.GetSource());
                            }
                        }
                    }
                }
                else if (sym is not null && symType?.IsInferred == true)
                {
                    // Inferred variable without initializer
                    context.Diagnostics.ReportCannotInferType(
                        declarator.Identifier.GetSpan() ?? default,
                        name.Text,
                        declaration.GetSource());
                }
            }
        }
    }

    private Symbol? ResolveLocalOrFieldSymbol(Scope scope, SymbolPart name)
    {
        var syms = scope[new SymbolName(name)];
        return syms.FirstOrDefault(s => s is LocalVariableSymbol or FieldSymbol or GlobalVariableSymbol);
    }

    private static void SetVariableSymbolType(Symbol sym, TypeRef type)
    {
        switch (sym)
        {
            case LocalVariableSymbol local: local.Type = type; break;
            case FieldSymbol field: field.Type = type; break;
            case GlobalVariableSymbol global: global.Type = type; break;
        }
    }

    private static TypeRef? GetVariableSymbolType(Symbol sym) => sym switch
    {
        LocalVariableSymbol local => local.Type,
        FieldSymbol field => field.Type,
        GlobalVariableSymbol global => global.Type,
        _ => null
    };

    private TypeRef ResolveExpression(Expression? expr, Scope scope, SymbolHandle containingFunction)
    {
        if (expr is null)
            return TypeRef.Unresolved;

        TypeRef result = TypeRef.Unresolved;

        switch (expr)
        {
            case LiteralExpression lit:
                result = ResolveLiteral(lit);
                break;

            case NamedExpression named:
                result = ResolveNamed(named, scope, containingFunction);
                break;

            case ParenthesizedExpression paren:
                result = ResolveExpression(paren.Expression, scope, containingFunction);
                break;

            case TupleExpression tuple:
                var elements = new List<TupleElement>(tuple.Arguments.Count);
                for (int i = 0; i < tuple.Arguments.Count; i++)
                {
                    var argType = ResolveExpression(tuple.Arguments[i], scope, containingFunction);
                    elements.Add(new TupleElement(argType, null));
                }
                result = TypeRef.Tuple(elements);
                break;

            case BinaryExpression binary:
                result = ResolveBinary(binary, scope, containingFunction);
                break;

            case UnaryExpression unary:
                result = ResolveUnary(unary, scope, containingFunction);
                break;

            case AssignmentExpression assignment:
                result = ResolveAssignment(assignment, scope, containingFunction);
                break;

            case CallExpression call:
                result = ResolveCall(call, scope, containingFunction);
                break;

            case MemberAccessExpression access:
                result = ResolveMemberAccess(access, scope, containingFunction);
                break;

            case IndexExpression index:
                result = ResolveIndex(index, scope, containingFunction);
                break;

            case IfExpression conditional:
                result = ResolveIf(conditional, scope, containingFunction);
                break;

            case WhileExpression loop:
                result = ResolveWhile(loop, scope, containingFunction);
                break;

            case BlockExpression block:
                result = ResolveBlock(block, scope, containingFunction);
                break;

            case MatchExpression match:
                result = ResolveMatch(match, scope, containingFunction);
                break;

            case ConstructorCallExpression constructorCall:
                result = ResolveConstructorCall(constructorCall, scope, containingFunction);
                break;

            case TypeAsExpression typeAs:
                result = ResolveTypeAs(typeAs, scope);
                break;

            case AsExpression asExpr:
                result = ResolveAs(asExpr, scope);
                break;

            case ArrayCreationExpression arrayCreation:
                var elemType = ResolveTypeSyntax(arrayCreation.Type, scope);
                ResolveExpression(arrayCreation.Size, scope, containingFunction);
                result = TypeRef.Array(elemType);
                break;

            case CollectionExpression collection:
                result = ResolveCollection(collection, scope, containingFunction);
                break;

            case VariantCreationExpression variantCreation:
                result = ResolveExpression(variantCreation.Target, scope, containingFunction);
                break;

            case NameofExpression nameofExpr:
                ResolveExpression(nameofExpr.Argument, scope, containingFunction);
                result = hStr is { } hs ? TypeRef.Resolved(hs) : TypeRef.Unresolved;
                break;

            default:
                break;
        }

        context.SetExpressionType(expr, result);
        return result;
    }

    private TypeRef ResolveLiteral(LiteralExpression lit)
    {
        if (lit.Literal.MatchingKind is MatchingKeywordKind.True or MatchingKeywordKind.False)
            return boolType;

        if (lit.HasSuffix)
        {
            var suffix = lit.Literal.GetSuffixSpan().ToString();

            // 1. Check for literal functions declared on types or at file level
            foreach (var m in context.MethodSymbols)
            {
                if (m.Flags.HasFlag(FunctionFlags.Literal) && m.Name.Text == suffix)
                {
                    context.ResolvedTree.AddReference(lit, ResolutionContext.GetHandle(m));
                    return m.ReturnType;
                }
            }

            foreach (var fn in context.FunctionSymbols)
            {
                if (fn.Flags.HasFlag(FunctionFlags.Literal) && fn.Name.Text == suffix)
                {
                    context.ResolvedTree.AddReference(lit, ResolutionContext.GetHandle(fn));
                    return fn.ReturnType;
                }
            }

            return TypeRef.Unresolved;
        }

        return lit.Literal.Kind switch
        {
            TokenKind.Integer => TypeRef.Literal(CompileTimeLiteralKind.Int, lit.Literal.Value),
            TokenKind.Float => TypeRef.Literal(CompileTimeLiteralKind.Float, lit.Literal.Value),
            TokenKind.String => TypeRef.Literal(CompileTimeLiteralKind.Str, lit.Literal.Value),
            TokenKind.Char => TypeRef.Literal(CompileTimeLiteralKind.Char, lit.Literal.Value),
            _ => TypeRef.Unresolved
        };
    }

    private List<MethodSymbol> GetMatchingLiteralMethods(TypeRef targetType, CompileTimeLiteralKind kind)
    {
        var result = new List<MethodSymbol>();
        if (targetType.Handle is not { } handle || context.GetTypeSymbol(handle) is not { } typeSym)
            return result;

        var scope = GetOwnedScope(typeSym);
        foreach (var sym in scope.Symbols.Values)
        {
            if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.Literal))
            {
                result.Add(method);
            }
        }
        return result;
    }

    private TypeRef ResolveNamed(NamedExpression named, Scope scope, SymbolHandle containingFunction = default)
    {
        if (named.Identifier.MatchingKind is MatchingKeywordKind.True or MatchingKeywordKind.False)
            return boolType;

        if (named.Identifier.MatchingKind is MatchingKeywordKind.This)
        {
            if (context.GetSymbol(containingFunction) is MethodSymbol m && !m.Flags.HasFlag(FunctionFlags.Static) && m.Parent is { } parentHandle)
            {
                context.ResolvedTree.AddReference(named, parentHandle);
                return TypeRef.Resolved(parentHandle);
            }
            return TypeRef.Unresolved;
        }

        var hygiene = context.GetHygiene(named);
        var part = named is GenericNameExpression generic
            ? new SymbolPart(generic.Identifier, generic.GenericArguments.Count, hygiene)
            : new SymbolPart(named.Identifier, 0, hygiene);

        var syms = scope[new SymbolName(part)];
        if (syms.Count > 0)
        {
            var sym = syms[0];
            context.ResolvedTree.AddReference(named, ResolutionContext.GetHandle(sym));
            return GetTypeFromSymbol(sym);
        }

        if (context.ResolvedTree.TryGetReference(named, out var handle))
            return GetTypeFromHandle(handle);

        return TypeRef.Unresolved;
    }

    private TypeRef GetTypeFromSymbol(Symbol sym) => sym switch
    {
        LocalVariableSymbol local => local.Type,
        FieldSymbol field => field.Type,
        ParameterSymbol param => param.Type,
        GlobalVariableSymbol global => global.Type,
        TypeSymbol typeSymbol => TypeRef.Resolved(ResolutionContext.GetHandle(typeSymbol)),
        MethodSymbol method => method.ReturnType,
        FunctionSymbol fn => fn.ReturnType,
        PropertySymbol prop => prop.Type,
        _ => TypeRef.Unresolved
    };

    private TypeRef GetTypeFromHandle(SymbolHandle handle)
    {
        var sym = context.GetSymbol(handle);
        return sym is not null ? GetTypeFromSymbol(sym) : TypeRef.Resolved(handle);
    }

    private TypeRef ResolveBinary(BinaryExpression binary, Scope scope, SymbolHandle containingFunction)
    {
        var lhs = ResolveExpression(binary.LeftExpression, scope, containingFunction);
        var rhs = ResolveExpression(binary.RightExpression, scope, containingFunction);

        var opKind = GetBinaryOperatorKind(binary.OperatorToken.Kind);
        if (opKind == OperatorKind.None)
            return TypeRef.Error;

        // 1. Uncollapsed literals on both sides
        if (lhs.IsLiteral && rhs.IsLiteral)
        {
            var lLit = lhs.AsLiteral!;
            var rLit = rhs.AsLiteral!;
            if (lLit.LiteralKind == rLit.LiteralKind)
            {
                if (opKind is OperatorKind.Equality or OperatorKind.Inequality or
                    OperatorKind.LessThan or OperatorKind.LessThanOrEqual or
                    OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual)
                {
                    return boolType;
                }

                return TypeRef.Literal(lLit.LiteralKind, "");
            }
        }

        // 2. Canonical bool operations
        if ((opKind == OperatorKind.LogicalAnd || opKind == OperatorKind.LogicalOr) && IsBool(lhs) && IsBool(rhs))
            return boolType;

        // 3. One literal operand and one concrete operand (e.g. 10 + myInt64)
        if (lhs.IsLiteral && !rhs.IsLiteral && !rhs.IsUnresolved && !rhs.IsError)
        {
            if (TryResolveOneLiteralBinary(opKind, binary, binary.LeftExpression, lhs, rhs, isLhsLiteral: true, scope, containingFunction, out var res))
                return res;
        }
        else if (rhs.IsLiteral && !lhs.IsLiteral && !lhs.IsUnresolved && !lhs.IsError)
        {
            if (TryResolveOneLiteralBinary(opKind, binary, binary.RightExpression, rhs, lhs, isLhsLiteral: false, scope, containingFunction, out var res))
                return res;
        }

        // 4. Operator overload lookup through DiscoveredOperators for concrete operands
        if (TryResolveOperatorOverload(opKind, lhs, rhs, binary, out var overloadResult))
            return overloadResult;

        // 5. Neither overload found: report MH2001
        if (!lhs.IsUnresolved && !lhs.IsError && !lhs.IsInferred && !rhs.IsUnresolved && !rhs.IsError && !rhs.IsInferred)
        {
            context.Diagnostics.ReportTypeMismatch(
                binary.OperatorToken.Span,
                binary.OperatorToken.Value,
                binary.LeftExpression.GetSpan() ?? default,
                lhs.ToDisplayString(context),
                binary.RightExpression.GetSpan() ?? default,
                rhs.ToDisplayString(context),
                source: binary.GetSource());
        }

        return TypeRef.Error;
    }

    private bool TryResolveOneLiteralBinary(
        OperatorKind kind,
        BinaryExpression binary,
        Expression literalExpr,
        TypeRef literalType,
        TypeRef concreteType,
        bool isLhsLiteral,
        Scope scope,
        SymbolHandle containingFunction,
        out TypeRef result)
    {
        result = TypeRef.Unresolved;
        if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
            return false;

        if (concreteType.Handle is not { } cHandle || context.GetTypeSymbol(cHandle) is not { } cTypeSym)
            return false;

        var typeScope = GetOwnedScope(cTypeSym);
        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null) continue;

            foreach (var sym in typeScope.Symbols.Values)
            {
                if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                {
                    if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                    {
                        if (method.Parameters.Count == 2)
                        {
                            var p0 = context.GetParameterSymbol(method.Parameters[0]);
                            var p1 = context.GetParameterSymbol(method.Parameters[1]);
                            if (p0 is null || p1 is null) continue;

                            var litParam = isLhsLiteral ? p0.Type : p1.Type;
                            var concParam = isLhsLiteral ? p1.Type : p0.Type;

                            if (concParam.IsAssignableFrom(concreteType, context) && IsLiteralCompatibleWith(literalType, litParam))
                            {
                                CollapseExpression(literalExpr, litParam, scope, containingFunction);
                                context.ResolvedTree.AddReference(binary, ResolutionContext.GetHandle(method));
                                result = method.ReturnType;
                                return true;
                            }
                        }
                    }
                }
            }
        }
        return false;
    }

    private static OperatorKind GetBinaryOperatorKind(TokenKind tokenKind) => tokenKind switch
    {
        TokenKind.Plus => OperatorKind.Addition,
        TokenKind.Minus => OperatorKind.Subtraction,
        TokenKind.Asterisk => OperatorKind.Multiplication,
        TokenKind.ForwardSlash => OperatorKind.Division,
        TokenKind.Percentage => OperatorKind.Remainder,
        TokenKind.Ampersand => OperatorKind.BitwiseAnd,
        TokenKind.VerticalBar => OperatorKind.BitwiseOr,
        TokenKind.Caret => OperatorKind.BitwiseXor,
        TokenKind.LessThanLessThanSigns => OperatorKind.LeftShift,
        TokenKind.GreaterThanGreaterThanSigns => OperatorKind.RightShift,
        TokenKind.LessThanLessThanLessThanSigns => OperatorKind.RotateLeft,
        TokenKind.EqualsEquals => OperatorKind.Equality,
        TokenKind.ExclamationEquals => OperatorKind.Inequality,
        TokenKind.LessThanSign => OperatorKind.LessThan,
        TokenKind.LessThanEquals => OperatorKind.LessThanOrEqual,
        TokenKind.GreaterThanSign => OperatorKind.GreaterThan,
        TokenKind.GreaterThanEquals => OperatorKind.GreaterThanOrEqual,
        TokenKind.AmpersandAmpersand => OperatorKind.LogicalAnd,
        TokenKind.VerticalBarVerticalBar => OperatorKind.LogicalOr,
        _ => OperatorKind.None
    };

    private bool TryResolveOperatorOverload(
        OperatorKind kind,
        TypeRef lhs,
        TypeRef rhs,
        SyntaxNode syntax,
        out TypeRef result)
    {
        result = TypeRef.Unresolved;

        if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
            return false;

        // Operators must resolve to explicit Interface.Operator methods on operand type (LHS or RHS)
        var operandHandles = new List<SymbolHandle>();
        if (lhs.Handle is { } lh) operandHandles.Add(lh);
        if (rhs.Handle is { } rh && !operandHandles.Contains(rh)) operandHandles.Add(rh);

        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null)
                continue;

            foreach (var opHandle in operandHandles)
            {
                var operandTypeSym = context.GetTypeSymbol(opHandle);
                if (operandTypeSym is null)
                    continue;

                var scope = GetOwnedScope(operandTypeSym);
                foreach (var sym in scope.Symbols.Values)
                {
                    if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                    {
                        if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                        {
                            // Verify parameter types match operands
                            if (method.Parameters.Count == 2)
                            {
                                var p0 = context.GetParameterSymbol(method.Parameters[0]);
                                var p1 = context.GetParameterSymbol(method.Parameters[1]);

                                if (p0 is not null && p1 is not null &&
                                    p0.Type.IsAssignableFrom(lhs, context) &&
                                    p1.Type.IsAssignableFrom(rhs, context))
                                {
                                    context.ResolvedTree.AddReference(syntax, ResolutionContext.GetHandle(method));
                                    result = method.ReturnType;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    private TypeRef ResolveUnary(UnaryExpression unary, Scope scope, SymbolHandle containingFunction)
    {
        var operand = ResolveExpression(unary.Operand, scope, containingFunction);
        var opKind = GetUnaryOperatorKind(unary.OperatorToken.Kind, unary.Position);

        if (opKind == OperatorKind.None)
            return TypeRef.Error;

        // 1. Built-in language pointer/reference operations
        if (opKind == OperatorKind.AddressOf)
            return TypeRef.Reference(operand);

        if (opKind == OperatorKind.Dereference)
        {
            if (operand.SpecialType is ReferenceSemanticType refType)
                return refType.ElementType;
            if (operand.SpecialType is PointerSemanticType ptrType)
                return ptrType.ElementType;
            return TypeRef.Error;
        }

        // 2. Canonical bool
        if (opKind == OperatorKind.LogicalNot && IsBool(operand))
            return boolType;

        // 3. Uncollapsed literal unary
        if (operand.IsLiteral)
            return operand;

        // 4. Overload lookup through DiscoveredOperators
        if (TryResolveUnaryOverload(opKind, operand, unary, out var overloadResult))
            return overloadResult;

        if (!operand.IsUnresolved && !operand.IsError && !operand.IsInferred)
        {
            context.Diagnostics.ReportUnaryTypeMismatch(
                unary.OperatorToken.Span,
                unary.OperatorToken.Value,
                unary.Operand.GetSpan() ?? default,
                operand.ToDisplayString(context),
                source: unary.GetSource());
        }

        return TypeRef.Error;
    }

    private static OperatorKind GetUnaryOperatorKind(TokenKind tokenKind, UnaryPosition position) => tokenKind switch
    {
        TokenKind.Minus => OperatorKind.UnaryNegation,
        TokenKind.Plus => OperatorKind.UnaryPositive,
        TokenKind.ExclamationMark => OperatorKind.LogicalNot,
        TokenKind.Tilde => OperatorKind.BitwiseNot,
        TokenKind.PlusPlus => position == UnaryPosition.Prefix ? OperatorKind.PrefixIncrement : OperatorKind.PostfixIncrement,
        TokenKind.MinusMinus => position == UnaryPosition.Prefix ? OperatorKind.PrefixDecrement : OperatorKind.PostfixDecrement,
        TokenKind.Ampersand => OperatorKind.AddressOf,
        TokenKind.Asterisk => OperatorKind.Dereference,
        _ => OperatorKind.None
    };

    private bool TryResolveUnaryOverload(OperatorKind kind, TypeRef operand, SyntaxNode syntax, out TypeRef result)
    {
        result = TypeRef.Unresolved;

        if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
            return false;

        if (operand.Handle is not { } opHandle)
            return false;

        var operandTypeSym = context.GetTypeSymbol(opHandle);
        if (operandTypeSym is null)
            return false;

        var scope = GetOwnedScope(operandTypeSym);
        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null)
                continue;

            foreach (var sym in scope.Symbols.Values)
            {
                if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                {
                    if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                    {
                        // Check if instance method on receiver or static unary accepting operand
                        if (method.Parameters.Count == 0 || (method.Parameters.Count == 1 && context.GetParameterSymbol(method.Parameters[0]) is { } p && p.Type.IsAssignableFrom(operand, context)))
                        {
                            context.ResolvedTree.AddReference(syntax, ResolutionContext.GetHandle(method));
                            result = method.ReturnType;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private TypeRef ResolveAssignment(AssignmentExpression assignment, Scope scope, SymbolHandle containingFunction)
    {
        var lhs = ResolveExpression(assignment.LhsExpression, scope, containingFunction);
        var rhs = ResolveExpression(assignment.RhsExpression, scope, containingFunction);

        // Verify LValue
        if (!IsLValue(assignment.LhsExpression))
        {
            context.Diagnostics.ReportCannotAssignToNonLValue(assignment.LhsExpression.GetSpan() ?? default, assignment.GetSource());
            return TypeRef.Error;
        }

        var opToken = assignment.EqualsOperator.Kind;
        if (opToken == TokenKind.Equals)
        {
            if (rhs.IsLiteral && !lhs.IsLiteral && !lhs.IsError && !lhs.IsUnresolved && !lhs.IsInferred)
            {
                var collapsed = CollapseExpression(assignment.RhsExpression, lhs, scope, containingFunction);
                return collapsed.IsError ? TypeRef.Error : lhs;
            }

            if (!lhs.IsAssignableFrom(rhs, context) &&
                !TryResolveImplicitCast(rhs, lhs, out _) &&
                !lhs.IsError && !lhs.IsUnresolved && !lhs.IsInferred &&
                !rhs.IsError && !rhs.IsUnresolved && !rhs.IsInferred &&
                assignment.RhsExpression is not CollectionExpression)
            {
                context.Diagnostics.ReportAssignmentTypeMismatch(
                    assignment.EqualsOperator.Span,
                    rhs.ToDisplayString(context),
                    lhs.ToDisplayString(context),
                    assignment.GetSource());
            }

            return lhs;
        }

        // Compound assignments (+=, -=, etc.)
        return lhs;
    }

    private static bool IsLValue(Expression expr) => expr switch
    {
        NamedExpression => true,
        IndexExpression => true,
        MemberAccessExpression => true,
        UnaryExpression u when u.OperatorToken.Kind == TokenKind.Asterisk => true,
        _ => false
    };

    private TypeRef ResolveCall(CallExpression call, Scope scope, SymbolHandle containingFunction)
    {
        if (call.Callee is MemberAccessExpression access)
        {
            var recvType = ResolveExpression(access.Expression, scope, containingFunction);
            if (recvType.IsLiteral)
            {
                return recvType;
            }
        }

        var calleeType = ResolveExpression(call.Callee, scope, containingFunction);
        var argTypes = new List<TypeRef>(call.Arguments.Count);
        foreach (var arg in call.Arguments)
            argTypes.Add(ResolveExpression(arg, scope, containingFunction));

        if (context.ResolvedTree.TryGetReference(call.Callee, out var calleeHandle))
        {
            var sym = context.GetSymbol(calleeHandle);
            if (sym is FunctionSymbol fn)
                return TypeCheckCall(call, fn.Parameters, fn.ReturnType, argTypes, scope, containingFunction);
            if (sym is MethodSymbol m)
                return TypeCheckCall(call, m.Parameters, m.ReturnType, argTypes, scope, containingFunction);
        }

        // Check if callee is an instance invoking Callable operator:
        if (TryResolveCallableOperator(call, calleeType, argTypes, out var callableReturn))
            return callableReturn;

        if (!calleeType.IsError && !calleeType.IsUnresolved && !calleeType.IsInferred)
        {
            context.Diagnostics.ReportTypeNotCallable(call.Callee.GetSpan() ?? default, calleeType.ToDisplayString(context), call.GetSource());
            return TypeRef.Error;
        }

        return calleeType;
    }

    private bool TryResolveCallableOperator(CallExpression call, TypeRef calleeType, List<TypeRef> argTypes, out TypeRef result)
    {
        result = TypeRef.Unresolved;
        if (calleeType.Handle is not { } typeHandle || context.GetTypeSymbol(typeHandle) is not { } typeSym)
            return false;

        if (!context.DiscoveredOperators.TryGetOperators(OperatorKind.Callable, out var candidates))
            return false;

        var typeScope = GetOwnedScope(typeSym);
        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null) continue;

            foreach (var sym in typeScope.Symbols.Values)
            {
                if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                {
                    if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                    {
                        if (method.Parameters.Count == argTypes.Count)
                        {
                            bool matches = true;
                            for (int i = 0; i < argTypes.Count; i++)
                            {
                                if (context.GetParameterSymbol(method.Parameters[i]) is { } param &&
                                    !param.Type.IsAssignableFrom(argTypes[i], context))
                                {
                                    matches = false;
                                    break;
                                }
                            }
                            if (matches)
                            {
                                context.ResolvedTree.AddReference(call, ResolutionContext.GetHandle(method));
                                result = method.ReturnType;
                                return true;
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    private static bool IsVariadicParameter(ParameterSymbol p) =>
        p.Syntax?.Declarator.Type is ModifiedType m && m.Modifier is VariadicTypeModifier;

    private static bool HasDefaultValue(ParameterSymbol p) =>
        p.Syntax?.Initializer is not null;

    private TypeRef TypeCheckCall(CallExpression call, IReadOnlyList<SymbolHandle> paramHandles, TypeRef returnType, List<TypeRef> argTypes, Scope scope, SymbolHandle containingFunction)
    {
        var expectedParams = new List<ParameterSymbol>();
        bool isVariadic = false;
        foreach (var ph in paramHandles)
        {
            if (context.GetParameterSymbol(ph) is { } param)
            {
                expectedParams.Add(param);
                if (IsVariadicParameter(param)) isVariadic = true;
            }
        }

        int requiredCount = expectedParams.Count(p => !IsVariadicParameter(p) && !HasDefaultValue(p));
        int maxCount = isVariadic ? int.MaxValue : expectedParams.Count;

        if (argTypes.Count < requiredCount || argTypes.Count > maxCount)
        {
            context.Diagnostics.ReportArgumentCountMismatch(
                call.GetSpan() ?? default,
                expectedParams.Count,
                argTypes.Count,
                call.GetSource());
        }

        for (int i = 0; i < argTypes.Count && i < expectedParams.Count; i++)
        {
            var param = expectedParams[i];
            if (argTypes[i].IsLiteral)
            {
                CollapseExpression(call.Arguments[i], param.Type, scope, containingFunction);
                argTypes[i] = param.Type;
            }
            else if (!param.Type.IsAssignableFrom(argTypes[i], context) &&
                !TryResolveImplicitCast(argTypes[i], param.Type, out _) &&
                !argTypes[i].IsError && !argTypes[i].IsUnresolved && !argTypes[i].IsInferred &&
                !param.Type.IsError && !param.Type.IsUnresolved && !param.Type.IsInferred)
            {
                context.Diagnostics.ReportArgumentTypeMismatch(
                    call.Arguments[i].GetSpan() ?? default,
                    i + 1,
                    argTypes[i].ToDisplayString(context),
                    param.Type.ToDisplayString(context),
                    call.GetSource());
            }
        }

        return returnType;
    }

    private TypeRef ResolveMemberAccess(MemberAccessExpression access, Scope scope, SymbolHandle containingFunction)
    {
        if (context.ResolvedTree.TryGetReference(access, out var existingRef))
        {
            var existingSym = context.GetSymbol(existingRef);
            if (existingSym is not null)
                return GetTypeFromSymbol(existingSym);
        }

        var receiverType = ResolveExpression(access.Expression, scope, containingFunction);
        var memberName = access.Identifier.Value;

        if (receiverType.IsLiteral)
        {
            return receiverType;
        }

        // Check if access.Expression is a TypeSymbol (static access / enum variant)
        bool isStaticAccessOnType = access.Expression is TypeAsExpression;
        if (!isStaticAccessOnType && access.Expression is NamedExpression namedExpr)
        {
            if (context.ResolvedTree.TryGetReference(namedExpr, out var namedH) && context.GetSymbol(namedH) is TypeSymbol)
                isStaticAccessOnType = true;
            else if (receiverType.Kind == TypeRefKind.Resolved && receiverType.Handle is { } rh && context.GetTypeSymbol(rh) is { } ts && namedExpr.Identifier.Value == ts.Name.Text)
                isStaticAccessOnType = true;
        }

        // Untagged union and sum type member checks (instance members only!):
        if (!isStaticAccessOnType)
        {
            var unwrapped = receiverType;
            bool isUntagged = unwrapped.IsUntagged;
            ISumTypeSymbol? sumType = null;
            UnionSemanticType? unionSemantic = unwrapped.SpecialType as UnionSemanticType;

            if (unwrapped.Kind == TypeRefKind.Untagged && unwrapped.SpecialType is UntaggedSemanticType untagged)
                unwrapped = untagged.InnerType;

            if (unwrapped.Handle is { } handle && context.GetTypeSymbol(handle) is ISumTypeSymbol s)
                sumType = s;

            if (sumType is not null || unionSemantic is not null)
            {
                MethodSymbol? declaredMethod = null;
                if (sumType is not null)
                {
                    foreach (var methodHandle in sumType.Methods)
                    {
                        if (context.GetMethodSymbol(methodHandle) is { } m && m.Name.Text == memberName)
                        {
                            declaredMethod = m;
                            break;
                        }
                    }
                }

                if (declaredMethod is not null)
                {
                    // MH0608: Calling ordinary instance method on untagged sum type
                    if (isUntagged && !declaredMethod.Flags.HasFlag(FunctionFlags.Untagged))
                    {
                        context.Diagnostics.ReportUntaggedMethodMismatch(
                            access.Identifier.Span,
                            memberName,
                            receiverType.ToDisplayString(context),
                            access.GetSource());
                    }

                    context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(declaredMethod));
                    return declaredMethod.ReturnType;
                }

                // MH0609: Calling variant-level method directly on sum type instance
                bool foundOnVariant = false;
                if (sumType is not null)
                {
                    if (sumType.UnionConstituents.Count > 0)
                    {
                        foreach (var constituent in sumType.UnionConstituents)
                        {
                            if (constituent.Handle is { } ch && context.GetTypeSymbol(ch) is { } cTypeSym)
                            {
                                var cScope = GetOwnedScope(cTypeSym);
                                if (cScope[new SymbolName(new SymbolPart(memberName))].Count > 0)
                                {
                                    foundOnVariant = true;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (var vHandle in sumType.EnumVariants)
                        {
                            if (context.GetEnumVariantSymbol(vHandle) is { } vSym)
                            {
                                var vScope = GetOwnedScope(vSym);
                                if (vScope[new SymbolName(new SymbolPart(memberName))].Count > 0)
                                {
                                    foundOnVariant = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                else if (unionSemantic is not null)
                {
                    foreach (var constituent in unionSemantic.Variants)
                    {
                        if (constituent.Handle is { } ch && context.GetTypeSymbol(ch) is { } cTypeSym)
                        {
                            var cScope = GetOwnedScope(cTypeSym);
                            if (cScope[new SymbolName(new SymbolPart(memberName))].Count > 0)
                            {
                                foundOnVariant = true;
                                break;
                            }
                        }
                    }
                }

                if (foundOnVariant)
                {
                    context.Diagnostics.ReportDirectVariantMethodInvocation(
                        access.Identifier.Span,
                        memberName,
                        receiverType.ToDisplayString(context),
                        access.GetSource());
                }
            }
        }

        // Look up member on receiver type (filtering out InterfaceScoped methods)
        if (receiverType.Handle is { } recvHandle)
        {
            var typeSymObj = (Symbol?)context.GetTypeSymbol(recvHandle) ?? context.GetSymbol(recvHandle);
            if (typeSymObj is not null)
            {
                var recvScope = GetOwnedScope(typeSymObj);
                var syms = recvScope[new SymbolName(new SymbolPart(memberName))];
                bool isExplicitInterfaceAccess = access.Expression is TypeAsExpression;
                var accessible = syms.Where(cand =>
                {
                    if (cand is MethodSymbol m)
                    {
                        if (isExplicitInterfaceAccess)
                            return true;
                        return !m.Flags.HasFlag(FunctionFlags.InterfaceScoped);
                    }
                    return !isExplicitInterfaceAccess;
                }).ToList();
                if (accessible.Count > 0)
                {
                    context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(accessible[0]));
                    return GetTypeFromSymbol(accessible[0]);
                }

                if (!isStaticAccessOnType)
                {
                    context.Diagnostics.ReportMemberNotFound(
                        access.Identifier.Span,
                        memberName,
                        receiverType.ToDisplayString(context),
                        access.GetSource());
                    return TypeRef.Error;
                }
            }
        }

        return TypeRef.Unresolved;
    }

    private TypeRef ResolveIndex(IndexExpression index, Scope scope, SymbolHandle containingFunction)
    {
        var targetType = ResolveExpression(index.Expression, scope, containingFunction);
        ResolveExpression(index.Index, scope, containingFunction);

        if (targetType.SpecialType is ArraySemanticType arraySemantic)
            return arraySemantic.ElementType;
        if (targetType.SpecialType is SpanSemanticType spanSemantic)
            return spanSemantic.ElementType;
        if (targetType.SpecialType is PointerSemanticType ptrSemantic)
            return ptrSemantic.ElementType;

        // User-defined indexer
        if (TryResolveIndexer(targetType, index, out var indexerResult))
            return indexerResult;

        return TypeRef.Unresolved;
    }

    private bool TryResolveIndexer(TypeRef targetType, IndexExpression index, out TypeRef result)
    {
        result = TypeRef.Unresolved;

        if (targetType.Handle is not { } handle)
            return false;

        var typeSym = context.GetTypeSymbol(handle);
        if (typeSym is null)
            return false;

        var scope = GetOwnedScope(typeSym);
        foreach (var sym in scope.Symbols.Values)
        {
            if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
            {
                if (context.DiscoveredOperators.TryGetOperators(OperatorKind.IndexerGetter, out var candidates))
                {
                    if (candidates.Any(c => method.ExplicitInterfaceName?.Last.Equals(context.GetTypeSymbol(c.InterfaceHandle)?.Name) == true ||
                                            method.ExplicitInterfaceName?.Last.Text == context.GetTypeSymbol(c.InterfaceHandle)?.Name.Text))
                    {
                        context.ResolvedTree.AddReference(index, ResolutionContext.GetHandle(method));
                        result = method.ReturnType;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private TypeRef ResolveIf(IfExpression conditional, Scope scope, SymbolHandle containingFunction)
    {
        Scope ifScope = conditional.Declaration is not null ? context.GetSyntaxScope(conditional, scope) : scope;
        if (conditional.Declaration is not null)
            ResolveVariableDeclaration(conditional.Declaration, ifScope, containingFunction);

        var condType = ResolveExpression(conditional.Condition, ifScope, containingFunction);
        if (!IsBool(condType) && !condType.IsError && !condType.IsUnresolved)
        {
            context.Diagnostics.ReportConditionTypeMismatch(
                conditional.Condition.GetSpan() ?? default,
                condType.ToDisplayString(context),
                conditional.GetSource());
        }

        var thenType = ResolveExpression(conditional.ThenExpression, ifScope, containingFunction);
        if (conditional.ElseExpression is null)
            return voidType;

        var elseType = ResolveExpression(conditional.ElseExpression.Expression, ifScope, containingFunction);

        if (thenType.IsError || elseType.IsError)
            return TypeRef.Error;
        if (thenType.IsUnresolved || elseType.IsUnresolved)
            return TypeRef.Unresolved;

        // Strict exact matching:
        if (thenType.Equals(elseType))
            return thenType;

        if (thenType.IsLiteral && elseType.IsLiteral &&
            thenType.SpecialType is CompileTimeLiteralType l1 &&
            elseType.SpecialType is CompileTimeLiteralType l2 &&
            l1.LiteralKind == l2.LiteralKind)
        {
            return thenType;
        }

        if (thenType.IsLiteral && !elseType.IsLiteral && elseType.IsAssignableFrom(thenType, context))
            return elseType;

        if (!thenType.IsLiteral && elseType.IsLiteral && thenType.IsAssignableFrom(elseType, context))
            return thenType;

        // Branches mismatch! Do not widen to common base / Object!
        context.Diagnostics.ReportAmbiguousConditionalBranches(
            conditional.GetSpan() ?? default,
            thenType.ToDisplayString(context),
            elseType.ToDisplayString(context),
            conditional.GetSource());

        return TypeRef.Error;
    }

    private TypeRef ResolveWhile(WhileExpression loop, Scope scope, SymbolHandle containingFunction)
    {
        Scope whileScope = loop.Declaration is not null ? context.GetSyntaxScope(loop, scope) : scope;
        if (loop.Declaration is not null)
            ResolveVariableDeclaration(loop.Declaration, whileScope, containingFunction);

        var condType = ResolveExpression(loop.Condition, whileScope, containingFunction);
        if (!IsBool(condType) && !condType.IsError && !condType.IsUnresolved)
        {
            context.Diagnostics.ReportConditionTypeMismatch(
                loop.Condition.GetSpan() ?? default,
                condType.ToDisplayString(context),
                loop.GetSource());
        }

        ResolveExpression(loop.Body, whileScope, containingFunction);
        return voidType;
    }

    private TypeRef ResolveBlock(BlockExpression block, Scope scope, SymbolHandle containingFunction)
    {
        var blockScope = context.GetSyntaxScope(block, scope);
        var expectedReturn = GetFunctionReturnType(containingFunction);
        foreach (var local in block.Locals)
            ResolveLocal(local, blockScope, containingFunction, expectedReturn);

        TypeRef result = voidType;
        if (block.FinalExpression is { } finalExpr)
            result = ResolveExpression(finalExpr, blockScope, containingFunction);

        CollapseRemainingLocals(blockScope, containingFunction);

        return result;
    }

    private TypeRef GetFunctionReturnType(SymbolHandle containingFunction)
    {
        var sym = context.GetSymbol(containingFunction);
        return sym switch
        {
            FunctionSymbol fn => fn.ReturnType,
            MethodSymbol m => m.ReturnType,
            PropertySymbol p => p.Type,
            _ => voidType
        };
    }

    private TypeRef ResolveMatch(MatchExpression match, Scope scope, SymbolHandle containingFunction)
    {
        var governingType = ResolveExpression(match.Expression, scope, containingFunction);

        // MH0606: Untagged sum type match without discriminant
        bool isUntaggedSumType = false;
        if (governingType.IsUntagged)
            isUntaggedSumType = true;
        else if (governingType.Handle is { } h && context.GetTypeSymbol(h) is { } ts && ts.Flags.HasFlag(TypeFlags.Untagged))
            isUntaggedSumType = true;

        if (isUntaggedSumType)
        {
            context.Diagnostics.ReportUntaggedMatchDisallowed(
                match.Expression.GetSpan() ?? default,
                governingType.ToDisplayString(context),
                match.GetSource());
        }

        ISumTypeSymbol? sumTypeSym = null;
        TypeSymbol? typeSym = null;
        UnionSemanticType? unionType = null;

        if (governingType.SpecialType is UnionSemanticType u)
            unionType = u;
        else if (governingType.Handle is { } gh)
        {
            typeSym = context.GetTypeSymbol(gh);
            sumTypeSym = typeSym as ISumTypeSymbol;
        }

        bool hasWildcard = false;
        var matchedVariants = new HashSet<string>();
        var matchedTypes = new HashSet<TypeRef>();
        TypeRef armResultType = voidType;

        foreach (var arm in match.Arms)
        {
            var armScope = context.GetSyntaxScope(arm, scope);
            ValidatePattern(arm.Pattern, armScope, governingType, sumTypeSym, unionType, ref hasWildcard, matchedVariants, matchedTypes, arm.GetSource());
            var armType = ResolveExpression(arm.Expression, armScope, containingFunction);
            if (armResultType.IsResolved && armResultType == voidType)
                armResultType = armType;
        }

        // Exhaustiveness checking
        if (!hasWildcard && !isUntaggedSumType)
        {
            if (sumTypeSym is not null && typeSym?.TypeKind == TypeKind.Enum)
            {
                foreach (var vHandle in sumTypeSym.EnumVariants)
                {
                    if (context.GetEnumVariantSymbol(vHandle) is { } vSym && !matchedVariants.Contains(vSym.Name.Text))
                    {
                        context.Diagnostics.ReportNonExhaustiveMatch(
                            match.MatchKeyword.Span,
                            vSym.Name.Text,
                            match.GetSource());
                        break;
                    }
                }
            }
            else if (unionType is not null)
            {
                foreach (var constituent in unionType.Variants)
                {
                    if (!matchedTypes.Any(t => t.Equals(constituent)))
                    {
                        context.Diagnostics.ReportNonExhaustiveMatch(
                            match.MatchKeyword.Span,
                            constituent.ToDisplayString(context),
                            match.GetSource());
                        break;
                    }
                }
            }
            else if (sumTypeSym is not null && sumTypeSym.UnionConstituents.Count > 0)
            {
                foreach (var constituent in sumTypeSym.UnionConstituents)
                {
                    if (!matchedTypes.Any(t => t.Equals(constituent)))
                    {
                        context.Diagnostics.ReportNonExhaustiveMatch(
                            match.MatchKeyword.Span,
                            constituent.ToDisplayString(context),
                            match.GetSource());
                        break;
                    }
                }
            }
        }

        return armResultType;
    }

    private void ValidatePattern(
        PatternSyntax pattern,
        Scope armScope,
        TypeRef governingType,
        ISumTypeSymbol? sumTypeSym,
        UnionSemanticType? unionType,
        ref bool hasWildcard,
        HashSet<string> matchedVariants,
        HashSet<TypeRef> matchedTypes,
        SourceText? source)
    {
        switch (pattern)
        {
            case WildcardPattern:
            case VariablePattern:
                hasWildcard = true;
                break;

            case EnumVariantPattern variantPat:
                var variantName = context.GetScopedSymbolName(variantPat.Name).Last.Text;
                if (sumTypeSym is not null && sumTypeSym.EnumVariants.Count > 0)
                {
                    bool found = false;
                    foreach (var vHandle in sumTypeSym.EnumVariants)
                    {
                        if (context.GetEnumVariantSymbol(vHandle) is { } v && v.Name.Text == variantName)
                        {
                            found = true;
                            matchedVariants.Add(variantName);
                            break;
                        }
                    }

                    if (!found)
                    {
                        context.Diagnostics.ReportInvalidPatternForSumType(
                            variantPat.GetSpan() ?? default,
                            variantName,
                            (sumTypeSym as Symbol)?.Name.ToString() ?? "Enum",
                            source);
                    }
                }
                else if (unionType is not null || (sumTypeSym is not null && sumTypeSym.UnionConstituents.Count > 0))
                {
                    var constituents = unionType?.Variants ?? sumTypeSym!.UnionConstituents;
                    var matched = constituents.FirstOrDefault(c => c.ToDisplayString(context) == variantName || (c.Handle is { } ch && context.GetTypeSymbol(ch)?.Name.Text == variantName));
                    if (matched != default)
                    {
                        matchedTypes.Add(matched);
                    }
                    else
                    {
                        string name = unionType?.ToDisplayString(context) ?? (sumTypeSym as Symbol)?.Name.ToString() ?? "Union";
                        context.Diagnostics.ReportInvalidPatternForSumType(
                            variantPat.GetSpan() ?? default,
                            variantName,
                            name,
                            source);
                    }
                }
                break;

            case TypePattern typePat:
                var resolvedType = ResolveTypeSyntax(typePat.Type, armScope);
                var constituentsList = unionType?.Variants ?? sumTypeSym?.UnionConstituents;

                if (constituentsList is not null)
                {
                    bool isValid = constituentsList.Any(c => c.Equals(resolvedType) || (c.Handle is { } ch && resolvedType.Handle is { } rh && ch == rh));
                    if (isValid)
                    {
                        matchedTypes.Add(resolvedType);
                    }
                    else
                    {
                        string name = unionType?.ToDisplayString(context) ?? (sumTypeSym as Symbol)?.Name.ToString() ?? "Union";
                        context.Diagnostics.ReportInvalidPatternForSumType(
                            typePat.GetSpan() ?? default,
                            resolvedType.ToDisplayString(context),
                            name,
                            source);
                    }
                }
                break;
        }
    }

    private TypeRef ResolveConstructorCall(ConstructorCallExpression constructorCall, Scope scope, SymbolHandle containingFunction)
    {
        var isPut = constructorCall.Kind == ObjectCreationKind.Put;
        var targetType = ResolveTypeSyntax(constructorCall.Type, scope);

        var ctorArgTypes = new List<TypeRef>(constructorCall.Arguments.Count);
        foreach (var arg in constructorCall.Arguments)
            ctorArgTypes.Add(ResolveExpression(arg, scope, containingFunction));

        var allocArgTypes = new List<TypeRef>();
        if (constructorCall.AllocationArguments is { } allocArgs)
        {
            foreach (var a in allocArgs)
                allocArgTypes.Add(ResolveExpression(a, scope, containingFunction));
        }

        // Two-phase allocation resolution:
        if (isPut)
        {
            if (context.DiscoveredOperators.TryGetOperators(OperatorKind.Put, out var putCandidates))
            {
                foreach (var candidate in putCandidates)
                {
                    if (context.GetMethodSymbol(candidate.MethodHandle) is { } putMethod)
                    {
                        if (constructorCall.AllocationArguments is { } aArgs)
                        {
                            for (int i = 0; i < aArgs.Count && i < putMethod.Parameters.Count; i++)
                            {
                                if (allocArgTypes[i].IsLiteral && context.GetParameterSymbol(putMethod.Parameters[i]) is { } p)
                                    CollapseExpression(aArgs[i], p.Type, scope, containingFunction);
                            }
                        }
                        context.ResolvedTree.AddReference(constructorCall, candidate.MethodHandle);
                        break;
                    }
                }
            }
        }
        else if (allocArgTypes.Count > 0 || constructorCall.AllocationTypeArguments is not null)
        {
            if (context.DiscoveredOperators.TryGetOperators(OperatorKind.New, out var newCandidates))
            {
                foreach (var candidate in newCandidates)
                {
                    if (context.GetMethodSymbol(candidate.MethodHandle) is { } newMethod)
                    {
                        if (constructorCall.AllocationArguments is { } aArgs)
                        {
                            for (int i = 0; i < aArgs.Count && i < newMethod.Parameters.Count; i++)
                            {
                                if (allocArgTypes[i].IsLiteral && context.GetParameterSymbol(newMethod.Parameters[i]) is { } p)
                                    CollapseExpression(aArgs[i], p.Type, scope, containingFunction);
                            }
                        }
                        context.ResolvedTree.AddReference(constructorCall, candidate.MethodHandle);
                        break;
                    }
                }
            }
        }

        // Resolve Constructor on targetType:
        if (targetType.Handle is { } targetHandle && context.GetTypeSymbol(targetHandle) is { } targetSym)
        {
            var targetScope = GetOwnedScope(targetSym);
            foreach (var sym in targetScope.Symbols.Values)
            {
                if (sym is MethodSymbol m && (m.SpecialKind == SpecialFunctionKind.Constructor || m.Name.Text == ".ctor" || m.Name.Text == "Construct"))
                {
                    if (m.Parameters.Count == ctorArgTypes.Count)
                    {
                        bool matches = true;
                        for (int i = 0; i < ctorArgTypes.Count; i++)
                        {
                            if (context.GetParameterSymbol(m.Parameters[i]) is { } param)
                            {
                                if (ctorArgTypes[i].IsLiteral)
                                {
                                    if (!IsLiteralCompatibleWith(ctorArgTypes[i], param.Type))
                                    {
                                        matches = false;
                                        break;
                                    }
                                }
                                else if (!param.Type.IsAssignableFrom(ctorArgTypes[i], context))
                                {
                                    matches = false;
                                    break;
                                }
                            }
                        }
                        if (matches)
                        {
                            for (int i = 0; i < ctorArgTypes.Count; i++)
                            {
                                if (ctorArgTypes[i].IsLiteral && context.GetParameterSymbol(m.Parameters[i]) is { } param)
                                {
                                    CollapseExpression(constructorCall.Arguments[i], param.Type, scope, containingFunction);
                                    ctorArgTypes[i] = param.Type;
                                }
                            }
                            context.ResolvedTree.AddReference(constructorCall, ResolutionContext.GetHandle(m));
                            break;
                        }
                    }
                }
            }
        }

        return isPut ? voidType : targetType;
    }

    private TypeRef ResolveTypeAs(TypeAsExpression typeAs, Scope scope) =>
        ResolveTypeSyntax(typeAs.TargetType, scope);

    private TypeRef ResolveAs(AsExpression asExpr, Scope scope) =>
        ResolveTypeSyntax(asExpr.Type, scope);

    private TypeRef ResolveCollection(CollectionExpression collection, Scope scope, SymbolHandle containingFunction)
    {
        TypeRef elemType = TypeRef.Unresolved;
        foreach (var item in collection.Expressions)
        {
            var itemType = ResolveExpression(item, scope, containingFunction);
            if (elemType.IsUnresolved)
                elemType = itemType;
        }

        return TypeRef.Array(elemType);
    }

    private TypeRef ResolveTypeSyntax(TypeSyntax syntax, Scope scope)
    {
        var name = ResolutionContext.GetSymbolName(syntax);
        var syms = scope.ResolveType(name);
        if (syms.Count > 0)
            return TypeRef.Resolved(ResolutionContext.GetHandle(syms[0]));

        return TypeRef.Unresolved;
    }

    private void CollapseRemainingLocals(Scope scope, SymbolHandle containingFunction)
    {
        if (uncollapsedLocals.Count == 0)
            return;

        var snapshot = uncollapsedLocals.ToList();
        foreach (var (local, initNode) in snapshot)
        {
            if (uncollapsedLocals.ContainsKey(local))
            {
                var defaultType = CollapseLiteralToDefault(local.Type);
                uncollapsedLocals.Remove(local);
                local.Type = defaultType;
                CollapseExpression(initNode, defaultType, scope, containingFunction);
            }
        }
    }

    private TypeRef CollapseExpression(
        SyntaxNode node,
        TypeRef targetType,
        Scope scope,
        SymbolHandle containingFunction)
    {
        if (targetType.IsLiteral || targetType.IsInferred || targetType.IsError || targetType.IsUnresolved)
            return targetType;

        switch (node)
        {
            case NamedExpression named:
                LocalVariableSymbol? local = null;
                var namedPart = new SymbolPart(named.Identifier, 0, context.GetHygiene(named));
                var namedSyms = scope[new SymbolName(namedPart)];
                if (namedSyms.Count > 0 && namedSyms[0] is LocalVariableSymbol ls)
                    local = ls;
                else if (context.ResolvedTree.TryGetReference(named, out var handle) &&
                         context.GetSymbol(handle) is LocalVariableSymbol lh)
                    local = lh;

                if (local is not null)
                {
                    if (uncollapsedLocals.TryGetValue(local, out var initNode))
                    {
                        uncollapsedLocals.Remove(local);
                        local.Type = targetType;
                        CollapseExpression(initNode, targetType, scope, containingFunction);
                    }
                    else
                    {
                        local.Type = targetType;
                    }
                }
                context.SetExpressionType(named, targetType);
                return targetType;

            case LiteralExpression lit:
                if (lit.Literal.MatchingKind is MatchingKeywordKind.True or MatchingKeywordKind.False)
                    return boolType;

                var literalKind = lit.Literal.Kind is TokenKind.Float ? CompileTimeLiteralKind.Float :
                                  lit.Literal.Kind is TokenKind.String ? CompileTimeLiteralKind.Str :
                                  lit.Literal.Kind is TokenKind.Char ? CompileTimeLiteralKind.Char : CompileTimeLiteralKind.Int;
                var literalOverloads = GetMatchingLiteralMethods(targetType, literalKind);
                if (literalOverloads.Count > 1)
                {
                    var overloadNames = string.Join(", ", literalOverloads.Select(m => m.Name.Text));
                    context.Diagnostics.ReportAmbiguousLiteralConversion(
                        lit.GetSpan() ?? default,
                        targetType.ToDisplayString(context),
                        overloadNames,
                        lit.GetSource());
                    context.SetExpressionType(lit, TypeRef.Error);
                    return TypeRef.Error;
                }
                if (literalOverloads.Count == 1)
                {
                    context.ResolvedTree.AddReference(lit, ResolutionContext.GetHandle(literalOverloads[0]));
                    context.SetExpressionType(lit, targetType);
                    return targetType;
                }

                if (!IsLiteralCompatibleWith(lit, targetType))
                {
                    return TypeRef.Error;
                }
                context.SetExpressionType(lit, targetType);
                return targetType;

            case BinaryExpression binary:
                var opKind = GetBinaryOperatorKind(binary.OperatorToken.Kind);
                if (TryFindOperatorForTarget(opKind, targetType, out var opMethod, out var lhsType, out var rhsType) && opMethod != null)
                {
                    CollapseExpression(binary.LeftExpression, lhsType, scope, containingFunction);
                    CollapseExpression(binary.RightExpression, rhsType, scope, containingFunction);
                    context.ResolvedTree.AddReference(binary, ResolutionContext.GetHandle(opMethod));
                    context.SetExpressionType(binary, opMethod.ReturnType);
                    return opMethod.ReturnType;
                }
                else
                {
                    context.Diagnostics.ReportTypeMismatch(
                        binary.OperatorToken.Span,
                        binary.OperatorToken.Value,
                        binary.LeftExpression.GetSpan() ?? default,
                        targetType.ToDisplayString(context),
                        binary.RightExpression.GetSpan() ?? default,
                        targetType.ToDisplayString(context),
                        source: binary.GetSource());
                    context.SetExpressionType(binary, TypeRef.Error);
                    return TypeRef.Error;
                }

            case UnaryExpression unary:
                var uOpKind = GetUnaryOperatorKind(unary.OperatorToken.Kind, unary.Position);
                if (TryFindUnaryOperatorForTarget(uOpKind, targetType, out var uOpMethod, out var opParamType) && uOpMethod != null)
                {
                    CollapseExpression(unary.Operand, opParamType, scope, containingFunction);
                    context.ResolvedTree.AddReference(unary, ResolutionContext.GetHandle(uOpMethod));
                    context.SetExpressionType(unary, uOpMethod.ReturnType);
                    return uOpMethod.ReturnType;
                }
                else
                {
                    context.Diagnostics.ReportUnaryTypeMismatch(
                        unary.OperatorToken.Span,
                        unary.OperatorToken.Value,
                        unary.Operand.GetSpan() ?? default,
                        targetType.ToDisplayString(context),
                        source: unary.GetSource());
                    context.SetExpressionType(unary, TypeRef.Error);
                    return TypeRef.Error;
                }

            case CallExpression call:
                if (call.Callee is MemberAccessExpression access)
                {
                    CollapseExpression(access.Expression, targetType, scope, containingFunction);
                    var memberSym = LookupMemberOnType(targetType, access.Identifier.Value);
                    if (memberSym is MethodSymbol m)
                    {
                        context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(m));
                        context.ResolvedTree.AddReference(call, ResolutionContext.GetHandle(m));
                        context.SetExpressionType(call, m.ReturnType);
                        return m.ReturnType;
                    }
                    else
                    {
                        context.Diagnostics.ReportMemberNotFound(
                            access.Identifier.Span,
                            access.Identifier.Value,
                            targetType.ToDisplayString(context),
                            source: access.GetSource());
                        context.SetExpressionType(call, TypeRef.Error);
                        return TypeRef.Error;
                    }
                }
                return TypeRef.Unresolved;

            case ParenthesizedExpression paren:
                var res = CollapseExpression(paren.Expression, targetType, scope, containingFunction);
                context.SetExpressionType(paren, res);
                return res;

            default:
                return targetType;
        }
    }

    private bool TryFindOperatorForTarget(
        OperatorKind kind,
        TypeRef targetType,
        out MethodSymbol? opMethod,
        out TypeRef lhsType,
        out TypeRef rhsType)
    {
        opMethod = null;
        lhsType = TypeRef.Unresolved;
        rhsType = TypeRef.Unresolved;

        if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
            return false;

        if (targetType.Handle is not { } targetHandle)
            return false;

        var targetSym = (Symbol?)context.GetTypeSymbol(targetHandle) ?? context.GetSymbol(targetHandle);
        if (targetSym is null)
            return false;

        var scope = GetOwnedScope(targetSym);
        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null)
                continue;

            foreach (var sym in scope.Symbols.Values)
            {
                if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                {
                    if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                    {
                        if (method.Parameters.Count == 2)
                        {
                            var p0 = context.GetParameterSymbol(method.Parameters[0])?.Type ?? TypeRef.Unresolved;
                            var p1 = context.GetParameterSymbol(method.Parameters[1])?.Type ?? TypeRef.Unresolved;
                            opMethod = method;
                            lhsType = p0;
                            rhsType = p1;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private bool TryFindUnaryOperatorForTarget(
        OperatorKind kind,
        TypeRef targetType,
        out MethodSymbol? opMethod,
        out TypeRef operandType)
    {
        opMethod = null;
        operandType = TypeRef.Unresolved;

        if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
            return false;

        if (targetType.Handle is not { } targetHandle)
            return false;

        var targetSym = (Symbol?)context.GetTypeSymbol(targetHandle) ?? context.GetSymbol(targetHandle);
        if (targetSym is null)
            return false;

        var scope = GetOwnedScope(targetSym);
        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null)
                continue;

            foreach (var sym in scope.Symbols.Values)
            {
                if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                {
                    if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                    {
                        if (method.Parameters.Count == 1)
                        {
                            var p0 = context.GetParameterSymbol(method.Parameters[0])?.Type ?? TypeRef.Unresolved;
                            opMethod = method;
                            operandType = p0;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private bool TryResolveImplicitCast(TypeRef from, TypeRef to, out MethodSymbol? castMethod)
    {
        castMethod = null;
        if (!context.DiscoveredOperators.TryGetOperators(OperatorKind.ImplicitCast, out var candidates))
            return false;

        var operandHandles = new List<SymbolHandle>();
        if (from.Handle is { } fh) operandHandles.Add(fh);
        if (to.Handle is { } th && !operandHandles.Contains(th)) operandHandles.Add(th);

        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null)
                continue;

            foreach (var opHandle in operandHandles)
            {
                var operandTypeSym = (Symbol?)context.GetTypeSymbol(opHandle) ?? context.GetSymbol(opHandle);
                if (operandTypeSym is null)
                    continue;

                var scope = GetOwnedScope(operandTypeSym);
                foreach (var sym in scope.Symbols.Values)
                {
                    if (sym is MethodSymbol method && method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                    {
                        if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                        {
                            if (method.Parameters.Count == 1)
                            {
                                var paramType = context.GetParameterSymbol(method.Parameters[0])?.Type ?? TypeRef.Unresolved;
                                if (paramType.Equals(from) && method.ReturnType.Equals(to))
                                {
                                    castMethod = method;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    private Symbol? LookupMemberOnType(TypeRef targetType, string memberName)
    {
        if (targetType.Handle is not { } handle)
            return null;

        var sym = (Symbol?)context.GetTypeSymbol(handle) ?? context.GetSymbol(handle);
        if (sym is not null)
        {
            var scope = GetOwnedScope(sym);
            var found = scope[new SymbolName(new SymbolPart(memberName))];
            var accessible = found.Where(cand => cand is not MethodSymbol m || !m.Flags.HasFlag(FunctionFlags.InterfaceScoped)).ToList();
            if (accessible.Count > 0)
                return accessible[0];
        }

        return null;
    }

    private bool IsLiteralCompatibleWith(LiteralExpression lit, TypeRef targetType)
    {
        if (targetType.IsLiteral)
            return true;

        if (lit.Literal.Kind is TokenKind.Integer)
        {
            if (IsNumeric(targetType)) return true;
        }
        else if (lit.Literal.Kind is TokenKind.Float)
        {
            if (IsNumeric(targetType)) return true;
        }
        else if (lit.Literal.Kind is TokenKind.String)
        {
            if (hStr is { } hs && targetType.Handle == hs) return true;
        }
        else if (lit.Literal.Kind is TokenKind.Char)
        {
            if (hChar is { } hc && targetType.Handle == hc) return true;
        }

        if (targetType.Handle is { } th)
        {
            var typeSym = (Symbol?)context.GetTypeSymbol(th) ?? context.GetSymbol(th);
            if (typeSym is not null)
            {
                var scope = GetOwnedScope(typeSym);
                foreach (var sym in scope.Symbols.Values)
                {
                    if (sym is MethodSymbol m && m.Flags.HasFlag(FunctionFlags.Literal))
                        return true;
                }
            }
        }

        return false;
    }

    private bool IsLiteralCompatibleWith(TypeRef litType, TypeRef targetType)
    {
        if (targetType.IsLiteral)
            return true;

        if (litType.SpecialType is CompileTimeLiteralType lit)
        {
            if (lit.LiteralKind == CompileTimeLiteralKind.Int && IsNumeric(targetType)) return true;
            if (lit.LiteralKind == CompileTimeLiteralKind.Float && IsNumeric(targetType)) return true;
            if (lit.LiteralKind == CompileTimeLiteralKind.Str && hStr is { } hs && targetType.Handle == hs) return true;
            if (lit.LiteralKind == CompileTimeLiteralKind.Char && hChar is { } hc && targetType.Handle == hc) return true;
        }

        if (targetType.Handle is { } th)
        {
            var typeSym = (Symbol?)context.GetTypeSymbol(th) ?? context.GetSymbol(th);
            if (typeSym is not null)
            {
                var scope = GetOwnedScope(typeSym);
                foreach (var sym in scope.Symbols.Values)
                {
                    if (sym is MethodSymbol m && m.Flags.HasFlag(FunctionFlags.Literal))
                        return true;
                }
            }
        }

        return false;
    }
}
