using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    private bool IsUsingDirective()
    {
        if (CurrentToken.MatchingKind is not MatchingKeywordKind.Using)
            return false;

        int offset = 1;
        int angleBracketDepth = 0;

        while (true)
        {
            var token = Peek(offset);
            if (token.Kind is TokenKind.EndToken or TokenKind.Semicolon)
                break;

            if (token.Kind is TokenKind.LessThanSign)
                angleBracketDepth++;
            else if (token.Kind is TokenKind.GreaterThanSign)
                angleBracketDepth--;
            else if (angleBracketDepth == 0)
            {
                if (token.Kind is TokenKind.Equals || token.MatchingKind is MatchingKeywordKind.Where)
                    return false;
            }

            offset++;
        }

        return true;
    }

    private UsingDirective ParseUsingDirective()
    {
        Token keyword = Consume();
        NamedSyntax name = ParseNamedSyntax(allowQualified: true, allowGenericName: false);
        Token semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the namespace name", MissingTokenAnchor.AfterPrevious);
        return new UsingDirective(keyword, name, semicolon);
    }

    private IReadOnlyList<Directive> ParseDirectives(out bool topLevelStatementsEnabled, bool allowImplicitTopLevel = false)
    {
        List<Directive> directives = [];
        topLevelStatementsEnabled = allowImplicitTopLevel;

        while (CurrentToken.Kind is TokenKind.Octothorpe || IsUsingDirective())
        {
            if (CurrentToken.Kind is TokenKind.Octothorpe)
            {
                PragmaDirective pragma = ParsePragmaDirective();
                directives.Add(pragma);

                if (pragma.PragmaKeyword.Value != "pragma")
                {
                    diagnostics.ReportExpectedToken(pragma.PragmaKeyword.Span, "'pragma'", GetTokenDisplay(pragma.PragmaKeyword), "after '#'");
                    continue;
                }

                if (pragma.Name.Value != "toplevel")
                {
                    diagnostics.ReportExpectedToken(pragma.Name.Span, "'toplevel'", GetTokenDisplay(pragma.Name), "for the pragma name");
                    continue;
                }

                if (pragma.Value.Value == "enable")
                    topLevelStatementsEnabled = true;
                else if (pragma.Value.Value == "disable")
                    topLevelStatementsEnabled = false;
                else
                    diagnostics.ReportExpectedToken(pragma.Value.Span, "'enable' or 'disable'", GetTokenDisplay(pragma.Value), "for '#pragma toplevel'");
            }
            else
            {
                UsingDirective usingDirective = ParseUsingDirective();
                directives.Add(usingDirective);
            }
        }

        return directives;
    }

    private IReadOnlyList<PragmaDirective> ParsePragmaDirectives(out bool topLevelStatementsEnabled, bool allowImplicitTopLevel = false)
    {
        var directives = ParseDirectives(out topLevelStatementsEnabled, allowImplicitTopLevel);
        var pragmas = new List<PragmaDirective>();
        foreach (var d in directives)
            if (d is PragmaDirective p)
                pragmas.Add(p);
        return pragmas;
    }

    private PragmaDirective ParsePragmaDirective()
    {
        Token hashToken = Consume();
        Token pragmaKeyword = ExpectIdentifierToken("after '#'");
        Token name = ExpectIdentifierToken("for the pragma name");
        Token value = ExpectIdentifierToken("for the pragma value");

        return new PragmaDirective(hashToken, pragmaKeyword, name, value);
    }
}