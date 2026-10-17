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
                if (CurrentToken.MatchingKind is MatchingKeywordKind.Return)
                    return ParseTopLevelReturnStatement();
                else if (CurrentToken.MatchingKind is MatchingKeywordKind.Goto)
                    return ParseTopLevelGotoStatement();
                else if (Peek().Kind is TokenKind.Colon)
                    return ParseTopLevelLabelStatement();
                break;

            case TokenKind.Semicolon:
                return ParseTopLevelEmptyStatement();
        }

        return ParseTopLevelExpressionStatement();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TopLevelEmptyStatement ParseTopLevelEmptyStatement() => new TopLevelEmptyStatement(Consume());

    private TopLevelExpressionStatement ParseTopLevelExpressionStatement()
    {
        var expression = ParseExpectedExpression("for the top-level statement");
        Token semicolon;

        if (CurrentToken.Kind is TokenKind.Semicolon)
            semicolon = Consume();
        else if (EndsWithBlock(expression))
            semicolon = CreateMissingToken();
        else
            semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the top-level expression", MissingTokenAnchor.AfterPrevious);

        return new TopLevelExpressionStatement(expression, semicolon);
    }


    private TopLevelReturnStatement ParseTopLevelReturnStatement()
    {
        var statement = ParseReturnStatement();

        return new TopLevelReturnStatement(statement);
    }

    private TopLevelLabelStatement ParseTopLevelLabelStatement() => new(Consume(), Consume());

    private TopLevelGotoStatement ParseTopLevelGotoStatement()
    {
        var keyword = Consume();
        var identifier = ExpectIdentifierToken("after 'goto'");
        var semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the goto target", MissingTokenAnchor.AfterPrevious);
        return new TopLevelGotoStatement(keyword, identifier, semicolon);
    }

    /// <summary> Parses a local statement. </summary>
    /// <returns> The statement node. </returns>
    private LocalStatement ParseLocalStatement(StatementParseMode parseMode = StatementParseMode.Normal)
    {
        switch (CurrentToken.Kind)
        {
            case TokenKind.Identifier:
            case TokenKind.LeftParen:
                if (CurrentToken.MatchingKind is MatchingKeywordKind.Return)
                    return ParseLocalReturnStatement();
                else if (CurrentToken.MatchingKind is MatchingKeywordKind.Goto)
                    return ParseLocalGotoStatement();
                else if (Peek().Kind is TokenKind.Colon)
                    return ParseLocalLabelStatement();
                else if (LooksLikeVariableDeclaration() is (var success, var context))
                {
                    if (success || context is LookaheadResultContext.MissingDelimeter)
                        return ParseLocalVariableDeclarationStatement();
                }
                break;

            case TokenKind.Semicolon:
                return ParseLocalEmptyStatement();
        }

        return ParseLocalExpressionStatement(parseMode);
    }



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LocalEmptyStatement ParseLocalEmptyStatement() => new LocalEmptyStatement(Consume());

    /// <summary> Parses a local expression statement. </summary>
    /// <returns> The local expression statement node. </returns>
    private LocalExpressionStatement ParseLocalExpressionStatement(StatementParseMode parseMode)
    {
        var expression = ParseExpectedExpression("for the local statement");
        Token semicolon;
        bool isFinal;

        if (CurrentToken.Kind is TokenKind.Semicolon)
        {
            semicolon = Consume();
            isFinal = false;
        }
        else if (EndsWithBlock(expression))
        {
            if (parseMode is StatementParseMode.AllowFinalExpression && CurrentToken.Kind is TokenKind.RightBrace or TokenKind.EndToken)
            {
                semicolon = CreateMissingToken();
                isFinal = true;
            }
            else
            {
                semicolon = CreateMissingToken();
                isFinal = false;
            }
        }
        else if (CurrentToken.Kind is TokenKind.RightBrace or TokenKind.EndToken)
        {
            if (parseMode is StatementParseMode.AllowFinalExpression)
            {
                semicolon = CreateMissingToken();
                isFinal = true;
            }
            else if (parseMode is StatementParseMode.FunctionBody)
            {
                MissingTokenAnchor effectiveAnchor = GetEffectiveAnchor(MissingTokenAnchor.BeforeCurrent, isClosingToken: true);
                TextSpan diagnosticSpan = GetMissingTokenDiagnosticSpan(effectiveAnchor);
                diagnostics.ReportExpectedToken(diagnosticSpan, "a return statement", GetTokenDisplay(CurrentToken));
                semicolon = CreateMissingToken();
                isFinal = false;
            }
            else
            {
                semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the local expression", MissingTokenAnchor.AfterPrevious);
                isFinal = false;
            }
        }
        else
        {
            semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the local expression", MissingTokenAnchor.AfterPrevious);
            isFinal = false;
        }

        return new LocalExpressionStatement(expression, semicolon, isFinalExpression: isFinal);
    }

    /// <summary> Parses a local variable declaration statement. </summary>
    /// <returns> The local variable declaration statement node. </returns>
    private LocalVariableDeclarationStatement ParseLocalVariableDeclarationStatement(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null, TypeSyntax? type = null, NamedSyntax? firstIdentifier = null)
    {
        var variableDeclaration = ParseVariableDeclaration(attributes, modifiers, type, firstIdentifier);
        var semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the local variable declaration", MissingTokenAnchor.AfterPrevious);

        return new LocalVariableDeclarationStatement(variableDeclaration, semicolon);
    }

    private (VariableDeclaration? Declaration, Token? Semicolon, Expression Condition) ParseConditionalHeader(string statementName)
    {
        if (LooksLikeVariableDeclaration().Success)
        {
            var declaration = ParseVariableDeclaration();
            var semicolon = ExpectToken(TokenKind.Semicolon, "';'", $"after the declaration in the '{statementName}' condition", MissingTokenAnchor.AfterPrevious);
            var condition = ParseExpectedExpression($"for the '{statementName}' condition", MissingTokenAnchor.AfterPrevious);

            return (declaration, semicolon, condition);
        }
        else
        {
            var condition = ParseExpectedExpression($"for the '{statementName}' condition", MissingTokenAnchor.AfterPrevious);
            return (null, null, condition);
        }
    }

    private static bool EndsWithBlock(Expression expr)
    {
        if (expr is BlockExpression)
            return true;
        if (expr is IfExpression ifExpr)
        {
            if (ifExpr.ElseExpression is not null)
                return EndsWithBlock(ifExpr.ElseExpression.Expression);

            return EndsWithBlock(ifExpr.ThenExpression);
        }
        if (expr is WhileExpression whileExpr)
            return EndsWithBlock(whileExpr.Body);

        return false;
    }


    private LocalReturnStatement ParseLocalReturnStatement()
    {
        var statement = ParseReturnStatement();

        return new LocalReturnStatement(statement);
    }

    private LocalLabelStatement ParseLocalLabelStatement() => new(Consume(), Consume());

    private LocalGotoStatement ParseLocalGotoStatement()
    {
        var keyword = Consume();
        var identifier = ExpectIdentifierToken("after 'goto'");
        var semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the goto target", MissingTokenAnchor.AfterPrevious);

        return new LocalGotoStatement(keyword, identifier, semicolon);
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