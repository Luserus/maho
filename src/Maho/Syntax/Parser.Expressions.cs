using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    /// <summary> Parses an expression. </summary>
    /// <returns> The expression node. </returns>
    private Expression ParseExpression(int minBindingPower = 0)
    {
        Expression left;

        // NUD: prefix operators or primary
        var (kind, length) = GetCombinedOperatorData();

        if (length > 0 && operatorTable.TryGetValue(kind, out var prefixEntry) && prefixEntry.IsPrefix)
        {
            // consume the prefix operator (combined)
            var opToken = ConsumeOperator();
            int rbp = prefixEntry.RightBindingPower;
            var right = ParseExpectedExpression(anchor: MissingTokenAnchor.AfterPrevious, minBindingPower: rbp);
            left = new UnaryExpression(opToken, right, UnaryPosition.Prefix);
        }
        else
            left = ParsePrimaryExpression();

        return ParseExpressionContinuation(left, minBindingPower);
    }

    /// <summary> Continues a parsed expression with postfix and infix operators. </summary>
    private Expression ParseExpressionContinuation(Expression left, int minBindingPower = 0)
    {
        while (true)
        {
            if (CurrentToken.Kind is TokenKind.LeftParen)
            {
                var leftParen = Consume();
                var arguments = ParseExpressionArgumentList();
                var rightParen = ExpectToken(TokenKind.RightParen, "')'", "to close the argument list");

                left = new CallExpression(left, leftParen, arguments, rightParen);
                continue;
            }
            else if (CurrentToken.Kind is TokenKind.LeftBracket)
            {
                var leftBracket = Consume();
                var indices = ParseExpressionList(TokenKind.RightBracket);
                var rightBracket = ExpectToken(TokenKind.RightBracket, "']'", "to close the index expression");

                left = new IndexExpression(left, leftBracket, indices, rightBracket);
                continue;
            }
            else if (CurrentToken.Kind is TokenKind.Dot)
            {
                var dot = Consume();
                var identifier = ExpectIdentifierToken("after '.'");
                left = new MemberAccessExpression(left, dot, identifier);

                continue;
            }
            else if (CurrentToken.MatchingKind is MatchingKeywordKind.As)
            {
                const int asBindingPower = 45;

                if (asBindingPower < minBindingPower)
                    break;

                var asKeyword = Consume();
                var targetType = ParseTypeSyntax();
                left = new AsExpression(left, asKeyword, targetType);

                continue;
            }
            else if (CurrentToken.Kind is TokenKind.LeftBrace && left is MemberAccessExpression or IdentifierNameExpression or ScopeResolutionExpression && LooksLikeTypeInitializer())
            {
                var initializer = ParseTypeInitializer();
                left = new VariantCreationExpression(left, initializer);
                continue;
            }

            var (kind, length) = GetCombinedOperatorData();

            if (kind is TokenKind.MinusGreaterThan)
            {
                if (90 < minBindingPower)
                    break;
                
                var arrow = ConsumeOperator();
                while (IsMinusGreaterThan())
                {
                    var nextArrow = ConsumeOperator();
                    left = new MemberAccessExpression(left, arrow, nextArrow);
                    arrow = nextArrow;
                }

                var identifier = ExpectIdentifierToken("after '->'");
                left = new MemberAccessExpression(left, arrow, identifier);
                continue;
            }

            if (length == 0)
                break; // no operator here

            if (!operatorTable.TryGetValue(kind, out var entry))
                break; // operator not in table -> stop (lexer may produce non-op tokens)

            // If postfix possible, prefer it
            if (entry.IsPostfix)
            {
                int lbp = entry.LeftBindingPower;

                if (lbp < minBindingPower)
                    break;

                // consume combined operator
                var opToken = ConsumeOperator();
                left = new UnaryExpression(opToken, left, UnaryPosition.Postfix);

                continue; // allow chaining
            }

            // If infix possible, handle it
            if (entry.IsInfix)
            {
                int lbp = entry.LeftBindingPower;
                if (lbp < minBindingPower)
                    break;

                // consume combined operator
                var opTok = ConsumeOperator();
                int rbp = entry.RightBindingPower;
                string? context = opTok.Kind is TokenKind.Equals or TokenKind.PlusEquals or TokenKind.MinusEquals 
                    or TokenKind.AsteriskEquals or TokenKind.ForwardSlashEquals or TokenKind.PercentageEquals
                    ? $"after '{opTok.Value}' in the assignment expression" 
                    : $"after '{opTok.Value}' in the binary expression";
                var right = ParseExpectedExpression(context: context, anchor: MissingTokenAnchor.AfterPrevious, minBindingPower: rbp);

                if (opTok.Kind is TokenKind.Equals or TokenKind.PlusEquals or TokenKind.MinusEquals 
                    or TokenKind.AsteriskEquals or TokenKind.ForwardSlashEquals or TokenKind.PercentageEquals)
                    left = new AssignmentExpression(left, opTok, right);
                else
                    left = new BinaryExpression(left, opTok, right);

                continue;
            }

            break;
        }

        return left;
    }

    private bool LooksLikeLambda()
    {
        if (CurrentToken.Kind is TokenKind.Identifier && Peek().Kind is TokenKind.Equals && Peek(2).Kind is TokenKind.GreaterThanSign)
            return true;

        if (CurrentToken.Kind is not TokenKind.LeftParen)
            return false;

        int probe = current + 1;
        int depth = 1;

        while (probe < tokens.Count && depth > 0)
        {
            if (tokens[probe].Kind is TokenKind.LeftParen)
                depth++;
            else if (tokens[probe].Kind is TokenKind.RightParen)
                depth--;

            probe++;
        }

        if (depth != 0 || probe >= tokens.Count)
            return false;

        bool IsProbeEqualsGreaterThan(int idx) =>
            idx + 1 < tokens.Count && tokens[idx].Kind is TokenKind.Equals && tokens[idx + 1].Kind is TokenKind.GreaterThanSign;

        bool IsProbeMinusGreaterThan(int idx) =>
            idx + 1 < tokens.Count && tokens[idx].Kind is TokenKind.Minus && tokens[idx + 1].Kind is TokenKind.GreaterThanSign;

        if (IsProbeEqualsGreaterThan(probe))
            return true;

        if (IsProbeMinusGreaterThan(probe))
        {
            int arrowProbe = probe + 2;
            while (arrowProbe < tokens.Count && tokens[arrowProbe].Kind is not TokenKind.Semicolon and not TokenKind.RightBrace and not TokenKind.EndToken)
            {
                if (IsProbeEqualsGreaterThan(arrowProbe))
                    return true;
                arrowProbe++;
            }
        }

        return false;
    }

    private LambdaExpression ParseLambdaExpression()
    {
        var parameters = new List<LambdaParameterSyntax>();
        Token? arrowToken = null;
        TypeSyntax? returnType = null;

        if (CurrentToken.Kind is TokenKind.Identifier && Peek().Kind is TokenKind.Equals && Peek(2).Kind is TokenKind.GreaterThanSign)
        {
            var ident = ParseNamedSyntax();
            parameters.Add(new LambdaParameterSyntax(ident, null));
        }
        else
        {
            ExpectToken(TokenKind.LeftParen, "'('", "in lambda expression");

            while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
            {
                if (CurrentToken.Kind is TokenKind.Identifier && (Peek().Kind is TokenKind.Comma or TokenKind.RightParen))
                {
                    var paramIdent = ParseNamedSyntax();
                    parameters.Add(new LambdaParameterSyntax(paramIdent, null));
                }
                else
                {
                    var paramType = ParseTypeSyntax();
                    var paramIdent = ParseNamedSyntax();
                    parameters.Add(new LambdaParameterSyntax(paramIdent, paramType));
                }

                if (CurrentToken.Kind is TokenKind.Comma)
                    Consume();
                else
                    break;
            }

            ExpectToken(TokenKind.RightParen, "')'", "to close lambda parameters");

            if (IsMinusGreaterThan())
            {
                arrowToken = ConsumeOperator();
                returnType = ParseTypeSyntax();
            }
        }

        var equalsGreaterThanToken = ConsumeArrow();

        Expression body;
        if (CurrentToken.Kind is TokenKind.LeftBrace)
        {
            var (openBrace, locals, finalExpression, closeBrace) = ParseBlock(allowFinalExpression: false);
            body = new BlockExpression(openBrace, locals, finalExpression, closeBrace);
        }
        else
        {
            body = ParseExpression();
        }

        return new LambdaExpression(parameters, arrowToken, returnType, equalsGreaterThanToken, body);
    }

    /// <summary> Parses a primary expression without operator involvement. </summary>
    /// <returns> The primary expression node. </returns>
    private Expression ParsePrimaryExpression()
    {
        if (LooksLikeLambda())
            return ParseLambdaExpression();

        return CurrentToken.Kind switch
        {
            TokenKind.Dollar => ParseMacroInvocationExpression(),
            TokenKind.LeftParen => ParseParenthesizedOrTupleExpression(),
            TokenKind.LeftBrace => ParseBlockExpression(),
            TokenKind.LeftBracket => ParseCollectionExpression(),
            TokenKind.LessThanSign => ParseTypeAsExpression(),
            TokenKind.Identifier => CurrentToken.MatchingKind switch
            {
                MatchingKeywordKind.New or MatchingKeywordKind.Put => ParseObjectCreationExpression(),
                MatchingKeywordKind.If => ParseIfExpression(),
                MatchingKeywordKind.While => ParseWhileExpression(),
                MatchingKeywordKind.For => ParseForExpression(),
                MatchingKeywordKind.Match => ParseMatchExpression(),
                MatchingKeywordKind.Try => ParseTryExpression(),
                MatchingKeywordKind.Nameof when Peek().Kind is TokenKind.LeftParen => ParseNameofExpression(),
                MatchingKeywordKind.Default => ParseDefaultExpression(),
                MatchingKeywordKind.True or MatchingKeywordKind.False or MatchingKeywordKind.Null => ParseLiteralExpression(),
                _ => ParsePrimaryIdentifierOrScopedExpression()
            },
            TokenKind.Integer or TokenKind.Float or TokenKind.Char or TokenKind.String
                or TokenKind.SuffixedInteger or TokenKind.SuffixedFloat or TokenKind.SuffixedChar or TokenKind.SuffixedString => ParseLiteralExpression(),
            _ => CreateMissingExpression()
        };
    }

    private NameofExpression ParseNameofExpression()
    {
        var keyword = Consume();
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after 'nameof'");
        var argument = ParseExpression();
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close 'nameof' expression");
        return new NameofExpression(keyword, openParen, argument, closeParen);
    }

    private DefaultExpression ParseDefaultExpression()
    {
        var keyword = Consume();
        if (CurrentToken.Kind is TokenKind.LeftParen)
        {
            var openParen = Consume();
            var explicitType = ParseTypeSyntax();
            var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close 'default(Type)' expression");
            return new DefaultExpression(keyword, openParen, explicitType, closeParen);
        }

        return new DefaultExpression(keyword);
    }

    private TypeAsExpression ParseTypeAsExpression()
    {
        var lessThan = Consume();
        var targetType = ParseTypeSyntax();
        Token asKeyword;
        if (CurrentToken.MatchingKind is MatchingKeywordKind.As)
        {
            asKeyword = Consume();
        }
        else
        {
            diagnostics.ReportExpectedToken(CurrentToken.Span, "'as'", GetTokenDisplay(CurrentToken), "in '<Type as Interface>' expression");
            asKeyword = CreateMissingToken();
        }

        var interfaceType = ParseTypeSyntax();
        var greaterThan = ExpectToken(TokenKind.GreaterThanSign, "'>'", "to close '<Type as Interface>' expression");
        return new TypeAsExpression(lessThan, targetType, asKeyword, interfaceType, greaterThan);
    }

    /// <summary> Parses a literal expression. </summary>
    /// <returns> The literal expression node. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LiteralExpression ParseLiteralExpression() => new LiteralExpression(Consume());

    private bool LooksLikeColonColonAt(int offset = 0)
    {
        var first = Peek(offset);
        if (first.Kind is TokenKind.ColonColon)
            return true;

        var second = Peek(offset + 1);
        return first.Kind is TokenKind.Colon &&
               second.Kind is TokenKind.Colon &&
               first.Span.End == second.Span.Start &&
               first.TrailingTrivia.Length == 0 &&
               second.LeadingTrivia.Length == 0;
    }

    private bool LooksLikeTurbofish()
    {
        if (!LooksLikeColonColonAt(0))
            return false;

        int afterColon = CurrentToken.Kind is TokenKind.ColonColon ? 1 : 2;
        return Peek(afterColon).Kind is TokenKind.LessThanSign;
    }

    private Token ConsumeColonColon()
    {
        if (CurrentToken.Kind is TokenKind.ColonColon)
            return Consume();

        var first = Consume();
        var second = Consume();
        return new Token(text, new TextSpan(first.Span.Start, second.Span.End - first.Span.Start), TokenKind.ColonColon, first.LeadingTrivia, second.TrailingTrivia);
    }

    private Expression ParsePrimaryIdentifierOrScopedExpression()
    {
        if ((CurrentToken.MatchingKind is MatchingKeywordKind.Global || CurrentToken.Kind is TokenKind.Identifier) &&
            LooksLikeColonColonAt(1))
        {
            int afterColon = Peek(1).Kind is TokenKind.ColonColon ? 2 : 3;
            if (Peek(afterColon).Kind is not TokenKind.LessThanSign)
            {
                var qualifier = Consume();
                var colonColon = ConsumeColonColon();
                NamedExpression member = CurrentToken.Kind is TokenKind.Identifier
                    ? ParseNamedExpression()
                    : new IdentifierNameExpression(ExpectIdentifierToken("after '::'"));
                return new ScopeResolutionExpression(qualifier, colonColon, member);
            }
        }

        return ParseNamedExpression();
    }

    private NamedExpression ParseNamedExpression()
    {
        var identifier = Consume();

        if (LooksLikeTurbofish())
        {
            var colonColon = ConsumeColonColon();
            var (lessThan, genericArguments, greaterThan) = ParseGenerics();
            return new GenericNameExpression(identifier, colonColon, lessThan, genericArguments, greaterThan);
        }

        if (CurrentToken.Kind is TokenKind.LessThanSign && LooksLikeExpressionGenerics().ShouldParseAsGenerics)
        {
            var (lessThan, genericArguments, greaterThan) = ParseGenerics();
            return new GenericNameExpression(identifier, lessThan, genericArguments, greaterThan);
        }

        return new IdentifierNameExpression(identifier);
    }

    private Expression ParseParenthesizedOrTupleExpression()
    {
        var openParen = Consume(); // consume '('

        if (CurrentToken.Kind is TokenKind.RightParen)
        {
            var closeParen = Consume();
            return new TupleExpression(openParen, new SeparatedSyntaxList<Expression>([]), closeParen);
        }

        var firstExpr = ParseExpectedExpression("inside parentheses", MissingTokenAnchor.AfterPrevious);

        if (CurrentToken.Kind is TokenKind.Comma)
        {
            var nodesAndSeparators = new List<SyntaxNode> { firstExpr, Consume() };

            while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
            {
                var nextExpr = ParseExpectedExpression("in the tuple expression", MissingTokenAnchor.AfterPrevious);
                nodesAndSeparators.Add(nextExpr);

                if (CurrentToken.Kind is TokenKind.Comma)
                {
                    nodesAndSeparators.Add(Consume());
                }
                else
                    break;
            }

            var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the tuple expression");
            return new TupleExpression(openParen, new SeparatedSyntaxList<Expression>(nodesAndSeparators), closeParen);
        }

        var rightParen = ExpectToken(TokenKind.RightParen, "')'", "to close the parenthesized expression");
        return new ParenthesizedExpression(openParen, firstExpr, rightParen);
    }

    private IfExpression ParseIfExpression()
    {
        var ifKeyword = Consume();
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after 'if'");
        var (declaration, semicolon, condition) = ParseConditionalHeader("if");
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the 'if' condition");
        var thenExpression = ParseExpectedExpression("for the 'if' then-expression", MissingTokenAnchor.AfterPrevious);

        ElseExpression? elseExpression = null;

        if (CurrentToken.MatchingKind is MatchingKeywordKind.Else)
        {
            var elseKeyword = Consume();
            var elseExpr = ParseExpectedExpression("for the 'else' expression", MissingTokenAnchor.AfterPrevious);
            elseExpression = new ElseExpression(elseKeyword, elseExpr);
        }

        return new IfExpression(ifKeyword, openParen, declaration, semicolon, condition, closeParen, thenExpression, elseExpression);
    }

    private WhileExpression ParseWhileExpression()
    {
        var whileKeyword = Consume();
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after 'while'");
        var (declaration, semicolon, condition) = ParseConditionalHeader("while");
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the 'while' condition");
        var body = ParseExpectedExpression("for the 'while' body", MissingTokenAnchor.AfterPrevious);
        return new WhileExpression(whileKeyword, openParen, declaration, semicolon, condition, closeParen, body);
    }

    private ForExpression ParseForExpression()
    {
        var forKeyword = Consume();
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after 'for'");

        VariableDeclaration? declaration = null;
        Expression? variableExpr = null;

        if (LooksLikeForVariableDeclaration())
        {
            declaration = ParseVariableDeclaration();
        }
        else
        {
            variableExpr = ParseExpectedExpression("for loop variable", MissingTokenAnchor.AfterPrevious);
        }

        Token inKeyword;
        if (CurrentToken.MatchingKind is MatchingKeywordKind.In)
        {
            inKeyword = Consume();
        }
        else
        {
            diagnostics.ReportExpectedToken(CurrentToken.Span, "'in'", GetTokenDisplay(CurrentToken));
            inKeyword = CreateMissingToken();
        }

        var collection = ParseExpectedExpression("for loop collection", MissingTokenAnchor.AfterPrevious);
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the 'for' loop header");
        var body = ParseExpectedExpression("for the 'for' loop body", MissingTokenAnchor.AfterPrevious);

        return new ForExpression(forKeyword, openParen, declaration, variableExpr, inKeyword, collection, closeParen, body);
    }

    private (Token OpenBrace, IReadOnlyList<Local> Locals, Expression? FinalExpression, Token CloseBrace) ParseBlock(bool allowFinalExpression)
    {
        var openBrace = Consume();
        var locals = new List<Local>();
        Expression? finalExpression = null;

        switch (allowFinalExpression)
        {
            case true:
                while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
                {
                    var start = current;
                    var local = ParseLocal(StatementParseMode.AllowFinalExpression);

                    if (local is LocalExpressionStatement expressionStatement && expressionStatement.IsFinalExpression)
                    {
                        finalExpression = expressionStatement.Expression;
                        break;
                    }

                    locals.Add(local);
                    RecoverLocalIfStalled(start);
                }
                break;

            case false:
                while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
                {
                    var start = current;
                    var local = ParseLocal(StatementParseMode.Normal);
                    locals.Add(local);
                    RecoverLocalIfStalled(start);
                }
                break;
        }

        var closeBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close the block");

        return (openBrace, locals, finalExpression, closeBrace);
    }

    private BlockExpression ParseBlockExpression()
    {
        var (openBrace, locals, finalExpression, closeBrace) = ParseBlock(allowFinalExpression: true);

        return new BlockExpression(openBrace, locals, finalExpression, closeBrace);
    }

    private CollectionExpression ParseCollectionExpression()
    {
        var leftBracket = Consume();
        var expressions = ParseExpressionList(TokenKind.RightBracket);
        var rightBracket = ExpectToken(TokenKind.RightBracket, "']'", "to close the collection expression");
        var modifiers = ParseCollectionExpressionModifiers();

        return new CollectionExpression(leftBracket, expressions, rightBracket, modifiers);
    }

    private SeparatedSyntaxList<Expression> ParseExpressionList(TokenKind delimiter)
    {
        List<SyntaxNode> nodesAndSeparators = [];

        while (CurrentToken.Kind != delimiter && CurrentToken.Kind is not TokenKind.EndToken)
        {
            if (CurrentToken.Kind is TokenKind.Semicolon)
                break;

            var firstExpr = ParseExpectedExpression("after ',' in the expression list", MissingTokenAnchor.AfterPrevious);
            Expression elemExpr = firstExpr;
            if (delimiter == TokenKind.RightBracket && CurrentToken.Kind is TokenKind.Colon)
            {
                List<SyntaxNode> termNodes = [firstExpr];
                while (CurrentToken.Kind is TokenKind.Colon)
                {
                    termNodes.Add(Consume());
                    termNodes.Add(ParseExpectedExpression("after ':' in collection element"));
                }
                elemExpr = new MultiTermExpression(new SeparatedSyntaxList<Expression>(termNodes));
            }

            nodesAndSeparators.Add(elemExpr);

            if (CurrentToken.Kind is TokenKind.Comma)
            {
                nodesAndSeparators.Add(Consume());
            }
            else
                break;
        }

        return new SeparatedSyntaxList<Expression>(nodesAndSeparators);
    }

    private TypeInitializer ParseTypeInitializer()
    {
        var leftBrace = Consume();
        var expressions = ParseExpressionList(TokenKind.RightBrace);
        var rightBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close the type initializer");

        return new TypeInitializer(leftBrace, expressions, rightBrace);
    }

    private ObjectWithClause ParseObjectWithClause()
    {
        var withKeyword = Consume();
        var initializer = CurrentToken.Kind is TokenKind.LeftBrace
            ? ParseTypeInitializer()
            : new TypeInitializer(
                ExpectToken(TokenKind.LeftBrace, "'{'", "after 'with'"),
                new SeparatedSyntaxList<Expression>([]),
                ExpectToken(TokenKind.RightBrace, "'}'", "to close the with clause"));

        return new ObjectWithClause(withKeyword, initializer);
    }

    private IReadOnlyList<CollectionExpressionModifier> ParseCollectionExpressionModifiers()
    {
        List<CollectionExpressionModifier> modifiers = [];

        if (CurrentToken.MatchingKind is MatchingKeywordKind.With && Peek().Kind is TokenKind.LeftParen)
            modifiers.Add(ParseCollectionConstructorModifier());

        return modifiers;
    }

    private CollectionConstructorModifier ParseCollectionConstructorModifier()
    {
        var withKeyword = Consume();
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after 'with'");
        var arguments = ParseExpressionArgumentList();
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the collection constructor modifier");

        return new CollectionConstructorModifier(withKeyword, openParen, arguments, closeParen);
    }

    private ObjectCreationExpression ParseObjectCreationExpression()
    {
        var keyword = Consume();

        var kind = keyword.MatchingKind switch
        {
            MatchingKeywordKind.New => ObjectCreationKind.New,
            MatchingKeywordKind.Put => ObjectCreationKind.Put,
            _ => throw new ArgumentOutOfRangeException(nameof(keyword), keyword.MatchingKind, "Unhandled object creation keyword.")
        };

        Token? allocLessThan = null;
        SeparatedSyntaxList<TypeSyntax>? allocTypeArguments = null;
        Token? allocGreaterThan = null;

        if (CurrentToken.Kind is TokenKind.LessThanSign && LooksLikeGenericArguments().Success)
        {
            (allocLessThan, allocTypeArguments, allocGreaterThan) = ParseGenerics();
        }

        Token? allocOpenParen = null;
        SeparatedSyntaxList<Expression>? allocArguments = null;
        Token? allocCloseParen = null;

        if (LooksLikeAllocationArguments())
        {
            allocOpenParen = Consume();
            allocArguments = ParseExpressionArgumentList();
            allocCloseParen = ExpectToken(TokenKind.RightParen, "')'", "to close the allocation argument list");
        }

        var type = ParseTypeSyntax();

        if (type is ModifiedType modified && CurrentToken.Kind is not TokenKind.LeftParen)
        {
            if (modified.Modifier is ArrayTypeModifier arrayModifier)
            {
                var elementType = modified.Type;
                TypeInitializer? initializer = null;

                if (CurrentToken.Kind is TokenKind.LeftBrace)
                    initializer = ParseTypeInitializer();

                ObjectWithClause? withClause = null;

                if (CurrentToken.MatchingKind is MatchingKeywordKind.With && Peek().Kind is TokenKind.LeftBrace)
                    withClause = ParseObjectWithClause();

                return new ArrayCreationExpression(keyword, kind, elementType, arrayModifier.LeftBracket, null, arrayModifier.RightBracket, initializer, withClause);
            }
            else if (modified.Modifier is FixedArrayTypeModifier fixedModifier)
            {
                var elementType = modified.Type;
                TypeInitializer? initializer = null;

                if (CurrentToken.Kind is TokenKind.LeftBrace)
                    initializer = ParseTypeInitializer();

                ObjectWithClause? withClause = null;

                if (CurrentToken.MatchingKind is MatchingKeywordKind.With && Peek().Kind is TokenKind.LeftBrace)
                    withClause = ParseObjectWithClause();

                return new ArrayCreationExpression(keyword, kind, elementType, fixedModifier.LeftBracket, fixedModifier.Size, fixedModifier.RightBracket, initializer, withClause);
            }
        }
        if (CurrentToken.Kind is TokenKind.LeftBrace)
        {
            var initializer = ParseTypeInitializer();
            return new ObjectInitializerExpression(
                keyword,
                kind,
                allocLessThan,
                allocTypeArguments,
                allocGreaterThan,
                allocOpenParen,
                allocArguments,
                allocCloseParen,
                type,
                initializer);
        }

        var openParen = ExpectToken(TokenKind.LeftParen, "'('", GetObjectCreationContext(keyword.MatchingKind));

        var arguments = ParseExpressionArgumentList();
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the argument list");
        ObjectWithClause? objectWithClause = null;

        if (CurrentToken.MatchingKind is MatchingKeywordKind.With && Peek().Kind is TokenKind.LeftBrace)
            objectWithClause = ParseObjectWithClause();

        return new ConstructorCallExpression(
            keyword,
            kind,
            allocLessThan,
            allocTypeArguments,
            allocGreaterThan,
            allocOpenParen,
            allocArguments,
            allocCloseParen,
            type,
            openParen,
            arguments,
            closeParen,
            objectWithClause);
    }

    private SeparatedSyntaxList<Expression> ParseExpressionArgumentList()
    {
        List<SyntaxNode> nodesAndSeparators = [];

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            if (CurrentToken.Kind is TokenKind.Semicolon)
                break;

            nodesAndSeparators.Add(ParseArgumentExpression());

            if (CurrentToken.Kind is TokenKind.Comma)
            {
                nodesAndSeparators.Add(Consume());
            }
            else
                break;
        }

        return new SeparatedSyntaxList<Expression>(nodesAndSeparators);
    }

    private Expression ParseArgumentExpression()
    {
        if (CurrentToken.Kind is TokenKind.Identifier && Peek().Kind is TokenKind.Colon)
        {
            var name = Consume();
            var colon = Consume();
            var value = ParseExpectedExpression("after ':' in the named argument", MissingTokenAnchor.AfterPrevious);

            return new NamedArgumentExpression(name, colon, value);
        }

        return ParseExpectedExpression("in the argument list", MissingTokenAnchor.AfterPrevious);
    }

    private bool LooksLikeTypeInitializer()
    {
        if (CurrentToken.Kind is not TokenKind.LeftBrace)
            return false;

        if (Peek().Kind is TokenKind.RightBrace)
            return true;

        if (Peek().Kind is TokenKind.Identifier && Peek(2).Kind is TokenKind.Equals)
            return true;

        return false;
    }

    private MatchExpression ParseMatchExpression()
    {
        var matchKeyword = Consume();
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after 'match'");
        var expression = ParseExpectedExpression("for the match expression", MissingTokenAnchor.AfterPrevious);
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the match condition");
        var openBrace = ExpectToken(TokenKind.LeftBrace, "'{'", "to open the match body");

        var arms = new List<MatchArm>();

        while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
        {
            var pattern = ParsePattern();
            var arrowToken = ConsumeArrow();
            var armExpr = ParseExpectedExpression("for the match arm body", MissingTokenAnchor.AfterPrevious);
            Token? commaToken = null;

            if (CurrentToken.Kind is TokenKind.Comma)
                commaToken = Consume();

            arms.Add(new MatchArm(pattern, arrowToken, armExpr, commaToken));
        }

        var closeBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close the match body");
        return new MatchExpression(matchKeyword, openParen, expression, closeParen, openBrace, arms, closeBrace);
    }

    private TryExpression ParseTryExpression()
    {
        var tryKeyword = Consume();
        BlockExpression tryBlock;
        if (CurrentToken.Kind is TokenKind.LeftBrace)
        {
            tryBlock = ParseBlockExpression();
        }
        else
        {
            diagnostics.ReportExpectedToken(CurrentToken.Span, "'{'", GetTokenDisplay(CurrentToken), "after 'try'");
            tryBlock = new BlockExpression(CreateMissingToken(), Array.Empty<Local>(), null, CreateMissingToken());
        }

        var catchClauses = new List<CatchClause>();
        while (CurrentToken.MatchingKind is MatchingKeywordKind.Catch)
        {
            var catchKeyword = Consume();
            Token? openParen = null;
            TypeSyntax? type = null;
            Token? varId = null;
            Token? closeParen = null;

            if (CurrentToken.Kind is TokenKind.LeftParen)
            {
                openParen = Consume();
                if (CurrentToken.Kind is TokenKind.Identifier && CurrentToken.Span.Length == 1 && text[CurrentToken.Span.Start] == '_')
                {
                    varId = Consume();
                }
                else
                {
                    type = ParseTypeSyntax();
                    if (CurrentToken.Kind is TokenKind.Identifier)
                    {
                        varId = Consume();
                    }
                }
                closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close catch specification");
            }

            BlockExpression catchBlock;
            if (CurrentToken.Kind is TokenKind.LeftBrace)
            {
                catchBlock = ParseBlockExpression();
            }
            else
            {
                diagnostics.ReportExpectedToken(CurrentToken.Span, "'{'", GetTokenDisplay(CurrentToken), "for catch block");
                catchBlock = new BlockExpression(CreateMissingToken(), Array.Empty<Local>(), null, CreateMissingToken());
            }

            catchClauses.Add(new CatchClause(catchKeyword, openParen, type, varId, closeParen, catchBlock));
        }

        FinallyClause? finallyClause = null;
        if (CurrentToken.MatchingKind is MatchingKeywordKind.Finally)
        {
            var finallyKeyword = Consume();
            BlockExpression finallyBlock;
            if (CurrentToken.Kind is TokenKind.LeftBrace)
            {
                finallyBlock = ParseBlockExpression();
            }
            else
            {
                diagnostics.ReportExpectedToken(CurrentToken.Span, "'{'", GetTokenDisplay(CurrentToken), "for finally block");
                finallyBlock = new BlockExpression(CreateMissingToken(), Array.Empty<Local>(), null, CreateMissingToken());
            }

            finallyClause = new FinallyClause(finallyKeyword, finallyBlock);
        }

        if (catchClauses.Count == 0 && finallyClause is null)
        {
            diagnostics.ReportExpectedToken(CurrentToken.Span, "'catch' or 'finally'", GetTokenDisplay(CurrentToken), "after 'try' block");
        }

        return new TryExpression(tryKeyword, tryBlock, catchClauses, finallyClause);
    }

    private PatternSyntax ParsePattern()
    {
        // 1. Wildcard '_'
        if (CurrentToken.Kind is TokenKind.Identifier && CurrentToken.Span.Length == 1 && text[CurrentToken.Span.Start] == '_')
            return new WildcardPattern(Consume());

        // 2. Literal pattern
        if (IsLiteralTokenKind(CurrentToken.Kind))
            return new LiteralPattern(ParseLiteralExpression());

        // 3. Identifier-based pattern
        if (CurrentToken.Kind is TokenKind.Identifier)
        {
            TypeSyntax? typeQualifier = null;
            if (Peek().Kind is TokenKind.Dot)
            {
                var typeIdent = Consume();
                typeQualifier = new SimpleType(typeIdent);
                Consume(); // consume '.'
            }

            var nameIdent = Consume();
            var name = new SimpleName(nameIdent);

            // Check if followed by '(' -> Tuple variant pattern
            if (CurrentToken.Kind is TokenKind.LeftParen)
            {
                var openParen = Consume();
                var subPatterns = new List<SyntaxNode>();
                while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
                {
                    subPatterns.Add(ParsePattern());
                    if (CurrentToken.Kind is TokenKind.Comma)
                        subPatterns.Add(Consume());
                    else
                        break;
                }
                var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close tuple variant pattern");
                return EnumVariantPattern.Tuple(typeQualifier, name, openParen, new SeparatedSyntaxList<PatternSyntax>(subPatterns), closeParen);
            }

            // Check if followed by '{' -> Struct variant pattern
            if (CurrentToken.Kind is TokenKind.LeftBrace)
            {
                var openBrace = Consume();
                var fieldPatterns = new List<FieldPatternSyntax>();
                while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
                {
                    var fieldName = new SimpleName(ExpectIdentifierToken("for struct field pattern name"));
                    Token? colonToken = null;
                    PatternSyntax fieldPattern;
                    if (CurrentToken.Kind is TokenKind.Colon)
                    {
                        colonToken = Consume();
                        fieldPattern = ParsePattern();
                    }
                    else
                    {
                        fieldPattern = new VariablePattern(fieldName);
                    }
                    fieldPatterns.Add(new FieldPatternSyntax(fieldName, colonToken, fieldPattern));
                    if (CurrentToken.Kind is TokenKind.Comma)
                        Consume();
                }
                var closeBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close struct variant pattern");
                return EnumVariantPattern.Struct(typeQualifier, name, openBrace, fieldPatterns, closeBrace);
            }

            // Check if followed by an identifier (and not '=>') -> Type pattern with variable, e.g. A a => ...
            if (typeQualifier is null && CurrentToken.Kind is TokenKind.Identifier && CurrentToken.MatchingKind is not MatchingKeywordKind.As)
            {
                var varIdent = Consume();
                return new TypePattern(new SimpleType(nameIdent), new SimpleName(varIdent));
            }

            // Otherwise: Unit variant pattern (which also represents type pattern if used in union)
            return EnumVariantPattern.Unit(typeQualifier, name);
        }

        // 4. Parenthesized or complex type pattern: e.g. (int32 | Str) x
        if (CurrentToken.Kind is TokenKind.LeftParen)
        {
            var type = ParseTypeSyntax();
            NamedSyntax? varName = null;
            if (CurrentToken.Kind is TokenKind.Identifier)
                varName = new SimpleName(Consume());
            return new TypePattern(type, varName);
        }

        // Fallback: missing pattern
        diagnostics.ReportExpectedPattern(CurrentToken.Span, GetTokenDisplay(CurrentToken), "in match arm");
        return new WildcardPattern(new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []));
    }
}