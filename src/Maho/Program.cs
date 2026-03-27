using System;
using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Text;

SourceFile file = new SourceFile("maho/src/Maho/Test.mh");
SourceText text = new SourceText(file);
DiagnosticsManager diagnosticsManager = new();

// Create a new lexer instance with a test code snippet.
Lexer lexer = new Lexer(text, diagnosticsManager);

// Lex the program string.
lexer.Lex();
Console.WriteLine(lexer.ToString());

// Store the lexed tokens for Parsing.
var tokens = lexer.Tokens;

Parser parser = new Parser(text, diagnosticsManager);

// Pass the tokens to the parser to parse the tokens into Syntax Tree.
parser.Parse(tokens);
var tree = parser.Root;