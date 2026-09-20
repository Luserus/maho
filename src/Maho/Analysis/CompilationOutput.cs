using System.Collections.Generic;
using System.Linq;

namespace Maho.Analysis;

/// <summary>
/// Specifies the variant of compilation output produced by the compiler.
/// </summary>
public enum CompilationOutputKind : byte
{
    /// <summary> Standard compilation output with diagnostics. </summary>
    Diagnostics,

    /// <summary> Debug compilation output containing serialized syntax/parser payloads. </summary>
    Debug,

    /// <summary> Intermediate language or bytecode compilation output. </summary>
    Il
}

/// <summary>
/// Abstract base class for polymorphic compilation results across different compiler modes and targets.
/// </summary>
public abstract class CompilationOutput
{
    /// <summary> The kind of output represented by this instance. </summary>
    public abstract CompilationOutputKind Kind { get; }

    /// <summary> Whether any fatal or error-level diagnostics were reported. </summary>
    public abstract bool HasErrors { get; }

    /// <summary> The diagnostics produced during analysis or compilation. </summary>
    public abstract IReadOnlyList<DiagnosticInfo> Diagnostics { get; }

    /// <summary> Whether compilation succeeded without error diagnostics. </summary>
    public bool Success => !HasErrors;
}

/// <summary>
/// Polymorphic compilation output containing diagnostics and optional serialized AST/token payloads.
/// </summary>
public sealed class DebugCompilationOutput : CompilationOutput
{
    public override CompilationOutputKind Kind => CompilationOutputKind.Debug;

    /// <summary> Source path associated with this compilation output. </summary>
    public string SourcePath { get; }

    /// <summary> Serialized JSON representation of lexer tokens, if requested. </summary>
    public string? LexerJson { get; }

    /// <summary> Serialized JSON representation of the parsed syntax tree, if requested. </summary>
    public string? ParserJson { get; }

    /// <summary> Serialized JSON representation of diagnostics. </summary>
    public string DiagnosticsJson { get; }

    public override bool HasErrors { get; }
    public override IReadOnlyList<DiagnosticInfo> Diagnostics { get; }

    public DebugCompilationOutput(
        string sourcePath,
        string? lexerJson,
        string? parserJson,
        IReadOnlyList<DiagnosticInfo> diagnostics,
        string? diagnosticsJson = null)
    {
        SourcePath = sourcePath;
        LexerJson = lexerJson;
        ParserJson = parserJson;
        Diagnostics = diagnostics;
        DiagnosticsJson = diagnosticsJson ?? DebugJson.Serialize(diagnostics);
        HasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }
}

/// <summary>
/// Polymorphic compilation output containing diagnostics only.
/// </summary>
public sealed class DiagnosticsCompilationOutput : CompilationOutput
{
    public override CompilationOutputKind Kind => CompilationOutputKind.Diagnostics;
    public string SourcePath { get; }
    public override bool HasErrors { get; }
    public override IReadOnlyList<DiagnosticInfo> Diagnostics { get; }

    public DiagnosticsCompilationOutput(string sourcePath, IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        SourcePath = sourcePath;
        Diagnostics = diagnostics;
        HasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }
}

/// <summary>
/// Polymorphic compilation output representing emitted IL or bytecode.
/// </summary>
public sealed class IlCompilationOutput : CompilationOutput
{
    public override CompilationOutputKind Kind => CompilationOutputKind.Il;

    /// <summary> Emitted IL or bytecode binary data. </summary>
    public byte[]? IlBytes { get; }

    /// <summary> Disassembled or human-readable IL text. </summary>
    public string? IlDisassembly { get; }

    public override bool HasErrors { get; }
    public override IReadOnlyList<DiagnosticInfo> Diagnostics { get; }

    public IlCompilationOutput(byte[]? ilBytes, string? ilDisassembly, IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        IlBytes = ilBytes;
        IlDisassembly = ilDisassembly;
        Diagnostics = diagnostics;
        HasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }
}
