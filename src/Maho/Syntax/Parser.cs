using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Maho.Diagnostics;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Parses the program tokens into Syntax Tree. </summary>
internal sealed partial class Parser
{
    /// <summary> Trie node used to recognize multi-token operator sequences during Pratt parsing. </summary>
    private sealed class OperatorTrieNode
    {
        /// <summary> Outgoing operator-sequence edges keyed by the token's source character. </summary>
        public Dictionary<char, OperatorTrieNode> Next { get; } = [];
        /// <summary> Concrete operator kind represented at this trie node when one sequence ends here. </summary>
        public TokenKind? Kind { get; set; } = null;
    }

    /// <summary> Binding-power metadata for one operator token inside the Pratt parser table. </summary>
    private readonly struct OperatorEntry
    {
        /// <summary> Token kind this table entry describes. </summary>
        public TokenKind Kind { get; }
        /// <summary> Role flags indicating whether the operator is legal in prefix/infix/postfix positions. </summary>
        public OperatorRole Role { get; }
        /// <summary> Left binding power used when the operator appears after an already-parsed expression. </summary>
        public int LeftBindingPower { get; }
        /// <summary> Right binding power used while parsing the operator's operand or right-hand side. </summary>
        public int RightBindingPower { get; }

        /// <summary> Indicates whether the operator can begin a prefix expression. </summary>
        public bool IsPrefix => (Role & OperatorRole.Prefix) != 0;
        /// <summary> Indicates whether the operator can appear between two expressions. </summary>
        public bool IsInfix => (Role & OperatorRole.Infix) != 0;
        /// <summary> Indicates whether the operator can trail an already-parsed expression. </summary>
        public bool IsPostfix => (Role & OperatorRole.Postfix) != 0;

        /// <summary> Creates one operator-table entry with its role and binding powers. </summary>
        public OperatorEntry(TokenKind kind, OperatorRole role, int lbp, int rbp)
        {
            Kind = kind;
            Role = role;
            LeftBindingPower = lbp;
            RightBindingPower = rbp;
        }
    }

    /// <summary> Role flags used to describe how one token kind behaves in expression parsing. </summary>
    [System.Flags]
    private enum OperatorRole : byte
    {
        None = 0,
        Prefix = 1,
        Infix = 2,
        Postfix = 4
    }

    /// <summary> Returns the value and combined form of combined operator token types. </summary>
    /// <returns> The string value and TokenKind of the combined operators. </returns>
    private static readonly (string Value, TokenKind Kind)[] OperatorDefinitions = [
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

    private static readonly OperatorTrieNode operatorTrie;
    private readonly DiagnosticsManager diagnostics;
    private readonly SourceText text;
    /// <summary> The tokens to parse. </summary>
    private List<Token> tokens = null!;
    /// <summary> Current index of Token being read from the token list. </summary>
    private int current;
    /// <summary> Current Token being read from the token list. </summary>
    private Token CurrentToken => tokens[current];
    /// <summary> Most recently consumed token, or the first token before any consumption has happened. </summary>
    private Token PreviousToken => current > 0 ? tokens[current - 1] : tokens[0];

    /// <summary> Parsed root produced by the last successful call to <see cref="Parse"/>. </summary>
    public CompilationUnit Root { get; private set; } = null!;

    /// <summary> Indicates whether the current token is one of the ordinary declaration modifiers recognized by the grammar. </summary>
    private bool IsCurrentTokenRegularModifier => CurrentToken.MatchingKind is MatchingKeywordKind.Public or MatchingKeywordKind.Private or MatchingKeywordKind.Internal or MatchingKeywordKind.Extern or
                                                   MatchingKeywordKind.Protected or MatchingKeywordKind.Sealed or MatchingKeywordKind.Virtual or MatchingKeywordKind.Static or MatchingKeywordKind.Const or MatchingKeywordKind.Partial or
                                                   MatchingKeywordKind.Unsafe;
    /// <summary> Indicates whether the current token is the contextual <c>intrinsic</c> modifier for an attribute declaration. </summary>
    private bool IsCurrentTokenIntrinsicAttributeModifier => CurrentToken.MatchingKind is MatchingKeywordKind.Intrinsic && IsIntrinsicAttributeModifierAt(current);
    /// <summary> Indicates whether the current token is one of the declaration modifiers recognized by the grammar. </summary>
    private bool IsCurrentTokenModifier => IsCurrentTokenRegularModifier || IsCurrentTokenIntrinsicAttributeModifier;
    /// <summary> Indicates whether the current token starts a bracketed attribute list. </summary>
    private bool IsCurrentTokenAttributeListStart => CurrentToken.Kind is TokenKind.LeftBracket;
    /// <summary> Indicates whether the current token can begin a type declaration. </summary>
    private bool IsCurrentTokenTypeDeclarationStart => CurrentToken.MatchingKind is MatchingKeywordKind.Struct or MatchingKeywordKind.Class or MatchingKeywordKind.Enum or MatchingKeywordKind.Union or MatchingKeywordKind.Interface or MatchingKeywordKind.Attribute;

    /// <summary> Tests whether <c>intrinsic</c> at a given token index is acting as an attribute-only modifier. </summary>
    private bool IsIntrinsicAttributeModifierAt(int tokenIndex)
    {
        if (tokenIndex < 0 || tokenIndex >= tokens.Count || tokens[tokenIndex].MatchingKind is not MatchingKeywordKind.Intrinsic)
            return false;

        int probe = tokenIndex + 1;

        while (probe < tokens.Count)
        {
            MatchingKeywordKind kind = tokens[probe].MatchingKind;

            if (kind is MatchingKeywordKind.Attribute)
                return true;

            if (kind is MatchingKeywordKind.Intrinsic or MatchingKeywordKind.Public or MatchingKeywordKind.Private or MatchingKeywordKind.Internal or MatchingKeywordKind.Extern or MatchingKeywordKind.Unsafe or
                MatchingKeywordKind.Protected or MatchingKeywordKind.Sealed or MatchingKeywordKind.Static or MatchingKeywordKind.Const or MatchingKeywordKind.Partial)
            {
                probe++;
                continue;
            }

            return false;
        }

        return false;
    }

    /// <summary> Controls whether statement parsing should allow a trailing expression result. </summary>
    private enum StatementParseMode : byte
    {
        Normal,
        AllowFinalExpression
    }

    /// <summary> Chooses which source location to use when synthesizing a missing token. </summary>
    private enum MissingTokenAnchor : byte
    {
        BeforeCurrent,
        AfterPrevious
    }

    /// <summary> Builds the shared operator trie once for the parser type. </summary>
    static Parser() => operatorTrie = BuildOperatorTrie();

    /// <summary> Creates a parser over one token stream and shared diagnostics sink. </summary>
    public Parser(SourceText text, DiagnosticsManager diagnostics)
    {
        this.text = text;
        this.diagnostics = diagnostics;
    }

    /// <summary> Parses the tokens into Syntax Tree. This method is in Work-In-Progress and will me modified later to return the Syntax Tree. </summary>
    /// <param name="tokens"> The tokens to parse. </param>
    public CompilationUnit Parse(List<Token> tokens)
    {
        this.tokens = FilterTokens(tokens);
        current = default;

        var compilationUnit = ParseCompilationUnit();
        Root = compilationUnit;
        return compilationUnit;
    }

    /// <summary> Removes bad tokens that the parser should treat only through diagnostics, not syntax structure. </summary>
    /// <param name="sourceTokens">Raw token stream emitted by the lexer.</param>
    /// <returns>Filtered token list safe for parser traversal.</returns>
    private static List<Token> FilterTokens(List<Token> sourceTokens)
    {
        List<Token> filtered = new List<Token>(sourceTokens.Count);

        foreach (var token in sourceTokens)
        {
            if (token.Kind is TokenKind.BadToken)
                continue;

            filtered.Add(token);
        }

        return filtered;
    }

    /// <summary> Recognizes token kinds that can stand in for literal expressions during parsing. </summary>
    private static bool IsLiteralTokenKind(TokenKind kind) =>
        kind is TokenKind.Integer or TokenKind.Float or TokenKind.Char or TokenKind.String;

    /// <summary> Parses the full compilation unit until the synthetic end token is reached. </summary>
    private CompilationUnit ParseCompilationUnit()
    {
        IReadOnlyList<PragmaDirective> pragmas = ParsePragmaDirectives(out bool topLevelStatementsEnabled);
        var topLevels = new List<TopLevel>();

        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            var start = current;
            var topLevel = ParseTopLevel(topLevelStatementsEnabled);
            topLevels.Add(topLevel);
            RecoverTopLevelIfStalled(start);
        }

        var eofToken = Consume();

        return new CompilationUnit(pragmas, topLevels, eofToken);
    }

    /// <summary> Builds the operator trie used by combined-operator lookups. </summary>
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

    /// <summary> Parses the next top-level construct based on the current token's grammar role. </summary>
    private TopLevel ParseTopLevel(bool topLevelStatementsEnabled)
    {
        if (CurrentToken.MatchingKind is MatchingKeywordKind.Namespace)
            return ParseNamespaceDeclaration(topLevelStatementsEnabled);
        else if (CurrentToken.MatchingKind is MatchingKeywordKind.Global)
            return ParseTopLevelGlobalBlock(topLevelStatementsEnabled);
        else if (CurrentToken.Kind is TokenKind.LeftBrace)
            return ParseTopLevelBlock([], [], topLevelStatementsEnabled);
        else if (IsCurrentTokenAttributeListStart || IsCurrentTokenModifier || IsCurrentTokenTypeDeclarationStart)
            return ParseTopLevelDeclaration(topLevelStatementsEnabled);
        else if (CurrentToken.MatchingKind is MatchingKeywordKind.If or MatchingKeywordKind.While or MatchingKeywordKind.Return)
            return ParseTopLevelStatementWithValidation(topLevelStatementsEnabled);
        else if (LooksLikeVariableDeclaration() is (var success, var context) && success)
        {
            if (context is LookaheadResultContext.AmbiguousPointerDeclaration)
                return ParseTopLevelAmbiguousPointerDeclaration();

            if (context is LookaheadResultContext.AmbiguousReferenceDeclaration)
                return ParseTopLevelAmbiguousReferenceDeclaration();

            return ParseTopLevelDeclaration(topLevelStatementsEnabled);
        }

        return ParseTopLevelStatementWithValidation(topLevelStatementsEnabled);
    }

    /// <summary> Parses the next member declaration inside a type body. </summary>
    private Member ParseMember()
    {
        IReadOnlyList<AttributeListSyntax> attributes = ParseAttributeLists();
        var modifiers = ParseModifiers();

        if (CurrentToken.Kind is TokenKind.LeftBrace)
            return ParseMemberBlockDeclaration(attributes, modifiers);
        else if (IsCurrentTokenTypeDeclarationStart)
            return ParseMemberTypeDeclaration(attributes, modifiers);
        else
            return ParseMemberFieldDeclarationOrFunctionOrProperty(attributes, modifiers);
    }

    /// <summary> Parses the next local construct inside a block or function body. </summary>
    private Local ParseLocal(StatementParseMode parseMode = StatementParseMode.Normal)
    {
        if (IsCurrentTokenAttributeListStart || IsCurrentTokenModifier)
            return ParseLocalDeclaration();

        return ParseLocalStatement(parseMode);
    }

    /// <summary> Parses a comma-separated generic type-argument list up to the closing <c>&gt;</c>. </summary>
    private SeparatedSyntaxList<TypeSyntax> ParseTypeArgumentList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();
        bool wasCommaLast = false;

        while (CurrentToken.Kind is not TokenKind.GreaterThanSign and not TokenKind.EndToken)
        {
            nodesAndSeparators.Add(ParseTypeSyntax());
            wasCommaLast = false;

            if (CurrentToken.Kind is TokenKind.Comma)
            {
                nodesAndSeparators.Add(Consume());
                wasCommaLast = true;
            }
            else
                break;
        }

        if (wasCommaLast)
            diagnostics.ReportExpectedType(CurrentToken.Span, GetTokenDisplay(CurrentToken), "after ',' in the type argument list");

        return new SeparatedSyntaxList<TypeSyntax>(nodesAndSeparators);
    }

    /// <summary> Parses one complete generic argument clause, including the surrounding angle brackets. </summary>
    private (Token LessThan, SeparatedSyntaxList<TypeSyntax> TypeArguments, Token GreaterThan) ParseGenerics()
    {
        var lessThan = Consume();
        var typeArguments = ParseTypeArgumentList();
        var greaterThan = ExpectToken(TokenKind.GreaterThanSign, "'>'", "to close the generic argument list");

        return (lessThan, typeArguments, greaterThan);
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

    /// <summary> Consumes one logical operator token, combining adjacent raw tokens when necessary. </summary>
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

    private TopLevelBlock ParseTopLevelBlock(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, bool topLevelStatementsEnabled)
    {
        var openBrace = Consume();
        var members = new List<TopLevel>();

        while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
        {
            var start = current;
            var member = ParseTopLevel(topLevelStatementsEnabled);
            members.Add(member);
            RecoverTopLevelIfStalled(start);
        }
        var closeBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close the top-level block");

        return new TopLevelBlock(attributes, modifiers, openBrace, members, closeBrace);
    }

    private TopLevelGlobalBlock ParseTopLevelGlobalBlock(bool topLevelStatementsEnabled)
    {
        Token globalKeyword = Consume();
        Token openBrace = ExpectToken(TokenKind.LeftBrace, "'{'", "after 'global'");
        List<TopLevel> members = [];

        while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
        {
            int start = current;
            members.Add(ParseTopLevel(topLevelStatementsEnabled));
            RecoverTopLevelIfStalled(start);
        }

        Token closeBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close the global block");
        return new TopLevelGlobalBlock(globalKeyword, openBrace, members, closeBrace);
    }

    private TopLevelStatement ParseTopLevelStatementWithValidation(bool topLevelStatementsEnabled)
    {
        if (!topLevelStatementsEnabled)
            diagnostics.ReportError("MH0011", "Top-level statements require '#pragma toplevel enable' in this file.", CurrentToken.Span);

        return ParseTopLevelStatement();
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
}
