using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Parser
{   
    private TopLevelStatement ParseTopLevelStatement()
    {
        switch (CurrentToken.Kind)
        {
            case TokenKind.Identifier:
                if (CurrentToken.MatchingKind is MatchingKeywordKind.If)
                    return ParseTopLevelIfStatement();
                else if (CurrentToken.MatchingKind is MatchingKeywordKind.While)
                    return ParseTopLevelWhileStatement();
                else if (CurrentToken.MatchingKind is MatchingKeywordKind.Return)
                    return ParseTopLevelReturnStatement();
                break;

            case TokenKind.Semicolon:
                return ParseTopLevelEmptyStatement();

            case TokenKind.LeftBrace:
                return ParseTopLevelBlockStatement();
        }

        return ParseTopLevelExpressionStatement();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TopLevelEmptyStatement ParseTopLevelEmptyStatement() => new TopLevelEmptyStatement(Consume());

    private TopLevelExpressionStatement ParseTopLevelExpressionStatement()
    {
        var expression = ParseExpectedExpression("for the top-level statement");
        var semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the top-level expression", MissingTokenAnchor.AfterPrevious);

        return new TopLevelExpressionStatement(expression, semicolon);
    }

    private TopLevelIfStatement ParseTopLevelIfStatement()
    {
        var ifKeyword = Consume();
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after 'if'");
        var condition = ParseExpectedExpression("for the 'if' condition", MissingTokenAnchor.AfterPrevious);
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the 'if' condition");

        var thenStatement = ParseTopLevelStatement();

        TopLevelElseStatement? elseStatement = null;

        if (CurrentToken.MatchingKind is MatchingKeywordKind.Else)
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
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after 'while'");
        var condition = ParseExpectedExpression("for the 'while' condition", MissingTokenAnchor.AfterPrevious);
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the 'while' condition");

        var body = ParseTopLevelStatement();

        return new TopLevelWhileStatement(whileKeyword, openParen, condition, closeParen, body);
    }

    private TopLevelBlockStatement ParseTopLevelBlockStatement()
    {
        var (openBrace, locals, _, closeBrace) = ParseBlock(allowFinalExpression: false);

        return new TopLevelBlockStatement(openBrace, locals, closeBrace);
    }

    private TopLevelReturnStatement ParseTopLevelReturnStatement()
    {
        var statement = ParseReturnStatement();

        return new TopLevelReturnStatement(statement);
    }

    /// <summary> Parses a local statement. </summary>
    /// <returns> The statement node. </returns>
    private LocalStatement ParseLocalStatement(StatementParseMode parseMode = StatementParseMode.Normal)
    {
        switch (parseMode)
        {
            case StatementParseMode.Normal:
                switch (CurrentToken.Kind)
                {
                    case TokenKind.Identifier:
                        if (CurrentToken.MatchingKind is MatchingKeywordKind.If)
                            return ParseLocalIfStatement();
                        else if (CurrentToken.MatchingKind is MatchingKeywordKind.While)
                            return ParseLocalWhileStatement();
                        else if (CurrentToken.MatchingKind is MatchingKeywordKind.Return)
                            return ParseLocalReturnStatement();
                        else if (LooksLikeVariableDeclaration().Success)
                            return ParseLocalVariableDeclarationStatement();
                        break;

                    case TokenKind.Semicolon:
                        return ParseLocalEmptyStatement();

                    case TokenKind.LeftBrace:
                        return ParseLocalBlockStatement();
                }

                return ParseLocalExpressionStatement(allowFinalExpression: false);

            case StatementParseMode.AllowFinalExpression:
                switch (CurrentToken.Kind)
                {
                    case TokenKind.Identifier:
                        if (CurrentToken.MatchingKind is MatchingKeywordKind.If)
                            return ParseLocalIfStatement();
                        else if (CurrentToken.MatchingKind is MatchingKeywordKind.While)
                            return ParseLocalWhileStatement();
                        else if (CurrentToken.MatchingKind is MatchingKeywordKind.Return)
                            return ParseLocalReturnStatement();
                        else if (LooksLikeVariableDeclaration().Success)
                            return ParseLocalVariableDeclarationStatement();
                        break;

                    case TokenKind.Semicolon:
                        return ParseLocalEmptyStatement();
                }

                return ParseLocalExpressionStatement(allowFinalExpression: true);

            default:
                throw new ArgumentOutOfRangeException(nameof(parseMode), parseMode, "Unhandled statement parse mode.");
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LocalEmptyStatement ParseLocalEmptyStatement() => new LocalEmptyStatement(Consume());

    /// <summary> Parses a local expression statement. </summary>
    /// <returns> The local expression statement node. </returns>
    private LocalExpressionStatement ParseLocalExpressionStatement(bool allowFinalExpression)
    {
        var expression = ParseExpectedExpression("for the local statement");
        Token semicolon;

        if (CurrentToken.Kind is not TokenKind.Semicolon && !allowFinalExpression)
        {
            semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the local expression", MissingTokenAnchor.AfterPrevious);
        }
        else if (CurrentToken.Kind is not TokenKind.Semicolon)
            semicolon = CreateMissingToken(); // Fabricated semicolon
        else
            semicolon = Consume();

        return new LocalExpressionStatement(expression, semicolon, isFinalExpression: allowFinalExpression);
    }

    /// <summary> Parses a local variable declaration statement. </summary>
    /// <returns> The local variable declaration statement node. </returns>
    private LocalVariableDeclarationStatement ParseLocalVariableDeclarationStatement(IReadOnlyList<Token>? modifiers = null, TypeSyntax? type = null, NamedSyntax? firstIdentifier = null)
    {
        var variableDeclaration = ParseVariableDeclaration(modifiers, type, firstIdentifier);
        var semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the local variable declaration", MissingTokenAnchor.AfterPrevious);

        return new LocalVariableDeclarationStatement(variableDeclaration, semicolon);
    }

    private LocalIfStatement ParseLocalIfStatement()
    {
        var ifKeyword = Consume();
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after 'if'");
        var condition = ParseExpectedExpression("for the 'if' condition", MissingTokenAnchor.AfterPrevious);
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the 'if' condition");

        var thenStatement = ParseLocalStatement();

        LocalElseStatement? elseStatement = null;

        if (CurrentToken.MatchingKind is MatchingKeywordKind.Else)
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
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after 'while'");
        var condition = ParseExpectedExpression("for the 'while' condition", MissingTokenAnchor.AfterPrevious);
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the 'while' condition");

        var body = ParseLocalStatement();

        return new LocalWhileStatement(whileKeyword, openParen, condition, closeParen, body);
    }
  
    private LocalBlockStatement ParseLocalBlockStatement()
    {
        var (openBrace, locals, _, closeBrace) = ParseBlock(allowFinalExpression: false);

        return new LocalBlockStatement(openBrace, locals, closeBrace);
    }


    private LocalReturnStatement ParseLocalReturnStatement()
    {
        var statement = ParseReturnStatement();

        return new LocalReturnStatement(statement);
    }

    private ReturnStatement ParseReturnStatement()
    {
        var returnKeyword = Consume();
        Expression? expression = null;

        if (CurrentToken.Kind is not TokenKind.Semicolon)
            expression = ParseExpectedExpression("after 'return'", MissingTokenAnchor.AfterPrevious);

        var semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the return statement", MissingTokenAnchor.AfterPrevious);

        return new ReturnStatement(returnKeyword, expression, semicolon);
    }
}