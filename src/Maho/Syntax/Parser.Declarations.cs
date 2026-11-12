using System;
using System.Collections.Generic;
using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    private TopLevelDeclaration ParseTopLevelDeclaration(bool topLevelStatementsEnabled)
    {
        IReadOnlyList<AttributeListSyntax> attributes = ParseAttributeLists();
        var modifiers = ParseModifiers();

        if (CurrentToken.MatchingKind is MatchingKeywordKind.Literal)
            return new TopLevelFunctionDeclaration(ParseLiteralFunctionDeclaration(attributes, modifiers));
        else if (CurrentToken.MatchingKind is MatchingKeywordKind.Macro)
            return new TopLevelMacroDeclaration(ParseMacroDeclaration(attributes, modifiers));
        else if (CurrentToken.Kind is TokenKind.Dollar)
            return ParseTopLevelMacroInvocationDeclaration();
        else if (CurrentToken.Kind is TokenKind.LeftBrace)
            return ParseTopLevelBlock(null, topLevelStatementsEnabled);
        else if (CurrentToken.MatchingKind is MatchingKeywordKind.Attribute)
            return ParseTopLevelAttributeDeclaration(attributes, modifiers);
        else if (IsCurrentTokenTypeDeclarationStart)
            return ParseTopLevelTypeDeclaration(attributes, modifiers);
        else if (CurrentToken.MatchingKind is MatchingKeywordKind.Func && Peek().Kind is not TokenKind.LeftParen)
            return new TopLevelFunctionDeclaration(ParseFunction(attributes, modifiers));
        else
            return ParseTopLevelVariableDeclaration(attributes, modifiers);
    }

    private NamespaceDeclaration ParseNamespaceDeclaration(bool topLevelStatementsEnabled)
    {
        var keyword = Consume();
        var name = ParseNamedSyntax(allowQualified: true, allowGenericName: false);
        var body = ParseNamespaceBody(topLevelStatementsEnabled);

        return new NamespaceDeclaration(keyword, name, body);
    }

    private AliasDeclaration ParseAliasDeclaration()
    {
        Token keyword = Consume();
        NamedSyntax name = ParseNamedSyntax(allowQualified: true, allowGenericName: true);
        List<TypeConstraintClause> constraints = [];

        while (CurrentToken.MatchingKind is MatchingKeywordKind.Where)
            constraints.Add(ParseTypeConstraintClause());

        Token equals = ExpectToken(TokenKind.Equals, "'='", "after the alias name or constraints");
        TypeSyntax target = ParseTypeSyntax();
        Token semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the alias target", MissingTokenAnchor.AfterPrevious);

        return new AliasDeclaration(keyword, name, constraints, equals, target, semicolon);
    }

    private TopLevelAliasDeclaration ParseTopLevelAliasDeclaration() => new(ParseAliasDeclaration());
    private MemberAliasDeclaration ParseMemberAliasDeclaration() => new(ParseAliasDeclaration());
    private LocalAliasDeclaration ParseLocalAliasDeclaration() => new(ParseAliasDeclaration());

    private NamespaceBody ParseNamespaceBody(bool topLevelStatementsEnabled)
    {
        if (CurrentToken.Kind is TokenKind.Semicolon)
            return new NamespaceEmptyBody(Consume());
        else
            return ParseNamespaceBlockBody(topLevelStatementsEnabled);
    }

    private NamespaceBlockBody ParseNamespaceBlockBody(bool topLevelStatementsEnabled)
    {
        var openBrace = Consume();
        var directives = new List<Directive>();
        var members = new List<TopLevel>();

        while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
        {
            if (IsUsingDirective())
            {
                directives.Add(ParseUsingDirective());
                continue;
            }

            var start = current;
            var member = ParseTopLevel(topLevelStatementsEnabled);
            members.Add(member);
            RecoverTopLevelIfStalled(start);
        }
        var closeBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close the namespace body");

        return new NamespaceBlockBody(openBrace, directives, members, closeBrace);
    }

    private TypeDeclaration ParseType(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers)
    {
        var keyword = Consume();

        var kind = keyword.MatchingKind switch
        {
            MatchingKeywordKind.Class => TypeKind.Class,
            MatchingKeywordKind.Struct => TypeKind.Struct,
            MatchingKeywordKind.Interface => TypeKind.Interface,
            MatchingKeywordKind.Enum => TypeKind.Enum,
            MatchingKeywordKind.Union => TypeKind.Union,
            _ => throw new InvalidOperationException("Unhandeled case")
        };

        var name = ParseNamedSyntax(allowQualified: true, allowGenericQualifiedParts: false);
        TypeSyntax? discriminantType = null;
        SeparatedSyntaxList<TypeSyntax>? unionConstituents = null;

        if (kind == TypeKind.Enum && CurrentToken.MatchingKind is MatchingKeywordKind.With)
        {
            Consume(); // consume 'with'
            discriminantType = ParseTypeSyntax();
        }
        else if (kind == TypeKind.Union && CurrentToken.Kind is TokenKind.LeftParen)
        {
            unionConstituents = ParseUnionConstituentList();
        }

        TypeBaseClause? baseClause = null;

        if (CurrentToken.Kind is TokenKind.Colon)
            baseClause = ParseTypeBaseClause();

        List<TypeConstraintClause> constraints = [];

        while (CurrentToken.MatchingKind is MatchingKeywordKind.Where)
            constraints.Add(ParseTypeConstraintClause());

        TypeBody body;

        if (CurrentToken.Kind is TokenKind.LeftBrace)
            body = kind == TypeKind.Enum ? ParseEnumTypeBlockBody() : ParseTypeBlockBody();
        else if (CurrentToken.Kind is TokenKind.Semicolon)
            body = ParseTypeEmptyBody();
        else
        {
            diagnostics.ReportExpectedBody(CurrentToken.Span, "a type body", GetTokenDisplay(CurrentToken), "after the type declaration");
            body = new TypeEmptyBody(CreateMissingToken());
        }

        return new TypeDeclaration(attributes, modifiers, keyword, kind, name, baseClause, constraints, body, discriminantType, unionConstituents);
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

        while (CurrentToken.Kind is not TokenKind.RightBracket and not TokenKind.EndToken)
        {
            nodesAndSeparators.Add(ParseAttributeApplication());

            if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        Token closeBracket = ExpectToken(TokenKind.RightBracket, "']'", "to close the attribute list");
        return new AttributeListSyntax(openBracket, new SeparatedSyntaxList<AttributeApplication>(nodesAndSeparators), closeBracket);
    }

    /// <summary> Parses one attribute application, including any constructor arguments. </summary>
    private AttributeApplication ParseAttributeApplication()
    {
        Token? dollarToken = null;
        if (CurrentToken.Kind is TokenKind.Dollar)
            dollarToken = Consume();

        NamedSyntax name = ParseNamedSyntax(allowQualified: true);

        if (CurrentToken.Kind is not TokenKind.LeftParen)
            return new AttributeApplication(dollarToken, name, openParen: null, new SeparatedSyntaxList<Expression>([]), closeParen: null);

        Token openParen = Consume();
        SeparatedSyntaxList<Expression> arguments = ParseExpressionArgumentList();
        Token closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the attribute argument list");

        return new AttributeApplication(dollarToken, name, openParen, arguments, closeParen);
    }

    private TypeBaseClause ParseTypeBaseClause()
    {
        var colon = Consume();
        List<SyntaxNode> nodesAndSeparators = [];

        while (CurrentToken.Kind is not TokenKind.LeftBrace and not TokenKind.Semicolon and not TokenKind.EndToken && CurrentToken.MatchingKind is not MatchingKeywordKind.Where)
        {
            if (CurrentToken.Kind is not TokenKind.Identifier)
            {
                diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), "for the type");
                break;
            }

            TypeSyntax type = ParseTypeSyntax();
            nodesAndSeparators.Add(type);

            if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

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

        while (CurrentToken.Kind is not TokenKind.LeftBrace and not TokenKind.Semicolon and not TokenKind.Equals and not TokenKind.EndToken &&
               CurrentToken.MatchingKind is not MatchingKeywordKind.Where)
        {
            var constraint = ParseTypeConstraint();
            nodesAndSeparators.Add(constraint);

            if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

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

    private TypeEnumBody ParseEnumTypeBlockBody()
    {
        var openBrace = Consume();
        var variants = new List<EnumVariant>();
        var members = new List<Member>();

        while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
        {
            if (IsCurrentTokenModifier || LooksLikeEnumMemberDeclaration())
                break;

            var variantAttrs = ParseAttributeLists();
            if (CurrentToken.Kind is not TokenKind.Identifier)
                break;

            var variantName = ParseNamedSyntax();
            EnumVariant variant;

            if (CurrentToken.Kind is TokenKind.LeftParen)
            {
                var openParen = Consume();
                var parameters = ParseParameterList();
                var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the tuple variant parameters");
                Token? equalsToken = null;
                Expression? explicitValue = null;
                if (CurrentToken.Kind is TokenKind.Equals)
                {
                    equalsToken = Consume();
                    explicitValue = ParseExpression();
                }
                variant = new EnumTupleVariant(variantAttrs, variantName, openParen, parameters, closeParen, equalsToken, explicitValue);
            }
            else if (CurrentToken.Kind is TokenKind.LeftBrace)
            {
                var openVariantBrace = Consume();
                var fields = new List<MemberFieldDeclaration>();
                while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
                {
                    var fieldAttrs = ParseAttributeLists();
                    var fieldModifiers = ParseModifiers();
                    var fieldType = ParseTypeSyntax();
                    var fieldName = ParseNamedSyntax();
                    var semicolon = ExpectToken(TokenKind.Semicolon, ";", "after struct variant field");
                    fields.Add(new MemberFieldDeclaration(new VariableDeclaration(fieldAttrs, fieldModifiers, fieldType, new SeparatedSyntaxList<VariableDeclarator>([new VariableDeclarator(fieldName, null)])), semicolon));
                }
                var closeVariantBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close struct variant body");
                Token? equalsToken = null;
                Expression? explicitValue = null;
                if (CurrentToken.Kind is TokenKind.Equals)
                {
                    equalsToken = Consume();
                    explicitValue = ParseExpression();
                }
                variant = new EnumStructVariant(variantAttrs, variantName, openVariantBrace, fields, closeVariantBrace, equalsToken, explicitValue);
            }
            else
            {
                Token? equalsToken = null;
                Expression? explicitValue = null;
                if (CurrentToken.Kind is TokenKind.Equals)
                {
                    equalsToken = Consume();
                    explicitValue = ParseExpression();
                }
                variant = new EnumUnitVariant(variantAttrs, variantName, equalsToken, explicitValue);
            }

            variants.Add(variant);

            if (CurrentToken.Kind is TokenKind.Comma)
                Consume();
            else if (CurrentToken.Kind is TokenKind.Semicolon)
                Consume();
        }

        while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
        {
            var start = current;
            var member = ParseMember();
            members.Add(member);
            RecoverMemberIfStalled(start);
        }

        var closeBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close the enum body");
        return new TypeEnumBody(openBrace, variants, members, closeBrace);
    }

    private bool LooksLikeEnumMemberDeclaration()
    {
        if (IsCurrentTokenModifier)
            return true;

        if (CurrentToken.Kind is TokenKind.Identifier && Peek().Kind is TokenKind.Identifier)
            return true;

        return false;
    }

    private SeparatedSyntaxList<TypeSyntax> ParseUnionConstituentList()
    {
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "for union constituent types");
        var nodesAndSeparators = new List<SyntaxNode>();

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            var type = ParseTypeSyntax();
            nodesAndSeparators.Add(type);

            if (CurrentToken.Kind is TokenKind.VerticalBar)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        ExpectToken(TokenKind.RightParen, "')'", "to close union constituent types");
        return new SeparatedSyntaxList<TypeSyntax>(nodesAndSeparators);
    }


    private TopLevelAttributeDeclaration ParseTopLevelAttributeDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers)
    {
        var attribute = ParseAttributeSignature(attributes, modifiers);
        var semicolon = ExpectToken(TokenKind.Semicolon, ";", "after the top level attribute definition", MissingTokenAnchor.AfterPrevious);

        return new TopLevelAttributeDeclaration(attribute, semicolon);
    }

    private TopLevelTypeDeclaration ParseTopLevelTypeDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers)
    {
        var type = ParseType(attributes, modifiers);
        return new TopLevelTypeDeclaration(type);
    }

    private TopLevelBlockDeclaration ParseTopLevelBlock(Token? attachedKeyword, bool topLevelStatementsEnabled)
    {
        var openBrace = Consume();
        var members = new List<TopLevel>();

        while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
        {
            var start = current;
            var member = ParseTopLevel(topLevelStatementsEnabled);
            members.Add(member);
            RecoverTopLevelIfStalled(start);
        }
        var closeBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close the top-level block");

        return new TopLevelBlockDeclaration(attachedKeyword, openBrace, members, closeBrace);
    }

    private TopLevelBlockDeclaration ParseTopLevelBlock(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, bool topLevelStatementsEnabled)
    {
        Token? attached = modifiers.Count > 0 ? modifiers[0] : null;
        return ParseTopLevelBlock(attached, topLevelStatementsEnabled);
    }

    private TopLevelDeclaration ParseTopLevelVariableDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers)
    {
        var type = ParseTypeSyntax();

        if (IsDestructuringTupleType(type) && CurrentToken.Kind is TokenKind.Equals)
        {
            var tupleName = CreateTupleNameFromTupleType((TupleType)type);
            return ParseTopLevelVariableDeclaration(attributes, modifiers, type, tupleName);
        }

        var identifier = ParseNamedSyntax(allowQualified: true);
        return ParseTopLevelVariableDeclaration(attributes, modifiers, type, identifier);
    }

    private TopLevelVariableDeclaration ParseTopLevelVariableDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier)
    {
        var declaration = ParseVariableDeclaration(attributes, modifiers, type, identifier);
        var semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after the top-level variable declaration", MissingTokenAnchor.AfterPrevious);

        return new TopLevelVariableDeclaration(declaration, semicolon);
    }

    private MemberAttributeDeclaration ParseMemberAttributeDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers)
    {
        var attribute = ParseAttributeSignature(attributes, modifiers);
        var semicolon = ExpectToken(TokenKind.Semicolon, ";", "after the member attribute definition", MissingTokenAnchor.AfterPrevious);

        return new MemberAttributeDeclaration(attribute, semicolon);
    }

    private MemberTypeDeclaration ParseMemberTypeDeclaration(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null)
    {
        attributes ??= ParseAttributeLists();
        modifiers ??= ParseModifiers();
        var type = ParseType(attributes, modifiers);

        return new MemberTypeDeclaration(type);
    }

    private MemberBlockDeclaration ParseMemberBlockDeclaration(Token? attachedKeyword = null)
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

        var closeBrace = ExpectToken(TokenKind.RightBrace, "'}'", "to close the member block");

        return new MemberBlockDeclaration(attachedKeyword, openBrace, members, closeBrace);
    }

    private MemberBlockDeclaration ParseMemberBlockDeclaration(IReadOnlyList<AttributeListSyntax>? attributes, IReadOnlyList<Token>? modifiers)
    {
        Token? attached = modifiers is { Count: > 0 } ? modifiers[0] : null;
        return ParseMemberBlockDeclaration(attached);
    }

    private Member ParseMemberFieldOrPropertyDeclaration(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null)
    {
        attributes ??= ParseAttributeLists();
        modifiers ??= ParseModifiers();
        var type = ParseTypeSyntax();

        if (IsDestructuringTupleType(type) && CurrentToken.Kind is TokenKind.Equals)
        {
            var tupleName = CreateTupleNameFromTupleType((TupleType)type);
            return ParseMemberFieldDeclaration(attributes, modifiers, type, tupleName);
        }

        var identifier = ParseNamedSyntax(allowQualified: true);

        if (CurrentToken.Kind is TokenKind.LeftBrace)
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

    /// <summary> Parses one property declaration inside a type body. </summary>
    private MemberPropertyDeclaration ParseMemberPropertyDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier)
    {
        PropertyAccessorList body = ParsePropertyAccessorList();
        return new MemberPropertyDeclaration(attributes, modifiers, type, identifier, body);
    }

    private Local ParseLocalDeclaration(StatementParseMode parseMode = StatementParseMode.Normal)
    {
        IReadOnlyList<AttributeListSyntax> attributes = ParseAttributeLists();
        var modifiers = ParseModifiers();

        if (CurrentToken.Kind is TokenKind.LeftBrace)
        {
            var (openBrace, locals, finalExpression, closeBrace) = ParseBlock(allowFinalExpression: parseMode is StatementParseMode.AllowFinalExpression);
            var blockExpr = new BlockExpression(attributes, modifiers, openBrace, locals, finalExpression, closeBrace);
            Token semicolon;
            bool isFinal = false;

            if (CurrentToken.Kind is TokenKind.Semicolon)
                semicolon = Consume();
            else if (parseMode is StatementParseMode.AllowFinalExpression && CurrentToken.Kind is TokenKind.RightBrace or TokenKind.EndToken)
            {
                semicolon = CreateMissingToken();
                isFinal = true;
            }
            else
                semicolon = CreateMissingToken();

            return new LocalExpressionStatement(blockExpr, semicolon, isFinalExpression: isFinal);
        }
        else if (CurrentToken.MatchingKind is MatchingKeywordKind.Attribute)
            return ParseLocalAttributeDeclaration(attributes, modifiers);
        else if (CurrentToken.MatchingKind is MatchingKeywordKind.Literal)
            return new LocalFunctionDeclaration(ParseLiteralFunctionDeclaration(attributes, modifiers));
        else if (CurrentToken.MatchingKind is MatchingKeywordKind.Macro)
            return ParseLocalMacroDeclaration(attributes, modifiers);
        else if (IsCurrentTokenTypeDeclarationStart)
            return ParseLocalTypeDeclaration(attributes, modifiers);
        else if (CurrentToken.MatchingKind is MatchingKeywordKind.Func && Peek().Kind is not TokenKind.LeftParen)
            return new LocalFunctionDeclaration(ParseFunction(attributes, modifiers));
        else
            return ParseLocalVariableDeclarationStatement(attributes, modifiers);
    }

    private LocalAttributeDeclaration ParseLocalAttributeDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers)
    {
        var attribute = ParseAttributeSignature(attributes, modifiers);
        var semicolon = ExpectToken(TokenKind.Semicolon, ";", "after the attribute definition");

        return new LocalAttributeDeclaration(attribute, semicolon);
    }

    private LocalTypeDeclaration ParseLocalTypeDeclaration(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null)
    {
        attributes ??= ParseAttributeLists();
        modifiers ??= ParseModifiers();
        var type = ParseType(attributes, modifiers);

        return new LocalTypeDeclaration(type);
    }

    private Local ParseLocalVariableDeclarationStatementOrFunction(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null) =>
        ParseLocalVariableDeclarationStatement(attributes, modifiers);

    private LocalFunctionDeclaration ParseLocalFunctionDeclaration(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null)
    {
        var function = ParseFunction(attributes, modifiers);

        return new LocalFunctionDeclaration(function);
    }

    private AttributeSignature ParseAttributeSignature(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers)
    {
        var keyword = Consume();
        var identifier = ParseNamedSyntax();

        AttributeParameters? parameters = null;

        if (CurrentToken.Kind is TokenKind.LeftParen)
            parameters = ParseAttributeParameters();

        return new AttributeSignature(attributes, modifiers, keyword, identifier, parameters);
    }

    private AttributeParameters ParseAttributeParameters()
    {
        var leftParen = Consume();
        var parameters = ParseParameterList();
        var rightParen = ExpectToken(TokenKind.RightParen, "')'", "to close the parameter list");

        return new AttributeParameters(leftParen, parameters, rightParen);
    }

    private VariableDeclaration ParseVariableDeclaration(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null, TypeSyntax? type = null, NamedSyntax? firstIdentifier = null)
    {
        attributes ??= ParseAttributeLists();
        modifiers ??= ParseModifiers();
        type ??= ParseTypeSyntax();

        var nodesAndSeparators = new List<SyntaxNode>();
        bool wasCommaLast = false;
        bool parsingFirstDeclarator = firstIdentifier is not null;

        while (parsingFirstDeclarator || (CurrentToken.Kind is not TokenKind.EndToken and not TokenKind.Semicolon))
        {
            NamedSyntax identifier;

            if (parsingFirstDeclarator)
            {
                identifier = firstIdentifier!;
                parsingFirstDeclarator = false;
            }
            else
            {
                if (firstIdentifier is null && nodesAndSeparators.Count == 0 && IsDestructuringTupleType(type) && CurrentToken.Kind is TokenKind.Equals)
                    identifier = CreateTupleNameFromTupleType((TupleType)type);
                else if (CurrentToken.Kind is TokenKind.LeftParen)
                    identifier = ParseTupleName();
                else if (CurrentToken.Kind is TokenKind.Identifier)
                    identifier = ParseNamedSyntax();
                else
                {
                    diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), "for the variable name");
                    break;
                }
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

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            var attributes = ParseAttributeLists();
            var modifiers = ParseModifiers();

            var type = ParseTypeSyntax();
            NamedSyntax identifier;

            if (CurrentToken.Kind is TokenKind.Identifier)
                identifier = ParseNamedSyntax();
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
            var variableDecl = new Parameter(attributes, declarator, initializer);

            nodesAndSeparators.Add(variableDecl);

            if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        return new SeparatedSyntaxList<Parameter>(nodesAndSeparators);
    }

    private FunctionDeclaration ParseFunction(IReadOnlyList<AttributeListSyntax>? attributes = null, IReadOnlyList<Token>? modifiers = null)
    {
        attributes ??= ParseAttributeLists();
        var preModifiers = modifiers ?? ParseModifiers();

        Token funcKeyword = Consume();
        var identifier = ParseNamedSyntax(allowQualified: true, allowGenericName: true);

        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after the function name");
        var parameters = ParseParameterList();
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the parameter list");

        var postModifiers = ParseModifiers();

        Token? arrowToken = null;
        TypeSyntax returnType;

        if (IsMinusGreaterThan())
        {
            arrowToken = ConsumeOperator();
            returnType = ParseTypeSyntax();
        }
        else
        {
            returnType = new SimpleType(new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.Identifier, Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>(), MatchingKeywordKind.None, "void"));
        }

        List<TypeConstraintClause> constraints = [];
        while (CurrentToken.MatchingKind is MatchingKeywordKind.Where)
            constraints.Add(ParseTypeConstraintClause());

        var signature = new FunctionSignature(preModifiers, funcKeyword, identifier, openParen, parameters, closeParen, postModifiers, arrowToken, returnType, constraints);

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
        else if (IsArrow())
        {
            var arrow = ConsumeArrow();
            var expr = ParseExpression();
            var semicolon = ExpectToken(TokenKind.Semicolon, "';'", "after expression body");
            return new FunctionLambdaBody(arrow, new LocalReturnStatement(new ReturnStatement(arrow, expr, semicolon)));
        }
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
            var local = ParseLocal(StatementParseMode.FunctionBody);
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

    private bool IsMinusGreaterThan() => GetCombinedOperatorData().Kind is TokenKind.MinusGreaterThan;

    private Token ExpectMinusGreaterThan(string contextDescription = "before return type")
    {
        if (IsMinusGreaterThan())
            return ConsumeOperator();

        return ExpectToken(TokenKind.MinusGreaterThan, "'->'", contextDescription);
    }

    private FunctionTypeSyntax ParseFunctionType()
    {
        var funcKeyword = Consume();
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after 'func' in function pointer type");
        var parameterTypes = ParseFunctionTypeParameterList();
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the function pointer parameter list");
        var arrowToken = ExpectMinusGreaterThan("before return type in function pointer type");
        var returnType = ParseTypeSyntax();
        return new FunctionTypeSyntax(funcKeyword, openParen, parameterTypes, closeParen, arrowToken, returnType);
    }

    private DelegateTypeSyntax ParseDelegateType()
    {
        var delegateKeyword = Consume();
        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after 'delegate' in delegate type");
        var parameterTypes = ParseFunctionTypeParameterList();
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the delegate parameter list");
        var arrowToken = ExpectMinusGreaterThan("before return type in delegate type");
        var returnType = ParseTypeSyntax();
        return new DelegateTypeSyntax(delegateKeyword, openParen, parameterTypes, closeParen, arrowToken, returnType);
    }

    private SeparatedSyntaxList<TypeSyntax> ParseFunctionTypeParameterList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            var type = ParseTypeSyntax();
            nodesAndSeparators.Add(type);

            if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        return new SeparatedSyntaxList<TypeSyntax>(nodesAndSeparators);
    }

    private TypeSyntax ParseTypeSyntax()
    {
        if (CurrentToken.MatchingKind is MatchingKeywordKind.Func && Peek().Kind is TokenKind.LeftParen)
            return ParseFunctionType();

        if (CurrentToken.MatchingKind is MatchingKeywordKind.Delegate && Peek().Kind is TokenKind.LeftParen)
            return ParseDelegateType();

        if (CurrentToken.Kind is TokenKind.Ampersand)
        {
            var ampersand = Consume();
            var innerType = ParseTypeSyntax();
            return new PrefixModifiedType(new SpanTypeModifier(ampersand), innerType);
        }

        if (CurrentToken.MatchingKind is MatchingKeywordKind.Untagged)
        {
            var untagged = Consume();
            var innerType = ParseTypeSyntax();
            return new PrefixModifiedType(new UntaggedTypeModifier(untagged), innerType);
        }

        var type = ParsePrimaryType();

        if (CurrentToken.Kind is TokenKind.LeftBracket or TokenKind.QuestionMark or TokenKind.Asterisk or TokenKind.Ampersand ||
            (CurrentToken.Kind is TokenKind.Dot && Peek(1).Kind is TokenKind.Dot && Peek(2).Kind is TokenKind.Dot))
            type = ParseModifiedType(type);

        if (CurrentToken.Kind is TokenKind.Dot)
            type = ParseQualifiedType(type);

        return type;
    }

    private TypeSyntax ParsePrimaryType()
    {
        if (CurrentToken.Kind is TokenKind.LeftParen)
            return ParseParenthesizedType();

        if (CurrentToken.Kind is not TokenKind.Identifier || !CanBeTypeIdentifier(CurrentToken.MatchingKind))
        {
            diagnostics.ReportExpectedType(CurrentToken.Span, GetTokenDisplay(CurrentToken), "for the type name");
            return new SimpleType(RecoverWithMissingToken());
        }

        var identifier = Consume();
        TypeSyntax baseType;

        if (CurrentToken.Kind is TokenKind.LessThanSign && LooksLikeGenericArguments().Success)
            baseType = ParseGenericType(identifier);
        else
            baseType = new SimpleType(identifier);

        if (CurrentToken.Kind is TokenKind.LeftParen && LooksLikeUniformTupleType().Success)
            return ParseUniformTupleType(baseType);

        return baseType;
    }

    private UniformTupleType ParseUniformTupleType(TypeSyntax elementType)
    {
        var openParen = Consume();
        var nodesAndSeparators = new List<SyntaxNode>();

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            var ident = ExpectIdentifierToken("in tuple element list");
            nodesAndSeparators.Add(new SimpleName(ident));

            if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the tuple type");
        return new UniformTupleType(elementType, openParen, new SeparatedSyntaxList<SimpleName>(nodesAndSeparators), closeParen);
    }

    private TypeSyntax ParseParenthesizedType()
    {
        var openParen = Consume();
        var nodesAndSeparators = new List<SyntaxNode>();

        var firstType = ParseTypeSyntax();

        if (CurrentToken.Kind is TokenKind.VerticalBar)
        {
            nodesAndSeparators.Add(firstType);
            nodesAndSeparators.Add(Consume());

            while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
            {
                var nextType = ParseTypeSyntax();
                nodesAndSeparators.Add(nextType);

                if (CurrentToken.Kind is TokenKind.VerticalBar)
                    nodesAndSeparators.Add(Consume());
                else
                    break;
            }

            var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the union type");
            return new UnionType(openParen, new SeparatedSyntaxList<TypeSyntax>(nodesAndSeparators), closeParen);
        }

        Token? firstName = null;
        if (CurrentToken.Kind is TokenKind.Identifier && CurrentToken.Kind is not TokenKind.Comma && CurrentToken.Kind is not TokenKind.RightParen)
            firstName = Consume();

        nodesAndSeparators.Add(new TupleTypeElement(firstType, firstName));

        if (CurrentToken.Kind is TokenKind.Comma)
        {
            nodesAndSeparators.Add(Consume());

            while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
            {
                var element = ParseTypeSyntax();
                Token? name = null;

                if (CurrentToken.Kind is TokenKind.Identifier && CurrentToken.Kind is not TokenKind.Comma && CurrentToken.Kind is not TokenKind.RightParen)
                    name = Consume();

                nodesAndSeparators.Add(new TupleTypeElement(element, name));

                if (CurrentToken.Kind is TokenKind.Comma)
                    nodesAndSeparators.Add(Consume());
                else
                    break;
            }
        }

        var tupleCloseParen = ExpectToken(TokenKind.RightParen, "')'", "to close the tuple type");
        return new TupleType(openParen, new SeparatedSyntaxList<TupleTypeElement>(nodesAndSeparators), tupleCloseParen);
    }

    private TupleName ParseTupleName()
    {
        var openParen = Consume();
        var nodesAndSeparators = new List<SyntaxNode>();

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            var ident = ExpectIdentifierToken("for the tuple declarator name");
            nodesAndSeparators.Add(new SimpleName(ident));

            if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the tuple declarator");
        return new TupleName(openParen, new SeparatedSyntaxList<NamedSyntax>(nodesAndSeparators), closeParen);
    }

    private static bool IsDestructuringTupleType(TypeSyntax type)
    {
        if (type is not TupleType tupleType || tupleType.Elements.Count == 0)
            return false;

        for (int i = 0; i < tupleType.Elements.Count; i++)
        {
            if (tupleType.Elements[i].Name is null)
                return false;
        }

        return true;
    }

    private static TupleName CreateTupleNameFromTupleType(TupleType tupleType)
    {
        var nodesAndSeparators = new List<SyntaxNode>();

        for (int i = 0; i < tupleType.Elements.Count; i++)
        {
            var elem = tupleType.Elements[i];
            nodesAndSeparators.Add(new SimpleName(elem.Name!));
            var separator = tupleType.Elements.GetSeparator(i);

            if (separator is not null)
                nodesAndSeparators.Add(separator);
        }

        return new TupleName(tupleType.OpenParen, new SeparatedSyntaxList<NamedSyntax>(nodesAndSeparators), tupleType.CloseParen);
    }

    private QualifiedType ParseQualifiedType(TypeSyntax firstPart)
    {
        var dot = Consume();
        var next = ParseTypeSyntax();

        return new QualifiedType(firstPart, dot, next);
    }

    private GenericType ParseGenericType(Token identifier)
    {
        var (lessThan, genericArguments, GreaterThan) = ParseGenerics();

        return new GenericType(identifier, lessThan, genericArguments, GreaterThan);
    }

    private TypeSyntax ParseModifiedType(TypeSyntax baseType)
    {
        TypeSyntax type = baseType;

        while (CurrentToken.Kind is TokenKind.LeftBracket or TokenKind.QuestionMark or TokenKind.Asterisk or TokenKind.Ampersand ||
               (CurrentToken.Kind is TokenKind.Dot && Peek(1).Kind is TokenKind.Dot && Peek(2).Kind is TokenKind.Dot))
        {
            PostfixTypeModifier modifier = CurrentToken.Kind switch
            {
                TokenKind.LeftBracket => ParseArrayOrFixedArrayTypeModifier(),
                TokenKind.QuestionMark => ParseOptionalTypeModifier(),
                TokenKind.Asterisk => ParsePointerTypeModifier(),
                TokenKind.Ampersand => ParseReferenceTypeModifier(),
                TokenKind.Dot => ParseVariadicTypeModifier(),
                _ => throw new InvalidOperationException()
            };

            type = new ModifiedType(type, modifier);
        }

        return type;
    }

    private VariadicTypeModifier ParseVariadicTypeModifier()
    {
        var dot1 = Consume();
        var dot2 = ExpectToken(TokenKind.Dot, "'.'", "in the variadic type modifier");
        var dot3 = ExpectToken(TokenKind.Dot, "'.'", "in the variadic type modifier");
        return new VariadicTypeModifier([dot1, dot2, dot3]);
    }

    private PostfixTypeModifier ParseArrayOrFixedArrayTypeModifier()
    {
        var openBracket = Consume();

        if (CurrentToken.Kind is not TokenKind.RightBracket)
        {
            var size = ParseExpectedExpression("for the array size", MissingTokenAnchor.AfterPrevious);
            var closeBracket = ExpectToken(TokenKind.RightBracket, "']'", "to close the array type modifier");
            return new FixedArrayTypeModifier(openBracket, size, closeBracket);
        }

        var rightBracket = Consume();
        return new ArrayTypeModifier(openBracket, rightBracket);
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

    private NamedSyntax ParseNamedSyntax(bool allowQualified = false, bool allowGenericName = true, bool allowGenericQualifiedParts = true)
    {
        if (CurrentToken.Kind is TokenKind.LeftParen)
            return ParseTupleName();

        NamedSyntax name = ParseNamedSyntaxPart();

        if (!allowQualified || CurrentToken.Kind is not TokenKind.Dot)
        {
            ReportInvalidGenericNameSegment(name, allowGenericName, isQualifiedPart: false);
            return name;
        }

        List<SyntaxNode> nodesAndSeparators = [name];
        ReportInvalidGenericNameSegment(name, allowGenericName, isQualifiedPart: true);

        while (CurrentToken.Kind is TokenKind.Dot)
        {
            nodesAndSeparators.Add(Consume());
            NamedSyntax part = ParseNamedSyntaxPart();
            nodesAndSeparators.Add(part);
            ReportInvalidGenericNameSegment(part, allowGenericName, isQualifiedPart: CurrentToken.Kind is TokenKind.Dot && !allowGenericQualifiedParts);
        }

        return new QualifiedName(new SeparatedSyntaxList<NamedSyntax>(nodesAndSeparators));
    }

    private void ReportInvalidGenericNameSegment(NamedSyntax name, bool allowGenericName, bool isQualifiedPart)
    {
        if (name is not GenericName genericName || (allowGenericName && !isQualifiedPart))
            return;

        diagnostics.ReportExpectedToken(genericName.LessThanToken.Span, "a non-generic name", GetTokenDisplay(genericName.LessThanToken), "in this qualified name");
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
        var genericParameters = ParseGenericParameterList();
        var greaterThan = ExpectToken(TokenKind.GreaterThanSign, "'>'", "to close the generic parameter list");

        return new GenericName(name, lessThan, genericParameters, greaterThan);
    }

    private SeparatedSyntaxList<GenericParameterSyntax> ParseGenericParameterList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();

        while (CurrentToken.Kind is not TokenKind.GreaterThanSign and not TokenKind.EndToken)
        {
            if (CurrentToken.Kind is not TokenKind.Identifier)
            {
                diagnostics.ReportExpectedIdentifier(CurrentToken.Span, GetTokenDisplay(CurrentToken), "for the generic parameter name");
                break;
            }

            Token identifier = Consume();
            List<Token> ellipsis = [];

            if (CurrentToken.Kind is TokenKind.Dot)
            {
                ellipsis.Add(Consume());
                ellipsis.Add(ExpectToken(TokenKind.Dot, "'.'", "in the variadic marker"));
                ellipsis.Add(ExpectToken(TokenKind.Dot, "'.'", "in the variadic marker"));
            }

            Token? colon = null;
            Token? valueKindToken = null;
            GenericParameterKind kind = GenericParameterKind.Type;

            if (CurrentToken.Kind is TokenKind.Colon)
            {
                colon = Consume();
                valueKindToken = ExpectIdentifierToken("for the compile-time parameter kind");
                kind = valueKindToken.MatchingKind switch
                {
                    MatchingKeywordKind.Int => GenericParameterKind.Integer,
                    MatchingKeywordKind.Float => GenericParameterKind.Float,
                    MatchingKeywordKind.Const => GenericParameterKind.Constant,
                    _ => GenericParameterKind.Type
                };
            }

            nodesAndSeparators.Add(new GenericParameterSyntax(identifier, ellipsis, colon, valueKindToken, kind));

            if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        return new SeparatedSyntaxList<GenericParameterSyntax>(nodesAndSeparators);
    }

    private FunctionDeclaration ParseLiteralFunctionDeclaration(
        IReadOnlyList<AttributeListSyntax> attributes,
        IReadOnlyList<Token> modifiers)
    {
        var literalKeyword = Consume();
        var suffix = ExpectIdentifierToken("for the literal suffix");
        var identifier = new SimpleName(suffix);

        // Synthesize a missing return type (literal functions return the enclosing type)
        var missingReturnType = new SimpleType(CreateMissingToken());

        var openParen = ExpectToken(TokenKind.LeftParen, "'('", "after the literal suffix");
        var parameters = ParseParameterList();
        var closeParen = ExpectToken(TokenKind.RightParen, "')'", "to close the literal parameter list");

        List<TypeConstraintClause> constraints = [];
        while (CurrentToken.MatchingKind is MatchingKeywordKind.Where)
            constraints.Add(ParseTypeConstraintClause());

        var signature = new FunctionSignature(
            modifiers,
            missingReturnType,
            identifier,
            openParen,
            parameters,
            closeParen,
            constraints);

        var body = ParseFunctionBody();

        return new FunctionDeclaration(
            attributes,
            signature,
            body,
            SpecialFunctionKind.Literal);
    }
}