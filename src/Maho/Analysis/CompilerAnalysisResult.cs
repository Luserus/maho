using System.Collections.Generic;

namespace Maho;

public sealed record CompilerAnalysisResult(
    string SourcePath,
    string? LexerJson,
    string? ParserJson,
    IReadOnlyList<DiagnosticInfo> Diagnostics,
    string DiagnosticsJson)
{
    public bool HasErrors
    {
        get
        {
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                if (Diagnostics[i].Severity is DiagnosticSeverity.Error)
                    return true;
            }

            return false;
        }
    }
}
