namespace Maho.Tests;

public sealed class AnalysisSessionTests
{
    [Fact]
    public void Compilation_FromSource_ValidCode_SucceedsWithoutErrors()
    {
        var code = """
            namespace Sample;

            public class Service
            {
                public struct int;
                public int Count;
            }
            """;

        var compilation = Compilation.FromSource(code, "Service.mh");

        Assert.False(compilation.HasErrors);
        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void Compilation_FromSource_InvalidSyntax_ReportsDiagnostics()
    {
        var code = """
            namespace Sample;

            public class Broken
            {
                §;
            }
            """;

        var compilation = Compilation.FromSource(code, "Broken.mh");

        Assert.True(compilation.HasErrors);
        Assert.NotEmpty(compilation.Diagnostics);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0100");
    }

    [Fact]
    public void AnalysisSession_IncrementalSnippets_RetainsSymbolsAcrossPrompts()
    {
        var session = new AnalysisSession();

        // Prompt 1: Declare a type
        var snippet1 = session.AnalyzeSnippet("public class Counter { public int Value; }");
        Assert.True(snippet1.Success);
        Assert.True(session.CommitSnippet(snippet1));
        Assert.Equal(1, session.SnippetCount);

        // Prompt 2: Declare a function referencing the type from prompt 1
        var snippet2 = session.AnalyzeSnippet("public function Increment(Counter c) { }");
        Assert.True(snippet2.Success);
        Assert.True(session.CommitSnippet(snippet2));
        Assert.Equal(2, session.SnippetCount);

        // Prompt 3: Execute a top-level statement referencing the previous declarations
        var snippet3 = session.AnalyzeSnippet("Counter c = new Counter();");
        Assert.True(snippet3.Success);
        Assert.True(session.CommitSnippet(snippet3));
        Assert.Equal(3, session.SnippetCount);
    }

    [Fact]
    public void AnalysisSession_DiagnosticLineAdjustment_HidesInjectedPragmaLine()
    {
        var session = new AnalysisSession();

        // Provide code with an invalid token on line 1
        var snippet = session.AnalyzeSnippet("§ invalid");

        Assert.False(snippet.Success);
        Assert.NotEmpty(snippet.Diagnostics);

        // The line number should be reported as 1 (not 2 from the injected pragma)
        var diag = snippet.Diagnostics[0];
        Assert.Equal(1, diag.Span.StartLocation.Line);
    }

    [Fact]
    public void CompilationOptions_WarningsAsErrors_TreatsWarningsAsErrors()
    {
        var options = new CompilationOptions
        {
            WarningsAsErrors = true
        };

        Assert.True(options.WarningsAsErrors);
        Assert.Equal(DiagnosticColorMode.Auto, options.ColorMode);
        Assert.Equal(DiagnosticPathStyle.Relative, options.PathStyle);
        Assert.Equal(DiagnosticFormat.Pretty, options.OutputFormat);
    }
}
