namespace Maho.Syntax;

/// <summary> Local declaration that introduces a nested function. </summary>
internal sealed class LocalFunctionDeclaration : LocalDeclaration
{
    /// <summary> Nested function declaration. </summary>
    public FunctionDeclaration Function { get; }

    /// <summary> Creates one local function declaration node. </summary>
    public LocalFunctionDeclaration(FunctionDeclaration function) => Function = function;
}
