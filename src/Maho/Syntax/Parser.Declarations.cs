using System.Collections.Generic;
using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    private TopLevel ParseTopLevelDeclaration()
    {   
        var modifiers = ParseModifiers();

        if (CurrentTokenIsTypeDeclarationStart)
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
            var member = ParseTopLevel();
            members.Add(member);
        }

        Token closeBrace;

        if (CurrentToken.Kind is not TokenKind.RightBrace)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "}");
            closeBrace = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            closeBrace = Consume();

        return new NamespaceBlockBody(openBrace, members, closeBrace);
    }

    private TypeDeclaration ParseType(IReadOnlyList<Token> modifiers)
    {
        var keyword = Consume();

        var kind = keyword.Value switch
        {
            "class" => TypeKind.Class,
            "struct" => TypeKind.Struct,
            "interface" => TypeKind.Interface,
            "enum" => TypeKind.Enum,
            _ => throw new System.Exception($"Impossible default case.")
        };

        var name = ParseNamedSyntax();

        TypeBody body;

        if (CurrentToken.Kind is TokenKind.LeftBrace)
            body = ParseTypeBlockBody();
        else if (CurrentToken.Kind is TokenKind.Semicolon)
            body = ParseTypeEmptyBody();
        else
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "type body");
            body = new TypeEmptyBody(new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []));
        }

        return new TypeDeclaration(modifiers, keyword, kind, name, body);
    }

    private TypeBlockBody ParseTypeBlockBody()
    {
        var openBrace = Consume();
        var members = new List<Member>();

        while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
        {
            var member = ParseMember();
            members.Add(member);
        }

        Token closeBrace;

        if (CurrentToken.Kind is not TokenKind.RightBrace)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "}");
            closeBrace = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            closeBrace = Consume();

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

        Token semicolon;

        if (CurrentToken.Kind is not TokenKind.Semicolon)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ";");
            semicolon = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            semicolon = Consume();

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

        Token semicolon;

        if (CurrentToken.Kind is not TokenKind.Semicolon)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ";");
            semicolon = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            semicolon = Consume();

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

        if (CurrentTokenIsTypeDeclarationStart)
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
        bool isFirst = true;
        bool wasCommaLast = false;

        while (CurrentToken.Kind is not TokenKind.EndToken and not TokenKind.Semicolon)
        {
            if (CurrentToken.Kind is not TokenKind.Identifier)
            {
                diagnostics.ReportUnexpectedToken(CurrentToken.Span, CurrentToken.Value);
                break;
            }

            var identifier = (isFirst && firstIdentifier is not null) ? firstIdentifier : ParseNamedSyntax();
            isFirst = false;
            wasCommaLast = false;

            AssignmentClause? initializer = null;

            if (CurrentToken.Kind is TokenKind.Equals)
            {
                var assignmentOp = Consume();
                var initExpr = ParseExpression();

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
            diagnostics.ReportUnexpectedToken(CurrentToken.Span, CurrentToken.Value);

        var declarators = new SeparatedSyntaxList<VariableDeclarator>(nodesAndSeparators);

        return new VariableDeclaration(modifiers,type, declarators);
    }

    private SeparatedSyntaxList<Parameter> ParseParameterList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            if (CurrentToken.Kind is not TokenKind.Identifier)
            {
                diagnostics.ReportUnexpectedToken(CurrentToken.Span, CurrentToken.Value);
                break;
            }

            var modifiers = ParseModifiers();

            if (CurrentToken.Kind is not TokenKind.Identifier)
            {
                diagnostics.ReportUnexpectedToken(CurrentToken.Span, CurrentToken.Value);
                break;
            }

            var type = ParseTypeSyntax();
            NamedSyntax identifier;

            if (CurrentToken.Kind is TokenKind.Identifier)
            {
                identifier = ParseNamedSyntax();
            }
            else
            {
                diagnostics.ReportMissingToken(CurrentToken.Span, "parameter identifier");
                identifier = new SimpleName(new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []));
            }

            AssignmentClause? initializer = null;

            if (CurrentToken.Kind is TokenKind.Equals)
            {
                var assignmentOp = Consume();
                var initExpr = ParseExpression();

                initializer = new AssignmentClause(assignmentOp, initExpr);
            }

            var declarator = new ParameterVariableDeclarator(modifiers, type, identifier);
            var variableDecl = new Parameter(declarator, initializer);

            nodesAndSeparators.Add(variableDecl);

            if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        return new SeparatedSyntaxList<Parameter>(nodesAndSeparators);
    }

    private FunctionDeclaration ParseFunction(IReadOnlyList<Token>? modifiers = null, TypeSyntax? returnType = null, NamedSyntax? identifier = null)
    {
        modifiers ??= ParseModifiers();
        returnType ??= ParseTypeSyntax();
        identifier ??= ParseNamedSyntax();

        Token openParen;

        if (CurrentToken.Kind is not TokenKind.LeftParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "(");
            openParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            openParen = Consume();

        var parameters = ParseParameterList();

        Token closeParen;

        if (CurrentToken.Kind is not TokenKind.RightParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ")");
            closeParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            closeParen = Consume();

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
            diagnostics.ReportMissingToken(CurrentToken.Span, "function body");
            return new FunctionEmptyBody(new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []));
        }
    }

    private FunctionBlockBody ParseFunctionBlockBody()
    {
        var openBrace = Consume();
        var locals = new List<Local>();

        while (CurrentToken.Kind is not TokenKind.RightBrace and not TokenKind.EndToken)
        {
            var local = ParseLocal();
            locals.Add(local);
        }

        if (CurrentToken.Kind is not TokenKind.RightBrace)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "}");
            var closeBrace = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
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
        var identifier = Consume();

        if (CurrentToken.Kind is TokenKind.LessThanSign && LooksLikeGenericName())
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

    private ModifiedType ParseModifiedType(TypeSyntax baseType)
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

        return (ModifiedType)type;
    }

    private ArrayTypeModifier ParseArrayTypeModifier()
    {
        var openBracket = Consume();
        Expression? size = null;

        if (CurrentToken.Kind is not TokenKind.RightBracket)
            size = ParseExpression();

        Token closeBracket;

        if (CurrentToken.Kind is not TokenKind.RightBracket)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "]");
            closeBracket = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            closeBracket = Consume();

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

        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            switch (CurrentToken.Value)
            {
                case "private":
                case "protected":
                case "internal":
                case "public":
                case "static":
                case "sealed":
                    list.Add(Consume());
                    break;

                default:
                    return list;
            }
        }

        return list;
    }

    private NamedSyntax ParseNamedSyntax()
    {
        Token name = Consume();

        if (CurrentToken.Kind is TokenKind.LessThanSign && LooksLikeGenericName())
            return ParseGenericName(name);
        else
            return new SimpleName(name);
    }

    private GenericName ParseGenericName(Token name)
    {
        var lessThan = Consume();
        var typeParameters = ParseTypeParameterList();
        Token greaterThan;

        if (CurrentToken.Kind is not TokenKind.GreaterThanSign)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ">");
            greaterThan = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            greaterThan = Consume();

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
                diagnostics.ReportUnexpectedToken(CurrentToken.Span, CurrentToken.Value);
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
            diagnostics.ReportUnexpectedToken(CurrentToken.Span, CurrentToken.Value);

        return new SeparatedSyntaxList<SimpleName>(nodesAndSeparators);
    }
}