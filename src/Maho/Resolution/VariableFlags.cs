namespace Maho.Resolution;

[System.Flags]
internal enum VariableFlags : ulong
{
    None        = 0,
    Public      = 1UL << 0,
    Private     = 1UL << 1,
    Protected   = 1UL << 2,
    Internal    = 1UL << 3,

    Static      = 1UL << 4,
    Readonly    = 1UL << 5,
}