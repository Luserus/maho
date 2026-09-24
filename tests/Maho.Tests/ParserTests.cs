using System.Collections;
using System.Reflection;
using Maho;
using Maho.Diagnostics;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class ParserTests
{
    [Theory]
    [InlineData("namespace Demo;", typeof(NamespaceDeclaration), typeof(NamespaceEmptyBody))]
    [InlineData("namespace Demo { public class Inner; }", typeof(NamespaceDeclaration), typeof(NamespaceBlockBody))]
    [InlineData("public class Box;", typeof(TopLevelTypeDeclaration), typeof(TypeEmptyBody))]
    [InlineData("public attribute Marker;", typeof(TopLevelAttributeDeclaration), null)]
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
    [InlineData("public int Value { get; set; }", typeof(MemberPropertyDeclaration), null)]
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
    [InlineData("return 0;", typeof(TopLevelReturnStatement))]
    [InlineData("return value;", typeof(TopLevelReturnStatement))]
    [InlineData(";", typeof(TopLevelEmptyStatement))]
    public void Parse_TopLevelStatementKinds(string source, Type expectedType)
    {
        TopLevel statement = ParseSingleTopLevel($"#pragma toplevel enable\n{source}", expectedType);
        Assert.IsType(expectedType, statement);
    }

    [Fact]
    public void Parse_TopLevelPragma_EnablesStatementsForItsCompilationUnit()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            int value = 1;
            call();
            """);

        Assert.Empty(diagnostics.Diagnostics);
        Assert.True(PragmaDirective.EnablesTopLevelStatements(root.Pragmas));

        PragmaDirective pragma = Assert.Single(root.Pragmas);
        Assert.Equal("pragma", pragma.PragmaKeyword.Value);
        Assert.Equal("toplevel", pragma.Name.Value);
        Assert.Equal("enable", pragma.Value.Value);
        Assert.IsType<TopLevelVariableDeclaration>(root.Members[0]);
        Assert.IsType<TopLevelExpressionStatement>(root.Members[1]);
    }

    [Fact]
    public void Parse_AliasDeclarations_PreserveTargetSpecializationAndConstraints()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            using Simple = Namespace.Type;
            using Specialized = Namespace.Type<Int32>;
            using Generic<T> where T : Constraint = Namespace.Type<T, Int32>;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        TopLevelAliasDeclaration simple = Assert.IsType<TopLevelAliasDeclaration>(root.Members[0]);
        TopLevelAliasDeclaration specialized = Assert.IsType<TopLevelAliasDeclaration>(root.Members[1]);
        TopLevelAliasDeclaration generic = Assert.IsType<TopLevelAliasDeclaration>(root.Members[2]);

        Assert.IsType<SimpleName>(simple.Alias.Name);
        Assert.IsType<QualifiedType>(simple.Alias.Target);
        Assert.IsType<QualifiedType>(specialized.Alias.Target);
        GenericName genericName = Assert.IsType<GenericName>(generic.Alias.Name);
        Assert.Equal("T", Assert.Single(genericName.GenericParameters).Identifier.Value);
        Assert.Single(generic.Alias.Constraints);
        GenericType genericTarget = Assert.IsType<GenericType>(Assert.IsType<QualifiedType>(generic.Alias.Target).Right);
        Assert.Equal("T", Assert.IsType<NamedExpressionGenericArgument>(genericTarget.GenericArguments[0]).Expression.Identifier.Value);
        Assert.Equal("Int32", Assert.IsType<NamedExpressionGenericArgument>(genericTarget.GenericArguments[1]).Expression.Identifier.Value);
    }

    [Fact]
    public void Parse_UsingDirective_SimpleNamespace()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            using Std;
            """);

        Assert.Empty(diagnostics.Diagnostics);
        Directive directive = Assert.Single(root.Directives);
        UsingDirective usingDirective = Assert.IsType<UsingDirective>(directive);
        Assert.Equal(MatchingKeywordKind.Using, usingDirective.Keyword.MatchingKind);
        SimpleName name = Assert.IsType<SimpleName>(usingDirective.Namespace);
        Assert.Equal("Std", name.Name.Value);
        Assert.Equal(TokenKind.Semicolon, usingDirective.Semicolon.Kind);

        Assert.Single(root.Usings);
        Assert.Empty(root.Pragmas);
        Assert.Empty(root.Members);
    }

    [Fact]
    public void Parse_UsingDirective_QualifiedNamespace()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            using Std.Collections.Generic;
            """);

        Assert.Empty(diagnostics.Diagnostics);
        UsingDirective usingDirective = Assert.Single(root.Usings);
        QualifiedName qualified = Assert.IsType<QualifiedName>(usingDirective.Namespace);
        Assert.Equal(3, qualified.Parts.Count);
        Assert.Equal("Std", Assert.IsType<SimpleName>(qualified.Parts[0]).Name.Value);
        Assert.Equal("Collections", Assert.IsType<SimpleName>(qualified.Parts[1]).Name.Value);
        Assert.Equal("Generic", Assert.IsType<SimpleName>(qualified.Parts[2]).Name.Value);
    }

    [Fact]
    public void Parse_UsingDirectives_InterleavedWithPragmas()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            using Std;
            using Std.IO;

            int value = 42;
            """);

        Assert.Empty(diagnostics.Diagnostics);
        Assert.Equal(3, root.Directives.Count);
        Assert.IsType<PragmaDirective>(root.Directives[0]);
        Assert.IsType<UsingDirective>(root.Directives[1]);
        Assert.IsType<UsingDirective>(root.Directives[2]);

        Assert.Single(root.Pragmas);
        Assert.Equal(2, root.Usings.Count);
        Assert.True(root.EnablesTopLevelStatements);

        Assert.Single(root.Members);
        Assert.IsType<TopLevelVariableDeclaration>(root.Members[0]);
    }

    [Fact]
    public void Parse_UsingDirective_DisambiguatesFromAliasDeclaration()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            using Std;
            using MyInt = Std.Int32;
            using GenericBox<T> = Std.Box<T>;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        // Directive list only contains the using directive
        UsingDirective usingDirective = Assert.Single(root.Usings);
        Assert.Equal("Std", Assert.IsType<SimpleName>(usingDirective.Namespace).Name.Value);
        Assert.Single(root.Directives);

        // Alias declarations are TopLevel members, NOT directives
        Assert.Equal(2, root.Members.Count);
        TopLevelAliasDeclaration simpleAlias = Assert.IsType<TopLevelAliasDeclaration>(root.Members[0]);
        Assert.Equal("MyInt", Assert.IsType<SimpleName>(simpleAlias.Alias.Name).Name.Value);

        TopLevelAliasDeclaration genericAlias = Assert.IsType<TopLevelAliasDeclaration>(root.Members[1]);
        Assert.Equal("GenericBox", Assert.IsType<GenericName>(genericAlias.Alias.Name).Name.Value);
    }

    [Fact]
    public void Parse_UsingDirective_MissingSemicolon_ReportsDiagnosticAndRecovers()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            using Std
            class MyClass;
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);
        Assert.Contains(diagnostics.Diagnostics, d => d.Message.Contains("';'"));

        UsingDirective usingDirective = Assert.Single(root.Usings);
        Assert.Equal("Std", Assert.IsType<SimpleName>(usingDirective.Namespace).Name.Value);
        Assert.Single(root.Members);
        Assert.IsType<TopLevelTypeDeclaration>(root.Members[0]);
    }

    [Fact]
    public void Parse_UsingDirective_InsideNamespaceBody_IsParsedIntoNamespaceDirectives()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            namespace MyNamespace
            {
                using Std;

                public struct Point;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        Assert.Single(root.Members);
        NamespaceDeclaration ns = Assert.IsType<NamespaceDeclaration>(root.Members[0]);
        NamespaceBlockBody body = Assert.IsType<NamespaceBlockBody>(ns.Body);
        Assert.Single(body.Directives);
        Assert.Single(body.Usings);
        UsingDirective usingDirective = body.Usings[0];
        Assert.Equal("Std", Assert.IsType<SimpleName>(usingDirective.Namespace).Name.Value);
        Assert.Single(body.Members);
        Assert.IsType<TopLevelTypeDeclaration>(body.Members[0]);
    }

    [Fact]
    public void Parse_TypeBlock_SupportsUsingDirective_And_AliasDeclaration()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public class MyClass
            {
                using Std.Math;
                using Num = Std.Int32;

                public Num value;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var typeDecl = Assert.IsType<TopLevelTypeDeclaration>(Assert.Single(root.Members));
        var body = Assert.IsType<TypeBlockBody>(typeDecl.Type.Body);
        Assert.Equal(3, body.Members.Count);

        var memberUsing = Assert.IsType<MemberUsingDirective>(body.Members[0]);
        var memberAlias = Assert.IsType<MemberAliasDeclaration>(body.Members[1]);
        Assert.IsType<MemberFieldDeclaration>(body.Members[2]);

        Assert.Equal("Std", Assert.IsType<SimpleName>(Assert.IsType<QualifiedName>(memberUsing.Directive.Namespace).Parts[0]).Name.Value);
        Assert.Equal("Num", Assert.IsType<SimpleName>(memberAlias.Alias.Name).Name.Value);
    }

    [Fact]
    public void Parse_LocalBlock_SupportsUsingDirective_And_AliasDeclaration()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static void Test()
            {
                {
                    using Std.Collections;
                    using IntList = Std.List;

                    IntList items;
                }
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var funcDecl = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        var body = Assert.IsType<FunctionBlockBody>(funcDecl.Function.Body);
        var blockStmt = Assert.IsType<LocalBlockStatement>(Assert.Single(body.Locals));

        Assert.Equal(3, blockStmt.Locals.Count);
        var localUsing = Assert.IsType<LocalUsingDirective>(blockStmt.Locals[0]);
        var localAlias = Assert.IsType<LocalAliasDeclaration>(blockStmt.Locals[1]);
        Assert.IsType<LocalVariableDeclarationStatement>(blockStmt.Locals[2]);

        Assert.Equal("Collections", Assert.IsType<SimpleName>(Assert.IsType<QualifiedName>(localUsing.Directive.Namespace).Parts[1]).Name.Value);
        Assert.Equal("IntList", Assert.IsType<SimpleName>(localAlias.Alias.Name).Name.Value);
    }

    [Fact]
    public void Parse_TopLevelBlock_SupportsUsingDirective_And_AliasDeclaration()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            global
            {
                using Std;
                using MyInt = Std.Int32;
                int x = 1;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var topBlock = Assert.IsType<TopLevelBlockDeclaration>(Assert.Single(root.Members));
        Assert.Equal(3, topBlock.Members.Count);

        var topUsing = Assert.IsType<TopLevelUsingDirective>(topBlock.Members[0]);
        var topAlias = Assert.IsType<TopLevelAliasDeclaration>(topBlock.Members[1]);
        Assert.IsType<TopLevelVariableDeclaration>(topBlock.Members[2]);

        Assert.Equal("Std", Assert.IsType<SimpleName>(topUsing.Directive.Namespace).Name.Value);
        Assert.Equal("MyInt", Assert.IsType<SimpleName>(topAlias.Alias.Name).Name.Value);
    }

    [Fact]
    public void Parse_GlobalModifiedTopLevelBlock_PreservesItsDeclarations()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            global
            {
                int globalValue = 1;
                class GlobalType;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        TopLevelBlockDeclaration block = Assert.IsType<TopLevelBlockDeclaration>(Assert.Single(root.Members));
        Assert.Collection(block.Modifiers, modifier => Assert.Equal(MatchingKeywordKind.Global, modifier.MatchingKind));
        Assert.IsType<TopLevelVariableDeclaration>(block.Members[0]);
        Assert.IsType<TopLevelTypeDeclaration>(block.Members[1]);
    }

    [Fact]
    public void Parse_TopLevelStatementWithoutPragma_ReportsAnError()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            call();
            """);

        Assert.False(PragmaDirective.EnablesTopLevelStatements(root.Pragmas));
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.DiagnosticCode == "MH0160");
        Assert.IsType<TopLevelExpressionStatement>(Assert.Single(root.Members));
    }

    [Fact]
    public void Parse_TopLevelBlock_IsTopLevelConstruct()
    {
        TopLevel topLevel = ParseSingleTopLevel("""
            {
                int value = 1;
            }
            """, typeof(TopLevelBlockDeclaration));

        TopLevelBlockDeclaration block = Assert.IsType<TopLevelBlockDeclaration>(topLevel);
        Assert.IsType<TopLevelVariableDeclaration>(Assert.Single(block.Members));
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

    [Theory]
    [InlineData("PointerType * value;", typeof(TopLevelAmbiguousPointerDeclaration), typeof(AmbiguousPointerDeclaration))]
    [InlineData("ReferenceType & value;", typeof(TopLevelAmbiguousReferenceDeclaration), typeof(AmbiguousReferenceDeclaration))]
    public void Parse_TopLevelAmbiguousDeclarationKinds(string source, Type expectedType, Type expectedDeclarationType)
    {
        TopLevel topLevel = ParseSingleTopLevel(source, expectedType);

        object declaration = topLevel switch
        {
            TopLevelAmbiguousPointerDeclaration pointer => pointer.Declaration,
            TopLevelAmbiguousReferenceDeclaration reference => reference.Declaration,
            _ => throw new Xunit.Sdk.XunitException($"Top-level node '{topLevel.GetType().Name}' does not expose an ambiguous declaration.")
        };

        Assert.IsType(expectedDeclarationType, declaration);
    }

    [Theory]
    [InlineData("PointerType * value;", typeof(LocalAmbiguousPointerDeclarationStatement), typeof(AmbiguousPointerDeclaration))]
    [InlineData("ReferenceType & value;", typeof(LocalAmbiguousReferenceDeclarationStatement), typeof(AmbiguousReferenceDeclaration))]
    public void Parse_LocalAmbiguousDeclarationKinds(string source, Type expectedType, Type expectedDeclarationType)
    {
        Local local = ParseSingleLocal(source, expectedType);

        object declaration = local switch
        {
            LocalAmbiguousPointerDeclarationStatement pointer => pointer.Declaration,
            LocalAmbiguousReferenceDeclarationStatement reference => reference.Declaration,
            _ => throw new Xunit.Sdk.XunitException($"Local node '{local.GetType().Name}' does not expose an ambiguous declaration.")
        };

        Assert.IsType(expectedDeclarationType, declaration);
    }

    [Theory]
    [InlineData("[Marker] PointerType * value;")]
    [InlineData("public ReferenceType & value;")]
    public void Parse_TopLevelAttributedOrModifiedPointerReferenceDeclarations_AreUnambiguousDeclarations(string source)
    {
        TopLevel topLevel = ParseSingleTopLevel(source, typeof(TopLevelVariableDeclaration));
        TopLevelVariableDeclaration variable = Assert.IsType<TopLevelVariableDeclaration>(topLevel);

        Assert.IsType<ModifiedType>(variable.Declaration.Type);
    }

    [Theory]
    [InlineData("[Marker] PointerType * value;")]
    [InlineData("static ReferenceType & value;")]
    public void Parse_LocalAttributedOrModifiedPointerReferenceDeclarations_AreUnambiguousDeclarations(string source)
    {
        Local local = ParseSingleLocal(source, typeof(LocalVariableDeclarationStatement));
        LocalVariableDeclarationStatement variable = Assert.IsType<LocalVariableDeclarationStatement>(local);

        Assert.IsType<ModifiedType>(variable.Declaration.Type);
    }

    [Fact]
    public void Parse_AttributedModifiedTopLevelBlock_PreservesMetadataAndMembers()
    {
        TopLevelBlockDeclaration block = Assert.IsType<TopLevelBlockDeclaration>(ParseSingleTopLevel("""
            [Attribute]
            unsafe
            {
                public struct Example
                {
                    unsafe
                    {
                        void Func() { }
                    }
                }
            }
            """, typeof(TopLevelBlockDeclaration)));

        Assert.Single(block.Attributes);
        Assert.Contains(block.Modifiers, token => token.MatchingKind == MatchingKeywordKind.Unsafe);

        TopLevelTypeDeclaration topLevelType = Assert.IsType<TopLevelTypeDeclaration>(Assert.Single(block.Members));
        TypeBlockBody typeBody = Assert.IsType<TypeBlockBody>(topLevelType.Type.Body);
        MemberBlockDeclaration memberBlock = Assert.IsType<MemberBlockDeclaration>(Assert.Single(typeBody.Members));

        Assert.Empty(memberBlock.Attributes);
        Assert.Contains(memberBlock.Modifiers, token => token.MatchingKind == MatchingKeywordKind.Unsafe);
        Assert.IsType<MemberFunctionDeclaration>(Assert.Single(memberBlock.Members));
    }

    [Fact]
    public void Parse_UnmarkedTypeDeclarationInsideTopLevelBlock()
    {
        TopLevelBlockDeclaration block = Assert.IsType<TopLevelBlockDeclaration>(ParseSingleTopLevel("""
            {
                struct UnmarkedTopLevelBlock;
            }
            """, typeof(TopLevelBlockDeclaration)));

        TopLevelTypeDeclaration topLevelType = Assert.IsType<TopLevelTypeDeclaration>(Assert.Single(block.Members));
        Assert.Equal("UnmarkedTopLevelBlock", Assert.IsType<SimpleName>(topLevelType.Type.Name).Name.Value);
    }

    [Fact]
    public void Parse_AttributedModifiedBlocks_WorkInNamespaceAndLocalScopes()
    {
        NamespaceDeclaration @namespace = Assert.IsType<NamespaceDeclaration>(ParseSingleTopLevel("""
            namespace Demo
            {
                [Attribute]
                unsafe
                {
                    int value;
                }
            }
            """, typeof(NamespaceDeclaration)));

        NamespaceBlockBody namespaceBody = Assert.IsType<NamespaceBlockBody>(@namespace.Body);
        TopLevelBlockDeclaration topLevelBlock = Assert.IsType<TopLevelBlockDeclaration>(Assert.Single(namespaceBody.Members));

        Assert.Single(topLevelBlock.Attributes);
        Assert.Contains(topLevelBlock.Modifiers, token => token.MatchingKind == MatchingKeywordKind.Unsafe);

        LocalBlockStatement localBlock = Assert.IsType<LocalBlockStatement>(ParseSingleLocal("""
            [Attribute]
            unsafe
            {
                int nested;
            }
            """, typeof(LocalBlockStatement)));

        Assert.Single(localBlock.Attributes);
        Assert.Contains(localBlock.Modifiers, token => token.MatchingKind == MatchingKeywordKind.Unsafe);
    }

    [Fact]
    public void Parse_VariableDeclaration_PreservesMultipleDeclaratorsAndInitializers()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public int first = 1, second = first;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        TopLevelVariableDeclaration declaration = Assert.IsType<TopLevelVariableDeclaration>(Assert.Single(root.Members));
        Assert.Equal(2, declaration.Declaration.Declarators.Count);
        Assert.Equal("first", Assert.IsType<SimpleName>(declaration.Declaration.Declarators[0].Identifier).Name.Value);
        Assert.IsType<LiteralExpression>(declaration.Declaration.Declarators[0].Initializer?.Initializer);
        Assert.Equal("second", Assert.IsType<SimpleName>(declaration.Declaration.Declarators[1].Identifier).Name.Value);
        Assert.IsType<IdentifierNameExpression>(declaration.Declaration.Declarators[1].Initializer?.Initializer);
    }

    [Fact]
    public void Parse_LocalVariableDeclaration_PreservesMultipleDeclarators()
    {
        LocalVariableDeclarationStatement declaration = Assert.IsType<LocalVariableDeclarationStatement>(ParseSingleLocal("""
            Value first = new Value(), second = first;
            """, typeof(LocalVariableDeclarationStatement)));

        Assert.Equal(2, declaration.Declaration.Declarators.Count);
        Assert.Equal("first", Assert.IsType<SimpleName>(declaration.Declaration.Declarators[0].Identifier).Name.Value);
        Assert.Equal("second", Assert.IsType<SimpleName>(declaration.Declaration.Declarators[1].Identifier).Name.Value);
        Assert.IsType<ConstructorCallExpression>(declaration.Declaration.Declarators[0].Initializer?.Initializer);
        Assert.IsType<IdentifierNameExpression>(declaration.Declaration.Declarators[1].Initializer?.Initializer);
    }

    [Fact]
    public void Parse_ObjectCreationWithClause_AttachesToConstructorCall()
    {
        LocalVariableDeclarationStatement local = Assert.IsType<LocalVariableDeclarationStatement>(ParseSingleLocal("""
            SomeType value = new SomeType(ctorValue) with { prop = "val" };
            """, typeof(LocalVariableDeclarationStatement)));

        Assert.Single(local.Declaration.Declarators);
        ConstructorCallExpression constructor = Assert.IsType<ConstructorCallExpression>(local.Declaration.Declarators[0].Initializer?.Initializer);
        ObjectWithClause withClause = Assert.IsType<ObjectWithClause>(constructor.WithClause);
        AssignmentExpression assignment = Assert.IsType<AssignmentExpression>(Assert.Single(withClause.Initializer.Expressions));

        Assert.Equal("prop", Assert.IsType<IdentifierNameExpression>(assignment.LhsExpression).Identifier.Value);
    }

    [Fact]
    public void Parse_ObjectCreationWithClause_AttachesToArrayCreation()
    {
        LocalVariableDeclarationStatement local = Assert.IsType<LocalVariableDeclarationStatement>(ParseSingleLocal("""
            int[] arr = put int[10] with { SomeProp = someVal };
            """, typeof(LocalVariableDeclarationStatement)));

        Assert.Single(local.Declaration.Declarators);
        ArrayCreationExpression array = Assert.IsType<ArrayCreationExpression>(local.Declaration.Declarators[0].Initializer?.Initializer);
        ObjectWithClause withClause = Assert.IsType<ObjectWithClause>(array.WithClause);
        AssignmentExpression assignment = Assert.IsType<AssignmentExpression>(Assert.Single(withClause.Initializer.Expressions));

        Assert.Equal("SomeProp", Assert.IsType<IdentifierNameExpression>(assignment.LhsExpression).Identifier.Value);
    }

    [Fact]
    public void Parse_CollectionExpressionModifier_WithConstructorArguments()
    {
        LocalReturnStatement local = Assert.IsType<LocalReturnStatement>(ParseSingleLocal("""
            return [val1, val2, val3] with(capacity: 10);
            """, typeof(LocalReturnStatement)));

        CollectionExpression collection = Assert.IsType<CollectionExpression>(local.Statement.Expression);
        CollectionConstructorModifier modifier = Assert.IsType<CollectionConstructorModifier>(Assert.Single(collection.Modifiers));
        NamedArgumentExpression argument = Assert.IsType<NamedArgumentExpression>(Assert.Single(modifier.Arguments));

        Assert.Equal("capacity", argument.Name.Value);
        Assert.IsType<LiteralExpression>(argument.Value);
    }

    [Fact]
    public void Parse_NamedArgumentExpression_InCallableArgumentList()
    {
        LocalExpressionStatement local = Assert.IsType<LocalExpressionStatement>(ParseSingleLocal("""
            call(capacity: 10);
            """, typeof(LocalExpressionStatement)));

        CallExpression call = Assert.IsType<CallExpression>(local.Expression);
        NamedArgumentExpression argument = Assert.IsType<NamedArgumentExpression>(Assert.Single(call.Arguments));

        Assert.Equal("capacity", argument.Name.Value);
        Assert.IsType<LiteralExpression>(argument.Value);
    }

    [Theory]
    [InlineData("return (A) - B;", TokenKind.Minus)]
    [InlineData("return (A) * B;", TokenKind.Asterisk)]
    public void Parse_CastFollowedByPrefixInfixOperator_IsAmbiguous(string source, TokenKind expectedOperator)
    {
        LocalReturnStatement local = Assert.IsType<LocalReturnStatement>(ParseSingleLocal(source, typeof(LocalReturnStatement)));

        AmbiguousCastOrParenthesizedExpression ambiguous = Assert.IsType<AmbiguousCastOrParenthesizedExpression>(local.Statement.Expression);
        UnaryExpression castOperand = Assert.IsType<UnaryExpression>(ambiguous.CastExpression.Expression);
        BinaryExpression parenthesizedAlternative = Assert.IsType<BinaryExpression>(ambiguous.ParenthesizedExpression);

        Assert.Equal(expectedOperator, castOperand.OperatorToken.Kind);
        Assert.Equal(expectedOperator, parenthesizedAlternative.OperatorToken.Kind);
        Assert.IsType<ParenthesizedExpression>(parenthesizedAlternative.LeftExpression);
    }

    [Fact]
    public void Parse_CastFollowedByIdentifier_IsUnambiguousCast()
    {
        LocalReturnStatement local = Assert.IsType<LocalReturnStatement>(ParseSingleLocal("""
            return (A)B;
            """, typeof(LocalReturnStatement)));

        CastExpression cast = Assert.IsType<CastExpression>(local.Statement.Expression);
        Assert.IsType<IdentifierNameExpression>(cast.Expression);
    }

    [Fact]
    public void Parse_ParenthesizedExpressionFollowedByNonPrefixInfixOperator_IsUnambiguousBinary()
    {
        LocalReturnStatement local = Assert.IsType<LocalReturnStatement>(ParseSingleLocal("""
            return (A) / B;
            """, typeof(LocalReturnStatement)));

        BinaryExpression binary = Assert.IsType<BinaryExpression>(local.Statement.Expression);
        Assert.Equal(TokenKind.ForwardSlash, binary.OperatorToken.Kind);
        Assert.IsType<ParenthesizedExpression>(binary.LeftExpression);
    }

    [Fact]
    public void Parse_ParenthesizedExpressionFollowedByMemberAccess_IsUnambiguousMemberAccess()
    {
        LocalReturnStatement local = Assert.IsType<LocalReturnStatement>(ParseSingleLocal("""
            return (A).B;
            """, typeof(LocalReturnStatement)));

        MemberAccessExpression memberAccess = Assert.IsType<MemberAccessExpression>(local.Statement.Expression);
        Assert.IsType<ParenthesizedExpression>(memberAccess.Expression);
        Assert.Equal("B", memberAccess.Identifier.Value);
    }

    [Fact]
    public void Parse_TypeDeclaration_WithBaseClauseAndConstraints()
    {
        TypeDeclaration type = ParseSingleTopLevelType("""
            public class Box<T> : Base<T>, Outer.Inner
                where T: FirstConstraint, Second.Constraint;
            """);

        GenericName name = Assert.IsType<GenericName>(type.Name);
        Assert.Equal("Box", name.Name.Value);
        Assert.Single(name.GenericParameters);
        Assert.Equal("T", name.GenericParameters[0].Identifier.Value);

        TypeBaseClause baseClause = Assert.IsType<TypeBaseClause>(type.Base);
        Assert.Equal(2, baseClause.BaseTypes.Count);

        GenericType firstBaseType = Assert.IsType<GenericType>(baseClause.BaseTypes[0]);
        Assert.Equal("Base", firstBaseType.Name.Value);
        NamedExpressionGenericArgument firstBaseArgument = Assert.IsType<NamedExpressionGenericArgument>(firstBaseType.GenericArguments[0]);
        Assert.Equal("T", firstBaseArgument.Expression.Identifier.Value);

        QualifiedType secondBaseType = Assert.IsType<QualifiedType>(baseClause.BaseTypes[1]);
        Assert.Equal("Outer", Assert.IsType<SimpleType>(secondBaseType.Left).Name.Value);
        Assert.Equal("Inner", Assert.IsType<SimpleType>(secondBaseType.Right).Name.Value);

        TypeConstraintClause constraintClause = Assert.Single(type.Constraints);
        Assert.Equal("T", constraintClause.GenericParameter.Name.Value);
        Assert.Equal(2, constraintClause.Constraints.Count);

        TypeTypeConstraint firstConstraint = Assert.IsType<TypeTypeConstraint>(constraintClause.Constraints[0]);
        Assert.Equal("FirstConstraint", Assert.IsType<SimpleType>(firstConstraint.Type).Name.Value);

        TypeTypeConstraint secondConstraint = Assert.IsType<TypeTypeConstraint>(constraintClause.Constraints[1]);
        QualifiedType secondConstraintType = Assert.IsType<QualifiedType>(secondConstraint.Type);
        Assert.Equal("Second", Assert.IsType<SimpleType>(secondConstraintType.Left).Name.Value);
        Assert.Equal("Constraint", Assert.IsType<SimpleType>(secondConstraintType.Right).Name.Value);
    }

    [Fact]
    public void Parse_QualifiedDeclarationNamesAllowGenericsOnlyOnTheFinalTypeName()
    {
        var (_, validDiagnostics, _, _) = CompilerTestBed.Parse("""
            struct A.B<T>;
            """);
        var (_, invalidTypeDiagnostics, _, _) = CompilerTestBed.Parse("""
            struct A.B<T>.C;
            """);
        var (_, invalidNamespaceDiagnostics, _, _) = CompilerTestBed.Parse("""
            namespace A<T>;
            """);

        Assert.Empty(validDiagnostics.Diagnostics);
        Assert.NotEmpty(invalidTypeDiagnostics.Diagnostics);
        Assert.NotEmpty(invalidNamespaceDiagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_FunctionDeclaration_WithTypeConstraints()
    {
        FunctionDeclaration function = ParseSingleTopLevelFunction("""
            public static TResult Build<TInput, TResult>(TInput input)
                where TInput: Source
                where TResult: Output<TInput>;
            """);

        GenericName identifier = Assert.IsType<GenericName>(function.Signature.Identifier);
        Assert.Equal("Build", identifier.Name.Value);
        Assert.Equal(2, identifier.GenericParameters.Count);
        Assert.Equal("TInput", identifier.GenericParameters[0].Identifier.Value);
        Assert.Equal("TResult", identifier.GenericParameters[1].Identifier.Value);

        Assert.Equal(2, function.Signature.Constraints.Count);

        TypeConstraintClause inputConstraint = function.Signature.Constraints[0];
        Assert.Equal("TInput", inputConstraint.GenericParameter.Name.Value);
        TypeTypeConstraint inputTypeConstraint = Assert.IsType<TypeTypeConstraint>(Assert.Single(inputConstraint.Constraints));
        Assert.Equal("Source", Assert.IsType<SimpleType>(inputTypeConstraint.Type).Name.Value);

        TypeConstraintClause resultConstraint = function.Signature.Constraints[1];
        Assert.Equal("TResult", resultConstraint.GenericParameter.Name.Value);
        TypeTypeConstraint resultTypeConstraint = Assert.IsType<TypeTypeConstraint>(Assert.Single(resultConstraint.Constraints));
        GenericType resultConstraintType = Assert.IsType<GenericType>(resultTypeConstraint.Type);
        Assert.Equal("Output", resultConstraintType.Name.Value);
        Assert.Equal("TInput", Assert.IsType<NamedExpressionGenericArgument>(resultConstraintType.GenericArguments[0]).Expression.Identifier.Value);
    }

    [Fact]
    public void Parse_TrailingCommas_AreAllowedOutsideVariableDeclarators()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            [First, Second,]
            public class Box<T, N: int,> : Base<T,>, Other,
                where T : Constraint, OtherConstraint,
            {
                public void Function(Int32 a, Int32 b,)
                {
                    Call(a, b,);
                    new Int32[] { a, b, };
                }
            }

            public Box<Int32, 100,> value;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        TypeDeclaration box = Assert.IsType<TopLevelTypeDeclaration>(root.Members[0]).Type;
        GenericName name = Assert.IsType<GenericName>(box.Name);
        TypeBaseClause baseClause = Assert.IsType<TypeBaseClause>(box.Base);
        TypeConstraintClause constraint = Assert.Single(box.Constraints);
        FunctionDeclaration function = Assert.IsType<MemberFunctionDeclaration>(Assert.IsType<TypeBlockBody>(box.Body).Members[0]).Function;
        FunctionBlockBody body = Assert.IsType<FunctionBlockBody>(function.Body);
        GenericType variableType = Assert.IsType<GenericType>(Assert.IsType<TopLevelVariableDeclaration>(root.Members[1]).Declaration.Type);

        AssertTrailingComma(box.Attributes[0].Attributes);
        AssertTrailingComma(name.GenericParameters);
        AssertTrailingComma(baseClause.BaseTypes);
        AssertTrailingComma(Assert.IsType<GenericType>(baseClause.BaseTypes[0]).GenericArguments);
        AssertTrailingComma(constraint.Constraints);
        AssertTrailingComma(function.Signature.Parameters);
        AssertTrailingComma(Assert.IsType<CallExpression>(Assert.IsType<LocalExpressionStatement>(body.Locals[0]).Expression).Arguments);
        AssertTrailingComma(Assert.IsType<ArrayCreationExpression>(Assert.IsType<LocalExpressionStatement>(body.Locals[1]).Expression).Initializer!.Expressions);
        AssertTrailingComma(variableType.GenericArguments);
    }

    [Fact]
    public void Parse_VariableDeclarators_RejectTrailingComma()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("Int32 first, second,;");

        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.DiagnosticCode == "MH0122");

        VariableDeclaration declaration = Assert.IsType<TopLevelVariableDeclaration>(Assert.Single(root.Members)).Declaration;
        Assert.Equal(2, declaration.Declarators.Count);
        AssertTrailingComma(declaration.Declarators);
    }

    [Fact]
    public void Parse_GenericDeclaration_SupportsCompileTimeAndVariadicParameters()
    {
        TypeDeclaration declaration = ParseSingleTopLevelType("""
            struct Example<T, N: int, F: float, C: const, Rest...> where N : Std.Int32;
            """);

        GenericName name = Assert.IsType<GenericName>(declaration.Name);
        Assert.Equal(5, name.GenericParameters.Count);
        Assert.Equal(GenericParameterKind.Type, name.GenericParameters[0].Kind);
        Assert.Equal(GenericParameterKind.Integer, name.GenericParameters[1].Kind);
        Assert.Equal(GenericParameterKind.Float, name.GenericParameters[2].Kind);
        Assert.Equal(GenericParameterKind.Constant, name.GenericParameters[3].Kind);
        Assert.True(name.GenericParameters[4].IsVariadic);
        Assert.Equal(3, name.GenericParameters[4].Ellipsis.Count);

        TypeConstraintClause constraint = Assert.Single(declaration.Constraints);
        Assert.Equal("N", constraint.GenericParameter.Name.Value);
        Assert.IsType<QualifiedType>(Assert.IsType<TypeTypeConstraint>(Assert.Single(constraint.Constraints)).Type);
    }

    [Fact]
    public void Parse_DeclarationAttributes_SupportQualifiedNamesAndConstructorArguments()
    {
        AttributeSignature attribute = ParseSingleTopLevelAttribute("""
            [Marker]
            [Standard.IntrinsicType("Int32", 32)]
            public attribute SignedInt;
            """);

        Assert.Equal(2, attribute.Attributes.Count);

        AttributeApplication simpleAttribute = Assert.Single(attribute.Attributes[0].Attributes);
        Assert.Equal("Marker", Assert.IsType<SimpleName>(simpleAttribute.Name).Name.Value);
        Assert.Empty(simpleAttribute.Arguments);
        Assert.Null(simpleAttribute.OpenParen);
        Assert.Null(simpleAttribute.CloseParen);

        AttributeApplication qualifiedAttribute = Assert.Single(attribute.Attributes[1].Attributes);
        QualifiedName qualifiedName = Assert.IsType<QualifiedName>(qualifiedAttribute.Name);
        Assert.Equal(2, qualifiedName.Parts.Count);
        Assert.Equal("Standard", Assert.IsType<SimpleName>(qualifiedName.Parts[0]).Name.Value);
        Assert.Equal("IntrinsicType", Assert.IsType<SimpleName>(qualifiedName.Parts[1]).Name.Value);
        Assert.Equal(2, qualifiedAttribute.Arguments.Count);
        Assert.IsType<LiteralExpression>(qualifiedAttribute.Arguments[0]);
        Assert.IsType<LiteralExpression>(qualifiedAttribute.Arguments[1]);
    }

    [Fact]
    public void Parse_IntrinsicModifier_IsValidOnlyForAttributeDeclarations()
    {
        AttributeSignature intrinsicAttribute = ParseSingleTopLevelAttribute("""
            public intrinsic attribute Intrinsic;
            """);

        Assert.Equal(2, intrinsicAttribute.Modifiers.Count);
        Assert.Contains(intrinsicAttribute.Modifiers, token => token.MatchingKind == MatchingKeywordKind.Intrinsic);

        TopLevelVariableDeclaration variable = Assert.IsType<TopLevelVariableDeclaration>(ParseSingleTopLevel("""
            public intrinsic value;
            """, typeof(TopLevelVariableDeclaration)));

        SimpleType variableType = Assert.IsType<SimpleType>(variable.Declaration.Type);
        Assert.Equal("intrinsic", variableType.Name.Value);
        Assert.DoesNotContain(variable.Declaration.Modifiers, token => token.MatchingKind == MatchingKeywordKind.Intrinsic);
    }

    [Fact]
    public void Parse_PropertyDeclaration_SupportsAccessorModifiersAndBodies()
    {
        MemberPropertyDeclaration property = Assert.IsType<MemberPropertyDeclaration>(ParseSingleMember("""
            [Meta]
            public int Value
            {
                get;
                private set
                {
                    return;
                }
            }
            """, typeof(MemberPropertyDeclaration)));

        Assert.Single(property.Attributes);
        Assert.Equal("Value", Assert.IsType<SimpleName>(property.Identifier).Name.Value);
        Assert.Equal(2, property.Body.Accessors.Count);
        Assert.Equal(PropertyAccessorKind.Get, property.Body.Accessors[0].Kind);
        Assert.IsType<FunctionEmptyBody>(property.Body.Accessors[0].Body);
        Assert.Equal(PropertyAccessorKind.Set, property.Body.Accessors[1].Kind);
        Assert.Single(property.Body.Accessors[1].Modifiers);
        Assert.Equal(MatchingKeywordKind.Private, property.Body.Accessors[1].Modifiers[0].MatchingKind);
        Assert.IsType<FunctionBlockBody>(property.Body.Accessors[1].Body);
    }

    [Fact]
    public void Parse_TypeDeclaration_AllowsTrailingCommaBeforeConstraintClause()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public class Box<T> : Base<T>,
                where T: Constraint
            {
                public int Value;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        TopLevelTypeDeclaration declaration = Assert.Single(root.Members.OfType<TopLevelTypeDeclaration>());
        TypeDeclaration type = declaration.Type;

        TypeBaseClause baseClause = Assert.IsType<TypeBaseClause>(type.Base);
        Assert.Single(baseClause.BaseTypes);
        AssertTrailingComma(baseClause.BaseTypes);
        Assert.Single(type.Constraints);

        TypeBlockBody body = Assert.IsType<TypeBlockBody>(type.Body);
        Assert.Single(body.Members);
        Assert.IsType<MemberFieldDeclaration>(body.Members[0]);
    }

    [Fact]
    public void Parse_SupportedSyntaxSurface_BuildsCurrentNodeSet()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            using Extra;
            namespace Outer;

            public int topValue = 1;
            public int topOther;
            PointerCandidate * topPointer;
            ReferenceCandidate & topReference;
            using TopAlias = Extra.Nested;
            using Extra;

            namespace Extra
            {
                public class Nested;
            }

            public struct Box<T> : BaseBox<T>, Extra.Nested where T: Extra.Nested, Constraint<T>
            {
                using Extra;
                using MemberAlias = Extra.Nested;
                public T Value;
                public class Nested;

                public static int Transform<TInput>(int[] items, int* pointer, int? maybe, int& reference, Extra.Nested nested, TInput input) where TInput: Extra.Nested
                {
                    using Extra;
                    using LocalAlias = Extra.Nested;
                    public class LocalBox<TLocal> : Scoped where TLocal: Extra.Nested;
                    public static int LocalFunc<TLocal>(TLocal input) where TLocal: Extra.Nested
                    {
                        return 0;
                    }

                    int local = 1;
                    int[] numbers = new int[3] { 1, 2, 3 };
                    PointerLocal * localPointer;
                    ReferenceLocal & localReference;
                    local = -(local + 1) + (int)items[0];
                    local = (local) - local;
                    local = { int last = 2; 3 };
                    local = [1, 2, 3] with(capacity: 10)[0];
                    local = new Box<int>(local) with { Value = local }.Value;
                    numbers = put int[3] with { Length = local };
                    local = put Box<int>(local).Value;
                    local = if (local) local else 0;
                    local = identity<int>(value: local);

                    if (local) return local; else ;
                    while (local) ;
                    { int scoped = 0; scoped = local; }
                    return local;
                }

                unsafe
                {
                    void UnsafeFunc() { }
                }
            }

            public static int identity<T>(T value) where T: Extra.Nested
            {
                return value;
            }

            public static int Main()
            {
                return 0;
            }

            public static void Forward();

            global { using Extra; int globalBlockValue = 0; }
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
            typeof(PragmaDirective),
            typeof(UsingDirective),
            typeof(TopLevelUsingDirective),
            typeof(TopLevelAliasDeclaration),
            typeof(MemberUsingDirective),
            typeof(MemberAliasDeclaration),
            typeof(LocalUsingDirective),
            typeof(LocalAliasDeclaration),
            typeof(AliasDeclaration),
            typeof(NamespaceDeclaration),
            typeof(NamespaceEmptyBody),
            typeof(NamespaceBlockBody),
            typeof(TopLevelTypeDeclaration),
            typeof(TopLevelFunctionDeclaration),
            typeof(TopLevelVariableDeclaration),
            typeof(TopLevelAmbiguousPointerDeclaration),
            typeof(TopLevelAmbiguousReferenceDeclaration),
            typeof(TypeDeclaration),
            typeof(TypeBlockBody),
            typeof(TypeEmptyBody),
            typeof(FunctionDeclaration),
            typeof(FunctionBlockBody),
            typeof(FunctionEmptyBody),
            typeof(MemberFieldDeclaration),
            typeof(MemberFunctionDeclaration),
            typeof(MemberTypeDeclaration),
            typeof(MemberBlockDeclaration),
            typeof(LocalTypeDeclaration),
            typeof(LocalFunctionDeclaration),
            typeof(LocalVariableDeclarationStatement),
            typeof(LocalAmbiguousPointerDeclarationStatement),
            typeof(LocalAmbiguousReferenceDeclarationStatement),
            typeof(TopLevelExpressionStatement),
            typeof(TopLevelIfStatement),
            typeof(TopLevelElseStatement),
            typeof(TopLevelWhileStatement),
            typeof(TopLevelBlockDeclaration),
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
            typeof(AmbiguousPointerDeclaration),
            typeof(AmbiguousReferenceDeclaration),
            typeof(AssignmentClause),
            typeof(Parameter),
            typeof(ParameterVariableDeclarator),
            typeof(SimpleName),
            typeof(GenericName),
            typeof(SimpleType),
            typeof(GenericType),
            typeof(QualifiedType),
            typeof(ModifiedType),
            typeof(TypeBaseClause),
            typeof(TypeConstraintClause),
            typeof(TypeTypeConstraint),
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
            typeof(AmbiguousCastOrParenthesizedExpression),
            typeof(BlockExpression),
            typeof(CollectionExpression),
            typeof(CollectionConstructorModifier),
            typeof(IfExpression),
            typeof(ElseExpression),
            typeof(ConstructorCallExpression),
            typeof(ArrayCreationExpression),
            typeof(ObjectWithClause),
            typeof(CollectionInitializer),
            typeof(NamedArgumentExpression));
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

    private static AttributeSignature ParseSingleTopLevelAttribute(string source)
    {
        TopLevelAttributeDeclaration declaration = Assert.IsType<TopLevelAttributeDeclaration>(ParseSingleTopLevel(source, typeof(TopLevelAttributeDeclaration)));
        return declaration.Attribute;
    }

    private static TypeDeclaration ParseSingleTopLevelType(string source)
    {
        TopLevelTypeDeclaration declaration = Assert.IsType<TopLevelTypeDeclaration>(ParseSingleTopLevel(source, typeof(TopLevelTypeDeclaration)));
        return declaration.Type;
    }

    private static FunctionDeclaration ParseSingleTopLevelFunction(string source)
    {
        TopLevelFunctionDeclaration declaration = Assert.IsType<TopLevelFunctionDeclaration>(ParseSingleTopLevel(source, typeof(TopLevelFunctionDeclaration)));
        return declaration.Function;
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

    private static void AssertTrailingComma<T>(SeparatedSyntaxList<T> list) where T : SyntaxNode
    {
        Assert.NotEqual(0, list.Count);
        Assert.Equal(TokenKind.Comma, Assert.IsType<Token>(list.GetSeparator(list.Count - 1)).Kind);
    }

    [Fact]
    public void Parse_ErrorSpans_PointToUnexpectedTokenOnSameLineAndEndOfLineForMissingTokens()
    {
        var (text, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            namespace Sample;

            some err
            nice = }
            foo.;
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);

        // 1. "some err" should be parsed as TopLevelVariableDeclaration missing a semicolon
        var someErrMember = Assert.IsType<TopLevelVariableDeclaration>(root.Members[1]);
        Assert.Equal("some", Assert.IsType<SimpleType>(someErrMember.Declaration.Type).Name.Value);
        Assert.Equal("err", Assert.IsType<SimpleName>(someErrMember.Declaration.Declarators[0].Identifier).Name.Value);

        // Diagnostic for missing ';' after "some err" points to unexpected token on line 5 (1-based line 5:1) with line 4 as Context
        var missingSemiDiag = Assert.Single(diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0120" && d.Message.Contains("variable declaration"));
        Assert.Equal(5, missingSemiDiag.Span.GetStartLine(text) + 1);
        Assert.Equal(1, missingSemiDiag.Span.GetStartColumn(text) + 1);
        Assert.Equal(2, missingSemiDiag.Labels.Count);
        Assert.Equal(DiagnosticLabelStyle.Context, missingSemiDiag.Labels[0].Style);
        Assert.Equal(4, missingSemiDiag.Labels[0].Span.GetStartLine(text) + 1);
        Assert.Equal(9, missingSemiDiag.Labels[0].Span.GetStartColumn(text) + 1);
        Assert.Equal(DiagnosticLabelStyle.Primary, missingSemiDiag.Labels[1].Style);
        Assert.Equal(5, missingSemiDiag.Labels[1].Span.GetStartLine(text) + 1);
        Assert.Equal(1, missingSemiDiag.Labels[1].Span.GetStartColumn(text) + 1);

        // 2. "nice = }" should be parsed as an expression statement (assignment expression)
        var niceStmt = Assert.IsType<TopLevelExpressionStatement>(root.Members[2]);
        var assignExpr = Assert.IsType<AssignmentExpression>(niceStmt.Expression);
        Assert.Equal("nice", Assert.IsType<IdentifierNameExpression>(assignExpr.LhsExpression).Identifier.Value);

        // Diagnostic for missing expression after '=' on the same line points to '}' (1-based line 5:8)
        var missingExprDiag = Assert.Single(diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0121");
        Assert.Equal(5, missingExprDiag.Span.GetStartLine(text) + 1);
        Assert.Equal(8, missingExprDiag.Span.GetStartColumn(text) + 1);

        // Diagnostic for missing ';' after "nice = }" points to unexpected token on line 6 (1-based line 6:1) with line 5 as Context
        var missingSemiNiceDiag = Assert.Single(diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0120" && d.Message.Contains("top-level expression"));
        Assert.Equal(6, missingSemiNiceDiag.Span.GetStartLine(text) + 1);
        Assert.Equal(1, missingSemiNiceDiag.Span.GetStartColumn(text) + 1);
        Assert.Equal(2, missingSemiNiceDiag.Labels.Count);
        Assert.Equal(DiagnosticLabelStyle.Context, missingSemiNiceDiag.Labels[0].Style);
        Assert.Equal(5, missingSemiNiceDiag.Labels[0].Span.GetStartLine(text) + 1);
        Assert.Equal(9, missingSemiNiceDiag.Labels[0].Span.GetStartColumn(text) + 1);
        Assert.Equal(DiagnosticLabelStyle.Primary, missingSemiNiceDiag.Labels[1].Style);
        Assert.Equal(6, missingSemiNiceDiag.Labels[1].Span.GetStartLine(text) + 1);
        Assert.Equal(1, missingSemiNiceDiag.Labels[1].Span.GetStartColumn(text) + 1);

        // 3. "foo.;" should have diagnostic pointing to ';' (1-based line 6:5)
        var missingIdentDiag = Assert.Single(diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0122");
        Assert.Equal(6, missingIdentDiag.Span.GetStartLine(text) + 1);
        Assert.Equal(5, missingIdentDiag.Span.GetStartColumn(text) + 1);
    }

    [Fact]
    public void Parse_MacroDeclaration_MultiArm()
    {
        var topLevel = ParseSingleTopLevel("""
            public macro $fact {
                (0) => 1;
                (@n: expr) => @n * $fact(@n - 1);
            }
            """, typeof(TopLevelMacroDeclaration));

        var macroDecl = Assert.IsType<TopLevelMacroDeclaration>(topLevel).Macro;
        Assert.Equal("fact", macroDecl.Name.Value);
        Assert.Equal(2, macroDecl.Arms.Count);

        var arm0 = macroDecl.Arms[0];
        Assert.NotNull(arm0.Pattern);
        Assert.Single(arm0.Pattern.Parameters);
        Assert.Equal(MacroParameterKind.Literal, arm0.Pattern.Parameters[0].Kind);

        var arm1 = macroDecl.Arms[1];
        Assert.NotNull(arm1.Pattern);
        Assert.Single(arm1.Pattern.Parameters);
        Assert.Equal("n", arm1.Pattern.Parameters[0].Name.Value);
        Assert.Equal(MacroParameterKind.Expression, arm1.Pattern.Parameters[0].Kind);
        Assert.False(arm1.Pattern.Parameters[0].IsVariadic);
    }

    [Fact]
    public void Parse_MacroDeclaration_ShorthandBody()
    {
        var topLevel = ParseSingleTopLevel("""
            public macro $Point2D {
                int32 x;
                int32 y;
            }
            """, typeof(TopLevelMacroDeclaration));

        var macroDecl = Assert.IsType<TopLevelMacroDeclaration>(topLevel).Macro;
        Assert.Equal("Point2D", macroDecl.Name.Value);
        var arm = Assert.Single(macroDecl.Arms);
        Assert.Null(arm.Pattern);
        Assert.False(arm.Template.IsExpression);
        Assert.NotEmpty(arm.Template.Tokens);
    }

    [Fact]
    public void Parse_MacroDeclaration_ArrowExpression()
    {
        var topLevel = ParseSingleTopLevel("""
            public macro $Answer => 42;
            """, typeof(TopLevelMacroDeclaration));

        var macroDecl = Assert.IsType<TopLevelMacroDeclaration>(topLevel).Macro;
        Assert.Equal("Answer", macroDecl.Name.Value);
        var arm = Assert.Single(macroDecl.Arms);
        Assert.Null(arm.Pattern);
        Assert.True(arm.Template.IsExpression);
        Assert.Single(arm.Template.Tokens);
        Assert.Equal("42", arm.Template.Tokens[0].Value);
    }

    [Fact]
    public void Parse_MacroDeclaration_VariadicParameter()
    {
        var topLevel = ParseSingleTopLevel("""
            public macro $ListInit {
                (@items: expr...) => [@items...];
            }
            """, typeof(TopLevelMacroDeclaration));

        var macroDecl = Assert.IsType<TopLevelMacroDeclaration>(topLevel).Macro;
        var arm = Assert.Single(macroDecl.Arms);
        Assert.NotNull(arm.Pattern);
        var param = Assert.Single(arm.Pattern.Parameters);
        Assert.Equal("items", param.Name.Value);
        Assert.Equal(MacroParameterKind.Expression, param.Kind);
        Assert.True(param.IsVariadic);
    }

    [Fact]
    public void Parse_MemberMacroInvocation_AndDeclaration()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Point
            {
                public macro $InnerMixin {
                    int32 z;
                }
                $Point2D;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var typeDecl = Assert.IsType<TopLevelTypeDeclaration>(Assert.Single(root.Members));
        var body = Assert.IsType<TypeBlockBody>(typeDecl.Type.Body);
        Assert.Equal(2, body.Members.Count);
        Assert.IsType<MemberMacroDeclaration>(body.Members[0]);
        var inv = Assert.IsType<MemberMacroInvocationDeclaration>(body.Members[1]);
        Assert.Equal("Point2D", inv.Invocation.Name.Value);
        Assert.Empty(inv.Invocation.Arguments);
    }

    [Fact]
    public void Parse_BacktickEscapedIdentifier_TreatedAsIdentifier()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct `class`
            {
                public int32 `macro`;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var typeDecl = Assert.IsType<TopLevelTypeDeclaration>(Assert.Single(root.Members));
        var simpleName = Assert.IsType<SimpleName>(typeDecl.Type.Name);
        Assert.Equal("class", simpleName.Name.Value);
    }

    [Fact]
    public void Parse_ContiguousCombinedOperator_ParsesAsCombined()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            var a = 1 == 2;
            var b = 3 <= 4;
            var c = 5 != 6;
            """);

        Assert.Empty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_SeparatedCombinedOperator_DoesNotParseAsCombined()
    {
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            var a = 1 = = 2;
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_SeparatedLessThanEquals_DoesNotParseAsCombined()
    {
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            var a = 1 < = 2;
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_ContiguousArrow_ParsesInMacroArm()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            macro $test => 42;
            macro $test2 {
                () => 42;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        Assert.Equal(2, root.Members.Count);
        var macro1 = Assert.IsType<TopLevelMacroDeclaration>(root.Members[0]);
        Assert.Equal(TokenKind.EqualsGreaterThan, macro1.Macro.Arms[0].ArrowToken!.Kind);
        var macro2 = Assert.IsType<TopLevelMacroDeclaration>(root.Members[1]);
        Assert.Equal(TokenKind.EqualsGreaterThan, macro2.Macro.Arms[0].ArrowToken!.Kind);
    }

    [Fact]
    public void Parse_SeparatedArrow_FailsInMacroArm()
    {
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            macro $test = > 42;
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);
        Assert.Contains(diagnostics.Diagnostics, d => d.Message.Contains("'=>'"));
    }

    [Fact]
    public void Parse_ArrowInUnexpectedExpressionContext_ReportsArrowInDiagnostic()
    {
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            var a = 1 => 2;
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);
        Assert.Contains(diagnostics.Diagnostics, d => d.Message.Contains("'=>'"));
    }
}