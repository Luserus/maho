namespace Maho.Syntax;

internal sealed class TopLevelFunctionDeclaration : TopLevelDeclaration
{
    public FunctionDeclaration Function { get; }

    public TopLevelFunctionDeclaration(FunctionDeclaration function) => Function = function;
}