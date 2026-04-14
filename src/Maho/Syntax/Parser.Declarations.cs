using System;
using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    private TopLevel ParseTopLevelDeclaration()
    {
        IReadOnlyList<AttributeListSyntax> attributes = ParseAttributeLists();
        var modifiers = ParseModifiers();

        if (IsCurrentTokenTypeDeclarationStart)
            return ParseTopLevelTypeDeclaration(attributes, modifiers);
        else
            return ParseTopLevelVariableDeclarationOrFunction(attributes, modifiers);
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
        var closeBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close the namespace body");

        return new NamespaceBlockBody(openBrace, members, closeBrace);
    }

    private TypeDeclaration ParseType(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers)
    {
        var keyword = Consume();

        var kind = keyword.MatchingKind switch
        {
            MatchingKeywordKind.Attribute => TypeKind.Attribute,
            MatchingKeywordKind.Class => TypeKind.Class,
            MatchingKeywordKind.Struct => TypeKind.Struct,
            MatchingKeywordKind.Interface => TypeKind.Interface,
            MatchingKeywordKind.Enum => TypeKind.Enum,
            MatchingKeywordKind.Union => TypeKind.Union,
            _ => throw new InvalidOperationException("Unhandeled case")
        };

        var name = ParseNamedSyntax();
        TypeBaseClause? baseClause = null;

        if (CurrentToken.Kind is TokenKind.Colon)
            baseClause = ParseTypeBaseClause();

        List<TypeConstraintClause> constraints = [];

        while (CurrentToken.MatchingKind is MatchingKeywordKind.Where)
            constraints.Add(ParseTypeConstraintClause());

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

        return new TypeDeclaration(attributes, modifiers, keyword, kind, name, baseClause, constraints, body);
    }

    /// <summary> Parses every bracketed attribute list attached to the current declaration. </summary>
    private IReadOnlyList<AttributeListSyntax> ParseAttributeLists()
    {
        List<AttributeListSyntax> attributes = [];

        while (IsCurrentTokenAttributeListStart)
            attributes.Add(ParseAttributeList());

        return attributes;
    }

    /// <summary> Parses one bracketed attribute list. </summary>
    private AttributeListSyntax ParseAttributeList()
    {
        Token openBracket = Consume();
        List<SyntaxNode> nodesAndSeparators = [];
        bool wasCommaLast = false;

        while (CurrentToken.Kind is not TokenKind.RightBracket and not TokenKind.EndToken)
        {
            nodesAndSeparators.Add(ParseAttributeApplication());
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
            diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), "after ',' in the attribute list");

        Token closeBracket = ExpectToken(TokenKind.RightBracket, "']'", "to close the attribute list");
        return new AttributeListSyntax(openBracket, new SeparatedSyntaxList<AttributeApplication>(nodesAndSeparators), closeBracket);
    }

    /// <summary> Parses one attribute application, including any constructor arguments. </summary>
    private AttributeApplication ParseAttributeApplication()
    {
        NamedSyntax name = ParseNamedSyntax(allowQualified: true);

        if (CurrentToken.Kind is not TokenKind.LeftParen)
            return new AttributeApplication(name, openParen: null, new SeparatedSyntaxList<Expression>([]), closeParen: null);

        Token openParen = Consume();
        SeparatedSyntaxList<Expression> arguments = ParseExpressionArgumentList();
        Token closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the attribute argument list");
        return new AttributeApplication(name, openParen, arguments, closeParen);
    }

    private TypeBaseClause ParseTypeBaseClause()
    {
        var colon = Consume();
        List<SyntaxNode> nodesAndSeparators = [];
        bool wasCommaLast = false;

        while (CurrentToken.Kind is not TokenKind.LeftBrace and not TokenKind.Semicolon and not TokenKind.EndToken && CurrentToken.MatchingKind is not MatchingKeywordKind.Where)
        {
            if (CurrentToken.Kind is not TokenKind.Identifier)
            {
                diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), "for the type");
                break;
            }

            TypeSyntax type = ParseTypeSyntax();
            nodesAndSeparators.Add(type);
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
            diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), "after ',' in the base type list");

        var baseTypes = new SeparatedSyntaxList<TypeSyntax>(nodesAndSeparators);

        return new TypeBaseClause(colon, baseTypes);
    }

    private TypeConstraintClause ParseTypeConstraintClause()
    {
        var whereKeyword = Consume();

        var typeToken = ExpectIdentifierToken("for constraint type");
        var type = new SimpleName(typeToken);
        var colon = ExpectToken(TokenKind.Colon, "':'", "after the type");

        List<SyntaxNode> nodesAndSeparators = [];
        bool wasCommaLast = false;

        while (CurrentToken.Kind is not TokenKind.LeftBrace and not TokenKind.Semicolon and not TokenKind.EndToken)
        {
            var constraint = ParseTypeConstraint();
            nodesAndSeparators.Add(constraint);
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
            diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), "after ',' in the constraints list");

        var typeConstraints = new SeparatedSyntaxList<TypeConstraint>(nodesAndSeparators);

        return new TypeConstraintClause(whereKeyword, type, colon, typeConstraints);
    }

    private TypeConstraint ParseTypeConstraint() => new TypeTypeConstraint(ParseTypeSyntax());

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
        var closeBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close the type body");

        return new TypeBlockBody(openBrace, members, closeBrace);
    }

    private TypeEmptyBody ParseTypeEmptyBody() => new TypeEmptyBody(Consume());

    private TopLevelTypeDeclaration ParseTopLevelTypeDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers)
    {
        var type = ParseType(attributes, modifiers);
        
        return new TopLevelTypeDeclaration(type);
    }

    private TopLevel ParseTopLevelVariableDeclarationOrFunction(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers)
    {
        var type = ParseTypeSyntax();
        var identifier = ParseNamedSyntax();

        if (CurrentToken.Kind is TokenKind.LeftParen)
            return ParseTopLevelFunctionDeclaration(attributes, modifiers, type, identifier);
        else
            return ParseTopLevelVariableDeclaration(attributes, modifiers, type, identifier);
    }

    private TopLevel ParseTopLevelVariableDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier)
    {
        var declaration = ParseVariableDeclaration(attributes, modifiers, type, identifier);
        var semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the top-level variable declaration", MissingTokenAnchor.AfterPrevious);

        return new TopLevelVariableDeclaration(declaration, semicolon);
    }

    private TopLevel ParseTopLevelFunctionDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier)
    {
        var function = ParseFunction(attributes, modifiers, type, identifier);

        return new TopLevelFunctionDeclaration(function);
    }

    private MemberTypeDeclaration ParseMemberTypeDeclaration(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null)
    {
        attributes ??= ParseAttributeLists();
        modifiers ??= ParseModifiers();
        var type = ParseType(attributes, modifiers);
        
        return new MemberTypeDeclaration(type);
    }

    private Member ParseMemberFieldDeclarationOrFunctionOrProperty(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null)
    {
        attributes ??= ParseAttributeLists();
        modifiers ??= ParseModifiers();
        var type = ParseTypeSyntax();
        var identifier = ParseNamedSyntax();

        if (CurrentToken.Kind is TokenKind.LeftParen)
            return ParseMemberFunction(attributes, modifiers, type, identifier);
        else if (CurrentToken.Kind is TokenKind.LeftBrace)
            return ParseMemberPropertyDeclaration(attributes, modifiers, type, identifier);
        else
            return ParseMemberFieldDeclaration(attributes, modifiers, type, identifier);
    }

    private MemberFieldDeclaration ParseMemberFieldDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier)
    {
        var declaration = ParseVariableDeclaration(attributes, modifiers, type, identifier);
        var semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the field declaration", MissingTokenAnchor.AfterPrevious);

        return new MemberFieldDeclaration(declaration, semicolon);
    }

    private MemberFunctionDeclaration ParseMemberFunction(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, TypeSyntax returnType, NamedSyntax identifier)
    {
        var function = ParseFunction(attributes, modifiers, returnType, identifier);

        return new MemberFunctionDeclaration(function);
    }

    /// <summary> Parses one property declaration inside a type body. </summary>
    private MemberPropertyDeclaration ParseMemberPropertyDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier)
    {
        PropertyAccessorList body = ParsePropertyAccessorList();
        return new MemberPropertyDeclaration(attributes, modifiers, type, identifier, body);
    }

    private Local ParseLocalDeclaration()
    {
        IReadOnlyList<AttributeListSyntax> attributes = ParseAttributeLists();
        var modifiers = ParseModifiers();

        if (IsCurrentTokenTypeDeclarationStart)
            return ParseLocalTypeDeclaration(attributes, modifiers);
        else
            return ParseLocalVariableDeclarationStatementOrFunction(attributes, modifiers);
    }

    private LocalTypeDeclaration ParseLocalTypeDeclaration(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null)
    {
        attributes ??= ParseAttributeLists();
        modifiers ??= ParseModifiers();
        var type = ParseType(attributes, modifiers);

        return new LocalTypeDeclaration(type);
    }

    private Local ParseLocalVariableDeclarationStatementOrFunction(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null)
    {
        attributes ??= ParseAttributeLists();
        var type = ParseTypeSyntax();
        var identifier = ParseNamedSyntax();

        if (CurrentToken.Kind is TokenKind.LeftParen)
            return ParseLocalFunctionDeclaration(attributes, modifiers, type, identifier);
        else
            return ParseLocalVariableDeclarationStatement(attributes, modifiers, type, identifier);
    
    }

    private LocalFunctionDeclaration ParseLocalFunctionDeclaration(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null, TypeSyntax? type = null, NamedSyntax? identifier = null)
    {
        var function = ParseFunction(attributes, modifiers, type, identifier);

        return new LocalFunctionDeclaration(function);
    }

    private VariableDeclaration ParseVariableDeclaration(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null, TypeSyntax? type = null, NamedSyntax? firstIdentifier = null)
    {
        attributes ??= ParseAttributeLists();
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

        return new VariableDeclaration(attributes, modifiers, type, declarators);
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
            diagnostics.ReportExpectedParameter(CurrentToken.Span, GetTokenDisplay(CurrentToken), "after ',' in the parameter list");

        return new SeparatedSyntaxList<Parameter>(nodesAndSeparators);
    }

    private FunctionDeclaration ParseFunction(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null, TypeSyntax? returnType = null, NamedSyntax? identifier = null)
    {
        attributes ??= ParseAttributeLists();
        modifiers ??= ParseModifiers();
        returnType ??= ParseTypeSyntax();
        identifier ??= ParseNamedSyntax();

        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after the function name");

        var parameters = ParseParameterList();
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the parameter list");

        List<TypeConstraintClause> constraints = [];

        while (CurrentToken.MatchingKind is MatchingKeywordKind.Where)
            constraints.Add(ParseTypeConstraintClause());

        var signature = new FunctionSignature(modifiers, returnType, identifier, openParen, parameters, closeParen, constraints);

        var body = ParseFunctionBody();

        return new FunctionDeclaration(attributes, signature, body);
    }

    /// <summary> Parses the accessor list that forms a property body. </summary>
    private PropertyAccessorList ParsePropertyAccessorList()
    {
        Token openBrace = Consume();
        List<PropertyAccessorDeclaration> accessors = [];

        while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
        {
            int start = current;
            accessors.Add(ParsePropertyAccessorDeclaration());
            RecoverMemberIfStalled(start);
        }

        Token closeBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close the property body");
        return new PropertyAccessorList(openBrace, accessors, closeBrace);
    }

    /// <summary> Parses one accessor inside a property body. </summary>
    private PropertyAccessorDeclaration ParsePropertyAccessorDeclaration()
    {
        IReadOnlyList<AttributeListSyntax> attributes = ParseAttributeLists();
        IReadOnlyList<Token> modifiers = ParseModifiers();

        Token keyword;
        PropertyAccessorKind kind;

        if (CurrentToken.MatchingKind is MatchingKeywordKind.Get or MatchingKeywordKind.Set)
        {
            keyword = Consume();
            kind = keyword.MatchingKind is MatchingKeywordKind.Get ? PropertyAccessorKind.Get : PropertyAccessorKind.Set;
        }
        else
        {
            diagnostics.ReportExpectedToken(CurrentToken.Span, "'get' or 'set'", GetTokenDisplay(CurrentToken), "for the property accessor");
            keyword = RecoverWithMissingToken();
            kind = PropertyAccessorKind.Get;
        }

        FunctionBody body = ParseFunctionBody();
        return new PropertyAccessorDeclaration(attributes, modifiers, keyword, kind, body);
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

        var closeBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close the function body");
        return new FunctionBlockBody(openBrace, locals, closeBrace);
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
                _ => throw new InvalidOperationException()
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

        var closeBracket = ExpectToken(TokenKind.RightBracket, "']'", "to close the array type modifier");

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

    private NamedSyntax ParseNamedSyntax(bool allowQualified = false)
    {
        NamedSyntax name = ParseNamedSyntaxPart();

        if (!allowQualified || CurrentToken.Kind is not TokenKind.Dot)
            return name;

        List<SyntaxNode> nodesAndSeparators = [name];

        while (CurrentToken.Kind is TokenKind.Dot)
        {
            nodesAndSeparators.Add(Consume());
            nodesAndSeparators.Add(ParseNamedSyntaxPart());
        }

        return new QualifiedName(new SeparatedSyntaxList<NamedSyntax>(nodesAndSeparators));
    }

    /// <summary> Parses one simple or generic name segment. </summary>
    private NamedSyntax ParseNamedSyntaxPart()
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
        var greaterThan = ExpectToken(TokenKind.GreaterThanSign, "'>'", "to close the generic parameter list");

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
            diagnostics.ReportExpectedTypeParameter(CurrentToken.Span, GetTokenDisplay(CurrentToken), "after ',' in the type parameter list");

        return new SeparatedSyntaxList<SimpleName>(nodesAndSeparators);
    }
}
