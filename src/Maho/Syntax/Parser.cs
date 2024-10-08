using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Maho.Syntax
{
    /// <summary> Parses the program tokens into Syntax Tree. </summary>
    internal sealed class Parser
    {
        /// <summary> The tokens to parse. </summary>
        private List<Token> tokens = null!;

        /// <summary> Current index of Token being read from the token list. </summary>
        private int current;

        /// <summary> Parses the tokens into Syntax Tree. This method is in Work-In-Progress and will me modified later to return the Syntax Tree. </summary>
        /// <param name="tokens"> The tokens to parse. </param>
        public void Parse(List<Token> tokens)
        {
            this.tokens = tokens;
            current = default;

            while (current < tokens.Count)
            {
                
            }
        }

        /// <summary> Peek ahead in the tokens list by specified offset. </summary>
        /// <param name="offset"> Offset by which to peek ahead. By default, it is 1. </param>
        /// <returns> Token at the index peeked. Returns last token from the list if the offset added to current index exceeds the token list count. </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Token Peek(int offset = 1)
        {
            if (current + offset < tokens.Count)
                return tokens[current + offset];
            else
                return tokens[^1];
        }

        /// <summary> Consumes the current token and moves the current index ahead by 1. </summary>
        /// <returns> The token consumed. </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Token Consume()
        {
            int current = this.current;
            ++this.current;
            return tokens[current];
        }
    }
}