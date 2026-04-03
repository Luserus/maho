using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Lexer
{
    private void ReportBadToken(int start) =>
        diagnostics.ReportBadToken(new TextSpan(start, 1), text.ToString(new TextSpan(start, 1)));

    private void ReportUnterminatedLiteral(int start, TokenKind tokenKind)
    {
        var span = new TextSpan(start, current - start);

        if (tokenKind is TokenKind.String)
            diagnostics.ReportUnterminatedString(span);
        else
            diagnostics.ReportUnterminatedCharacter(span);
    }

    private void ReportCharacterLiteralLength(int start, int characterCount)
    {
        if (characterCount == 0)
            diagnostics.ReportEmptyCharacterLiteral(new TextSpan(start, current - start));
    }
}