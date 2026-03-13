using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Parses the program tokens into Syntax Tree. </summary>
internal sealed partial class Parser
{
    private readonly DiagnosticsManager diagnostics;
    private readonly SourceText text;
    /// <summary> The tokens to parse. </summary>
    private List<Token> tokens = null!;
    /// <summary> Current index of Token being read from the token list. </summary>
    private int current;
    /// <summary> Current Token being read from the token list. </summary>
    private Token CurrentToken => tokens[current];

    public CompilationUnit Root { get; private set; } = null!;

    private enum StatementParseMode : byte
    {
        Normal,
        AllowFinalExpression,
        AllowStatementWithoutSemicolon
    }

    static Parser() => operatorTrie = BuildOperatorTrie();

    public Parser(SourceText text, DiagnosticsManager diagnostics)
    {
        this.text = text;
        this.diagnostics = diagnostics;
    } 

    /// <summary> Parses the tokens into Syntax Tree. This method is in Work-In-Progress and will me modified later to return the Syntax Tree. </summary>
    /// <param name="tokens"> The tokens to parse. </param>
    public void Parse(List<Token> tokens)
    {
        this.tokens = tokens;
        current = default;

        var compilationUnit = ParseCompilationUnit();
        Root = compilationUnit;
    }

    private CompilationUnit ParseCompilationUnit()
    {
        var members = new List<TopLevel>();

        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            var member = ParseTopLevel();
            members.Add(member);
        }

        var eofToken = Consume();

        return new CompilationUnit(members, eofToken);
    }

    private TopLevel ParseTopLevel()
    {
        return ParseTopLevelStatement();
    }

    private bool LooksLikeGenericName()
    {
        int offset = 1;
        int depth = 1;

        while (true)
        {
            var token = Peek(offset);

            switch (token.Kind)
            {
                case TokenKind.Identifier:
                case TokenKind.Comma:
                    offset++;
                    continue;

                case TokenKind.LessThanSign:
                    depth++;
                    offset++;
                    continue;

                case TokenKind.GreaterThanSign:
                    depth--;
                    offset++;

                    if (depth == 0)
                    {
                        var next = Peek(offset);
                        // valid generic if followed by . or identifier or '('
                        return next.Kind is TokenKind.Dot or TokenKind.LeftParen or TokenKind.Identifier;
                    }

                    continue;

                default:
                    return false;
            }
        }
    }

    private ISeparatedSyntaxList ParseTypeArgumentList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();

        while (CurrentToken.Kind is not TokenKind.GreaterThanSign and not TokenKind.EndToken)
        {
            nodesAndSeparators.Add(new IdentifierName(Consume()));

            if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        return new SeparatedSyntaxList<IdentifierName>(nodesAndSeparators);
    }

    private NamedSyntax ParseNamedSyntax()
    {
        var identifier = Consume();

        if (CurrentToken.Kind is TokenKind.LessThanSign && LooksLikeGenericName())
        {
            var (lessThan, typeArguments, greaterThan) = ParseGenerics();
            return new GenericName(identifier, lessThan, typeArguments, greaterThan);
        }

        return new IdentifierName(identifier);
    }
    
    private (Token LessThan, ISeparatedSyntaxList TypeArguments, Token GreaterThan) ParseGenerics()
    {
        var lessThan = Consume(); // consume '<'
        var typeArguments = ParseTypeArgumentList();

        Token greaterThan;

        if (CurrentToken.Kind is not TokenKind.GreaterThanSign)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ">");
            greaterThan = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            greaterThan = Consume();

        return (lessThan, typeArguments, greaterThan);
    }

    private (Token Type, Token Identifier) ParseVariableDeclaration()
    {
        var type = Consume();
        var identifier = Consume();

        return (type, identifier);
    }

    /// <summary> Parses a list of modifiers. </summary>
    /// <returns> The modifier list. </returns>
    private ModifierList ParseModifiers()
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
                    list.Add(CurrentToken);
                    break;

                default:
                    return new ModifierList(list);
            }
        }

        return new ModifierList(list);
    }

    private (Token OpenBrace, IReadOnlyList<LocalStatement> Statements, Expression? FinalExpression, Token CloseBrace) ParseBlock(bool allowFinalExpression)
    {
        var openBrace = Consume();
        var statements = new List<LocalStatement>();
        Expression? finalExpression = null;

        switch (allowFinalExpression)
        {
            case true:
                while (CurrentToken.Kind is not TokenKind.RightCurlyBrace and not TokenKind.EndToken)
                {
                    var statement = ParseLocalStatement(StatementParseMode.AllowFinalExpression);

                    if (statement is LocalExpressionStatement expressionStatement && expressionStatement.IsFinalExpression)
                    {
                        finalExpression = expressionStatement.Expression;
                        break;
                    }

                    statements.Add(statement);
                }
                break;

            case false:
                while (CurrentToken.Kind is not TokenKind.RightCurlyBrace and not TokenKind.EndToken)
                {
                    var statement = ParseLocalStatement(StatementParseMode.Normal);
                    statements.Add(statement);
                }
                break;
        }

        Token closeBrace;

        if (CurrentToken.Kind is not TokenKind.RightCurlyBrace)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "}");
            closeBrace = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            closeBrace = Consume();

        return (openBrace, statements, finalExpression, closeBrace);
    }

    private ISeparatedSyntaxList ParseArgumentList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            nodesAndSeparators.Add(ParseExpression());

            if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        return new SeparatedSyntaxList<Expression>(nodesAndSeparators);
    }

    private ISeparatedSyntaxList ParseParameterList()
    {
        var nodesAndSeparators = new List<SyntaxNode>();

        while (CurrentToken.Kind is not TokenKind.RightParen and not TokenKind.EndToken)
        {
            var modifiers = ParseModifiers();

            if (CurrentToken.Kind is TokenKind.Identifier && Peek(2).Kind is TokenKind.Identifier)
            {
                var type = ParseNamedSyntax();
                var identifier = new IdentifierName(Consume());

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

    private Function ParseFunction()
    {
        var modifiers = ParseModifiers();
        var returnType = ParseNamedSyntax();
        var identifier = ParseNamedSyntax();

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
            diagnostics.ReportMissingToken(CurrentToken.Span, "(");
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
        var statements = new List<LocalStatement>();

        while (CurrentToken.Kind is not TokenKind.RightCurlyBrace and not TokenKind.EndToken)
        {
            var statement = ParseLocalStatement();
            statements.Add(statement);
        }

        if (CurrentToken.Kind is not TokenKind.RightCurlyBrace)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "}");
            var closeBrace = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
            return new FunctionBlockBody(openBrace, statements, closeBrace);
        }
        else
        {
            var closeBrace = Consume();
            return new FunctionBlockBody(openBrace, statements, closeBrace);
        }
    }

    private FunctionEmptyBody ParseFunctionEmptyBody()
    {
        var semicolon = Consume();
        return new FunctionEmptyBody(semicolon);
    }
}