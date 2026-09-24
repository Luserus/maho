using System;
using Maho.Diagnostics;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Parsing diagnostics and recovery helpers. </summary>
internal sealed partial class Parser
{
    /// <summary> Treats closing delimiters as a distinct recovery class so missing-token spans stay sensible. </summary>
    private static bool IsClosingToken(TokenKind kind) =>
        kind is TokenKind.RightParen or TokenKind.RightBracket or TokenKind.RightBrace or TokenKind.GreaterThanSign;

    /// <summary> Converts the current token into a deferred diagnostic display value. </summary>
    private DiagnosticText GetTokenDisplay(Token token)
    {
        if (token.Kind is TokenKind.EndToken)
            return DiagnosticText.EndOfFile;
        if (token.Kind is TokenKind.MissingToken)
            return DiagnosticText.MissingToken;

        if (current < tokens.Count && token == CurrentToken)
        {
            var (_, combinedLength) = GetCombinedOperatorData();
            if (combinedLength > 1)
            {
                var lastToken = Peek(combinedLength - 1);
                return DiagnosticText.SourceSpan(text, new TextSpan(token.Span.Start, lastToken.Span.End - token.Span.Start));
            }
        }

        return DiagnosticText.SourceSpan(text, token.Span);
    }

    /// <summary> Synthesizes a zero-width missing token at the current cursor position. </summary>
    private Token CreateMissingToken() => CreateMissingTokenAt(CurrentToken.Span.Start);

    /// <summary> Creates a synthetic missing token at a chosen source position. </summary>
    private Token CreateMissingTokenAt(int position) => new(text, new TextSpan(position, 0), TokenKind.MissingToken, [], []);

    /// <summary> Picks the span to attach to a missing-token diagnostic. </summary>
    private TextSpan GetMissingTokenDiagnosticSpan(MissingTokenAnchor anchor) =>
        anchor switch
        {
            MissingTokenAnchor.BeforeCurrent => CurrentToken.Span,
            MissingTokenAnchor.AfterPrevious => new TextSpan(PreviousToken.Span.End, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "unhandled missing token anchor.")
        };

    /// <summary> Computes where a synthetic missing token should be inserted. </summary>
    private int GetMissingTokenPosition(MissingTokenAnchor anchor) =>
        anchor switch
        {
            MissingTokenAnchor.BeforeCurrent => CurrentToken.Span.Start,
            MissingTokenAnchor.AfterPrevious => PreviousToken.Span.End,
            _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "unhandled missing token anchor.")
        };

    /// <summary> Chooses the cleaner anchor for a missing token or delimiter based on token layout. </summary>
    private MissingTokenAnchor GetEffectiveAnchor(MissingTokenAnchor anchor, bool isClosingToken = false)
    {
        if (current <= 0 || CurrentToken.Kind is TokenKind.EndToken)
            return anchor;

        if (!isClosingToken && anchor is MissingTokenAnchor.BeforeCurrent)
            return MissingTokenAnchor.BeforeCurrent;

        int currentLine = text.GetLineIndex(CurrentToken.Span.Start);
        int previousLine = text.GetLineIndex(PreviousToken.Span.End);

        return currentLine > previousLine
            ? MissingTokenAnchor.AfterPrevious
            : MissingTokenAnchor.BeforeCurrent;
    }

    /// <summary> Recovers by consuming one token when the stream is not already at a recovery boundary. </summary>
    private Token RecoverWithMissingToken()
    {
        if (!IsRecoveryBoundary(CurrentToken.Kind))
            Consume();

        return CreateMissingToken();
    }

    /// <summary> Expects one token kind and reports a targeted diagnostic when recovery is needed. </summary>
    private Token ExpectToken(TokenKind expectedKind, string expectedText, string? context = null, MissingTokenAnchor anchor = MissingTokenAnchor.BeforeCurrent)
    {
        if (CurrentToken.Kind == expectedKind)
            return Consume();

        MissingTokenAnchor effectiveAnchor = GetEffectiveAnchor(anchor, IsClosingToken(expectedKind));
        TextSpan diagnosticSpan = GetMissingTokenDiagnosticSpan(effectiveAnchor);
        int missingTokenPosition = GetMissingTokenPosition(effectiveAnchor);

        TextSpan? unexpectedSpan = null;
        if (CurrentToken.Kind is not TokenKind.EndToken && CurrentToken.Span.Length > 0)
        {
            if (text.GetLineIndex(CurrentToken.Span.Start) != text.GetLineIndex(diagnosticSpan.Start))
                unexpectedSpan = CurrentToken.Span;
        }

        diagnostics.ReportExpectedToken(diagnosticSpan, expectedText, GetTokenDisplay(CurrentToken), context, unexpectedSpan: unexpectedSpan);

        return CreateMissingTokenAt(missingTokenPosition);
    }

    /// <summary> Expects an identifier token and recovers with a synthetic placeholder if needed. </summary>
    private Token ExpectIdentifierToken(string? context = null)
    {
        if (CurrentToken.Kind is TokenKind.Identifier)
            return Consume();

        diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), context);
        return RecoverWithMissingToken();
    }

    /// <summary> Constructs a missing expression node and reports the appropriate diagnostic. </summary>
    private Expression CreateMissingExpression(string? context = null, MissingTokenAnchor anchor = MissingTokenAnchor.BeforeCurrent)
    {
        MissingTokenAnchor effectiveAnchor = GetEffectiveAnchor(anchor);
        TextSpan diagnosticSpan = GetMissingTokenDiagnosticSpan(effectiveAnchor);

        TextSpan? unexpectedSpan = null;
        if (CurrentToken.Kind is not TokenKind.EndToken && CurrentToken.Span.Length > 0)
        {
            if (text.GetLineIndex(CurrentToken.Span.Start) != text.GetLineIndex(diagnosticSpan.Start))
                unexpectedSpan = CurrentToken.Span;
        }

        diagnostics.ReportExpectedExpression(diagnosticSpan, GetTokenDisplay(CurrentToken), context, unexpectedSpan: unexpectedSpan);

        if (effectiveAnchor is MissingTokenAnchor.BeforeCurrent)
            return new LiteralExpression(RecoverWithMissingToken());

        return new LiteralExpression(CreateMissingTokenAt(GetMissingTokenPosition(effectiveAnchor)));
    }

    /// <summary> Parses an expression or synthesizes a missing one when parsing cannot continue. </summary>
    private Expression ParseExpectedExpression(string? context = null, MissingTokenAnchor anchor = MissingTokenAnchor.BeforeCurrent) =>
        CanStartExpression() ? ParseExpression() : CreateMissingExpression(context, anchor);

    private static string GetObjectCreationContext(MatchingKeywordKind keywordKind) => keywordKind switch
    {
        MatchingKeywordKind.New => "after 'new'",
        MatchingKeywordKind.Put => "after 'put'",
        _ => "after the object creation keyword"
    };
}