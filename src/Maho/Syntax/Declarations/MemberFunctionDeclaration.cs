namespace Maho.Syntax;

internal sealed class MemberFunctionDeclaration : Member
{
    public FunctionDeclaration Function { get; }

    public MemberFunctionDeclaration(FunctionDeclaration function) => Function = function;
}