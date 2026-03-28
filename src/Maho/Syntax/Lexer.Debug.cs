using System;
using System.Text;
using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Lexer
{
    private const string Reset = "\u001b[0m";
    private const string Dim = "\u001b[2m";
    private const string BrightWhite = "\u001b[97m";
    private const string Yellow = "\u001b[33m";
    private const string Cyan = "\u001b[36m";
    private const string Magenta = "\u001b[35m";

    /// <summary> Gets a formatted string representation of the token stream for debugging. </summary>
    /// <returns> The formatted token stream. </returns>
    public override string ToString()
    {
        StringBuilder sb = new();

        sb.AppendLine("Token Stream");
        sb.AppendLine();

        for (int i = 0; i < Tokens.Count; i++)
        {
            Token token = Tokens[i];
            string matchingKind = token.MatchingKind is MatchingKeywordKind.None
                ? string.Empty
                : Colorize($"/{token.MatchingKind}", Magenta);

            sb.Append(Colorize(i.ToString("D3"), Dim));
            sb.Append(Colorize("  ", Dim));
            sb.Append(Colorize(token.Kind.ToString(), Yellow));
            sb.Append(matchingKind);
            sb.Append(Colorize("  ", Dim));
            sb.Append(Colorize(FormatTokenValue(token), BrightWhite));
            sb.Append(Colorize("  ", Dim));
            sb.Append(Colorize(FormatSpan(token.Span), Dim));

            if (token.LeadingTrivia.Length > 0 || token.TrailingTrivia.Length > 0)
            {
                sb.Append(Colorize("  ", Dim));
                sb.Append(Colorize(FormatTriviaSummary(token), Cyan));
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    public string ToJson()
    {
        DebugLexerTokenInfo[] tokens = new DebugLexerTokenInfo[Tokens.Count];

        for (int i = 0; i < tokens.Length; i++)
        {
            var token = Tokens[i];
            tokens[i] = new DebugLexerTokenInfo(
                i,
                token.Kind.ToString(),
                token.Value,
                DebugJson.GetDisplayText(token),
                DebugJson.GetMatchingKind(token.MatchingKind),
                DebugJson.CreateSpan(text, token.Span),
                DebugJson.CreateTrivia(text, token.LeadingTrivia),
                DebugJson.CreateTrivia(text, token.TrailingTrivia));
        }

        return DebugJson.Serialize(new DebugLexerInfo("lexer", tokens.Length, tokens));
    }

    private string FormatSpan(TextSpan span)
    {
        int startLine = span.GetStartLine(text) + 1;
        int startColumn = span.GetStartColumn(text) + 1;
        int endLine = span.GetEndLine(text) + 1;
        int endColumn = span.GetEndColumn(text) + 1;

        return $"[{span.Start}..{span.End}), len: {span.Length}, ({startLine}, {startColumn})..({endLine}, {endColumn})";
    }

    private static string FormatTokenValue(Token token)
    {
        if (token.Kind is TokenKind.EndToken)
            return "\"<eof>\"";

        if (token.Kind is TokenKind.MissingToken)
            return "\"<missing>\"";

        string value = Escape(token.Value);

        return string.IsNullOrEmpty(value)
            ? "\"\""
            : $"\"{value}\"";
    }

    private static string FormatTriviaSummary(Token token)
    {
        StringBuilder sb = new();

        if (token.LeadingTrivia.Length > 0)
        {
            sb.Append("leading: ");
            sb.Append(FormatTriviaKinds(token.LeadingTrivia));
        }

        if (token.TrailingTrivia.Length > 0)
        {
            if (sb.Length > 0)
                sb.Append(' ');

            sb.Append("trailing: ");
            sb.Append(FormatTriviaKinds(token.TrailingTrivia));
        }

        return sb.ToString();
    }

    private static string FormatTriviaKinds(SyntaxTrivia[] trivias)
    {
        StringBuilder sb = new();
        sb.Append('[');

        for (int i = 0; i < trivias.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");

            sb.Append(trivias[i].Kind);
        }

        sb.Append(']');
        return sb.ToString();
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value
            .Replace("\\", "\\\\")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t")
            .Replace("\"", "\\\"");
    }

    private static string Colorize(string value, string color)
    {
        if (!ShouldUseColor())
            return value;

        return $"{color}{value}{Reset}";
    }

    private static bool ShouldUseColor()
    {
        if (Console.IsOutputRedirected)
            return false;

        if (Environment.GetEnvironmentVariable("NO_COLOR") is not null)
            return false;

        string? term = Environment.GetEnvironmentVariable("TERM");
        return !string.IsNullOrEmpty(term) && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
    }
}