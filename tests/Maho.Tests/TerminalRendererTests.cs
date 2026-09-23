using Maho.Diagnostics;

namespace Maho.Tests;

public sealed class TerminalRendererTests
{
    [Fact]
    public void TerminalRenderer_RendersRustStyleDiagnosticWithGutterAndCarets()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        renderer.RegisterSource("test.mh", "public class Foo;\npublic class Foo;");

        var spanInfo = new TextSpanInfo(
            Start: 13,
            Length: 3,
            End: 16,
            StartLocation: new TextLocation(2, 14),
            EndLocation: new TextLocation(2, 17));

        var diagnostic = new DiagnosticInfo(
            Code: "MH0530",
            Message: "type 'Foo' is already declared in this scope",
            Severity: DiagnosticSeverity.Error,
            Span: spanInfo,
            SourcePath: "test.mh",
            Labels: [
                new DiagnosticLabelInfo(spanInfo, "duplicate declaration", DiagnosticLabelStyle.Primary, "test.mh")
            ],
            Notes: [
                new DiagnosticNoteInfo("first declared on line 1", "test.mh")
            ],
            HelpMessages: [
                new DiagnosticHelpInfo("consider renaming one of the types", "test.mh")
            ]);

        string output = renderer.Render(diagnostic);

        Assert.Contains("Error [MH0530]: type 'Foo' is already declared in this scope", output);
        Assert.Contains("--> test.mh: (2:14)", output);
        Assert.Contains("2 | public class Foo;", output);
        Assert.Contains("^^^ duplicate declaration", output);
        Assert.Contains("= note: first declared on line 1", output);
        Assert.Contains("= help: consider renaming one of the types", output);
    }

    [Fact]
    public void TerminalRenderer_WithSecondaryLabels_RendersDashes()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        renderer.RegisterSource("types.mh", "public class Bar;\npublic class Bar;");

        var primarySpan = new TextSpanInfo(13, 3, 16, new TextLocation(2, 14), new TextLocation(2, 17));
        var secondarySpan = new TextSpanInfo(13, 3, 16, new TextLocation(1, 14), new TextLocation(1, 17));

        var diagnostic = new DiagnosticInfo(
            Code: "MH0530",
            Message: "type 'Bar' is already declared in this scope",
            Severity: DiagnosticSeverity.Error,
            Span: primarySpan,
            SourcePath: "types.mh",
            Labels: [
                new DiagnosticLabelInfo(primarySpan, "redeclared here", DiagnosticLabelStyle.Primary, "types.mh"),
                new DiagnosticLabelInfo(secondarySpan, "previously declared here", DiagnosticLabelStyle.Secondary, "types.mh")
            ]);

        string output = renderer.Render(diagnostic);

        Assert.Contains("^^^ redeclared here", output);
        Assert.Contains("1 | public class Bar;", output);
        Assert.Contains("--- previously declared here", output);
    }

    [Fact]
    public void TerminalRenderer_WithSuggestions_RendersDiffLines()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        renderer.RegisterSource("fix.mh", "var oldName = 10;");

        var span = new TextSpanInfo(4, 7, 11, new TextLocation(1, 5), new TextLocation(1, 12));

        var diagnostic = new DiagnosticInfo(
            Code: "MH2001",
            Message: "naming convention violation",
            Severity: DiagnosticSeverity.Warning,
            Span: span,
            SourcePath: "fix.mh",
            Suggestions: [
                new DiagnosticSuggestionInfo(
                    "rename to PascalCase",
                    [new TextEditInfo(span, "var NewName = 10;", "fix.mh")],
                    SuggestionApplicability.MachineApplicable)
            ]);

        string output = renderer.Render(diagnostic);

        Assert.Contains("Warning [MH2001]: naming convention violation", output);
        Assert.Contains("= suggestion: rename to PascalCase", output);
        Assert.Contains("- var oldName = 10;", output);
        Assert.Contains("+ var NewName = 10;", output);
    }

    [Fact]
    public void TerminalRenderer_ColorModeAlways_EmitsAnsiEscapeCodes()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Always, DiagnosticPathStyle.Relative);
        renderer.RegisterSource("color.mh", "int x = 1;");

        var span = new TextSpanInfo(0, 3, 3, new TextLocation(1, 1), new TextLocation(1, 4));
        var diagnostic = new DiagnosticInfo(
            Code: "MH0101",
            Message: "syntax error",
            Severity: DiagnosticSeverity.Error,
            Span: span,
            SourcePath: "color.mh");

        string output = renderer.Render(diagnostic);

        // Contains ANSI escape sequence \u001b[31;1m for bold red
        Assert.Contains("\u001b[31;1m", output);
        Assert.Contains("\u001b[0m", output);
    }

    [Fact]
    public void TerminalRenderer_PreservesTabsInCaretIndentation()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        renderer.RegisterSource("tabs.mh", "\tint x = 1;");

        // "int" starts at column 2 (after 1 tab)
        var span = new TextSpanInfo(1, 3, 4, new TextLocation(1, 2), new TextLocation(1, 5));
        var diagnostic = new DiagnosticInfo(
            Code: "MH0101",
            Message: "test message",
            Severity: DiagnosticSeverity.Error,
            Span: span,
            SourcePath: "tabs.mh");

        string output = renderer.Render(diagnostic);

        // The gutter line under "int" must have a tab in the indent to match the source line tab
        Assert.Contains("1 | \tint x = 1;", output);
        Assert.Contains("  | \t^^^", output);
    }

    [Fact]
    public void TerminalRenderer_ClampsOverlongSecondaryLabels()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        renderer.RegisterSource("clamp.mh", "short line;\nshort line;");

        var primarySpan = new TextSpanInfo(0, 5, 5, new TextLocation(1, 1), new TextLocation(1, 6));
        // Secondary label with length 100 on an 11-char line
        var overlongSpan = new TextSpanInfo(0, 100, 100, new TextLocation(2, 1), new TextLocation(2, 101));

        var diagnostic = new DiagnosticInfo(
            Code: "MH0530",
            Message: "error",
            Severity: DiagnosticSeverity.Error,
            Span: primarySpan,
            SourcePath: "clamp.mh",
            Labels: [
                new DiagnosticLabelInfo(primarySpan, "primary", DiagnosticLabelStyle.Primary, "clamp.mh"),
                new DiagnosticLabelInfo(overlongSpan, "secondary", DiagnosticLabelStyle.Secondary, "clamp.mh")
            ]);

        string output = renderer.Render(diagnostic);

        // Dashes must not exceed the line length (11 characters)
        Assert.Contains(new string('-', 11), output);
        Assert.DoesNotContain(new string('-', 12), output);
    }

    [Fact]
    public void TerminalRenderer_MultiLineExpectedToken_RendersBothLinesAndCarets()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        renderer.RegisterSource("sample.mh", "some err\nnice = }");

        // Line 1 col 9 (after "some err", missing semicolon)
        var span1 = new TextSpanInfo(8, 0, 8, new TextLocation(1, 9), new TextLocation(1, 9));
        // Line 2 col 1..4 ("nice")
        var span2 = new TextSpanInfo(9, 4, 13, new TextLocation(2, 1), new TextLocation(2, 5));

        var diagnostic = new DiagnosticInfo(
            Code: "MH0120",
            Message: "Expected ';' after the top-level variable declaration, found 'nice'.",
            Severity: DiagnosticSeverity.Error,
            Span: span2,
            SourcePath: "sample.mh",
            Labels: [
                new DiagnosticLabelInfo(span1, null, DiagnosticLabelStyle.Context, "sample.mh"),
                new DiagnosticLabelInfo(span2, null, DiagnosticLabelStyle.Primary, "sample.mh")
            ]);

        string output = renderer.Render(diagnostic);

        Assert.Contains("1 | some err", output);
        Assert.DoesNotContain("  |         ^", output);
        Assert.Contains("2 | nice = }", output);
        Assert.Contains("  | ^^^^", output);
    }

    [Fact]
    public void TerminalRenderer_WithInterveningLines_TruncatesWithEllipsis()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        var sourceLines = new string[20];
        sourceLines[0] = "some err";
        for (int i = 1; i < 19; i++)
            sourceLines[i] = "";
        sourceLines[19] = "nice = }";
        renderer.RegisterSource("gap.mh", string.Join("\n", sourceLines));

        var span1 = new TextSpanInfo(8, 0, 8, new TextLocation(1, 9), new TextLocation(1, 9));
        var span2 = new TextSpanInfo(100, 4, 104, new TextLocation(20, 1), new TextLocation(20, 5));

        var diagnostic = new DiagnosticInfo(
            Code: "MH0120",
            Message: "Expected ';' after the top-level variable declaration, found 'nice'.",
            Severity: DiagnosticSeverity.Error,
            Span: span2,
            SourcePath: "gap.mh",
            Labels: [
                new DiagnosticLabelInfo(span1, null, DiagnosticLabelStyle.Context, "gap.mh"),
                new DiagnosticLabelInfo(span2, null, DiagnosticLabelStyle.Primary, "gap.mh")
            ]);

        string output = renderer.Render(diagnostic);

        Assert.Contains(" 1 | some err", output);
        Assert.DoesNotContain("   |         ^", output);
        Assert.Contains("   ...", output);
        Assert.Contains("20 | nice = }", output);
        Assert.Contains("   | ^^^^", output);
    }
}

