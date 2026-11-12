using Maho.Syntax;

namespace Maho.Tests;

public sealed class FunctionSyntaxTests
{
    [Fact]
    public void FunctionDeclaration_TrailingReturnType_ParsesCorrectly()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static func Calculate(int a, int b) -> int
            {
                return a + b;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var funcDecl = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        var sig = funcDecl.Function.Signature;

        Assert.NotNull(sig.FuncKeyword);
        Assert.Equal("func", sig.FuncKeyword.Value);
        Assert.Equal("Calculate", Assert.IsType<SimpleName>(sig.Identifier).Name.Value);
        Assert.Equal(2, sig.Parameters.Count);
        Assert.NotNull(sig.ArrowToken);
        Assert.NotNull(sig.ReturnType);
        var retType = Assert.IsType<SimpleType>(sig.ReturnType);
        Assert.Equal("int", retType.Name.Value);
        Assert.IsType<FunctionBlockBody>(funcDecl.Function.Body);
    }

    [Fact]
    public void FunctionDeclaration_OmittedReturnType_SynthesizesVoid()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func DoWork()
            {
                return;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var funcDecl = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        var sig = funcDecl.Function.Signature;

        Assert.NotNull(sig.FuncKeyword);
        Assert.Null(sig.ArrowToken);
        Assert.NotNull(sig.ReturnType);
        var retType = Assert.IsType<SimpleType>(sig.ReturnType);
        Assert.Equal("void", retType.Name.Value);
        Assert.IsType<FunctionBlockBody>(funcDecl.Function.Body);
    }

    [Fact]
    public void FunctionDeclaration_PreAndPostModifiers_BothCapturedInModifiers()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func Compute(int x) static -> int
            {
                return x;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var funcDecl = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        var sig = funcDecl.Function.Signature;

        Assert.Equal(2, sig.Modifiers.Count);
        Assert.Equal("public", sig.Modifiers[0].Value);
        Assert.Equal("static", sig.Modifiers[1].Value);
        var retType = Assert.IsType<SimpleType>(sig.ReturnType);
        Assert.Equal("int", retType.Name.Value);
    }

    [Fact]
    public void FunctionDeclaration_GenericParametersAndWhereClauses_AfterReturnType()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static func Transform<TInput, TOutput>(TInput input) -> TOutput
                where TInput : Source
                where TOutput : Destination
            {
                return default;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var funcDecl = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        var sig = funcDecl.Function.Signature;

        Assert.Equal("Transform", Assert.IsType<GenericName>(sig.Identifier).Name.Value);
        var retType = Assert.IsType<SimpleType>(sig.ReturnType);
        Assert.Equal("TOutput", retType.Name.Value);
        Assert.Equal(2, sig.Constraints.Count);
        Assert.Equal("TInput", sig.Constraints[0].GenericParameter.Name.Value);
        Assert.Equal("TOutput", sig.Constraints[1].GenericParameter.Name.Value);
    }

    [Fact]
    public void FunctionDeclaration_ExpressionBody_ParsesCorrectly()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func Square(int x) -> int => x * x;
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var funcDecl = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        var exprBody = Assert.IsType<FunctionLambdaBody>(funcDecl.Function.Body);
        var retStmt = Assert.IsType<LocalReturnStatement>(exprBody.Statement);
        Assert.IsType<BinaryExpression>(retStmt.Statement.Expression);
    }

    [Fact]
    public void FunctionDeclaration_EmptyBody_ParsesCorrectly()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func Forward(int x) -> void;
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var funcDecl = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        Assert.IsType<FunctionEmptyBody>(funcDecl.Function.Body);
    }
}
