using System;
using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    private static bool IsClosingToken(TokenKind kind) =>
        kind is TokenKind.RightParen or TokenKind.RightBracket or TokenKind.RightBrace or TokenKind.GreaterThanSign;

    private string GetTokenDisplay(Token token) => token.Kind switch
    {
        TokenKind.EndToken => "<end of file>",
        TokenKind.MissingToken => "<missing>",
        _ when string.IsNullOrEmpty(token.Value) => $"<{token.Kind}>",
        _ => token.Value
    };

    private Token CreateMissingToken() => CreateMissingTokenAt(CurrentToken.Span.Start);

    private Token CreateMissingTokenAt(int position) => new(text, new TextSpan(position, 0), TokenKind.MissingToken, [], []);

    private TextSpan GetMissingTokenDiagnosticSpan(MissingTokenAnchor anchor) =>
        anchor switch
        {
            MissingTokenAnchor.BeforeCurrent => CurrentToken.Span,
            MissingTokenAnchor.AfterPrevious => new TextSpan(PreviousToken.Span.End, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unhandled missing token anchor.")
        };

    private int GetMissingTokenPosition(MissingTokenAnchor anchor) =>
        anchor switch
        {
            MissingTokenAnchor.BeforeCurrent => CurrentToken.Span.Start,
            MissingTokenAnchor.AfterPrevious => PreviousToken.Span.End,
            _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unhandled missing token anchor.")
        };

    private MissingTokenAnchor GetClosingTokenAnchor()
    {
        if (current <= 0)
            return MissingTokenAnchor.BeforeCurrent;

        int currentLine = text.GetLineIndex(CurrentToken.Span.Start);
        int previousLine = text.GetLineIndex(PreviousToken.Span.End);

        return currentLine > previousLine
            ? MissingTokenAnchor.AfterPrevious
            : MissingTokenAnchor.BeforeCurrent;
    }

    private void ReportExpectedTokenDiagnostic(TextSpan span, TokenKind expectedKind, string expectedText, string? context)
    {
        string found = GetTokenDisplay(CurrentToken);

        if (expectedKind is TokenKind.Semicolon)
            diagnostics.ReportExpectedSemicolon(span, found, context);
        else if (IsClosingToken(expectedKind))
            diagnostics.ReportExpectedClosingToken(span, expectedText, found, context);
        else
            diagnostics.ReportExpectedToken(span, expectedText, found, context);
    }

    private Token RecoverWithMissingToken()
    {
        if (!IsRecoveryBoundary(CurrentToken.Kind))
            Consume();

        return CreateMissingToken();
    }

    private Token ExpectToken(TokenKind expectedKind, string expectedText, string? context = null, MissingTokenAnchor anchor = MissingTokenAnchor.BeforeCurrent)
    {
        if (CurrentToken.Kind is var currentKind && currentKind == expectedKind)
            return Consume();

        ReportExpectedTokenDiagnostic(GetMissingTokenDiagnosticSpan(anchor), expectedKind, expectedText, context);
        return CreateMissingTokenAt(GetMissingTokenPosition(anchor));
    }

    private Token ExpectClosingToken(TokenKind expectedKind, string expectedText, string? context = null, params TokenKind[] recoveryKinds)
    {
        if (CurrentToken.Kind == expectedKind)
            return Consume();

        // When the recovery token starts on a later line, anchor the missing closer at the end of
        // the previous token so the insertion point stays on the line where the construct started.
        MissingTokenAnchor anchor = GetClosingTokenAnchor();
        TextSpan diagnosticSpan = GetMissingTokenDiagnosticSpan(anchor);
        int missingTokenPosition = GetMissingTokenPosition(anchor);

        diagnostics.ReportExpectedClosingToken(diagnosticSpan, expectedText, GetTokenDisplay(CurrentToken), context);

        if (!Contains(recoveryKinds, CurrentToken.Kind))
            SynchronizeTo([expectedKind, .. recoveryKinds]);

        if (CurrentToken.Kind == expectedKind)
            return Consume();

        return CreateMissingTokenAt(missingTokenPosition);
    }

    private Token ExpectIdentifierToken(string? context = null)
    {
        if (CurrentToken.Kind is TokenKind.Identifier)
            return Consume();

        diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), context);
        return RecoverWithMissingToken();
    }

    private Expression CreateMissingExpression(string? context = null, MissingTokenAnchor anchor = MissingTokenAnchor.BeforeCurrent)
    {
        diagnostics.ReportExpectedExpression(GetMissingTokenDiagnosticSpan(anchor), GetTokenDisplay(CurrentToken), context);

        if (anchor is MissingTokenAnchor.BeforeCurrent)
            return new LiteralExpression(RecoverWithMissingToken());

        return new LiteralExpression(CreateMissingTokenAt(GetMissingTokenPosition(anchor)));
    }

    private Expression ParseExpectedExpression(string? context = null, MissingTokenAnchor anchor = MissingTokenAnchor.BeforeCurrent) =>
        CanStartExpression() ? ParseExpression() : CreateMissingExpression(context, anchor);
}
