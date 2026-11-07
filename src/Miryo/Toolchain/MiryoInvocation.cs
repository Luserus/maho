using System;
using System.Collections.Generic;
using System.Text;

namespace Miryo.Toolchain;

/// <summary>
/// Parsed representation of a Miryo command-line invocation, supporting optional
/// bracketed toolchain properties (e.g. [v: 1.0, target: release]) alongside
/// standard commands and flags.
/// </summary>
public sealed class MiryoInvocation
{
    public string Command { get; }
    public IReadOnlyDictionary<string, string> Properties { get; }
    public string[] Arguments { get; }
    public string TargetVersion { get; }
    public string? ColorMode { get; }

    public MiryoInvocation(string command, IReadOnlyDictionary<string, string> properties, string[] arguments, string targetVersion, string? colorMode = null)
    {
        Command = command;
        Properties = properties;
        Arguments = arguments;
        TargetVersion = targetVersion;
        ColorMode = colorMode;
    }

    /// <summary>
    /// Parses process arguments into a <see cref="MiryoInvocation"/>.
    /// Extracts bracketed properties `[k: v, ...]`, extracts the primary command,
    /// and collects remaining arguments.
    /// </summary>
    public static MiryoInvocation Parse(string[] args, string? defaultVersion = null)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var remaining = new List<string>();
        string? command = null;
        string? colorMode = null;

        int i = 0;
        while (i < args.Length)
        {
            string token = args[i];

            // Detect start of bracketed property expression: e.g. "[v: 1.0]" or "[v:" "1.0]"
            if (token.StartsWith('[') || (token == "["))
            {
                var bracketContent = new StringBuilder();

                while (i < args.Length)
                {
                    string part = args[i];
                    bracketContent.Append(part).Append(' ');
                    if (part.EndsWith(']'))
                    {
                        i++;
                        break;
                    }
                    i++;
                }

                string raw = bracketContent.ToString().Trim();
                if (raw.StartsWith('[')) raw = raw[1..];
                if (raw.EndsWith(']')) raw = raw[..^1];

                ParseProperties(raw, properties);
                continue;
            }

            if (token == "--color")
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    colorMode = args[++i].ToLowerInvariant();
                    if (command is not null)
                    {
                        remaining.Add(token);
                        remaining.Add(args[i]);
                    }
                }
                else
                {
                    colorMode = "always";
                    if (command is not null)
                        remaining.Add(token);
                }
                i++;
                continue;
            }

            if (token.StartsWith("--color=", StringComparison.OrdinalIgnoreCase))
            {
                colorMode = token[(token.IndexOf('=') + 1)..].ToLowerInvariant();
                if (command is not null)
                    remaining.Add(token);
                i++;
                continue;
            }

            if (command is null)
            {
                command = token;
                i++;
                continue;
            }

            remaining.Add(token);
            i++;
        }

        command ??= "help";

        if (properties.TryGetValue("color", out string? propColor) && !string.IsNullOrWhiteSpace(propColor))
            colorMode ??= propColor.ToLowerInvariant();

        // Determine target version: explicit property takes precedence, followed by default, then fallback
        string version;
        if (properties.TryGetValue("v", out string? propV) && !string.IsNullOrWhiteSpace(propV))
            version = MahoPaths.NormalizeVersion(propV);
        else if (properties.TryGetValue("version", out string? propVer) && !string.IsNullOrWhiteSpace(propVer))
            version = MahoPaths.NormalizeVersion(propVer);
        else if (!string.IsNullOrWhiteSpace(defaultVersion))
            version = MahoPaths.NormalizeVersion(defaultVersion);
        else
            version = "0.2";

        return new MiryoInvocation(command.ToLowerInvariant(), properties, remaining.ToArray(), version, colorMode);
    }

    private static void ParseProperties(string raw, Dictionary<string, string> properties)
    {
        // Format: "k: v, k2: v2" or "k=v"
        string[] pairs = raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string pair in pairs)
        {
            int sep = pair.IndexOf(':');
            if (sep < 0)
                sep = pair.IndexOf('=');

            if (sep > 0)
            {
                string key = pair[..sep].Trim();
                string val = pair[(sep + 1)..].Trim();
                // strip quotes if present
                if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
                    val = val[1..^1];

                properties[key] = val;
            }
            else if (!string.IsNullOrWhiteSpace(pair))
            {
                properties[pair.Trim()] = "true";
            }
        }
    }
}
