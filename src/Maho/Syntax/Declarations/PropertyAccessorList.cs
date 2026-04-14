using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Brace-delimited property body containing one or more accessors. </summary>
internal sealed class PropertyAccessorList : SyntaxNode
{
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Accessors declared inside the property body. </summary>
    public IReadOnlyList<PropertyAccessorDeclaration> Accessors { get; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    /// <summary> Creates one property accessor list. </summary>
    public PropertyAccessorList(Token openBrace, IReadOnlyList<PropertyAccessorDeclaration> accessors, Token closeBrace)
    {
        OpenBrace = openBrace;
        Accessors = accessors;
        CloseBrace = closeBrace;
    }
}
