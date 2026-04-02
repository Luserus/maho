using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Maho.Cli;

/// <summary>
/// Reconstructs human-readable CLI output from the serialized analysis artifacts returned by the
/// core library. This keeps the terminal renderer decoupled from parser and lexer implementation
/// details while still producing rich, source-aware output.
/// </summary>
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

    /// <summary>
    /// Renders lexer and parser debug payloads from a completed analysis result. The renderer works
    /// entirely from serialized payloads so it can run after the core analysis objects are gone.
    /// </summary>
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
                // Debug rendering intentionally round-trips through the serialized contract so the
                // CLI depends on payload shape, not live lexer objects.
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

                // Keep lexer and parser sections independently renderable so callers can request
                // either payload without paying a formatting penalty.
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

    /// <summary>
    /// Renders diagnostics with source context when the original file can be reloaded, and falls
    /// back to summary-only output when contextual rendering is no longer possible.
    /// </summary>
    public static string RenderDiagnosticsOutput(CompilerAnalysisResult analysis, string displayPath, bool useColor)
    {
        DiagnosticInfo[] diagnostics = DeserializeJson<DiagnosticInfo[]>(analysis.DiagnosticsJson);

        if (diagnostics.Length == 0)
            return string.Empty;

        try
        {
            // Diagnostics carry offsets and locations, but not source excerpts. The renderer reloads
            // the file so it can reconstruct highlighted context after analysis has finished.
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

            // Losing source context should degrade the report, not erase it. Keep the diagnostic
            // summaries and explain why excerpts are missing.
            for (int i = 0; i < diagnostics.Length; i++)
                PrintDiagnosticSummary(writer, diagnostics[i], useColor);

            writer.WriteLine(Colorize($"tip: failed to load source context: {ex.Message}", BrightBlack, useColor));
            writer.WriteLine();
            return writer.ToString();
        }
    }

    /// <summary>
    /// Formats an analysis failure as an internal compiler problem so it stays visually distinct
    /// from normal user-facing syntax diagnostics.
    /// </summary>
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

    /// <summary>
    /// Formats environmental or input-related failures without implying a compiler defect.
    /// </summary>
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

    /// <summary>
    /// Produces the token-stream view used by <c>--lex</c>, including trivia summaries and
    /// matching-keyword metadata for contextual tokens.
    /// </summary>
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

    /// <summary>
    /// Produces the tree view used by <c>--parse</c>. A missing root is rendered as an explicit
    /// "unparsed" state rather than as an empty block.
    /// </summary>
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

    /// <summary>
    /// Recursively writes one serialized parser node and all of its descendants using a stable
    /// tree-layout convention derived from the debug payload.
    /// </summary>
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

    /// <summary>
    /// Chooses the textual representation for a serialized parser node, rendering tokens and
    /// non-token syntax nodes differently so the tree stays easy to scan.
    /// </summary>
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

    /// <summary>
    /// Sorts diagnostics into source order before printing so output stays stable even if earlier
    /// stages reported diagnostics in a different sequence.
    /// </summary>
    private static void PrintDiagnostics(TextWriter writer, IReadOnlyList<DiagnosticInfo> diagnostics, SourceBuffer buffer, bool useColor)
    {
        List<(DiagnosticInfo Diagnostic, int Index)> orderedDiagnostics = [];

        for (int i = 0; i < diagnostics.Count; i++)
            orderedDiagnostics.Add((diagnostics[i], i));

        // Diagnostics are re-sorted here because production order reflects parser recovery paths,
        // while human readers expect source order.
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

    /// <summary>
    /// Writes the summary line and highlighted source excerpt for a single diagnostic.
    /// </summary>
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

    /// <summary>
    /// Writes the location, severity, code, and message header shared by both full and fallback
    /// diagnostics rendering paths.
    /// </summary>
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

    /// <summary>
    /// Writes the source excerpt for a diagnostic while preserving enough formatting information to
    /// align carets correctly across tabs, zero-width spans, and multi-line ranges.
    /// </summary>
    private static void PrintDiagnosticContext(TextWriter writer, DiagnosticInfo diagnostic, SourceBuffer buffer, int startLineIndex, int endLineIndex, string accent, int endLineNumber, int endColumn, bool useColor)
    {
        int maxContextLines = 3;
        bool showNextLineContext =
            diagnostic.Span.Length == 0 &&
            startLineIndex == endLineIndex &&
            endLineIndex + 1 < buffer.Lines.Length &&
            diagnostic.Span.Start == buffer.Lines[endLineIndex].End;

        int lastLineIndex = Math.Min(endLineIndex, startLineIndex + maxContextLines - 1);
        int finalDisplayedLineIndex = showNextLineContext ? Math.Min(lastLineIndex + 1, buffer.Lines.Length - 1) : lastLineIndex;
        int lineNumberWidth = Math.Max(2, (finalDisplayedLineIndex + 1).ToString().Length);
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

            // Reuse the first-line indentation when placing the follow-up tip so the hint visually
            // points at the same recovery site as the underline.
            if (lineIndex == startLineIndex)
                tipIndent = markerIndent;
        }

        if (showNextLineContext)
        {
            SourceLine nextLine = buffer.Lines[endLineIndex + 1];
            int nextLineNumber = endLineIndex + 2;
            string renderedNextLine = nextLine.Text.Replace("\t", "    ");
            string previewText = ClipLineForConnector(renderedNextLine, tipIndent?.Length ?? 0);

            writer.Write(Colorize($"{nextLineNumber.ToString().PadLeft(lineNumberWidth)} | ", Dim, useColor));
            writer.Write(previewText);

            if (tipIndent is not null)
            {
                if (previewText.Length < tipIndent.Length)
                    writer.Write(new string(' ', tipIndent.Length - previewText.Length));

                writer.Write(Colorize("│", BrightBlack, useColor));
            }

            writer.WriteLine();
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

    /// <summary>
    /// Emits a small hint beneath the first highlighted line so diagnostics can suggest a likely
    /// recovery step without bloating the headline message.
    /// </summary>
    private static void PrintDiagnosticTip(TextWriter writer, DiagnosticInfo diagnostic, int lineNumberWidth, string indent, bool useColor)
    {
        writer.Write(Colorize($"{new string(' ', lineNumberWidth)} | ", Dim, useColor));
        writer.Write(indent);
        writer.Write(Colorize("└─ ", BrightBlack, useColor));
        writer.WriteLine(Colorize(GetDiagnosticTip(diagnostic), Cyan, useColor));
    }

    /// <summary>
    /// Computes the character offset where highlighting should begin for a line fragment in the
    /// rendered excerpt.
    /// </summary>
    private static int GetUnderlineStart(TextSpanInfo span, SourceLine line)
    {
        if (span.Start <= line.Start)
            return 0;

        return Math.Min(span.Start - line.Start, line.Text.Length);
    }

    /// <summary>
    /// Computes the visible width of the underline for one line, expanding tabs to preserve caret
    /// alignment and guaranteeing at least one marker for empty spans.
    /// </summary>
    private static int GetUnderlineWidth(TextSpanInfo span, SourceLine line, bool zeroLengthAtEndLine)
    {
        int highlightStart = Math.Max(span.Start, line.Start);
        int highlightEnd = Math.Min(span.End, line.End);
        int width = highlightEnd - highlightStart;

        if (width > 0)
            return GetExpandedWidth(line.Text, highlightStart - line.Start, width);

        // Parser recovery can produce zero-length spans; still render a single caret so the user
        // has a visible insertion point.
        if (zeroLengthAtEndLine)
            return 1;

        return 1;
    }

    /// <summary>
    /// Converts the prefix preceding a highlight into a whitespace-only string with tabs expanded
    /// to the same width used when printing source lines.
    /// </summary>
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

    /// <summary>
    /// Measures the visible width of a source slice after tab expansion, matching the width rules
    /// used by the rendered source excerpt.
    /// </summary>
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

    private static string ClipLineForConnector(string text, int connectorColumn)
    {
        if (connectorColumn <= 0 || text.Length < connectorColumn)
            return text;

        if (connectorColumn <= 4)
            return new string('.', connectorColumn);

        return text[..(connectorColumn - 4)] + "...";
    }

    /// <summary>
    /// Provides lightweight remediation hints for known diagnostic codes while leaving unknown codes
    /// with a generic prompt.
    /// </summary>
    private static string GetDiagnosticTip(DiagnosticInfo diagnostic)
    {
        string? codeTip = diagnostic.Code switch
        {
            "MH0000" => "Remove or replace this token.",
            "MH0001" => "Add the closing \" before the line ends.",
            "MH0002" => "Add the closing ' before the line ends.",
            "MH0003" => "Add at least one character between the ''.",
            "MH0008" => "Add a body here or terminate the declaration correctly.",
            _ => null
        };

        if (codeTip is not null)
            return codeTip;

        if (TryCreateExpectedTextTip(diagnostic.ExpectedText, out string expectedTextTip))
            return expectedTextTip;

        return "Check here.";
    }

    private static bool TryCreateExpectedTextTip(string? expectedText, out string tip)
    {
        if (string.IsNullOrWhiteSpace(expectedText) || string.Equals(expectedText, "valid syntax", StringComparison.Ordinal))
        {
            tip = string.Empty;
            return false;
        }

        tip = $"Add {expectedText} here.";
        return true;
    }

    /// <summary>
    /// Maps diagnostic severities to the accent color used consistently across summaries and
    /// highlighted carets.
    /// </summary>
    private static string GetDiagnosticColor(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => Red,
        DiagnosticSeverity.Warning => Yellow,
        DiagnosticSeverity.Info => Cyan,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unhandled diagnostic severity.")
    };

    /// <summary>
    /// Produces the compact trivia annotation appended to token lines so the lexer view can expose
    /// trivia without exploding into one line per trivia item.
    /// </summary>
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

    /// <summary>
    /// Formats a trivia collection as a compact bracketed list of kinds, intentionally omitting the
    /// trivia text because the lexer view is optimized for scanability.
    /// </summary>
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

    /// <summary>
    /// Normalizes token text for display so synthetic sentinel tokens stay recognizable and normal
    /// text is escaped the same way everywhere in the renderer.
    /// </summary>
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

    /// <summary>
    /// Formats a serialized span in one compact string that shows both raw offsets and user-facing
    /// line and column endpoints.
    /// </summary>
    private static string FormatSpan(SerializedTextSpanInfo span) =>
        $"[{span.Start}..{span.End}), len: {span.Length}, ({span.StartLine}, {span.StartColumn})..({span.EndLine}, {span.EndColumn})";

    /// <summary>
    /// Escapes control characters and quotes so token text can be rendered inline without changing
    /// the surrounding layout.
    /// </summary>
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

    /// <summary>
    /// Deserializes a renderer DTO from compiler-produced JSON and fails fast when the payload shape
    /// no longer matches what the renderer expects.
    /// </summary>
    private static T DeserializeJson<T>(string json)
    {
        T? value = JsonSerializer.Deserialize<T>(json, JsonOptions);

        // A null result here means the serialized contract drifted, which is a renderer bug rather
        // than a recoverable formatting oddity.
        if (value is null)
            throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name}.");

        return value;
    }

    /// <summary>
    /// Applies ANSI color only when the current rendering mode and output stream can support it.
    /// </summary>
    private static string Colorize(string value, string color, bool useColor)
    {
        if (!useColor || !ShouldUseColor())
            return value;

        return $"{color}{value}{Reset}";
    }

    /// <summary>
    /// Uses stdout-specific terminal state to decide whether emitted color would help a human reader
    /// or interfere with redirected output.
    /// </summary>
    private static bool ShouldUseColor()
    {
        if (Console.IsOutputRedirected)
            return false;

        if (Environment.GetEnvironmentVariable("NO_COLOR") is not null)
            return false;

        string? term = Environment.GetEnvironmentVariable("TERM");
        return !string.IsNullOrEmpty(term) && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Minimal line model used only by the renderer so source excerpts can be generated without
    /// depending on the compiler's heavier source-text abstractions.
    /// </summary>
    private readonly struct SourceLine
    {
        public string Text { get; }
        public int Start { get; }
        public int End { get; }

        /// <summary>
        /// Stores already-sliced line text together with the original absolute offsets so rendered
        /// diagnostics can translate spans back into line-local coordinates.
        /// </summary>
        public SourceLine(string text, int start, int end)
        {
            Text = text;
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// Renderer-local text buffer that supports line parsing and offset-to-line lookup using only
    /// the data needed for diagnostics rendering.
    /// </summary>
    private sealed class SourceBuffer
    {
        private readonly SourceLine[] lines;

        /// <summary>
        /// Gets the parsed line table used for excerpt rendering and span-to-line translation.
        /// </summary>
        public SourceLine[] Lines => lines;

        /// <summary>
        /// Initializes the buffer from already-loaded text so the line table can be computed once
        /// and reused across all diagnostics for the same file.
        /// </summary>
        private SourceBuffer(string text)
        {
            lines = ParseLines(text);
        }

        /// <summary>
        /// Reloads the original source file so diagnostics can render excerpts even though the CLI
        /// itself only holds serialized analysis results.
        /// </summary>
        public static SourceBuffer Load(string filePath) => new(File.ReadAllText(filePath));

        /// <summary>
        /// Maps an absolute character offset to its containing line via binary search.
        /// </summary>
        public int GetLineIndex(int position)
        {
            int lower = 0;
            int upper = lines.Length - 1;

            // Match the core text layer and treat line lookup as a binary-search problem so large
            // files do not make diagnostics rendering scale linearly with the number of lines.
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

        /// <summary>
        /// Splits text into renderable lines while preserving the absolute offsets needed to project
        /// diagnostics back into each line.
        /// </summary>
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
                // Keep the final unterminated line visible in diagnostics output.
                AddLine(parsedLines, text, lineStart, position);

            return [.. parsedLines];
        }

        /// <summary>
        /// Adds one parsed line using a sliced copy of the source text, which keeps later rendering
        /// logic simple and independent from the original full-text buffer.
        /// </summary>
        private static void AddLine(List<SourceLine> lines, string text, int start, int end) =>
            lines.Add(new SourceLine(text.Substring(start, end - start), start, end));

        /// <summary>
        /// Recognizes the line terminator width at a given position so line parsing can treat CRLF
        /// as one break while still handling CR-only and LF-only files.
        /// </summary>
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

    /// <summary>
    /// DTO used by the renderer when consuming serialized span information from compiler output.
    /// </summary>
    private sealed record SerializedTextSpanInfo(int Start, int Length, int End, int StartLine, int StartColumn, int EndLine, int EndColumn);

    /// <summary>
    /// DTO used by the renderer when consuming serialized trivia information from compiler output.
    /// </summary>
    private sealed record SerializedSyntaxTriviaInfo(string Kind, string Text, SerializedTextSpanInfo Span);

    /// <summary>
    /// Renderer-side view of one serialized token in the lexer debug payload.
    /// </summary>
    private sealed record SerializedLexerTokenInfo(
        int Index,
        string Kind,
        string Text,
        string DisplayText,
        string? MatchingKind,
        SerializedTextSpanInfo Span,
        IReadOnlyList<SerializedSyntaxTriviaInfo> LeadingTrivia,
        IReadOnlyList<SerializedSyntaxTriviaInfo> TrailingTrivia);

    /// <summary>
    /// Root DTO for serialized lexer output.
    /// </summary>
    private sealed record SerializedLexerInfo(string Kind, int TokenCount, IReadOnlyList<SerializedLexerTokenInfo> Tokens);

    /// <summary>
    /// Associates a child parser node with the property name it came from so tree rendering can
    /// expose structural intent rather than only raw child order.
    /// </summary>
    private sealed record SerializedParserChildInfo(string PropertyName, SerializedParserNodeInfo Node);

    /// <summary>
    /// Renderer-side view of one serialized parser node.
    /// </summary>
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

    /// <summary>
    /// Root DTO for serialized parser output.
    /// </summary>
    private sealed record SerializedParserInfo(string Kind, SerializedParserNodeInfo? Root);
}
