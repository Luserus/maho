using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

    private List<Statement> statements = [];

    private enum StatementParseMode : byte
    {
        Normal,
        AllowFinalExpression,
        AllowStatementWithoutSemicolon
    }

    static Parser() => operatorTrie = BuildOperatorTrie();

    public Parser(SourceText text, DiagnosticsManager diagnosticsManager)
    {
        this.text = text;
        diagnostics = diagnosticsManager;
    } 

    /// <summary> Parses the tokens into Syntax Tree. This method is in Work-In-Progress and will me modified later to return the Syntax Tree. </summary>
    /// <param name="tokens"> The tokens to parse. </param>
    public void Parse(List<Token> tokens)
    {
        this.tokens = tokens;
        current = default;

        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            var statement = ParseStatement();
            statements.Add(statement);
        }

        var eofToken = Consume();
    }

    /// <summary> Parses an expression. </summary>
    /// <returns> The expression node. </returns>
    private Expression ParseExpression(int minBindingPower = 0)
    {
        Expression left;

        // NUD: prefix operators or primary
        var (kind, length) = GetCombinedOperatorData();

        if (length > 0 && operatorTable.TryGetValue(kind, out var prefixEntry) && prefixEntry.IsPrefix)
        {
            // consume the prefix operator (combined)
            var opToken = ConsumeOperator();
            int rbp = prefixEntry.RightBindingPower;
            var right = ParseExpression(rbp);
            left = new UnaryExpression(opToken, right, UnaryPosition.Prefix);
        }
        else
            left = ParsePrimaryExpression();

        // LED: loop for postfix (prefer) and infix
        while (true)
        {
            if (CurrentToken.Kind is TokenKind.LeftParen)
            {
                var leftParen = Consume();
                var arguments = ParseArgumentList();

                Token rightParen;

                if (CurrentToken.Kind is not TokenKind.RightParen)
                {
                    diagnostics.ReportMissingToken(CurrentToken.Span, ")");
                    rightParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
                }
                else
                    rightParen = Consume();

                left = new CallExpression(left, leftParen, arguments, rightParen);
                continue;
            }
            (kind, length) = GetCombinedOperatorData();

            if (length == 0)
                break; // no operator here

            if (!operatorTable.TryGetValue(kind, out var entry))
                break; // operator not in table -> stop (lexer may produce non-op tokens)

            // If postfix possible, prefer it
            if (entry.IsPostfix)
            {
                int lbp = entry.LeftBindingPower;

                if (lbp < minBindingPower)
                    break;

                // consume combined operator
                var opToken = ConsumeOperator();
                left = new UnaryExpression(opToken, left, UnaryPosition.Postfix);

                continue; // allow chaining
            }

            // If infix possible, handle it
            if (entry.IsInfix)
            {
                int lbp = entry.LeftBindingPower;
                if (lbp < minBindingPower) 
                    break;

                // consume combined operator
                var opTok = ConsumeOperator();
                int rbp = entry.RightBindingPower;
                var right = ParseExpression(rbp);

                if (opTok.Kind is TokenKind.Equals)
                    left = new AssignmentExpression(left, opTok, right);
                else
                    left = new BinaryExpression(left, opTok, right);

                continue;
            }

            break;
        }

        return left;
    }

    private Expression ParseObjectCreationExpression()
    {
        return default!;
    }

    /// <summary> Parses a statement. </summary>
    /// <returns> The statement node. </returns>
    private Statement ParseStatement(StatementParseMode parseMode = StatementParseMode.Normal)
    {
        switch (parseMode)
        {
            default:
                switch (CurrentToken.Kind)
                {
                    case TokenKind.Identifier:
                        if (CurrentToken.Value == "if")
                            return ParseIfStatement();
                        else if (Peek().Kind is TokenKind.Identifier)
                            return ParseVariableDeclarationStatement();
                        break;

                    case TokenKind.Semicolon:
                        return new EmptyStatement(Consume());

                    case TokenKind.LeftCurlyBrace:
                        return ParseBlockStatement();
                }

                return ParseExpressionStatement(allowFinalExpression: false);

            case StatementParseMode.AllowFinalExpression:
                switch (CurrentToken.Kind)
                {
                    case TokenKind.Identifier:
                        if (CurrentToken.Value == "if")
                            return ParseIfStatement();
                        else if (Peek().Kind is TokenKind.Identifier)
                            return ParseVariableDeclarationStatement();
                        break;

                    case TokenKind.Semicolon:
                        return new EmptyStatement(Consume());
                }

                return ParseExpressionStatement(allowFinalExpression: true);

            case StatementParseMode.AllowStatementWithoutSemicolon:
                switch (CurrentToken.Kind)
                {
                    case TokenKind.Identifier:
                        if (CurrentToken.Value == "if")
                            return ParseIfStatement();
                        else if (Peek().Kind is TokenKind.Identifier)
                            return ParseVariableDeclarationStatement(allowMissingSemicolon: true);
                        break;

                    case TokenKind.LeftCurlyBrace:
                        return ParseBlockStatement();
                }
                
                return new ExpressionStatement(ParseExpression(), new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []));
        }
    }

    /// <summary> Parses an expression statement. </summary>
    /// <returns> The expression statement node. </returns>
    private ExpressionStatement ParseExpressionStatement(bool allowFinalExpression)
    {
        var expression = ParseExpression();
        Token semicolon;

        if (CurrentToken.Kind is not TokenKind.Semicolon && !allowFinalExpression)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ";");
            semicolon = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else if (CurrentToken.Kind is not TokenKind.Semicolon)
            semicolon = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []); // Fabricated semicolon
        else
            semicolon = Consume();

        return new ExpressionStatement(expression, semicolon, isFinalExpression: allowFinalExpression);
    }

    private NamedExpression ParseNamedExpression()
    {
        var namedSyntax = ParseNamedSyntax();

        if (namedSyntax is GenericName genericName)
            return new GenericNameExpression(genericName);
        
        return new IdentifierExpression((IdentifierName)namedSyntax);
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
                        return next.Kind is TokenKind.Dot or TokenKind.LeftParen;
                    }

                    continue;

                default:
                    return false;
            }
        }
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

    /// <summary> Parses a literal expression. </summary>
    /// <returns> The literal expression node. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LiteralExpression ParseLiteralExpression() => new(Consume());

    private (Token Type, Token Identifier) ParseVariableDeclaration()
    {
        var type = Consume();
        var identifier = Consume();

        return (type, identifier);
    }

    /// <summary> Parses a variable declaration statement. </summary>
    /// <returns> The variable declaration statement node. </returns>
    private VariableDeclarationStatement ParseVariableDeclarationStatement(bool allowMissingSemicolon = false)
    {
        var modifiers = ParseModifiers();
        var type = ParseNamedSyntax();

        var nodesAndSeparators = new List<ISyntaxNode>();

        while (CurrentToken.Kind is not TokenKind.EndToken and not TokenKind.Semicolon)
        {
            var identifier = new IdentifierName(Consume());

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

        Token semicolon;

        if (CurrentToken.Kind is not TokenKind.Semicolon && !allowMissingSemicolon)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ";");
            semicolon = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else if (CurrentToken.Kind is not TokenKind.Semicolon)
            semicolon = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        else
            semicolon = Consume();

        return new VariableDeclarationStatement(type, new SeparatedSyntaxList<VariableDeclarator>(nodesAndSeparators), semicolon);
    }

    /// <summary> Parses a primary expression without operator involvement. </summary>
    /// <returns> The primary expression node. </returns>
    private Expression ParsePrimaryExpression() => CurrentToken.Kind switch
    {
        TokenKind.LeftParen => ParseParenthesizedExpression(),
        TokenKind.LeftCurlyBrace => ParseBlockExpression(),

        TokenKind.Identifier => CurrentToken.Value switch
        {
            "new" or "put" => ParseObjectCreationExpression(),
            "if" => ParseIfExpression(),
            _ => ParseNamedExpression()
        },

        _ => ParseLiteralExpression()
    };

    private ParenthesizedExpression ParseParenthesizedExpression()
    {
        var leftParen = Consume(); // consume '('
        var expression = ParseExpression();
        Token rightParen;

        if (CurrentToken.Kind is not TokenKind.RightParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "')'");
            rightParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            rightParen = Consume();

        return new ParenthesizedExpression(leftParen, expression, rightParen);
    }

    private (Token OpenBrace, IReadOnlyList<Statement> Statements, Expression? FinalExpression, Token CloseBrace) ParseBlock(bool allowFinalExpression)
    {
        var openBrace = Consume();
        var statements = new List<Statement>();
        Expression? finalExpression = null;

        switch (allowFinalExpression)
        {
            case true:
                while (CurrentToken.Kind is not TokenKind.RightCurlyBrace and not TokenKind.EndToken)
                {
                    var statement = ParseStatement(StatementParseMode.AllowFinalExpression);

                    if (statement is ExpressionStatement expressionStatement && expressionStatement.IsFinalExpression)
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
                    var statement = ParseStatement(StatementParseMode.Normal);
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

    private BlockExpression ParseBlockExpression()
    {
        var (openBrace, statements, finalExpression, closeBrace) = ParseBlock(allowFinalExpression: true);

        return new BlockExpression(openBrace, statements, finalExpression, closeBrace);
    }

    private BlockStatement ParseBlockStatement()
    {
        var (openBrace, statements, _, closeBrace) = ParseBlock(allowFinalExpression: false);

        return new BlockStatement(openBrace, statements, closeBrace);
    }

    private IfExpression ParseIfExpression()
    {
        var ifKeyword = Consume();
        Token openParen;

        if (CurrentToken.Kind is not TokenKind.LeftParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "(");
            openParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            openParen = Consume();

        var condition = ParseExpression();

        Token closeParen;

        if (CurrentToken.Kind is not TokenKind.RightParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ")");
            closeParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            closeParen = Consume();

        var thenExpression = ParseExpression();

        ElseExpression? elseExpression = null;

        if (CurrentToken.Value == "else")
        {
            var elseKeyword = Consume();
            var elseExpr = ParseExpression();
            elseExpression = new ElseExpression(elseKeyword, elseExpr);
        }

        return new IfExpression(ifKeyword, openParen, condition, closeParen, thenExpression, elseExpression);
    }

    private IfStatement ParseIfStatement()
    {
        var ifKeyword = Consume();
        Token openParen;

        if (CurrentToken.Kind is not TokenKind.LeftParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "(");
            openParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            openParen = Consume();

        var condition = ParseExpression();

        Token closeParen;

        if (CurrentToken.Kind is not TokenKind.RightParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ")");
            closeParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            closeParen = Consume();

        var thenStatement = ParseStatement();

        ElseStatement? elseStatement = null;

        if (CurrentToken.Value == "else")
        {
            var elseKeyword = Consume();
            var elseStmt = ParseStatement();
            elseStatement = new ElseStatement(elseKeyword, elseStmt);
        }

        return new IfStatement(ifKeyword, openParen, condition, closeParen, thenStatement, elseStatement);
    }

    private CallExpression ParseCallExpression()
    {
        var callee = ParseExpression();
        var openParen = Consume();
        var arguments = ParseArgumentList();
        Token closeParen;

        if (CurrentToken.Kind is not TokenKind.RightParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, ")");
            closeParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            closeParen = Consume();

        return new CallExpression(callee, openParen, arguments, closeParen);
    }

    private ISeparatedSyntaxList ParseArgumentList()
    {
        var nodesAndSeparators = new List<ISyntaxNode>();

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

    private ISeparatedSyntaxList ParseTypeArgumentList()
    {
        var nodesAndSeparators = new List<ISyntaxNode>();

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

    private ISeparatedSyntaxList ParseParameterList()
    {
        var nodesAndSeparators = new List<ISyntaxNode>();

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
                var variableDecl = new ParameterVariableDeclaration(declarator, initializer);

                nodesAndSeparators.Add(variableDecl);
            }
            else if (CurrentToken.Kind is TokenKind.Comma)
                nodesAndSeparators.Add(Consume());
            else
                break;
        }

        return new SeparatedSyntaxList<Parameter>(nodesAndSeparators);
    }

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

    private FunctionMember ParseFunctionMember()
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

        if (CurrentToken.Kind is not TokenKind.LeftParen)
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "(");
            closeParen = new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []);
        }
        else
            closeParen = Consume();

        var signature = new FunctionSignature(modifiers, returnType, identifier, openParen, parameters, closeParen);

        if (CurrentToken.Kind is TokenKind.LeftCurlyBrace)
        {
            var body = ParseBlockStatement();
            return new FunctionDefinition(signature, body);
        }
        else if (CurrentToken.Kind is TokenKind.Semicolon)
        {
            var semicolon = Consume();
            return new FunctionDeclaration(signature, semicolon);
        }
        else
        {
            diagnostics.ReportMissingToken(CurrentToken.Span, "(");
            return new FunctionDeclaration(signature, new Token(text, new TextSpan(CurrentToken.Span.Start, 0), TokenKind.MissingToken, [], []));
        }
    }
}