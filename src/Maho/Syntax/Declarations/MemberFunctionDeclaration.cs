namespace Maho.Syntax;

internal sealed class MemberFunctionDeclaration : Member
{
    public Function Function { get; }

    public MemberFunctionDeclaration(Function function) => Function = function;
}