namespace Maho.Syntax;

internal sealed class FunctionMemberDeclaration : MemberDeclaration
{
    public Function Function { get; }

    public FunctionMemberDeclaration(Function function) => Function = function;
}