using System.Collections.Generic;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Lookahead helpers used by the parser to disambiguate grammar shapes without committing input. </summary>
internal sealed partial class Parser
{
    /// <summary> Independent cursor used while speculative parsing walks ahead of the real parser position. </summary>
    private int lookaheadCurrent;
    /// <summary> Token currently under the speculative lookahead cursor. </summary>
    private Token LookaheadCurrentToken => tokens[lookaheadCurrent];

    /// <summary> Explains why a speculative parse succeeded or failed while disambiguating grammar. </summary>
    private enum LookaheadResultContext : byte
    {
        Success,
        MissingDelimeter,
        MissingSeparator,
        FailedParseTypeSyntax,
        FailedParseNamedSyntax,
        IsBinaryOperator,
        AmbiguousCastOrParenthesizedExpression,
        AmbiguousPointerDeclaration,
        AmbiguousReferenceDeclaration
    }

    /// <summary> Checks whether the upcoming tokens form a plausible generic type-argument clause. </summary>
    private (bool Success, LookaheadResultContext Context) LooksLikeGenericArguments(bool fromLookahead = false)
    {
        if (!fromLookahead)
            lookaheadCurrent = current;

        var saved = lookaheadCurrent;

        LookaheadConsume(); // less than '<'

        while (LookaheadCurrentToken.Kind is not TokenKind.GreaterThanSign and not TokenKind.EndToken)
        {
            var (_, success, result) = LookaheadParseTypeSyntax();

            if (!success)
            {
                lookaheadCurrent = saved;
                return (false, LookaheadResultContext.FailedParseTypeSyntax);
            }

            if (LookaheadCurrentToken.Kind is TokenKind.GreaterThanSign)
                break;

            if (LookaheadCurrentToken.Kind is not TokenKind.Comma)
            {
                lookaheadCurrent = saved;
                return (false, LookaheadResultContext.MissingSeparator);
            }

            LookaheadConsume(); // comma ','
        }

        if (LookaheadCurrentToken.Kind is not TokenKind.GreaterThanSign)
        {
            lookaheadCurrent = saved;
            return (false, LookaheadResultContext.MissingDelimeter);
        }

        lookaheadCurrent = saved;
        return (true, LookaheadResultContext.Success);
    }

    /// <summary> Checks whether the upcoming tokens form a plausible generic type-parameter clause. </summary>
    private (bool Success, LookaheadResultContext Context) LooksLikeGenericParameters(bool fromLookahead = false)
    {
        if (!fromLookahead)
            lookaheadCurrent = current;

        var saved = lookaheadCurrent;

        LookaheadConsume(); // less than '<'

        while (LookaheadCurrentToken.Kind is not TokenKind.GreaterThanSign and not TokenKind.EndToken)
        {
            if (LookaheadCurrentToken.Kind is not TokenKind.Identifier)
            {
                lookaheadCurrent = saved;
                return (false, LookaheadResultContext.FailedParseTypeSyntax);
            }
            
            LookaheadConsume(); // identifier

            if (LookaheadCurrentToken.Kind is TokenKind.GreaterThanSign)
                break;

            if (LookaheadCurrentToken.Kind is not TokenKind.Comma)
            {
                lookaheadCurrent = saved;
                return (false, LookaheadResultContext.MissingSeparator);
            }

            LookaheadConsume(); // comma ','
        }

        if (LookaheadCurrentToken.Kind is not TokenKind.GreaterThanSign)
        {
            lookaheadCurrent = saved;
            return (false, LookaheadResultContext.MissingDelimeter);
        }

        lookaheadCurrent = saved;
        return (true, LookaheadResultContext.Success);
    }

    /// <summary> Checks whether the upcoming tokens look like a cast expression rather than grouping parentheses. </summary>
    private (bool Success, LookaheadResultContext Context) LooksLikeCastExpression()
    {
        lookaheadCurrent = current;

        LookaheadConsume(); // Left paren
        var (_, success, result) = LookaheadParseTypeSyntax();

        if (!success)
            return (false, LookaheadResultContext.FailedParseTypeSyntax);

        if (LookaheadCurrentToken.Kind is not TokenKind.RightParen)
            return (false, LookaheadResultContext.MissingDelimeter);

        LookaheadConsume(); // Right paren

        bool castExpressionIsViable = LookaheadCanStartExpression();
        bool parenthesizedExpressionIsViable = LookaheadCanContinueExpression();

        if (castExpressionIsViable && parenthesizedExpressionIsViable)
            return (true, LookaheadResultContext.AmbiguousCastOrParenthesizedExpression);

        if (castExpressionIsViable)
            return (true, LookaheadResultContext.Success);

        if (parenthesizedExpressionIsViable)
            return (false, LookaheadResultContext.IsBinaryOperator);

        return (false, LookaheadResultContext.MissingDelimeter);
    }

    /// <summary> Checks whether the speculative current token can begin an expression. </summary>
    private bool LookaheadCanStartExpression()
    {
        if (LookaheadCurrentToken.Kind is TokenKind.LeftParen or TokenKind.LeftBrace or TokenKind.LeftBracket or TokenKind.Identifier)
            return true;

        if (IsLiteralTokenKind(LookaheadCurrentToken.Kind))
            return true;

        var (kind, length) = LookaheadGetCombinedOperatorData();
        return length > 0 && operatorTable.TryGetValue(kind, out var entry) && entry.IsPrefix;
    }

    /// <summary> Checks whether the speculative current token can continue an already-parsed expression. </summary>
    private bool LookaheadCanContinueExpression()
    {
        if (LookaheadCurrentToken.Kind is TokenKind.LeftParen or TokenKind.LeftBracket or TokenKind.Dot)
            return true;

        var (kind, length) = LookaheadGetCombinedOperatorData();
        return length > 0 && operatorTable.TryGetValue(kind, out var entry) && (entry.IsInfix || entry.IsPostfix);
    }

    /// <summary> Checks whether the upcoming tokens look like a variable declaration. </summary>
    private (bool Success, LookaheadResultContext Context) LooksLikeVariableDeclaration()
    {
        lookaheadCurrent = current;

        var (_, success, result) = LookaheadParseTypeSyntax();

        if (!success)
            return (false, LookaheadResultContext.FailedParseTypeSyntax);

        (_, success) = LookaheadParseNamedSyntax();

        if (!success)
            return (false, LookaheadResultContext.FailedParseNamedSyntax);

        if (LookaheadCurrentToken.Kind is TokenKind.Equals)
            return (true, LookaheadResultContext.Success);
        else if (LookaheadCurrentToken.Kind is TokenKind.Semicolon)
            return (true, result);

        return (false, LookaheadResultContext.MissingDelimeter);
    }

    /// <summary> Consumes the current speculative token and advances the lookahead cursor. </summary>
    private Token LookaheadConsume()
    {
        var currentToken = LookaheadCurrentToken;
        lookaheadCurrent++;
        return currentToken;
    }

    /// <summary> Peeks ahead in the speculative token stream without advancing the cursor. </summary>
    private Token LookaheadPeek(int offset = 1) => lookaheadCurrent + offset < tokens.Count ? tokens[lookaheadCurrent + offset] : tokens[^1];

    /// <summary> Reads the longest combined operator sequence visible from the lookahead cursor. </summary>
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

    /// <summary> Consumes one logical operator token from the speculative stream, combining raw tokens when needed. </summary>
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

    /// <summary> Parses a speculative generic type-argument list without mutating real parser state. </summary>
    private (SeparatedSyntaxList<TypeSyntax> TypeArguments, bool Success) LookaheadParseTypeArgumentList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();
        bool wasCommaLast = false;

        while (LookaheadCurrentToken.Kind is not TokenKind.GreaterThanSign and not TokenKind.EndToken)
        {
            if (LookaheadCurrentToken.Kind is not TokenKind.Identifier)
                return (new SeparatedSyntaxList<TypeSyntax>(nodesAndSeparators), false);

            var (type, success, _) = LookaheadParseTypeSyntax();

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

    /// <summary> Parses one complete speculative generic argument clause, including its angle brackets. </summary>
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

    /// <summary> Speculatively parses type syntax, including postfix modifiers and qualification chains. </summary>
    private (TypeSyntax Type, bool Success, LookaheadResultContext Context) LookaheadParseTypeSyntax()
    {
        var (type, success) = LookaheadParsePrimaryType();

        if (!success)
            return (type, false, LookaheadResultContext.FailedParseTypeSyntax);

        if (LookaheadCurrentToken.Kind is TokenKind.LeftBracket or TokenKind.QuestionMark or TokenKind.Asterisk or TokenKind.Ampersand)
            (type, success) = LookaheadParseModifiedType(type);

        if (!success)
            return (type, false, LookaheadResultContext.FailedParseTypeSyntax);

        if (LookaheadCurrentToken.Kind is TokenKind.Dot)
            (type, success) = LookaheadParseQualifiedType(type);

        if (!success)
            return (type, false, LookaheadResultContext.FailedParseTypeSyntax);

        if (type is ModifiedType modifiedType)
        {
            if (modifiedType.Modifier.Kind is PostfixTypeModifierKind.Pointer)
                return (type, true, LookaheadResultContext.AmbiguousPointerDeclaration);
            else if (modifiedType.Modifier.Kind is PostfixTypeModifierKind.Reference)
                return (type, true, LookaheadResultContext.AmbiguousReferenceDeclaration);
        }

        return (type, true, LookaheadResultContext.Success);
    }

    /// <summary> Speculatively parses the first segment of a type reference before modifiers or qualification. </summary>
    private (TypeSyntax Type, bool Success) LookaheadParsePrimaryType()
    {
        if (LookaheadCurrentToken.Kind is not TokenKind.Identifier)
            return (new SimpleType(LookaheadCurrentToken), false);

        var identifier = LookaheadConsume();

        if (LookaheadCurrentToken.Kind is TokenKind.LessThanSign && LooksLikeGenericArguments(fromLookahead: true).Success)
        {
            var (genericType, success) = LookaheadParseGenericType(identifier);

            if (!success)
                return (genericType, false);

            return (genericType, true);
        }
        else
            return (new SimpleType(identifier), true);
    }

    /// <summary> Speculatively parses a qualified type chain such as <c>A.B</c>. </summary>
    private (QualifiedType Type, bool Success) LookaheadParseQualifiedType(TypeSyntax firstPart)
    {
        var dot = LookaheadConsume();
        var (next, success, _) = LookaheadParseTypeSyntax();

        if (!success)
            return (new QualifiedType(firstPart, dot, next), false);

        return (new QualifiedType(firstPart, dot, next), true);
    }

    /// <summary> Speculatively parses a generic type name after its identifier has already been consumed. </summary>
    private (GenericType Type, bool Success) LookaheadParseGenericType(Token identifier)
    {
        var (lessThan, typeArguments, GreaterThan, success) = LookaheadParseGenerics();

        if (!success)
            return (new GenericType(identifier, lessThan, typeArguments, new Token(text, new TextSpan(LookaheadCurrentToken.Span.Start, 0), TokenKind.MissingToken, [], [])), false);

        return (new GenericType(identifier, lessThan, typeArguments, GreaterThan), true);
    }

    /// <summary> Speculatively parses zero or more postfix type modifiers such as arrays, pointers, references, or optionals. </summary>
    private (TypeSyntax Type, bool Success) LookaheadParseModifiedType(TypeSyntax baseType)
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

        return (type, true);
    }

    /// <summary> Speculatively parses an array type modifier. </summary>
    private (ArrayTypeModifier Type, bool Success) LookaheadParseArrayTypeModifier()
    {
        var openBracket = LookaheadConsume();

        if (LookaheadCurrentToken.Kind is not TokenKind.RightBracket)
            return (new ArrayTypeModifier(openBracket, null, new Token(text, new TextSpan(LookaheadCurrentToken.Span.Start, 0), TokenKind.MissingToken, [], [])), false);

        var closeBracket = LookaheadConsume();

        return (new ArrayTypeModifier(openBracket, null, closeBracket), true);
    }

    /// <summary> Speculatively parses a pointer type modifier. </summary>
    private (PointerTypeModifier Type, bool Success) LookaheadParsePointerTypeModifier()
    {
        var asterisk = LookaheadConsume();
        return (new PointerTypeModifier(asterisk), true);
    }

    /// <summary> Speculatively parses an optional type modifier. </summary>
    private (OptionalTypeModifier Type, bool Success) LookaheadParseOptionalTypeModifier()
    {
        var questionMark = LookaheadConsume();
        return (new OptionalTypeModifier(questionMark), true);
    }

    /// <summary> Speculatively parses a reference type modifier. </summary>
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
            switch (LookaheadCurrentToken.MatchingKind)
            {
                case MatchingKeywordKind.Private:
                case MatchingKeywordKind.Protected:
                case MatchingKeywordKind.Internal:
                case MatchingKeywordKind.Public:
                case MatchingKeywordKind.Static:
                case MatchingKeywordKind.Sealed:
                case MatchingKeywordKind.Unsafe:
                    list.Add(LookaheadConsume());
                    break;

                default:
                    return list;
            }
        }

        return list;
    }

    /// <summary>
    /// Speculatively parses name syntax for constructs that need to distinguish simple names from
    /// generic names before the parser commits to a declaration path.
    /// </summary>
    private (NamedSyntax Type, bool Success) LookaheadParseNamedSyntax()
    {
        if (LookaheadCurrentToken.Kind is not TokenKind.Identifier)
            return (new SimpleName(LookaheadCurrentToken), false);

        Token name = LookaheadConsume();

        if (LookaheadCurrentToken.Kind is TokenKind.LessThanSign && LooksLikeGenericParameters(fromLookahead: true).Success)
            return LookaheadParseGenericName(name);
        else
            return (new SimpleName(name), true);
    }

    /// <summary> Speculatively parses a generic name after its identifier has already been consumed. </summary>
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

    /// <summary> Speculatively parses a generic type-parameter list. </summary>
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
