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
internal sealed partial class ExpressionResolutionPass : ResolutionPass
{
    private ResolutionContext context = null!;

    // Cached primitive handles for zero-allocation checks
    private SymbolHandle? hInt8, hInt16, hInt32, hInt64, hInt128, hNInt;
    private SymbolHandle? hUInt8, hUInt16, hUInt32, hUInt64, hUInt128, hNUInt;
    private SymbolHandle? hFloat16, hFloat32, hFloat64, hFloat128;
    private SymbolHandle? hBool, hChar, hStr;

    private TypeRef voidType = TypeRef.EmptyTuple;
    private TypeRef boolType = TypeRef.Unresolved;

    /// <summary>Tracks visited function bodies to avoid redundant evaluation across multiple traversal passes.</summary>
    private readonly HashSet<FunctionBody> visitedBodies = [];

    /// <summary>Tracks visited variable declarations to avoid redundant evaluation across multiple traversal passes.</summary>
    private readonly HashSet<VariableDeclaration> visitedDeclarations = [];

    /// <summary>Tracks intermediate module-rooted namespace chains (<c>ModA::Ns</c> or <c>ModA::Ns.Sub</c>) across member access steps.</summary>
    private readonly Dictionary<Expression, List<(ResolutionContext Module, NamespaceTrieNode Node)>> scopedNamespaceChains = [];

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

    /// <summary>Tracks the current property accessor kind being resolved (get/set), if any.</summary>
    private PropertyAccessorKind? currentAccessorKind;

    private int currentCatchDepth = 0;
    private bool inFinallyBlock = false;

    /// <summary>
    /// Executes Pass 5: binds expression types, dispatches operators against library interfaces,
    /// and resolves retroactive literal collapsing across all statements.
    /// </summary>
    /// <param name="context">The active resolution context.</param>
    public override void Resolve(ResolutionContext context)
    {
        this.context = context;
        currentCatchDepth = 0;
        inFinallyBlock = false;
        CacheIntrinsicHandles();

        // 1. Resolve top-level statements / main scope locals
        ResolveTopLevelStatements();

        // 1.5. Resolve constants
        foreach (var constant in context.ConstantSymbols.ToArray())
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(constant));
            ResolveConstant(constant);
        }

        // 2. Resolve global variable initializers
        foreach (var global in context.GlobalVariableSymbols.ToArray())
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(global));
            if (global.Syntax is { } varDecl && visitedDeclarations.Add(varDecl))
                ResolveVariableDeclaration(varDecl, global.EnclosingScope, default);
        }

        // 3. Resolve field initializers
        foreach (var field in context.FieldSymbols.ToArray())
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(field));
            if (field.Syntax is { } varDecl && visitedDeclarations.Add(varDecl))
                ResolveVariableDeclaration(varDecl, field.EnclosingScope, default);
        }

        // 4. Resolve function bodies
        foreach (var function in context.FunctionSymbols.ToArray())
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(function));
            if (function.Syntax is { } syntax && visitedBodies.Add(syntax.Body))
            {
                var scope = context.GetSyntaxScope(syntax.Body, GetOwnedScope(function));
                ResolveFunctionBody(syntax.Body, scope, ResolutionContext.GetHandle(function), function.ReturnType);
            }
        }

        // 5. Resolve method bodies
        foreach (var method in context.MethodSymbols.ToArray())
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
        foreach (var property in context.PropertySymbols.ToArray())
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
                        currentAccessorKind = accessor.Kind;
                        ResolveFunctionBody(accessor.Body, scope, ResolutionContext.GetHandle(property), expectedReturn);
                        currentAccessorKind = null;
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

    /// <summary>Checks whether a type reference corresponds to empty tuple ().</summary>
    private bool IsVoidType(TypeRef t) => t.IsEmptyTuple;

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
                case TopLevelThrowStatement throwStmt:
                    ResolveTopLevelThrowStatement(throwStmt, scope, main);
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

            case LocalThrowStatement throwStmt:
                ResolveThrowStatement(throwStmt, scope, containingFunction);
                break;

            case LocalVariableDeclarationStatement varStmt:
                ResolveVariableDeclaration(varStmt.Declaration, scope, containingFunction);
                break;

            case LocalConstantDeclaration constStmt:
                ResolveConstantDeclaration(constStmt.Declaration, scope, containingFunction);
                break;

            case LocalYieldStatement yieldStmt:
                ResolveYieldStatement(yieldStmt, scope, containingFunction);
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
        if (inFinallyBlock)
        {
            context.Diagnostics.ReportControlLeavesFinally(returnStmt.Statement.Keyword.Span, "return", returnStmt.GetSource());
        }

        TextSpan span;

        if (returnStmt.Statement.Expression is { } expr)
        {
            if (!expectedReturnType.IsInferred && !expectedReturnType.IsUnresolved && !expectedReturnType.IsError && !expectedReturnType.IsLiteral)
                expectedExpressionTypes[expr] = expectedReturnType;

            var returnedType = ResolveExpression(expr, scope, containingFunction);
            span = expr.GetSpan() ?? returnStmt.Statement.Keyword.Span;

            if (returnedType.IsError || returnedType.Kind == TypeRefKind.Error)
                return;

            if (lambdaReturnTypes.Count > 0)
                lambdaReturnTypes.Peek().Add(returnedType);

            if (expectedReturnType.IsNever(context))
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
                else
                {
                    var unwrappedReturnExpr = UnwrapParentheses(returnStmt.Statement.Expression);
                    InterfaceConformance? chosenConf = ValidateConformanceConversion(
                        returnedType,
                        expectedReturnType,
                        returnStmt.Statement.Expression,
                        scope,
                        containingFunction);

                    if (chosenConf is null &&
                        context.ResolvedTree.TryGetConformance(unwrappedReturnExpr, out var existingConf) &&
                        existingConf is not null)
                    {
                        chosenConf = existingConf;
                    }

                    if (chosenConf is not null)
                    {
                        context.ResolvedTree.AddConformance(returnStmt.Statement.Expression, chosenConf);
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

            if (expectedReturnType.IsNever(context))
            {
                context.Diagnostics.ReportReturnTypeMismatch(
                    span,
                    "()",
                    "!",
                    exprLabel: "()",
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
                        "()",
                        expectedReturnType.ToQualifiedString(context),
                        exprLabel: "()",
                        returnLabel: expectedReturnType.ToLabelString(context, scope),
                        source: returnStmt.GetSource());
                }
            }
        }
    }

    private void ResolveThrowStatement(LocalThrowStatement throwStmt, Scope scope, SymbolHandle containingFunction)
    {
        if (throwStmt.Statement.Expression is { } expr)
        {
            var thrownType = ResolveExpression(expr, scope, containingFunction);
            if (thrownType.IsError || thrownType.Kind == TypeRefKind.Error)
                return;

            ValidateThrownType(thrownType, expr.GetSpan() ?? throwStmt.Statement.Keyword.Span, scope, throwStmt.GetSource());
        }
        else
        {
            if (currentCatchDepth <= 0)
                context.Diagnostics.ReportBareThrowOutsideCatch(throwStmt.Statement.Keyword.Span, throwStmt.GetSource());
        }
    }

    private void ResolveTopLevelThrowStatement(TopLevelThrowStatement throwStmt, Scope scope, SymbolHandle main)
    {
        if (throwStmt.Statement.Expression is { } expr)
        {
            var thrownType = ResolveExpression(expr, scope, main);
            if (thrownType.IsError || thrownType.Kind == TypeRefKind.Error)
                return;

            ValidateThrownType(thrownType, expr.GetSpan() ?? throwStmt.Statement.Keyword.Span, scope, throwStmt.GetSource());
        }
        else
        {
            if (currentCatchDepth <= 0)
                context.Diagnostics.ReportBareThrowOutsideCatch(throwStmt.Statement.Keyword.Span, throwStmt.GetSource());
        }
    }

    private void ValidateThrownType(TypeRef thrownType, TextSpan span, Scope scope, SourceText? source)
    {
        if (thrownType.Kind == TypeRefKind.Error)
            return;

        bool isRefOrPointer = TypeRef.UnwrapDyn(thrownType).SpecialType is ReferenceSemanticType or PointerSemanticType or AliasSemanticType;
        if (!isRefOrPointer && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Reference, out var refHandle) && thrownType.Handle == refHandle)
            isRefOrPointer = true;
        if (!isRefOrPointer && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.MutableReference, out var mutRefHandle) && thrownType.Handle == mutRefHandle)
            isRefOrPointer = true;
        if (!isRefOrPointer && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Pointer, out var ptrHandle) && thrownType.Handle == ptrHandle)
            isRefOrPointer = true;
        if (!isRefOrPointer && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.MutablePointer, out var mutPtrHandle) && thrownType.Handle == mutPtrHandle)
            isRefOrPointer = true;

        if (isRefOrPointer)
        {
            context.Diagnostics.ReportThrowMustBeValueOrMoved(span, thrownType.ToQualifiedString(context), source);
            return;
        }

        if (context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Throwable, out var throwableHandle))
        {
            var throwableType = TypeRef.Resolved(throwableHandle);
            if (!throwableType.IsAssignableFrom(thrownType, context))
            {
                context.Diagnostics.ReportTypeNotThrowable(
                    span,
                    thrownType.ToQualifiedString(context),
                    thrownType.ToLabelString(context, scope),
                    source);
            }
        }
        else
        {
            context.Diagnostics.ReportTypeNotThrowable(
                span,
                thrownType.ToQualifiedString(context),
                thrownType.ToLabelString(context, scope),
                source);
        }
    }

    /// <summary>
    /// Collapses an uncollapsed compile-time scalar literal type (<see cref="CompileTimeLiteralType"/>)
    /// into its default canonical intrinsic type:
    /// <list type="bullet">
    ///   <item><description><see cref="CompileTimeLiteralKind.Int"/> -> <c>int32</c></description></item>
    ///   <item><description><see cref="CompileTimeLiteralKind.Float"/> -> <c>float64</c></description></item>
    ///   <item><description><see cref="CompileTimeLiteralKind.Str"/> -> <c>&amp;Str</c></description></item>
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
                CompileTimeLiteralKind.Str => hStr is { } h ? TypeRef.Reference(TypeRef.Resolved(h)) : TypeRef.Inferred,
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
                if (TryResolveDeconstructTuple(initType, out var tupleSemantic, out var deconstructMethod))
                {
                    if (deconstructMethod != default)
                        context.ResolvedTree.AddReference(declaration, deconstructMethod);

                    if (tupleName.Elements.Count != tupleSemantic!.Elements.Count)
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
                        if (sym is LocalVariableSymbol && !initType.IsError && !initType.IsSized(context))
                        {
                            context.Diagnostics.ReportUnsizedTypeNotAllowed(
                                declarator.Identifier.GetSpan() ?? declarator.GetSpan() ?? default,
                                initType.ToDisplayString(context),
                                "as a local variable",
                                declaration.GetSource());
                        }
                    }
                }
                else if (sym is not null && symType is { } st && !st.IsInferred && !st.IsUnresolved && !st.IsError)
                {
                    if (sym is LocalVariableSymbol && !st.IsSized(context))
                    {
                        context.Diagnostics.ReportUnsizedTypeNotAllowed(
                            declaration.Type.GetSpan() ?? declarator.GetSpan() ?? default,
                            st.ToDisplayString(context),
                            "as a local variable",
                            declaration.GetSource());
                    }

                    if (initExpr is CollectionExpression colExpr)
                    {
                        TypeCheckCollectionExpression(colExpr, st, scope, containingFunction, declarator);
                        SetVariableSymbolType(sym, st);
                    }
                    else if (!initType.IsInferred && !initType.IsUnresolved && !initType.IsError)
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
                        else
                        {
                            ValidateConformanceConversion(initType, st, initExpr, scope, containingFunction);
                        }
                    }
                }
            }
            }
            else if (sym is LocalVariableSymbol && symType is { IsInferred: false, IsUnresolved: false, IsError: false } uninitType && !uninitType.IsSized(context))
            {
                context.Diagnostics.ReportUnsizedTypeNotAllowed(
                    declaration.Type.GetSpan() ?? declarator.GetSpan() ?? default,
                    uninitType.ToDisplayString(context),
                    "as a local variable",
                    declaration.GetSource());
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

    private void ResolveConstant(ConstantSymbol constant)
    {
        if (constant.Syntax is not { } syntax)
            return;

        var valType = ResolveExpression(syntax.Value, constant.EnclosingScope, default);

        if (syntax.Type is null)
        {
            constant.Type = valType;
        }
        else
        {
            if (valType.IsLiteral && constant.Type.IsResolved)
            {
                CollapseExpression(syntax.Value, constant.Type, constant.EnclosingScope, default);
            }
            else if (constant.Type.IsResolved && !constant.Type.IsAssignableFrom(valType, context))
            {
                context.Diagnostics.ReportAssignmentTypeMismatch(
                    syntax.Value.GetSpan() ?? default,
                    valType.ToQualifiedString(context),
                    constant.Type.ToQualifiedString(context),
                    source: syntax.Value.GetSource());
            }
        }
    }

    private void ResolveConstantDeclaration(ConstantDeclaration declaration, Scope scope, SymbolHandle containingFunction)
    {
        var valType = ResolveExpression(declaration.Value, scope, containingFunction);
        var name = ResolutionContext.GetSymbolName(declaration.Identifier).Last;
        var syms = scope[new SymbolName(name)];
        ConstantSymbol? constant = syms.OfType<ConstantSymbol>().FirstOrDefault();

        if (constant is not null)
        {
            if (declaration.Type is null)
            {
                constant.Type = valType;
            }
            else
            {
                if (valType.IsLiteral && constant.Type.IsResolved)
                {
                    CollapseExpression(declaration.Value, constant.Type, scope, containingFunction);
                }
                else if (constant.Type.IsResolved && !constant.Type.IsAssignableFrom(valType, context))
                {
                    context.Diagnostics.ReportAssignmentTypeMismatch(
                        declaration.Value.GetSpan() ?? default,
                        valType.ToQualifiedString(context),
                        constant.Type.ToQualifiedString(context),
                        source: declaration.Value.GetSource());
                }
            }
        }
    }

    internal InterfaceConformance? ValidateConformanceConversion(
        TypeRef srcType,
        TypeRef destType,
        SyntaxNode syntaxNode,
        Scope scope,
        SymbolHandle containingFunction,
        Token? explicitModuleQualifier = null)
    {
        if (srcType.IsError || destType.IsError || srcType.IsUnresolved || destType.IsUnresolved ||
            srcType.IsInferred || destType.IsInferred)
        {
            return null;
        }

        var lookup = context.ResolveInterfaceConformance(
            srcType,
            destType,
            explicitModuleQualifier,
            scope,
            containingFunction);

        switch (lookup.Status)
        {
            case ResolutionContext.ConformanceLookupStatus.Found:
                context.ResolvedTree.AddConformance(syntaxNode, lookup.Conformance!);
                return lookup.Conformance;

            case ResolutionContext.ConformanceLookupStatus.Inaccessible:
                context.Diagnostics.ReportInaccessibleInterfaceConformance(
                    syntaxNode.GetSpan() ?? default,
                    TypeRef.UnwrapConformance(srcType).ToDisplayString(context),
                    lookup.Conformance!.InterfaceType.ToDisplayString(context),
                    lookup.Conformance.Accessibility.ToString().ToLowerInvariant(),
                    syntaxNode.GetSource());
                return null;

            case ResolutionContext.ConformanceLookupStatus.Ambiguous:
                context.Diagnostics.ReportAmbiguousInterfaceConformance(
                    syntaxNode.GetSpan() ?? default,
                    TypeRef.UnwrapConformance(srcType).ToDisplayString(context),
                    TypeRef.UnwrapConformance(destType).ToDisplayString(context),
                    lookup.AmbiguousCandidates!.Select(c => c.ModuleName).Distinct().ToList(),
                    syntaxNode.GetSource());
                return null;

            case ResolutionContext.ConformanceLookupStatus.AmbiguousModule:
                context.Diagnostics.ReportAmbiguousModuleMember(
                    syntaxNode.GetSpan() ?? default,
                    lookup.AmbiguousModuleName!,
                    TypeRef.UnwrapConformance(destType).ToDisplayString(context),
                    syntaxNode.GetSource());
                return null;

            default:
                return null;
        }
    }

    private bool TryGetCallableEffectiveVisibility(SymbolHandle containingFunction, out Accessibility visibility)
    {
        visibility = Accessibility.Public;
        if (containingFunction == default || context.GetSymbol(containingFunction) is not { } fnSym)
            return false;

        if (fnSym is FunctionSymbol topFn)
        {
            visibility = AccessibilityExtensions.FromFunctionFlags(topFn.Flags, isTopLevel: true);
            return true;
        }

        Accessibility memberAcc;
        SymbolHandle? parentH;
        if (fnSym is MethodSymbol m)
        {
            memberAcc = AccessibilityExtensions.FromFunctionFlags(m.Flags, isTopLevel: false);
            parentH = m.Parent;
        }
        else if (fnSym is PropertySymbol p)
        {
            memberAcc = AccessibilityExtensions.FromFunctionFlags(p.Flags, isTopLevel: false);
            parentH = p.Parent;
        }
        else
        {
            return false;
        }

        Accessibility ownerAcc = Accessibility.Public;
        if (parentH is { } pH && context.GetSymbol(pH) is { } parentSym)
        {
            ownerAcc = parentSym switch
            {
                TypeSymbol ts => AccessibilityExtensions.FromTypeFlags(ts.Flags, isTopLevel: true),
                NestedTypeSymbol nts => AccessibilityExtensions.FromTypeFlags(nts.Flags, isTopLevel: false),
                ExtensionSymbol ext => ext.Accessibility,
                _ => Accessibility.Public
            };
        }

        visibility = (Accessibility)System.Math.Min((int)ownerAcc, (int)memberAcc);
        return true;
    }

    private static Expression UnwrapParentheses(Expression expr)
    {
        while (expr is ParenthesizedExpression paren)
            expr = paren.Expression;
        return expr;
    }

}
