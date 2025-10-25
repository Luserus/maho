namespace Maho.Syntax;

internal sealed class FunctionDeclaration : FunctionMember
{
    public Token Semicolon { get; }

    public FunctionDeclaration(FunctionSignature signature, Token semicolon) : base(signature)
    {
        Semicolon = semicolon;
    }
}