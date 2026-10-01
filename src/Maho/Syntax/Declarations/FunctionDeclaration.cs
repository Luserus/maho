using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Declaration node for a function signature and its associated body. </summary>
internal sealed class FunctionDeclaration : SyntaxNode
{
    /// <summary>Classifies this function as a constructor, destructor, operator, or ordinary function.</summary>
    public SpecialFunctionKind SpecialKind { get; }
    /// <summary>If SpecialKind is Operator, the parsed operator kind. Null otherwise.</summary>
    public OperatorKind? OperatorKind { get; }
    /// <summary>Operator tokens when this is an operator overload declaration. Empty otherwise.</summary>
    public IReadOnlyList<Token> OperatorTokens { get; }
    /// <summary> Attributes attached to the function declaration. </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    /// <summary> Signature portion of the declaration. </summary>
    public FunctionSignature Signature { get; }
    /// <summary> Body portion of the declaration. </summary>
    public FunctionBody Body { get; }

    /// <summary> Creates one function declaration from its parsed attributes, signature, and body. </summary>
    public FunctionDeclaration(IReadOnlyList<AttributeListSyntax> attributes,
                               FunctionSignature signature, FunctionBody body,
                               SpecialFunctionKind specialKind = SpecialFunctionKind.None,
                               OperatorKind? operatorKind = null,
                               IReadOnlyList<Token>? operatorTokens = null)
    {
        Attributes = attributes;
        Signature = signature;
        Body = body;
        SpecialKind = specialKind;
        OperatorKind = operatorKind;
        OperatorTokens = operatorTokens ?? [];
    }
}