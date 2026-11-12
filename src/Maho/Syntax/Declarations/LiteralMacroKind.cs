namespace Maho.Syntax;

/// <summary>
/// Classifies the constant literal kind that a literal macro operates on.
/// </summary>
internal enum LiteralMacroKind : byte
{
    Int,
    Float,
    Str,
    Char
}

