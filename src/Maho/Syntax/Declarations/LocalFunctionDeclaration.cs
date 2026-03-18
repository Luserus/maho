namespace Maho.Syntax;

internal sealed class LocalFunctionDeclaration : LocalDeclaration
{
    public FunctionDeclaration Function { get; }

    public LocalFunctionDeclaration(FunctionDeclaration function) => Function = function;
}