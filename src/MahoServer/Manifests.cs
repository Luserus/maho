using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maho.Server;

/// <summary>
/// Compiler manifest (~/.maho/compiler/{version}/manifest.json) describing the compiler library.
/// </summary>
public sealed record CompilerManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("assembly")] string Assembly)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static CompilerManifest Load(string path)
    {
        if (!File.Exists(path))
            return new CompilerManifest("0.2.0", "Maho.dll");

        string json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<CompilerManifest>(json, JsonOptions)
            ?? new CompilerManifest("0.2.0", "Maho.dll");
    }

    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
