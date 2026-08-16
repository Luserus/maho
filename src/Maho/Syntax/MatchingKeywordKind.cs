namespace Maho.Syntax;

/// <summary> Enumerates identifiers that should be treated as contextual keywords by the lexer. </summary>
internal enum MatchingKeywordKind : byte
{
    None,
    Get,
    Set,
    If,
    Else,
    While,
    Return,
    Public,
    Private,
    Internal,
    Protected,
    Sealed,
    Virtual,
    Extern,
    Namespace,
    Attribute,
    Struct,
    Class,
    Enum,
    Union,
    Interface,
    Static,
    For,
    New,
    Put,
    Const,
    Where,
    With,
    Partial,
    Unsafe,
    Intrinsic,
    Var,
    Dyn
}
