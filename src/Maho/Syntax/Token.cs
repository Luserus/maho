using System.Text;

namespace Maho.Syntax
{
    /// <summary> Token of the program which serves as the smallest unit of meaningful data the compiler can use. </summary>
    internal sealed class Token
    {
        /// <summary> Data of the Token. </summary>
        public StringBuilder Data { get; set; } = new();
        /// <summary> Returns the Token data in string form. </summary>
        /// <remarks> This field is readonly. </remarks>
        public string Value { get => Data.ToString(); }
        /// <summary> Line number of the Token. </summary>
        public int LineNumber { get; set; }
        /// <summary> Column number of the Token. </summary>
        public int ColumnNumber { get; set; }
        /// <summary> Token kind of the Token. </summary>
        public TokenKind Kind { get; set; }
    }
}