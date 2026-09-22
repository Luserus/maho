using System.Collections.Generic;

namespace Maho.Analysis;

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

    /// <summary> List of referenced project file paths (.mhpr). </summary>
    public IReadOnlyList<string> ReferencedProjects { get; init; } = [];

    /// <summary> Project-wide alias definitions. </summary>
    public IReadOnlyDictionary<string, string> GlobalAliases { get; init; } = new Dictionary<string, string>();

    /// <summary> Default options with user-friendly out-of-the-box settings. </summary>
    public static CompilationOptions Default => new();
}
