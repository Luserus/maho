using System.Collections.Generic;
using System.Text.Json;
using Miryo.Build;

namespace Maho.Tests;

public sealed class MahoCompilerTests
{
    private static CompilerProjectAnalysisResult AnalyzeProject(string projectPath)
    {
        var project = MiryoBuildSystem.LoadProject(projectPath);
        var options = new CompilationOptions
        {
            EntryFile = project.EntryFile,
            ImplicitTopLevel = project.ImplicitTopLevel,
            RootDirectory = project.ProjectDirectory,
            ReferencedProjects = [.. project.ProjectsReferenced],
            GlobalAliases = new Dictionary<string, string>((Dictionary<string, string>)project.GlobalAliases),
            ProjectFilePath = project.ProjectFilePath
        };
        return MahoCompiler.AnalyzeFiles(project.SourceFiles, AnalysisOutput.None, project.ProjectDirectory, options);
    }

    [Fact]
    public void AnalyzeText_WithDebugOutputs_ReturnsStructuredPayloads()
    {
        DebugCompilationOutput result = MahoCompiler.AnalyzeText("""
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
        DebugCompilationOutput result = MahoCompiler.AnalyzeText("""
            public static int Main()
            {
                §;
                string text = "unterminated
                return 0;
            }
            """, AnalysisOutput.None, "invalid.mh");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MH0100");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MH0101");

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

            CompilerProjectAnalysisResult result = AnalyzeProject(projectPath);

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

            CompilerProjectAnalysisResult result = AnalyzeProject(projectPath);

            Assert.True(result.HasErrors);
            Assert.All(result.Files, file => Assert.Contains(file.Analysis!.Diagnostics, diagnostic => diagnostic.Code == "MH0161"));
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

            CompilerProjectAnalysisResult result = AnalyzeProject(projectPath);

            Assert.True(result.HasErrors);
            Assert.Equal(Path.GetFullPath(firstPath), result.EntryFile);
            Assert.All(result.Files, file => Assert.Contains(file.Analysis!.Diagnostics, diagnostic => diagnostic.Code == "MH0161"));
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

            CompilerProjectAnalysisResult result = AnalyzeProject(projectPath);

            Assert.False(result.HasErrors);
            Assert.Equal(Path.GetFullPath(programPath), result.EntryFile);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void AnalyzeProjectFile_ImplicitTopLevel_AllowsTopLevelWithoutPragmaWhenEntryFileIsExplicit()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"maho-project-implicit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string projectPath = Path.Combine(tempDirectory, "Sample.mhpr");
            string programPath = Path.Combine(tempDirectory, "Program.mh");

            File.WriteAllText(projectPath, """
                EntryFile : "Program.mh";
                ImplicitTopLevel : true;
                """);
            File.WriteAllText(programPath, "call();");

            CompilerProjectAnalysisResult result = AnalyzeProject(projectPath);

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
    public void AnalyzeProjectFile_ImplicitTopLevel_SelectsSingleFileUsingTopLevelWhenNoEntryFile()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"maho-project-implicit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string projectPath = Path.Combine(tempDirectory, "Sample.mhpr");
            string firstPath = Path.Combine(tempDirectory, "Types.mh");
            string secondPath = Path.Combine(tempDirectory, "Entry.mh");

            File.WriteAllText(projectPath, "ImplicitTopLevel : true;");
            File.WriteAllText(firstPath, "public class Point { public struct int; public int X; }");
            File.WriteAllText(secondPath, "run();");

            CompilerProjectAnalysisResult result = AnalyzeProject(projectPath);

            Assert.False(result.HasErrors);
            Assert.Equal(Path.GetFullPath(secondPath), result.EntryFile);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void AnalyzeProjectFile_ImplicitTopLevel_RejectsMultipleFilesUsingTopLevel()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"maho-project-implicit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string projectPath = Path.Combine(tempDirectory, "Sample.mhpr");
            string firstPath = Path.Combine(tempDirectory, "First.mh");
            string secondPath = Path.Combine(tempDirectory, "Second.mh");

            File.WriteAllText(projectPath, "ImplicitTopLevel : true;");
            File.WriteAllText(firstPath, "first();");
            File.WriteAllText(secondPath, "second();");

            CompilerProjectAnalysisResult result = AnalyzeProject(projectPath);

            Assert.True(result.HasErrors);
            Assert.All(result.Files, file => Assert.Contains(file.Analysis!.Diagnostics, diagnostic => diagnostic.Code == "MH0161"));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void AnalyzeProjectFile_ImplicitTopLevel_PragmaDisableOverridesImplicitTopLevel()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"maho-project-implicit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string projectPath = Path.Combine(tempDirectory, "Sample.mhpr");
            string programPath = Path.Combine(tempDirectory, "Program.mh");

            File.WriteAllText(projectPath, """
                EntryFile : "Program.mh";
                ImplicitTopLevel : true;
                """);
            File.WriteAllText(programPath, """
                #pragma toplevel disable
                call();
                """);

            CompilerProjectAnalysisResult result = AnalyzeProject(projectPath);

            CompilerBatchFileResult program = Assert.Single(result.Files);
            Assert.True(program.HasErrors);
            Assert.Contains(program.Analysis!.Diagnostics, diagnostic => diagnostic.Code == "MH0160");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void AnalyzeProjectFile_GlobalAliases_ResolvesAliasesAcrossFiles()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"maho-project-aliases-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string projectPath = Path.Combine(tempDirectory, "Sample.mhpr");
            string typesPath = Path.Combine(tempDirectory, "Types.mh");
            string programPath = Path.Combine(tempDirectory, "Program.mh");

            File.WriteAllText(projectPath, """
                GlobalAliases : {
                    "int32" : "Std.Int32",
                    "str" : "Std.Text.String"
                };
                """);

            File.WriteAllText(typesPath, """
                namespace Std {
                    public struct Int32;
                    namespace Text {
                        public struct String;
                    }
                }
                """);

            File.WriteAllText(programPath, """
                namespace App;

                public struct Consumer {
                    internal int32 number;
                    internal str message;
                }
                """);

            CompilerProjectAnalysisResult result = AnalyzeProject(projectPath);

            Assert.False(result.HasErrors);
            Assert.Equal(2, result.Files.Length);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void AnalyzeProjectFile_GlobalAliases_UnresolvedAliasUsage_ReportsDiagnostic()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"maho-project-unresolved-alias-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string projectPath = Path.Combine(tempDirectory, "Sample.mhpr");
            string programPath = Path.Combine(tempDirectory, "Program.mh");

            File.WriteAllText(projectPath, """
                GlobalAliases : {
                    "int32" : "Std.Int32"
                };
                """);

            File.WriteAllText(programPath, """
                public struct Consumer {
                    internal int32 number;
                }
                """);

            CompilerProjectAnalysisResult result = AnalyzeProject(projectPath);

            Assert.True(result.HasErrors);
            CompilerBatchFileResult program = Assert.Single(result.Files);
            Assert.Contains(program.Analysis!.Diagnostics, diagnostic => diagnostic.Code == "MH0500" && diagnostic.Message.Contains("int32"));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void MahoCompiler_ReportsPhaseTimers_ForSyntaxAndSemanticPhases()
    {
        var output = MahoCompiler.CompileSource("public struct Sample;");
        Assert.NotNull(output.PhaseTimers);
        Assert.True(output.PhaseTimers.Syntax >= TimeSpan.Zero);
        Assert.True(output.PhaseTimers.SemanticAnalysis >= TimeSpan.Zero);
        Assert.Equal(output.PhaseTimers.Syntax + output.PhaseTimers.SemanticAnalysis + output.PhaseTimers.Lowering, output.PhaseTimers.Total);
        Assert.Equal(output.PhaseTimers.Total, output.Elapsed);
    }
}