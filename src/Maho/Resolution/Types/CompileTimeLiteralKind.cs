namespace Maho.Resolution;

/// <summary>Classifies the raw scalar kind of a compile-time literal expression.</summary>
internal enum CompileTimeLiteralKind : byte
{
    /// <summary>Unsuffixed integer literal (e.g. 42, 0xFF, 0b1010).</summary>
    Int,

    /// <summary>Unsuffixed floating-point literal (e.g. 3.14, 1e-5).</summary>
    Float,

    /// <summary>String literal (e.g. "hello world").</summary>
    Str,

    /// <summary>Character literal (e.g. 'c').</summary>
    Char
}


