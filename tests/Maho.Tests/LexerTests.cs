using Maho.Syntax;

namespace Maho.Tests;

public sealed class LexerTests
{
    [Fact]
    public void Lex_RecognizesKeywordsLiteralsAndEndToken()
    {
        var (_, diagnostics, lexer) = CompilerTestBed.Lex("public intrinsic attribute Marker { public dyn Value { get; set; } public static unsafe var Main(dyn value) { return \"hi\"; } } global");

        Token publicToken = lexer.Tokens.First(token => token.Value == "public");
        Token intrinsicToken = Assert.Single(lexer.Tokens, token => token.Value == "intrinsic");
        Token attributeToken = Assert.Single(lexer.Tokens, token => token.Value == "attribute");
        Token getToken = Assert.Single(lexer.Tokens, token => token.Value == "get");
        Token setToken = Assert.Single(lexer.Tokens, token => token.Value == "set");
        Token staticToken = Assert.Single(lexer.Tokens, token => token.Value == "static");
        Token unsafeToken = Assert.Single(lexer.Tokens, token => token.Value == "unsafe");
        Token varToken = Assert.Single(lexer.Tokens, token => token.Value == "var");
        Token dynToken = lexer.Tokens.First(token => token.Value == "dyn");
        Token globalToken = Assert.Single(lexer.Tokens, token => token.Value == "global");
        Token stringToken = Assert.Single(lexer.Tokens, token => token.Kind is TokenKind.String);

        Assert.Equal(MatchingKeywordKind.Public, publicToken.MatchingKind);
        Assert.Equal(MatchingKeywordKind.Intrinsic, intrinsicToken.MatchingKind);
        Assert.Equal(MatchingKeywordKind.Attribute, attributeToken.MatchingKind);
        Assert.Equal(MatchingKeywordKind.Get, getToken.MatchingKind);
        Assert.Equal(MatchingKeywordKind.Set, setToken.MatchingKind);
        Assert.Equal(MatchingKeywordKind.Static, staticToken.MatchingKind);
        Assert.Equal(MatchingKeywordKind.Unsafe, unsafeToken.MatchingKind);
        Assert.Equal(MatchingKeywordKind.Var, varToken.MatchingKind);
        Assert.Equal(MatchingKeywordKind.Dyn, dynToken.MatchingKind);
        Assert.Equal(MatchingKeywordKind.Global, globalToken.MatchingKind);
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
