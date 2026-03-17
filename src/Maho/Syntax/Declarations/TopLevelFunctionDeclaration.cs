namespace Maho.Syntax;

internal sealed class TopLevelFunctionDeclaration : TopLevelDeclaration
{
    public Function Function { get; }

    public TopLevelFunctionDeclaration(Function function) => Function = function;
}