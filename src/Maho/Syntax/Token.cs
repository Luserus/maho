using System.Text;

namespace Maho.Syntax
{
    /// <summary> Token of the program which serves as the smallest unit of meaningful data the compiler can use. </summary>
    internal sealed class Token
    {
        /// <value> Data of the Token. </value>
        public StringBuilder Data { get; set; } = new();
        /// <value> Returns the Token data in string form. </value>
        /// <remarks> This field is readonly. </remarks>
        public string Value { get => Data.ToString(); }
        /// <value> Line number of the Token. </value>
        public int LineNumber { get; set; }
        /// <value> Column number of the Token. </value>
        public int ColumnNumber { get; set; }
        /// <value> Token kind of the Token. </values>
        public TokenKind Kind { get; set; }
    }
}