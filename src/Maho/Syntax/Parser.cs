using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Parses the program tokens into Syntax Tree. </summary>
internal sealed partial class Parser
{
    private readonly DiagnosticsManager diagnostics;
    private readonly SourceText text;
    /// <summary> The tokens to parse. </summary>
    private List<Token> tokens = null!;
    /// <summary> Current index of Token being read from the token list. </summary>
    private int current;
    /// <summary> Current Token being read from the token list. </summary>
    private Token CurrentToken => tokens[current];

    public CompilationUnit Root { get; private set; } = null!;

    private bool CurrentTokenIsModifier => CurrentToken.MatchingKind is MatchingKeywordKind.Public or MatchingKeywordKind.Private or MatchingKeywordKind.Internal or MatchingKeywordKind.Extern or
                                            MatchingKeywordKind.Protected or MatchingKeywordKind.Sealed or MatchingKeywordKind.Static or MatchingKeywordKind.Const;
    private bool CurrentTokenIsTypeDeclarationStart => CurrentToken.MatchingKind is MatchingKeywordKind.Struct or MatchingKeywordKind.Class or MatchingKeywordKind.Enum or MatchingKeywordKind.Union or MatchingKeywordKind.Interface;

    private enum StatementParseMode : byte
    {
        Normal,
        AllowFinalExpression
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
        this.tokens = tokens;
        current = default;

        var compilationUnit = ParseCompilationUnit();
        Root = compilationUnit;
    }

    private CompilationUnit ParseCompilationUnit()
    {
        var topLevels = new List<TopLevel>();

        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            var topLevel = ParseTopLevel();
            topLevels.Add(topLevel);
        }

        var eofToken = Consume();

        return new CompilationUnit(topLevels, eofToken);
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
            if (CurrentToken.Kind is not TokenKind.Identifier)
            {
                diagnostics.ReportUnexpectedToken(CurrentToken.Span, CurrentToken.Value);
                break;
            }

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
            diagnostics.ReportUnexpectedToken(CurrentToken.Span, CurrentToken.Value);

        return new SeparatedSyntaxList<TypeSyntax>(nodesAndSeparators);
    }
    
    private (Token LessThan, SeparatedSyntaxList<TypeSyntax> TypeArguments, Token GreaterThan) ParseGenerics()
    {
        var lessThan = Consume();
        var typeArguments = ParseTypeArgumentList();

        Token greaterThan;

        if (CurrentToken.Kind is not TokenKind.GreaterThanSign)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ">");
            greaterThan = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            greaterThan = Consume();

        return (lessThan, typeArguments, greaterThan);
    }
}