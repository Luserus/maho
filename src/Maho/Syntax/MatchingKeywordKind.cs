namespace Maho.Syntax;

/// <summary> Enumerates identifiers that should be treated as contextual keywords by the lexer. </summary>
internal enum MatchingKeywordKind : byte
{
    None,
    If,
    Else,
    While,
    Return,
    Public,
    Private,
    Internal,
    Protected,
    Sealed,
    Extern,
    Namespace,
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
    Partial
}
