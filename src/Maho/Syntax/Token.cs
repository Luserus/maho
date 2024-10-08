using System.Text;

namespace Maho.Syntax
{
    /// <summary> Token of the program which serves as the smallest unit of meaningful data the compiler can use. </summary>
    internal struct Token
    {
        /// <summary> Initializes the Token. </summary>
        public Token() => Data = new();

        /// <summary> Data of the Token. </summary>
        public StringBuilder Data { get; set; }
        /// <summary> Returns the Token data in string form. </summary>
        public string Value { get; set; } = null!;
        /// <summary> Line number of the Token. </summary>
        public int LineNumber { get; set; }
        /// <summary> Column number of the Token. </summary>
        public int CharNumber { get; set; }
        /// <summary> Token kind of the Token. </summary>
        public TokenKind Kind { get; set; }
    }
}