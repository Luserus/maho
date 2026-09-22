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
            CommandLine.Run(["--diagnostics", "json", "-o", diagFile, sourceFile]);

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
            CommandLine.Run(["--debug", "parse", "-o", debugFile, sourceFile]);

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
            CommandLine.Run([sourceFile, "--debug", "parse", "-o", debugFile, "--diagnostics", "json", "-o", diagFile]);

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

            int exitCode = CommandLine.Run([Path.Combine("SubDir", "Test.mhpr")]);
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

            int exitCode = CommandLine.Run(["--diagnostic-paths", "project", Path.Combine("SubDir", "Test.mhpr")]);
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

            int exitCode = CommandLine.Run(["--diagnostic-paths", "full", Path.Combine("SubDir", "Test.mhpr")]);
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
                CommandLine.Run([sourceFile]);
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
                int exitCode = CommandLine.Run(["--implicit-toplevel=false", sourceFile]);
                Assert.Equal(1, exitCode);
                string output = sw.ToString();
                Assert.Contains("MH0011", output);
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
    public void CommandLine_DirectoryWithoutProjectFile_ErrorsWithoutNoProjectFlag()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CliDirNoProjErr_" + Guid.NewGuid().ToString("N"));
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
                int exitCode = CommandLine.Run([tempDir]);
                Assert.Equal(1, exitCode);
                string output = sw.ToString();
                Assert.Contains("No project file ('.mhpr') found", output);
                Assert.Contains("--no-project", output);
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
                Assert.Contains("MH0012", output);
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
                Assert.Contains("MH0011", output);
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
