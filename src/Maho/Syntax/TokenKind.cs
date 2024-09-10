namespace Maho.Syntax
{
    /// <summary> Token kind of a Token which represents what kind of Token it is and how statemachine handles it. </summary>
    public enum TokenKind
    {
        NullToken,
        Whitespace,
        Tabspace,
        Newline,
        Identifier,
        Number
    }
}