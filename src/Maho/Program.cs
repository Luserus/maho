using System;
using Maho.Syntax;
using Maho.Text;

// Create a new lexer instance with a test code snippet.
Lexer lexer = new(new SourceText("var x = 20;"));

// Lex the program string.
lexer.Lex();
Console.WriteLine(lexer.ToString());
Console.ReadLine();

// Store the lexed tokens for Parsing.
var tokens = lexer.Tokens;

Parser parser = new();

// Pass the tokens to the parser to parse the tokens into Syntax Tree.
parser.Parse(tokens);