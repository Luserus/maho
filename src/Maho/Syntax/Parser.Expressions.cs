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
            var right = ParseExpectedExpression(anchor: MissingTokenAnchor.AfterPrevious);
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
                var rightParen = ExpectClosingToken(TokenKind.RightParen, "')'", "to close the argument list", TokenKind.Semicolon, TokenKind.RightBrace);

                left = new CallExpression(left, leftParen, arguments, rightParen);
                continue;
            }
            else if (CurrentToken.Kind is TokenKind.LeftBracket)
            {
                var leftBracket = Consume();
                var index = ParseExpectedExpression("for the index expression", MissingTokenAnchor.AfterPrevious);
                var rightBracket = ExpectClosingToken(TokenKind.RightBracket, "']'", "to close the index expression", TokenKind.Semicolon, TokenKind.RightBrace, TokenKind.RightParen);

                left = new IndexExpression(left, leftBracket, index, rightBracket);
                continue;
            }
            else if (CurrentToken.Kind is TokenKind.Dot)
            {
                var dot = Consume();
                var identifier = ExpectIdentifierToken("after '.'");
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
                var right = ParseExpectedExpression(anchor: MissingTokenAnchor.AfterPrevious);

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
        TokenKind.Integer or TokenKind.Float or TokenKind.Char or TokenKind.String => ParseLiteralExpression(),
        _ => CreateMissingExpression()
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
        var expression = ParseExpectedExpression("inside the parenthesized expression", MissingTokenAnchor.AfterPrevious);
        var rightParen = ExpectClosingToken(TokenKind.RightParen, "')'", "to close the parenthesized expression", TokenKind.Semicolon, TokenKind.RightBrace, TokenKind.RightBracket);

        return new ParenthesizedExpression(leftParen, expression, rightParen);
    }

    private CastExpression ParseCastExpression()
    {
        var leftParen = Consume();
        var type = ParseTypeSyntax();
        var rightParen = ExpectClosingToken(TokenKind.RightParen, "')'", "to close the cast type", TokenKind.Semicolon, TokenKind.RightBrace, TokenKind.RightBracket);
        var expression = ParseExpectedExpression("after the cast", MissingTokenAnchor.AfterPrevious);

        return new CastExpression(leftParen, type, rightParen, expression);
    }

    private IfExpression ParseIfExpression()
    {
        var ifKeyword = Consume();
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after 'if'");
        var condition = ParseExpectedExpression("for the 'if' condition", MissingTokenAnchor.AfterPrevious);
        var closeParen = ExpectClosingToken(TokenKind.RightParen, "')'", "to close the 'if' condition", TokenKind.LeftBrace, TokenKind.Semicolon, TokenKind.RightBrace);
        var thenExpression = ParseExpectedExpression("for the 'if' then-expression", MissingTokenAnchor.AfterPrevious);

        ElseExpression? elseExpression = null;

        if (CurrentToken.MatchingKind is MatchingKeywordKind.Else)
        {
            var elseKeyword = Consume();
            var elseExpr = ParseExpectedExpression("for the 'else' expression", MissingTokenAnchor.AfterPrevious);
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
        var closeBrace = ExpectClosingToken(TokenKind.RightBrace, "'}'", "to close the block");

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
        var rightBracket = ExpectClosingToken(TokenKind.RightBracket, "']'", "to close the collection expression", TokenKind.Semicolon, TokenKind.RightBrace, TokenKind.RightParen);

        return new CollectionExpression(leftBracket, expressions, rightBracket);
    }

    private SeparatedSyntaxList<Expression> ParseExpressionList(TokenKind delimiter)
    {
        List<SyntaxNode> nodesAndSeparators = [];
        bool wasCommaLast = false;

        while (CurrentToken.Kind != delimiter && CurrentToken.Kind is not TokenKind.EndToken)
        {
            nodesAndSeparators.Add(ParseExpectedExpression("in the expression list", MissingTokenAnchor.AfterPrevious));
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
            diagnostics.ReportExpectedExpression(CurrentToken.Span, GetTokenDisplay(CurrentToken), "after ',' in the collection expression");

        return new SeparatedSyntaxList<Expression>(nodesAndSeparators);
    }

    private CollectionInitializer ParseCollectionInitializer()
    {
        var leftBrace = Consume();
        var expressions = ParseExpressionList(TokenKind.RightBrace);
        var rightBrace = ExpectClosingToken(TokenKind.RightBracket, "'}'", "to close the collection initializer", TokenKind.Semicolon, TokenKind.RightBrace, TokenKind.RightParen);

        return new CollectionInitializer(leftBrace, expressions, rightBrace);
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

        var type = ParseTypeSyntax();

        if (type is ModifiedType { Modifier: ArrayTypeModifier arrayModifier } arrayType && CurrentToken.Kind is not TokenKind.LeftParen)
        {
            var elementType = arrayType.Type;
            CollectionInitializer? initializer = null;

            if (CurrentToken.Kind is TokenKind.LeftBrace)
                initializer = ParseCollectionInitializer();

            return new ArrayCreationExpression(keyword, kind, elementType, arrayModifier.LeftBracket, arrayModifier.Size, arrayModifier.RightBracket, initializer);
        }
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", $"after '{keyword.Value}'");

        var arguments = ParseExpressionArgumentList();
        var closeParen = ExpectClosingToken(TokenKind.RightParen, "')'", "to close the argument list", TokenKind.Semicolon, TokenKind.RightBrace);

        return new ConstructorCallExpression(keyword, kind, type, openParen, arguments, closeParen);
    }

    private SeparatedSyntaxList<Expression> ParseExpressionArgumentList()
    {
        List<SyntaxNode> nodesAndSeparators = [];
        bool wasCommaLast = false;

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            nodesAndSeparators.Add(ParseExpectedExpression("in the argument list", MissingTokenAnchor.AfterPrevious));
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
            diagnostics.ReportExpectedExpression(CurrentToken.Span, GetTokenDisplay(CurrentToken), "after ',' in the argument list");

        return new SeparatedSyntaxList<Expression>(nodesAndSeparators);
    }
}
