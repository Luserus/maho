namespace Maho.Syntax;

internal readonly struct Function
{
    public FunctionSignature Signature { get; }
    public FunctionBody Body { get; }

    public Function(FunctionSignature signature, FunctionBody body)
    {
        Signature = signature;
        Body = body;
    }
}