using System;

namespace Maho;

/// <summary>
/// Selects which optional debug payloads should accompany an analysis result.
/// Diagnostics are always produced; these flags only control extra inspection data.
/// </summary>
[Flags]
public enum AnalysisOutput
{
    /// <summary> Do not include lexer or parser debug payloads. </summary>
    None = 0,

    /// <summary> Include serialized lexer state for token-stream inspection. </summary>
    Lexer = 1 << 0,

    /// <summary> Include serialized parser state for syntax-tree inspection. </summary>
    Parser = 1 << 1
}
