namespace Maho.Syntax;

internal readonly struct FunctionSignature
{
    public ModifierList Modifiers { get; }
    public NamedSyntax ReturnType { get; }
    public NamedSyntax Identifier { get; }
    public Token OpenParen { get; }
    public ISeparatedSyntaxList Parameters { get; }
    public Token CloseParen { get; }

    public FunctionSignature(ModifierList modifiers, NamedSyntax returnType, NamedSyntax identifier, Token openParen, ISeparatedSyntaxList parameters, Token closeParen)
    {
        Modifiers = modifiers;
        ReturnType = returnType;
        Identifier = identifier;
        OpenParen = openParen;
        Parameters = parameters;
        CloseParen = closeParen;
    }
}