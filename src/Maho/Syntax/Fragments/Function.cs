namespace Maho.Syntax;

internal sealed class Function : SyntaxNode
{
    public FunctionSignature Signature { get; }
    public FunctionBody Body { get; }

    public Function(FunctionSignature signature, FunctionBody body)
    {
        Signature = signature;
        Body = body;
    }
}