using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Declaration node for a function signature and its associated body. </summary>
internal sealed class FunctionDeclaration : SyntaxNode
{
    /// <summary> Attributes attached to the function declaration. </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    /// <summary> Signature portion of the declaration. </summary>
    public FunctionSignature Signature { get; }
    /// <summary> Body portion of the declaration. </summary>
    public FunctionBody Body { get; }

    /// <summary> Creates one function declaration from its parsed attributes, signature, and body. </summary>
    public FunctionDeclaration(IReadOnlyList<AttributeListSyntax> attributes, FunctionSignature signature, FunctionBody body)
    {
        Attributes = attributes;
        Signature = signature;
        Body = body;
    }
}
