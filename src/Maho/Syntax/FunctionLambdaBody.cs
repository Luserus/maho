namespace Maho.Syntax;

internal sealed class FunctionLambdaBody : FunctionBody
{
    public Token LambdaArrow { get; }
    public Statement Statement { get; }

    public FunctionLambdaBody(Token lambdaArrow, Statement statement)
    {
        LambdaArrow = lambdaArrow;
        Statement = statement;
    }
}