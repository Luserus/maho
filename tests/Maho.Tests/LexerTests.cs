using Maho.Syntax;

namespace Maho.Tests;

public sealed class LexerTests
{
    [Fact]
    public void Lex_RecognizesKeywordsLiteralsAndEndToken()
    {
        var (_, diagnostics, lexer) = CompilerTestBed.Lex("public static var Main(dyn value) { return \"hi\"; }");

        Token publicToken = Assert.Single(lexer.Tokens, token => token.Value == "public");
        Token staticToken = Assert.Single(lexer.Tokens, token => token.Value == "static");
        Token varToken = Assert.Single(lexer.Tokens, token => token.Value == "var");
        Token dynToken = Assert.Single(lexer.Tokens, token => token.Value == "dyn");
        Token stringToken = Assert.Single(lexer.Tokens, token => token.Kind is TokenKind.String);

        Assert.Equal(MatchingKeywordKind.Public, publicToken.MatchingKind);
        Assert.Equal(MatchingKeywordKind.Static, staticToken.MatchingKind);
        Assert.Equal(MatchingKeywordKind.Var, varToken.MatchingKind);
        Assert.Equal(MatchingKeywordKind.Dyn, dynToken.MatchingKind);
        Assert.Equal("\"hi\"", stringToken.Value);
        Assert.Equal(TokenKind.EndToken, lexer.Tokens[^1].Kind);
        Assert.Empty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Lex_ReportsInvalidAndUnterminatedTokens()
    {
        var (_, diagnostics, lexer) = CompilerTestBed.Lex("$\n\"unterminated");

        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.DiagnosticCode == "MH0000");
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.DiagnosticCode == "MH0001");
        Assert.Contains(lexer.Tokens, token => token.Kind is TokenKind.BadToken);
        Assert.Equal(TokenKind.EndToken, lexer.Tokens[^1].Kind);
    }
}
