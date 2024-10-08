using Maho.Syntax;

// Pass the program string that is to be lexed.
Lexer lexer = new("var x = 20;");
// Call the function to lex the string.
lexer.Lex();


// Store the lexed tokens for Parsing.
var tokens = lexer.Tokens;