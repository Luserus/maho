using System.Collections.Generic;

namespace Maho;

/// <summary>
/// Immutable analysis result returned by <see cref="MahoCompiler"/>. It carries both structured
/// diagnostics and any optional serialized debug views requested by the caller.
/// </summary>
/// <param name="SourcePath">Stable identity for the analyzed document.</param>
/// <param name="LexerJson">Serialized lexer debug payload when requested.</param>
/// <param name="ParserJson">Serialized parser debug payload when requested.</param>
/// <param name="Diagnostics">Structured diagnostics intended for direct API consumption.</param>
/// <param name="DiagnosticsJson">Serialized diagnostics payload used by JSON-oriented consumers.</param>
public sealed record CompilerAnalysisResult(
    string SourcePath,
    string? LexerJson,
    string? ParserJson,
    IReadOnlyList<DiagnosticInfo> Diagnostics,
    string DiagnosticsJson)
{
    /// <summary>
    /// Scans the diagnostics collection for error severity so callers can branch on success without
    /// duplicating severity policy in every consumer.
    /// </summary>
    public bool HasErrors
    {
        get
        {
            // Keep the failure signal derived from the actual diagnostics payload so the result
            // cannot drift out of sync with a separately cached flag.
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                if (Diagnostics[i].Severity is DiagnosticSeverity.Error)
                    return true;
            }

            return false;
        }
    }
}
