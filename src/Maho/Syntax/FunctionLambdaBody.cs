namespace Maho.Syntax;

internal sealed class FunctionLambdaBody : FunctionBody
{
    public Token LambdaArrow { get; }
    public LocalStatement Statement { get; }

    public FunctionLambdaBody(Token lambdaArrow, LocalStatement statement)
    {
        LambdaArrow = lambdaArrow;
        Statement = statement;
    }
}