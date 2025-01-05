using Maho.Syntax;
using Maho.Text;

Lexer lexer = new(new SourceText("var x = 20;"));
// Pass the program string that is to be lexed and lex the program string.
lexer.Lex();


// Store the lexed tokens for Parsing.
var tokens = lexer.Tokens;

Parser parser = new();
// Pass the tokens to the parser to parse the tokens into Syntax Tree.
parser.Parse(tokens);