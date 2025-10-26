namespace Maho.Syntax;

internal abstract class FunctionMember : MemberDeclaration
{
    public FunctionSignature Signature { get; }

    protected FunctionMember(FunctionSignature signature) => Signature = signature;
}