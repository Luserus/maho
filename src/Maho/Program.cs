using Maho.Syntax;

Lexer lexer = new();
// Pass the program string that is to be lexed and lex the program string.
lexer.Lex("var x = 20;");


// Store the lexed tokens for Parsing.
var tokens = lexer.Tokens;

Parser parser = new();
// Pass the tokens to the parser to parse the tokens into Syntax Tree.
parser.Parse(tokens);