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
            var right = ParseExpression(rbp);
            left = new UnaryExpression(opToken, right, UnaryPosition.Prefix);
        }
        else
            left = ParsePrimaryExpression();

        // LED: loop for postfix (prefer) and infix
        while (true)
        {
            if (CurrentToken.Kind is TokenKind.LeftParen)
            {
                var leftParen = Consume();
                var arguments = ParseExpressionArgumentList();

                Token rightParen;

                if (CurrentToken.Kind is not TokenKind.RightParen)
                {
                    diagnostics.ReportMissingToken(CurrentToken.Span, ")");
                    rightParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
                }
                else
                    rightParen = Consume();

                left = new CallExpression(left, leftParen, arguments, rightParen);
                continue;
            }
            else if (CurrentToken.Kind is TokenKind.LeftBracket)
            {
                var leftBracket = Consume();
                var index = ParseExpression();

                Token rightBracket;

                if (CurrentToken.Kind is not TokenKind.RightBracket)
                {
                    diagnostics.ReportMissingToken(CurrentToken.Span, "]");
                    rightBracket = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
                }
                else
                    rightBracket = Consume();

                left = new IndexExpression(left, leftBracket, index, rightBracket);
                continue;
            }
            else if (CurrentToken.Kind is TokenKind.Dot)
            {
                var dot = Consume();

                if (CurrentToken.Kind is not TokenKind.Identifier)
                {
                    diagnostics.ReportUnexpectedToken(CurrentToken.Span, CurrentToken.Value);
                    continue;
                }

                var identifier = Consume();
                left = new MemberAccessExpression(left, dot, identifier);
                continue;
            }

            (kind, length) = GetCombinedOperatorData();

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
                var right = ParseExpression(rbp);

                if (opTok.Kind is TokenKind.Equals)
                    left = new AssignmentExpression(left, opTok, right);
                else
                    left = new BinaryExpression(left, opTok, right);

                continue;
            }

            break;
        }

        return left;
    }

    /// <summary> Parses a primary expression without operator involvement. </summary>
    /// <returns> The primary expression node. </returns>
    private Expression ParsePrimaryExpression() => CurrentToken.Kind switch
    {
        TokenKind.LeftParen => ParseParenthesizedOrCastExpression(),
        TokenKind.LeftBrace => ParseBlockExpression(),
        TokenKind.LeftBracket => ParseCollectionExpression(),

        TokenKind.Identifier => CurrentToken.MatchingKind switch
        {
            MatchingKeywordKind.New or MatchingKeywordKind.Put => ParseObjectCreationExpression(),
            MatchingKeywordKind.If => ParseIfExpression(),
            _ => ParseNamedExpression()
        },

        _ => ParseLiteralExpression()
    };

    /// <summary> Parses a literal expression. </summary>
    /// <returns> The literal expression node. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LiteralExpression ParseLiteralExpression() => new LiteralExpression(Consume());

    private NamedExpression ParseNamedExpression()
    {
        var identifier = Consume();

        if (CurrentToken.Kind is TokenKind.LessThanSign && LooksLikeGenericArguments().Success)
        {
            var (lessThan, typeArguments, greaterThan) = ParseGenerics();
            return new GenericNameExpression(identifier, lessThan, typeArguments, greaterThan);
        }

        return new IdentifierNameExpression(identifier);
    }

    private Expression ParseParenthesizedOrCastExpression()
    {
        var (success, _) = LooksLikeCastExpression();

        if (success)
        {
            return ParseCastExpression();
        }

        return ParseParenthesizedExpression();
    }

    private ParenthesizedExpression ParseParenthesizedExpression()
    {
        var leftParen = Consume(); // consume '('
        var expression = ParseExpression();
        Token rightParen;

        if (CurrentToken.Kind is not TokenKind.RightParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "')'");
            rightParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            rightParen = Consume();

        return new ParenthesizedExpression(leftParen, expression, rightParen);
    }

    private CastExpression ParseCastExpression()
    {
        var leftParen = Consume();
        var type = ParseTypeSyntax();
        var rightParen = Consume();

        var expression = ParseExpression();

        return new CastExpression(leftParen, type, rightParen, expression);
    }

    private IfExpression ParseIfExpression()
    {
        var ifKeyword = Consume();
        Token openParen;

        if (CurrentToken.Kind is not TokenKind.LeftParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "(");
            openParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            openParen = Consume();

        var condition = ParseExpression();

        Token closeParen;

        if (CurrentToken.Kind is not TokenKind.RightParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ")");
            closeParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            closeParen = Consume();

        var thenExpression = ParseExpression();

        ElseExpression? elseExpression = null;

        if (CurrentToken.MatchingKind is MatchingKeywordKind.Else)
        {
            var elseKeyword = Consume();
            var elseExpr = ParseExpression();
            elseExpression = new ElseExpression(elseKeyword, elseExpr);
        }

        return new IfExpression(ifKeyword, openParen, condition, closeParen, thenExpression, elseExpression);
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
                    var local = ParseLocal(StatementParseMode.AllowFinalExpression);

                    if (local is LocalExpressionStatement expressionStatement && expressionStatement.IsFinalExpression)
                    {
                        finalExpression = expressionStatement.Expression;
                        break;
                    }

                    locals.Add(local);
                }
                break;

            case false:
                while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
                {
                    var local = ParseLocal(StatementParseMode.Normal);
                    locals.Add(local);
                }
                break;
        }

        Token closeBrace;

        if (CurrentToken.Kind is not TokenKind.RightBrace)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "}");
            closeBrace = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            closeBrace = Consume();

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
        var expressions = ParseExpressionList();

        Token rightBracket;

        if (CurrentToken.Kind is not TokenKind.RightBracket)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "]");
            rightBracket = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            rightBracket = Consume();

        return new CollectionExpression(leftBracket, expressions, rightBracket);
    }

    private SeparatedSyntaxList<Expression> ParseExpressionList()
    {
        List<SyntaxNode> nodesAndSeparators = [];
        bool wasCommaLast = false;

        while (CurrentToken.Kind is not TokenKind.RightBracket and not TokenKind.EndToken)
        {
            nodesAndSeparators.Add(ParseExpression());
            wasCommaLast = false;

            if (CurrentToken.Kind is TokenKind.Comma)
            {
                nodesAndSeparators.Add(Consume());
                wasCommaLast = true;
            }
            else
                break;
        }

        if (wasCommaLast)
            diagnostics.ReportUnexpectedToken(CurrentToken.Span, CurrentToken.Value);

        return new SeparatedSyntaxList<Expression>(nodesAndSeparators);
    }

    private ObjectCreationExpression ParseObjectCreationExpression()
    {
        var keyword = Consume();

        var kind = keyword.MatchingKind switch
        {
            MatchingKeywordKind.New => ObjectCreationKind.New,
            MatchingKeywordKind.Put => ObjectCreationKind.Put,
            _ => throw new System.Exception("Impossible case: keyword is guaranteed to be 'new' or 'put' from parent function.")
        };

        var type = ParseTypeSyntax();

        if (type is ModifiedType { Modifier: ArrayTypeModifier arrayModifier } arrayType && CurrentToken.Kind is not TokenKind.LeftParen)
        {
            var elementType = arrayType.Type;
            CollectionExpression? initializer = null;

            if (CurrentToken.Kind is TokenKind.LeftBracket)
                initializer = ParseCollectionExpression();

            return new ArrayCreationExpression(keyword, kind, elementType, arrayModifier.LeftBracket, arrayModifier.Size, arrayModifier.RightBracket, initializer);
        }

        Token openParen;

        if (CurrentToken.Kind is not TokenKind.LeftParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "(");
            openParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            openParen = Consume();

        var arguments = ParseExpressionArgumentList();

        Token closeParen;
        
        if (CurrentToken.Kind is not TokenKind.RightParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ")");
            closeParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            closeParen = Consume();

        return new ConstructorCallExpression(keyword, kind, type, openParen, arguments, closeParen);
    }

    private SeparatedSyntaxList<Expression> ParseExpressionArgumentList()
    {
        List<SyntaxNode> nodesAndSeparators = [];
        bool wasCommaLast = false;

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            nodesAndSeparators.Add(ParseExpression());
            wasCommaLast = false;

            if (CurrentToken.Kind is TokenKind.Comma)
            {
                nodesAndSeparators.Add(Consume());
                wasCommaLast = true;
            }
            else
                break;
        }

        if (wasCommaLast)
            diagnostics.ReportUnexpectedToken(CurrentToken.Span, CurrentToken.Value);

        return new SeparatedSyntaxList<Expression>(nodesAndSeparators);
    }
}