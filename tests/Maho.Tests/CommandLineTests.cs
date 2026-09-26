namespace Maho.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void CommandLine_HelpFlag_PrintsUsageAndExitsZeroWithoutRequiringFiles()
    {
        using var sw = new StringWriter();
        var originalOut = Console.Out;

        try
        {
            Console.SetOut(sw);
            int exitCode = CommandLine.Run(["-h"]);
            Assert.Equal(0, exitCode);
            Assert.Contains("Usage: maho", sw.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void CommandLine_VersionFlag_PrintsVersionAndExitsZeroWithoutRequiringFiles()
    {
        using var sw = new StringWriter();
        var originalOut = Console.Out;

        try
        {
            Console.SetOut(sw);
            int exitCode = CommandLine.Run(["--version"]);
            Assert.Equal(0, exitCode);
            Assert.Contains("Maho: v", sw.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void CommandLine_ShortVersionFlag_PrintsVersionAndExitsZero()
    {
        using var sw = new StringWriter();
        var originalOut = Console.Out;

        try
        {
            Console.SetOut(sw);
            int exitCode = CommandLine.Run(["-v"]);
            Assert.Equal(0, exitCode);
            Assert.Contains("Maho: v", sw.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void CommandLine_NoArguments_PrintsUsageAndExitsZero()
    {
        using var sw = new StringWriter();
        var originalOut = Console.Out;

        try
        {
            Console.SetOut(sw);
            int exitCode = CommandLine.Run([]);
            Assert.Equal(0, exitCode);
            Assert.Contains("Usage: maho", sw.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void CommandLine_DiagnosticsWithShortOutputFlag_WritesToFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliShortOutput_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(sourceFile, "var x = 1;");

            string diagFile = Path.Combine(tempDir, "diag.json");
            CommandLine.Run(["-np", "--diagnostics", "json", "-o", diagFile, sourceFile]);

            Assert.True(File.Exists(diagFile));
            string json = File.ReadAllText(diagFile);
            Assert.Contains("\"files\"", json);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_DebugWithShortOutputFlag_WritesToFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliDebugShortOutput_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(sourceFile, "public struct Widget;");

            string debugFile = Path.Combine(tempDir, "debug.json");
            CommandLine.Run(["-np", "--debug", "parse", "-o", debugFile, sourceFile]);

            Assert.True(File.Exists(debugFile));
            string json = File.ReadAllText(debugFile);
            Assert.Contains("\"files\"", json);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_CombinedDebugAndDiagnostics_WithIndividualOutputFlags_WritesBothFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliCombinedOutput_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(sourceFile, "public struct Widget;");

            string debugFile = Path.Combine(tempDir, "debug.json");
            string diagFile = Path.Combine(tempDir, "diag.json");

            // Put source first to verify positional independence
            CommandLine.Run(["-np", sourceFile, "--debug", "parse", "-o", debugFile, "--diagnostics", "json", "-o", diagFile]);

            Assert.True(File.Exists(debugFile));
            Assert.True(File.Exists(diagFile));
            Assert.Contains("\"parser\"", File.ReadAllText(debugFile));
            Assert.Contains("\"diagnostics\"", File.ReadAllText(diagFile));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_DiagnosticPaths_DefaultsToCwd()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "Maho_CliDiagPaths_" + Guid.NewGuid().ToString("N"));
        string projectDir = Path.Combine(baseDir, "SubDir");
        Directory.CreateDirectory(projectDir);

        string originalCwd = Directory.GetCurrentDirectory();
        var originalError = Console.Error;

        try
        {
            Directory.SetCurrentDirectory(baseDir);

            string sourceFile = Path.Combine(projectDir, "Program.mh");
            File.WriteAllText(sourceFile, "some syntax error ;");

            string projectFile = Path.Combine(projectDir, "Test.mhpr");
            File.WriteAllText(projectFile, "EntryFile : \"Program.mh\";\nSources : [\"Program.mh\"];\n");

            using var sw = new StringWriter();
            Console.SetError(sw);

            int exitCode = CommandLine.Run([Path.Combine("SubDir", "Program.mh")]);
            string output = sw.ToString();

            // Default relative path should be relative to CWD, which includes SubDir/Program.mh
            string expectedRelPath = Path.Combine("SubDir", "Program.mh");
            Assert.Contains(expectedRelPath, output);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Console.SetError(originalError);
            Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public void CommandLine_DiagnosticPaths_ProjectRelativeFlag_PrintsRelativeToProjectRoot()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "Maho_CliDiagPathsProj_" + Guid.NewGuid().ToString("N"));
        string projectDir = Path.Combine(baseDir, "SubDir");
        Directory.CreateDirectory(projectDir);

        string originalCwd = Directory.GetCurrentDirectory();
        var originalError = Console.Error;

        try
        {
            Directory.SetCurrentDirectory(baseDir);

            string sourceFile = Path.Combine(projectDir, "Program.mh");
            File.WriteAllText(sourceFile, "some syntax error ;");

            string projectFile = Path.Combine(projectDir, "Test.mhpr");
            File.WriteAllText(projectFile, "EntryFile : \"Program.mh\";\nSources : [\"Program.mh\"];\n");

            using var sw = new StringWriter();
            Console.SetError(sw);

            int exitCode = CommandLine.Run(["--diagnostic-paths", "project", "SubDir"]);
            string output = sw.ToString();

            // Should be relative to project root (Program.mh), NOT SubDir/Program.mh
            Assert.Contains("--> Program.mh:", output);
            Assert.DoesNotContain("SubDir" + Path.DirectorySeparatorChar + "Program.mh", output);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Console.SetError(originalError);
            Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public void CommandLine_DiagnosticPaths_FullFlag_PrintsFullPath()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "Maho_CliDiagPathsFull_" + Guid.NewGuid().ToString("N"));
        string projectDir = Path.Combine(baseDir, "SubDir");
        Directory.CreateDirectory(projectDir);

        string originalCwd = Directory.GetCurrentDirectory();
        var originalError = Console.Error;

        try
        {
            Directory.SetCurrentDirectory(baseDir);

            string sourceFile = Path.Combine(projectDir, "Program.mh");
            File.WriteAllText(sourceFile, "some syntax error ;");

            string projectFile = Path.Combine(projectDir, "Test.mhpr");
            File.WriteAllText(projectFile, "EntryFile : \"Program.mh\";\nSources : [\"Program.mh\"];\n");

            using var sw = new StringWriter();
            Console.SetError(sw);

            int exitCode = CommandLine.Run(["--diagnostic-paths", "full", Path.Combine("SubDir", "Program.mh")]);
            string output = sw.ToString();

            Assert.Contains(Path.GetFullPath(sourceFile), output);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Console.SetError(originalError);
            Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public void CommandLine_ImplicitTopLevel_DefaultAllowsTopLevelStatementsForSingleFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliImplicitDefault_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "Script.mh");
            File.WriteAllText(sourceFile, "foo();");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                CommandLine.Run(["-np", sourceFile]);
                string output = sw.ToString();
                Assert.DoesNotContain("MH0011", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_ImplicitTopLevel_FalseFlag_DisallowsTopLevelStatements()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliImplicitFalse_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "Script.mh");
            File.WriteAllText(sourceFile, "foo();");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run(["-np", "--implicit-toplevel=false", sourceFile]);
                Assert.Equal(1, exitCode);
                string output = sw.ToString();
                Assert.Contains("MH0160", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_DirectoryWithProjectFile_AutomaticallyCompilesProject()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliDirProject_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string projectFile = Path.Combine(tempDir, "App.mhpr");
            string programFile = Path.Combine(tempDir, "Program.mh");

            File.WriteAllText(projectFile, "EntryFile : \"Program.mh\";\nSources : [\"Program.mh\"];\nImplicitTopLevel : true;\n");
            File.WriteAllText(programFile, "call();");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                CommandLine.Run([tempDir]);
                string output = sw.ToString();
                // Should compile the project without reporting missing project file
                Assert.DoesNotContain("No project file", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_DirectoryWithoutProjectFile_SucceedsWithNoProjectFlag()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliDirNoProjOk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string programFile = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(programFile, "call();");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                CommandLine.Run(["--no-project", tempDir]);
                string output = sw.ToString();
                Assert.DoesNotContain("No project file", output);
                Assert.DoesNotContain("MH0011", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_NoProject_MultipleFiles_SingleTopLevelCandidateSucceeds()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliNoProjMultiOk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string typesFile = Path.Combine(tempDir, "Types.mh");
            string entryFile = Path.Combine(tempDir, "Entry.mh");

            File.WriteAllText(typesFile, "public class Point { public int X; }");
            File.WriteAllText(entryFile, "call();");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                CommandLine.Run(["--no-project", tempDir]);
                string output = sw.ToString();
                Assert.DoesNotContain("MH0011", output);
                Assert.DoesNotContain("MH0012", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_NoProject_MultipleFiles_MultipleTopLevelCandidatesReportsMH0012()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliNoProjMultiErr_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string file1 = Path.Combine(tempDir, "First.mh");
            string file2 = Path.Combine(tempDir, "Second.mh");

            File.WriteAllText(file1, "call1();");
            File.WriteAllText(file2, "call2();");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run(["--no-project", tempDir]);
                Assert.Equal(1, exitCode);
                string output = sw.ToString();
                Assert.Contains("MH0161", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_NoProject_ImplicitTopLevelFalse_RequiresPragma()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliNoProjImplFalse_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string programFile = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(programFile, "call();");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run(["--no-project", "--implicit-toplevel=false", tempDir]);
                Assert.Equal(1, exitCode);
                string output = sw.ToString();
                Assert.Contains("MH0160", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_CompileFailure_PrintsErrorsCountStatus()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliErrorStatus_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(sourceFile, "public struct Widget;\npublic struct Widget;");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run(["-np", "--color", "never", sourceFile]);
                Assert.Equal(1, exitCode);
                string output = sw.ToString();
                Assert.Contains("Build failed with 1 error(s)", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_MacroTraceback_RendersBoxConnectorBelowUnderline()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliMacroTrace_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(sourceFile, """
                public macro $DefineType {
                    (@name: ident) => {
                        public struct @name {
                            public UnknownType val;
                        }
                    }
                }

                $DefineType(MyStruct);
                """);

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run(["-np", "--color", "never", sourceFile]);
                Assert.Equal(1, exitCode);
                string output = sw.ToString();
                Assert.Contains("└── from this macro invocation", output);
                Assert.Contains("Build failed with 1 error(s)", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_FileWithoutNoProject_AssumesProjectFile_AllowsAnyExtension()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliProjectCustomExt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string programFile = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(programFile, "call();");

            string projectFile = Path.Combine(tempDir, "CustomBuild.proj");
            File.WriteAllText(projectFile, "EntryFile : \"Program.mh\";\nSources : [\"Program.mh\"];\nImplicitTopLevel : true;\n");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                CommandLine.Run([projectFile]);
                string output = sw.ToString();
                Assert.DoesNotContain("No project file", output);
                Assert.DoesNotContain("MH0011", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_SingleFile_FailsWithPipelineNotImplementedMH9000()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliSingleFile_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(sourceFile, "public struct Widget;");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run([sourceFile]);
                Assert.Equal(1, exitCode);
                string output = sw.ToString();
                Assert.Contains("MH9000", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_CheckFlag_SucceedsWithExitCodeZero()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliCheckOk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(sourceFile, "public struct Widget;");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run(["--check", sourceFile]);
                Assert.Equal(0, exitCode);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_MultipleSourceFiles_CompilesTogether()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliMultiFiles_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string file1 = Path.Combine(tempDir, "A.mh");
            string file2 = Path.Combine(tempDir, "B.mh");
            File.WriteAllText(file1, "public struct Alpha;");
            File.WriteAllText(file2, "public struct Beta { Alpha A; }");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run(["--check", file1, file2]);
                Assert.Equal(0, exitCode);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_EmitIlFlag_ExplicitlyFailsWithMH9000()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliEmitIl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(sourceFile, "public struct Widget;");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run(["--emit-il", sourceFile]);
                Assert.Equal(1, exitCode);
                string output = sw.ToString();
                Assert.Contains("MH9000", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_ShortNoProjectFlag_CompilesScriptWithArbitraryExtension()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliNpAnyExt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string scriptFile = Path.Combine(tempDir, "Script.txt");
            File.WriteAllText(scriptFile, "public struct Widget;\npublic struct Widget;");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run(["-np", "--color", "never", scriptFile]);
                Assert.Equal(1, exitCode);
                string output = sw.ToString();
                Assert.Contains("Build failed with 1 error(s)", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_DirectoryScanning_WithCustomPattern_DiscoversMatchingFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliDirPattern_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string matchingFile = Path.Combine(tempDir, "Widget.custom");
            File.WriteAllText(matchingFile, "public struct CustomWidget;");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run(["--check", "--pattern", "*.custom", tempDir]);
                Assert.Equal(0, exitCode);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_DirectoryWithoutFiles_FailsWithHelpfulError()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliDirEmpty_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run([tempDir]);
                Assert.Equal(1, exitCode);
                string output = sw.ToString();
                Assert.Contains("No source files found in directory", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_NoRecurse_DoesNotScanSubdirectories()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliNoRecurse_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string subDir = Path.Combine(tempDir, "Sub");
        Directory.CreateDirectory(subDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Root.mh"), "public struct RootWidget;");
            File.WriteAllText(Path.Combine(subDir, "Invalid.mh"), "syntax error here ;;;");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run(["--check", "--no-recurse", tempDir]);
                Assert.Equal(0, exitCode);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CommandLine_GlobalAlias_ResolvesAliasesAcrossFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliAlias_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceA = Path.Combine(tempDir, "A.mh");
            string sourceB = Path.Combine(tempDir, "B.mh");

            File.WriteAllText(sourceA, "namespace Lib { public struct Foo; }");
            File.WriteAllText(sourceB, "namespace App; public struct Bar { internal MyFoo f; }");

            using var sw = new StringWriter();
            var originalError = Console.Error;
            try
            {
                Console.SetError(sw);
                int exitCode = CommandLine.Run(["--check", "-np", "--color", "never", "--alias", "MyFoo=Lib.Foo", sourceA, sourceB]);
                Assert.Equal(0, exitCode);
                string output = sw.ToString();
                Assert.Contains("Build succeeded", output);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}


