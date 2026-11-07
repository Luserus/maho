using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Miryo.Toolchain;

/// <summary>
/// Server/root manifest (~/.maho/server/manifest.json) describing active versions and installed toolchains.
/// Managed by Miryo as the toolchain manager.
/// </summary>
public sealed record ToolchainRootManifest(
    [property: JsonPropertyName("activeCompilerVersion")] string ActiveCompilerVersion,
    [property: JsonPropertyName("installedTools")] IReadOnlyList<string> InstalledTools,
    [property: JsonPropertyName("installedCompilers")] IReadOnlyList<string> InstalledCompilers)
{
    [JsonIgnore]
    public IReadOnlyList<string> InstalledSdkCompilers => InstalledCompilers;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ToolchainRootManifest Load(string path)
    {
        if (!File.Exists(path))
            return new ToolchainRootManifest("0.2", ["0.2"], ["0.2"]);
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ToolchainRootManifest>(json, JsonOptions)
            ?? new ToolchainRootManifest("0.2", ["0.2"], ["0.2"]);
    }

    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
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

/// <summary>
/// Tool manifest (~/.maho/tools/{version}/manifest.json) declaring tool metadata.
/// </summary>
public sealed record ToolManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("targetCompilerVersion")] string TargetCompilerVersion,
    [property: JsonPropertyName("tools")] IReadOnlyDictionary<string, string> Tools)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ToolManifest Load(string path)
    {
        if (!File.Exists(path))
        {
            return new ToolManifest("0.2.0", "0.2", new Dictionary<string, string>
            {
                ["mahoc"] = "mahoc",
                ["maho-server"] = "maho-server",
                ["maho-ls"] = "maho-ls"
            });
        }
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ToolManifest>(json, JsonOptions)
            ?? new ToolManifest("0.2.0", "0.2", new Dictionary<string, string>());
    }

    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}

/// <summary>
/// Compiler manifest (~/.maho/compiler/{version}/manifest.json).
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
