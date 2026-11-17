using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Base initializer invocation such as <c>BaseType(arg1, arg2)</c>. </summary>
internal sealed class BaseInitializer : SyntaxNode
{
    public TypeSyntax BaseType { get; }
    public Token OpenParen { get; }
    public SeparatedSyntaxList<Expression> Arguments { get; }
    public Token CloseParen { get; }

    public BaseInitializer(TypeSyntax baseType, Token openParen, SeparatedSyntaxList<Expression> arguments, Token closeParen)
    {
        BaseType = baseType;
        OpenParen = openParen;
        Arguments = arguments;
        CloseParen = closeParen;
    }
}

