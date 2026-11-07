using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Miryo.Update;

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

    private static int Main(string[] args)
    {
        string? pipeHandle = null;
        string? targetPath = null;
        string? sourcePath = null;
        string? newVersion = null;
        string? colorMode = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe-handle" when i + 1 < args.Length:
                    pipeHandle = args[++i];
                    break;
                case "--target" when i + 1 < args.Length:
                    targetPath = args[++i];
                    break;
                case "--source" when i + 1 < args.Length:
                    sourcePath = args[++i];
                    break;
                case "--version" when i + 1 < args.Length:
                    newVersion = args[++i];
                    break;
                case "--color" when i + 1 < args.Length && !args[i + 1].StartsWith('-'):
                    colorMode = args[++i].ToLowerInvariant();
                    break;
                case "--color":
                    colorMode = "always";
                    break;
                case string c when c.StartsWith("--color=", StringComparison.OrdinalIgnoreCase):
                    colorMode = c[(c.IndexOf('=') + 1)..].ToLowerInvariant();
                    break;
            }
        }

        bool useColors = ShouldUseColors(colorMode);

        if (string.IsNullOrWhiteSpace(pipeHandle) || string.IsNullOrWhiteSpace(targetPath) || string.IsNullOrWhiteSpace(sourcePath))
        {
            Console.Error.WriteLine($"{FormatError("error", useColors)}: missing required arguments (--pipe-handle, --target, --source).");
            return 1;
        }

        try
        {
            // Connect to anonymous pipe passed from miryo
            using (var pipe = new AnonymousPipeClientStream(PipeDirection.In, pipeHandle))
            {
                // This read blocks until miryo exits and closes the write end of the pipe
                int _ = pipe.ReadByte();
            }

            // miryo has now exited. Safely overwrite target binary on disk.
            if (!File.Exists(sourcePath))
            {
                Console.Error.WriteLine($"{FormatError("error", useColors)}: source binary not found at '{sourcePath}'.");
                return 1;
            }

            bool updated = false;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.Copy(sourcePath, targetPath, overwrite: true);
                    updated = true;
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(100);
                }
            }

            if (!updated)
            {
                Console.Error.WriteLine($"{FormatError("error", useColors)}: failed to replace '{targetPath}'.");
                return 1;
            }

            // Update bin manifest if present
            try
            {
                string binDir = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
                string binManifestPath = Path.Combine(binDir, "manifest.json");
                var manifest = BinManifest.Load(binManifestPath);
                if (!string.IsNullOrWhiteSpace(newVersion))
                {
                    manifest = manifest with { Version = newVersion };
                }
                manifest.Save(binManifestPath);
            }
            catch
            {
            }

            // Clean up temporary staged source
            try
            {
                if (File.Exists(sourcePath))
                    File.Delete(sourcePath);
            }
            catch
            {
            }

            Console.Out.WriteLine("miryo updated successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{FormatError("error", useColors)}: {ex.Message}");
            return 1;
        }
    }
}

/// <summary>
/// Binary manifest (~/.maho/bin/manifest.json) describing miryo and updater utilities.
/// </summary>
public sealed record BinManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("miryo")] string Miryo,
    [property: JsonPropertyName("miryoUpdate")] string MiryoUpdate,
    [property: JsonPropertyName("channel")] string Channel)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static BinManifest Load(string path)
    {
        if (!File.Exists(path))
            return new BinManifest("1.0.0", "miryo", "miryo-update", "stable");
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BinManifest>(json, JsonOptions)
            ?? new BinManifest("1.0.0", "miryo", "miryo-update", "stable");
    }

    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
