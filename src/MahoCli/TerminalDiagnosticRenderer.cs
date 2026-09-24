using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Maho.Diagnostics;

namespace Maho;

/// <summary>
/// Renders rich, compiler diagnostics to the terminal with ANSI colors,
/// line gutters, primary carets (^^^^), secondary dashes (----), notes, help messages, and suggestions.
/// </summary>
public sealed class TerminalDiagnosticRenderer
{
    private readonly Dictionary<string, string[]> fileCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool useColors;
    private readonly DiagnosticPathStyle pathStyle;
    private readonly string rootDirectory;
    private readonly string? projectDirectory;

    // ANSI Escape Sequences
    private const string Reset = "\u001b[0m";
    private const string Bold = "\u001b[1m";
    private const string Red = "\u001b[31;1m";
    private const string Yellow = "\u001b[33;1m";
    private const string Cyan = "\u001b[36;1m";
    private const string Blue = "\u001b[34;1m";
    private const string Green = "\u001b[32;1m";

    public TerminalDiagnosticRenderer(
        DiagnosticColorMode colorMode = DiagnosticColorMode.Auto,
        DiagnosticPathStyle pathStyle = DiagnosticPathStyle.Relative,
        string? rootDirectory = null,
        string? projectDirectory = null)
    {
        this.pathStyle = pathStyle;
        this.rootDirectory = rootDirectory ?? Directory.GetCurrentDirectory();
        this.projectDirectory = projectDirectory;
        useColors = ShouldEnableColors(colorMode);
    }

    /// <summary>
    /// Registers in-memory source lines for a source path so virtual or memory files can be rendered.
    /// </summary>
    public void RegisterSource(string path, string content)
    {
        fileCache[path] = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
    }

    /// <summary>
    /// Formats a single diagnostic into a rich Rust-style terminal string.
    /// </summary>
    public string Render(DiagnosticInfo diagnostic)
    {
        var sb = new StringBuilder();

        // 1. Header: error[MH1002]: message
        string severityName = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            _ => "info"
        };

        string severityColor = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => Red,
            DiagnosticSeverity.Warning => Yellow,
            _ => Cyan
        };

        sb.Append(Colorize(severityColor, $"{severityName} [{diagnostic.Code}]"));
        sb.Append(Colorize(Bold, $": {diagnostic.Message}"));
        sb.AppendLine();

        // 2. Source file location: --> path:line:col
        string? displayPath = FormatDisplayPath(diagnostic.SourcePath);
        int line = diagnostic.Span.StartLocation.Line;
        int col = diagnostic.Span.StartLocation.Column;

        if (displayPath is not null)
        {
            sb.Append(Colorize(Blue, "  --> "));
            sb.AppendLine($"{displayPath}: ({line}:{col})");
        }

        // 3. Source snippet with gutter and carets
        string[]? lines = GetSourceLines(diagnostic.SourcePath);
        if (lines is not null)
        {
            var lineAnnotations = new SortedDictionary<int, List<LineAnnotation>>();

            void AddAnnotation(int lineNum, int colNum, int length, DiagnosticLabelStyle style, string? message)
            {
                if (lineNum < 1 || lineNum > lines.Length)
                    return;

                if (!lineAnnotations.TryGetValue(lineNum, out var list))
                {
                    list = [];
                    lineAnnotations[lineNum] = list;
                }

                if (list.Any(existing => existing.StartCol == colNum && existing.Length == length && existing.Style == style && existing.Message == message))
                    return;

                list.Add(new LineAnnotation(colNum, length, style, message));
            }

            if (diagnostic.Labels.Count > 0)
            {
                bool primaryCovered = false;
                foreach (var label in diagnostic.Labels)
                {
                    if (label.FilePath is not null && diagnostic.SourcePath is not null &&
                        !string.Equals(label.FilePath, diagnostic.SourcePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int lLine = label.Span.StartLocation.Line;
                    int lCol = label.Span.StartLocation.Column;
                    if (lLine == line && lCol == col)
                        primaryCovered = true;

                    AddAnnotation(lLine, lCol, label.Span.Length, label.Style, label.Message);
                }

                if (!primaryCovered && line >= 1 && line <= lines.Length)
                {
                    AddAnnotation(line, col, diagnostic.Span.Length, DiagnosticLabelStyle.Primary, null);
                }
            }
            else if (line >= 1 && line <= lines.Length)
            {
                AddAnnotation(line, col, diagnostic.Span.Length, DiagnosticLabelStyle.Primary, null);
            }

            if (lineAnnotations.Count > 0)
            {
                int maxLine = lineAnnotations.Keys.Max();
                int gutterWidth = Math.Max(maxLine.ToString().Length, 2);
                string emptyGutter = new string(' ', gutterWidth);

                // Empty gutter line: "   |"
                sb.Append(Colorize(Blue, $"{emptyGutter} |"));
                sb.AppendLine();

                int previousLine = -1;
                foreach (var (curLine, annotations) in lineAnnotations)
                {
                    if (previousLine != -1 && curLine > previousLine + 1)
                    {
                        sb.Append(Colorize(Blue, $"{emptyGutter} ..."));
                        sb.AppendLine();
                    }
                    previousLine = curLine;

                    string lineStr = curLine.ToString().PadLeft(gutterWidth);
                    string sourceLine = lines[curLine - 1];
                    sb.Append(Colorize(Blue, $"{lineStr} | "));
                    sb.AppendLine(sourceLine);

                    var activeAnnotations = annotations.Where(a => a.Style != DiagnosticLabelStyle.Context).ToList();
                    if (activeAnnotations.Count > 0)
                    {
                        activeAnnotations.Sort((a, b) => a.StartCol.CompareTo(b.StartCol));

                        // 1. Partition carets into non-overlapping layers
                        var caretLayers = new List<List<LineAnnotation>>();
                        foreach (var ann in activeAnnotations)
                        {
                            int startCol = Math.Max(1, ann.StartCol);
                            bool placed = false;
                            foreach (var layer in caretLayers)
                            {
                                var last = layer[^1];
                                int lastEndCol = Math.Max(1, last.StartCol) + Math.Max(1, last.Length);
                                if (startCol >= lastEndCol)
                                {
                                    layer.Add(ann);
                                    placed = true;
                                    break;
                                }
                            }
                            if (!placed)
                            {
                                caretLayers.Add([ann]);
                            }
                        }

                        foreach (var layer in caretLayers)
                        {
                            sb.Append(Colorize(Blue, $"{emptyGutter} | "));
                            int currentVisualCol = 1;

                            foreach (var ann in layer)
                            {
                                int startCol = Math.Max(1, ann.StartCol);
                                int length = Math.Max(1, ann.Length);
                                if (startCol - 1 + length > sourceLine.Length && sourceLine.Length >= startCol)
                                    length = Math.Max(1, sourceLine.Length - startCol + 1);

                                AppendWhitespace(sb, sourceLine, currentVisualCol, startCol);

                                char mark = ann.Style is DiagnosticLabelStyle.Secondary ? '-' : '^';
                                string markColor = ann.Style is DiagnosticLabelStyle.Secondary ? Cyan : severityColor;
                                string underline = new string(mark, length);
                                sb.Append(Colorize(markColor, underline));

                                currentVisualCol = startCol + length;
                            }
                            sb.AppendLine();
                        }

                        // 2. Render labels below the carets using L-shaped connectors (└──)
                        int GetConnectorCol(LineAnnotation ann)
                        {
                            int startCol = Math.Max(1, ann.StartCol);
                            int length = Math.Max(1, ann.Length);
                            if (startCol - 1 + length > sourceLine.Length && sourceLine.Length >= startCol)
                                length = Math.Max(1, sourceLine.Length - startCol + 1);
                            return startCol + (length - 1) / 2;
                        }

                        var labeled = activeAnnotations.Where(a => !string.IsNullOrEmpty(a.Message)).ToList();
                        labeled.Sort((a, b) => GetConnectorCol(a).CompareTo(GetConnectorCol(b)));

                        for (int k = labeled.Count - 1; k >= 0; k--)
                        {
                            var targetAnn = labeled[k];
                            int targetCol = GetConnectorCol(targetAnn);
                            string targetColor = targetAnn.Style is DiagnosticLabelStyle.Secondary ? Cyan : severityColor;

                            sb.Append(Colorize(Blue, $"{emptyGutter} | "));
                            int currentCol = 1;

                            // Preceding vertical connector lines for pending labels below this line
                            for (int j = 0; j < k; j++)
                            {
                                var branchAnn = labeled[j];
                                int branchCol = GetConnectorCol(branchAnn);
                                if (branchCol < currentCol)
                                    continue;

                                AppendWhitespace(sb, sourceLine, currentCol, branchCol);

                                string branchColor = branchAnn.Style is DiagnosticLabelStyle.Secondary ? Cyan : severityColor;
                                sb.Append(Colorize(branchColor, "|"));
                                currentCol = branchCol + 1;
                            }

                            if (targetCol >= currentCol)
                            {
                                AppendWhitespace(sb, sourceLine, currentCol, targetCol);
                            }

                            sb.Append(Colorize(targetColor, "└── "));
                            sb.AppendLine(Colorize(targetColor, targetAnn.Message ?? ""));
                        }
                    }
                }

                if (diagnostic.MacroTrace is not null)
                {
                    sb.Append(Colorize(Blue, $"{emptyGutter} |"));
                    sb.AppendLine();

                    var currentTrace = diagnostic.MacroTrace;
                    while (currentTrace is not null)
                    {
                        string? invPath = currentTrace.InvocationFilePath;
                        string? displayInvPath = FormatDisplayPath(invPath);
                        int invLine = currentTrace.InvocationSpan.StartLocation.Line;
                        int invCol = currentTrace.InvocationSpan.StartLocation.Column;

                        sb.Append(Colorize(Blue, "  ::: "));
                        sb.AppendLine($"{displayInvPath ?? "source"}: ({invLine}:{invCol})");

                        string[]? invLines = GetSourceLines(invPath);
                        if (invLines is not null && invLine >= 1 && invLine <= invLines.Length)
                        {
                            int invGutterWidth = Math.Max(invLine.ToString().Length, 2);
                            string invEmptyGutter = new string(' ', invGutterWidth);

                            sb.Append(Colorize(Blue, $"{invEmptyGutter} |"));
                            sb.AppendLine();

                            string invLineStr = invLine.ToString().PadLeft(invGutterWidth);
                            string invSourceLine = invLines[invLine - 1];
                            sb.Append(Colorize(Blue, $"{invLineStr} | "));
                            sb.AppendLine(invSourceLine);

                            sb.Append(Colorize(Blue, $"{invEmptyGutter} | "));
                            AppendWhitespace(sb, invSourceLine, 1, invCol);

                            int invLength = Math.Max(1, currentTrace.InvocationSpan.Length);
                            if (invCol - 1 + invLength > invSourceLine.Length && invSourceLine.Length >= invCol)
                                invLength = Math.Max(1, invSourceLine.Length - invCol + 1);

                            string underline = new string('-', invLength);
                            sb.Append(Colorize(Cyan, underline));
                            sb.AppendLine(Colorize(Cyan, " from this macro invocation"));

                            sb.Append(Colorize(Blue, $"{invEmptyGutter} |"));
                            sb.AppendLine();
                        }

                        currentTrace = currentTrace.Parent;
                    }

                    string macroName = diagnostic.MacroTrace.MacroName;
                    if (!macroName.StartsWith('$'))
                        macroName = $"${macroName}";

                    string? defLocation = null;
                    if (diagnostic.MacroTrace.DefinitionSpan.HasValue)
                    {
                        string? defPath = FormatDisplayPath(diagnostic.MacroTrace.DefinitionFilePath);
                        int defLine = diagnostic.MacroTrace.DefinitionSpan.Value.StartLocation.Line;
                        int defCol = diagnostic.MacroTrace.DefinitionSpan.Value.StartLocation.Column;
                        defLocation = defPath is not null ? $" at {defPath}: ({defLine}:{defCol})" : $" at line {defLine}";
                    }

                    sb.Append(Colorize(Blue, $"{emptyGutter} = "));
                    sb.Append(Colorize(Bold, "note: "));
                    sb.AppendLine($"in macro definition '{macroName}'{defLocation}");
                }

                bool hasAdditionalInfo = diagnostic.MacroTrace is null && (diagnostic.Notes.Count > 0 || diagnostic.HelpMessages.Count > 0 || diagnostic.Suggestions.Count > 0);
                if (hasAdditionalInfo)
                {
                    sb.Append(Colorize(Blue, $"{emptyGutter} |"));
                    sb.AppendLine();
                }

                // 4. Notes
                foreach (var note in diagnostic.Notes)
                {
                    sb.Append(Colorize(Blue, $"{emptyGutter} = "));
                    sb.Append(Colorize(Bold, "note: "));
                    sb.AppendLine(note.Message);
                }

                // 5. Help messages
                foreach (var help in diagnostic.HelpMessages)
                {
                    sb.Append(Colorize(Blue, $"{emptyGutter} = "));
                    sb.Append(Colorize(Cyan, "help: "));
                    sb.AppendLine(help.Message);
                }

                // 6. Suggestions
                foreach (var suggestion in diagnostic.Suggestions)
                {
                    sb.Append(Colorize(Blue, $"{emptyGutter} = "));
                    sb.Append(Colorize(Green, "suggestion: "));
                    sb.AppendLine(suggestion.Description);

                    foreach (var edit in suggestion.Edits)
                    {
                        if (edit.Span.StartLocation.Line >= 1 && edit.Span.StartLocation.Line <= lines.Length)
                        {
                            string original = lines[edit.Span.StartLocation.Line - 1];
                            sb.Append(Colorize(Blue, $"{emptyGutter}   - "));
                            sb.AppendLine(Colorize(Red, original));
                            sb.Append(Colorize(Blue, $"{emptyGutter}   + "));
                            sb.AppendLine(Colorize(Green, edit.NewText));
                        }
                    }
                }

                // Final empty gutter line
                sb.Append(Colorize(Blue, $"{emptyGutter} |"));
                sb.AppendLine();
            }
            else
            {
                AppendFallback(sb, diagnostic);
            }
        }
        else
        {
            AppendFallback(sb, diagnostic);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders a list of diagnostics.
    /// </summary>
    public string RenderAll(IEnumerable<DiagnosticInfo> diagnostics)
    {
        var sb = new StringBuilder();
        foreach (var diag in diagnostics)
            sb.AppendLine(Render(diag));
        return sb.ToString();
    }

    private string[]? GetSourceLines(string? path)
    {
        if (path is null)
            return null;

        if (fileCache.TryGetValue(path, out var cached))
            return cached;

        if (File.Exists(path))
        {
            try
            {
                var lines = File.ReadAllLines(path);
                fileCache[path] = lines;
                return lines;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private string Colorize(string ansiColor, string text)
    {
        if (!useColors)
            return text;

        return $"{ansiColor}{text}{Reset}";
    }

    private static bool ShouldEnableColors(DiagnosticColorMode mode) => mode switch
    {
        DiagnosticColorMode.Always => true,
        DiagnosticColorMode.Never => false,
        DiagnosticColorMode.Auto => !Console.IsOutputRedirected &&
                                    Environment.GetEnvironmentVariable("NO_COLOR") == null &&
                                    Environment.GetEnvironmentVariable("TERM") != "dumb",
        _ => false
    };

    private string? FormatDisplayPath(string? rawPath)
    {
        if (rawPath is null)
            return null;

        if (pathStyle == DiagnosticPathStyle.Relative)
        {
            try
            {
                return Path.GetRelativePath(rootDirectory, rawPath);
            }
            catch
            {
                return rawPath;
            }
        }
        else if (pathStyle == DiagnosticPathStyle.ProjectRelative)
        {
            try
            {
                string targetRoot = projectDirectory ?? rootDirectory;
                return Path.GetRelativePath(targetRoot, rawPath);
            }
            catch
            {
                return rawPath;
            }
        }
        else if (pathStyle == DiagnosticPathStyle.Full)
        {
            try
            {
                return Path.GetFullPath(rawPath);
            }
            catch
            {
                return rawPath;
            }
        }

        return rawPath;
    }

    private void AppendFallback(StringBuilder sb, DiagnosticInfo diagnostic)
    {
        if (diagnostic.MacroTrace is not null)
        {
            string macroName = diagnostic.MacroTrace.MacroName;
            if (!macroName.StartsWith('$'))
                macroName = $"${macroName}";

            string? defLocation = null;
            if (diagnostic.MacroTrace.DefinitionSpan.HasValue)
            {
                string? defPath = FormatDisplayPath(diagnostic.MacroTrace.DefinitionFilePath);
                int defLine = diagnostic.MacroTrace.DefinitionSpan.Value.StartLocation.Line;
                int defCol = diagnostic.MacroTrace.DefinitionSpan.Value.StartLocation.Column;
                defLocation = defPath is not null ? $" at {defPath}: ({defLine}:{defCol})" : $" at line {defLine}";
            }

            sb.Append(Colorize(Bold, "  = note: "));
            sb.AppendLine($"in macro definition '{macroName}'{defLocation}");
        }
        foreach (var note in diagnostic.Notes)
        {
            sb.Append(Colorize(Bold, "  = note: "));
            sb.AppendLine(note.Message);
        }
        foreach (var help in diagnostic.HelpMessages)
        {
            sb.Append(Colorize(Cyan, "  = help: "));
            sb.AppendLine(help.Message);
        }
    }

    private static void AppendWhitespace(StringBuilder sb, string sourceLine, int fromCol, int toCol)
    {
        for (int i = fromCol - 1; i < toCol - 1; i++)
        {
            if (i >= 0 && i < sourceLine.Length && sourceLine[i] == '\t')
                sb.Append('\t');
            else
                sb.Append(' ');
        }
    }

    private readonly record struct LineAnnotation(
        int StartCol,
        int Length,
        DiagnosticLabelStyle Style,
        string? Message
    );
}
