using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

/// <summary>
/// Pass 5 of semantic resolution: Expression Type Binding, Operator Resolution, and Eager/Retroactive Literal Collapsing.
/// <para>
/// Responsibilities:
/// <list type="bullet">
///   <item><description>Binds every expression in function/method/accessor bodies and variable/field initializers to its semantic <see cref="TypeRef"/>.</description></item>
///   <item><description>Dispatches operators (<c>+</c>, <c>-</c>, <c>*</c>, <c>==</c>, indexers, callables, etc.) strictly through discovered operator interfaces (<c>[OperatorFunction]</c>).</description></item>
///   <item><description>Performs zero constant-folding: expressions resolve to AST operator invocation nodes rather than pre-calculated constants.</description></item>
///   <item><description>Implements eager, retroactive literal collapsing: unsuffixed literals start as unconstrained scalar types (<see cref="CompileTimeLiteralType"/>) and collapse into the first concrete lvalue type encountered, retroactively resolving all operations chained on them.</description></item>
///   <item><description>Enforces sequential eager collapsing: subsequent uses retain the collapsed type, emitting argument mismatch diagnostics if incompatible.</description></item>
///   <item><description>Resolves canonical booleans (<c>true</c>/<c>false</c>) directly to canonical <c>bool</c> without literal polymorphism.</description></item>
///   <item><description>Resolves <c>this</c> to the enclosing type handle within non-static methods, constructors, and property accessors.</description></item>
///   <item><description>Resolves two-phase object allocation and construction (<c>New</c>/<c>Put</c> allocation followed by <c>Ctor</c> construction).</description></item>
/// </list>
/// </para>
/// </summary>
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

    /// <summary>Tracks visited function bodies to avoid redundant evaluation across multiple traversal passes.</summary>
    private readonly HashSet<FunctionBody> visitedBodies = [];

    /// <summary>Tracks visited variable declarations to avoid redundant evaluation across multiple traversal passes.</summary>
    private readonly HashSet<VariableDeclaration> visitedDeclarations = [];

    /// <summary>
    /// Tracks local variables declared with unconstrained literals (<c>var x = 10;</c>) mapped to their initializer AST node,
    /// enabling retroactive type propagation when an assignment or operator subsequently collapses the variable.
    /// </summary>
    private readonly Dictionary<LocalVariableSymbol, SyntaxNode> uncollapsedLocals = new();

    /// <summary>Expected target types for contextual type propagation into lambdas and expressions.</summary>
    private readonly Dictionary<Expression, TypeRef> expectedExpressionTypes = new();

    /// <summary>Tracks captured outer symbols for active lambda expressions.</summary>
    private readonly Stack<HashSet<Symbol>> lambdaCapturedSymbols = new();

    /// <summary>Lexical scopes of currently resolving lambda expressions.</summary>
    private readonly Stack<Scope> activeLambdaScopes = new();

    /// <summary>Tracks return expression types encountered in lambda block bodies.</summary>
    private readonly Stack<List<TypeRef>> lambdaReturnTypes = new();

    /// <summary>
    /// Executes Pass 5: binds expression types, dispatches operators against library interfaces,
    /// and resolves retroactive literal collapsing across all statements.
    /// </summary>
    /// <param name="context">The active resolution context.</param>
    public override void Resolve(ResolutionContext context)
    {
        this.context = context;
        CacheIntrinsicHandles();

        // 1. Resolve top-level statements / main scope locals
        ResolveTopLevelStatements();

        // 2. Resolve global variable initializers
        foreach (var global in context.GlobalVariableSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(global));
            if (global.Syntax is { } varDecl && visitedDeclarations.Add(varDecl))
                ResolveVariableDeclaration(varDecl, global.EnclosingScope, default);
        }

        // 3. Resolve field initializers
        foreach (var field in context.FieldSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(field));
            if (field.Syntax is { } varDecl && visitedDeclarations.Add(varDecl))
                ResolveVariableDeclaration(varDecl, field.EnclosingScope, default);
        }

        // 4. Resolve function bodies
        foreach (var function in context.FunctionSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(function));
            if (function.Syntax is { } syntax && visitedBodies.Add(syntax.Body))
            {
                var scope = context.GetSyntaxScope(syntax.Body, GetOwnedScope(function));
                ResolveFunctionBody(syntax.Body, scope, ResolutionContext.GetHandle(function), function.ReturnType);
            }
        }

        // 5. Resolve method bodies
        foreach (var method in context.MethodSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(method));
            if (method.Syntax is { } syntax && visitedBodies.Add(syntax.Body))
            {
                var scope = context.GetSyntaxScope(syntax.Body, GetOwnedScope(method));
                if (syntax.Signature.BaseClause is { } baseClause)
                {
                    foreach (var init in baseClause.Initializers)
                    {
                        foreach (var arg in init.Arguments)
                            ResolveExpression(arg, scope, ResolutionContext.GetHandle(method));
                    }
                }
                ResolveFunctionBody(syntax.Body, scope, ResolutionContext.GetHandle(method), method.ReturnType);
            }
        }

        // 6. Resolve property accessors
        foreach (var property in context.PropertySymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(property));
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

        context.CurrentTreeFilePath = null;
    }

    /// <summary>
    /// Caches handles to discovered intrinsic primitives in local fields for fast zero-allocation checks.
    /// Includes name-based fallbacks for unit test environments that do not register full intrinsic metadata.
    /// </summary>
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

        if (intrinsics.TryGet(KnownIntrinsics.Unit, out var v))
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

    /// <summary>
    /// Looks up a type symbol handle by name in top-level or nested types as a fallback for testing.
    /// </summary>
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

    /// <summary>Checks whether a type reference corresponds to a signed integer primitive.</summary>
    private bool IsSignedInteger(TypeRef t) =>
        t.Handle is { } h && (h == hInt8 || h == hInt16 || h == hInt32 || h == hInt64 || h == hInt128 || h == hNInt);

    /// <summary>Checks whether a type reference corresponds to an unsigned integer primitive.</summary>
    private bool IsUnsignedInteger(TypeRef t) =>
        t.Handle is { } h && (h == hUInt8 || h == hUInt16 || h == hUInt32 || h == hUInt64 || h == hUInt128 || h == hNUInt);

    /// <summary>Checks whether a type reference corresponds to any integer primitive.</summary>
    private bool IsInteger(TypeRef t) => IsSignedInteger(t) || IsUnsignedInteger(t);

    /// <summary>Checks whether a type reference corresponds to a floating-point primitive.</summary>
    private bool IsFloatingPoint(TypeRef t) =>
        t.Handle is { } h && (h == hFloat16 || h == hFloat32 || h == hFloat64 || h == hFloat128);

    /// <summary>Checks whether a type reference is numeric (integer or floating-point).</summary>
    private bool IsNumeric(TypeRef t) => IsInteger(t) || IsFloatingPoint(t);

    /// <summary>Checks whether a type reference corresponds to canonical boolean.</summary>
    private bool IsBool(TypeRef t) => t.Handle is { } h && h == hBool;

    /// <summary>Checks whether a type reference corresponds to <c>void</c>.</summary>
    private bool IsVoidType(TypeRef t)
    {
        if (t.Handle is { } h)
        {
            var unwrapped = context.UnwrapAlias(h);
            if (hVoid is not null && unwrapped == hVoid.Value)
                return true;
            if (context.DiscoveredIntrinsics.TryGetIntrinsicName(unwrapped, out var intrinsicName) && intrinsicName == KnownIntrinsics.Unit)
                return true;

            var sym = context.GetSymbol(unwrapped);
            if (sym is not null && (sym.Name.Text.Equals(KnownIntrinsics.Unit, StringComparison.OrdinalIgnoreCase) || sym.Name.Text.Equals("void", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    /// <summary>Retrieves the lexical child scope owned by a symbol, or returns its enclosing scope.</summary>
    private static Scope GetOwnedScope(Symbol symbol) =>
        symbol.EnclosingScope.GetChildScope(ResolutionContext.GetHandle(symbol)) ?? symbol.EnclosingScope;

    /// <summary>
    /// Resolves top-level statements across compilation units that opted into top-level execution.
    /// Traverses statements under the synthetic main function and collapses remaining unconstrained locals.
    /// </summary>
    private void ResolveTopLevelStatements()
    {
        foreach (var tree in context.SyntaxTrees)
        {
            if (tree.Root is CompilationUnit unit && unit.EnablesTopLevelStatements)
            {
                context.CurrentTreeFilePath = tree.FilePath;
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
        context.CurrentTreeFilePath = null;
    }

    /// <summary>
    /// Recursively resolves top-level statements and member declarations (blocks, namespaces, expressions).
    /// </summary>
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

    /// <summary>
    /// Resolves all statements within a function, method, or accessor body.
    /// Traverses statements in lexical sequence, binding types, resolving operators, and performing eager literal collapsing.
    /// At the end of the body, triggers <see cref="CollapseRemainingLocals"/> to collapse any locals that remained unconstrained.
    /// </summary>
    /// <param name="body">The AST function body.</param>
    /// <param name="scope">The lexical scope for the body.</param>
    /// <param name="containingFunction">Symbol handle of the containing callable.</param>
    /// <param name="expectedReturnType">The expected return type of the callable.</param>
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

    /// <summary>
    /// Dispatches and resolves an individual local statement (expression statement, return, local variable declaration, goto).
    /// </summary>
    private void ResolveLocal(Local local, Scope scope, SymbolHandle containingFunction, TypeRef expectedReturnType)
    {
        switch (local)
        {
            case LocalExpressionStatement exprStmt:
                var t = ResolveExpression(exprStmt.Expression, scope, containingFunction);

                // If expression statement evaluates to an uncollapsed literal:
                // - If it is the final expression of a typed function, collapse to expectedReturnType.
                // - Otherwise, because its result is discarded with no target lvalue, collapse to default intrinsic.
                if (t.IsLiteral)
                {
                    var targetType = exprStmt.IsFinalExpression && !expectedReturnType.IsInferred && !expectedReturnType.IsUnresolved && !expectedReturnType.IsError
                        ? expectedReturnType
                        : CollapseLiteralToDefault(t);
                    t = CollapseExpression(exprStmt.Expression, targetType, scope, containingFunction);
                }

                // Final expression acts as implicit return: verify type compatibility against expected return type
                if (exprStmt.IsFinalExpression && !expectedReturnType.IsInferred && !expectedReturnType.IsUnresolved && !expectedReturnType.IsError && !t.IsUnresolved && !t.IsError)
                {
                    if (!expectedReturnType.IsAssignableFrom(t, context))
                    {
                        context.Diagnostics.ReportReturnTypeMismatch(
                            exprStmt.Expression.GetSpan() ?? default,
                            t.ToQualifiedString(context),
                            expectedReturnType.ToQualifiedString(context),
                            exprLabel: t.ToLabelString(context, scope),
                            returnLabel: expectedReturnType.ToLabelString(context, scope),
                            source: exprStmt.GetSource());
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

    /// <summary>
    /// Resolves and type-checks a <see cref="LocalReturnStatement"/> against the containing function's return type.
    /// <para>
    /// If the returned expression is an uncollapsed literal, it is collapsed retroactively against <paramref name="expectedReturnType"/>.
    /// If concrete, assignability is checked, falling back to implicit conversion operators (<c>[OperatorFunction("ImplicitCast")]</c>).
    /// Emits diagnostic <c>MH2006</c> if a return type mismatch occurs.
    /// </para>
    /// </summary>
    private void ResolveReturnStatement(LocalReturnStatement returnStmt, Scope scope, SymbolHandle containingFunction, TypeRef expectedReturnType)
    {
        TextSpan span;

        if (returnStmt.Statement.Expression is { } expr)
        {
            var returnedType = ResolveExpression(expr, scope, containingFunction);
            span = expr.GetSpan() ?? returnStmt.Statement.Keyword.Span;

            if (lambdaReturnTypes.Count > 0)
                lambdaReturnTypes.Peek().Add(returnedType);

            if (expectedReturnType.IsNever)
            {
                context.Diagnostics.ReportReturnTypeMismatch(
                    span,
                    returnedType.ToQualifiedString(context),
                    "!",
                    exprLabel: returnedType.ToLabelString(context, scope),
                    returnLabel: "!",
                    source: returnStmt.GetSource());
                return;
            }

            if (!expectedReturnType.IsInferred && !expectedReturnType.IsUnresolved && !expectedReturnType.IsError)
            {
                if (returnedType.IsLiteral)
                {
                    // Retroactively collapse the returned literal into the expected return type
                    var collapsed = CollapseExpression(expr, expectedReturnType, scope, containingFunction);
                    if (collapsed.IsError)
                    {
                        context.Diagnostics.ReportReturnTypeMismatch(
                            span,
                            returnedType.ToQualifiedString(context),
                            expectedReturnType.ToQualifiedString(context),
                            exprLabel: returnedType.ToLabelString(context, scope),
                            returnLabel: expectedReturnType.ToLabelString(context, scope),
                            source: returnStmt.GetSource());
                    }
                }
                else if (!expectedReturnType.IsAssignableFrom(returnedType, context) &&
                         !TryResolveImplicitCast(returnedType, expectedReturnType, out _))
                {
                    if (TypeRef.IsNonContiguousSplitBase(expectedReturnType, returnedType, context, out var splitBaseName))
                    {
                        context.Diagnostics.ReportNonContiguousBaseConversion(
                            span,
                            splitBaseName!,
                            returnStmt.GetSource());
                    }
                    else
                    {
                        context.Diagnostics.ReportReturnTypeMismatch(
                            span,
                            returnedType.ToQualifiedString(context),
                            expectedReturnType.ToQualifiedString(context),
                            exprLabel: returnedType.ToLabelString(context, scope),
                            returnLabel: expectedReturnType.ToLabelString(context, scope),
                            source: returnStmt.GetSource());
                    }
                }
            }
        }
        else
        {
            // Bare return statement (return;) must only appear in void-returning functions
            span = returnStmt.Statement.Keyword.Span;

            if (lambdaReturnTypes.Count > 0)
                lambdaReturnTypes.Peek().Add(voidType);

            var unitQualified = (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Unit, out var uH))
                ? ResolutionContext.GetQualifiedSymbolName(uH, context)
                : KnownIntrinsics.Unit;
            var unitLabel = (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Unit, out uH))
                ? ResolutionContext.GetLabelSymbolName(uH, context, scope)
                : KnownIntrinsics.Unit;

            if (expectedReturnType.IsNever)
            {
                context.Diagnostics.ReportReturnTypeMismatch(
                    span,
                    unitQualified,
                    "!",
                    exprLabel: unitLabel,
                    returnLabel: "!",
                    source: returnStmt.GetSource());
                return;
            }

            if (!expectedReturnType.IsInferred && !expectedReturnType.IsUnresolved && !expectedReturnType.IsError)
            {
                if (!IsVoidType(expectedReturnType))
                {
                    context.Diagnostics.ReportReturnTypeMismatch(
                        span,
                        unitQualified,
                        expectedReturnType.ToQualifiedString(context),
                        exprLabel: unitLabel,
                        returnLabel: expectedReturnType.ToLabelString(context, scope),
                        source: returnStmt.GetSource());
                }
            }
        }
    }

    /// <summary>
    /// Collapses an uncollapsed compile-time scalar literal type (<see cref="CompileTimeLiteralType"/>)
    /// into its default canonical intrinsic type:
    /// <list type="bullet">
    ///   <item><description><see cref="CompileTimeLiteralKind.Int"/> -> <c>int32</c></description></item>
    ///   <item><description><see cref="CompileTimeLiteralKind.Float"/> -> <c>float64</c></description></item>
    ///   <item><description><see cref="CompileTimeLiteralKind.Str"/> -> <c>Str</c></description></item>
    ///   <item><description><see cref="CompileTimeLiteralKind.Char"/> -> <c>char</c></description></item>
    /// </list>
    /// </summary>
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

    /// <summary>
    /// Resolves variable declarations (both tuple destructuring and single variable declarators).
    /// <para>
    /// Program Flow:
    /// <list type="number">
    ///   <item><description>Tuple Destructuring (<c>var (a, b) = expr;</c>): Resolves RHS, validates arity against <see cref="TupleSemanticType"/>, and assigns element types to targets.</description></item>
    ///   <item><description>Inferred Declarator (<c>var x = expr;</c>): If RHS is an unconstrained literal, records in <see cref="uncollapsedLocals"/> for retroactive propagation; otherwise assigns concrete type.</description></item>
    ///   <item><description>Explicitly Typed Declarator (<c>Concrete x = expr;</c>): If RHS is an unconstrained literal, immediately collapses it to the target type via <see cref="CollapseExpression"/>; otherwise verifies assignability or implicit conversion operators (<c>MH2003</c>).</description></item>
    ///   <item><description>Uninitialized Inferred Variable (<c>var x;</c>): Reports <c>MH2008</c>.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    private void ResolveVariableDeclaration(VariableDeclaration declaration, Scope scope, SymbolHandle containingFunction)
    {
        var declarator = declaration.Declarator;
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
                if (symType is { IsInferred: false } targetType)
                    expectedExpressionTypes[initExpr] = targetType;

                var initType = ResolveExpression(initExpr, scope, containingFunction);
                if (sym is not null && symType?.IsInferred == true)
                {
                    // Inferred local variable:
                    // If initialized from unconstrained literal, track in uncollapsedLocals for retroactive propagation
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
                    // Explicitly typed variable:
                    // If initialized from unconstrained literal, immediately collapse literal to target type
                    if (initType.IsLiteral)
                    {
                        var collapsed = CollapseExpression(initExpr, st, scope, containingFunction);
                        if (collapsed.IsError)
                        {
                            context.Diagnostics.ReportAssignmentTypeMismatch(
                                initExpr.GetSpan() ?? default,
                                initType.ToQualifiedString(context),
                                st.ToQualifiedString(context),
                                rhsLabel: initType.ToLabelString(context, scope),
                                lhsLabel: st.ToLabelString(context, scope),
                                source: declaration.GetSource());
                        }
                    }
                    else
                    {
                        // Type check explicit variable initialization: check assignability or implicit conversion
                        if (!st.IsAssignableFrom(initType, context) &&
                            !TryResolveImplicitCast(initType, st, out _))
                        {
                            if (TypeRef.IsNonContiguousSplitBase(st, initType, context, out var splitBaseName))
                            {
                                context.Diagnostics.ReportNonContiguousBaseConversion(
                                    initExpr.GetSpan() ?? default,
                                    splitBaseName!,
                                    declaration.GetSource());
                            }
                            else
                            {
                                context.Diagnostics.ReportAssignmentTypeMismatch(
                                    initExpr.GetSpan() ?? default,
                                    initType.ToQualifiedString(context),
                                    st.ToQualifiedString(context),
                                    rhsLabel: initType.ToLabelString(context, scope),
                                    lhsLabel: st.ToLabelString(context, scope),
                                    source: declaration.GetSource());
                            }
                        }
                    }
                }
            }
            else if (sym is not null && symType?.IsInferred == true)
            {
                // Inferred variable without initializer cannot determine type
                context.Diagnostics.ReportCannotInferType(
                    declarator.Identifier.GetSpan() ?? default,
                    name.Text,
                    declaration.GetSource());
            }
        }
    }

    /// <summary>
    /// Looks up a variable symbol (local, field, or global) in the given lexical scope matching the specified name.
    /// </summary>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="name">The symbol name part including hygiene identifier.</param>
    /// <returns>The matching variable symbol, or <see langword="null"/> if none is declared.</returns>
    private Symbol? ResolveLocalOrFieldSymbol(Scope scope, SymbolPart name)
    {
        var syms = scope[new SymbolName(name)];
        return syms.FirstOrDefault(s => s is LocalVariableSymbol or FieldSymbol or GlobalVariableSymbol);
    }

    /// <summary>
    /// Sets the resolved type on a variable symbol (local, field, or global).
    /// </summary>
    /// <param name="sym">The variable symbol to update.</param>
    /// <param name="type">The resolved semantic type.</param>
    private static void SetVariableSymbolType(Symbol sym, TypeRef type)
    {
        switch (sym)
        {
            case LocalVariableSymbol local: local.Type = type; break;
            case FieldSymbol field: field.Type = type; break;
            case GlobalVariableSymbol global: global.Type = type; break;
        }
    }

    /// <summary>
    /// Retrieves the current semantic type assigned to a variable symbol (local, field, or global).
    /// </summary>
    /// <param name="sym">The variable symbol to inspect.</param>
    /// <returns>The assigned <see cref="TypeRef"/>, or <see langword="null"/> if the symbol is not a variable.</returns>
    private static TypeRef? GetVariableSymbolType(Symbol sym) => sym switch
    {
        LocalVariableSymbol local => local.Type,
        FieldSymbol field => field.Type,
        GlobalVariableSymbol global => global.Type,
        _ => null
    };

    /// <summary>
    /// Recursively resolves the semantic type of an AST expression node within a lexical scope.
    /// Dispatches each expression syntax node to its specialized resolver, caches the resolved type
    /// in the resolution context, and returns the computed <see cref="TypeRef"/>.
    /// </summary>
    /// <param name="expr">The AST expression node to resolve.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The resolved semantic type of the expression.</returns>
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
                if (context.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null && !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Tuple))
                {
                    context.Diagnostics.ReportMissingIntrinsicForSyntax(tuple.GetSpan() ?? default, "tuple expression", KnownIntrinsics.Tuple, tuple.GetSource());
                }
                context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var tupleHandle);
                result = TypeRef.Tuple(elements, tupleHandle != default ? tupleHandle : null);
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
                result = ResolveAs(asExpr, scope, containingFunction);
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

            case LambdaExpression lambda:
                result = ResolveLambda(lambda, scope, containingFunction);
                break;

            default:
                break;
        }

        context.SetExpressionType(expr, result);
        return result;
    }

    /// <summary>
    /// Resolves the semantic type of a literal expression.
    /// <para>
    /// Maho applies strict literal typing rules:
    /// 1. Canonical booleans (<c>true</c>, <c>false</c>) immediately resolve to canonical <c>bool</c>.
    /// 2. Suffixed literals (e.g. <c>10km</c>, <c>42px</c>) match user-defined literal conversion functions marked with <see cref="FunctionFlags.Literal"/>.
    /// 3. Unsuffixed literals return unconstrained compile-time literal types (<see cref="CompileTimeLiteralType"/>)
    ///    preserving their source token kind until an lvalue context or explicit annotation collapses them.
    /// </para>
    /// </summary>
    /// <param name="lit">The literal expression node.</param>
    /// <returns>The resolved or uncollapsed literal <see cref="TypeRef"/>.</returns>
    private TypeRef ResolveLiteral(LiteralExpression lit)
    {
        // 1. Canonical booleans cannot be overloaded or uncollapsed
        if (lit.Literal.MatchingKind is MatchingKeywordKind.True or MatchingKeywordKind.False)
            return boolType;

        // 2. Suffixed literals dispatch to custom literal conversion methods or functions
        if (lit.HasSuffix)
        {
            var suffix = lit.Literal.GetSuffixSpan().ToString();

            // Check for literal methods declared on types
            foreach (var m in context.MethodSymbols)
            {
                if (m.Flags.HasFlag(FunctionFlags.Literal))
                {
                    bool matches = m.Name.Text == suffix;
                    if (!matches && m.IntrinsicSignature is { } sig)
                    {
                        var startAngle = sig.IndexOf('<');
                        var endAngle = sig.IndexOf('>');
                        if (startAngle >= 0 && endAngle > startAngle && sig.Substring(startAngle + 1, endAngle - startAngle - 1) == suffix)
                            matches = true;
                    }

                    if (matches)
                    {
                        context.ResolvedTree.AddReference(lit, ResolutionContext.GetHandle(m));
                        return m.ReturnType;
                    }
                }
            }

            // Check for file-level literal functions
            foreach (var fn in context.FunctionSymbols)
            {
                if (fn.Flags.HasFlag(FunctionFlags.Literal))
                {
                    bool matches = fn.Name.Text == suffix;
                    if (!matches && fn.IntrinsicSignature is { } sig)
                    {
                        var startAngle = sig.IndexOf('<');
                        var endAngle = sig.IndexOf('>');
                        if (startAngle >= 0 && endAngle > startAngle && sig.Substring(startAngle + 1, endAngle - startAngle - 1) == suffix)
                            matches = true;
                    }

                    if (matches)
                    {
                        context.ResolvedTree.AddReference(lit, ResolutionContext.GetHandle(fn));
                        return fn.ReturnType;
                    }
                }
            }

            return TypeRef.Unresolved;
        }

        // 3. Unsuffixed literals yield uncollapsed compile-time literal types
        return lit.Literal.Kind switch
        {
            TokenKind.Integer => TypeRef.Literal(CompileTimeLiteralKind.Int, lit.Literal.Value),
            TokenKind.Float => TypeRef.Literal(CompileTimeLiteralKind.Float, lit.Literal.Value),
            TokenKind.String => TypeRef.Literal(CompileTimeLiteralKind.Str, lit.Literal.Value),
            TokenKind.Char => TypeRef.Literal(CompileTimeLiteralKind.Char, lit.Literal.Value),
            _ => TypeRef.Unresolved
        };
    }

    /// <summary>
    /// Finds all methods decorated with <see cref="FunctionFlags.Literal"/> on the specified target type
    /// that are capable of converting a compile-time literal into that type.
    /// </summary>
    /// <param name="targetType">The destination type to search.</param>
    /// <param name="kind">The kind of compile-time literal being converted.</param>
    /// <returns>A list of matching literal conversion method symbols.</returns>
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

    /// <summary>
    /// Resolves an identifier or named expression within the current lexical scope.
    /// Handles canonical booleans (<c>true</c>, <c>false</c>), <c>this</c> self-references in instance methods,
    /// hygienic symbol lookup, and binding references into the resolution tree.
    /// </summary>
    /// <param name="named">The named expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The resolved type of the named symbol, or <see cref="TypeRef.Unresolved"/> if not found.</returns>
    private TypeRef ResolveNamed(NamedExpression named, Scope scope, SymbolHandle containingFunction = default)
    {
        // 1. Reserved boolean literal keywords
        if (named.Identifier.MatchingKind is MatchingKeywordKind.True or MatchingKeywordKind.False)
            return boolType;

        // 2. 'this' receiver expression inside non-static member functions
        if (named.Identifier.MatchingKind is MatchingKeywordKind.This)
        {
            if (context.GetSymbol(containingFunction) is MethodSymbol m && !m.Flags.HasFlag(FunctionFlags.Static) && m.Parent is { } parentHandle)
            {
                if (lambdaCapturedSymbols.Count > 0)
                    lambdaCapturedSymbols.Peek().Add(m);

                context.ResolvedTree.AddReference(named, parentHandle);
                return TypeRef.Resolved(parentHandle);
            }
            return TypeRef.Unresolved;
        }

        // 3. Lexical scope lookup with hygiene tracking
        var hygiene = context.GetHygiene(named);
        var part = named is GenericNameExpression generic
            ? new SymbolPart(generic.Identifier, generic.GenericArguments.Count, hygiene)
            : new SymbolPart(named.Identifier, 0, hygiene);

        var syms = scope[new SymbolName(part)];
        if (syms.Count > 0)
        {
            var sym = syms[0];
            if (lambdaCapturedSymbols.Count > 0 && activeLambdaScopes.Count > 0)
            {
                var curLambdaScope = activeLambdaScopes.Peek();
                if (sym is LocalVariableSymbol or ParameterSymbol)
                {
                    if (!IsEnclosedBy(sym.EnclosingScope, curLambdaScope))
                        lambdaCapturedSymbols.Peek().Add(sym);
                }
            }

            context.ResolvedTree.AddReference(named, ResolutionContext.GetHandle(sym));
            return GetTypeFromSymbol(sym);
        }

        // 4. Fallback check for previously recorded resolution tree references
        if (context.ResolvedTree.TryGetReference(named, out var handle))
            return GetTypeFromHandle(handle);

        return TypeRef.Unresolved;
    }

    /// <summary>
    /// Checks whether an inner scope is enclosed by or identical to a target outer scope.
    /// </summary>
    private static bool IsEnclosedBy(Scope? inner, Scope target)
    {
        for (var curr = inner; curr != null; curr = curr.Parent)
        {
            if (curr == target) return true;
        }
        return false;
    }

    /// <summary>
    /// Extracts the semantic <see cref="TypeRef"/> associated with a declared symbol.
    /// </summary>
    /// <param name="sym">The symbol to inspect.</param>
    /// <returns>The resolved semantic type of the symbol.</returns>
    private TypeRef GetTypeFromSymbol(Symbol sym) => sym switch
    {
        LocalVariableSymbol local => local.Type,
        FieldSymbol field => field.Type,
        ParameterSymbol param => param.Type,
        GlobalVariableSymbol global => global.Type,
        TypeSymbol typeSymbol => TypeRef.Resolved(ResolutionContext.GetHandle(typeSymbol)),
        MethodSymbol method => method.Type != default && method.Type.Kind != TypeRefKind.Unresolved ? method.Type : (method.Flags.HasFlag(FunctionFlags.Static) ? TypeRef.Function(method.ReturnType, Array.Empty<TypeRef>()) : TypeRef.Delegate(method.ReturnType, Array.Empty<TypeRef>())),
        FunctionSymbol fn => fn.Type != default && fn.Type.Kind != TypeRefKind.Unresolved ? fn.Type : TypeRef.Function(fn.ReturnType, Array.Empty<TypeRef>()),
        PropertySymbol prop => prop.Type,
        _ => TypeRef.Unresolved
    };

    /// <summary>
    /// Resolves the semantic type of a symbol reference handle.
    /// </summary>
    /// <param name="handle">The symbol handle to inspect.</param>
    /// <returns>The resolved semantic type, or a resolved reference type to the handle.</returns>
    private TypeRef GetTypeFromHandle(SymbolHandle handle)
    {
        var sym = context.GetSymbol(handle);
        return sym is not null ? GetTypeFromSymbol(sym) : TypeRef.Resolved(handle);
    }

    /// <summary>
    /// Resolves the semantic type of a binary operator expression.
    /// <para>
    /// Resolution proceeds through five sequential tiers:
    /// 1. Uncollapsed literals on both sides (e.g. <c>10 + 20</c>): comparisons yield <c>bool</c>;
    ///    arithmetic preserves the uncollapsed literal kind.
    /// 2. Canonical boolean operations (<c>&amp;&amp;</c>, <c>||</c>) yielding <c>bool</c>.
    /// 3. Mixed binary expressions with one literal and one concrete operand (e.g. <c>10 + myInt64</c>):
    ///    dispatches to <see cref="TryResolveOneLiteralBinary"/> to find an operator overload on the concrete type
    ///    and collapse the literal to the operator parameter's expected type.
    /// 4. Concrete operand operator overload lookup via <see cref="DiscoveredOperators"/>
    ///    by inspecting explicit interface implementations (<c>&lt;T as Add&gt;::Add</c>) on either operand type.
    /// 5. Diagnostic reporting: emits <c>MH2001</c> if no compatible operator overload exists.
    /// </para>
    /// </summary>
    /// <param name="binary">The binary expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The resolved semantic type of the binary operation, or <see cref="TypeRef.Error"/> on failure.</returns>
    private TypeRef ResolveBinary(BinaryExpression binary, Scope scope, SymbolHandle containingFunction)
    {
        var lhs = ResolveExpression(binary.LeftExpression, scope, containingFunction);
        var rhs = ResolveExpression(binary.RightExpression, scope, containingFunction);

        var opKind = GetBinaryOperatorKind(binary.OperatorToken.Kind);
        if (opKind == OperatorKind.None)
            return TypeRef.Error;

        // 1. Uncollapsed literals on both sides:
        // Comparison operators evaluate to canonical bool; arithmetic preserves uncollapsed literal type
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

        // 2. Canonical bool operations (&&, ||)
        if ((opKind == OperatorKind.LogicalAnd || opKind == OperatorKind.LogicalOr) && IsBool(lhs) && IsBool(rhs))
            return boolType;

        // 3. One literal operand and one concrete operand (e.g. 10 + myInt64 or myInt64 + 10)
        // Collapses the literal operand directly to the operator parameter type without falling back to Int32
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

        // 5. No compatible operator overload found: report MH2001
        if (!lhs.IsUnresolved && !lhs.IsError && !lhs.IsInferred && !rhs.IsUnresolved && !rhs.IsError && !rhs.IsInferred)
        {
            var opName = KnownIntrinsics.GetOperatorName(opKind);
            context.Diagnostics.ReportTypeMismatch(
                binary.OperatorToken.Span,
                binary.OperatorToken.Value,
                binary.LeftExpression.GetSpan() ?? default,
                lhs.ToQualifiedString(context),
                binary.RightExpression.GetSpan() ?? default,
                rhs.ToQualifiedString(context),
                lhsLabel: lhs.ToLabelString(context, scope),
                rhsLabel: rhs.ToLabelString(context, scope),
                opName: opName,
                source: binary.GetSource());
        }

        return TypeRef.Error;
    }

    /// <summary>
    /// Attempts to resolve a binary operator where one operand is an unconstrained compile-time literal
    /// and the other is a concrete type.
    /// <para>
    /// Searches candidate operator interface implementations registered on the concrete operand type.
    /// If an operator method is found where the concrete parameter matches and the literal is compatible
    /// with the literal parameter, the literal operand is eagerly collapsed to the parameter type and bound.
    /// </para>
    /// </summary>
    /// <param name="kind">The binary operator kind.</param>
    /// <param name="binary">The binary expression syntax node.</param>
    /// <param name="literalExpr">The literal expression AST node.</param>
    /// <param name="literalType">The uncollapsed literal type of the literal operand.</param>
    /// <param name="concreteType">The concrete semantic type of the other operand.</param>
    /// <param name="isLhsLiteral"><see langword="true"/> if LHS is the literal; <see langword="false"/> if RHS is.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <param name="result">The resulting return type of the operator overload if found.</param>
    /// <returns><see langword="true"/> if an operator overload was found and bound; otherwise <see langword="false"/>.</returns>
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

    /// <summary>
    /// Maps a Maho binary operator token kind to its corresponding semantic <see cref="OperatorKind"/>.
    /// </summary>
    /// <param name="tokenKind">The token kind from the lexer.</param>
    /// <returns>The mapped <see cref="OperatorKind"/>, or <see cref="OperatorKind.None"/> if not an operator.</returns>
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

    /// <summary>
    /// Attempts to find a user-defined binary operator overload implemented on either operand type.
    /// <para>
    /// In Maho, operators are declared as explicit interface implementations (<c>&lt;T as Add&gt;::Add</c>).
    /// This method searches candidate operator methods registered in <see cref="DiscoveredOperators"/>
    /// that are implemented in the owned scope of either the LHS or RHS operand type.
    /// </para>
    /// </summary>
    /// <param name="kind">The operator kind to look up.</param>
    /// <param name="lhs">The semantic type of the left operand.</param>
    /// <param name="rhs">The semantic type of the right operand.</param>
    /// <param name="syntax">The syntax node to bind in the resolution tree if matched.</param>
    /// <param name="result">The return type of the matching operator method.</param>
    /// <returns><see langword="true"/> if a matching operator overload was found and bound; otherwise <see langword="false"/>.</returns>
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

    /// <summary>
    /// Resolves the semantic type of a unary operator expression.
    /// <para>
    /// Supports built-in pointer and reference operators (<c>&amp;</c> address-of, <c>*</c> dereference),
    /// canonical boolean negation (<c>!bool</c>), uncollapsed compile-time literals (preserving uncollapsed type),
    /// and user-defined unary operator overloads via <see cref="DiscoveredOperators"/>.
    /// Emits <c>MH2002</c> if no compatible unary operator exists.
    /// </para>
    /// </summary>
    /// <param name="unary">The unary expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The resolved semantic type of the unary operation, or <see cref="TypeRef.Error"/> on failure.</returns>
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

        // 2. Canonical bool negation
        if (opKind == OperatorKind.LogicalNot && IsBool(operand))
            return boolType;

        // 3. Uncollapsed literal unary (e.g. -10 maintains uncollapsed Int literal)
        if (operand.IsLiteral)
            return operand;

        // 4. Overload lookup through DiscoveredOperators for user-defined types
        if (opKind == OperatorKind.Try)
        {
            if (TryResolveUnaryOverload(opKind, operand, unary, out var tryResult))
            {
                if (tryResult.SpecialType is UnionSemanticType union && union.Variants.Count == 2)
                {
                    var returnPayloadType = union.Variants[0];
                    var failType = union.Variants[1];

                    // Check enclosing function return type
                    var enclosingReturn = GetEnclosingFunctionReturnType(containingFunction);
                    if (enclosingReturn is { IsError: false, IsUnresolved: false } encRet)
                    {
                        if (!encRet.IsAssignableFrom(failType, context) &&
                            !TryResolveImplicitCast(failType, encRet, out _))
                        {
                            context.Diagnostics.ReportReturnTypeMismatch(
                                unary.OperatorToken.Span,
                                failType.ToQualifiedString(context),
                                encRet.ToQualifiedString(context),
                                exprLabel: failType.ToLabelString(context, scope),
                                returnLabel: encRet.ToLabelString(context, scope),
                                source: unary.GetSource());
                        }
                    }

                    return returnPayloadType;
                }
                return tryResult;
            }
        }
        else if (TryResolveUnaryOverload(opKind, operand, unary, out var overloadResult))
        {
            return overloadResult;
        }

        // 5. No unary overload found: report MH2002
        if (!operand.IsUnresolved && !operand.IsError && !operand.IsInferred)
        {
            var opName = KnownIntrinsics.GetOperatorName(opKind);
            context.Diagnostics.ReportUnaryTypeMismatch(
                unary.OperatorToken.Span,
                unary.OperatorToken.Value,
                unary.Operand.GetSpan() ?? default,
                operand.ToQualifiedString(context),
                operandLabel: operand.ToLabelString(context, scope),
                opName: opName,
                source: unary.GetSource());
        }

        return TypeRef.Error;
    }

    /// <summary>
    /// Maps a unary token kind and position (prefix vs. postfix) to its corresponding <see cref="OperatorKind"/>.
    /// </summary>
    /// <param name="tokenKind">The operator token kind.</param>
    /// <param name="position">Whether the unary operator appears as a prefix or postfix.</param>
    /// <returns>The mapped <see cref="OperatorKind"/>, or <see cref="OperatorKind.None"/> if unmapped.</returns>
    private static OperatorKind GetUnaryOperatorKind(TokenKind tokenKind, UnaryPosition position) => tokenKind switch
    {
        TokenKind.Minus => OperatorKind.UnaryNegation,
        TokenKind.Plus => OperatorKind.UnaryPositive,
        TokenKind.ExclamationMark => position == UnaryPosition.Prefix ? OperatorKind.LogicalNot : OperatorKind.Unwrap,
        TokenKind.QuestionMark => position == UnaryPosition.Postfix ? OperatorKind.Try : OperatorKind.None,
        TokenKind.Tilde => OperatorKind.BitwiseNot,
        TokenKind.PlusPlus => position == UnaryPosition.Prefix ? OperatorKind.PrefixIncrement : OperatorKind.PostfixIncrement,
        TokenKind.MinusMinus => position == UnaryPosition.Prefix ? OperatorKind.PrefixDecrement : OperatorKind.PostfixDecrement,
        TokenKind.Ampersand => OperatorKind.AddressOf,
        TokenKind.Asterisk => OperatorKind.Dereference,
        _ => OperatorKind.None
    };

    /// <summary>
    /// Attempts to find a user-defined unary operator overload implemented on the operand type.
    /// </summary>
    /// <param name="kind">The unary operator kind.</param>
    /// <param name="operand">The semantic type of the operand.</param>
    /// <param name="syntax">The syntax node to bind in the resolution tree if matched.</param>
    /// <param name="result">The return type of the matching unary operator method.</param>
    /// <returns><see langword="true"/> if a matching unary operator was found; otherwise <see langword="false"/>.</returns>
    private bool TryResolveUnaryOverload(OperatorKind kind, TypeRef operand, SyntaxNode syntax, out TypeRef result)
    {
        result = TypeRef.Unresolved;

        if (!context.DiscoveredOperators.TryGetOperators(kind, out var candidates))
            return false;

        var opHandle = operand.Handle;
        if (opHandle is null)
        {
            if (operand.Kind == TypeRefKind.Optional && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var hOpt))
                opHandle = hOpt;
            else if (operand.Kind == TypeRefKind.FixedArray && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.FixedArray, out var hFix))
                opHandle = hFix;
            else if (operand.Kind == TypeRefKind.Array && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var hArr))
                opHandle = hArr;
            else if (operand.Kind == TypeRefKind.Span && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Span, out var hSpan))
                opHandle = hSpan;
            else if (operand.Kind == TypeRefKind.Tuple && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var hTup))
                opHandle = hTup;
        }

        if (opHandle is null)
            return false;

        var operandTypeSym = (Symbol?)context.GetTypeSymbol(opHandle.Value) ?? context.GetSymbol(opHandle.Value);
        if (operandTypeSym is null)
            return false;

        // Build generic parameter mapping if operand is generic (e.g. Optional<T>)
        Dictionary<SymbolHandle, TypeRef>? typeArgs = null;
        if (operand.SpecialType is OptionalSemanticType optSem && operandTypeSym is TypeSymbol ts && ts.GenericParameters.Count == 1)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef> { [ts.GenericParameters[0]] = optSem.ElementType };
        }

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
                            var rawReturn = method.ReturnType;
                            result = typeArgs is not null ? SubstituteTypeRef(rawReturn, typeArgs) : rawReturn;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves an assignment expression (<c>=</c>, <c>+=</c>, <c>-=</c>, etc.).
    /// <para>
    /// Verifies that the LHS denotes a valid assignable L-value (<c>MH2003</c>).
    /// For simple assignment (<c>=</c>), if the RHS is an uncollapsed literal, immediately collapses
    /// the RHS (and its retroactive subtree) to the LHS type.
    /// Checks type assignability and implicit conversion operators (<see cref="TryResolveImplicitCast"/>),
    /// reporting <c>MH2004</c> on mismatch.
    /// </para>
    /// </summary>
    /// <param name="assignment">The assignment expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The resolved type of the assignment (the LHS type), or <see cref="TypeRef.Error"/> on failure.</returns>
    private TypeRef ResolveAssignment(AssignmentExpression assignment, Scope scope, SymbolHandle containingFunction)
    {
        var lhs = ResolveExpression(assignment.LhsExpression, scope, containingFunction);
        if (!lhs.IsInferred && !lhs.IsUnresolved && !lhs.IsError && !lhs.IsLiteral)
            expectedExpressionTypes[assignment.RhsExpression] = lhs;
        var rhs = ResolveExpression(assignment.RhsExpression, scope, containingFunction);

        // 1. Verify that the LHS expression denotes a valid writable L-value
        if (!IsLValue(assignment.LhsExpression))
        {
            context.Diagnostics.ReportCannotAssignToNonLValue(assignment.LhsExpression.GetSpan() ?? default, assignment.GetSource());
            return TypeRef.Error;
        }

        var opToken = assignment.EqualsOperator.Kind;
        if (opToken == TokenKind.Equals)
        {
            // 2a. If LHS is an unconstrained literal (e.g. var x = 100; x = Some();), collapse LHS to RHS type if compatible
            if (lhs.IsLiteral && !rhs.IsLiteral && !rhs.IsError && !rhs.IsUnresolved && !rhs.IsInferred)
            {
                if (IsLiteralCompatibleWith(lhs, rhs))
                {
                    var collapsed = CollapseExpression(assignment.LhsExpression, rhs, scope, containingFunction);
                    return collapsed.IsError ? TypeRef.Error : rhs;
                }
            }

            // 2b. If RHS is an unconstrained literal, eagerly collapse it to the target LHS type
            if (rhs.IsLiteral && !lhs.IsLiteral && !lhs.IsError && !lhs.IsUnresolved && !lhs.IsInferred)
            {
                var collapsed = CollapseExpression(assignment.RhsExpression, lhs, scope, containingFunction);
                return collapsed.IsError ? TypeRef.Error : lhs;
            }

            // 3. Verify type compatibility: assignability or registered implicit conversion operator
            if (!lhs.IsAssignableFrom(rhs, context) &&
                !TryResolveImplicitCast(rhs, lhs, out _) &&
                !lhs.IsError && !lhs.IsUnresolved && !lhs.IsInferred &&
                !rhs.IsError && !rhs.IsUnresolved && !rhs.IsInferred &&
                assignment.RhsExpression is not CollectionExpression)
            {
                if (TypeRef.IsNonContiguousSplitBase(lhs, rhs, context, out var splitBaseName))
                {
                    context.Diagnostics.ReportNonContiguousBaseConversion(
                        assignment.RhsExpression.GetSpan() ?? assignment.EqualsOperator.Span,
                        splitBaseName!,
                        assignment.GetSource());
                }
                else
                {
                    context.Diagnostics.ReportAssignmentTypeMismatch(
                        assignment.EqualsOperator.Span,
                        rhs.ToQualifiedString(context),
                        lhs.ToQualifiedString(context),
                        rhsLabel: rhs.ToLabelString(context, scope),
                        lhsLabel: lhs.ToLabelString(context, scope),
                        source: assignment.GetSource());
                }
            }

            return lhs;
        }

        // Compound assignments (+=, -=, etc.) return LHS type
        if (lhs.IsLiteral && !rhs.IsLiteral && !rhs.IsError && !rhs.IsUnresolved && !rhs.IsInferred)
        {
            if (IsLiteralCompatibleWith(lhs, rhs))
            {
                var collapsed = CollapseExpression(assignment.LhsExpression, rhs, scope, containingFunction);
                return collapsed.IsError ? TypeRef.Error : rhs;
            }
        }

        return lhs;
    }

    /// <summary>
    /// Determines whether the given expression denotes a valid L-value (assignable storage location).
    /// In Maho, valid L-values are named expressions, index expressions, member accesses, and pointer dereferences.
    /// </summary>
    /// <param name="expr">The AST expression node to inspect.</param>
    /// <returns><see langword="true"/> if the expression is an L-value; otherwise <see langword="false"/>.</returns>
    private static bool IsLValue(Expression expr) => expr switch
    {
        NamedExpression => true,
        IndexExpression => true,
        MemberAccessExpression => true,
        UnaryExpression u when u.OperatorToken.Kind == TokenKind.Asterisk => true,
        _ => false
    };

    /// <summary>
    /// Resolves the semantic type of a function, method, or callable operator invocation.
    /// <para>
    /// Handles member access receivers, functions/methods bound in the resolution tree,
    /// and user-defined types implementing the <see cref="OperatorKind.Callable"/> operator interface.
    /// Emits <c>MH2009</c> if the callee expression is not callable.
    /// </para>
    /// </summary>
    /// <param name="call">The call expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The resolved return type of the invocation, or <see cref="TypeRef.Error"/> on failure.</returns>
    private TypeRef ResolveCall(CallExpression call, Scope scope, SymbolHandle containingFunction)
    {
        // 1. If callee is member access on an uncollapsed literal receiver (e.g. 10.Abs()),
        // return uncollapsed receiver type so retroactive propagation can resolve the method once typed
        if (call.Callee is MemberAccessExpression access)
        {
            var recvType = ResolveExpression(access.Expression, scope, containingFunction);
            if (recvType.IsLiteral)
            {
                return recvType;
            }
        }

        var calleeType = ResolveExpression(call.Callee, scope, containingFunction);

        if (calleeType.IsLiteral)
        {
            if (expectedExpressionTypes.TryGetValue(call, out var expected) && !expected.IsLiteral && !expected.IsInferred)
            {
                return calleeType;
            }

            calleeType = CollapseLiteralToDefault(calleeType);
        }

        // Pre-propagate expected parameter types into argument expressions (for lambdas)
        if (calleeType.SpecialType is FunctionSemanticType fT)
        {
            for (int i = 0; i < call.Arguments.Count && i < fT.ParameterTypes.Count; i++)
                expectedExpressionTypes[call.Arguments[i]] = fT.ParameterTypes[i];
        }
        else if (calleeType.SpecialType is DelegateSemanticType dT)
        {
            for (int i = 0; i < call.Arguments.Count && i < dT.ParameterTypes.Count; i++)
                expectedExpressionTypes[call.Arguments[i]] = dT.ParameterTypes[i];
        }
        else if (context.ResolvedTree.TryGetReference(call.Callee, out var calleeHandleEarly))
        {
            var sym = context.GetSymbol(calleeHandleEarly);
            IReadOnlyList<SymbolHandle>? paramHandles = sym switch
            {
                FunctionSymbol fn => fn.Parameters,
                MethodSymbol m => m.Parameters,
                _ => null
            };
            if (paramHandles != null)
            {
                for (int i = 0; i < call.Arguments.Count && i < paramHandles.Count; i++)
                {
                    if (context.GetParameterSymbol(paramHandles[i]) is { } p)
                        expectedExpressionTypes[call.Arguments[i]] = p.Type;
                }
            }
        }

        var argTypes = new List<TypeRef>(call.Arguments.Count);
        foreach (var arg in call.Arguments)
            argTypes.Add(ResolveExpression(arg, scope, containingFunction));

        if (calleeType.IsError)
            return TypeRef.Error;
        if (calleeType.IsUnresolved)
            return calleeType;

        // All calls route through Callable operator
        if (!context.DiscoveredOperators.TryGetOperators(OperatorKind.Callable, out _))
        {
            context.Diagnostics.ReportMissingIntrinsicForSyntax(call.Callee.GetSpan() ?? default, "invocation", KnownIntrinsics.OpCallable, call.GetSource());
            return TypeRef.Error;
        }

        // 2. Function pointer / static function invocation (routes through Callable.Invoke)
        if (calleeType.SpecialType is FunctionSemanticType fnType)
        {
            if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Function))
            {
                context.Diagnostics.ReportMissingIntrinsicForSyntax(call.Callee.GetSpan() ?? default, "function call", KnownIntrinsics.Function, call.GetSource());
                return TypeRef.Error;
            }
            return TypeCheckFunctionTypeCall(call, fnType.ParameterTypes, fnType.ReturnType, argTypes, scope, containingFunction);
        }

        // 3. Delegate / instance method invocation (routes through Callable.Invoke)
        if (calleeType.SpecialType is DelegateSemanticType delType)
        {
            if (!context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Delegate))
            {
                context.Diagnostics.ReportMissingIntrinsicForSyntax(call.Callee.GetSpan() ?? default, "delegate call", KnownIntrinsics.Delegate, call.GetSource());
                return TypeRef.Error;
            }
            return TypeCheckFunctionTypeCall(call, delType.ParameterTypes, delType.ReturnType, argTypes, scope, containingFunction);
        }

        // 4. Instance invoking user-defined Callable operator: T(...)
        if (TryResolveCallableOperator(call, calleeType, argTypes, out var callableReturn))
            return callableReturn;

        // 5. Non-callable type error reporting: MH0611
        if (!calleeType.IsInferred)
        {
            context.Diagnostics.ReportTypeNotCallable(call.Callee.GetSpan() ?? default, calleeType.ToQualifiedString(context), labelTypeName: calleeType.ToLabelString(context, scope), source: call.GetSource());
            return TypeRef.Error;
        }

        return calleeType;
    }

    /// <summary>
    /// Attempts to resolve an invocation on an instance whose type implements the <see cref="OperatorKind.Callable"/> operator.
    /// </summary>
    /// <param name="call">The call expression syntax node.</param>
    /// <param name="calleeType">The semantic type of the receiver/callee object.</param>
    /// <param name="argTypes">The evaluated argument types.</param>
    /// <param name="result">The return type of the matching Callable method if found.</param>
    /// <returns><see langword="true"/> if a matching Callable operator was resolved; otherwise <see langword="false"/>.</returns>
    private bool TryResolveCallableOperator(CallExpression call, TypeRef calleeType, List<TypeRef> argTypes, out TypeRef result)
    {
        result = TypeRef.Unresolved;
        var typeHandle = calleeType.Handle;
        if (typeHandle is null)
        {
            if (calleeType.Kind == TypeRefKind.FixedArray && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.FixedArray, out var hFix))
                typeHandle = hFix;
            else if (calleeType.Kind == TypeRefKind.Array && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var hArr))
                typeHandle = hArr;
            else if (calleeType.Kind == TypeRefKind.Span && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Span, out var hSpan))
                typeHandle = hSpan;
            else if (calleeType.Kind == TypeRefKind.Tuple && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var hTup))
                typeHandle = hTup;
            else if (calleeType.Kind == TypeRefKind.Optional && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var hOpt))
                typeHandle = hOpt;
        }

        if (typeHandle is null)
            return false;

        var typeSym = (Symbol?)context.GetTypeSymbol(typeHandle.Value) ?? context.GetSymbol(typeHandle.Value);
        if (typeSym is null)
            return false;

        if (!context.DiscoveredOperators.TryGetOperators(OperatorKind.Callable, out var candidates))
            return false;

        // Build generic mapping if calleeType is generic (e.g. Optional<T>, FixedArray<T, N>)
        Dictionary<SymbolHandle, TypeRef>? typeArgs = null;
        if (calleeType.SpecialType is OptionalSemanticType optSem && typeSym is TypeSymbol ts1 && ts1.GenericParameters.Count >= 1)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef> { [ts1.GenericParameters[0]] = optSem.ElementType };
        }
        else if (calleeType.SpecialType is FixedArraySemanticType fixSem && typeSym is TypeSymbol ts2 && ts2.GenericParameters.Count >= 1)
        {
            typeArgs = new Dictionary<SymbolHandle, TypeRef> { [ts2.GenericParameters[0]] = fixSem.ElementType };
        }

        var typeScope = GetOwnedScope(typeSym);
        foreach (var candidate in candidates)
        {
            var ifaceSym = context.GetTypeSymbol(candidate.InterfaceHandle);
            if (ifaceSym is null) continue;

            IReadOnlyList<TypeRef> baseTypes = typeSym switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                _ => []
            };

            foreach (var sym in typeScope.Symbols.Values)
            {
                if (sym is MethodSymbol method)
                {
                    bool isCandidate = false;
                    if (method.Flags.HasFlag(FunctionFlags.InterfaceScoped))
                    {
                        if (method.ExplicitInterfaceName is { } ein && (ein.Last.Equals(ifaceSym.Name) || ein.Last.Text == ifaceSym.Name.Text))
                            isCandidate = true;
                    }
                    else if (baseTypes.Any(b => b.Handle == candidate.InterfaceHandle))
                    {
                        if (context.GetMethodSymbol(candidate.MethodHandle) is { } opMethod &&
                            (method.Name.Text == opMethod.Name.Text || method.Name.Text == "Invoke" || method.Name.Text == "Call"))
                        {
                            isCandidate = true;
                        }
                    }

                    if (isCandidate)
                    {
                        if (method.Parameters.Count == argTypes.Count)
                        {
                            bool matches = true;
                            for (int i = 0; i < argTypes.Count; i++)
                            {
                                if (context.GetParameterSymbol(method.Parameters[i]) is { } param)
                                {
                                    var effParam = typeArgs is not null ? SubstituteTypeRef(param.Type, typeArgs) : param.Type;
                                    if (!effParam.IsAssignableFrom(argTypes[i], context))
                                    {
                                        matches = false;
                                        break;
                                    }
                                }
                            }
                            if (matches)
                            {
                                context.ResolvedTree.AddReference(call, ResolutionContext.GetHandle(method));
                                var rawRet = method.ReturnType;
                                result = typeArgs is not null ? SubstituteTypeRef(rawRet, typeArgs) : rawRet;
                                return true;
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a parameter is declared with a variadic modifier (<c>...</c>).
    /// </summary>
    /// <param name="p">The parameter symbol to inspect.</param>
    /// <returns><see langword="true"/> if variadic; otherwise <see langword="false"/>.</returns>
    private static bool IsVariadicParameter(ParameterSymbol p) =>
        p.Syntax?.Declarator.Type is ModifiedType m && m.Modifier is VariadicTypeModifier;

    /// <summary>
    /// Determines whether a parameter has a default initial value expression.
    /// </summary>
    /// <param name="p">The parameter symbol to inspect.</param>
    /// <returns><see langword="true"/> if optional with a default value; otherwise <see langword="false"/>.</returns>
    private static bool HasDefaultValue(ParameterSymbol p) =>
        p.Syntax?.Initializer is not null;

    /// <summary>
    /// Type-checks call arguments against expected parameters, eagerly collapses unconstrained literal arguments,
    /// verifies argument counts, and checks assignability or implicit conversions.
    /// </summary>
    /// <param name="call">The call expression syntax node.</param>
    /// <param name="paramHandles">List of parameter symbol handles for the target function or method.</param>
    /// <param name="returnType">The declared return type of the callee.</param>
    /// <param name="argTypes">The evaluated argument types.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The return type of the validated call.</returns>
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

        // Verify argument count within [requiredCount, maxCount]
        if (argTypes.Count < requiredCount || argTypes.Count > maxCount)
        {
            context.Diagnostics.ReportArgumentCountMismatch(
                call.GetSpan() ?? default,
                expectedParams.Count,
                argTypes.Count,
                call.GetSource());
        }

        // Verify argument types and eagerly collapse literal arguments
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
                if (TypeRef.IsNonContiguousSplitBase(param.Type, argTypes[i], context, out var splitBaseName))
                {
                    context.Diagnostics.ReportNonContiguousBaseConversion(
                        call.Arguments[i].GetSpan() ?? default,
                        splitBaseName!,
                        call.GetSource());
                }
                else
                {
                    context.Diagnostics.ReportArgumentTypeMismatch(
                        call.Arguments[i].GetSpan() ?? default,
                        i + 1,
                        argTypes[i].ToQualifiedString(context),
                        param.Type.ToQualifiedString(context),
                        argLabel: argTypes[i].ToLabelString(context, scope),
                        paramLabel: param.Type.ToLabelString(context, scope),
                        source: call.GetSource());
                }
            }
        }

        return returnType;
    }

    private TypeRef TypeCheckFunctionTypeCall(CallExpression call, IReadOnlyList<TypeRef> expectedParamTypes, TypeRef returnType, List<TypeRef> argTypes, Scope scope, SymbolHandle containingFunction)
    {
        if (argTypes.Count != expectedParamTypes.Count)
        {
            context.Diagnostics.ReportArgumentCountMismatch(
                call.GetSpan() ?? default,
                expectedParamTypes.Count,
                argTypes.Count,
                call.GetSource());
        }

        for (int i = 0; i < argTypes.Count && i < expectedParamTypes.Count; i++)
        {
            var expectedType = expectedParamTypes[i];
            if (argTypes[i].IsLiteral)
            {
                CollapseExpression(call.Arguments[i], expectedType, scope, containingFunction);
                argTypes[i] = expectedType;
            }
            else if (!expectedType.IsAssignableFrom(argTypes[i], context) &&
                !TryResolveImplicitCast(argTypes[i], expectedType, out _) &&
                !argTypes[i].IsError && !argTypes[i].IsUnresolved && !argTypes[i].IsInferred &&
                !expectedType.IsError && !expectedType.IsUnresolved && !expectedType.IsInferred)
            {
                if (TypeRef.IsNonContiguousSplitBase(expectedType, argTypes[i], context, out var splitBaseName))
                {
                    context.Diagnostics.ReportNonContiguousBaseConversion(
                        call.Arguments[i].GetSpan() ?? default,
                        splitBaseName!,
                        call.GetSource());
                }
                else
                {
                    context.Diagnostics.ReportArgumentTypeMismatch(
                        call.Arguments[i].GetSpan() ?? default,
                        i + 1,
                        argTypes[i].ToQualifiedString(context),
                        expectedType.ToQualifiedString(context),
                        argLabel: argTypes[i].ToLabelString(context, scope),
                        paramLabel: expectedType.ToLabelString(context, scope),
                        source: call.GetSource());
                }
            }
        }

        return returnType;
    }

    /// <summary>
    /// Resolves a member access expression (<c>receiver.member</c>).
    /// <para>
    /// Handles static vs instance member access, sum type safety rules (preventing ordinary instance method
    /// invocations on untagged sum types <c>MH0608</c> and variant-level method calls on sum types <c>MH0609</c>),
    /// and hides interface-scoped operator methods from standard instance access.
    /// Emits <c>MH2008</c> if the member is not found.
    /// </para>
    /// </summary>
    /// <param name="access">The member access expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The resolved type of the member, or <see cref="TypeRef.Error"/>/<see cref="TypeRef.Unresolved"/> on failure.</returns>
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

        // If receiver is an uncollapsed literal, defer resolution until collapsed
        if (receiverType.IsLiteral)
        {
            return receiverType;
        }

        bool isArrowAccess = access.Dot.Kind == TokenKind.MinusGreaterThan;
        TypeRef originalReceiverType = receiverType;

        if (isArrowAccess)
        {
            if (receiverType.SpecialType is ReferenceSemanticType refType)
            {
                receiverType = refType.ElementType;
            }
            else if (receiverType.SpecialType is PointerSemanticType ptrType)
            {
                receiverType = ptrType.ElementType;
            }
            else if (TryResolveIndirectMemberAccessOperator(receiverType, out var elemType))
            {
                receiverType = elemType;
            }
            else
            {
                context.Diagnostics.ReportMemberNotFound(
                    access.Dot.Span,
                    "->",
                    receiverType.ToQualifiedString(context),
                    labelTypeName: receiverType.ToLabelString(context, scope),
                    source: access.GetSource());
                return TypeRef.Error;
            }
        }

        if (receiverType.SpecialType is DynSemanticType dynReceiver)
        {
            receiverType = dynReceiver.TargetType;
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

        // Untagged union and sum type member safety checks (instance members only!)
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
                    // MH0608: Calling ordinary instance method on untagged sum type without @untagged flag
                    if (isUntagged && !declaredMethod.Flags.HasFlag(FunctionFlags.Untagged))
                    {
                        context.Diagnostics.ReportUntaggedMethodMismatch(
                            access.Identifier.Span,
                            memberName,
                            receiverType.ToDisplayString(context),
                            access.GetSource());
                    }

                    context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(declaredMethod));
                    return GetTypeFromSymbol(declaredMethod);
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
        SymbolHandle? recvHandle = receiverType.Handle;
        if (recvHandle is null)
        {
            if (receiverType.SpecialType is ReferenceSemanticType)
            {
                if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Reference, out var refH))
                    recvHandle = refH;
            }
            else if (receiverType.SpecialType is PointerSemanticType)
            {
                if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Pointer, out var ptrH))
                    recvHandle = ptrH;
            }
            else if (receiverType.SpecialType is FunctionSemanticType)
            {
                if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Function, out var fnH))
                    recvHandle = fnH;
                else if (context.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null)
                    context.Diagnostics.ReportMissingIntrinsicForSyntax(access.Identifier.Span, "function member access", KnownIntrinsics.Function, access.GetSource());
            }
            else if (receiverType.SpecialType is DelegateSemanticType)
            {
                if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Delegate, out var delH))
                    recvHandle = delH;
                else if (context.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null)
                    context.Diagnostics.ReportMissingIntrinsicForSyntax(access.Identifier.Span, "delegate member access", KnownIntrinsics.Delegate, access.GetSource());
            }
        }

        if (recvHandle is not null)
        {
            bool isExplicitInterfaceAccess = access.Expression is TypeAsExpression;
            var (member, isAmbiguous, _) = FindMemberInTypeHierarchy(recvHandle.Value, memberName, isExplicitInterfaceAccess);

            if (isAmbiguous)
            {
                var typeSymObj = (Symbol?)context.GetTypeSymbol(recvHandle.Value) ?? context.GetSymbol(recvHandle.Value);
                context.Diagnostics.ReportAmbiguousMemberAccess(
                    access.Identifier.Span,
                    memberName,
                    typeSymObj?.Name.Text ?? receiverType.ToString(),
                    access.GetSource());
                return TypeRef.Error;
            }

            if (member is not null)
            {
                context.ResolvedTree.AddReference(access, ResolutionContext.GetHandle(member));
                return GetTypeFromSymbol(member);
            }

            if (!isStaticAccessOnType)
            {
                context.Diagnostics.ReportMemberNotFound(
                    access.Identifier.Span,
                    memberName,
                    originalReceiverType.ToQualifiedString(context),
                    labelTypeName: originalReceiverType.ToLabelString(context, scope),
                    source: access.GetSource());
                return TypeRef.Error;
            }
        }

        return TypeRef.Unresolved;
    }

    private bool TryResolveIndirectMemberAccessOperator(TypeRef type, out TypeRef elemType)
    {
        elemType = TypeRef.Unresolved;
        if (type.Handle is not { } handle)
            return false;

        if (context.DiscoveredOperators.TryGetOperators(OperatorKind.IndirectMemberAccess, out var candidates))
        {
            foreach (var cand in candidates)
            {
                if (TypeRef.IsSubtypeOf(handle, cand.InterfaceHandle, context))
                {
                    if (context.GetMethodSymbol(cand.MethodHandle) is { } m)
                    {
                        if (m.ReturnType.SpecialType is ReferenceSemanticType r)
                        {
                            elemType = r.ElementType;
                            return true;
                        }
                        elemType = m.ReturnType;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private (Symbol? Member, bool IsAmbiguous, List<Symbol>? Ambiguities) FindMemberInTypeHierarchy(
        SymbolHandle typeHandle,
        string memberName,
        bool isExplicitInterfaceAccess)
    {
        typeHandle = context.UnwrapAlias(typeHandle);
        var typeSymObj = (Symbol?)context.GetTypeSymbol(typeHandle) ?? context.GetSymbol(typeHandle);
        if (typeSymObj is null)
            return (null, false, null);

        var recvScope = GetOwnedScope(typeSymObj);
        var directSyms = recvScope[new SymbolName(new SymbolPart(memberName))];
        var directAccessible = directSyms.Where(cand =>
        {
            if (cand is MethodSymbol m)
            {
                if (isExplicitInterfaceAccess)
                    return true;
                return !m.Flags.HasFlag(FunctionFlags.InterfaceScoped);
            }
            return !isExplicitInterfaceAccess;
        }).ToList();

        if (directAccessible.Count > 0)
            return (directAccessible[0], false, null);

        IReadOnlyList<SymbolHandle> bases = typeSymObj switch
        {
            TypeSymbol ts => ts.LinearizedBases,
            NestedTypeSymbol nts => nts.LinearizedBases,
            _ => []
        };

        if (bases.Count <= 1)
        {
            IReadOnlyList<TypeRef> fallbackBases = typeSymObj switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                _ => []
            };

            if (fallbackBases.Count > 0)
            {
                var fbList = new List<SymbolHandle> { typeHandle };
                foreach (var fb in fallbackBases)
                {
                    if (fb.Handle is { } fbh)
                        fbList.Add(context.UnwrapAlias(fbh));
                }
                bases = fbList;
            }
        }

        if (bases.Count <= 1)
            return (null, false, null);

        var candidateBases = new List<(SymbolHandle BaseHandle, Symbol Member)>();
        foreach (var bh in bases.Skip(1))
        {
            var unwrappedBh = context.UnwrapAlias(bh);
            var bSym = (Symbol?)context.GetTypeSymbol(unwrappedBh) ?? context.GetSymbol(unwrappedBh);
            if (bSym is null)
                continue;

            var bScope = GetOwnedScope(bSym);
            var bSyms = bScope[new SymbolName(new SymbolPart(memberName))];
            var bAccessible = bSyms.Where(cand =>
            {
                if (cand is MethodSymbol m)
                {
                    if (isExplicitInterfaceAccess)
                        return true;
                    return !m.Flags.HasFlag(FunctionFlags.InterfaceScoped);
                }
                return !isExplicitInterfaceAccess;
            }).ToList();

            if (bAccessible.Count > 0)
            {
                candidateBases.Add((unwrappedBh, bAccessible[0]));
            }
        }

        if (candidateBases.Count == 0)
            return (null, false, null);

        if (candidateBases.Count == 1)
            return (candidateBases[0].Member, false, null);

        // Filter out candidates where BaseHandle is a supertype of another candidate's base handle
        var nonShadowed = candidateBases.Where(c1 =>
            !candidateBases.Any(c2 => c1.BaseHandle != c2.BaseHandle && TypeRef.IsSubtypeOf(c2.BaseHandle, c1.BaseHandle, context))
        ).ToList();

        // Deduplicate identical member handles (e.g. from shared root base)
        var uniqueMembers = nonShadowed
            .GroupBy(c => ResolutionContext.GetHandle(c.Member))
            .Select(g => g.First())
            .ToList();

        if (uniqueMembers.Count == 1)
            return (uniqueMembers[0].Member, false, null);

        return (null, true, uniqueMembers.Select(c => c.Member).ToList());
    }

    /// <summary>
    /// Resolves the semantic type of an indexer expression (<c>target[index]</c>).
    /// Supports built-in indexing for arrays, spans, and pointers, as well as user-defined indexer operators.
    /// </summary>
    /// <param name="index">The index expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The element type of the indexed target, or <see cref="TypeRef.Unresolved"/> on failure.</returns>
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

    /// <summary>
    /// Attempts to resolve a user-defined indexer getter operator on the target type.
    /// </summary>
    /// <param name="targetType">The type being indexed.</param>
    /// <param name="index">The index expression syntax node.</param>
    /// <param name="result">The return type of the indexer method.</param>
    /// <returns><see langword="true"/> if a matching indexer was found; otherwise <see langword="false"/>.</returns>
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

    /// <summary>
    /// Resolves an <c>if</c> conditional expression.
    /// <para>
    /// Validates condition expression (<c>MH2006</c> if not boolean).
    /// Maho enforces strict exact branch matching without implicit widening: both branches must evaluate
    /// to the exact same concrete type or compatible literal type, emitting <c>MH2011</c> if they diverge.
    /// </para>
    /// </summary>
    /// <param name="conditional">The if-expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The common branch type, <see cref="TypeRef.Void"/> (if no else branch), or <see cref="TypeRef.Error"/>.</returns>
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
                condType.ToQualifiedString(context),
                actualLabel: condType.ToLabelString(context, ifScope),
                source: conditional.GetSource());
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
            thenType.ToQualifiedString(context),
            elseType.ToQualifiedString(context),
            thenLabel: thenType.ToLabelString(context, ifScope),
            elseLabel: elseType.ToLabelString(context, ifScope),
            source: conditional.GetSource());

        return TypeRef.Error;
    }

    /// <summary>
    /// Resolves a <c>while</c> loop expression and validates that its condition evaluates to a boolean (<c>MH2006</c>).
    /// </summary>
    /// <param name="loop">The while expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns><see cref="TypeRef.Void"/>.</returns>
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
                condType.ToQualifiedString(context),
                actualLabel: condType.ToLabelString(context, whileScope),
                source: loop.GetSource());
        }

        ResolveExpression(loop.Body, whileScope, containingFunction);
        return voidType;
    }

    /// <summary>
    /// Resolves a block expression: enters a block scope, resolves local statements sequentially,
    /// evaluates the final tail expression (if any), and triggers <see cref="CollapseRemainingLocals"/>
    /// for any local literals left unconstrained at the end of the block.
    /// </summary>
    /// <param name="block">The block expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The semantic type of the final expression, or <see cref="TypeRef.Void"/> if none.</returns>
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

    /// <summary>
    /// Resolves a lambda expression, performing bidirectional type inference against expected function/delegate types,
    /// tracking captured symbols (locals, parameters, 'this'), and validating block body return semantics.
    /// </summary>
    /// <param name="lambda">The lambda expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>A <see cref="TypeRef"/> representing a Function or Delegate type.</returns>
    private TypeRef ResolveLambda(LambdaExpression lambda, Scope scope, SymbolHandle containingFunction)
    {
        FunctionSemanticType? expectedFn = null;
        DelegateSemanticType? expectedDel = null;

        if (expectedExpressionTypes.TryGetValue(lambda, out var expectedType))
        {
            if (expectedType.SpecialType is FunctionSemanticType f)
                expectedFn = f;
            else if (expectedType.SpecialType is DelegateSemanticType d)
                expectedDel = d;
        }

        IReadOnlyList<TypeRef>? contextualParamTypes = expectedFn?.ParameterTypes ?? expectedDel?.ParameterTypes;
        TypeRef? contextualReturnType = expectedFn?.ReturnType ?? expectedDel?.ReturnType;

        var lambdaScope = context.GetSyntaxScope(lambda, scope);
        var paramTypes = new List<TypeRef>(lambda.Parameters.Count);

        for (int i = 0; i < lambda.Parameters.Count; i++)
        {
            var pSyntax = lambda.Parameters[i];
            TypeRef pType;

            var paramPart = ResolutionContext.GetSymbolName(pSyntax.Identifier).Last;

            if (pSyntax.Type is not null)
            {
                pType = ResolveTypeSyntax(pSyntax.Type, scope);
            }
            else if (contextualParamTypes is not null && i < contextualParamTypes.Count)
            {
                pType = contextualParamTypes[i];
            }
            else
            {
                var span = pSyntax.Identifier.GetSpan() ?? default;
                context.Diagnostics.ReportCannotInferLambdaParameterType(span, paramPart.Text, pSyntax.Identifier.GetSource());
                pType = TypeRef.Error;
            }

            paramTypes.Add(pType);
            var paramSymbol = lambdaScope[paramPart].OfType<ParameterSymbol>().FirstOrDefault()
                ?? context.CreateParameterSymbol(lambdaScope, paramPart, containingFunction, null);
            paramSymbol.Type = pType;
        }

        TypeRef? explicitReturnType = null;
        if (lambda.ReturnType is not null)
        {
            explicitReturnType = ResolveTypeSyntax(lambda.ReturnType, lambdaScope);
        }

        TypeRef targetReturnType = explicitReturnType ?? contextualReturnType ?? TypeRef.Inferred;

        var captures = new HashSet<Symbol>();
        lambdaCapturedSymbols.Push(captures);
        activeLambdaScopes.Push(lambdaScope);

        TypeRef resolvedReturnType = voidType;

        try
        {
            if (lambda.Body is BlockExpression block)
            {
                if (block.FinalExpression is not null)
                {
                    var span = block.FinalExpression.GetSpan() ?? block.CloseBrace.Span;
                    context.Diagnostics.ReportLambdaBlockBodyTrailingExpression(span, block.CloseBrace.Source);
                }

                var blockScope = context.GetSyntaxScope(block, lambdaScope);
                var lambdaReturnList = new List<TypeRef>();
                lambdaReturnTypes.Push(lambdaReturnList);

                try
                {
                    foreach (var local in block.Locals)
                    {
                        ResolveLocal(local, blockScope, containingFunction, targetReturnType);
                    }
                }
                finally
                {
                    lambdaReturnTypes.Pop();
                }

                CollapseRemainingLocals(blockScope, containingFunction);

                if (explicitReturnType is { } expRet)
                {
                    resolvedReturnType = expRet;
                }
                else if (contextualReturnType is { } ctxRet && !ctxRet.IsInferred && !ctxRet.IsUnresolved)
                {
                    resolvedReturnType = ctxRet;
                }
                else if (lambdaReturnList.Count > 0)
                {
                    resolvedReturnType = lambdaReturnList[0];
                }
                else
                {
                    resolvedReturnType = voidType;
                }
            }
            else
            {
                // Expression body
                if (!targetReturnType.IsInferred && !targetReturnType.IsUnresolved && !targetReturnType.IsError)
                {
                    expectedExpressionTypes[lambda.Body] = targetReturnType;
                }

                var bodyType = ResolveExpression(lambda.Body, lambdaScope, containingFunction);

                if (explicitReturnType is { } expRet)
                {
                    if (bodyType.IsLiteral)
                        bodyType = CollapseExpression(lambda.Body, expRet, lambdaScope, containingFunction);

                    if (!expRet.IsAssignableFrom(bodyType, context) &&
                        !TryResolveImplicitCast(bodyType, expRet, out _))
                    {
                        var span = lambda.Body.GetSpan() ?? lambda.EqualsGreaterThanToken.Span;
                        context.Diagnostics.ReportReturnTypeMismatch(
                            span,
                            bodyType.ToQualifiedString(context),
                            expRet.ToQualifiedString(context),
                            exprLabel: bodyType.ToLabelString(context, lambdaScope),
                            returnLabel: expRet.ToLabelString(context, lambdaScope),
                            source: lambda.EqualsGreaterThanToken.Source);
                    }
                    resolvedReturnType = expRet;
                }
                else if (contextualReturnType is { } ctxRet && !ctxRet.IsInferred && !ctxRet.IsUnresolved)
                {
                    if (bodyType.IsLiteral)
                        bodyType = CollapseExpression(lambda.Body, ctxRet, lambdaScope, containingFunction);

                    resolvedReturnType = ctxRet;
                }
                else
                {
                    resolvedReturnType = bodyType;
                }
            }
        }
        finally
        {
            activeLambdaScopes.Pop();
            lambdaCapturedSymbols.Pop();
        }

        // Non-capturing lambdas yield Function; capturing lambdas yield Delegate
        if (captures.Count > 0 || expectedDel is not null)
        {
            return TypeRef.Delegate(resolvedReturnType, paramTypes);
        }

        return TypeRef.Function(resolvedReturnType, paramTypes);
    }

    /// <summary>
    /// Retrieves the declared return type of the enclosing function, method, or property.
    /// </summary>
    /// <param name="containingFunction">Symbol handle to the enclosing symbol.</param>
    /// <returns>The declared return type, or <see cref="TypeRef.Void"/> if undefined.</returns>
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

    /// <summary>
    /// Resolves a <c>match</c> pattern matching expression.
    /// <para>
    /// Evaluates the governing expression, enforces sum type pattern matching safety (prohibiting match on
    /// untagged sum types without an explicit discriminant, <c>MH0606</c>), validates pattern arms against
    /// enum variants or union constituents (<c>MH0607</c>), checks exhaustiveness (<c>MH0605</c>),
    /// and determines the common arm result type.
    /// </para>
    /// </summary>
    /// <param name="match">The match expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The common return type of the match arms, or <see cref="TypeRef.Error"/>.</returns>
    private TypeRef ResolveMatch(MatchExpression match, Scope scope, SymbolHandle containingFunction)
    {
        var governingType = ResolveExpression(match.Expression, scope, containingFunction);

        // 1. Untagged sum types cannot be matched without an external discriminant: MH0606
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

        // 2. Validate patterns in each match arm and resolve arm body expressions
        foreach (var arm in match.Arms)
        {
            var armScope = context.GetSyntaxScope(arm, scope);
            ValidatePattern(arm.Pattern, armScope, governingType, sumTypeSym, unionType, ref hasWildcard, matchedVariants, matchedTypes, arm.GetSource());
            var armType = ResolveExpression(arm.Expression, armScope, containingFunction);
            if (armResultType.IsResolved && armResultType == voidType)
                armResultType = armType;
        }

        // 3. Exhaustiveness checking: verify that all variants or constituents are covered
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

    /// <summary>
    /// Validates an individual match arm pattern against the governing sum type or union type.
    /// Emits <c>MH0607</c> if a pattern specifies an invalid or unrecognized variant for the type.
    /// </summary>
    /// <param name="pattern">The pattern syntax node.</param>
    /// <param name="armScope">The lexical scope of the match arm.</param>
    /// <param name="governingType">The type of the matched expression.</param>
    /// <param name="sumTypeSym">The sum type symbol if the governing type is a sum type.</param>
    /// <param name="unionType">The union semantic type if the governing type is an anonymous union.</param>
    /// <param name="hasWildcard">Updated to <see langword="true"/> if a wildcard or catch-all variable pattern is encountered.</param>
    /// <param name="matchedVariants">Set of matched enum variant names.</param>
    /// <param name="matchedTypes">Set of matched constituent types.</param>
    /// <param name="source">The source file context.</param>
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

    /// <summary>
    /// Resolves an object creation expression implementing Maho's two-phase allocation and construction model.
    /// <para>
    /// In Maho, allocation and construction are strictly separated:
    /// 1. Phase 1 (Allocation): resolves the <see cref="OperatorKind.Put"/> operator for placement allocation
    ///    (<c>put&lt;arena&gt; Type(...)</c>) or the <see cref="OperatorKind.New"/> operator for heap allocation
    ///    (<c>new&lt;allocator&gt; Type(...)</c>), eagerly collapsing any literal allocation arguments.
    /// 2. Phase 2 (Construction): resolves constructor methods (<c>Construct</c> or <c>.ctor</c>) on the target type,
    ///    matching argument counts and types and collapsing literal arguments.
    /// </para>
    /// </summary>
    /// <param name="constructorCall">The constructor call syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The constructed <see cref="TypeRef"/>, or <see cref="TypeRef.Void"/> for placement <c>put</c>.</returns>
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

        // Phase 1: Allocation operator resolution (Put vs New)
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

        // Phase 2: Constructor method resolution on targetType
        if (targetType.Handle is { } targetHandle && context.GetTypeSymbol(targetHandle) is { } targetSym)
        {
            var targetScope = GetOwnedScope(targetSym);
            foreach (var sym in targetScope.Symbols.Values)
            {
                if (sym is MethodSymbol m && m.Name.Text == "Construct")
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
                            // Collapse literal arguments to the matched parameter types
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

    /// <summary>
    /// Resolves an explicit type-cast expression (<c>&lt;expr as TargetType&gt;</c>).
    /// </summary>
    /// <param name="typeAs">The type-as expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <returns>The resolved target type.</returns>
    private TypeRef ResolveTypeAs(TypeAsExpression typeAs, Scope scope) =>
        ResolveTypeSyntax(typeAs.TargetType, scope);

    /// <summary>
    /// Resolves a cast expression (<c>expr as TargetType</c>).
    /// </summary>
    /// <param name="asExpr">The as-expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <returns>The resolved target type.</returns>
    private TypeRef ResolveAs(AsExpression asExpr, Scope scope, SymbolHandle containingFunction)
    {
        var targetType = ResolveTypeSyntax(asExpr.Type, scope);
        var exprType = ResolveExpression(asExpr.Expression, scope, containingFunction);

        if (TypeRef.IsNonContiguousSplitBase(targetType, exprType, context, out var splitBaseName))
        {
            context.Diagnostics.ReportNonContiguousBaseConversion(
                asExpr.GetSpan() ?? default,
                splitBaseName!,
                asExpr.GetSource());
        }

        return targetType;
    }

    /// <summary>
    /// Resolves a collection literal expression (<c>[a, b, c]</c>) and types it as an array.
    /// </summary>
    /// <param name="collection">The collection expression syntax node.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>An array <see cref="TypeRef"/> wrapping the inferred element type.</returns>
    private TypeRef ResolveCollection(CollectionExpression collection, Scope scope, SymbolHandle containingFunction)
    {
        TypeRef elemType = TypeRef.Unresolved;
        foreach (var item in collection.Expressions)
        {
            var itemType = ResolveExpression(item, scope, containingFunction);
            if (elemType.IsUnresolved)
                elemType = itemType;
        }

        context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.FixedArray, out var fixedArrayHandle);
        return TypeRef.FixedArray(elemType, collection.Expressions.Count, fixedArrayHandle != default ? fixedArrayHandle : null);
    }

    /// <summary>
    /// Resolves a type syntax node to a semantic <see cref="TypeRef"/> by querying the lexical scope.
    /// </summary>
    /// <param name="syntax">The type syntax node to resolve.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <returns>The resolved <see cref="TypeRef"/>, or <see cref="TypeRef.Unresolved"/> if not found.</returns>
    private TypeRef ResolveTypeSyntax(TypeSyntax syntax, Scope scope)
    {
        if (syntax is ModifiedType modified)
        {
            var inner = ResolveTypeSyntax(modified.Type, scope);
            return modified.Modifier switch
            {
                PointerTypeModifier => TypeRef.Pointer(inner),
                ReferenceTypeModifier => TypeRef.Reference(inner),
                OptionalTypeModifier => TypeRef.Optional(inner),
                ArrayTypeModifier => TypeRef.Array(inner),
                _ => TypeRef.Error
            };
        }

        if (syntax is PrefixModifiedType prefixModified)
        {
            var inner = ResolveTypeSyntax(prefixModified.Type, scope);
            if (prefixModified.Modifier is DynTypeModifier)
                return TypeRef.Dyn(inner);
            if (prefixModified.Modifier is UntaggedTypeModifier)
                return TypeRef.Untagged(inner);
            return TypeRef.Error;
        }

        var name = ResolutionContext.GetSymbolName(syntax);
        var syms = scope.ResolveType(name);
        if (syms.Count > 0)
            return TypeRef.Resolved(ResolutionContext.GetHandle(syms[0]));

        return TypeRef.Unresolved;
    }

    /// <summary>
    /// Fallback resolution invoked at the end of a block or function scope:
    /// collapses any local variables still holding unconstrained compile-time literal types
    /// to their canonical intrinsic types (<c>Int32</c>, <c>Float64</c>, <c>String</c>, <c>Char</c>)
    /// and retroactively propagates that type into their initializer expression trees.
    /// </summary>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
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

    /// <summary>
    /// Core sequential eager collapsing and retroactive literal resolution engine.
    /// <para>
    /// When an uncollapsed literal expression tree encounters a concrete target type
    /// (such as through assignment, parameter passing, or explicit variable type annotation),
    /// this method propagates the target type downward through the expression tree:
    /// <list type="bullet">
    /// <item><description>Updates local variable symbols and collapses their initializer trees.</description></item>
    /// <item><description>Validates literal conversions and reports ambiguity (<c>MH2014</c>).</description></item>
    /// <item><description>Re-resolves binary operator overloads on the target type and re-binds method references.</description></item>
    /// <item><description>Re-resolves unary operator overloads on the target type.</description></item>
    /// <item><description>Re-resolves member access and call invocations (e.g. <c>10.Abs()</c>) against the target type,
    /// reporting member not found (<c>MH2008</c>) if missing.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="node">The root syntax node of the expression subtree being collapsed.</param>
    /// <param name="targetType">The concrete target semantic type to collapse into.</param>
    /// <param name="scope">The current lexical scope.</param>
    /// <param name="containingFunction">Handle to the enclosing function or method symbol.</param>
    /// <returns>The collapsed semantic <see cref="TypeRef"/>, or <see cref="TypeRef.Error"/> on failure.</returns>
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
                // Named expression: collapse referencing local variable symbol and its initializer tree
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
                // Boolean literals always remain canonical bool
                if (lit.Literal.MatchingKind is MatchingKeywordKind.True or MatchingKeywordKind.False)
                    return boolType;

                var literalKind = lit.Literal.Kind is TokenKind.Float ? CompileTimeLiteralKind.Float :
                                  lit.Literal.Kind is TokenKind.String ? CompileTimeLiteralKind.Str :
                                  lit.Literal.Kind is TokenKind.Char ? CompileTimeLiteralKind.Char : CompileTimeLiteralKind.Int;
                var literalOverloads = GetMatchingLiteralMethods(targetType, literalKind);
                if (literalOverloads.Count > 1)
                {
                    // Ambiguous literal conversion: multiple literal methods match
                    var overloadNames = string.Join(", ", literalOverloads.Select(m => m.Name.Text));
                    context.Diagnostics.ReportAmbiguousLiteralConversion(
                        lit.GetSpan() ?? default,
                        targetType.ToQualifiedString(context),
                        overloadNames,
                        labelTypeName: targetType.ToLabelString(context, scope),
                        source: lit.GetSource());
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
                // Re-resolve binary operator overload against the concrete target type
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
                    var opName = KnownIntrinsics.GetOperatorName(opKind);
                    context.Diagnostics.ReportTypeMismatch(
                        binary.OperatorToken.Span,
                        binary.OperatorToken.Value,
                        binary.LeftExpression.GetSpan() ?? default,
                        targetType.ToQualifiedString(context),
                        binary.RightExpression.GetSpan() ?? default,
                        targetType.ToQualifiedString(context),
                        lhsLabel: targetType.ToLabelString(context, scope),
                        rhsLabel: targetType.ToLabelString(context, scope),
                        opName: opName,
                        source: binary.GetSource());
                    context.SetExpressionType(binary, TypeRef.Error);
                    return TypeRef.Error;
                }

            case UnaryExpression unary:
                // Re-resolve unary operator overload against the concrete target type
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
                    var opName = KnownIntrinsics.GetOperatorName(uOpKind);
                    context.Diagnostics.ReportUnaryTypeMismatch(
                        unary.OperatorToken.Span,
                        unary.OperatorToken.Value,
                        unary.Operand.GetSpan() ?? default,
                        targetType.ToQualifiedString(context),
                        operandLabel: targetType.ToLabelString(context, scope),
                        opName: opName,
                        source: unary.GetSource());
                    context.SetExpressionType(unary, TypeRef.Error);
                    return TypeRef.Error;
                }

            case CallExpression call:
                // Re-resolve member call (e.g. 10.Abs()) against the collapsed target type
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
                            targetType.ToQualifiedString(context),
                            labelTypeName: targetType.ToLabelString(context, scope),
                            source: access.GetSource());
                        context.SetExpressionType(call, TypeRef.Error);
                        return TypeRef.Error;
                    }
                }
                else if (call.Callee is LiteralExpression litCallee)
                {
                    var argTypes = new List<TypeRef>(call.Arguments.Count);
                    foreach (var arg in call.Arguments)
                        argTypes.Add(ResolveExpression(arg, scope, containingFunction));

                    MethodSymbol? cand1 = null;
                    if (targetType.Handle is { } targetH && context.GetTypeSymbol(targetH) is { } targetTypeSym)
                    {
                        var litKind = litCallee.Literal.Kind is TokenKind.Float ? CompileTimeLiteralKind.Float :
                                      litCallee.Literal.Kind is TokenKind.String ? CompileTimeLiteralKind.Str :
                                      litCallee.Literal.Kind is TokenKind.Char ? CompileTimeLiteralKind.Char : CompileTimeLiteralKind.Int;
                        var litOverloads = GetMatchingLiteralMethods(targetType, litKind);
                        if (litOverloads.Count > 1)
                        {
                            var overloadNames = string.Join(", ", litOverloads.Select(m => m.Name.Text));
                            context.Diagnostics.ReportAmbiguousLiteralConversion(
                                litCallee.GetSpan() ?? default,
                                targetType.ToQualifiedString(context),
                                overloadNames,
                                labelTypeName: targetType.ToLabelString(context, scope),
                                source: call.GetSource());
                            context.SetExpressionType(call, TypeRef.Error);
                            return TypeRef.Error;
                        }
                        if (litOverloads.Count == 1)
                        {
                            if (TryResolveCallableOperator(call, targetType, argTypes, out var ret1) && targetType.IsAssignableFrom(ret1, context))
                            {
                                if (context.ResolvedTree.TryGetReference(call, out var mHandle) && context.GetMethodSymbol(mHandle) is { } m1)
                                    cand1 = m1;
                            }
                        }
                    }

                    MethodSymbol? cand2 = null;
                    var defaultType = CollapseLiteralToDefault(TypeRef.Literal(CompileTimeLiteralKind.Int, litCallee.Literal.Value));
                    if (TryResolveCallableOperator(call, defaultType, argTypes, out var ret2) && targetType.IsAssignableFrom(ret2, context))
                    {
                        if (context.ResolvedTree.TryGetReference(call, out var mHandle2) && context.GetMethodSymbol(mHandle2) is { } m2)
                            cand2 = m2;
                    }

                    if (cand1 != null && cand2 != null && cand1 != cand2)
                    {
                        var span = litCallee.GetSpan() ?? default;
                        context.Diagnostics.ReportAmbiguousLiteralConversion(span, targetType.ToQualifiedString(context), litCallee.Literal.Value, labelTypeName: targetType.ToLabelString(context, scope), source: call.GetSource());
                        context.SetExpressionType(call, TypeRef.Error);
                        return TypeRef.Error;
                    }

                    var chosenMethod = cand1 ?? cand2;
                    if (chosenMethod != null)
                    {
                        context.ResolvedTree.AddReference(call, ResolutionContext.GetHandle(chosenMethod));
                        context.SetExpressionType(call, chosenMethod.ReturnType);
                        return chosenMethod.ReturnType;
                    }

                    if (TryResolveCallableOperator(call, defaultType, argTypes, out var defRet))
                    {
                        context.SetExpressionType(call, defRet);
                        return defRet;
                    }

                    context.Diagnostics.ReportTypeNotCallable(litCallee.GetSpan() ?? default, defaultType.ToQualifiedString(context), labelTypeName: defaultType.ToLabelString(context, scope), source: call.GetSource());
                    context.SetExpressionType(call, TypeRef.Error);
                    return TypeRef.Error;
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

    /// <summary>
    /// Searches for a binary operator implementation on the target type matching the specified operator kind.
    /// </summary>
    /// <param name="kind">The binary operator kind.</param>
    /// <param name="targetType">The concrete target type implementing the operator.</param>
    /// <param name="opMethod">The matching operator method symbol if found.</param>
    /// <param name="lhsType">The required left-hand parameter type.</param>
    /// <param name="rhsType">The required right-hand parameter type.</param>
    /// <returns><see langword="true"/> if a matching operator was found; otherwise <see langword="false"/>.</returns>
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

    /// <summary>
    /// Searches for a unary operator implementation on the target type matching the specified operator kind.
    /// </summary>
    /// <param name="kind">The unary operator kind.</param>
    /// <param name="targetType">The concrete target type implementing the operator.</param>
    /// <param name="opMethod">The matching unary operator method symbol if found.</param>
    /// <param name="operandType">The required operand parameter type.</param>
    /// <returns><see langword="true"/> if a matching operator was found; otherwise <see langword="false"/>.</returns>
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

    /// <summary>
    /// Attempts to find an implicit conversion operator (<see cref="OperatorKind.ImplicitCast"/>)
    /// converting from <paramref name="from"/> type to <paramref name="to"/> type.
    /// </summary>
    /// <param name="from">The source semantic type.</param>
    /// <param name="to">The destination semantic type.</param>
    /// <param name="castMethod">The matching implicit cast method if found.</param>
    /// <returns><see langword="true"/> if a matching implicit conversion was found; otherwise <see langword="false"/>.</returns>
    private bool TryResolveImplicitCast(TypeRef from, TypeRef to, out MethodSymbol? castMethod)
    {
        castMethod = null;
        if (!context.DiscoveredOperators.TryGetOperators(OperatorKind.ImplicitCast, out var candidates))
            return false;

        var operandHandles = new List<SymbolHandle>();
        if (from.Handle is { } fh) operandHandles.Add(fh);
        else if (from.Kind == TypeRefKind.Optional && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var optFromH)) operandHandles.Add(optFromH);

        if (to.Handle is { } th && !operandHandles.Contains(th)) operandHandles.Add(th);
        else if (to.Kind == TypeRefKind.Optional && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Optional, out var optToH) && !operandHandles.Contains(optToH)) operandHandles.Add(optToH);

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

                Dictionary<SymbolHandle, TypeRef>? typeArgs = null;
                if (to.SpecialType is OptionalSemanticType optTo && operandTypeSym is TypeSymbol toSym && toSym.GenericParameters.Count == 1)
                {
                    typeArgs = new Dictionary<SymbolHandle, TypeRef> { [toSym.GenericParameters[0]] = optTo.ElementType };
                }
                else if (from.SpecialType is OptionalSemanticType optFrom && operandTypeSym is TypeSymbol fromSym && fromSym.GenericParameters.Count == 1)
                {
                    typeArgs = new Dictionary<SymbolHandle, TypeRef> { [fromSym.GenericParameters[0]] = optFrom.ElementType };
                }

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
                                var effectiveParam = typeArgs is not null ? SubstituteTypeRef(paramType, typeArgs) : paramType;
                                var effectiveReturn = typeArgs is not null ? SubstituteTypeRef(method.ReturnType, typeArgs) : method.ReturnType;

                                if ((effectiveParam.Equals(from) || effectiveParam.IsAssignableFrom(from, context)) &&
                                    (to.Equals(effectiveReturn) || to.IsAssignableFrom(effectiveReturn, context)))
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

    /// <summary>
    /// Searches for a publicly accessible member on the specified type by name,
    /// filtering out interface-scoped operator methods.
    /// </summary>
    /// <param name="targetType">The type to query.</param>
    /// <param name="memberName">The name of the member to find.</param>
    /// <returns>The matching symbol if found; otherwise <see langword="null"/>.</returns>
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

    /// <summary>
    /// Checks whether a literal expression syntax node is type-compatible with a destination target type.
    /// </summary>
    /// <param name="lit">The literal expression syntax node.</param>
    /// <param name="targetType">The destination type.</param>
    /// <returns><see langword="true"/> if compatible; otherwise <see langword="false"/>.</returns>
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

    /// <summary>
    /// Checks whether an uncollapsed compile-time literal semantic type is compatible with a destination target type.
    /// </summary>
    /// <param name="litType">The uncollapsed literal <see cref="TypeRef"/>.</param>
    /// <param name="targetType">The destination type.</param>
    /// <returns><see langword="true"/> if compatible; otherwise <see langword="false"/>.</returns>
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

    private static TypeRef SubstituteTypeRef(TypeRef type, Dictionary<SymbolHandle, TypeRef> typeArgs)
    {
        if (type.Handle is { } h && typeArgs.TryGetValue(h, out var substituted))
            return substituted;

        if (type.SpecialType is OptionalSemanticType o)
            return TypeRef.Optional(SubstituteTypeRef(o.ElementType, typeArgs), type.Handle);

        if (type.SpecialType is UnionSemanticType u)
            return TypeRef.Union([.. u.Variants.Select(v => SubstituteTypeRef(v, typeArgs))], u.IsUntagged);

        if (type.SpecialType is PointerSemanticType p)
            return TypeRef.Pointer(SubstituteTypeRef(p.ElementType, typeArgs));

        if (type.SpecialType is ReferenceSemanticType r)
            return TypeRef.Reference(SubstituteTypeRef(r.ElementType, typeArgs));

        if (type.SpecialType is ArraySemanticType a)
            return TypeRef.Array(SubstituteTypeRef(a.ElementType, typeArgs));

        if (type.SpecialType is SpanSemanticType s)
            return TypeRef.Span(SubstituteTypeRef(s.ElementType, typeArgs));

        if (type.SpecialType is FixedArraySemanticType f)
            return TypeRef.FixedArray(SubstituteTypeRef(f.ElementType, typeArgs), f.Size, type.Handle);

        if (type.SpecialType is DynSemanticType dyn)
            return TypeRef.Dyn(SubstituteTypeRef(dyn.TargetType, typeArgs));

        if (type.SpecialType is UntaggedSemanticType untagged)
            return TypeRef.Untagged(SubstituteTypeRef(untagged.InnerType, typeArgs));

        return type;
    }

    private TypeRef? GetEnclosingFunctionReturnType(SymbolHandle containingFunction)
    {
        if (containingFunction != default)
        {
            var sym = context.GetSymbol(containingFunction);
            if (sym is FunctionSymbol fn) return fn.ReturnType;
            if (sym is MethodSymbol m) return m.ReturnType;
        }
        return null;
    }
}
