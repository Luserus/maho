using System;
using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Text;

SourceText text = new("var x = 20.2; var y = x + 5; if (y > 20) { y = y - 1; } else { y = y + 1; }");
DiagnosticsManager diagnosticsManager = new();

// Create a new lexer instance with a test code snippet.
Lexer lexer = new(text, diagnosticsManager);

// Lex the program string.
lexer.Lex();
Console.WriteLine(lexer.ToString());

// Store the lexed tokens for Parsing.
var tokens = lexer.Tokens;

Parser parser = new(text, diagnosticsManager);

// Pass the tokens to the parser to parse the tokens into Syntax Tree.
parser.Parse(tokens);