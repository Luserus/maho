namespace Maho.Syntax;

internal sealed class LocalFunctionDeclaration : LocalDeclaration
{
    public Function Function { get; }

    public LocalFunctionDeclaration(Function function) => Function = function;
}