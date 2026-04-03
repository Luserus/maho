namespace Maho.Syntax;

internal sealed partial class Lexer
{
    public override string ToString()
    {
        DebugLexerTokenInfo[] tokens = new DebugLexerTokenInfo[Tokens.Count];

        for (int i = 0; i < tokens.Length; i++)
        {
            Token token = Tokens[i];
            tokens[i] = new DebugLexerTokenInfo(
                i,
                token.Kind.ToString(),
                token.Value,
                DebugJson.GetDisplayText(token),
                DebugJson.GetMatchingKind(token.MatchingKind),
                DebugJson.CreateSpan(text, token.Span),
                DebugJson.CreateTrivia(text, token.LeadingTrivia),
                DebugJson.CreateTrivia(text, token.TrailingTrivia));
        }

        return DebugJson.Serialize(new DebugLexerInfo("lexer", tokens.Length, tokens));
    }
}