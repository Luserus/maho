using System;
using System.Collections.Generic;
using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    private TopLevel ParseTopLevelDeclaration()
    {   
        var modifiers = ParseModifiers();

        if (IsCurrentTokenTypeDeclarationStart)
            return ParseTopLevelTypeDeclaration(modifiers);
        else
            return ParseTopLevelVariableDeclarationOrFunction(modifiers);
    }

    private NamespaceDeclaration ParseNamespaceDeclaration()
    {
        var keyword = Consume();
        var name = ParseNamedSyntax();
        var body = ParseNamespaceBody();

        return new NamespaceDeclaration(keyword, name, body);
    }

    private NamespaceBody ParseNamespaceBody()
    {
        if (CurrentToken.Kind is TokenKind.Semicolon)
            return new NamespaceEmptyBody(Consume());
        else
            return ParseNamespaceBlockBody();
    }

    private NamespaceBlockBody ParseNamespaceBlockBody()
    {
        var members = new List<TopLevel>();
        var openBrace = Consume();

        while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
        {
            var start = current;
            var member = ParseTopLevel();
            members.Add(member);
            RecoverTopLevelIfStalled(start);
        }
        var closeBrace = ExpectClosingToken(TokenKind.RightBrace, "'}'", "to close the namespace body");

        return new NamespaceBlockBody(openBrace, members, closeBrace);
    }

    private TypeDeclaration ParseType(IReadOnlyList<Token> modifiers)
    {
        var keyword = Consume();

        var kind = keyword.MatchingKind switch
        {
            MatchingKeywordKind.Class => TypeKind.Class,
            MatchingKeywordKind.Struct => TypeKind.Struct,
            MatchingKeywordKind.Interface => TypeKind.Interface,
            MatchingKeywordKind.Enum => TypeKind.Enum,
            MatchingKeywordKind.Union => TypeKind.Union,
            _ => throw new ArgumentOutOfRangeException(nameof(keyword), keyword.MatchingKind, "Unhandled type declaration keyword.")
        };

        var name = ParseNamedSyntax();

        TypeBody body;

        if (CurrentToken.Kind is TokenKind.LeftBrace)
            body = ParseTypeBlockBody();
        else if (CurrentToken.Kind is TokenKind.Semicolon)
            body = ParseTypeEmptyBody();
        else
        {
            diagnostics.ReportExpectedBody(CurrentToken.Span, "a type body", GetTokenDisplay(CurrentToken), "after the type declaration");
            body = new TypeEmptyBody(CreateMissingToken());
        }

        return new TypeDeclaration(modifiers, keyword, kind, name, body);
    }

    private TypeBlockBody ParseTypeBlockBody()
    {
        var openBrace = Consume();
        var members = new List<Member>();

        while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
        {
            var start = current;
            var member = ParseMember();
            members.Add(member);
            RecoverMemberIfStalled(start);
        }
        var closeBrace = ExpectClosingToken(TokenKind.RightBrace, "'}'", "to close the type body");

        return new TypeBlockBody(openBrace, members, closeBrace);
    }

    private TypeEmptyBody ParseTypeEmptyBody() => new TypeEmptyBody(Consume());

    private TopLevelTypeDeclaration ParseTopLevelTypeDeclaration(IReadOnlyList<Token> modifiers)
    {
        var type = ParseType(modifiers);
        
        return new TopLevelTypeDeclaration(type);
    }

    private TopLevel ParseTopLevelVariableDeclarationOrFunction(IReadOnlyList<Token> modifiers)
    {
        var type = ParseTypeSyntax();
        var identifier = ParseNamedSyntax();

        if (CurrentToken.Kind is TokenKind.LeftParen)
            return ParseTopLevelFunctionDeclaration(modifiers, type, identifier);
        else
            return ParseTopLevelVariableDeclaration(modifiers, type, identifier);
    }

    private TopLevel ParseTopLevelVariableDeclaration(IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier)
    {
        var declaration = ParseVariableDeclaration(modifiers, type, identifier);
        var semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the top-level variable declaration", MissingTokenAnchor.AfterPrevious);

        return new TopLevelVariableDeclaration(declaration, semicolon);
    }

    private TopLevel ParseTopLevelFunctionDeclaration(IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier)
    {
        var function = ParseFunction(modifiers, type, identifier);

        return new TopLevelFunctionDeclaration(function);
    }

    private MemberTypeDeclaration ParseMemberTypeDeclaration(IReadOnlyList<Token>? modifiers = null)
    {
        modifiers ??= ParseModifiers();
        var type = ParseType(modifiers);
        
        return new MemberTypeDeclaration(type);
    }

    private Member ParseMemberFieldDeclarationOrFunction(IReadOnlyList<Token>? modifiers = null)
    {
        modifiers ??= ParseModifiers();
        var type = ParseTypeSyntax();
        var identifier = ParseNamedSyntax();

        if (CurrentToken.Kind is TokenKind.LeftParen)
            return ParseMemberFunction(modifiers, type, identifier);
        else
            return ParseMemberFieldDeclaration(modifiers, type, identifier);
    }

    private MemberFieldDeclaration ParseMemberFieldDeclaration(IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier)
    {
        var declaration = ParseVariableDeclaration(modifiers, type, identifier);
        var semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the field declaration", MissingTokenAnchor.AfterPrevious);

        return new MemberFieldDeclaration(declaration, semicolon);
    }

    private MemberFunctionDeclaration ParseMemberFunction(IReadOnlyList<Token> modifiers, TypeSyntax returnType, NamedSyntax identifier)
    {
        var function = ParseFunction(modifiers, returnType, identifier);

        return new MemberFunctionDeclaration(function);
    }

    private Local ParseLocalDeclaration()
    {
        var modifiers = ParseModifiers();

        if (IsCurrentTokenTypeDeclarationStart)
            return ParseLocalTypeDeclaration(modifiers);
        else
            return ParseLocalVariableDeclarationStatementOrFunction(modifiers);
    }

    private LocalTypeDeclaration ParseLocalTypeDeclaration(IReadOnlyList<Token>? modifiers = null)
    {
        modifiers ??= ParseModifiers();
        var type = ParseType(modifiers);

        return new LocalTypeDeclaration(type);
    }

    private Local ParseLocalVariableDeclarationStatementOrFunction(IReadOnlyList<Token>? modifiers = null)
    {
        var type = ParseTypeSyntax();
        var identifier = ParseNamedSyntax();

        if (CurrentToken.Kind is TokenKind.LeftParen)
            return ParseLocalFunctionDeclaration(modifiers, type, identifier);
        else
            return ParseLocalVariableDeclarationStatement(modifiers, type, identifier);
    
    }

    private LocalFunctionDeclaration ParseLocalFunctionDeclaration(IReadOnlyList<Token>? modifiers = null, TypeSyntax? type = null, NamedSyntax? identifier = null)
    {
        var function = ParseFunction(modifiers, type, identifier);

        return new LocalFunctionDeclaration(function);
    }

    private VariableDeclaration ParseVariableDeclaration(IReadOnlyList<Token>? modifiers = null, TypeSyntax? type = null, NamedSyntax? firstIdentifier = null)
    {
        modifiers ??= ParseModifiers();
        type ??= ParseTypeSyntax();

        var nodesAndSeparators = new List<SyntaxNode>();
        bool wasCommaLast = false;
        bool parsingFirstDeclarator = firstIdentifier is not null;

        while (CurrentToken.Kind is not TokenKind.EndToken and not TokenKind.Semicolon)
        {
            NamedSyntax identifier;

            if (parsingFirstDeclarator)
            {
                identifier = firstIdentifier!;
                parsingFirstDeclarator = false;
            }
            else
            {
                if (CurrentToken.Kind is not TokenKind.Identifier)
                {
                    diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), "for the variable name");
                    break;
                }

                identifier = ParseNamedSyntax();
            }

            wasCommaLast = false;

            AssignmentClause? initializer = null;

            if (CurrentToken.Kind is TokenKind.Equals)
            {
                var assignmentOp = Consume();
                var initExpr = ParseExpectedExpression("after '=' in the variable initializer", MissingTokenAnchor.AfterPrevious);

                initializer = new AssignmentClause(assignmentOp, initExpr);
            }

            var declarator = new VariableDeclarator(identifier, initializer);

            nodesAndSeparators.Add(declarator);

            if (CurrentToken.Kind is TokenKind.Comma)
            {
                nodesAndSeparators.Add(Consume());
                wasCommaLast = true;
            }
            else
                break;
        }

        if (wasCommaLast)
            diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), "after ',' in the variable declaration");

        var declarators = new SeparatedSyntaxList<VariableDeclarator>(nodesAndSeparators);

        return new VariableDeclaration(modifiers,type, declarators);
    }

    private SeparatedSyntaxList<Parameter> ParseParameterList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();
        bool wasCommaLast = false;

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            var modifiers = ParseModifiers();

            var type = ParseTypeSyntax();
            NamedSyntax identifier;

            if (CurrentToken.Kind is TokenKind.Identifier)
            {
                identifier = ParseNamedSyntax();
            }
            else
            {
                diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), "for the parameter name");
                identifier = new SimpleName(RecoverWithMissingToken());
            }

            AssignmentClause? initializer = null;

            if (CurrentToken.Kind is TokenKind.Equals)
            {
                var assignmentOp = Consume();
                var initExpr = ParseExpectedExpression("after '=' in the parameter default value", MissingTokenAnchor.AfterPrevious);

                initializer = new AssignmentClause(assignmentOp, initExpr);
            }

            var declarator = new ParameterVariableDeclarator(modifiers, type, identifier);
            var variableDecl = new Parameter(declarator, initializer);

            nodesAndSeparators.Add(variableDecl);
            wasCommaLast = false;

            if (CurrentToken.Kind is TokenKind.Comma)
            {
                nodesAndSeparators.Add(Consume());
                wasCommaLast = true;
            }
            else
                break;
        }

        if (wasCommaLast)
            diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), "after ',' in the parameter list");

        return new SeparatedSyntaxList<Parameter>(nodesAndSeparators);
    }

    private FunctionDeclaration ParseFunction(IReadOnlyList<Token>? modifiers = null, TypeSyntax? returnType = null, NamedSyntax? identifier = null)
    {
        modifiers ??= ParseModifiers();
        returnType ??= ParseTypeSyntax();
        identifier ??= ParseNamedSyntax();

        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after the function name");

        var parameters = ParseParameterList();
        var closeParen = ExpectClosingToken(TokenKind.RightParen, "')'", "to close the parameter list", TokenKind.LeftBrace, TokenKind.Semicolon, TokenKind.RightBrace);

        var signature = new FunctionSignature(modifiers, returnType, identifier, openParen, parameters, closeParen);

        var body = ParseFunctionBody();

        return new FunctionDeclaration(signature, body);
    }

    private FunctionBody ParseFunctionBody()
    {
        if (CurrentToken.Kind is TokenKind.LeftBrace)
            return ParseFunctionBlockBody();
        else if (CurrentToken.Kind is TokenKind.Semicolon)
            return ParseFunctionEmptyBody();
        else
        {
            diagnostics.ReportExpectedBody(CurrentToken.Span, "a function body", GetTokenDisplay(CurrentToken), "after the function signature");
            return new FunctionEmptyBody(CreateMissingToken());
        }
    }

    private FunctionBlockBody ParseFunctionBlockBody()
    {
        var openBrace = Consume();
        var locals = new List<Local>();

        while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
        {
            var start = current;
            var local = ParseLocal();
            locals.Add(local);
            RecoverLocalIfStalled(start);
        }

        if (CurrentToken.Kind is not TokenKind.RightBrace)
        {
            diagnostics.ReportExpectedClosingToken(CurrentToken.Span, "'}'", GetTokenDisplay(CurrentToken), "to close the function body");
            SynchronizeTo(TokenKind.RightBrace);
            var closeBrace = CurrentToken.Kind is TokenKind.RightBrace ? Consume() : CreateMissingToken();
            return new FunctionBlockBody(openBrace, locals, closeBrace);
        }
        else
        {
            var closeBrace = Consume();
            return new FunctionBlockBody(openBrace, locals, closeBrace);
        }
    }

    private FunctionEmptyBody ParseFunctionEmptyBody()
    {
        var semicolon = Consume();
        return new FunctionEmptyBody(semicolon);
    }

    private TypeSyntax ParseTypeSyntax()
    {
        var type = ParsePrimaryType();

        if (CurrentToken.Kind is TokenKind.LeftBracket or TokenKind.QuestionMark or TokenKind.Asterisk or TokenKind.Ampersand)
            type = ParseModifiedType(type);

        if (CurrentToken.Kind is TokenKind.Dot)
            type = ParseQualifiedType(type);

        return type;
    }

    private TypeSyntax ParsePrimaryType()
    {
        if (CurrentToken.Kind is not TokenKind.Identifier)
        {
            diagnostics.ReportExpectedType(CurrentToken.Span, GetTokenDisplay(CurrentToken), "for the type name");
            return new SimpleType(RecoverWithMissingToken());
        }

        var identifier = Consume();

        if (CurrentToken.Kind is TokenKind.LessThanSign && LooksLikeGenericArguments().Success)
            return ParseGenericType(identifier);
        else
            return new SimpleType(identifier);
    }

    private QualifiedType ParseQualifiedType(TypeSyntax firstPart)
    {
        var dot = Consume();
        var next = ParseTypeSyntax();

        return new QualifiedType(firstPart, dot, next);
    }

    private GenericType ParseGenericType(Token identifier)
    {
        var (lessThan, typeArguments, GreaterThan) = ParseGenerics();

        return new GenericType(identifier, lessThan, typeArguments, GreaterThan);
    }

    private TypeSyntax ParseModifiedType(TypeSyntax baseType)
    {
        TypeSyntax type = baseType;

        while (CurrentToken.Kind is TokenKind.LeftBracket or TokenKind.QuestionMark or TokenKind.Asterisk or TokenKind.Ampersand)
        {
            PostfixTypeModifier modifier = CurrentToken.Kind switch
            {
                TokenKind.LeftBracket  => ParseArrayTypeModifier(),
                TokenKind.QuestionMark => ParseOptionalTypeModifier(),
                TokenKind.Asterisk     => ParsePointerTypeModifier(),
                TokenKind.Ampersand    => ParseReferenceTypeModifier(),
                _ => throw new System.InvalidOperationException()
            };

            type = new ModifiedType(type, modifier);
        }

        return type;
    }

    private ArrayTypeModifier ParseArrayTypeModifier()
    {
        var openBracket = Consume();
        Expression? size = null;

        if (CurrentToken.Kind is not TokenKind.RightBracket)
            size = ParseExpectedExpression("for the array size", MissingTokenAnchor.AfterPrevious);

        var closeBracket = ExpectClosingToken(TokenKind.RightBracket, "']'", "to close the array type modifier", TokenKind.Semicolon, TokenKind.RightBrace, TokenKind.RightParen, TokenKind.Comma);

        return new ArrayTypeModifier(openBracket, size, closeBracket);
    }

    private PointerTypeModifier ParsePointerTypeModifier()
    {
        var asterisk = Consume();
        return new PointerTypeModifier(asterisk);
    }

    private OptionalTypeModifier ParseOptionalTypeModifier()
    {
        var questionMark = Consume();
        return new OptionalTypeModifier(questionMark);
    }

    private ReferenceTypeModifier ParseReferenceTypeModifier()
    {
        var ampersand = Consume();
        return new ReferenceTypeModifier(ampersand);
    }

    /// <summary> Parses a list of modifiers. </summary>
    /// <returns> The modifier list. </returns>
    private IReadOnlyList<Token> ParseModifiers()
    {
        var list = new List<Token>();

        while (IsCurrentTokenModifier)
            list.Add(Consume());

        return list;
    }

    private NamedSyntax ParseNamedSyntax()
    {
        Token name = ExpectIdentifierToken("for the name");

        if (CurrentToken.Kind is TokenKind.LessThanSign)
            return ParseGenericName(name);
        else
            return new SimpleName(name);
    }

    private GenericName ParseGenericName(Token name)
    {
        var lessThan = Consume();
        var typeParameters = ParseTypeParameterList();
        var greaterThan = ExpectClosingToken(TokenKind.GreaterThanSign, "'>'", "to close the generic parameter list", TokenKind.LeftBrace, TokenKind.LeftParen, TokenKind.LeftBracket);

        return new GenericName(name, lessThan, typeParameters, greaterThan);
    }

    private SeparatedSyntaxList<SimpleName> ParseTypeParameterList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();
        bool wasCommaLast = false;

        while (CurrentToken.Kind is not TokenKind.GreaterThanSign and not TokenKind.EndToken)
        {
            if (CurrentToken.Kind is not TokenKind.Identifier)
            {
                diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), "for the type parameter name");
                break;
            }

            nodesAndSeparators.Add(new SimpleName(Consume()));
            wasCommaLast = false;

            if (CurrentToken.Kind is TokenKind.Comma)
            {
                nodesAndSeparators.Add(Consume());
                wasCommaLast = true;
            }
            else
                break;
        }

        if (wasCommaLast)
            diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), "after ',' in the type parameter list");

        return new SeparatedSyntaxList<SimpleName>(nodesAndSeparators);
    }
}
