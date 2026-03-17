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

    private TypeSyntax ParseType(IReadOnlyList<Token> modifiers)
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

        if (CurrentToken.Kind is TokenKind.LeftCurlyBrace)
            body = ParseTypeBlockBody();
        else if (CurrentToken.Kind is TokenKind.Semicolon)
            body = ParseTypeEmptyBody();
        else
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "type body");
            body = new TypeEmptyBody(new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []));
        }

        return new TypeSyntax(modifiers, keyword, kind, name, body);
    }

    private TypeBlockBody ParseTypeBlockBody()
    {
        var openBrace = Consume();
        var members = new List<Member>();

        while (CurrentToken.Kind is not TokenKind.RightCurlyBrace and not TokenKind.EndToken)
        {
            var member = ParseMember();
            members.Add(member);
        }

        Token closeBrace;

        if (CurrentToken.Kind is not TokenKind.RightCurlyBrace)
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
        var type = ParseNamedSyntax();
        var identifier = ParseNamedSyntax();

        if (CurrentToken.Kind is TokenKind.LeftParen)
            return ParseTopLevelFunctionDeclaration(modifiers, type, identifier);
        else
            return ParseTopLevelVariableDeclaration(modifiers, type, (IdentifierName)identifier);
    }

    private TopLevel ParseTopLevelVariableDeclaration(IReadOnlyList<Token> modifiers, NamedSyntax type, IdentifierName identifier)
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

    private TopLevel ParseTopLevelFunctionDeclaration(IReadOnlyList<Token> modifiers, NamedSyntax type, NamedSyntax identifier)
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
        var type = ParseNamedSyntax();
        var identifier = ParseNamedSyntax();

        if (CurrentToken.Kind is TokenKind.LeftParen)
            return ParseMemberFunction(modifiers, type, identifier);
        else
            return ParseMemberFieldDeclaration(modifiers, type, (IdentifierName)identifier);
    }

    private MemberFieldDeclaration ParseMemberFieldDeclaration(IReadOnlyList<Token> modifiers, NamedSyntax type, IdentifierName identifier)
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

    private MemberFunctionDeclaration ParseMemberFunction(IReadOnlyList<Token> modifiers, NamedSyntax returnType, NamedSyntax identifier)
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
        var type = ParseNamedSyntax();
        var identifier = ParseNamedSyntax();

        if (CurrentToken.Kind is TokenKind.LeftParen)
            return ParseLocalFunctionDeclaration(modifiers, type, identifier);
        else
            return ParseLocalVariableDeclarationStatement(modifiers, type, (IdentifierName)identifier);
    
    }

    private LocalFunctionDeclaration ParseLocalFunctionDeclaration(IReadOnlyList<Token>? modifiers = null, NamedSyntax? type = null, NamedSyntax? identifier = null)
    {
        var function = ParseFunction(modifiers, type, identifier);

        return new LocalFunctionDeclaration(function);
    }

    private VariableDeclaration ParseVariableDeclaration(IReadOnlyList<Token>? modifiers = null, NamedSyntax? type = null, IdentifierName? firstIdentifier = null)
    {
        modifiers ??= ParseModifiers();
        type ??= ParseNamedSyntax();

        var nodesAndSeparators = new List<SyntaxNode>();
        bool isFirst = true;

        while (CurrentToken.Kind is not TokenKind.EndToken and not TokenKind.Semicolon)
        {
            var identifier = (isFirst && firstIdentifier is not null) ? firstIdentifier : ParseIdentifierName();
            isFirst = false;

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
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        var declarators = new SeparatedSyntaxList<VariableDeclarator>(nodesAndSeparators);

        return new VariableDeclaration(modifiers,type, declarators);
    }

    private SeparatedSyntaxList<Parameter> ParseParameterList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            var modifiers = ParseModifiers();

            if (CurrentToken.Kind is TokenKind.Identifier)
            {
                var type = ParseNamedSyntax();
                IdentifierName identifier;

                if (CurrentToken.Kind is TokenKind.Identifier)
                {
                    identifier = ParseIdentifierName();
                }
                else
                {
                    diagnostics.ReportMissingToken(CurrentToken.Span, "parameter identifier");
                    identifier = new IdentifierName(new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []));
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
            }
            else if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        return new SeparatedSyntaxList<Parameter>(nodesAndSeparators);
    }

    private Function ParseFunction(IReadOnlyList<Token>? modifiers = null, NamedSyntax? returnType = null, NamedSyntax? identifier = null)
    {
        modifiers ??= ParseModifiers();
        returnType ??= ParseNamedSyntax();
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

        return new Function(signature, body);
    }

    private FunctionBody ParseFunctionBody()
    {
        if (CurrentToken.Kind is TokenKind.LeftCurlyBrace)
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

        while (CurrentToken.Kind is not TokenKind.RightCurlyBrace and not TokenKind.EndToken)
        {
            var local = ParseLocal();
            locals.Add(local);
        }

        if (CurrentToken.Kind is not TokenKind.RightCurlyBrace)
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
}