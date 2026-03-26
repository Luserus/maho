namespace Maho.Syntax;

internal sealed class FunctionDeclaration : SyntaxNode
{
    public FunctionSignature Signature { get; }
    public FunctionBody Body { get; }

    public FunctionDeclaration(FunctionSignature signature, FunctionBody body)
    {
        Signature = signature;
        Body = body;
    }
}