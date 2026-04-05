namespace Maho.Syntax;

/// <summary> Function body represented as a lambda arrow followed by one statement. </summary>
internal sealed class FunctionLambdaBody : FunctionBody
{
    /// <summary> Lambda arrow token. </summary>
    public Token LambdaArrow { get; }
    /// <summary> Body statement. </summary>
    public LocalStatement Statement { get; }

    /// <summary> Creates one lambda-style function body node. </summary>
    public FunctionLambdaBody(Token lambdaArrow, LocalStatement statement)
    {
        LambdaArrow = lambdaArrow;
        Statement = statement;
    }
}
