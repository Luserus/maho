namespace Maho.Syntax;

/// <summary> Member declaration that introduces a nested function. </summary>
internal sealed class MemberFunctionDeclaration : Member
{
    /// <summary> Nested function declaration. </summary>
    public FunctionDeclaration Function { get; }

    /// <summary> Creates one member-function declaration node. </summary>
    public MemberFunctionDeclaration(FunctionDeclaration function) => Function = function;
}
