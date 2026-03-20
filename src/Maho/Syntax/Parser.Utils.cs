using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    private sealed class OperatorTrieNode
    {
        public Dictionary<char, OperatorTrieNode> Next { get; } = [];
        public TokenKind? Kind { get; set; } = null;
    }

    private static readonly OperatorTrieNode operatorTrie;

    private static readonly (string Value, TokenKind Kind)[] OperatorDefinitions =
    [/// <summary> Returns the value and combined form of combined operator token types. </summary>
    /// <returns> The string value and TokenKind of the combined operators. </returns>
        ("<<<", TokenKind.LessThanLessThanLessThanSigns),
        ("==", TokenKind.EqualsEquals),
        ("!=", TokenKind.ExclamationEquals),
        ("<<", TokenKind.LessThanLessThanSigns),
        (">>", TokenKind.GreaterThanGreaterThanSigns),
        ("<=", TokenKind.LessThanEquals),
        (">=", TokenKind.GreaterThanEquals),
        ("&&", TokenKind.AmpersandAmpersand),
        ("||", TokenKind.VerticalBarVerticalBar),
        ("+", TokenKind.Plus),
        ("-", TokenKind.Minus),
        ("*", TokenKind.Asterisk),
        ("/", TokenKind.ForwardSlash),
        ("%", TokenKind.Percentage),
        ("&", TokenKind.Ampersand),
        ("|", TokenKind.VerticalBar),
        ("<", TokenKind.LessThanSign),
        (">", TokenKind.GreaterThanSign),
        ("?", TokenKind.QuestionMark),
        ("=", TokenKind.Equals)
    ];

    private static readonly TokenKind[] synchronizationTokens = [TokenKind.Semicolon, TokenKind.RightBrace, TokenKind.EndToken];

    private static OperatorTrieNode BuildOperatorTrie()
    {
        var root = new OperatorTrieNode();

        foreach (var (value, kind) in OperatorDefinitions)
        {
            var node = root;

            foreach (char ch in value)
            {
                if (!node.Next.ContainsKey(ch))
                    node.Next[ch] = new OperatorTrieNode();

                node = node.Next[ch];
            }

            node.Kind = kind;
        }

        return root;
    }

    /// <summary> Returns the length and combined form of combined operator token types. </summary>
    /// <returns> The length and TokenKind of the combined operators. </returns>
    private (TokenKind Kind, int Length) GetCombinedOperatorData()
    {
        var node = operatorTrie;
        int length = 0;
        TokenKind? foundKind = null;

        // Read ahead using Peek(i), character by character
        for (int i = 0; ; i++)
        {
            var token = Peek(i);

            if (token.Kind is TokenKind.EndToken)
                break; // end of tokens

            if (!node.Next.TryGetValue(text[token.Span.Start], out node))
                break; // no further match

            length = i + 1;
            foundKind = node.Kind;
        }
                
        return (foundKind ?? TokenKind.NullToken, length);
    }


    [System.Flags]
    private enum OperatorRole : byte
    {
        None = 0,
        Prefix = 1,
        Infix = 2,
        Postfix = 4
    }

    private readonly struct OperatorEntry
    {
        public TokenKind Kind { get; }
        public OperatorRole Role { get; }
        public int LeftBindingPower { get; }
        public int RightBindingPower { get; }

        public bool IsPrefix => (Role & OperatorRole.Prefix) != 0;
        public bool IsInfix => (Role & OperatorRole.Infix) != 0;
        public bool IsPostfix => (Role & OperatorRole.Postfix) != 0;

        public OperatorEntry(TokenKind kind, OperatorRole role, int lbp, int rbp)
        {
            Kind = kind;
            Role = role;
            LeftBindingPower = lbp;
            RightBindingPower = rbp;
        }
    }

    private static readonly Dictionary<TokenKind, OperatorEntry> operatorTable = new()
    {
        { TokenKind.Plus, new OperatorEntry(TokenKind.Plus, OperatorRole.Prefix | OperatorRole.Infix, 70, 70) },
        { TokenKind.Minus, new OperatorEntry(TokenKind.Minus, OperatorRole.Prefix | OperatorRole.Infix, 70, 70) },
        { TokenKind.Asterisk, new OperatorEntry(TokenKind.Asterisk, OperatorRole.Prefix |OperatorRole.Infix, 60, 60) },
        { TokenKind.ForwardSlash, new OperatorEntry(TokenKind.ForwardSlash, OperatorRole.Infix, 60, 60) },
        { TokenKind.Percentage, new OperatorEntry(TokenKind.Percentage, OperatorRole.Infix, 60, 60) },
        { TokenKind.EqualsEquals, new OperatorEntry(TokenKind.EqualsEquals, OperatorRole.Infix, 35, 35) },
        { TokenKind.ExclamationEquals, new OperatorEntry(TokenKind.ExclamationEquals, OperatorRole.Infix, 35, 35) },
        { TokenKind.LessThanSign, new OperatorEntry(TokenKind.LessThanSign, OperatorRole.Infix, 40, 40) },
        { TokenKind.LessThanEquals, new OperatorEntry(TokenKind.LessThanEquals, OperatorRole.Infix, 40, 40) },
        { TokenKind.GreaterThanSign, new OperatorEntry(TokenKind.GreaterThanSign, OperatorRole.Infix, 40, 40) },
        { TokenKind.GreaterThanEquals, new OperatorEntry(TokenKind.GreaterThanEquals, OperatorRole.Infix, 40, 40) },
        { TokenKind.AmpersandAmpersand, new OperatorEntry(TokenKind.AmpersandAmpersand, OperatorRole.Infix, 25, 25) },
        { TokenKind.VerticalBarVerticalBar, new OperatorEntry(TokenKind.VerticalBarVerticalBar, OperatorRole.Infix, 20, 20) },
        { TokenKind.Equals, new OperatorEntry(TokenKind.Equals, OperatorRole.Infix, 9, 10) } // Right associative
    };

    private Token ConsumeOperator()
    {
        var (kind, length) = GetCombinedOperatorData();

        Token first = default!;
        Token token = default!;

        if (length == 0)
            return new Token(text, new TextSpan(LookaheadCurrentToken.Span.Start, 0), TokenKind.NullToken, [], []);

        for (int i = 0; i < length; i++)
        {
            token = Consume();

            if (i == 0)
                first = token;
        }

        Token last = token;

        return new Token(text, new TextSpan(first.Span.Start, last.Span.End - first.Span.Start), kind, first.LeadingTrivia, last.TrailingTrivia);
    }

    /// <summary> Peek ahead in the tokens list by specified offset. </summary>
    /// <param name="offset"> Offset by which to peek ahead. By default, it is 1. </param>
    /// <returns> Token at the index peeked. Returns last token from the list if the offset added to current index exceeds the token list count. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token Peek(int offset = 1) => current + offset < tokens.Count ? tokens[current + offset] : tokens[^1];

    /// <summary> Consumes the current token and moves the current index ahead by 1. </summary>
    /// <returns> The token consumed. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token Consume()
    {
        var currentToken = CurrentToken;
        current++;
        return currentToken;
    }

    private void Synchronize()
    {
        while (CurrentToken.Kind is not TokenKind.EndToken && System.Array.IndexOf(synchronizationTokens, CurrentToken.Kind) == -1)
            Consume();

        if (CurrentToken.Kind is TokenKind.Semicolon)
            Consume();
    }

    private static bool IsContextualStart(TokenKind kind) => kind is TokenKind.Identifier or TokenKind.RightBrace or TokenKind.EndToken;
}