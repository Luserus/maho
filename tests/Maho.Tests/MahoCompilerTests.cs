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
                public static dyn Broken()
                {
                    $;
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

    [Fact]
    public void AnalyzeProjectFile_AllowsOptedInTopLevelStatementsWhenEntryFileIsExplicit()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"maho-project-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string projectPath = Path.Combine(tempDirectory, "Sample.mhpr");
            string programPath = Path.Combine(tempDirectory, "Program.mh");

            File.WriteAllText(projectPath, """
                EntryFile : "Program.mh";
                GlobalUnsafeEnabled : false;
                ProjectsReferenced : [];
                GlobalAliases : {
                    "int32" : "Std.Int32",
                    "float32" : "Std.Float32"
                };
                """);
            File.WriteAllText(programPath, """
                #pragma toplevel enable
                call();
                """);

            CompilerProjectAnalysisResult result = MahoCompiler.AnalyzeProjectFile(projectPath);

            CompilerBatchFileResult program = Assert.Single(result.Files);
            Assert.False(program.HasErrors);
            Assert.Equal(Path.GetFullPath(programPath), result.EntryFile);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ProjectFileParser_RequiresSemicolonsBetweenProperties()
    {
        MahoProjectParseException exception = Assert.Throws<MahoProjectParseException>(() =>
            MahoProjectFileParser.Parse("""
                EntryFile : "Program.mh"
                GlobalUnsafeEnabled : false;
                """));

        Assert.Contains("Expected ';'", exception.Message);
    }

    [Fact]
    public void AnalyzeProjectFile_ReportsAmbiguousImplicitTopLevelEntryPoint()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"maho-project-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string projectPath = Path.Combine(tempDirectory, "Sample.mhpr");
            string firstPath = Path.Combine(tempDirectory, "First.mh");
            string secondPath = Path.Combine(tempDirectory, "Second.mh");

            File.WriteAllText(projectPath, string.Empty);
            File.WriteAllText(firstPath, "#pragma toplevel enable\nfirst();");
            File.WriteAllText(secondPath, "#pragma toplevel enable\nsecond();");

            CompilerProjectAnalysisResult result = MahoCompiler.AnalyzeProjectFile(projectPath);

            Assert.True(result.HasErrors);
            Assert.All(result.Files, file => Assert.Contains(file.Analysis!.Diagnostics, diagnostic => diagnostic.Code == "MH0012"));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void AnalyzeProjectFile_RejectsMultipleTopLevelFilesEvenWhenEntryFileIsExplicit()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"maho-project-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string projectPath = Path.Combine(tempDirectory, "Sample.mhpr");
            string firstPath = Path.Combine(tempDirectory, "First.mh");
            string secondPath = Path.Combine(tempDirectory, "Second.mh");

            File.WriteAllText(projectPath, "EntryFile : \"First.mh\";");
            File.WriteAllText(firstPath, "#pragma toplevel enable\nfirst();");
            File.WriteAllText(secondPath, "#pragma toplevel enable\nsecond();");

            CompilerProjectAnalysisResult result = MahoCompiler.AnalyzeProjectFile(projectPath);

            Assert.True(result.HasErrors);
            Assert.Equal(Path.GetFullPath(firstPath), result.EntryFile);
            Assert.All(result.Files, file => Assert.Contains(file.Analysis!.Diagnostics, diagnostic => diagnostic.Code == "MH0012"));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void AnalyzeProjectFile_SelectsItsOnlyTopLevelEntryCandidate()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"maho-project-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string projectPath = Path.Combine(tempDirectory, "Sample.mhpr");
            string programPath = Path.Combine(tempDirectory, "Program.mh");

            File.WriteAllText(projectPath, string.Empty);
            File.WriteAllText(programPath, "#pragma toplevel enable\nrun();");

            CompilerProjectAnalysisResult result = MahoCompiler.AnalyzeProjectFile(projectPath);

            Assert.False(result.HasErrors);
            Assert.Equal(Path.GetFullPath(programPath), result.EntryFile);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void AnalyzeProjectFile_ParsesCheckedInSampleProject()
    {
        string projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Samples/Test.mhpr"));

        CompilerProjectAnalysisResult result = MahoCompiler.AnalyzeProjectFile(projectPath);

        Assert.False(result.HasErrors);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(projectPath)!, "Program.mh"), result.EntryFile);
        Assert.Equal(2, result.Files.Length);
    }

    [Fact]
    public void CompileProjectFile_ReachesTheLoweringPlaceholderAfterSuccessfulFrontEnd()
    {
        string projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Samples/Test.mhpr"));

        CompilerPipelineNotImplementedException exception = Assert.Throws<CompilerPipelineNotImplementedException>(() =>
            MahoCompiler.CompileProjectFile(projectPath));

        Assert.False(exception.Analysis.HasErrors);
        Assert.Equal("The lowering and code-generation pipeline has not been implemented.", exception.Message);
    }
}
