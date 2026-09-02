namespace Maho.Resolution;

[System.Flags]
internal enum VariableFlags : ulong
{
    None        = 0,
    Public      = 1UL << 0,
    Protected   = 1UL << 1,
    Internal    = 1UL << 2,

    Static      = 1UL << 3,
    Readonly    = 1UL << 4,
    Const       = 1UL << 5,
    Immut       = 1UL << 6
}