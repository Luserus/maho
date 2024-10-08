using Maho.Syntax;

Lexer lexer = new();
// Pass the program string that is to be lexed and lex the program string.
lexer.Lex("var x = 20;");


// Store the lexed tokens for Parsing.
var tokens = lexer.Tokens;
// Add an EndToken at the end of the list to tell the parser when the final token has been reached.
tokens.Add(new() {Value = string.Empty, LineNumber = tokens[^1].LineNumber + 1, CharNumber = tokens[^1].CharNumber + 1, Kind = TokenKind.EndToken});

Parser parser = new();
// Pass the tokens to the parser to parse the tokens into Syntax Tree.
parser.Parse(tokens);
