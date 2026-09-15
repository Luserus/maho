namespace Maho.Resolution;

[System.Flags]
internal enum TypeFlags : ulong
{
    None        = 0,
    Public      = 1UL << 0,
    Protected   = 1UL << 1,
    Internal    = 1UL << 2,

    Static      = 1UL << 3,
    Sealed      = 1UL << 4,
    Readonly    = 1UL << 5,

    Unsafe      = 1UL << 6
}