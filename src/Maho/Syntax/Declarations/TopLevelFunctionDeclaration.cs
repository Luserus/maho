namespace Maho.Syntax;

/// <summary> Top-level wrapper around a function declaration. </summary>
internal sealed class TopLevelFunctionDeclaration : TopLevelDeclaration
{
    /// <summary> Wrapped function declaration. </summary>
    public FunctionDeclaration Function { get; }

    /// <summary> Creates one top-level function declaration wrapper. </summary>
    public TopLevelFunctionDeclaration(FunctionDeclaration function) => Function = function;
}
