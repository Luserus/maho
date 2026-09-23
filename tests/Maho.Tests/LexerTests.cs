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
        var (_, diagnostics, lexer) = CompilerTestBed.Lex("§\n\"unterminated");

        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.DiagnosticCode == "MH0100");
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.DiagnosticCode == "MH0101");
        Assert.Contains(lexer.Tokens, token => token.Kind is TokenKind.BadToken);
        Assert.Equal(TokenKind.EndToken, lexer.Tokens[^1].Kind);
    }

    [Fact]
    public void Lex_SingleLineComment_EmitsSingleLineCommentTriviaWithoutDiagnostics()
    {
        var (text, diagnostics, lexer) = CompilerTestBed.Lex("// this is a comment\nvar x = 10;");

        Assert.Empty(diagnostics.Diagnostics);
        Assert.False(diagnostics.HasErrors);

        Token varToken = Assert.Single(lexer.Tokens, t => t.Kind is TokenKind.Identifier && t.Value == "var");
        Assert.Contains(varToken.LeadingTrivia, trivia => trivia.Kind is SyntaxTriviaKind.SingleLineComment && text.ToString(trivia.Span) == "// this is a comment");
        Assert.Contains(varToken.LeadingTrivia, trivia => trivia.Kind is SyntaxTriviaKind.EndOfLine);
    }

    [Fact]
    public void Lex_SingleLineComment_AtEndOfFileWithoutTrailingNewline()
    {
        var (text, diagnostics, lexer) = CompilerTestBed.Lex("var x = 1; // trailing comment");

        Assert.Empty(diagnostics.Diagnostics);
        Assert.False(diagnostics.HasErrors);

        Token semicolonToken = Assert.Single(lexer.Tokens, t => t.Kind is TokenKind.Semicolon);
        Assert.Contains(semicolonToken.TrailingTrivia, trivia => trivia.Kind is SyntaxTriviaKind.SingleLineComment && text.ToString(trivia.Span) == "// trailing comment");
    }

    [Fact]
    public void Lex_CommentsOnlySource_ProducesOnlyEndTokenWithTrivia()
    {
        var (text, diagnostics, lexer) = CompilerTestBed.Lex("// single line\n/* multi line */\n// trailing");

        Assert.Empty(diagnostics.Diagnostics);
        Assert.False(diagnostics.HasErrors);

        Token endToken = Assert.Single(lexer.Tokens);
        Assert.Equal(TokenKind.EndToken, endToken.Kind);

        Assert.Contains(endToken.LeadingTrivia, trivia => trivia.Kind is SyntaxTriviaKind.SingleLineComment && text.ToString(trivia.Span) == "// single line");
        Assert.Contains(endToken.LeadingTrivia, trivia => trivia.Kind is SyntaxTriviaKind.MultiLineComment && text.ToString(trivia.Span) == "/* multi line */");
        Assert.Contains(endToken.LeadingTrivia, trivia => trivia.Kind is SyntaxTriviaKind.SingleLineComment && text.ToString(trivia.Span) == "// trailing");
    }

    [Fact]
    public void Lex_TerminatedMultiLineComment_EmitsMultiLineCommentTriviaWithoutDiagnostics()
    {
        var (text, diagnostics, lexer) = CompilerTestBed.Lex("/* simple comment */ var x = /* inline */ 1; /* multiline\n comment\n with * and / inside *** */");

        Assert.Empty(diagnostics.Diagnostics);
        Assert.False(diagnostics.HasErrors);

        Token varToken = Assert.Single(lexer.Tokens, t => t.Kind is TokenKind.Identifier && t.Value == "var");
        Assert.Contains(varToken.LeadingTrivia, trivia => trivia.Kind is SyntaxTriviaKind.MultiLineComment && text.ToString(trivia.Span) == "/* simple comment */");

        Token equalsToken = Assert.Single(lexer.Tokens, t => t.Kind is TokenKind.Equals);
        Assert.Contains(equalsToken.TrailingTrivia, trivia => trivia.Kind is SyntaxTriviaKind.MultiLineComment && text.ToString(trivia.Span) == "/* inline */");

        Token semicolonToken = Assert.Single(lexer.Tokens, t => t.Kind is TokenKind.Semicolon);
        Assert.Contains(semicolonToken.TrailingTrivia, trivia => trivia.Kind is SyntaxTriviaKind.MultiLineComment && text.ToString(trivia.Span).StartsWith("/* multiline"));
    }

    [Fact]
    public void Lex_UnterminatedMultiLineComment_AtEof_ReportsDiagnostic()
    {
        var (text, diagnostics, lexer) = CompilerTestBed.Lex("/* unclosed comment");

        Assert.True(diagnostics.HasErrors);
        var diag = Assert.Single(diagnostics.Diagnostics);
        Assert.Equal("MH0104", diag.DiagnosticCode);
        Assert.Equal("Unterminated multi-line comment.", diag.Message);
        Assert.Equal("/* unclosed comment", text.ToString(diag.Span));

        Token endToken = Assert.Single(lexer.Tokens);
        Assert.Equal(TokenKind.EndToken, endToken.Kind);
        Assert.Contains(endToken.LeadingTrivia, trivia => trivia.Kind is SyntaxTriviaKind.MultiLineComment && text.ToString(trivia.Span) == "/* unclosed comment");
    }

    [Fact]
    public void Lex_UnterminatedMultiLineComment_AfterTokens_ReportsDiagnostic()
    {
        var (text, diagnostics, lexer) = CompilerTestBed.Lex("var x = 1;\n/* unclosed multi-line\ncomment");

        Assert.True(diagnostics.HasErrors);
        var diag = Assert.Single(diagnostics.Diagnostics);
        Assert.Equal("MH0104", diag.DiagnosticCode);
        Assert.Equal("Unterminated multi-line comment.", diag.Message);
        Assert.Equal("/* unclosed multi-line\ncomment", text.ToString(diag.Span));

        // Tokens before the comment are intact
        Assert.Contains(lexer.Tokens, t => t.Kind is TokenKind.Identifier && t.Value == "var");
        Assert.Contains(lexer.Tokens, t => t.Kind is TokenKind.Identifier && t.Value == "x");
        Assert.Contains(lexer.Tokens, t => t.Kind is TokenKind.Equals);
        Assert.Contains(lexer.Tokens, t => t.Kind is TokenKind.Integer && t.Value == "1");
        Assert.Contains(lexer.Tokens, t => t.Kind is TokenKind.Semicolon);
        Assert.DoesNotContain(lexer.Tokens, t => t.Kind is TokenKind.BadToken);
        Assert.Equal(TokenKind.EndToken, lexer.Tokens[^1].Kind);
    }

    [Theory]
    [InlineData("/*", 2)]
    [InlineData("/**", 3)]
    [InlineData("/*/", 3)]
    public void Lex_UnterminatedMultiLineComment_MinimalPrefixes_ReportsDiagnostic(string source, int expectedLength)
    {
        var (text, diagnostics, lexer) = CompilerTestBed.Lex(source);

        Assert.True(diagnostics.HasErrors);
        var diag = Assert.Single(diagnostics.Diagnostics);
        Assert.Equal("MH0104", diag.DiagnosticCode);
        Assert.Equal(expectedLength, diag.Span.Length);
        Assert.Equal(source, text.ToString(diag.Span));
        Assert.DoesNotContain(lexer.Tokens, t => t.Kind is TokenKind.BadToken);
    }

    [Fact]
    public void Lex_MinimalTerminatedMultiLineComment_HasNoErrors()
    {
        var (text, diagnostics, lexer) = CompilerTestBed.Lex("/**/");

        Assert.False(diagnostics.HasErrors);
        Assert.Empty(diagnostics.Diagnostics);
        Token endToken = Assert.Single(lexer.Tokens);
        Assert.Contains(endToken.LeadingTrivia, trivia => trivia.Kind is SyntaxTriviaKind.MultiLineComment && text.ToString(trivia.Span) == "/**/");
    }

    [Fact]
    public void Lex_SingleLineComment_SpecialCharacters_HasNoErrors()
    {
        var (text, diagnostics, lexer) = CompilerTestBed.Lex("// special: 🚀 $ @ # % ^ & * ( ) \nvar z = 42;");

        Assert.False(diagnostics.HasErrors);
        Assert.Empty(diagnostics.Diagnostics);
        Token varToken = Assert.Single(lexer.Tokens, t => t.Kind is TokenKind.Identifier && t.Value == "var");
        Assert.Contains(varToken.LeadingTrivia, trivia => trivia.Kind is SyntaxTriviaKind.SingleLineComment && text.ToString(trivia.Span).Contains("🚀"));
    }
}