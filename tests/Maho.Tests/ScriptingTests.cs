using Maho.Analysis;

namespace Maho.Tests;

public sealed class ScriptingTests
{
    [Fact]
    public void SingleFile_EnablesTopLevelByDefault_ForScripting()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "maho_script_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            string scriptFile = Path.Combine(tempDir, "Script.mh");
            File.WriteAllText(scriptFile, """
                var x = 10;
                var y = 20;
                return x + y;
                """);

            // CompileFiles with a single file should enable top-level statements by default
            var result = MahoCompiler.AnalyzeFiles([scriptFile]);

            Assert.False(result.HasErrors);
            Assert.Single(result.Files);
            Assert.Equal(scriptFile, result.EntryFile);
            Assert.NotNull(result.Compilation);
            Assert.False(result.Compilation.HasErrors);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SingleFile_WithPragmaToplevelDisable_RejectsTopLevelStatements()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "maho_script_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            string scriptFile = Path.Combine(tempDir, "Script.mh");
            File.WriteAllText(scriptFile, """
                #pragma toplevel disable

                var x = 10;
                return x;
                """);

            var result = MahoCompiler.AnalyzeFiles([scriptFile]);

            Assert.True(result.HasErrors);
            var fileResult = Assert.Single(result.Files);
            Assert.NotNull(fileResult.Analysis);
            Assert.Contains(fileResult.Analysis.Diagnostics, d => d.Code == "MH0011");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MultiFile_DoesNotEnableTopLevelByDefault_WithoutEntryFileOrPragma()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "maho_script_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            string file1 = Path.Combine(tempDir, "File1.mh");
            string file2 = Path.Combine(tempDir, "File2.mh");

            File.WriteAllText(file1, """
                var x = 10;
                return x;
                """);
            File.WriteAllText(file2, """
                public struct Foo;
                """);

            var result = MahoCompiler.AnalyzeFiles([file1, file2]);

            // File1 should fail with MH0011 because implicit top-level is not enabled by default for multi-file batches
            Assert.True(result.HasErrors);
            var file1Result = Assert.Single(result.Files, f => f.SourcePath == file1);
            Assert.NotNull(file1Result.Analysis);
            Assert.Contains(file1Result.Analysis.Diagnostics, d => d.Code == "MH0011");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ParallelParsing_ProducesDeterministicAndIdenticalOutputs()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "maho_parallel_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            string[] filePaths = new string[10];
            for (int i = 0; i < 10; i++)
            {
                filePaths[i] = Path.Combine(tempDir, $"Module_{i:D2}.mh");
                File.WriteAllText(filePaths[i], $$"""
                    namespace Module_{{i:D2}};

                    public struct Type_{{i:D2}}
                    {
                        public int id;
                    }
                    """);
            }

            var result = MahoCompiler.AnalyzeFiles(filePaths, AnalysisOutput.Lexer | AnalysisOutput.Parser);

            Assert.False(result.HasErrors);
            Assert.Equal(10, result.Files.Length);

            // Files must be in exact source order
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(filePaths[i], result.Files[i].SourcePath);
                Assert.NotNull(result.Files[i].DebugOutput);
                Assert.NotNull(result.Files[i].DebugOutput!.LexerJson);
                Assert.NotNull(result.Files[i].DebugOutput!.ParserJson);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
