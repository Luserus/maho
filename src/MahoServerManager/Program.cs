using System;
using System.IO;
using System.Text;

namespace Maho.Server.Manager;

internal static class Program
{
    private const string AnsiReset = "\u001b[0m";
    private const string AnsiRed = "\u001b[31;1m";

    private static bool ShouldUseColors(string? colorMode) => colorMode switch
    {
        "never" or "false" or "off" => false,
        "always" or "true" or "on" => true,
        "auto" => (!Console.IsOutputRedirected || !Console.IsErrorRedirected) &&
                  Environment.GetEnvironmentVariable("NO_COLOR") == null,
        _ => Environment.GetEnvironmentVariable("NO_COLOR") == null
    };

    private static string FormatError(string text, bool useColors) => useColors ? $"{AnsiRed}{text}{AnsiReset}" : text;

    public static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
        }

        string? colorMode = null;
        string? command = null;
        string? targetVersion = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--color":
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        colorMode = args[++i].ToLowerInvariant();
                    else
                        colorMode = "always";
                    break;

                case string c when c.StartsWith("--color=", StringComparison.OrdinalIgnoreCase):
                    colorMode = c[(c.IndexOf('=') + 1)..].ToLowerInvariant();
                    break;

                case "-h" or "--help" or "help":
                    PrintUsage(Console.Out);
                    return 0;

                default:
                    if (arg.StartsWith('-'))
                    {
                        bool useColorsErr = ShouldUseColors(colorMode);
                        Console.Error.WriteLine($"{FormatError("error", useColorsErr)}: unknown option '{arg}' for server-manager.");
                        return 1;
                    }

                    if (command is null)
                        command = arg.ToLowerInvariant();
                    else
                        targetVersion ??= arg;

                    break;
            }
        }

        bool useColors = ShouldUseColors(colorMode);

        if (string.IsNullOrWhiteSpace(command))
        {
            PrintUsage(Console.Out);
            return 0;
        }

        if (command == "shutdown")
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(targetVersion))
                {
                    bool success = CompilerServerClient.ShutdownServer(targetVersion);
                    if (success)
                    {
                        Console.WriteLine($"stopped compiler server daemon for version {targetVersion}.");
                        return 0;
                    }
                    Console.WriteLine($"no running compiler server daemon found for version {targetVersion}.");
                    return 0;
                }

                int stopped = CompilerServerClient.ShutdownAllServers();
                Console.WriteLine($"stopped {stopped} compiler server{(stopped > 1 ? "s" : "")}.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{FormatError("error", useColors)}: failed to shutdown compiler servers: {ex.Message}");
                return 1;
            }
        }

        Console.Error.WriteLine($"{FormatError("error", useColors)}: unknown command '{command}'. Usage: server-manager shutdown [version] [--color <mode>]");
        return 1;
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: server-manager <command> [options]");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        writer.WriteLine("  shutdown [version]   Stop running compiler server daemons (all or specific version)");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --color <mode>       Control ANSI colored output (auto, always, never)");
        writer.WriteLine("  -h, --help           Show this help message");
    }
}
