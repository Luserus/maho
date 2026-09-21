using Maho.Analysis;
using Maho.Cli.Diagnostics;
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
            Code: "MH1002",
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

        Assert.Contains("Error [MH1002]: type 'Foo' is already declared in this scope", output);
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
            Code: "MH1002",
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
            Code: "MH0001",
            Message: "syntax error",
            Severity: DiagnosticSeverity.Error,
            Span: span,
            SourcePath: "color.mh");

        string output = renderer.Render(diagnostic);

        // Contains ANSI escape sequence \u001b[31;1m for bold red
        Assert.Contains("\u001b[31;1m", output);
        Assert.Contains("\u001b[0m", output);
    }
}
