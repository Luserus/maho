using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Maho.Analysis;

namespace Maho.Cli.Diagnostics;

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
        string? rootDirectory = null)
    {
        this.pathStyle = pathStyle;
        this.rootDirectory = rootDirectory ?? Directory.GetCurrentDirectory();
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
            DiagnosticSeverity.Error => "Error",
            DiagnosticSeverity.Warning => "Warning",
            _ => "Info"
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
        string? displayPath = diagnostic.SourcePath;
        if (displayPath is not null && pathStyle == DiagnosticPathStyle.Relative)
        {
            try
            {
                displayPath = Path.GetRelativePath(rootDirectory, displayPath);
            }
            catch
            {
                // Fallback to original path
            }
        }

        int line = diagnostic.Span.StartLocation.Line;
        int col = diagnostic.Span.StartLocation.Column;

        if (displayPath is not null)
        {
            sb.Append(Colorize(Blue, "  --> "));
            sb.AppendLine($"{displayPath}: ({line}:{col})");
        }

        // 3. Source snippet with gutter and carets
        string[]? lines = GetSourceLines(diagnostic.SourcePath);
        if (lines is not null && line >= 1 && line <= lines.Length)
        {
            string lineStr = line.ToString();
            int gutterWidth = Math.Max(lineStr.Length, 2);
            string emptyGutter = new string(' ', gutterWidth);

            // Empty gutter line: "   |"
            sb.Append(Colorize(Blue, $"{emptyGutter} |"));
            sb.AppendLine();

            // Source line: "14 | public struct Foo;"
            string sourceLine = lines[line - 1];
            string paddedLineStr = lineStr.PadLeft(gutterWidth);
            sb.Append(Colorize(Blue, $"{paddedLineStr} | "));
            sb.AppendLine(sourceLine);

            // Caret underline: "   |        ^^^^^^ message"
            int startCol = Math.Max(1, col);
            int length = Math.Max(1, diagnostic.Span.Length);

            // Avoid underline extending past end of line
            if (startCol - 1 + length > sourceLine.Length)
                length = Math.Max(1, sourceLine.Length - startCol + 1);

            string indent = new string(' ', Math.Max(0, startCol - 1));
            string carets = new string('^', length);

            sb.Append(Colorize(Blue, $"{emptyGutter} | "));
            sb.Append(indent);
            sb.Append(Colorize(severityColor, carets));

            // Primary label message if present
            var primaryLabel = diagnostic.Labels.Count > 0 ? diagnostic.Labels[0] : default;
            if (!string.IsNullOrEmpty(primaryLabel.Message))
            {
                sb.Append(' ');
                sb.Append(Colorize(severityColor, primaryLabel.Message));
            }
            sb.AppendLine();

            // Secondary labels on other lines
            for (int i = 1; i < diagnostic.Labels.Count; i++)
            {
                var label = diagnostic.Labels[i];
                int secondaryLine = label.Span.StartLocation.Line;
                if (secondaryLine >= 1 && secondaryLine <= lines.Length)
                {
                    string secLineStr = secondaryLine.ToString().PadLeft(gutterWidth);
                    sb.Append(Colorize(Blue, $"{secLineStr} | "));
                    sb.AppendLine(lines[secondaryLine - 1]);

                    int secCol = Math.Max(1, label.Span.StartLocation.Column);
                    int secLen = Math.Max(1, label.Span.Length);
                    string secIndent = new string(' ', Math.Max(0, secCol - 1));
                    string dashes = new string('-', secLen);

                    sb.Append(Colorize(Blue, $"{emptyGutter} | "));
                    sb.Append(secIndent);
                    sb.Append(Colorize(Cyan, dashes));
                    if (!string.IsNullOrEmpty(label.Message))
                    {
                        sb.Append(' ');
                        sb.Append(Colorize(Cyan, label.Message));
                    }
                    sb.AppendLine();
                }
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
            // Fallback when source lines aren't accessible
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
}
