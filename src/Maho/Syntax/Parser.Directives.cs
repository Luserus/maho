using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    private IReadOnlyList<PragmaDirective> ParsePragmaDirectives(out bool topLevelStatementsEnabled)
    {
        List<PragmaDirective> pragmas = [];
        topLevelStatementsEnabled = false;

        while (CurrentToken.Kind is TokenKind.Octothorpe)
        {
            PragmaDirective pragma = ParsePragmaDirective();
            pragmas.Add(pragma);

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
