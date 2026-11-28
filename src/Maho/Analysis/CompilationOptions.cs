using System;
using System.Collections.Generic;
using System.Text;

namespace Maho;

/// <summary>
/// Controls whether ANSI color formatting is applied to terminal diagnostic rendering.
/// </summary>
public enum DiagnosticColorMode : byte
{
    Auto,
    Always,
    Never
}

/// <summary>
/// Controls whether source file paths are printed relative to the working directory, relative to project root, or as full paths.
/// </summary>
public enum DiagnosticPathStyle : byte
{
    Relative,
    ProjectRelative,
    Full
}

/// <summary>
/// Controls the output format of diagnostics rendered to the terminal or stdout.
/// </summary>
public enum DiagnosticFormat : byte
{
    Pretty,
    Short,
    Json
}

/// <summary>
/// Configuration options controlling compilation behavior, diagnostic output, and project directives.
/// </summary>
public sealed record CompilationOptions
{
    /// <summary> Terminal color mode for diagnostics. </summary>
    public DiagnosticColorMode ColorMode { get; init; } = DiagnosticColorMode.Auto;

    /// <summary> Path style (relative or full) for diagnostics. </summary>
    public DiagnosticPathStyle PathStyle { get; init; } = DiagnosticPathStyle.Relative;

    /// <summary> Output format (pretty rust-style, short msbuild, or json) for diagnostics. </summary>
    public DiagnosticFormat OutputFormat { get; init; } = DiagnosticFormat.Pretty;

    /// <summary> Whether warnings should be treated as errors. </summary>
    public bool WarningsAsErrors { get; init; } = false;

    /// <summary> Optional explicit entry-point source file. </summary>
    public string? EntryFile { get; init; }

    /// <summary> Whether implicit top-level statements are allowed for the project's entry file. </summary>
    public bool ImplicitTopLevel { get; init; } = false;

    /// <summary> Root directory used for resolving relative paths. </summary>
    public string? RootDirectory { get; init; }

    /// <summary> Canonical PascalCase symbolic module name (used for <c>Module::Symbol</c> resolution). </summary>
    public string? ModuleName { get; init; }

    /// <summary> List of referenced project file paths (.mhpr), optionally prefixed with <c>Alias=</c>. </summary>
    public IReadOnlyList<string> ReferencedProjects { get; init; } = [];

    /// <summary>
    /// Positional compiler-provided aliases corresponding to <c>referencedCompilations</c>
    /// when passing duplicate module names for <c>extern using &lt;Alias&gt;;</c> disambiguation.
    /// </summary>
    public IReadOnlyList<string?> ReferencedAliases { get; init; } = [];

    /// <summary> Project-wide alias definitions. </summary>
    public IReadOnlyDictionary<string, string> GlobalAliases { get; init; } = new Dictionary<string, string>();

    /// <summary> Full path to the project file if loaded from disk. </summary>
    public string? ProjectFilePath { get; init; }

    /// <summary>
    /// Maximum recursion depth allowed during macro expansion.
    /// Defaults to 128. A value of 0 indicates unbounded recursion.
    /// </summary>
    public ulong MacroRecursionLimit { get; init; } = 128;

    /// <summary> Default options with user-friendly out-of-the-box settings. </summary>
    public static CompilationOptions Default => new();

    /// <summary>
    /// Splits a reference spec formatted as <c>Alias=Path</c> or <c>Path</c>.
    /// </summary>
    public static void ParseProjectReferenceSpec(string spec, out string? alias, out string path)
    {
        int eqIndex = spec.IndexOf('=');
        if (eqIndex > 0)
        {
            string candidate = spec[..eqIndex].Trim();
            if (candidate.Length > 0 && !candidate.Contains('/') && !candidate.Contains('\\') && IsValidIdentifier(candidate))
            {
                alias = candidate;
                path = spec[(eqIndex + 1)..].Trim();
                return;
            }
        }

        alias = null;
        path = spec;
    }

    private static bool IsValidIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s))
            return false;
        if (!char.IsLetter(s[0]) && s[0] != '_')
            return false;
        for (int i = 1; i < s.Length; i++)
        {
            if (!char.IsLetterOrDigit(s[i]) && s[i] != '_')
                return false;
        }
        return true;
    }

    /// <summary>
    /// Normalizes a project name into a valid PascalCase symbolic module identifier suitable for <c>::</c>.
    /// </summary>
    public static string NormalizeModuleIdentifier(string? rawName, string fallback = "App")
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return fallback;

        var sb = new StringBuilder(rawName.Length);
        bool capitalizeNext = true;
        foreach (char c in rawName)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }

        if (sb.Length == 0)
            return fallback;

        if (char.IsDigit(sb[0]))
            sb.Insert(0, '_');

        return sb.ToString();
    }

    /// <summary>
    /// Checks whether changing from this options instance to <paramref name="other"/> requires re-analyzing the AST and semantics.
    /// Non-destructive options such as formatting, color mode, or WarningsAsErrors return false.
    /// </summary>
    /// <param name="other">The compilation options instance to compare against.</param>
    /// <returns><see langword="true"/> if semantic re-analysis is required; otherwise <see langword="false"/>.</returns>
    public bool RequiresReanalysis(CompilationOptions? other)
    {
        if (other is null) return true;
        if (ImplicitTopLevel != other.ImplicitTopLevel) return true;
        if (MacroRecursionLimit != other.MacroRecursionLimit) return true;
        if (!string.Equals(EntryFile, other.EntryFile, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(RootDirectory, other.RootDirectory, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(ProjectFilePath, other.ProjectFilePath, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(ModuleName, other.ModuleName, StringComparison.Ordinal)) return true;

        if (ReferencedProjects.Count != other.ReferencedProjects.Count) return true;
        for (int i = 0; i < ReferencedProjects.Count; i++)
        {
            if (!string.Equals(ReferencedProjects[i], other.ReferencedProjects[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (ReferencedAliases.Count != other.ReferencedAliases.Count) return true;
        for (int i = 0; i < ReferencedAliases.Count; i++)
        {
            if (!string.Equals(ReferencedAliases[i], other.ReferencedAliases[i], StringComparison.Ordinal))
                return true;
        }

        if (GlobalAliases.Count != other.GlobalAliases.Count) return true;
        foreach (var (k, v) in GlobalAliases)
        {
            if (!other.GlobalAliases.TryGetValue(k, out var otherVal) || !string.Equals(v, otherVal, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
