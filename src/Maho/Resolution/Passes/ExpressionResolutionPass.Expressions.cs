using System;
using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class ExpressionResolutionPass
{
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

            case ScopeResolutionExpression scopeExpr:
                result = ResolveScopeResolution(scopeExpr, scope, containingFunction);
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
                if (tuple.Arguments.Count > 0 && context.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null && !context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Tuple))
                {
                    context.Diagnostics.ReportMissingIntrinsicForSyntax(tuple.GetSpan() ?? default, "tuple expression", KnownIntrinsics.Tuple, tuple.GetSource());
                }
                SymbolHandle? tupleIntrinsicHandle = null;
                if (tuple.Arguments.Count > 0 && context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var tH))
                    tupleIntrinsicHandle = tH;
                result = TypeRef.Tuple(elements, tupleIntrinsicHandle);
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

            case ForExpression forExpr:
                result = ResolveFor(forExpr, scope, containingFunction);
                break;

            case TryExpression tryExpr:
                result = ResolveTry(tryExpr, scope, containingFunction);
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

            case ObjectInitializerExpression objInit:
                result = ResolveObjectInitializer(objInit, scope, containingFunction);
                break;

            case DefaultExpression defaultExpr:
                result = ResolveDefaultExpression(defaultExpr, scope);
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

            case MultiTermExpression multiTerm:
                var termElements = new List<TupleElement>(multiTerm.Terms.Count);
                foreach (var term in multiTerm.Terms)
                {
                    var tType = ResolveExpression(term, scope, containingFunction);
                    termElements.Add(new TupleElement(tType, null));
                }
                context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var mtTupleHandle);
                result = TypeRef.Tuple(termElements, mtTupleHandle != default ? mtTupleHandle : null);
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
            var thisSyms = scope[new SymbolName(new SymbolPart(named.Identifier, 0, context.GetHygiene(named)))];
            if (thisSyms.Count > 0 && thisSyms[0] is ParameterSymbol thisParam)
            {
                context.ResolvedTree.AddReference(named, ResolutionContext.GetHandle(thisParam));
                return thisParam.Type;
            }

            var containingSym = context.GetSymbol(containingFunction);
            SymbolHandle? parentHandle = containingSym switch
            {
                MethodSymbol m when !m.Flags.HasFlag(FunctionFlags.Static) => m.Parent,
                PropertySymbol p when !p.Flags.HasFlag(FunctionFlags.Static) => p.Parent,
                _ => null
            };

            if (parentHandle is not null)
            {
                if (lambdaCapturedSymbols.Count > 0 && containingSym is not null)
                    lambdaCapturedSymbols.Peek().Add(containingSym);

                context.ResolvedTree.AddReference(named, parentHandle.Value);
                return TypeRef.Resolved(parentHandle.Value);
            }
            return TypeRef.Unresolved;
        }

        // Implicit 'value' parameter inside property setters
        if (named.Identifier.Value == "value" && currentAccessorKind is PropertyAccessorKind.Set && context.GetSymbol(containingFunction) is PropertySymbol prop)
        {
            return prop.Type;
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

            var symHandle = ResolutionContext.GetHandle(sym);
            context.ResolvedTree.AddReference(named, symHandle);

            if (named is GenericNameExpression genericExpr)
            {
                var resolvedArgs = new List<TypeRef>(genericExpr.GenericArguments.Count);
                foreach (var arg in genericExpr.GenericArguments)
                    resolvedArgs.Add(ResolveTypeSyntax(arg, scope));
                context.ValidateGenericTypeArgumentConformances(
                    symHandle,
                    [.. genericExpr.GenericArguments],
                    resolvedArgs,
                    scope,
                    containingFunction,
                    reportDiagnostics: true);
            }

            if (sym is MethodSymbol ifaceImplMethod &&
                ifaceImplMethod.Flags.HasFlag(FunctionFlags.InterfaceScoped) &&
                context.GetEnclosingOwnerHandle(containingFunction, scope) is { } ownerH)
            {
                foreach (var conf in context.Conformances)
                {
                    if (conf.OwnerHandle == ownerH && conf.MethodMap.ContainsValue(symHandle))
                    {
                        context.ResolvedTree.AddConformance(named, conf);
                        break;
                    }
                }
            }

            var resType = GetTypeFromSymbol(sym);
            if (sym is ConstantSymbol c && c.Value.Kind != ConstantValueKind.None)
                context.SetConstantValue(named, c.Value);
            return resType;
        }

        // 4. Fallback check for previously recorded resolution tree references
        if (context.ResolvedTree.TryGetReference(named, out var handle))
            return GetTypeFromHandle(handle);

        return TypeRef.Unresolved;
    }

    private TypeRef ResolveScopeResolution(ScopeResolutionExpression scopeExpr, Scope scope, SymbolHandle containingFunction)
    {
        if (context.ResolvedTree.TryGetReference(scopeExpr, out var existingH))
            return GetTypeFromHandle(existingH);

        var hygiene = context.GetHygiene(scopeExpr.Member);
        var part = scopeExpr.Member is GenericNameExpression generic
            ? new SymbolPart(generic.Identifier, generic.GenericArguments.Count, hygiene)
            : new SymbolPart(scopeExpr.Member.Identifier, 0, hygiene);

        List<TypeRef>? resolvedGenericArgs = null;
        if (scopeExpr.Member is GenericNameExpression genericExpr)
        {
            resolvedGenericArgs = new List<TypeRef>(genericExpr.GenericArguments.Count);
            foreach (var arg in genericExpr.GenericArguments)
                resolvedGenericArgs.Add(ResolveTypeSyntax(arg, scope));
        }

        var path = new SymbolName(part);
        var syms = context.ResolveScopedSymbols(
            scopeExpr.Qualifier,
            path,
            scope,
            typesOnly: false,
            scopeExpr.GetSpan() ?? default,
            scopeExpr.GetSource(),
            reportAmbiguity: true,
            out bool isAmbiguousAcrossModules);

        if (syms.Count > 0)
        {
            var sym = syms[0];
            var h = ResolutionContext.GetHandle(sym);
            context.ResolvedTree.AddReference(scopeExpr, h);
            context.ResolvedTree.AddReference(scopeExpr.Member, h);

            if (scopeExpr.Member is GenericNameExpression gExpr && resolvedGenericArgs is not null)
            {
                context.ValidateGenericTypeArgumentConformances(
                    h,
                    [.. gExpr.GenericArguments],
                    resolvedGenericArgs,
                    scope,
                    containingFunction,
                    reportDiagnostics: true);
            }

            if (sym is ConstantSymbol c && c.Value.Kind != ConstantValueKind.None)
                context.SetConstantValue(scopeExpr, c.Value);
            return GetTypeFromSymbol(sym);
        }

        if (!isAmbiguousAcrossModules && scopeExpr.Member is IdentifierNameExpression)
        {
            var nsMatches = context.ResolveScopedNamespaces(
                scopeExpr.Qualifier,
                path,
                scope,
                scopeExpr.GetSpan() ?? default,
                scopeExpr.GetSource(),
                reportAmbiguity: false);
            if (nsMatches.Count > 0)
                scopedNamespaceChains[scopeExpr] = nsMatches;
        }

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
        ConstantSymbol constant => constant.Type,
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

}
