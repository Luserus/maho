using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TopLevelEmptyStatement ParseTopLevelEmptyStatement() => new TopLevelEmptyStatement(Consume());
    
    private TopLevelStatement ParseTopLevelStatement()
    {
        switch (CurrentToken.Kind)
        {
            case TokenKind.Identifier:
                if (CurrentToken.Value == "if")
                    return ParseTopLevelIfStatement();
                else if (Peek().Kind is TokenKind.Identifier)
                    return ParseTopLevelVariableDeclarationStatement();
                break;

            case TokenKind.Semicolon:
                return ParseTopLevelEmptyStatement();

            case TokenKind.LeftCurlyBrace:
                return ParseTopLevelBlockStatement();
        }

        return ParseTopLevelExpressionStatement(allowFinalExpression: false);
    }

    private TopLevelExpressionStatement ParseTopLevelExpressionStatement(bool allowFinalExpression)
    {
        var expression = ParseExpression();
        Token semicolon;

        if (CurrentToken.Kind is not TokenKind.Semicolon && !allowFinalExpression)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ";");
            semicolon = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else if (CurrentToken.Kind is not TokenKind.Semicolon)
            semicolon = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []); // Fabricated semicolon
        else
            semicolon = Consume();

        return new TopLevelExpressionStatement(expression, semicolon, isFinalExpression: allowFinalExpression);
    }

    private TopLevelVariableDeclarationStatement ParseTopLevelVariableDeclarationStatement()
    {
        var modifiers = ParseModifiers();
        var type = ParseNamedSyntax();

        var nodesAndSeparators = new List<SyntaxNode>();

        while (CurrentToken.Kind is not TokenKind.EndToken and not TokenKind.Semicolon)
        {
            var identifier = new IdentifierName(Consume());

            AssignmentClause? initializer = null;

            if (CurrentToken.Kind is TokenKind.Equals)
            {
                var assignmentOp = Consume();
                var initExpr = ParseExpression();

                initializer = new AssignmentClause(assignmentOp, initExpr);
            }

            var declarator = new VariableDeclarator(identifier, initializer);

            nodesAndSeparators.Add(declarator);

            if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        Token semicolon;

        if (CurrentToken.Kind is not TokenKind.Semicolon)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ";");
            semicolon = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            semicolon = Consume();

        return new TopLevelVariableDeclarationStatement(type, new SeparatedSyntaxList<VariableDeclarator>(nodesAndSeparators), semicolon);
    }

    private TopLevelIfStatement ParseTopLevelIfStatement()
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

        var thenStatement = ParseTopLevelStatement();

        TopLevelElseStatement? elseStatement = null;

        if (CurrentToken.Value == "else")
        {
            var elseKeyword = Consume();
            var elseStmt = ParseTopLevelStatement();
            elseStatement = new TopLevelElseStatement(elseKeyword, elseStmt);
        }

        return new TopLevelIfStatement(ifKeyword, openParen, condition, closeParen, thenStatement, elseStatement);
    }

    private TopLevelWhileStatement ParseTopLevelWhileStatement()
    {
        var whileKeyword = Consume();
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

        var body = ParseTopLevelStatement();

        return new TopLevelWhileStatement(whileKeyword, openParen, condition, closeParen, body);
    }

    private TopLevelBlockStatement ParseTopLevelBlockStatement()
    {
        var (openBrace, statements, _, closeBrace) = ParseBlock(allowFinalExpression: false);

        return new TopLevelBlockStatement(openBrace, statements, closeBrace);
    }

    /// <summary> Parses a local statement. </summary>
    /// <returns> The statement node. </returns>
    private LocalStatement ParseLocalStatement(StatementParseMode parseMode = StatementParseMode.Normal)
    {
        switch (parseMode)
        {
            default:
                switch (CurrentToken.Kind)
                {
                    case TokenKind.Identifier:
                        if (CurrentToken.Value == "if")
                            return ParseLocalIfStatement();
                        else if (Peek().Kind is TokenKind.Identifier)
                            return ParseLocalVariableDeclarationStatement();
                        break;

                    case TokenKind.Semicolon:
                        return ParseLocalEmptyStatement();

                    case TokenKind.LeftCurlyBrace:
                        return ParseLocalBlockStatement();
                }

                return ParseLocalExpressionStatement(allowFinalExpression: false);

            case StatementParseMode.AllowFinalExpression:
                switch (CurrentToken.Kind)
                {
                    case TokenKind.Identifier:
                        if (CurrentToken.Value == "if")
                            return ParseLocalIfStatement();
                        else if (Peek().Kind is TokenKind.Identifier)
                            return ParseLocalVariableDeclarationStatement();
                        break;

                    case TokenKind.Semicolon:
                        return ParseLocalEmptyStatement();
                }

                return ParseLocalExpressionStatement(allowFinalExpression: true);

            case StatementParseMode.AllowStatementWithoutSemicolon:
                switch (CurrentToken.Kind)
                {
                    case TokenKind.Identifier:
                        if (CurrentToken.Value == "if")
                            return ParseLocalIfStatement();
                        else if (Peek().Kind is TokenKind.Identifier)
                            return ParseLocalVariableDeclarationStatement(allowMissingSemicolon: true);
                        break;

                    case TokenKind.LeftCurlyBrace:
                        return ParseLocalBlockStatement();
                }
                
                return new LocalExpressionStatement(ParseExpression(), new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []));
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LocalEmptyStatement ParseLocalEmptyStatement() => new LocalEmptyStatement(Consume());

    /// <summary> Parses a local expression statement. </summary>
    /// <returns> The local expression statement node. </returns>
    private LocalExpressionStatement ParseLocalExpressionStatement(bool allowFinalExpression)
    {
        var expression = ParseExpression();
        Token semicolon;

        if (CurrentToken.Kind is not TokenKind.Semicolon && !allowFinalExpression)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ";");
            semicolon = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else if (CurrentToken.Kind is not TokenKind.Semicolon)
            semicolon = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []); // Fabricated semicolon
        else
            semicolon = Consume();

        return new LocalExpressionStatement(expression, semicolon, isFinalExpression: allowFinalExpression);
    }

    /// <summary> Parses a local variable declaration statement. </summary>
    /// <returns> The local variable declaration statement node. </returns>
    private LocalVariableDeclarationStatement ParseLocalVariableDeclarationStatement(bool allowMissingSemicolon = false)
    {
        var modifiers = ParseModifiers();
        var type = ParseNamedSyntax();

        var nodesAndSeparators = new List<SyntaxNode>();

        while (CurrentToken.Kind is not TokenKind.EndToken and not TokenKind.Semicolon)
        {
            var identifier = new IdentifierName(Consume());

            AssignmentClause? initializer = null;

            if (CurrentToken.Kind is TokenKind.Equals)
            {
                var assignmentOp = Consume();
                var initExpr = ParseExpression();

                initializer = new AssignmentClause(assignmentOp, initExpr);
            }

            var declarator = new VariableDeclarator(identifier, initializer);

            nodesAndSeparators.Add(declarator);

            if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        Token semicolon;

        if (CurrentToken.Kind is not TokenKind.Semicolon && !allowMissingSemicolon)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ";");
            semicolon = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else if (CurrentToken.Kind is not TokenKind.Semicolon)
            semicolon = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        else
            semicolon = Consume();

        return new LocalVariableDeclarationStatement(type, new SeparatedSyntaxList<VariableDeclarator>(nodesAndSeparators), semicolon);
    }

    private LocalIfStatement ParseLocalIfStatement()
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

        var thenStatement = ParseLocalStatement();

        LocalElseStatement? elseStatement = null;

        if (CurrentToken.Value == "else")
        {
            var elseKeyword = Consume();
            var elseStmt = ParseLocalStatement();
            elseStatement = new LocalElseStatement(elseKeyword, elseStmt);
        }

        return new LocalIfStatement(ifKeyword, openParen, condition, closeParen, thenStatement, elseStatement);
    }

    private LocalWhileStatement ParseLocalWhileStatement()
    {
        var whileKeyword = Consume();
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

        var body = ParseLocalStatement();

        return new LocalWhileStatement(whileKeyword, openParen, condition, closeParen, body);
    }
  
    private LocalBlockStatement ParseLocalBlockStatement()
    {
        var (openBrace, statements, _, closeBrace) = ParseBlock(allowFinalExpression: false);

        return new LocalBlockStatement(openBrace, statements, closeBrace);
    }
}