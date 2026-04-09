namespace Maho;

/// <summary> Enumerates the type declaration forms supported by the syntax tree. </summary>
internal enum TypeKind : byte
{
    Class,
    Struct,
    Interface,
    Enum,
    Union
}
