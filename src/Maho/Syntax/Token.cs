using System.Text;

namespace Maho.Syntax;

/// <summary> Token of the program which serves as the smallest unit of meaningful data the compiler can use. </summary>
internal struct Token
{
    /// <summary> Initializes the Token. </summary>
    public Token() => Data = new();
    
    /// <summary> Initializes the Token with given data. </summary>
    /// <param name="value"> The Token data in string form. </param>
    /// <param name="charNumber"> Char number of the Token in the Program. </param>
    /// <param name="lineNumber"> Line number of the Token. </param>
    /// <param name="columnNumber"> Column number of the Token. </param>
    /// <param name="kind"> Token kind of the Token. </param>
    public Token(string value, int charNumber, int lineNumber, int columnNumber, TokenKind kind) : this()
    {
        Value = value;
        CharNumber = charNumber;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
        Kind = kind;
    }

    /// <summary> Data of the Token. </summary>
    public StringBuilder Data { get; set; }
    /// <summary> Returns the Token data in string form. </summary>
    public string Value { get; set; } = null!;
    /// <summary> Char number of the Token in the Program. </summary>
    public int CharNumber { get; set; }
    /// <summary> Line number of the Token. </summary>
    public int LineNumber { get; set; }
    /// <summary> Column number of the Token. </summary>
    public int ColumnNumber { get; set; }
    /// <summary> Token kind of the Token. </summary>
    public TokenKind Kind { get; set; }
}
