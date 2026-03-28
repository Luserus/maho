using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maho.Syntax;
using Maho.Text;

namespace Maho;

internal static class DebugJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);

    public static DebugTextSpanInfo CreateSpan(SourceText text, TextSpan span)
    {
        TextSpanInfo spanInfo = MahoCompiler.CreateSpanInfo(span, text);
        return new DebugTextSpanInfo(
            spanInfo.Start,
            spanInfo.Length,
            spanInfo.End,
            spanInfo.StartLocation.Line,
            spanInfo.StartLocation.Column,
            spanInfo.EndLocation.Line,
            spanInfo.EndLocation.Column);
    }

    public static DebugSyntaxTriviaInfo[] CreateTrivia(SourceText text, IReadOnlyList<SyntaxTrivia> trivias)
    {
        DebugSyntaxTriviaInfo[] triviaItems = new DebugSyntaxTriviaInfo[trivias.Count];

        for (int i = 0; i < trivias.Count; i++)
        {
            SyntaxTrivia trivia = trivias[i];
            triviaItems[i] = new DebugSyntaxTriviaInfo(
                trivia.Kind.ToString(),
                text.ToString(trivia.Span),
                CreateSpan(text, trivia.Span));
        }

        return triviaItems;
    }

    public static string? GetMatchingKind(MatchingKeywordKind kind) =>
        kind is MatchingKeywordKind.None ? null : kind.ToString();

    public static string GetDisplayText(Token token) => token.Kind switch
    {
        TokenKind.EndToken => "<eof>",
        TokenKind.MissingToken => "<missing>",
        _ => token.Value
    };
}

internal sealed record DebugTextSpanInfo(
    int Start,
    int Length,
    int End,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

internal sealed record DebugSyntaxTriviaInfo(string Kind, string Text, DebugTextSpanInfo Span);

internal sealed record DebugLexerTokenInfo(
    int Index,
    string Kind,
    string Text,
    string DisplayText,
    string? MatchingKind,
    DebugTextSpanInfo Span,
    IReadOnlyList<DebugSyntaxTriviaInfo> LeadingTrivia,
    IReadOnlyList<DebugSyntaxTriviaInfo> TrailingTrivia);

internal sealed record DebugLexerInfo(string Kind, int TokenCount, IReadOnlyList<DebugLexerTokenInfo> Tokens);

internal sealed record DebugParserChildInfo(string PropertyName, DebugParserNodeInfo Node);

internal sealed record DebugParserNodeInfo(
    string NodeType,
    DebugTextSpanInfo? Span,
    string? TokenKind,
    string? Text,
    string? DisplayText,
    string? MatchingKind,
    IReadOnlyList<DebugSyntaxTriviaInfo>? LeadingTrivia,
    IReadOnlyList<DebugSyntaxTriviaInfo>? TrailingTrivia,
    IReadOnlyList<DebugParserChildInfo> Children);

internal sealed record DebugParserInfo(string Kind, DebugParserNodeInfo? Root);
