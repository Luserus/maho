using System.Text.Json;

namespace Maho.Tests;

public sealed class MahoCompilerTests
{
    [Fact]
    public void AnalyzeText_WithDebugOutputs_ReturnsStructuredPayloads()
    {
        CompilerAnalysisResult result = MahoCompiler.AnalyzeText("""
            namespace Basic;

            public class Result;

            public static Result Main()
            {
                return 0;
            }
            """, AnalysisOutput.Lexer | AnalysisOutput.Parser, "basic.mh");

        Assert.False(result.HasErrors);
        Assert.NotNull(result.LexerJson);
        Assert.NotNull(result.ParserJson);

        using JsonDocument lexerJson = JsonDocument.Parse(result.LexerJson!);
        using JsonDocument parserJson = JsonDocument.Parse(result.ParserJson!);

        Assert.Equal("lexer", lexerJson.RootElement.GetProperty("kind").GetString());
        Assert.Equal("parser", parserJson.RootElement.GetProperty("kind").GetString());
        Assert.Equal("basic.mh", result.SourcePath);
    }

    [Fact]
    public void AnalyzeText_InvalidInput_ReturnsStructuredDiagnostics()
    {
        CompilerAnalysisResult result = MahoCompiler.AnalyzeText("""
            public static int Main()
            {
                $;
                string text = "unterminated
                return 0;
            }
            """, AnalysisOutput.None, "invalid.mh");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MH0000");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MH0001");

        using JsonDocument diagnosticsJson = JsonDocument.Parse(result.DiagnosticsJson);
        Assert.True(diagnosticsJson.RootElement.GetArrayLength() >= 2);
    }

    [Fact]
    public void AnalyzeFiles_ReturnsPerFileBatchResults()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"maho-batch-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string validPath = Path.Combine(tempDirectory, "Valid.mh");
            string invalidPath = Path.Combine(tempDirectory, "Invalid.mh");

            File.WriteAllText(validPath, """
                public class Result;

                public static Result Main()
                {
                    return 0;
                }
                """);
            File.WriteAllText(invalidPath, """
                public static int Main(
                {
                    return ;
                }
                """);

            CompilerProjectAnalysisResult result = MahoCompiler.AnalyzeFiles(
                [validPath, invalidPath],
                AnalysisOutput.Parser,
                "batch-tests");

            Assert.Equal("batch-tests", result.ProjectName);
            Assert.Equal(2, result.Files.Length);
            Assert.Contains(result.Files, file => file.SourcePath == Path.GetFullPath(validPath) && file.Analysis is not null && !file.HasErrors);
            Assert.Contains(result.Files, file => file.SourcePath == Path.GetFullPath(invalidPath) && file.Analysis is not null && file.HasErrors);
            Assert.True(result.HasErrors);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
