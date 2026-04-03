using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maho.Syntax;
using Maho.Text;

namespace Maho;

/// <summary>
/// Centralizes serialization helpers and DTOs for lexer and parser debug payloads. These types are
/// transport models rather than compiler-domain nodes, which keeps inspection output stable even if
/// internal syntax representations evolve.
/// </summary>
internal static class DebugJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary> Serializes a debug payload using the compiler's shared JSON conventions for inspection data. </summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);

    /// <summary>
    /// Converts an internal text span into the compact debug span schema used by lexer and parser
    /// payloads, reusing the same line/column projection as public diagnostics.
    /// </summary>
    public static DebugTextSpanInfo CreateSpan(SourceText text, TextSpan span)
    {
        // Reuse the public span projection so debug payloads and diagnostics never disagree about
        // line/column math for the same source range.
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

    /// <summary>
    /// Projects trivia into a serializable form that preserves kind, captured text, and span data
    /// for downstream renderers and snapshot-style tests.
    /// </summary>
    public static DebugSyntaxTriviaInfo[] CreateTrivia(SourceText text, IReadOnlyList<SyntaxTrivia> trivias)
    {
        DebugSyntaxTriviaInfo[] triviaItems = new DebugSyntaxTriviaInfo[trivias.Count];

        for (int i = 0; i < trivias.Count; i++)
        {
            SyntaxTrivia trivia = trivias[i];
            // Capture the original trivia text as well as the kind so downstream tooling can choose
            // between structural and source-faithful views.
            triviaItems[i] = new DebugSyntaxTriviaInfo(
                trivia.Kind.ToString(),
                text.ToString(trivia.Span),
                CreateSpan(text, trivia.Span));
        }

        return triviaItems;
    }

    /// <summary>
    /// Suppresses the sentinel <c>None</c> value so contextual-keyword metadata is omitted when it
    /// would not add information to the payload.
    /// </summary>
    public static string? GetMatchingKind(MatchingKeywordKind kind) =>
        kind is MatchingKeywordKind.None ? null : kind.ToString();

    /// <summary>
    /// Normalizes sentinel token text into explicit display strings so debug consumers do not need
    /// to infer special cases from token kind and span shape.
    /// </summary>
    public static string GetDisplayText(Token token) => token.Kind switch
    {
        TokenKind.EndToken => "<eof>",
        TokenKind.MissingToken => "<missing>",
        _ => token.Value
    };
}

/// <summary> Serialized representation of a source span used in debug payloads. </summary>
internal sealed record DebugTextSpanInfo(
    int Start,
    int Length,
    int End,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

/// <summary> Serialized representation of one trivia item attached to a token. </summary>
internal sealed record DebugSyntaxTriviaInfo(string Kind, string Text, DebugTextSpanInfo Span);

/// <summary> Serialized representation of one token in lexer debug output. </summary>
internal sealed record DebugLexerTokenInfo(
    int Index,
    string Kind,
    string Text,
    string DisplayText,
    string? MatchingKind,
    DebugTextSpanInfo Span,
    IReadOnlyList<DebugSyntaxTriviaInfo> LeadingTrivia,
    IReadOnlyList<DebugSyntaxTriviaInfo> TrailingTrivia);

/// <summary> Root payload for serialized lexer debug output. </summary>
internal sealed record DebugLexerInfo(string Kind, int TokenCount, IReadOnlyList<DebugLexerTokenInfo> Tokens);

/// <summary> Associates a serialized parser child with the property name it originated from. </summary>
internal sealed record DebugParserChildInfo(string PropertyName, DebugParserNodeInfo Node);

/// <summary> Serialized representation of one parser node or token in the debug tree. </summary>
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

/// <summary> Root payload for serialized parser debug output. </summary>
internal sealed record DebugParserInfo(string Kind, DebugParserNodeInfo? Root);
