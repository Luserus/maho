using System.Collections.Immutable;

namespace Maho.Syntax
{
    /// <summary> Lexes the program string into tokens which is later passed to the Parser for syntactic analysis. </summary>
    /// <param name="program"> The program string to lex. </param>
    /// <param name="tabwidth"> The tab width used by the Lexer to increment column number. By default, it is assumed to be 4. </param>
    internal sealed class Lexer(string program, int tabwidth = 4)
    {
        /// <summary> The program string to lex. </summary>
        /// <remarks> This field is readonly. </remarks>
        public string Program { get; } = program;
        /// <summary> The tab width used by the Lexer to increment column number. By default, it is assumed to be 4. </summary>
        /// <remarks> This field is readonly. </remarks>
        public int Tabwidth { get; } = tabwidth;
        /// <summary> Tokens lexed by the Lexer in the form of an Immutable array. </summary>
        /// <remarks> This field is readonly. </remarks>
        public ImmutableArray<Token> Tokens { get => [.. tokens]; }

        private readonly List<Token> tokens = [];

        /// <summary> Lexes the program string into individual tokens. </summary>
        public void Lex()
        {

        }
    }
}