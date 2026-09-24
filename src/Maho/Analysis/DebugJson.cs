using System;
using System.Collections.Generic;
using System.Text.Json;
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
    /// <summary> Serializes a debug payload using the compiler's shared JSON conventions for inspection data. </summary>
    public static string Serialize(DebugLexerInfo info) => JsonSerializer.Serialize(info, MahoJsonContext.Default.DebugLexerInfo);
    public static string Serialize(DebugParserInfo info) => JsonSerializer.Serialize(info, MahoJsonContext.Default.DebugParserInfo);
    public static string Serialize(IReadOnlyList<DiagnosticInfo> diagnostics) => JsonSerializer.Serialize(diagnostics, MahoJsonContext.Default.IReadOnlyListDiagnosticInfo);
    public static string Serialize(List<DiagnosticInfo> diagnostics) => JsonSerializer.Serialize(diagnostics, MahoJsonContext.Default.ListDiagnosticInfo);

    public static string Serialize<T>(T value)
    {
        if (value is DebugLexerInfo lexerInfo)
            return Serialize(lexerInfo);
        if (value is DebugParserInfo parserInfo)
            return Serialize(parserInfo);
        if (value is IReadOnlyList<DiagnosticInfo> diagList)
            return Serialize(diagList);

        throw new NotSupportedException($"Type {typeof(T).FullName} is not supported by trim-safe MahoJsonContext.");
    }

    /// <summary>
    /// Converts an internal text span into the compact debug span schema used by lexer and parser
    /// payloads, reusing the same line/column projection as public diagnostics.
    /// </summary>
    public static DebugTextSpanInfo CreateSpan(SourceText text, TextSpan span)
    {
        return new DebugTextSpanInfo(
            span.Start,
            span.Length,
            span.End,
            span.GetStartLine(text) + 1,
            span.GetStartColumn(text) + 1,
            span.GetEndLine(text) + 1,
            span.GetEndColumn(text) + 1);
    }

    /// <summary>
    /// Projects trivia into a serializable form that preserves kind, captured text, and span data
    /// for downstream renderers and snapshot-style tests.
    /// </summary>
    public static DebugSyntaxTriviaInfo[] CreateTrivia(SourceText text, ReadOnlySpan<SyntaxTrivia> trivias)
    {
        DebugSyntaxTriviaInfo[] triviaItems = new DebugSyntaxTriviaInfo[trivias.Length];

        for (int i = 0; i < trivias.Length; i++)
        {
            SyntaxTrivia trivia = trivias[i];
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
    DebugSyntaxTriviaInfo[] LeadingTrivia,
    DebugSyntaxTriviaInfo[] TrailingTrivia);

/// <summary> Root payload for serialized lexer debug output. </summary>
internal sealed record DebugLexerInfo(string Kind, int TokenCount, DebugLexerTokenInfo[] Tokens);

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
    DebugSyntaxTriviaInfo[]? LeadingTrivia,
    DebugSyntaxTriviaInfo[]? TrailingTrivia,
    DebugParserChildInfo[] Children);

/// <summary> Root payload for serialized parser debug output. </summary>
internal sealed record DebugParserInfo(string Kind, DebugParserNodeInfo? Root);
