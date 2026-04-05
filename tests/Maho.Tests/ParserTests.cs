using System.Collections;
using System.Reflection;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class ParserTests
{
    [Theory]
    [InlineData("namespace Demo;", typeof(NamespaceDeclaration), typeof(NamespaceEmptyBody))]
    [InlineData("namespace Demo { public class Inner; }", typeof(NamespaceDeclaration), typeof(NamespaceBlockBody))]
    [InlineData("public class Box;", typeof(TopLevelTypeDeclaration), typeof(TypeEmptyBody))]
    [InlineData("public class Box { public int Value; }", typeof(TopLevelTypeDeclaration), typeof(TypeBlockBody))]
    [InlineData("public static int Main();", typeof(TopLevelFunctionDeclaration), typeof(FunctionEmptyBody))]
    [InlineData("public static int Main() { return 0; }", typeof(TopLevelFunctionDeclaration), typeof(FunctionBlockBody))]
    [InlineData("public int value;", typeof(TopLevelVariableDeclaration), null)]
    public void Parse_TopLevelDeclarationKinds(string source, Type expectedType, Type? expectedBodyType)
    {
        TopLevel topLevel = ParseSingleTopLevel(source, expectedType);

        if (expectedBodyType is null)
            return;

        object body = topLevel switch
        {
            NamespaceDeclaration @namespace => @namespace.Body,
            TopLevelTypeDeclaration typeDeclaration => typeDeclaration.Type.Body,
            TopLevelFunctionDeclaration functionDeclaration => functionDeclaration.Function.Body,
            _ => throw new Xunit.Sdk.XunitException($"Top-level node '{topLevel.GetType().Name}' does not expose a body.")
        };

        Assert.IsType(expectedBodyType, body);
    }

    [Theory]
    [InlineData("public struct Nested;", typeof(MemberTypeDeclaration), typeof(TypeEmptyBody))]
    [InlineData("public struct Nested { public int Value; }", typeof(MemberTypeDeclaration), typeof(TypeBlockBody))]
    [InlineData("public static int Compute();", typeof(MemberFunctionDeclaration), typeof(FunctionEmptyBody))]
    [InlineData("public static int Compute() { return 0; }", typeof(MemberFunctionDeclaration), typeof(FunctionBlockBody))]
    [InlineData("public int Value;", typeof(MemberFieldDeclaration), null)]
    public void Parse_MemberDeclarationKinds(string source, Type expectedType, Type? expectedBodyType)
    {
        Member member = ParseSingleMember(source, expectedType);

        if (expectedBodyType is null)
            return;

        object body = member switch
        {
            MemberTypeDeclaration typeDeclaration => typeDeclaration.Type.Body,
            MemberFunctionDeclaration functionDeclaration => functionDeclaration.Function.Body,
            _ => throw new Xunit.Sdk.XunitException($"Member node '{member.GetType().Name}' does not expose a body.")
        };

        Assert.IsType(expectedBodyType, body);
    }

    [Theory]
    [InlineData("public class LocalBox;", typeof(LocalTypeDeclaration), typeof(TypeEmptyBody))]
    [InlineData("public class LocalBox { public int Value; }", typeof(LocalTypeDeclaration), typeof(TypeBlockBody))]
    [InlineData("public static int Local();", typeof(LocalFunctionDeclaration), typeof(FunctionEmptyBody))]
    [InlineData("public static int Local() { return 0; }", typeof(LocalFunctionDeclaration), typeof(FunctionBlockBody))]
    public void Parse_LocalDeclarationKinds(string source, Type expectedType, Type? expectedBodyType)
    {
        Local local = ParseSingleLocal(source, expectedType);

        if (expectedBodyType is null)
            return;

        object body = local switch
        {
            LocalTypeDeclaration typeDeclaration => typeDeclaration.Type.Body,
            LocalFunctionDeclaration functionDeclaration => functionDeclaration.Function.Body,
            _ => throw new Xunit.Sdk.XunitException($"Local node '{local.GetType().Name}' does not expose a body.")
        };

        Assert.IsType(expectedBodyType, body);
    }

    [Theory]
    [InlineData("call();", typeof(TopLevelExpressionStatement))]
    [InlineData("if (1) return; else ;", typeof(TopLevelIfStatement))]
    [InlineData("while (1) ;", typeof(TopLevelWhileStatement))]
    [InlineData("{ int value = 1; }", typeof(TopLevelBlockStatement))]
    [InlineData("return 0;", typeof(TopLevelReturnStatement))]
    [InlineData(";", typeof(TopLevelEmptyStatement))]
    public void Parse_TopLevelStatementKinds(string source, Type expectedType)
    {
        TopLevel statement = ParseSingleTopLevel(source, expectedType);
        Assert.IsType(expectedType, statement);
    }

    [Theory]
    [InlineData("int value = 1;", typeof(LocalVariableDeclarationStatement))]
    [InlineData("call();", typeof(LocalExpressionStatement))]
    [InlineData("if (1) return; else ;", typeof(LocalIfStatement))]
    [InlineData("while (1) ;", typeof(LocalWhileStatement))]
    [InlineData("{ int nested = 1; }", typeof(LocalBlockStatement))]
    [InlineData("return 0;", typeof(LocalReturnStatement))]
    [InlineData(";", typeof(LocalEmptyStatement))]
    public void Parse_LocalStatementKinds(string source, Type expectedType)
    {
        Local statement = ParseSingleLocal(source, expectedType);
        Assert.IsType(expectedType, statement);
    }

    [Fact]
    public void Parse_SupportedSyntaxSurface_BuildsCurrentNodeSet()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            namespace Outer;

            public int topValue = 1, topOther;

            namespace Extra
            {
                public class Nested;
            }

            public class Box<T>
            {
                public T Value;
                public class Nested;

                public static int Transform(int[] items, int* pointer, int? maybe, int& reference, Extra.Nested nested)
                {
                    public class LocalBox;
                    public static int LocalFunc<TLocal>(TLocal input)
                    {
                        return 0;
                    }

                    int local = 1;
                    int[] numbers = new int[3] { 1, 2, 3 };
                    local = -(local + 1) + (int)items[0];
                    local = { int last = 2; 3 };
                    local = [1, 2, 3][0];
                    local = new Box<int>(local).Value;
                    local = put Box<int>(local).Value;
                    local = if (local) local else 0;
                    local = identity<int>(local);

                    if (local) return local; else ;
                    while (local) ;
                    { int scoped = 0; scoped = local; }
                    return local;
                }
            }

            public static int identity<T>(T value)
            {
                return value;
            }

            public static int Main()
            {
                return 0;
            }

            public static void Forward();

            call();
            if (1) return; else ;
            while (1) ;
            { int blockValue = 0; }
            return 0;
            ;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        HashSet<Type> nodeTypes = CollectNodeTypes(root);

        AssertIncludesNodeTypes(
            nodeTypes,
            typeof(NamespaceDeclaration),
            typeof(NamespaceEmptyBody),
            typeof(NamespaceBlockBody),
            typeof(TopLevelTypeDeclaration),
            typeof(TopLevelFunctionDeclaration),
            typeof(TopLevelVariableDeclaration),
            typeof(TypeDeclaration),
            typeof(TypeBlockBody),
            typeof(TypeEmptyBody),
            typeof(FunctionDeclaration),
            typeof(FunctionBlockBody),
            typeof(FunctionEmptyBody),
            typeof(MemberFieldDeclaration),
            typeof(MemberFunctionDeclaration),
            typeof(MemberTypeDeclaration),
            typeof(LocalTypeDeclaration),
            typeof(LocalFunctionDeclaration),
            typeof(LocalVariableDeclarationStatement),
            typeof(TopLevelExpressionStatement),
            typeof(TopLevelIfStatement),
            typeof(TopLevelElseStatement),
            typeof(TopLevelWhileStatement),
            typeof(TopLevelBlockStatement),
            typeof(TopLevelReturnStatement),
            typeof(TopLevelEmptyStatement),
            typeof(LocalExpressionStatement),
            typeof(LocalIfStatement),
            typeof(LocalElseStatement),
            typeof(LocalWhileStatement),
            typeof(LocalBlockStatement),
            typeof(LocalReturnStatement),
            typeof(LocalEmptyStatement),
            typeof(VariableDeclaration),
            typeof(VariableDeclarator),
            typeof(AssignmentClause),
            typeof(Parameter),
            typeof(ParameterVariableDeclarator),
            typeof(SimpleName),
            typeof(GenericName),
            typeof(SimpleType),
            typeof(GenericType),
            typeof(QualifiedType),
            typeof(ModifiedType),
            typeof(ArrayTypeModifier),
            typeof(PointerTypeModifier),
            typeof(OptionalTypeModifier),
            typeof(ReferenceTypeModifier),
            typeof(LiteralExpression),
            typeof(IdentifierNameExpression),
            typeof(GenericNameExpression),
            typeof(CallExpression),
            typeof(IndexExpression),
            typeof(MemberAccessExpression),
            typeof(UnaryExpression),
            typeof(BinaryExpression),
            typeof(AssignmentExpression),
            typeof(ParenthesizedExpression),
            typeof(CastExpression),
            typeof(BlockExpression),
            typeof(CollectionExpression),
            typeof(IfExpression),
            typeof(ElseExpression),
            typeof(ConstructorCallExpression),
            typeof(ArrayCreationExpression),
            typeof(CollectionInitializer));
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

    private static TopLevel ParseSingleTopLevel(string source, Type expectedType)
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse(source);

        Assert.Empty(diagnostics.Diagnostics);

        TopLevel topLevel = Assert.Single(root.Members);
        Assert.IsType(expectedType, topLevel);
        return topLevel;
    }

    private static Member ParseSingleMember(string source, Type expectedType)
    {
        TopLevelTypeDeclaration wrapper = Assert.IsType<TopLevelTypeDeclaration>(ParseSingleTopLevel($$"""
            public class Host
            {
                {{source}}
            }
            """, typeof(TopLevelTypeDeclaration)));

        TypeBlockBody body = Assert.IsType<TypeBlockBody>(wrapper.Type.Body);
        Member member = Assert.Single(body.Members);
        Assert.IsType(expectedType, member);
        return member;
    }

    private static Local ParseSingleLocal(string source, Type expectedType)
    {
        TopLevelFunctionDeclaration wrapper = Assert.IsType<TopLevelFunctionDeclaration>(ParseSingleTopLevel($$"""
            public static int Main()
            {
                {{source}}
            }
            """, typeof(TopLevelFunctionDeclaration)));

        FunctionBlockBody body = Assert.IsType<FunctionBlockBody>(wrapper.Function.Body);
        Local local = Assert.Single(body.Locals);
        Assert.IsType(expectedType, local);
        return local;
    }

    private static HashSet<Type> CollectNodeTypes(SyntaxNode root)
    {
        HashSet<Type> types = [];
        CollectNodeTypes(root, types);
        return types;
    }

    private static void CollectNodeTypes(SyntaxNode node, ISet<Type> nodeTypes)
    {
        nodeTypes.Add(node.GetType());

        foreach (PropertyInfo property in node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).OrderBy(static property => property.MetadataToken))
        {
            object? value = property.GetValue(node);

            if (value is null or string)
                continue;

            if (value is SyntaxNode child)
            {
                CollectNodeTypes(child, nodeTypes);
                continue;
            }

            if (value is not IEnumerable sequence)
                continue;

            foreach (object? item in sequence)
            {
                if (item is SyntaxNode sequenceChild)
                    CollectNodeTypes(sequenceChild, nodeTypes);
            }
        }
    }

    private static void AssertIncludesNodeTypes(HashSet<Type> actualTypes, params Type[] expectedTypes)
    {
        foreach (Type expectedType in expectedTypes)
            Assert.Contains(expectedType, actualTypes);
    }
}
