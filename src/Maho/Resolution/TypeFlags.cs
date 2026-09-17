namespace Maho.Resolution;

[System.Flags]
internal enum TypeFlags : ulong
{
    None        = 0,
    Public      = 1UL << 0,
    Internal    = 1UL << 1,

    Static      = 1UL << 2,
    Sealed      = 1UL << 3,
    Readonly    = 1UL << 4,

    Unsafe      = 1UL << 5,
    Intrinsic   = 1UL << 6,
}