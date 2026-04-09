namespace Maho.Syntax;

/// <summary> Declaration node for a function signature and its associated body. </summary>
internal sealed class FunctionDeclaration : SyntaxNode
{
    /// <summary> Signature portion of the declaration. </summary>
    public FunctionSignature Signature { get; }
    /// <summary> Body portion of the declaration. </summary>
    public FunctionBody Body { get; }

    /// <summary> Creates one function declaration from its signature and body. </summary>
    public FunctionDeclaration(FunctionSignature signature, FunctionBody body)
    {
        Signature = signature;
        Body = body;
    }
}
