namespace Maho.Syntax;

/// <summary> Token kind of a Token which represents what kind of Token it is and how statemachine handles it. </summary>
public enum TokenKind
{
    // Default token kind
    NullToken, // ''
    // Final token kind
    EndToken, // ''

    // Trivia token kinds
    Whitespace, // ' '
    Tabspace, //    '\t'
    Newline, // '\n'

    // Any valid identifier word token kind
    Identifier, // 'ValidIdentifier'

    // Literal token kinds
    Integer, // '42'
    Float, // '42.01'
    Char, // ''a''
    String, // '"Any string of words."'

    // "Something went wrong" token kinds
    BadToken, // '{The given token}'
    MissingToken, // '{The expected token}'

    // Single operators
    ExclamationMark, // '!'
    DoubleQuote, // '"'
    Octothorpe, // '#'
    Percentage, // '%'
    Ampersand, // '&'
    SingleQuote, // '''
    LeftParen, // '('
    RightParen, // ')'
    Asterisk, // '*'
    Plus, // '+'
    Comma, // ','
    Minus, // '-'
    Dot, // '.'
    ForwardSlash, // '/'
    Colon, // ':'
    Semicolon, // ';'
    LessThanSign, // '<'
    Equals, // '='
    GreaterThanSign, // '>'
    QuestionMark, // '?'
    AtSymbol, // '@'
    LeftSquareBracket, // '['
    BackwardSlash, // '\'
    RightSqureBracket, // ']'
    Caret, // '^'
    Backtick, // '`'
    LeftCurlyBrace, // '{'
    VerticalBar, // '|'
    RightCurlyBrace, // '}'
    Tilde, // '~'

    // Combined operators
    EqualsEquals, // '=='
    ExclamationEquals, // '!='
    LessThanLessThanSigns, // '<<'
    GreaterThanGreaterThanSigns, // '>>'
    LessThanLessThanLessThanSigns, // '<<<' Lmao.
    LessThanEquals, // '<='
    GreaterThanEquals, // '>='
    AmpersandAmpersand, // '&&'
    VerticalBarVerticalBar // '||'
}
