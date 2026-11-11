namespace Maho.Resolution;

/// <summary>Classifies the raw scalar kind of a compile-time literal expression.</summary>
internal enum CompileTimeLiteralKind : byte
{
    Int,
    Float,
    Str,
    Char
}

