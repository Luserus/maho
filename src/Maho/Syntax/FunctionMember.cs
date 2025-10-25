namespace Maho.Syntax;

internal abstract class FunctionMember : DeclarationSyntax
{
    public FunctionSignature Signature { get; }

    protected FunctionMember(FunctionSignature signature) => Signature = signature;
}