using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Maho.Cli;

internal static class SerializedAnalysisRenderer
{
    private const string Reset = "\u001b[0m";
    private const string Dim = "\u001b[2m";
    private const string BrightWhite = "\u001b[97m";
    private const string Red = "\u001b[31m";
    private const string Yellow = "\u001b[33m";
    private const string Cyan = "\u001b[36m";
    private const string BrightBlack = "\u001b[90m";
    private const string Green = "\u001b[32m";
    private const string Blue = "\u001b[34m";
    private const string Magenta = "\u001b[35m";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string RenderDebugOutput(CompilerAnalysisResult analysis, string displayPath, bool includeFileHeader, bool useColor)
    {
        try
        {
            string? lexerJson = analysis.LexerJson;
            string? parserJson = analysis.ParserJson;

            if (string.IsNullOrEmpty(lexerJson) && string.IsNullOrEmpty(parserJson))
                return string.Empty;

            StringBuilder sb = new();
            bool wroteAnything = false;

            if (includeFileHeader)
            {
                sb.AppendLine();
                sb.AppendLine(Colorize(displayPath, Dim, useColor));
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(lexerJson))
            {
                string lexerOutput = RenderLexerOutput(DeserializeJson<SerializedLexerInfo>(lexerJson), useColor);
                sb.Append(lexerOutput);

                if (!lexerOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                    sb.AppendLine();

                wroteAnything = true;
            }

            if (!string.IsNullOrEmpty(parserJson))
            {
                if (wroteAnything)
                    sb.AppendLine();

                string parserOutput = RenderParserOutput(DeserializeJson<SerializedParserInfo>(parserJson), useColor);
                sb.Append(parserOutput);

                if (!parserOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                    sb.AppendLine();

                wroteAnything = true;
            }

            if (wroteAnything && !sb.ToString().EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal))
                sb.AppendLine();

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return RenderInternalFailure(displayPath, $"Failed to render debug output: {ex.Message}", useColor);
        }
    }

    public static string RenderDiagnosticsOutput(CompilerAnalysisResult analysis, string displayPath, bool useColor)
    {
        DiagnosticInfo[] diagnostics = DeserializeJson<DiagnosticInfo[]>(analysis.DiagnosticsJson);

        if (diagnostics.Length == 0)
            return string.Empty;

        try
        {
            SourceBuffer buffer = SourceBuffer.Load(analysis.SourcePath);
            using StringWriter writer = new();

            writer.WriteLine(Colorize(displayPath, Dim, useColor));
            writer.WriteLine();

            PrintDiagnostics(writer, diagnostics, buffer, useColor);
            return writer.ToString();
        }
        catch (Exception ex)
        {
            using StringWriter writer = new();
            writer.WriteLine(Colorize(displayPath, Dim, useColor));
            writer.WriteLine();

            for (int i = 0; i < diagnostics.Length; i++)
                PrintDiagnosticSummary(writer, diagnostics[i], useColor);

            writer.WriteLine(Colorize($"tip: failed to load source context: {ex.Message}", BrightBlack, useColor));
            writer.WriteLine();
            return writer.ToString();
        }
    }

    public static string RenderInternalFailure(string displayPath, string errorMessage, bool useColor)
    {
        using StringWriter writer = new();
        writer.WriteLine(Colorize(displayPath, Dim, useColor));
        writer.WriteLine();
        writer.WriteLine($"{Colorize("(internal)", BrightWhite, useColor)} {Colorize("error", Red, useColor)} {Colorize("MHC9999", Red, useColor)}: Unhandled analysis failure.");
        writer.WriteLine();
        writer.WriteLine(errorMessage);
        writer.WriteLine();
        return writer.ToString();
    }

    public static string RenderUserFacingFailure(string displayPath, string errorMessage, bool useColor)
    {
        using StringWriter writer = new();
        writer.WriteLine(Colorize(displayPath, Dim, useColor));
        writer.WriteLine();
        writer.Write(Colorize("error", Red, useColor));
        writer.Write(": ");
        writer.WriteLine(errorMessage);
        writer.WriteLine();
        return writer.ToString();
    }

    private static string RenderLexerOutput(SerializedLexerInfo lexer, bool useColor)
    {
        StringBuilder sb = new();

        sb.AppendLine("Token Stream");
        sb.AppendLine();

        for (int i = 0; i < lexer.Tokens.Count; i++)
        {
            SerializedLexerTokenInfo token = lexer.Tokens[i];
            string matchingKind = string.IsNullOrEmpty(token.MatchingKind)
                ? string.Empty
                : Colorize($"/{token.MatchingKind}", Magenta, useColor);

            sb.Append(Colorize(token.Index.ToString("D3"), Dim, useColor));
            sb.Append(Colorize("  ", Dim, useColor));
            sb.Append(Colorize(token.Kind, Yellow, useColor));
            sb.Append(matchingKind);
            sb.Append(Colorize("  ", Dim, useColor));
            sb.Append(Colorize(FormatTokenValue(token.Kind, token.Text), BrightWhite, useColor));
            sb.Append(Colorize("  ", Dim, useColor));
            sb.Append(Colorize(FormatSpan(token.Span), Dim, useColor));

            if (token.LeadingTrivia.Count > 0 || token.TrailingTrivia.Count > 0)
            {
                sb.Append(Colorize("  ", Dim, useColor));
                sb.Append(Colorize(FormatTriviaSummary(token), Cyan, useColor));
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string RenderParserOutput(SerializedParserInfo parser, bool useColor)
    {
        if (parser.Root is null)
            return "Syntax Tree\n\n<unparsed>";

        StringBuilder sb = new();

        sb.AppendLine("Syntax Tree");
        sb.AppendLine();

        AppendParserNode(sb, parser.Root, string.Empty, isLast: true, propertyName: null, useColor);

        return sb.ToString();
    }

    private static void AppendParserNode(StringBuilder sb, SerializedParserNodeInfo node, string indent, bool isLast, string? propertyName, bool useColor)
    {
        sb.Append(Colorize(indent, Dim, useColor));
        sb.Append(Colorize(isLast ? "└── " : "├── ", Dim, useColor));

        if (!string.IsNullOrEmpty(propertyName))
        {
            sb.Append(Colorize(propertyName, Cyan, useColor));
            sb.Append(Colorize(" -> ", Dim, useColor));
        }

        sb.AppendLine(FormatNode(node, useColor));

        string childIndent = indent + (isLast ? "    " : "│   ");

        for (int i = 0; i < node.Children.Count; i++)
        {
            SerializedParserChildInfo child = node.Children[i];
            AppendParserNode(sb, child.Node, childIndent, i == node.Children.Count - 1, child.PropertyName, useColor);
        }
    }

    private static string FormatNode(SerializedParserNodeInfo node, bool useColor)
    {
        string spanText = node.Span is null
            ? string.Empty
            : $" {Colorize(FormatSpan(node.Span), Dim, useColor)}";

        if (!string.IsNullOrEmpty(node.TokenKind))
        {
            string matchingKind = string.IsNullOrEmpty(node.MatchingKind)
                ? string.Empty
                : Colorize($"/{node.MatchingKind}", Magenta, useColor);

            return $"{Colorize("Token", Blue, useColor)} {Colorize(node.TokenKind, Yellow, useColor)}{matchingKind} {Colorize(FormatTokenValue(node.TokenKind, node.Text), BrightWhite, useColor)}{spanText}";
        }

        return $"{Colorize(node.NodeType, Green, useColor)}{spanText}";
    }

    private static void PrintDiagnostics(TextWriter writer, IReadOnlyList<DiagnosticInfo> diagnostics, SourceBuffer buffer, bool useColor)
    {
        List<(DiagnosticInfo Diagnostic, int Index)> orderedDiagnostics = [];

        for (int i = 0; i < diagnostics.Count; i++)
            orderedDiagnostics.Add((diagnostics[i], i));

        orderedDiagnostics.Sort(static (left, right) =>
        {
            int byLine = left.Diagnostic.Span.StartLocation.Line.CompareTo(right.Diagnostic.Span.StartLocation.Line);

            if (byLine != 0)
                return byLine;

            int byColumn = left.Diagnostic.Span.StartLocation.Column.CompareTo(right.Diagnostic.Span.StartLocation.Column);

            if (byColumn != 0)
                return byColumn;

            return left.Index.CompareTo(right.Index);
        });

        for (int i = 0; i < orderedDiagnostics.Count; i++)
            PrintDiagnostic(writer, orderedDiagnostics[i].Diagnostic, buffer, useColor);
    }

    private static void PrintDiagnostic(TextWriter writer, DiagnosticInfo diagnostic, SourceBuffer buffer, bool useColor)
    {
        PrintDiagnosticSummary(writer, diagnostic, useColor);
        writer.WriteLine();

        int startLineIndex = buffer.GetLineIndex(diagnostic.Span.Start);
        int endLineIndex = buffer.GetLineIndex(diagnostic.Span.End);

        PrintDiagnosticContext(
            writer,
            diagnostic,
            buffer,
            startLineIndex,
            endLineIndex,
            GetDiagnosticColor(diagnostic.Severity),
            diagnostic.Span.EndLocation.Line,
            diagnostic.Span.EndLocation.Column,
            useColor);

        writer.WriteLine();
    }

    private static void PrintDiagnosticSummary(TextWriter writer, DiagnosticInfo diagnostic, bool useColor)
    {
        string severity = diagnostic.Severity.ToString().ToLowerInvariant();
        string accent = GetDiagnosticColor(diagnostic.Severity);

        writer.Write(Colorize($"({diagnostic.Span.StartLocation.Line}, {diagnostic.Span.StartLocation.Column}) ", BrightWhite, useColor));
        writer.Write(Colorize($"{severity} ", accent, useColor));
        writer.Write(Colorize(diagnostic.Code, accent, useColor));
        writer.Write(": ");
        writer.WriteLine(diagnostic.Message);
    }

    private static void PrintDiagnosticContext(TextWriter writer, DiagnosticInfo diagnostic, SourceBuffer buffer, int startLineIndex, int endLineIndex, string accent, int endLineNumber, int endColumn, bool useColor)
    {
        int maxContextLines = 3;
        int lastLineIndex = Math.Min(endLineIndex, startLineIndex + maxContextLines - 1);
        int lineNumberWidth = Math.Max(2, (lastLineIndex + 1).ToString().Length);
        string? tipIndent = null;

        for (int lineIndex = startLineIndex; lineIndex <= lastLineIndex; lineIndex++)
        {
            SourceLine line = buffer.Lines[lineIndex];
            int lineNumber = lineIndex + 1;

            writer.Write(Colorize($"{lineNumber.ToString().PadLeft(lineNumberWidth)} | ", Dim, useColor));
            writer.WriteLine(line.Text.Replace("\t", "    "));

            int underlineStart = GetUnderlineStart(diagnostic.Span, line);
            int underlineWidth = GetUnderlineWidth(diagnostic.Span, line, lineIndex == endLineIndex && diagnostic.Span.Length == 0);
            string markerIndent = ExpandIndentation(line.Text, underlineStart);
            string marker = new('^', Math.Max(1, underlineWidth));

            writer.Write(Colorize($"{new string(' ', lineNumberWidth)} | ", Dim, useColor));
            writer.Write(markerIndent);
            writer.Write(Colorize(marker, accent, useColor));
            writer.WriteLine();

            if (lineIndex == startLineIndex)
                tipIndent = markerIndent;
        }

        if (tipIndent is not null)
            PrintDiagnosticTip(writer, diagnostic, lineNumberWidth, tipIndent, useColor);

        if (lastLineIndex < endLineIndex)
        {
            writer.Write(Colorize($"{new string(' ', lineNumberWidth)} | ", Dim, useColor));
            writer.WriteLine(Colorize("...", Dim, useColor));
            writer.Write(Colorize($"{new string(' ', lineNumberWidth)} | ", Dim, useColor));
            writer.WriteLine(Colorize($"continues through line {endLineNumber}, column {endColumn}", Dim, useColor));
        }
    }

    private static void PrintDiagnosticTip(TextWriter writer, DiagnosticInfo diagnostic, int lineNumberWidth, string indent, bool useColor)
    {
        writer.Write(Colorize($"{new string(' ', lineNumberWidth)} | ", Dim, useColor));
        writer.Write(indent);
        writer.Write(Colorize("└─ ", BrightBlack, useColor));
        writer.Write(Colorize("tip", Cyan, useColor));
        writer.Write(": ");
        writer.WriteLine(Colorize(GetDiagnosticTip(diagnostic), Dim, useColor));
    }

    private static int GetUnderlineStart(TextSpanInfo span, SourceLine line)
    {
        if (span.Start <= line.Start)
            return 0;

        return Math.Min(span.Start - line.Start, line.Text.Length);
    }

    private static int GetUnderlineWidth(TextSpanInfo span, SourceLine line, bool zeroLengthAtEndLine)
    {
        int highlightStart = Math.Max(span.Start, line.Start);
        int highlightEnd = Math.Min(span.End, line.End);
        int width = highlightEnd - highlightStart;

        if (width > 0)
            return GetExpandedWidth(line.Text, highlightStart - line.Start, width);

        if (zeroLengthAtEndLine)
            return 1;

        return 1;
    }

    private static string ExpandIndentation(string text, int count)
    {
        if (count <= 0)
            return string.Empty;

        int safeCount = Math.Min(count, text.Length);
        string prefix = text[..safeCount];
        char[] spaces = new char[prefix.Length];

        for (int i = 0; i < prefix.Length; i++)
            spaces[i] = prefix[i] == '\t' ? '\t' : ' ';

        return new string(spaces).Replace("\t", "    ");
    }

    private static int GetExpandedWidth(string text, int start, int width)
    {
        if (width <= 0)
            return 1;

        int safeStart = Math.Min(start, text.Length);
        int safeWidth = Math.Min(width, text.Length - safeStart);

        if (safeWidth <= 0)
            return 1;

        return text.Substring(safeStart, safeWidth).Replace("\t", "    ").Length;
    }

    private static string GetDiagnosticTip(DiagnosticInfo diagnostic) => diagnostic.Code switch
    {
        "MHC0001" => "Remove or replace this token.",
        "MHC0002" => "Add the closing double quote before the line ends.",
        "MHC0003" => "Add the closing single quote before the line ends.",
        "MHC0004" => "Add at least one character between the quotes.",
        "MHC1001" => "Insert the missing token here.",
        "MHC1002" => "Add an expression here.",
        "MHC1003" => "Add an identifier here.",
        "MHC1004" => "Add a type here.",
        _ => "Check this location."
    };

    private static string GetDiagnosticColor(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => Red,
        DiagnosticSeverity.Warning => Yellow,
        DiagnosticSeverity.Info => Cyan,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unhandled diagnostic severity.")
    };

    private static string FormatTriviaSummary(SerializedLexerTokenInfo token)
    {
        StringBuilder sb = new();

        if (token.LeadingTrivia.Count > 0)
        {
            sb.Append("leading: ");
            sb.Append(FormatTriviaKinds(token.LeadingTrivia));
        }

        if (token.TrailingTrivia.Count > 0)
        {
            if (sb.Length > 0)
                sb.Append(' ');

            sb.Append("trailing: ");
            sb.Append(FormatTriviaKinds(token.TrailingTrivia));
        }

        return sb.ToString();
    }

    private static string FormatTriviaKinds(IReadOnlyList<SerializedSyntaxTriviaInfo> trivias)
    {
        StringBuilder sb = new();
        sb.Append('[');

        for (int i = 0; i < trivias.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");

            sb.Append(trivias[i].Kind);
        }

        sb.Append(']');
        return sb.ToString();
    }

    private static string FormatTokenValue(string kind, string? text)
    {
        if (kind is "EndToken")
            return "\"<eof>\"";

        if (kind is "MissingToken")
            return "\"<missing>\"";

        string value = Escape(text ?? string.Empty);

        return string.IsNullOrEmpty(value)
            ? "\"\""
            : $"\"{value}\"";
    }

    private static string FormatSpan(SerializedTextSpanInfo span) =>
        $"[{span.Start}..{span.End}), len: {span.Length}, ({span.StartLine}, {span.StartColumn})..({span.EndLine}, {span.EndColumn})";

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

    private static T DeserializeJson<T>(string json)
    {
        T? value = JsonSerializer.Deserialize<T>(json, JsonOptions);

        if (value is null)
            throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name}.");

        return value;
    }

    private static string Colorize(string value, string color, bool useColor)
    {
        if (!useColor || !ShouldUseColor())
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

    private readonly struct SourceLine
    {
        public string Text { get; }
        public int Start { get; }
        public int End { get; }

        public SourceLine(string text, int start, int end)
        {
            Text = text;
            Start = start;
            End = end;
        }
    }

    private sealed class SourceBuffer
    {
        private readonly SourceLine[] lines;

        public SourceLine[] Lines => lines;

        private SourceBuffer(string text)
        {
            lines = ParseLines(text);
        }

        public static SourceBuffer Load(string filePath) => new(File.ReadAllText(filePath));

        public int GetLineIndex(int position)
        {
            int lower = 0;
            int upper = lines.Length - 1;

            while (lower <= upper)
            {
                int index = lower + ((upper - lower) >> 1);
                int start = lines[index].Start;

                if (position == start)
                    return index;

                if (position < start)
                    upper = index - 1;
                else
                    lower = index + 1;
            }

            return Math.Max(0, lower - 1);
        }

        private static SourceLine[] ParseLines(string text)
        {
            List<SourceLine> parsedLines = [];
            int position = 0;
            int lineStart = 0;

            while (position < text.Length)
            {
                int breakWidth = GetLineBreakWidth(text, position);

                if (breakWidth == 0)
                {
                    position++;
                    continue;
                }

                AddLine(parsedLines, text, lineStart, position);
                position += breakWidth;
                lineStart = position;
            }

            if (position >= lineStart)
                AddLine(parsedLines, text, lineStart, position);

            return [.. parsedLines];
        }

        private static void AddLine(List<SourceLine> lines, string text, int start, int end) =>
            lines.Add(new SourceLine(text.Substring(start, end - start), start, end));

        private static int GetLineBreakWidth(string text, int position)
        {
            char ch = text[position];
            char next = position + 1 < text.Length ? text[position + 1] : '\0';

            if (ch == '\r' && next == '\n')
                return 2;

            if (ch == '\r' || ch == '\n')
                return 1;

            return 0;
        }
    }

    private sealed record SerializedTextSpanInfo(int Start, int Length, int End, int StartLine, int StartColumn, int EndLine, int EndColumn);

    private sealed record SerializedSyntaxTriviaInfo(string Kind, string Text, SerializedTextSpanInfo Span);

    private sealed record SerializedLexerTokenInfo(
        int Index,
        string Kind,
        string Text,
        string DisplayText,
        string? MatchingKind,
        SerializedTextSpanInfo Span,
        IReadOnlyList<SerializedSyntaxTriviaInfo> LeadingTrivia,
        IReadOnlyList<SerializedSyntaxTriviaInfo> TrailingTrivia);

    private sealed record SerializedLexerInfo(string Kind, int TokenCount, IReadOnlyList<SerializedLexerTokenInfo> Tokens);

    private sealed record SerializedParserChildInfo(string PropertyName, SerializedParserNodeInfo Node);

    private sealed record SerializedParserNodeInfo(
        string NodeType,
        SerializedTextSpanInfo? Span,
        string? TokenKind,
        string? Text,
        string? DisplayText,
        string? MatchingKind,
        IReadOnlyList<SerializedSyntaxTriviaInfo>? LeadingTrivia,
        IReadOnlyList<SerializedSyntaxTriviaInfo>? TrailingTrivia,
        IReadOnlyList<SerializedParserChildInfo> Children);

    private sealed record SerializedParserInfo(string Kind, SerializedParserNodeInfo? Root);
}
