using System.Collections.Generic;
using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    private int lookaheadCurrent;
    private Token LookaheadCurrentToken => tokens[lookaheadCurrent];

    private bool LooksLikeGenericArguments(bool fromLookahead = false)
    {
        if (!fromLookahead)
            lookaheadCurrent = current;

        LookaheadConsume(); // less than '<'

        while (LookaheadCurrentToken.Kind is not (TokenKind.GreaterThanSign or TokenKind.EndToken))
        {
            var (_, success) = LookaheadParseTypeSyntax();

            if (!success)
                return false;

            if (LookaheadCurrentToken.Kind is TokenKind.GreaterThanSign)
                break;

            if (LookaheadCurrentToken.Kind is not TokenKind.Comma)
                return false;

            LookaheadConsume(); // comma ','
        }

        if (LookaheadCurrentToken.Kind is not TokenKind.GreaterThanSign)
            return false;

        return true;
    }

    private bool LooksLikeGenericParameters(bool fromLookahead = false)
    {
        if (!fromLookahead)
            lookaheadCurrent = current;

        LookaheadConsume(); // less than '<'

        while (LookaheadCurrentToken.Kind is not (TokenKind.GreaterThanSign or TokenKind.EndToken))
        {
            if (LookaheadCurrentToken.Kind is not TokenKind.Identifier)
                return false;
            
            LookaheadConsume(); // identifier

            if (LookaheadCurrentToken.Kind is TokenKind.GreaterThanSign)
                break;

            if (LookaheadCurrentToken.Kind is not TokenKind.Comma)
                return false;

            LookaheadConsume(); // comma ','
        }

        if (LookaheadCurrentToken.Kind is not TokenKind.GreaterThanSign)
            return false;

        return true;
    }

    private bool LooksLikeCastExpression()
    {
        if (CurrentToken.Kind is not TokenKind.LeftParen)
            return false;

        lookaheadCurrent = current;

        LookaheadConsume(); // Left parenGreaterThanSign
        var (_, success) = LookaheadParseTypeSyntax();

        if (!success)
            return false;

        if (LookaheadCurrentToken.Kind is not TokenKind.RightParen)
            return false;

        LookaheadConsume(); // Right paren

        var op = LookaheadConsumeOperator();

        if (operatorTable.TryGetValue(op.Kind, out var opEntry) && opEntry.Role is not OperatorRole.Infix and not OperatorRole.Postfix)
            return false;
        else if (op.Kind is TokenKind.LeftParen)
            return true;

        return true;
    }

    private Token LookaheadConsume()
    {
        var currentToken = LookaheadCurrentToken;
        lookaheadCurrent++;
        return currentToken;
    }

    private Token LookaheadPeek(int offset = 1) => lookaheadCurrent + offset < tokens.Count ? tokens[lookaheadCurrent + offset] : tokens[^1];

    private (TokenKind Kind, int Length) LookaheadGetCombinedOperatorData()
    {
        var node = operatorTrie;
        int length = 0;
        TokenKind? foundKind = null;

        // Read ahead using Peek(i), character by character
        for (int i = 0; ; i++)
        {
            var token = LookaheadPeek(i);

            if (token.Kind is TokenKind.EndToken)
                break; // end of tokens

            if (!node.Next.TryGetValue(text[token.Span.Start], out node))
                break; // no further match

            length = i + 1;
            foundKind = node.Kind;
        }
                
        return (foundKind ?? TokenKind.NullToken, length);
    }

    private Token LookaheadConsumeOperator()
    {
        var (kind, length) = LookaheadGetCombinedOperatorData();

        Token first = default!;
        Token token = default!;

        if (length == 0)
            return new Token(text, new TextSpan(LookaheadCurrentToken.Span.Start, 0), TokenKind.NullToken, [], []);

        for (int i = 0; i < length; i++)
        {
            token = LookaheadConsume();

            if (i == 0)
                first = token;
        }

        Token last = token;

        return new Token(text, new TextSpan(first.Span.Start, last.Span.End - first.Span.Start), kind, first.LeadingTrivia, last.TrailingTrivia);
    }

    private (SeparatedSyntaxList<TypeSyntax> TypeArguments, bool Success) LookaheadParseTypeArgumentList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();
        bool wasCommaLast = false;

        while (LookaheadCurrentToken.Kind is not TokenKind.GreaterThanSign and not TokenKind.EndToken)
        {
            if (LookaheadCurrentToken.Kind is not TokenKind.Identifier)
                return (new SeparatedSyntaxList<TypeSyntax>(nodesAndSeparators), false);

            var (type, success) = LookaheadParseTypeSyntax();

            if (!success)
                return (new SeparatedSyntaxList<TypeSyntax>(nodesAndSeparators), false);

            nodesAndSeparators.Add(type);
            wasCommaLast = false;

            if (LookaheadCurrentToken.Kind is TokenKind.Comma)
            {
                nodesAndSeparators.Add(LookaheadConsume());
                wasCommaLast = true;
            }
            else
                break;
        }

        if (wasCommaLast)
            return (new SeparatedSyntaxList<TypeSyntax>(nodesAndSeparators), false);

        return (new SeparatedSyntaxList<TypeSyntax>(nodesAndSeparators), true);
    }
    
    private (Token LessThan, SeparatedSyntaxList<TypeSyntax> TypeArguments, Token GreaterThan, bool Success) LookaheadParseGenerics()
    {
        var lessThan = LookaheadConsume();
        var (typeArguments, success) = LookaheadParseTypeArgumentList();

        if (!success)
        {
            return (lessThan, typeArguments, new Token(text, new TextSpan(LookaheadCurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []), false);
        }

        Token greaterThan;

        if (LookaheadCurrentToken.Kind is not TokenKind.GreaterThanSign)
        {
            return (lessThan, typeArguments, new Token(text, new TextSpan(LookaheadCurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []), false);
        }
        else
            greaterThan = LookaheadConsume();

        return (lessThan, typeArguments, greaterThan, true);
    }

    private (TypeSyntax Type, bool Success) LookaheadParseTypeSyntax()
    {
        var (type, success) = LookaheadParsePrimaryType();

        if (!success)
            return (type, false);

        if (LookaheadCurrentToken.Kind is TokenKind.LeftBracket or TokenKind.QuestionMark or TokenKind.Asterisk or TokenKind.Ampersand)
            (type, success) = LookaheadParseModifiedType(type);

        if (!success)
            return (type, false);

        if (LookaheadCurrentToken.Kind is TokenKind.Dot)
            (type, success) = LookaheadParseQualifiedType(type);

        if (!success)
            return (type, false);

        return (type, true);
    }

    private (TypeSyntax Type, bool Success) LookaheadParsePrimaryType()
    {
        if (LookaheadCurrentToken.Kind is not TokenKind.Identifier)
            return (new SimpleType(LookaheadCurrentToken), false);

        var identifier = LookaheadConsume();

        if (LookaheadCurrentToken.Kind is TokenKind.LessThanSign && LooksLikeGenericArguments())
        {
            var (genericType, success) = LookaheadParseGenericType(identifier);

            if (!success)
                return (genericType, false);

            return (genericType, true);
        }
        else
            return (new SimpleType(identifier), true);
    }

    private (QualifiedType Type, bool Success) LookaheadParseQualifiedType(TypeSyntax firstPart)
    {
        var dot = LookaheadConsume();
        var (next, success) = LookaheadParseTypeSyntax();

        if (!success)
            return (new QualifiedType(firstPart, dot, next), false);

        return (new QualifiedType(firstPart, dot, next), true);
    }

    private (GenericType Type, bool Success) LookaheadParseGenericType(Token identifier)
    {
        var (lessThan, typeArguments, GreaterThan, success) = LookaheadParseGenerics();

        if (!success)
            return (new GenericType(identifier, lessThan, typeArguments, new Token(text, new TextSpan(LookaheadCurrentToken.Span.Start, 0), TokenKind.MissingToken, [], [])), false);

        return (new GenericType(identifier, lessThan, typeArguments, GreaterThan), true);
    }

    private (ModifiedType Type, bool Success) LookaheadParseModifiedType(TypeSyntax baseType)
    {
        TypeSyntax type = baseType;

        while (LookaheadCurrentToken.Kind is TokenKind.LeftBracket or TokenKind.QuestionMark or TokenKind.Asterisk or TokenKind.Ampersand)
        {
            var (modifier, success) = LookaheadCurrentToken.Kind switch
            {
                TokenKind.LeftBracket => ((PostfixTypeModifier, bool))LookaheadParseArrayTypeModifier(),
                TokenKind.QuestionMark => ((PostfixTypeModifier, bool))LookaheadParseOptionalTypeModifier(),
                TokenKind.Asterisk => ((PostfixTypeModifier, bool))LookaheadParsePointerTypeModifier(),
                TokenKind.Ampersand => ((PostfixTypeModifier, bool))LookaheadParseReferenceTypeModifier(),
                _ => throw new System.InvalidOperationException(),
            };

            if (!success)
                return (new ModifiedType(type, modifier), false);

            type = new ModifiedType(type, modifier);
        }

        return ((ModifiedType)type, true);
    }

    private (ArrayTypeModifier Type, bool Success) LookaheadParseArrayTypeModifier()
    {
        var openBracket = LookaheadConsume();

        if (LookaheadCurrentToken.Kind is not TokenKind.RightBracket)
            return (new ArrayTypeModifier(openBracket, null, new Token(text, new TextSpan(LookaheadCurrentToken.Span.Start, 0), TokenKind.MissingToken, [], [])), false);

        var closeBracket = LookaheadConsume();

        return (new ArrayTypeModifier(openBracket, null, closeBracket), true);
    }

    private (PointerTypeModifier Type, bool Success) LookaheadParsePointerTypeModifier()
    {
        var asterisk = LookaheadConsume();
        return (new PointerTypeModifier(asterisk), true);
    }

    private (OptionalTypeModifier Type, bool Success) LookaheadParseOptionalTypeModifier()
    {
        var questionMark = LookaheadConsume();
        return (new OptionalTypeModifier(questionMark), true);
    }

    private (ReferenceTypeModifier Type, bool Success) LookaheadParseReferenceTypeModifier()
    {
        var ampersand = LookaheadConsume();
        return (new ReferenceTypeModifier(ampersand), true);
    }

    /// <summary> Parses a list of modifiers. </summary>
    /// <returns> The modifier list. </returns>
    private IReadOnlyList<Token> LookaheadParseModifiers()
    {
        var list = new List<Token>();

        while (LookaheadCurrentToken.Kind is not TokenKind.EndToken)
        {
            switch (LookaheadCurrentToken.Value)
            {
                case "private":
                case "protected":
                case "internal":
                case "public":
                case "static":
                case "sealed":
                    list.Add(LookaheadConsume());
                    break;

                default:
                    return list;
            }
        }

        return list;
    }

    // Currently unused. Might be useful in the future so kept.
    private (NamedSyntax Type, bool Success) LookaheadParseNamedSyntax()
    {
        Token name = LookaheadConsume();

        if (LookaheadCurrentToken.Kind is TokenKind.LessThanSign && LooksLikeGenericParameters())
            return LookaheadParseGenericName(name);
        else
            return (new SimpleName(name), true);
    }

    private (GenericName Type, bool Success) LookaheadParseGenericName(Token name)
    {
        var lessThan = LookaheadConsume();
        var (typeParameters, success) = LookaheadParseTypeParameterList();

        if (!success)
            return (new GenericName(name, lessThan, new SeparatedSyntaxList<SimpleName>([]), new Token(text, new TextSpan(LookaheadCurrentToken.Span.Start, 0), TokenKind.MissingToken, [], [])), false);

        if (LookaheadCurrentToken.Kind is not TokenKind.GreaterThanSign)
            return (new GenericName(name, lessThan, typeParameters, new Token(text, new TextSpan(LookaheadCurrentToken.Span.Start, 0), TokenKind.MissingToken, [], [])), false);

        var greaterThan = LookaheadConsume();

        return (new GenericName(name, lessThan, typeParameters, greaterThan), true);
    }

    private (SeparatedSyntaxList<SimpleName> Type, bool Success) LookaheadParseTypeParameterList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();
        bool wasCommaLast = false;

        while (LookaheadCurrentToken.Kind is not TokenKind.GreaterThanSign and not TokenKind.EndToken)
        {
            if (LookaheadCurrentToken.Kind is not TokenKind.Identifier)
                return (new SeparatedSyntaxList<SimpleName>(nodesAndSeparators), false);

            nodesAndSeparators.Add(new SimpleName(LookaheadConsume()));
            wasCommaLast = false;

            if (LookaheadCurrentToken.Kind is TokenKind.Comma)
            {
                nodesAndSeparators.Add(LookaheadConsume());
                wasCommaLast = true;
            }
            else
                break;
        }

        if (wasCommaLast)
            return (new SeparatedSyntaxList<SimpleName>(nodesAndSeparators), false);

        return (new SeparatedSyntaxList<SimpleName>(nodesAndSeparators), true);
    }
}