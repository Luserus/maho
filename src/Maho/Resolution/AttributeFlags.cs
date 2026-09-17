namespace Maho.Resolution;

[System.Flags]
internal enum AttributeFlags : ulong
{
    None        = 0,
    Public      = 1UL << 0,
    Internal    = 1UL << 1,
    Intrinsic   = 1UL << 2,
}