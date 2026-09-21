using Maho.Cli;

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
}
