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
}
