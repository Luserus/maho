using Maho.Syntax;

namespace Maho.Tests;

public sealed class ParserTests
{
    [Fact]
    public void Parse_BuildsNamespaceAndFunctionDeclarations()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            namespace Basic;

            public static int Main()
            {
                return 0;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        Assert.Collection(
            root.Members,
            member => Assert.IsType<NamespaceDeclaration>(member),
            member =>
            {
                TopLevelFunctionDeclaration function = Assert.IsType<TopLevelFunctionDeclaration>(member);
                Assert.IsType<SimpleName>(function.Function.Signature.Identifier);
                Assert.IsType<FunctionBlockBody>(function.Function.Body);
                Assert.Equal(0, function.Function.Signature.Parameters.Count);
            });
    }

    [Fact]
    public void Parse_RecoversAndStillParsesLaterDeclarations()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static int Broken(int argc, char*[] argv
            {
                call(1, 2, ;
                return 0
            }

            public static int Next()
            {
                return 1;
            }
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);

        TopLevelFunctionDeclaration nextFunction = Assert.Single(
            root.Members.OfType<TopLevelFunctionDeclaration>(),
            function => function.Function.Signature.Identifier is SimpleName { Name.Value: "Next" });

        Assert.IsType<FunctionBlockBody>(nextFunction.Function.Body);
    }
}
