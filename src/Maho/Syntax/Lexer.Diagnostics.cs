using Maho.Diagnostics;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Diagnostic helpers for lexer recovery and invalid-token reporting. </summary>
internal sealed partial class Lexer
{
    /// <summary> Reports one invalid token and preserves its original source slice. </summary>
    private void ReportBadToken(int start) =>
        diagnostics.ReportBadToken(new TextSpan(start, 1), DiagnosticText.SourceSpan(text, new TextSpan(start, 1)));

    /// <summary> Reports unterminated string and character literals using the current token kind. </summary>
    private void ReportUnterminatedLiteral(int start, TokenKind tokenKind)
    {
        var span = new TextSpan(start, current - start);

        if (tokenKind is TokenKind.String)
            diagnostics.ReportUnterminatedString(span);
        else
            diagnostics.ReportUnterminatedCharacter(span);
    }

    /// <summary> Reports an empty character literal when the lexer sees no payload between quotes. </summary>
    private void ReportCharacterLiteralLength(int start, int characterCount)
    {
        if (characterCount == 0)
            diagnostics.ReportEmptyCharacterLiteral(new TextSpan(start, current - start));
    }
}
