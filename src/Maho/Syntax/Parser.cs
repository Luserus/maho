using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Maho.Syntax;

/// <summary> Parses the program tokens into Syntax Tree. </summary>
internal sealed partial class Parser
{
    /// <summary> The tokens to parse. </summary>
    private List<Token> tokens = null!;
    /// <summary> Current index of Token being read from the token list. </summary>
    private int current;
    /// <summary> Current Token being read from the token list. </summary>
    private Token CurrentToken => Peek(0);

    /// <summary> Whitespace tokens which are stored for error reporting and formatting. </summary>
    public List<Token> WhitespaceTokens { get; } = new(256);

    /// <summary> Parses the tokens into Syntax Tree. This method is in Work-In-Progress and will me modified later to return the Syntax Tree. </summary>
    /// <param name="tokens"> The tokens to parse. </param>
    public void Parse(List<Token> tokens)
    {
        this.tokens = tokens;
        current = default;
        WhitespaceTokens.Clear();

        while (CurrentToken.Kind is not TokenKind.EndToken)
        {
            RemoveSpace();
            var statement = ParseStatement();
            RemoveSpace();
        }
        
        var eofToken = Match(TokenKind.EndToken);
    }

    /// <summary> Parses an expression. </summary>
    /// <returns> The expression node. </returns>
    private ExpressionSyntax ParseExpression()
    {
        if (CurrentToken.Kind is TokenKind.Identifier && IgnoreSpace().Kind is TokenKind.Equals)
            return ParseAssignmentExpression();

        return ParseBinaryExpression();
    }

    /// <summary> Parses a statement. </summary>
    /// <returns> The statement node. </returns>
    private StatementSyntax ParseStatement()
    {
        switch (CurrentToken.Kind)
        {
            case TokenKind.Identifier:
                if (IgnoreSpace().Kind is TokenKind.Identifier)
                    return ParseVariableInitializationStatement();
                break;
        }

        return ParseExpressionStatement();
    }

    /// <summary> Parses an expression statement. </summary>
    /// <returns> The expression statement node. </returns>
    private ExpressionStatementSyntax ParseExpressionStatement()
    {
        var expression = ParseExpression();
        RemoveSpace();
        var semicolon = Match(TokenKind.Semicolon);

        return new(expression, semicolon);
    }


    /// <summary> Parses an identifier expression. </summary>
    /// <returns> The identifier expression node. </returns>
    private IdentifierExpressionSyntax ParseIdentifierExpression() => new(Match(TokenKind.Identifier));

    /// <summary> Parses a literal expression. </summary>
    /// <returns> The literal expression node. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LiteralExpressionSyntax ParseLiteralExpression() => new(Match(TokenKind.Integer, TokenKind.Float, TokenKind.String, TokenKind.Char));

    /// <summary> Parses an assignment expression. </summary>
    /// <returns> The assignment expression node. </returns>
    private AssignmentExpressionSyntax ParseAssignmentExpression()
    {
        var identiferExpr = ParseIdentifierExpression();
        RemoveSpace();
        var equalsOperator = Match(TokenKind.Equals);
        RemoveSpace();
        var expression = ParseExpression();

        return new AssignmentExpressionSyntax(identiferExpr, equalsOperator, expression);
    }

    /// <summary> Parses a variable initialization statement. </summary>
    /// <returns> The variable initialization statement node. </returns>
    private VariableInitializationStatementSyntax ParseVariableInitializationStatement()
    {
        var type = Match(TokenKind.Identifier);
        RemoveSpace();
        var assignmentExpr = ParseAssignmentExpression();
        RemoveSpace();
        var semicolon = Match(TokenKind.Semicolon);

        return new(type, assignmentExpr, semicolon);
    }
    
    /// <summary> Parses a primary expression without operator involvement. </summary>
    /// <returns> The primary expression node. </returns>
    private ExpressionSyntax ParsePrimaryExpression()
    {
        return CurrentToken.Kind switch
        {
            TokenKind.Identifier => ParseIdentifierExpression(),
            _ => ParseLiteralExpression()
        };
    }

    /// <summary> Parses binary expression or unary expression depending on operator precedence. </summary>
    /// <param name="parentPrecedence"> The operator precedence of the parent recursive call. </param>
    /// <returns> Unary or Binary expression depending on operator precedence. </returns>
    private ExpressionSyntax ParseBinaryExpression(int parentPrecedence = 0)
    {
        ExpressionSyntax left;
        var unaryOpPrecedence = GetUnaryOperatorPrecedence();

        if (unaryOpPrecedence != 0 && unaryOpPrecedence >= parentPrecedence)
        {
            var opToken = Consume();
            RemoveSpace();
            var operand = ParseBinaryExpression(unaryOpPrecedence);
            RemoveSpace();
            left = new UnaryExpressionSyntax(opToken, operand);
        }
        else
            left = ParsePrimaryExpression();

        while (true)
        {
            RemoveSpace();
            var precedence = GetBinaryOperatorPrecedence();

            if (precedence == 0 || precedence <= parentPrecedence)
                break;

            Token opToken;
            const int maxCombinedBinaryOpPrecedence = 3;

            if (precedence > maxCombinedBinaryOpPrecedence)
                opToken = Consume();
            else
            {
                var leftOpToken = Consume();
                var rightOpToken = Consume();
                var (Value, Kind) = GetCombinedTokenData();

                opToken = new(Value, leftOpToken.CharNumber, leftOpToken.LineNumber, leftOpToken.ColumnNumber, Kind);
            }

            RemoveSpace();
            var right = ParseBinaryExpression(precedence);
            left = new BinaryExpressionSyntax(left, opToken, right);
        }

        return left;
    }
}
