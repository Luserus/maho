using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class FunctionSignature : SyntaxNode
{
    public IReadOnlyList<Token> Modifiers { get; }
    public NamedSyntax ReturnType { get; }
    public NamedSyntax Identifier { get; }
    public Token OpenParen { get; }
    public ISeparatedSyntaxList Parameters { get; }
    public Token CloseParen { get; }

    public FunctionSignature(IReadOnlyList<Token> modifiers, NamedSyntax returnType, NamedSyntax identifier, Token openParen, ISeparatedSyntaxList parameters, Token closeParen)
    {
        Modifiers = modifiers;
        ReturnType = returnType;
        Identifier = identifier;
        OpenParen = openParen;
        Parameters = parameters;
        CloseParen = closeParen;
    }
}