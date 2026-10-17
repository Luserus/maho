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
        Assert.Equal("unterminated multi-line comment", diag.Message);
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
        Assert.Equal("unterminated multi-line comment", diag.Message);
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

    [Theory]
    [InlineData("123", TokenKind.Integer, "123", null, "123")]
    [InlineData("1_000_000", TokenKind.Integer, "1_000_000", null, "1_000_000")]
    [InlineData("0b1010", TokenKind.Integer, "0b1010", null, "0b1010")]
    [InlineData("0B1100_0011", TokenKind.Integer, "0B1100_0011", null, "0B1100_0011")]
    [InlineData("0x1F", TokenKind.Integer, "0x1F", null, "0x1F")]
    [InlineData("0Xdead_beef", TokenKind.Integer, "0Xdead_beef", null, "0Xdead_beef")]
    [InlineData("123i32", TokenKind.SuffixedInteger, "123i32", "i32", "123")]
    [InlineData("123f32", TokenKind.SuffixedInteger, "123f32", "f32", "123")]
    [InlineData("123_i32", TokenKind.SuffixedInteger, "123_i32", "i32", "123")]
    [InlineData("0b1010u8", TokenKind.SuffixedInteger, "0b1010u8", "u8", "0b1010")]
    [InlineData("0b1010_u8", TokenKind.SuffixedInteger, "0b1010_u8", "u8", "0b1010")]
    [InlineData("0x10u32", TokenKind.SuffixedInteger, "0x10u32", "u32", "0x10")]
    [InlineData("0xdead_beef_u64", TokenKind.SuffixedInteger, "0xdead_beef_u64", "u64", "0xdead_beef")]
    [InlineData("0bar", TokenKind.SuffixedInteger, "0bar", "bar", "0")]
    [InlineData("1exp", TokenKind.SuffixedInteger, "1exp", "exp", "1")]
    public void Lex_IntegerAndSuffixedInteger_ProducesExpectedToken(
        string source, TokenKind expectedKind, string expectedValue, string? expectedSuffix, string expectedWithoutSuffix)
    {
        var (_, diagnostics, lexer) = CompilerTestBed.Lex(source);

        Assert.False(diagnostics.HasErrors);
        var token = lexer.Tokens[0];
        Assert.Equal(expectedKind, token.Kind);
        Assert.Equal(expectedValue, token.Value);
        Assert.Equal(expectedSuffix != null, token.HasSuffix);
        Assert.Equal(expectedSuffix, token.Suffix);
        Assert.Equal(expectedWithoutSuffix, token.ValueWithoutSuffix);
    }

    [Theory]
    [InlineData("123.456", TokenKind.Float, "123.456", null, "123.456")]
    [InlineData("1_000.5_00", TokenKind.Float, "1_000.5_00", null, "1_000.5_00")]
    [InlineData(".5", TokenKind.Float, ".5", null, ".5")]
    [InlineData("1e10", TokenKind.Float, "1e10", null, "1e10")]
    [InlineData("1E10", TokenKind.Float, "1E10", null, "1E10")]
    [InlineData("1e+10", TokenKind.Float, "1e+10", null, "1e+10")]
    [InlineData("1e-10", TokenKind.Float, "1e-10", null, "1e-10")]
    [InlineData("1.5e-3", TokenKind.Float, "1.5e-3", null, "1.5e-3")]
    [InlineData("1_000e1_0", TokenKind.Float, "1_000e1_0", null, "1_000e1_0")]
    [InlineData("1.5f32", TokenKind.SuffixedFloat, "1.5f32", "f32", "1.5")]
    [InlineData("1.5_f32", TokenKind.SuffixedFloat, "1.5_f32", "f32", "1.5")]
    [InlineData("1e10f32", TokenKind.SuffixedFloat, "1e10f32", "f32", "1e10")]
    [InlineData("1.5e-3_f64", TokenKind.SuffixedFloat, "1.5e-3_f64", "f64", "1.5e-3")]
    [InlineData(".5f32", TokenKind.SuffixedFloat, ".5f32", "f32", ".5")]
    public void Lex_FloatAndSuffixedFloat_ProducesExpectedToken(
        string source, TokenKind expectedKind, string expectedValue, string? expectedSuffix, string expectedWithoutSuffix)
    {
        var (_, diagnostics, lexer) = CompilerTestBed.Lex(source);

        Assert.False(diagnostics.HasErrors);
        var token = lexer.Tokens[0];
        Assert.Equal(expectedKind, token.Kind);
        Assert.Equal(expectedValue, token.Value);
        Assert.Equal(expectedSuffix != null, token.HasSuffix);
        Assert.Equal(expectedSuffix, token.Suffix);
        Assert.Equal(expectedWithoutSuffix, token.ValueWithoutSuffix);
    }

    [Theory]
    [InlineData("'a'", TokenKind.Char, "'a'", null, "'a'")]
    [InlineData("'a'u8", TokenKind.SuffixedChar, "'a'u8", "u8", "'a'")]
    [InlineData("'x'_custom", TokenKind.SuffixedChar, "'x'_custom", "_custom", "'x'")]
    [InlineData("\"hello\"", TokenKind.String, "\"hello\"", null, "\"hello\"")]
    [InlineData("\"hello\"s", TokenKind.SuffixedString, "\"hello\"s", "s", "\"hello\"")]
    [InlineData("\"world\"_custom", TokenKind.SuffixedString, "\"world\"_custom", "_custom", "\"world\"")]
    public void Lex_QuotedLiteralsAndSuffixedQuotedLiterals_ProducesExpectedToken(
        string source, TokenKind expectedKind, string expectedValue, string? expectedSuffix, string expectedWithoutSuffix)
    {
        var (_, diagnostics, lexer) = CompilerTestBed.Lex(source);

        Assert.False(diagnostics.HasErrors);
        var token = lexer.Tokens[0];
        Assert.Equal(expectedKind, token.Kind);
        Assert.Equal(expectedValue, token.Value);
        Assert.Equal(expectedSuffix != null, token.HasSuffix);
        Assert.Equal(expectedSuffix, token.Suffix);
        Assert.Equal(expectedWithoutSuffix, token.ValueWithoutSuffix);
    }

    [Fact]
    public void Lex_WhitespaceBetweenLiteralAndIdentifier_DoesNotLexAsSuffix()
    {
        var (_, diagnostics, lexer) = CompilerTestBed.Lex("123 f32 \"hello\" s");

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Integer, lexer.Tokens[0].Kind);
        Assert.Equal("123", lexer.Tokens[0].Value);
        Assert.Equal(TokenKind.Identifier, lexer.Tokens[1].Kind);
        Assert.Equal("f32", lexer.Tokens[1].Value);
        Assert.Equal(TokenKind.String, lexer.Tokens[2].Kind);
        Assert.Equal("\"hello\"", lexer.Tokens[2].Value);
        Assert.Equal(TokenKind.Identifier, lexer.Tokens[3].Kind);
        Assert.Equal("s", lexer.Tokens[3].Value);
    }

    [Theory]
    [InlineData("π")]                   // Greek small letter pi (Ll)
    [InlineData("α_β")]                 // Greek letters with underscore
    [InlineData("привет")]              // Cyrillic letters (Ll)
    [InlineData("номер_1")]             // Cyrillic letters with digit continue
    [InlineData("变量")]                // CJK Ideographs (Lo)
    [InlineData("이름")]                // Hangul syllables (Lo)
    [InlineData("متغير")]              // Arabic letters (Lo)
    [InlineData("Ⅻ")]                   // LetterNumber (Nl) - Roman Numeral
    [InlineData("Ⅻ_factor")]            // LetterNumber start with underscore and Latin
    [InlineData("café")]                // Latin with small letter e with acute
    [InlineData("e\u0301")]              // Latin with combining acute accent (Mn)
    [InlineData("foo‿bar")]             // Undertie connector punctuation (Pc) U+203F
    [InlineData("𝒳")]                   // Mathematical script capital X (Lu, surrogate pair)
    [InlineData("_private123")]         // ASCII underscore + digits
    public void Lex_UnicodeStandardAnnex31_ValidIdentifiers_LexAsIdentifier(string identifier)
    {
        var (_, diagnostics, lexer) = CompilerTestBed.Lex(identifier);

        Assert.False(diagnostics.HasErrors);
        var token = Assert.Single(lexer.Tokens, t => t.Kind is not TokenKind.EndToken);
        Assert.Equal(TokenKind.Identifier, token.Kind);
        Assert.Equal(identifier, token.Value);
    }

    [Fact]
    public void Lex_UnicodeStandardAnnex31_UnicodeSuffixesOnLiterals_Succeeds()
    {
        var (_, diagnostics, lexer) = CompilerTestBed.Lex("123_µs 456π 789_度 \"hello\"_мир");

        Assert.False(diagnostics.HasErrors);

        // 123_µs
        Assert.Equal(TokenKind.SuffixedInteger, lexer.Tokens[0].Kind);
        Assert.Equal("123_µs", lexer.Tokens[0].Value);
        Assert.Equal("µs", lexer.Tokens[0].Suffix);
        Assert.Equal("123", lexer.Tokens[0].ValueWithoutSuffix);

        // 456π
        Assert.Equal(TokenKind.SuffixedInteger, lexer.Tokens[1].Kind);
        Assert.Equal("456π", lexer.Tokens[1].Value);
        Assert.Equal("π", lexer.Tokens[1].Suffix);
        Assert.Equal("456", lexer.Tokens[1].ValueWithoutSuffix);

        // 789_度
        Assert.Equal(TokenKind.SuffixedInteger, lexer.Tokens[2].Kind);
        Assert.Equal("789_度", lexer.Tokens[2].Value);
        Assert.Equal("度", lexer.Tokens[2].Suffix);
        Assert.Equal("789", lexer.Tokens[2].ValueWithoutSuffix);

        // "hello"_мир
        Assert.Equal(TokenKind.SuffixedString, lexer.Tokens[3].Kind);
        Assert.Equal("\"hello\"_мир", lexer.Tokens[3].Value);
        Assert.Equal("_мир", lexer.Tokens[3].Suffix);
        Assert.Equal("\"hello\"", lexer.Tokens[3].ValueWithoutSuffix);
    }
}