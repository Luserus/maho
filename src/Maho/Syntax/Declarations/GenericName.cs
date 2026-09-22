namespace Maho.Syntax;

/// <summary> Name syntax that carries an explicit generic-parameter list. </summary>
internal sealed class GenericName : NamedSyntax
{
    /// <summary> Base identifier being specialized. </summary>
    public Token Name { get; }
    /// <summary> Opening angle bracket token. </summary>
    public Token LessThanToken { get; }
    /// <summary> Generic parameter list. </summary>
    public SeparatedSyntaxList<GenericParameterSyntax> GenericParameters { get; }
    /// <summary> Closing angle bracket token. </summary>
    public Token GreaterThanToken { get; }

    /// <summary> Creates one generic name node. </summary>
    public GenericName(Token name, Token lessThanToken, SeparatedSyntaxList<GenericParameterSyntax> genericParameters, Token greaterThanToken)
    {
        Name = name;
        LessThanToken = lessThanToken;
        GenericParameters = genericParameters;
        GreaterThanToken = greaterThanToken;
    }
}