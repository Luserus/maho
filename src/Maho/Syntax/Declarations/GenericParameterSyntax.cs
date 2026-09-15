using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary>One declared generic parameter, optionally variadic or constrained to a compile-time value kind.</summary>
internal sealed class GenericParameterSyntax : SyntaxNode
{
    public Token Identifier { get; }
    public IReadOnlyList<Token> Ellipsis { get; }
    public Token? Colon { get; }
    public Token? ValueKindToken { get; }
    public GenericParameterKind Kind { get; }
    public bool IsVariadic => Ellipsis.Count != 0;

    public GenericParameterSyntax(Token identifier, IReadOnlyList<Token> ellipsis, Token? colon, Token? valueKindToken, GenericParameterKind kind)
    {
        Identifier = identifier;
        Ellipsis = ellipsis;
        Colon = colon;
        ValueKindToken = valueKindToken;
        Kind = kind;
    }
}
