using System;

namespace Maho;

[Flags]
public enum AnalysisOutput
{
    None = 0,
    Lexer = 1 << 0,
    Parser = 1 << 1
}
