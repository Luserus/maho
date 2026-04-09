using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Parsed function signature, including modifiers, return type, name, and parameters. </summary>
internal sealed class FunctionSignature : SyntaxNode
{
    /// <summary> Modifiers that apply to the function. </summary>
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> Declared return type. </summary>
    public TypeSyntax ReturnType { get; }
    /// <summary> Function name syntax, including any generic parameter list. </summary>
    public NamedSyntax Identifier { get; }
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Ordered parameter list. </summary>
    public SeparatedSyntaxList<Parameter> Parameters { get; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }

    /// <summary> Creates one function signature node. </summary>
    public FunctionSignature(IReadOnlyList<Token> modifiers, TypeSyntax returnType, NamedSyntax identifier, Token openParen, SeparatedSyntaxList<Parameter> parameters, Token closeParen)
    {
        Modifiers = modifiers;
        ReturnType = returnType;
        Identifier = identifier;
        OpenParen = openParen;
        Parameters = parameters;
        CloseParen = closeParen;
    }
}
