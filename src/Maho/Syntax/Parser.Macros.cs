using System.Collections.Generic;
using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    private bool IsArrow() => GetCombinedOperatorData().Kind is TokenKind.EqualsGreaterThan;

    private Token ConsumeArrow()
    {
        if (IsArrow())
            return ConsumeOperator();

        return ExpectToken(TokenKind.EqualsGreaterThan, "'=>'", "in macro arm");
    }

    private bool IsMultiArmMacroStart()
    {
        if (CurrentToken.Kind is not TokenKind.LeftBrace)
            return false;

        var next = Peek(1);
        if (next.Kind is TokenKind.LeftParen)
            return true;

        var next2 = Peek(2);
        if (next.Kind is TokenKind.Equals && next2.Kind is TokenKind.GreaterThanSign &&
            next.Span.End == next2.Span.Start &&
            next.TrailingTrivia.Length == 0 &&
            next2.LeadingTrivia.Length == 0)
            return true;

        return false;
    }

    private MacroDeclaration ParseMacroDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers)
    {
        Token macroKeyword = Consume();
        Token dollarToken = ExpectToken(TokenKind.Dollar, "'$'", "before macro name");
        Token name = ExpectToken(TokenKind.Identifier, "an identifier", "as macro name");

        List<MacroArmSyntax> arms = [];

        if (IsArrow())
        {
            Token arrow = ConsumeArrow();
            var tokens = ParseTemplateTokensUntilSemicolon();
            Token? semicolon = null;
            if (CurrentToken.Kind is TokenKind.Semicolon)
                semicolon = Consume();

            arms.Add(new MacroArmSyntax(null, arrow, new MacroTemplateSyntax(tokens, isExpression: true), semicolon));
            return new MacroDeclaration(attributes, modifiers, macroKeyword, dollarToken, name, arms);
        }

        if (CurrentToken.Kind is not TokenKind.LeftBrace)
        {
            diagnostics.ReportExpectedToken(CurrentToken.Span, "'{' or '=>'", GetTokenDisplay(CurrentToken), "in macro definition", CurrentToken.Source);
            return new MacroDeclaration(attributes, modifiers, macroKeyword, dollarToken, name, arms);
        }

        if (IsMultiArmMacroStart())
        {
            Consume(); // '{'
            while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
            {
                var arm = ParseMacroArm();
                arms.Add(arm);
            }
            ExpectToken(TokenKind.RightBrace, "'}'", "to close macro definition");
        }
        else
        {
            var tokens = ParseTemplateTokensBlock();
            arms.Add(new MacroArmSyntax(null, null, new MacroTemplateSyntax(tokens, isExpression: false), null));
        }

        return new MacroDeclaration(attributes, modifiers, macroKeyword, dollarToken, name, arms);
    }

    private MacroArmSyntax ParseMacroArm()
    {
        MacroPatternSyntax? pattern = null;
        if (CurrentToken.Kind is TokenKind.LeftParen)
        {
            Token leftParen = Consume();
            List<MacroPatternParameter> parameters = [];
            while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
            {
                parameters.Add(ParseMacroPatternParameter());
                if (CurrentToken.Kind is TokenKind.Comma)
                    Consume();
                else
                    break;
            }
            Token rightParen = ExpectToken(TokenKind.RightParen, "')'", "to close pattern");
            pattern = new MacroPatternSyntax(leftParen, parameters, rightParen);
        }

        Token? arrow = null;
        if (IsArrow())
            arrow = ConsumeArrow();
        else if (CurrentToken.Kind is not TokenKind.LeftBrace)
            arrow = ExpectToken(TokenKind.EqualsGreaterThan, "'=>'", "in macro arm");

        bool isExpression;
        List<Token> templateTokens;
        Token? semicolon = null;

        if (CurrentToken.Kind is TokenKind.LeftBrace)
        {
            templateTokens = ParseTemplateTokensBlock();
            isExpression = false;
            if (CurrentToken.Kind is TokenKind.Semicolon)
                semicolon = Consume();
        }
        else
        {
            templateTokens = ParseTemplateTokensUntilSemicolon();
            isExpression = true;
            if (CurrentToken.Kind is TokenKind.Semicolon)
                semicolon = Consume();
        }

        return new MacroArmSyntax(pattern, arrow, new MacroTemplateSyntax(templateTokens, isExpression), semicolon);
    }

    private MacroPatternParameter ParseMacroPatternParameter()
    {
        if (CurrentToken.Kind is TokenKind.AtSymbol)
        {
            Token atToken = Consume();
            Token name = ExpectToken(TokenKind.Identifier, "an identifier", "as macro parameter name");
            Token? colon = null;
            MacroParameterKind kind = MacroParameterKind.Expression;

            if (CurrentToken.Kind is TokenKind.Colon)
            {
                colon = Consume();
                if (CurrentToken.Kind is TokenKind.Identifier)
                {
                    kind = CurrentToken.MatchingKind switch
                    {
                        MatchingKeywordKind.Expr => MacroParameterKind.Expression,
                        MatchingKeywordKind.Type => MacroParameterKind.Type,
                        MatchingKeywordKind.Ident => MacroParameterKind.Identifier,
                        MatchingKeywordKind.Stmt => MacroParameterKind.Statement,
                        MatchingKeywordKind.Tokens => MacroParameterKind.TokenStream,
                        MatchingKeywordKind.Token => MacroParameterKind.SingleToken,
                        _ => ReportInvalidMacroPatternKind(CurrentToken)
                    };
                    Consume();
                }
                else
                {
                    diagnostics.ReportExpectedToken(CurrentToken.Span, "parameter kind (expr, type, ident, stmt, tokens, token)", GetTokenDisplay(CurrentToken), "after ':' in macro pattern", CurrentToken.Source);
                }
            }

            bool isVariadic = false;
            var (opKind, opLen) = GetCombinedOperatorData();
            if (opKind is TokenKind.DotDotDot && opLen == 3)
            {
                ConsumeOperator();
                isVariadic = true;
            }
            else if (CurrentToken.Kind is TokenKind.DotDotDot)
            {
                Consume();
                isVariadic = true;
            }

            return new MacroPatternParameter(atToken, name, colon, kind, isVariadic);
        }
        else
        {
            var expr = ParseExpression();
            var name = new Token(expr.GetSource() ?? text, expr.GetSpan() ?? default, TokenKind.Identifier, [], []);
            return new MacroPatternParameter(null, name, null, MacroParameterKind.Literal, false, expr);
        }
    }

    private MacroParameterKind ReportInvalidMacroPatternKind(Token token)
    {
        diagnostics.ReportInvalidMacroPattern(token.Span, $"unknown parameter kind '{token.Value}', expected expr, type, ident, stmt, tokens, or token", token.Source);
        return MacroParameterKind.Expression;
    }

    private List<Token> ParseTemplateTokensUntilSemicolon()
    {
        List<Token> tokens = [];
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;

        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 && CurrentToken.Kind is TokenKind.Semicolon)
                break;

            if (CurrentToken.Kind is TokenKind.LeftParen) parenDepth++;
            else if (CurrentToken.Kind is TokenKind.RightParen && parenDepth > 0) parenDepth--;
            else if (CurrentToken.Kind is TokenKind.LeftBracket) bracketDepth++;
            else if (CurrentToken.Kind is TokenKind.RightBracket && bracketDepth > 0) bracketDepth--;
            else if (CurrentToken.Kind is TokenKind.LeftBrace) braceDepth++;
            else if (CurrentToken.Kind is TokenKind.RightBrace && braceDepth > 0) braceDepth--;

            tokens.Add(Consume());
        }

        return tokens;
    }

    private List<Token> ParseTemplateTokensBlock()
    {
        List<Token> tokens = [];
        ExpectToken(TokenKind.LeftBrace, "'{'", "to open macro body");
        int braceDepth = 1;

        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            if (CurrentToken.Kind is TokenKind.LeftBrace)
                braceDepth++;
            else if (CurrentToken.Kind is TokenKind.RightBrace)
            {
                braceDepth--;
                if (braceDepth == 0)
                {
                    Consume();
                    break;
                }
            }

            tokens.Add(Consume());
        }

        return tokens;
    }

    private MacroInvocationExpression ParseMacroInvocationExpression()
    {
        Token dollarToken = Consume();
        Token name = ExpectToken(TokenKind.Identifier, "an identifier", "as macro name");

        if (CurrentToken.Kind is not TokenKind.LeftParen)
            return new MacroInvocationExpression(dollarToken, name, null, [], null);

        Token leftParen = Consume();
        List<MacroArgumentSyntax> arguments = [];

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            var argTokens = ParseArgumentTokens();
            arguments.Add(new MacroArgumentSyntax(argTokens));

            if (CurrentToken.Kind is TokenKind.Comma)
                Consume();
            else
                break;
        }

        Token rightParen = ExpectToken(TokenKind.RightParen, "')'", "to close macro arguments");
        return new MacroInvocationExpression(dollarToken, name, leftParen, arguments, rightParen);
    }

    private List<Token> ParseArgumentTokens()
    {
        List<Token> tokens = [];
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;

        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 &&
                (CurrentToken.Kind is TokenKind.Comma or TokenKind.RightParen))
            {
                break;
            }

            if (CurrentToken.Kind is TokenKind.LeftParen) parenDepth++;
            else if (CurrentToken.Kind is TokenKind.RightParen && parenDepth > 0) parenDepth--;
            else if (CurrentToken.Kind is TokenKind.LeftBracket) bracketDepth++;
            else if (CurrentToken.Kind is TokenKind.RightBracket && bracketDepth > 0) bracketDepth--;
            else if (CurrentToken.Kind is TokenKind.LeftBrace) braceDepth++;
            else if (CurrentToken.Kind is TokenKind.RightBrace && braceDepth > 0) braceDepth--;

            tokens.Add(Consume());
        }

        return tokens;
    }

    private TopLevelMacroInvocationDeclaration ParseTopLevelMacroInvocationDeclaration()
    {
        var invocation = ParseMacroInvocationExpression();
        Token? semicolon = null;
        if (CurrentToken.Kind is TokenKind.Semicolon)
            semicolon = Consume();
        return new TopLevelMacroInvocationDeclaration(invocation, semicolon);
    }

    private MemberMacroInvocationDeclaration ParseMemberMacroInvocationDeclaration()
    {
        var invocation = ParseMacroInvocationExpression();
        Token? semicolon = null;
        if (CurrentToken.Kind is TokenKind.Semicolon)
            semicolon = Consume();
        return new MemberMacroInvocationDeclaration(invocation, semicolon);
    }

    private LocalMacroInvocationDeclaration ParseLocalMacroInvocationDeclaration()
    {
        var invocation = ParseMacroInvocationExpression();
        Token? semicolon = null;
        if (CurrentToken.Kind is TokenKind.Semicolon)
            semicolon = Consume();
        return new LocalMacroInvocationDeclaration(invocation, semicolon);
    }

    private LocalMacroDeclaration ParseLocalMacroDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers)
    {
        var macro = ParseMacroDeclaration(attributes, modifiers);
        return new LocalMacroDeclaration(macro);
    }
}
