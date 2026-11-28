namespace Maho.Syntax;

/// <summary> Declares an <c>extern using &lt;Alias&gt;;</c> directive importing a disambiguated external module alias. </summary>
internal sealed class ExternUsingDirective : Directive
{
    public Token ExternKeyword { get; }
    public Token UsingKeyword { get; }
    public NamedSyntax AliasName { get; }
    public Token Semicolon { get; }

    public ExternUsingDirective(Token externKeyword, Token usingKeyword, NamedSyntax aliasName, Token semicolon)
    {
        ExternKeyword = externKeyword;
        UsingKeyword = usingKeyword;
        AliasName = aliasName;
        Semicolon = semicolon;
    }
}

