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

        Assert.Contains("error [MH0530]: type 'Foo' is already declared in this scope", output);
        Assert.Contains("--> test.mh: (2:14)", output);
        Assert.Contains("2 | public class Foo;", output);
        Assert.Contains("^^^", output);
        Assert.Contains("└── duplicate declaration", output);
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

        Assert.Contains("^^^", output);
        Assert.Contains("└── redeclared here", output);
        Assert.Contains("1 | public class Bar;", output);
        Assert.Contains("---", output);
        Assert.Contains("└── previously declared here", output);
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

        Assert.Contains("warning [MH2001]: naming convention violation", output);
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

    [Fact]
    public void TerminalRenderer_WithHelpOrNotes_RendersEmptyGutterLineAfterDiagnosticCarets()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        renderer.RegisterSource("sample.mh", "SomeOtherType val;");

        var span = new TextSpanInfo(0, 13, 13, new TextLocation(1, 1), new TextLocation(1, 14));
        var diagnostic = new DiagnosticInfo(
            Code: "MH0500",
            Message: "Could not resolve type 'SomeOtherType'.",
            Severity: DiagnosticSeverity.Error,
            Span: span,
            SourcePath: "sample.mh",
            Labels: [
                new DiagnosticLabelInfo(span, "type 'SomeOtherType' not found", DiagnosticLabelStyle.Primary, "sample.mh")
            ],
            HelpMessages: [
                new DiagnosticHelpInfo("Check for a missing import or declaration.", "sample.mh")
            ]);

        string output = renderer.Render(diagnostic);

        Assert.Contains(
            "   | ^^^^^^^^^^^^^\n   |       └── type 'SomeOtherType' not found\n   |\n   = help: Check for a missing import or declaration.",
            output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void TerminalRenderer_MultipleLabelsOnSameLine_RendersHierarchicalTreeLevels()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        renderer.RegisterSource("expr.mh", "1 + \"hello\"");

        var lhsSpan = new TextSpanInfo(0, 1, 1, new TextLocation(1, 1), new TextLocation(1, 2));
        var opSpan = new TextSpanInfo(2, 1, 3, new TextLocation(1, 3), new TextLocation(1, 4));
        var rhsSpan = new TextSpanInfo(4, 7, 11, new TextLocation(1, 5), new TextLocation(1, 12));

        var diagnostic = new DiagnosticInfo(
            Code: "MH2001",
            Message: "cannot apply binary operator '+' to types 'Int32' and 'String8'",
            Severity: DiagnosticSeverity.Error,
            Span: opSpan,
            SourcePath: "expr.mh",
            Labels: [
                new DiagnosticLabelInfo(opSpan, "cannot apply '+'", DiagnosticLabelStyle.Primary, "expr.mh"),
                new DiagnosticLabelInfo(lhsSpan, "type is 'Int32'", DiagnosticLabelStyle.Secondary, "expr.mh"),
                new DiagnosticLabelInfo(rhsSpan, "type is 'String8'", DiagnosticLabelStyle.Secondary, "expr.mh")
            ]);

        string output = renderer.Render(diagnostic).Replace("\r\n", "\n");

        Assert.Contains("1 | 1 + \"hello\"", output);
        Assert.Contains("  | - ^ -------", output);
        Assert.Contains("  | | |    └── type is 'String8'", output);
        Assert.Contains("  | | └── cannot apply '+'", output);
        Assert.Contains("  | └── type is 'Int32'", output);
    }

    [Fact]
    public void TerminalRenderer_WithMacroTrace_RendersInvocationSnippetAndDefinitionNote()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        renderer.RegisterSource("StdLib.mh", "macro $DefineInt\n{\n    (@name: ident) => {\n        public struct @name {\n            public Result val;\n        }\n    }\n}\n\n$DefineInt(Foo);");

        var errSpan = new TextSpanInfo(69, 6, 75, new TextLocation(5, 20), new TextLocation(5, 26));
        var invSpan = new TextSpanInfo(100, 16, 116, new TextLocation(10, 1), new TextLocation(10, 17));
        var defSpan = new TextSpanInfo(0, 16, 16, new TextLocation(1, 1), new TextLocation(1, 17));

        var macroTrace = new MacroExpansionTraceInfo(
            "$DefineInt",
            invSpan,
            "StdLib.mh",
            defSpan,
            "StdLib.mh");

        var diagnostic = new DiagnosticInfo(
            Code: "MH0500",
            Message: "could not resolve type 'Result'",
            Severity: DiagnosticSeverity.Error,
            Span: errSpan,
            SourcePath: "StdLib.mh",
            Labels: [
                new DiagnosticLabelInfo(errSpan, "type 'Result' not found", DiagnosticLabelStyle.Primary, "StdLib.mh")
            ],
            HelpMessages: [
                new DiagnosticHelpInfo("check for a missing import or declaration", "StdLib.mh")
            ],
            MacroTrace: macroTrace);

        string output = renderer.Render(diagnostic).Replace("\r\n", "\n");

        Assert.Contains("error [MH0500]: could not resolve type 'Result'", output);
        Assert.Contains("--> StdLib.mh: (5:20)", output);
        Assert.Contains("5 |             public Result val;", output);
        Assert.Contains("  |                    ^^^^^^", output);
        Assert.Contains("└── type 'Result' not found", output);
        Assert.Contains("::: StdLib.mh: (10:1)", output);
        Assert.Contains("10 | $DefineInt(Foo);", output);
        Assert.Contains("----------------", output);
        Assert.Contains("└── from this macro invocation", output);
        Assert.Contains("= note: in macro definition '$DefineInt' at StdLib.mh: (1:1)", output);
        Assert.Contains("= help: check for a missing import or declaration", output);
    }

    [Fact]
    public void TerminalRenderer_RenderStatus_BuildSuccess_WithoutColors()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        string status = renderer.RenderStatus(0, 0, TimeSpan.FromMilliseconds(42)).Replace("\r\n", "\n");
        Assert.Equal("Build succeeded in 42ms\n", status);
    }

    [Fact]
    public void TerminalRenderer_RenderStatus_BuildSuccess_WithColors()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Always, DiagnosticPathStyle.Relative);
        string status = renderer.RenderStatus(0, 0, TimeSpan.FromMilliseconds(42));
        Assert.Contains("\u001b[32;1msucceeded\u001b[0m", status);
        Assert.Contains("42ms", status);
    }

    [Fact]
    public void TerminalRenderer_RenderStatus_ErrorsAndWarnings_WithoutColors()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        string status = renderer.RenderStatus(2, 1).Replace("\r\n", "\n");
        Assert.Equal("Build failed with 2 error(s) and 1 warning(s)\n", status);
    }

    [Fact]
    public void TerminalRenderer_RenderStatus_ErrorsAndWarnings_WithColors()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Always, DiagnosticPathStyle.Relative);
        string status = renderer.RenderStatus(2, 1);
        Assert.Contains("\u001b[31;1m2 error(s)\u001b[0m", status);
        Assert.Contains(" and ", status);
        Assert.Contains("\u001b[33;1m1 warning(s)\u001b[0m", status);
    }

    [Fact]
    public void TerminalRenderer_RenderStatus_ErrorsOnly()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        string status = renderer.RenderStatus(1, 0).Replace("\r\n", "\n");
        Assert.Equal("Build failed with 1 error(s)\n", status);
    }

    [Fact]
    public void TerminalRenderer_RenderStatus_WarningsOnly()
    {
        var renderer = new TerminalDiagnosticRenderer(DiagnosticColorMode.Never, DiagnosticPathStyle.Relative);
        string status = renderer.RenderStatus(0, 3, TimeSpan.FromMilliseconds(15)).Replace("\r\n", "\n");
        Assert.Equal("Build succeeded with 3 warning(s) in 15ms\n", status);
    }

    [Fact]
    public void TerminalRenderer_FormatTime_FormatsCorrectUnits()
    {
        Assert.Equal("0ms", TerminalDiagnosticRenderer.FormatTime(TimeSpan.Zero));
        Assert.Equal("1.23s", TerminalDiagnosticRenderer.FormatTime(TimeSpan.FromSeconds(1.234)));
        Assert.Equal("45.67ms", TerminalDiagnosticRenderer.FormatTime(TimeSpan.FromMilliseconds(45.67)));
        Assert.Equal("500ns", TerminalDiagnosticRenderer.FormatTime(TimeSpan.FromTicks(5)));
    }
}

