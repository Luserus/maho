using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Maho.Diagnostics;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Parses the program tokens into Syntax Tree. </summary>
internal sealed partial class Parser
{
    private sealed class OperatorTrieNode
    {
        public Dictionary<char, OperatorTrieNode> Next { get; } = [];
        public TokenKind? Kind { get; set; } = null;
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
    private Token PreviousToken => current > 0 ? tokens[current - 1] : tokens[0];

    public CompilationUnit Root { get; private set; } = null!;

    private bool CurrentTokenIsModifier => CurrentToken.MatchingKind is MatchingKeywordKind.Public or MatchingKeywordKind.Private or MatchingKeywordKind.Internal or MatchingKeywordKind.Extern or
                                            MatchingKeywordKind.Protected or MatchingKeywordKind.Sealed or MatchingKeywordKind.Static or MatchingKeywordKind.Const;
    private bool CurrentTokenIsTypeDeclarationStart => CurrentToken.MatchingKind is MatchingKeywordKind.Struct or MatchingKeywordKind.Class or MatchingKeywordKind.Enum or MatchingKeywordKind.Union or MatchingKeywordKind.Interface;

    private enum StatementParseMode : byte
    {
        Normal,
        AllowFinalExpression
    }

    private enum MissingTokenAnchor : byte
    {
        BeforeCurrent,
        AfterPrevious
    }

    static Parser() => operatorTrie = BuildOperatorTrie();

    public Parser(SourceText text, DiagnosticsManager diagnostics)
    {
        this.text = text;
        this.diagnostics = diagnostics;
    } 

    /// <summary> Parses the tokens into Syntax Tree. This method is in Work-In-Progress and will me modified later to return the Syntax Tree. </summary>
    /// <param name="tokens"> The tokens to parse. </param>
    public void Parse(List<Token> tokens)
    {
        this.tokens = FilterTokens(tokens);
        current = default;

        var compilationUnit = ParseCompilationUnit();
        Root = compilationUnit;
    }

    private List<Token> FilterTokens(List<Token> sourceTokens)
    {
        List<Token> filtered = new(sourceTokens.Count);

        foreach (var token in sourceTokens)
        {
            if (token.Kind is TokenKind.BadToken)
                continue;

            filtered.Add(token);
        }

        return filtered;
    }

    private static bool IsLiteralTokenKind(TokenKind kind) =>
        kind is TokenKind.Integer or TokenKind.Float or TokenKind.Char or TokenKind.String;

    private static bool IsRecoveryBoundary(TokenKind kind) =>
        kind is TokenKind.EndToken or TokenKind.RightParen or TokenKind.RightBracket or TokenKind.RightBrace or TokenKind.Semicolon or TokenKind.Comma;

    private string GetTokenDisplay(Token token) => token.Kind switch
    {
        TokenKind.EndToken => "<end of file>",
        TokenKind.MissingToken => "<missing>",
        _ when string.IsNullOrEmpty(token.Value) => $"<{token.Kind}>",
        _ => token.Value
    };

    private Token CreateMissingToken() => CreateMissingTokenAt(CurrentToken.Span.Start);

    private Token CreateMissingTokenAt(int position) => new(text, new TextSpan(position, 0), TokenKind.MissingToken, [], []);

    private TextSpan GetMissingTokenDiagnosticSpan(MissingTokenAnchor anchor) =>
        anchor switch
        {
            MissingTokenAnchor.BeforeCurrent => CurrentToken.Span,
            MissingTokenAnchor.AfterPrevious => new TextSpan(PreviousToken.Span.End, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unhandled missing token anchor.")
        };

    private int GetMissingTokenPosition(MissingTokenAnchor anchor) =>
        anchor switch
        {
            MissingTokenAnchor.BeforeCurrent => CurrentToken.Span.Start,
            MissingTokenAnchor.AfterPrevious => PreviousToken.Span.End,
            _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unhandled missing token anchor.")
        };

    private MissingTokenAnchor GetClosingTokenAnchor()
    {
        if (current <= 0)
            return MissingTokenAnchor.BeforeCurrent;

        int currentLine = text.GetLineIndex(CurrentToken.Span.Start);
        int previousLine = text.GetLineIndex(PreviousToken.Span.End);

        return currentLine > previousLine
            ? MissingTokenAnchor.AfterPrevious
            : MissingTokenAnchor.BeforeCurrent;
    }

    private void SynchronizeTo(params TokenKind[] stopKinds)
    {
        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            if (Contains(stopKinds, CurrentToken.Kind))
                break;

            Consume();
        }
    }

    private static bool Contains(TokenKind[] kinds, TokenKind kind)
    {
        for (int i = 0; i < kinds.Length; i++)
        {
            if (kinds[i] == kind)
                return true;
        }

        return false;
    }

    private Token RecoverWithMissingToken()
    {
        if (!IsRecoveryBoundary(CurrentToken.Kind))
            Consume();

        return CreateMissingToken();
    }

    private Token ExpectToken(TokenKind expectedKind, string expectedText, string? context = null, MissingTokenAnchor anchor = MissingTokenAnchor.BeforeCurrent)
    {
        if (CurrentToken.Kind is var currentKind && currentKind == expectedKind)
            return Consume();

        diagnostics.ReportExpectedToken(GetMissingTokenDiagnosticSpan(anchor), expectedText, GetTokenDisplay(CurrentToken), context);
        return CreateMissingTokenAt(GetMissingTokenPosition(anchor));
    }

    private Token ExpectClosingToken(TokenKind expectedKind, string expectedText, string? context = null, params TokenKind[] recoveryKinds)
    {
        if (CurrentToken.Kind == expectedKind)
            return Consume();

        // When the recovery token starts on a later line, anchor the missing closer at the end of
        // the previous token so the insertion point stays on the line where the construct started.
        MissingTokenAnchor anchor = GetClosingTokenAnchor();
        TextSpan diagnosticSpan = GetMissingTokenDiagnosticSpan(anchor);
        int missingTokenPosition = GetMissingTokenPosition(anchor);

        diagnostics.ReportExpectedToken(diagnosticSpan, expectedText, GetTokenDisplay(CurrentToken), context);

        if (!Contains(recoveryKinds, CurrentToken.Kind))
            SynchronizeTo([expectedKind, .. recoveryKinds]);

        if (CurrentToken.Kind == expectedKind)
            return Consume();

        return CreateMissingTokenAt(missingTokenPosition);
    }

    private Token ExpectIdentifierToken(string? context = null)
    {
        if (CurrentToken.Kind is TokenKind.Identifier)
            return Consume();

        diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), context);
        return RecoverWithMissingToken();
    }

    private bool CanStartExpression()
    {
        if (CurrentToken.Kind is TokenKind.LeftParen or TokenKind.LeftBrace or TokenKind.LeftBracket or TokenKind.Identifier)
            return true;

        if (IsLiteralTokenKind(CurrentToken.Kind))
            return true;

        var (kind, length) = GetCombinedOperatorData();
        return length > 0 && operatorTable.TryGetValue(kind, out var entry) && entry.IsPrefix;
    }

    private Expression CreateMissingExpression(string? context = null, MissingTokenAnchor anchor = MissingTokenAnchor.BeforeCurrent)
    {
        diagnostics.ReportExpectedExpression(GetMissingTokenDiagnosticSpan(anchor), GetTokenDisplay(CurrentToken), context);

        if (anchor is MissingTokenAnchor.BeforeCurrent)
            return new LiteralExpression(RecoverWithMissingToken());

        return new LiteralExpression(CreateMissingTokenAt(GetMissingTokenPosition(anchor)));
    }

    private Expression ParseExpectedExpression(string? context = null, MissingTokenAnchor anchor = MissingTokenAnchor.BeforeCurrent) =>
        CanStartExpression() ? ParseExpression() : CreateMissingExpression(context, anchor);

    private bool CanStartTopLevelConstruct() =>
        CurrentToken.Kind is TokenKind.EndToken or TokenKind.RightBrace or TokenKind.Semicolon ||
        CurrentToken.MatchingKind is MatchingKeywordKind.Namespace ||
        CurrentTokenIsModifier ||
        CanStartExpression();

    private bool CanStartMemberConstruct() =>
        CurrentToken.Kind is TokenKind.EndToken or TokenKind.RightBrace or TokenKind.Semicolon ||
        CurrentTokenIsModifier ||
        CurrentTokenIsTypeDeclarationStart ||
        CurrentToken.Kind is TokenKind.Identifier;

    private bool CanStartLocalConstruct() =>
        CurrentToken.Kind is TokenKind.EndToken or TokenKind.RightBrace or TokenKind.Semicolon ||
        CurrentTokenIsModifier ||
        CanStartExpression();

    private void SynchronizeConstruct(System.Func<bool> isRecoveryPoint)
    {
        if (CurrentToken.Kind is TokenKind.EndToken)
            return;

        Consume();

        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            if (CurrentToken.Kind is TokenKind.RightBrace)
                return;

            if (CurrentToken.Kind is TokenKind.Semicolon)
            {
                Consume();
                return;
            }

            if (isRecoveryPoint())
                return;

            Consume();
        }
    }

    private void SynchronizeTopLevel() => SynchronizeConstruct(CanStartTopLevelConstruct);
    private void SynchronizeMember() => SynchronizeConstruct(CanStartMemberConstruct);
    private void SynchronizeLocal() => SynchronizeConstruct(CanStartLocalConstruct);

    private void RecoverTopLevelIfStalled(int start)
    {
        if (current == start)
            SynchronizeTopLevel();
    }

    private void RecoverMemberIfStalled(int start)
    {
        if (current == start)
            SynchronizeMember();
    }

    private void RecoverLocalIfStalled(int start)
    {
        if (current == start)
            SynchronizeLocal();
    }

    private CompilationUnit ParseCompilationUnit()
    {
        var topLevels = new List<TopLevel>();

        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            var start = current;
            var topLevel = ParseTopLevel();
            topLevels.Add(topLevel);
            RecoverTopLevelIfStalled(start);
        }

        var eofToken = Consume();

        return new CompilationUnit(topLevels, eofToken);
    }

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

    private TopLevel ParseTopLevel()
    {
        if (CurrentToken.MatchingKind is MatchingKeywordKind.Namespace)
            return ParseNamespaceDeclaration();
        else if (CurrentTokenIsModifier)
            return ParseTopLevelDeclaration();

        return ParseTopLevelStatement();
    }

    private Member ParseMember()
    {
        var modifiers = ParseModifiers();

        if (CurrentTokenIsTypeDeclarationStart)
            return ParseMemberTypeDeclaration(modifiers);
        else
            return ParseMemberFieldDeclarationOrFunction(modifiers);
    }

    private Local ParseLocal(StatementParseMode parseMode = StatementParseMode.Normal)
    {
        if (CurrentTokenIsModifier)
            return ParseLocalDeclaration();
        
        return ParseLocalStatement(parseMode);
    }

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
    
    private (Token LessThan, SeparatedSyntaxList<TypeSyntax> TypeArguments, Token GreaterThan) ParseGenerics()
    {
        var lessThan = Consume();
        var typeArguments = ParseTypeArgumentList();
        var greaterThan = ExpectClosingToken(TokenKind.GreaterThanSign, "'>'", "to close the generic argument list", TokenKind.RightParen, TokenKind.Semicolon, TokenKind.RightBrace, TokenKind.Comma);

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
}
