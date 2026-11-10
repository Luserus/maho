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
                var scope = GetOwnedScope(function);
                ResolveFunctionBody(syntax.Body, scope, ResolutionContext.GetHandle(function), function.ReturnType);
            }
        }

        // 5. Resolve method bodies
        foreach (var method in context.MethodSymbols)
        {
            if (method.Syntax is { } syntax && visitedBodies.Add(syntax.Body))
            {
                var scope = GetOwnedScope(method);
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
    }

    private void ResolveLocal(Local local, Scope scope, SymbolHandle containingFunction, TypeRef expectedReturnType)
    {
        switch (local)
        {
            case LocalExpressionStatement exprStmt:
                var t = ResolveExpression(exprStmt.Expression, scope, containingFunction);
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

            if (!expectedReturnType.IsInferred && !expectedReturnType.IsUnresolved && !expectedReturnType.IsError &&
                !returnedType.IsInferred && !returnedType.IsUnresolved && !returnedType.IsError)
            {
                if (!expectedReturnType.IsAssignableFrom(returnedType, context))
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
                                SetVariableSymbolType(elemSym, tupleSemantic.Elements[i].Type);
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
                        // Inferred variable type resolution:
                        // If initType is known (including Void), infer the variable type:
                        if (initType.IsResolved || initType.IsSpecial)
                            SetVariableSymbolType(sym, initType);
                    }
                    else if (sym is not null && symType is { } st && !st.IsInferred && !st.IsUnresolved && !st.IsError &&
                             !initType.IsInferred && !initType.IsUnresolved && !initType.IsError &&
                             initExpr is not CollectionExpression)
                    {
                        // Type check explicit variable initialization
                        if (!st.IsAssignableFrom(initType, context))
                        {
                            context.Diagnostics.ReportAssignmentTypeMismatch(
                                initExpr.GetSpan() ?? default,
                                initType.ToDisplayString(context),
                                st.ToDisplayString(context),
                                declaration.GetSource());
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
                result = ResolveNamed(named, scope);
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
        if (lit.HasSuffix)
        {
            var suffix = lit.Literal.GetSuffixSpan();
            if (suffix.SequenceEqual("i8") && hInt8 is { } h1) return TypeRef.Resolved(h1);
            if (suffix.SequenceEqual("i16") && hInt16 is { } h2) return TypeRef.Resolved(h2);
            if (suffix.SequenceEqual("i32") && hInt32 is { } h3) return TypeRef.Resolved(h3);
            if (suffix.SequenceEqual("i64") && hInt64 is { } h4) return TypeRef.Resolved(h4);
            if (suffix.SequenceEqual("i128") && hInt128 is { } h5) return TypeRef.Resolved(h5);
            if ((suffix.SequenceEqual("isize") || suffix.SequenceEqual("nint")) && hNInt is { } h6) return TypeRef.Resolved(h6);

            if (suffix.SequenceEqual("u8") && hUInt8 is { } uh1) return TypeRef.Resolved(uh1);
            if (suffix.SequenceEqual("u16") && hUInt16 is { } uh2) return TypeRef.Resolved(uh2);
            if (suffix.SequenceEqual("u32") && hUInt32 is { } uh3) return TypeRef.Resolved(uh3);
            if (suffix.SequenceEqual("u64") && hUInt64 is { } uh4) return TypeRef.Resolved(uh4);
            if (suffix.SequenceEqual("u128") && hUInt128 is { } uh5) return TypeRef.Resolved(uh5);
            if ((suffix.SequenceEqual("usize") || suffix.SequenceEqual("nuint")) && hNUInt is { } uh6) return TypeRef.Resolved(uh6);

            if (suffix.SequenceEqual("f16") && hFloat16 is { } f1) return TypeRef.Resolved(f1);
            if (suffix.SequenceEqual("f32") && hFloat32 is { } f2) return TypeRef.Resolved(f2);
            if (suffix.SequenceEqual("f64") && hFloat64 is { } f3) return TypeRef.Resolved(f3);
            if (suffix.SequenceEqual("f128") && hFloat128 is { } f4) return TypeRef.Resolved(f4);

            if (suffix.SequenceEqual("s") && hStr is { } s) return TypeRef.Resolved(s);
        }

        return lit.Literal.Kind switch
        {
            TokenKind.Integer => hInt32 is { } h ? TypeRef.Resolved(h) : TypeRef.Unresolved,
            TokenKind.Float => hFloat64 is { } h ? TypeRef.Resolved(h) : TypeRef.Unresolved,
            TokenKind.String => hStr is { } h ? TypeRef.Resolved(h) : TypeRef.Unresolved,
            TokenKind.Char => hChar is { } h ? TypeRef.Resolved(h) : TypeRef.Unresolved,
            _ => TypeRef.Unresolved
        };
    }

    private TypeRef ResolveNamed(NamedExpression named, Scope scope)
    {
        var text = named.Identifier.Value;
        if (text == "true" || text == "false")
            return boolType;

        if (context.ResolvedTree.TryGetReference(named, out var handle))
            return GetTypeFromHandle(handle);

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

        // 1. Primitive fast-path
        if (TryResolvePrimitiveBinary(opKind, lhs, rhs, out var primResult))
            return primResult;

        // 2. Operator overload lookup through DiscoveredOperators
        if (TryResolveOperatorOverload(opKind, lhs, rhs, binary, out var overloadResult))
            return overloadResult;

        // 3. Neither primitive nor overload found: report MH2001 if both are concrete
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

    private bool TryResolvePrimitiveBinary(OperatorKind kind, TypeRef lhs, TypeRef rhs, out TypeRef result)
    {
        result = TypeRef.Unresolved;

        switch (kind)
        {
            case OperatorKind.Addition:
            case OperatorKind.Subtraction:
            case OperatorKind.Multiplication:
            case OperatorKind.Division:
            case OperatorKind.Remainder:
                if (lhs.Equals(rhs) && IsNumeric(lhs))
                {
                    result = lhs;
                    return true;
                }
                return false;

            case OperatorKind.BitwiseAnd:
            case OperatorKind.BitwiseOr:
            case OperatorKind.BitwiseXor:
                if (lhs.Equals(rhs) && (IsInteger(lhs) || IsBool(lhs)))
                {
                    result = lhs;
                    return true;
                }
                return false;

            case OperatorKind.LeftShift:
            case OperatorKind.RightShift:
            case OperatorKind.RotateLeft:
            case OperatorKind.RotateRight:
                if (IsInteger(lhs) && IsInteger(rhs))
                {
                    result = lhs;
                    return true;
                }
                return false;

            case OperatorKind.Equality:
            case OperatorKind.Inequality:
                if (lhs.Equals(rhs) || lhs.IsAssignableFrom(rhs, context) || rhs.IsAssignableFrom(lhs, context))
                {
                    result = boolType;
                    return true;
                }
                return false;

            case OperatorKind.LessThan:
            case OperatorKind.LessThanOrEqual:
            case OperatorKind.GreaterThan:
            case OperatorKind.GreaterThanOrEqual:
                if (lhs.Equals(rhs) && IsNumeric(lhs))
                {
                    result = boolType;
                    return true;
                }
                return false;

            case OperatorKind.LogicalAnd:
            case OperatorKind.LogicalOr:
                if (IsBool(lhs) && IsBool(rhs))
                {
                    result = boolType;
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

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
                        if (method.ExplicitInterfaceName is { } ein && ein.Last.Equals(ifaceSym.Name))
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

        // 1. Primitive fast-path
        if (TryResolvePrimitiveUnary(opKind, operand, out var primResult))
            return primResult;

        // 2. Overload lookup
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

    private bool TryResolvePrimitiveUnary(OperatorKind kind, TypeRef operand, out TypeRef result)
    {
        result = TypeRef.Unresolved;

        switch (kind)
        {
            case OperatorKind.AddressOf:
                result = TypeRef.Reference(operand);
                return true;

            case OperatorKind.Dereference:
                if (operand.SpecialType is ReferenceSemanticType refType)
                {
                    result = refType.ElementType;
                    return true;
                }
                if (operand.SpecialType is PointerSemanticType ptrType)
                {
                    result = ptrType.ElementType;
                    return true;
                }
                return false;

            case OperatorKind.UnaryNegation:
                if (IsSignedInteger(operand) || IsFloatingPoint(operand))
                {
                    result = operand;
                    return true;
                }
                return false;

            case OperatorKind.UnaryPositive:
                if (IsNumeric(operand))
                {
                    result = operand;
                    return true;
                }
                return false;

            case OperatorKind.LogicalNot:
                if (IsBool(operand))
                {
                    result = boolType;
                    return true;
                }
                return false;

            case OperatorKind.BitwiseNot:
                if (IsInteger(operand))
                {
                    result = operand;
                    return true;
                }
                return false;

            case OperatorKind.PrefixIncrement:
            case OperatorKind.PostfixIncrement:
            case OperatorKind.PrefixDecrement:
            case OperatorKind.PostfixDecrement:
                if (IsNumeric(operand))
                {
                    result = operand;
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

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
                    if (method.ExplicitInterfaceName is { } ein && ein.Last.Equals(ifaceSym.Name))
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
            if (!lhs.IsAssignableFrom(rhs, context) &&
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
        var calleeType = ResolveExpression(call.Callee, scope, containingFunction);
        var argTypes = new List<TypeRef>(call.Arguments.Count);
        foreach (var arg in call.Arguments)
            argTypes.Add(ResolveExpression(arg, scope, containingFunction));

        if (context.ResolvedTree.TryGetReference(call.Callee, out var calleeHandle))
        {
            var sym = context.GetSymbol(calleeHandle);
            if (sym is FunctionSymbol fn)
                return TypeCheckCall(call, fn.Parameters, fn.ReturnType, argTypes);
            if (sym is MethodSymbol m)
                return TypeCheckCall(call, m.Parameters, m.ReturnType, argTypes);
        }

        return calleeType;
    }

    private static bool IsVariadicParameter(ParameterSymbol p) =>
        p.Syntax?.Declarator.Type is ModifiedType m && m.Modifier is VariadicTypeModifier;

    private static bool HasDefaultValue(ParameterSymbol p) =>
        p.Syntax?.Initializer is not null;

    private TypeRef TypeCheckCall(CallExpression call, IReadOnlyList<SymbolHandle> paramHandles, TypeRef returnType, List<TypeRef> argTypes)
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
            if (!param.Type.IsAssignableFrom(argTypes[i], context) &&
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

        // Check if access.Expression is a TypeSymbol (static access / enum variant)
        bool isStaticAccessOnType = false;
        if (access.Expression is NamedExpression namedExpr)
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
        if (receiverType.Handle is { } recvHandle && context.GetTypeSymbol(recvHandle) is { } recvTypeSym)
        {
            var recvScope = GetOwnedScope(recvTypeSym);
            var syms = recvScope[new SymbolName(new SymbolPart(memberName))];
            var accessible = syms.Where(cand => cand is not MethodSymbol m || !m.Flags.HasFlag(FunctionFlags.InterfaceScoped)).ToList();
            if (accessible.Count > 0)
            {
                context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(accessible[0]));
                return GetTypeFromSymbol(accessible[0]);
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
                    if (candidates.Any(c => method.ExplicitInterfaceName?.Last.Equals(context.GetTypeSymbol(c.InterfaceHandle)?.Name) == true))
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
        var elseType = conditional.ElseExpression is not null
            ? ResolveExpression(conditional.ElseExpression, ifScope, containingFunction)
            : voidType;

        return thenType.IsAssignableFrom(elseType, context) ? thenType : elseType;
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
        foreach (var local in block.Locals)
            ResolveLocal(local, blockScope, containingFunction, voidType);

        if (block.FinalExpression is { } finalExpr)
            return ResolveExpression(finalExpr, blockScope, containingFunction);

        return voidType;
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
        var targetType = ResolveTypeSyntax(constructorCall.Type, scope);
        foreach (var arg in constructorCall.Arguments)
            ResolveExpression(arg, scope, containingFunction);

        return targetType;
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
}
