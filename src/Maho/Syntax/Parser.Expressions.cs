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
                var arguments = ParseArgumentList();

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
        TokenKind.LeftParen => ParseParenthesizedExpression(),
        TokenKind.LeftCurlyBrace => ParseBlockExpression(),

        TokenKind.Identifier => CurrentToken.Value switch
        {
            "new" or "put" => ParseObjectCreationExpression(),
            "if" => ParseIfExpression(),
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
        var namedSyntax = ParseNamedSyntax();

        if (namedSyntax is GenericName genericName)
            return new GenericNameExpression(genericName);
        
        return new IdentifierExpression((IdentifierName)namedSyntax);
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

        if (CurrentToken.Value == "else")
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
                while (CurrentToken.Kind is not TokenKind.RightCurlyBrace and not TokenKind.EndToken)
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
                while (CurrentToken.Kind is not TokenKind.RightCurlyBrace and not TokenKind.EndToken)
                {
                    var local = ParseLocal(StatementParseMode.Normal);
                    locals.Add(local);
                }
                break;
        }

        Token closeBrace;

        if (CurrentToken.Kind is not TokenKind.RightCurlyBrace)
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

    private ObjectCreationExpression ParseObjectCreationExpression()
    {
        var keyword = Consume();

        var kind = keyword.Value switch
        {
            "new" => ObjectCreationKind.New,
            "put" => ObjectCreationKind.Put,
            _ => throw new System.Exception("Impossible case: keyword is guaranteed to be 'new' or 'put' from parent function.")
        };

        var type = ParseNamedSyntax();

        if (CurrentToken.Kind is not TokenKind.LeftParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "(");
            var fakeOpenParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
            var emptyArguments = new SeparatedSyntaxList<Expression>(new List<SyntaxNode>());
            var fakeCloseParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
            return new ObjectCreationExpression(keyword, kind, type, fakeOpenParen, emptyArguments, fakeCloseParen);
        }

        var openParen = Consume();
        var arguments = ParseArgumentList();

        Token closeParen;
        
        if (CurrentToken.Kind is not TokenKind.RightParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ")");
            closeParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            closeParen = Consume();

        return new ObjectCreationExpression(keyword, kind, type, openParen, arguments, closeParen);
    }

    private SeparatedSyntaxList<Expression> ParseArgumentList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();
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

        return new SeparatedSyntaxList<Expression>(nodesAndSeparators);
    }
}