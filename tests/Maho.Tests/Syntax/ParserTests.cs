using System.Collections;
using System.Reflection;
using Maho;
using Maho.Diagnostics;
using Maho.Syntax;

namespace Maho.Tests;

public sealed partial class ParserTests
{
    [Theory]
    [InlineData("namespace Demo;", typeof(NamespaceDeclaration), typeof(NamespaceEmptyBody))]
    [InlineData("namespace Demo { public class Inner; }", typeof(NamespaceDeclaration), typeof(NamespaceBlockBody))]
    [InlineData("public class Box;", typeof(TopLevelTypeDeclaration), typeof(TypeEmptyBody))]
    [InlineData("public attribute Marker;", typeof(TopLevelAttributeDeclaration), null)]
    [InlineData("public class Box { public int Value; }", typeof(TopLevelTypeDeclaration), typeof(TypeBlockBody))]
    [InlineData("public static func Main() -> int;", typeof(TopLevelFunctionDeclaration), typeof(FunctionEmptyBody))]
    [InlineData("public static func Main() -> int { return 0; }", typeof(TopLevelFunctionDeclaration), typeof(FunctionBlockBody))]
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
    [InlineData("public static func Compute() -> int;", typeof(MemberFunctionDeclaration), typeof(FunctionEmptyBody))]
    [InlineData("public static func Compute() -> int { return 0; }", typeof(MemberFunctionDeclaration), typeof(FunctionBlockBody))]
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
    [InlineData("public static func Local() -> int;", typeof(LocalFunctionDeclaration), typeof(FunctionEmptyBody))]
    [InlineData("public static func Local() -> int { return 0; }", typeof(LocalFunctionDeclaration), typeof(FunctionBlockBody))]
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
    [InlineData("if (1) 2 else 3;", typeof(TopLevelExpressionStatement))]
    [InlineData("while (1) call();", typeof(TopLevelExpressionStatement))]
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
            public static func Test() -> void
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
        var exprStmt = Assert.IsType<LocalExpressionStatement>(Assert.Single(body.Locals));
        var blockStmt = Assert.IsType<BlockExpression>(exprStmt.Expression);

        Assert.Equal(3, blockStmt.Locals.Count);
        var localUsing = Assert.IsType<LocalUsingDirective>(blockStmt.Locals[0]);
        var localAlias = Assert.IsType<LocalAliasDeclaration>(blockStmt.Locals[1]);
        Assert.IsType<LocalVariableDeclarationStatement>(blockStmt.Locals[2]);

        Assert.Equal("Collections", Assert.IsType<SimpleName>(Assert.IsType<QualifiedName>(localUsing.Directive.Namespace).Parts[1]).Name.Value);
        Assert.Equal("IntList", Assert.IsType<SimpleName>(localAlias.Alias.Name).Name.Value);
    }

    [Fact]
    public void Parse_LocalBlock_RequiresSemicolonOnNonFinalExpressions()
    {
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            public static func Test() -> void
            {
                {
                    int x = 1;
                    x + 2
                    int y = 3;
                }
            }
            """);

        Assert.Contains(diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0120");
    }

    [Fact]
    public void Parse_LocalBlock_AllowsTrailingExpression()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static func Test() -> void
            {
                var val = {
                    int x = 1;
                    x + 2
                };
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_TrailingBlockExpressionInBlockContext()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static func Test() -> void
            {
                var val = {
                    int x = 1;
                    { x + 2 }
                };
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_StandaloneScopeBlockWithoutSemicolon()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static func Test() -> void
            {
                {
                    int x = 1;
                }
                int y = 2;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_IfStatementWithoutSemicolonWhenEndingWithBlock()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static func Test() -> void
            {
                if (1)
                {
                    int x = 1;
                }
                else
                {
                    int x = 2;
                }
                int y = 3;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_IfStatementWithEmbeddedExpressionAndSemicolon()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static func Test() -> void
            {
                if (1) call();
                int y = 3;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_FunctionBodyDisallowsTrailingExpressionAndReportsExpectedReturn()
    {
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            public static func Test() -> void
            {
                int x = 1;
                x + 2
            }
            """);

        var diag = Assert.Single(diagnostics.Diagnostics);
        Assert.Equal("MH0120", diag.DiagnosticCode);
        Assert.Contains("expected a return statement", diag.Message);
    }

    [Fact]
    public void Parse_FunctionBlockBody_RequiresSemicolonOnExpressions()
    {
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            public static func Test() -> void
            {
                int x = 1;
                x + 2
            }
            """);

        Assert.Contains(diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0120");
    }

    [Fact]
    public void Parse_BlockExpression_SupportsMultipleStatementsAndFinalExpression()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static func Test() -> void
            {
                var val = {
                    int x = 1;
                    call();
                    x + 2
                };
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var funcDecl = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        var body = Assert.IsType<FunctionBlockBody>(funcDecl.Function.Body);
        var varDecl = Assert.IsType<LocalVariableDeclarationStatement>(Assert.Single(body.Locals));
        var blockExpr = Assert.IsType<BlockExpression>(varDecl.Declaration.Declarator.Initializer!.Initializer);

        Assert.Equal(2, blockExpr.Locals.Count);
        Assert.IsType<LocalVariableDeclarationStatement>(blockExpr.Locals[0]);
        var callStmt = Assert.IsType<LocalExpressionStatement>(blockExpr.Locals[1]);
        Assert.False(callStmt.IsFinalExpression);
        Assert.NotNull(blockExpr.FinalExpression);
        Assert.IsType<BinaryExpression>(blockExpr.FinalExpression);
    }

    [Fact]
    public void Parse_WhileExpression_WithoutSemicolon_WhenBodyEndsWithBlock()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static func Test() -> void
            {
                while (true)
                {
                    call();
                }
                int next = 1;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var funcDecl = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        var body = Assert.IsType<FunctionBlockBody>(funcDecl.Function.Body);
        Assert.Equal(2, body.Locals.Count);
        var whileStmt = Assert.IsType<LocalExpressionStatement>(body.Locals[0]);
        var whileExpr = Assert.IsType<WhileExpression>(whileStmt.Expression);
        Assert.IsType<BlockExpression>(whileExpr.Body);
        Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[1]);
    }

    [Fact]
    public void Parse_WhileExpression_AsFinalExpressionInBlockExpression()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static func Test() -> void
            {
                var val = {
                    int x = 0;
                    while (x < 10)
                    {
                        x = x + 1;
                    }
                };
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var funcDecl = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        var body = Assert.IsType<FunctionBlockBody>(funcDecl.Function.Body);
        var varDecl = Assert.IsType<LocalVariableDeclarationStatement>(Assert.Single(body.Locals));
        var blockExpr = Assert.IsType<BlockExpression>(varDecl.Declaration.Declarator.Initializer!.Initializer);

        Assert.Single(blockExpr.Locals);
        Assert.NotNull(blockExpr.FinalExpression);
        var whileExpr = Assert.IsType<WhileExpression>(blockExpr.FinalExpression);
        Assert.IsType<BlockExpression>(whileExpr.Body);
    }

    [Fact]
    public void Parse_TopLevelWhileExpression_WithoutSemicolon_WhenBodyEndsWithBlock()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            while (true)
            {
                call();
            }
            int x = 1;
            """);

        Assert.Empty(diagnostics.Diagnostics);
        Assert.Equal(2, root.Members.Count);
        var whileStmt = Assert.IsType<TopLevelExpressionStatement>(root.Members[0]);
        var whileExpr = Assert.IsType<WhileExpression>(whileStmt.Expression);
        Assert.IsType<BlockExpression>(whileExpr.Body);
        Assert.IsType<TopLevelVariableDeclaration>(root.Members[1]);
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
    [InlineData("if (1) 2 else 3;", typeof(LocalExpressionStatement))]
    [InlineData("while (1) call();", typeof(LocalExpressionStatement))]
    [InlineData("{ int nested = 1; }", typeof(LocalExpressionStatement))]
    [InlineData("return 0;", typeof(LocalReturnStatement))]
    [InlineData(";", typeof(LocalEmptyStatement))]
    public void Parse_LocalStatementKinds(string source, Type expectedType)
    {
        Local statement = ParseSingleLocal(source, expectedType);
        Assert.IsType(expectedType, statement);
    }

    [Theory]
    [InlineData("PointerType * value;")]
    [InlineData("ReferenceType & value;")]
    public void Parse_TopLevelPointerAndReferenceDeclarations(string source)
    {
        TopLevel topLevel = ParseSingleTopLevel(source, typeof(TopLevelVariableDeclaration));
        TopLevelVariableDeclaration variable = Assert.IsType<TopLevelVariableDeclaration>(topLevel);
        Assert.NotNull(variable.Declaration.Declarator);
        Assert.IsType<ModifiedType>(variable.Declaration.Type);
    }

    [Theory]
    [InlineData("PointerType * value;")]
    [InlineData("ReferenceType & value;")]
    public void Parse_LocalPointerAndReferenceDeclarations(string source)
    {
        Local local = ParseSingleLocal(source, typeof(LocalVariableDeclarationStatement));
        LocalVariableDeclarationStatement variable = Assert.IsType<LocalVariableDeclarationStatement>(local);
        Assert.NotNull(variable.Declaration.Declarator);
        Assert.IsType<ModifiedType>(variable.Declaration.Type);
    }

    [Fact]
    public void Parse_StatementParenthesizedExpression_EscapeHatch()
    {
        Local local = ParseSingleLocal("(A * B);", typeof(LocalExpressionStatement));
        LocalExpressionStatement exprStmt = Assert.IsType<LocalExpressionStatement>(local);
        ParenthesizedExpression paren = Assert.IsType<ParenthesizedExpression>(exprStmt.Expression);
        BinaryExpression binary = Assert.IsType<BinaryExpression>(paren.Expression);
        Assert.Equal(TokenKind.Asterisk, binary.OperatorToken.Kind);
    }

    [Fact]
    public void Parse_StatementParenthesizedCollectionExpression_EscapeHatch()
    {
        Local local = ParseSingleLocal("([x]);", typeof(LocalExpressionStatement));
        LocalExpressionStatement exprStmt = Assert.IsType<LocalExpressionStatement>(local);
        ParenthesizedExpression paren = Assert.IsType<ParenthesizedExpression>(exprStmt.Expression);
        CollectionExpression coll = Assert.IsType<CollectionExpression>(paren.Expression);
        Assert.Single(coll.Expressions);

        Local multiLocal = ParseSingleLocal("([1, 2, 3]);", typeof(LocalExpressionStatement));
        LocalExpressionStatement multiExprStmt = Assert.IsType<LocalExpressionStatement>(multiLocal);
        ParenthesizedExpression multiParen = Assert.IsType<ParenthesizedExpression>(multiExprStmt.Expression);
        CollectionExpression multiColl = Assert.IsType<CollectionExpression>(multiParen.Expression);
        Assert.Equal(3, multiColl.Expressions.Count);
    }

    [Fact]
    public void Parse_TopLevelParenthesizedCollectionExpression_EscapeHatch()
    {
        TopLevel topLevel = ParseSingleTopLevel("""
            #pragma toplevel enable
            ([x]);
            """, typeof(TopLevelExpressionStatement));

        TopLevelExpressionStatement exprStmt = Assert.IsType<TopLevelExpressionStatement>(topLevel);
        ParenthesizedExpression paren = Assert.IsType<ParenthesizedExpression>(exprStmt.Expression);
        CollectionExpression coll = Assert.IsType<CollectionExpression>(paren.Expression);
        Assert.Single(coll.Expressions);
    }

    [Fact]
    public void Parse_StatementBracketDisambiguation_ParsesExpressionsWhenFollowedByDotOperatorSemicolon()
    {
        // [x]; parses as a collection expression statement
        Local local1 = ParseSingleLocal("[x];", typeof(LocalExpressionStatement));
        LocalExpressionStatement exprStmt1 = Assert.IsType<LocalExpressionStatement>(local1);
        CollectionExpression colExpr1 = Assert.IsType<CollectionExpression>(exprStmt1.Expression);
        Assert.Single(colExpr1.Expressions);

        // []; empty collection expression statement
        Local localEmpty = ParseSingleLocal("[];", typeof(LocalExpressionStatement));
        LocalExpressionStatement exprStmtEmpty = Assert.IsType<LocalExpressionStatement>(localEmpty);
        CollectionExpression colExprEmpty = Assert.IsType<CollectionExpression>(exprStmtEmpty.Expression);
        Assert.Empty(colExprEmpty.Expressions);

        // [1, 2, 3];
        Local local2 = ParseSingleLocal("[1, 2, 3];", typeof(LocalExpressionStatement));
        LocalExpressionStatement exprStmt2 = Assert.IsType<LocalExpressionStatement>(local2);
        CollectionExpression colExpr2 = Assert.IsType<CollectionExpression>(exprStmt2.Expression);
        Assert.Equal(3, colExpr2.Expressions.Count);

        // [x].foo;
        Local local3 = ParseSingleLocal("[x].foo;", typeof(LocalExpressionStatement));
        LocalExpressionStatement exprStmt3 = Assert.IsType<LocalExpressionStatement>(local3);
        Assert.IsType<MemberAccessExpression>(exprStmt3.Expression);

        // [x] + 1;
        Local local4 = ParseSingleLocal("[x] + 1;", typeof(LocalExpressionStatement));
        LocalExpressionStatement exprStmt4 = Assert.IsType<LocalExpressionStatement>(local4);
        Assert.IsType<BinaryExpression>(exprStmt4.Expression);

        // [1, 2][0];
        Local local5 = ParseSingleLocal("[1, 2][0];", typeof(LocalExpressionStatement));
        LocalExpressionStatement exprStmt5 = Assert.IsType<LocalExpressionStatement>(local5);
        Assert.IsType<IndexExpression>(exprStmt5.Expression);

        // [][0];
        Local local6 = ParseSingleLocal("[][0];", typeof(LocalExpressionStatement));
        LocalExpressionStatement exprStmt6 = Assert.IsType<LocalExpressionStatement>(local6);
        Assert.IsType<IndexExpression>(exprStmt6.Expression);

        // No diagnostics for [x]; inside a function
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            func Foo() -> void
            {
                [x];
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_StatementBracketDisambiguation_ParsesAttributeUsageWhenFollowedByIdentifierOrBrace()
    {
        // [Marker] int x = 1;
        Local local1 = ParseSingleLocal("[Marker] int x = 1;", typeof(LocalVariableDeclarationStatement));
        LocalVariableDeclarationStatement varStmt1 = Assert.IsType<LocalVariableDeclarationStatement>(local1);
        Assert.Single(varStmt1.Declaration.Attributes);
        Assert.Equal("Marker", Assert.IsType<SimpleName>(Assert.Single(varStmt1.Declaration.Attributes[0].Attributes).Name).Name.Value);

        // [] int x = 1; parses as empty attribute list annotation on that identifier
        Local localEmpty = ParseSingleLocal("[] int x = 1;", typeof(LocalVariableDeclarationStatement));
        LocalVariableDeclarationStatement varStmtEmpty = Assert.IsType<LocalVariableDeclarationStatement>(localEmpty);
        Assert.Single(varStmtEmpty.Declaration.Attributes);
        Assert.Empty(varStmtEmpty.Declaration.Attributes[0].Attributes);

        // [][] int x = 1; parses as two empty attribute lists
        Local localDoubleEmpty = ParseSingleLocal("[][] int x = 1;", typeof(LocalVariableDeclarationStatement));
        LocalVariableDeclarationStatement varStmtDoubleEmpty = Assert.IsType<LocalVariableDeclarationStatement>(localDoubleEmpty);
        Assert.Equal(2, varStmtDoubleEmpty.Declaration.Attributes.Count);
        Assert.Empty(varStmtDoubleEmpty.Declaration.Attributes[0].Attributes);
        Assert.Empty(varStmtDoubleEmpty.Declaration.Attributes[1].Attributes);

        // [Marker] { } parses as attributed block expression statement
        Local localBlock = ParseSingleLocal("[Marker] { int x = 1; }", typeof(LocalExpressionStatement));
        LocalExpressionStatement blockStmt = Assert.IsType<LocalExpressionStatement>(localBlock);
        BlockExpression blockExpr = Assert.IsType<BlockExpression>(blockStmt.Expression);
        Assert.Single(blockExpr.Attributes);
        Assert.Equal("Marker", Assert.IsType<SimpleName>(Assert.Single(blockExpr.Attributes[0].Attributes).Name).Name.Value);

        // [] { } parses as block expression statement with empty attribute list
        Local localEmptyBlock = ParseSingleLocal("[] { int x = 1; }", typeof(LocalExpressionStatement));
        LocalExpressionStatement emptyBlockStmt = Assert.IsType<LocalExpressionStatement>(localEmptyBlock);
        BlockExpression emptyBlockExpr = Assert.IsType<BlockExpression>(emptyBlockStmt.Expression);
        Assert.Single(emptyBlockExpr.Attributes);
        Assert.Empty(emptyBlockExpr.Attributes[0].Attributes);

        // [][] { } parses as block expression statement with two empty attribute lists
        Local localDoubleEmptyBlock = ParseSingleLocal("[][] { int x = 1; }", typeof(LocalExpressionStatement));
        LocalExpressionStatement doubleEmptyBlockStmt = Assert.IsType<LocalExpressionStatement>(localDoubleEmptyBlock);
        BlockExpression doubleEmptyBlockExpr = Assert.IsType<BlockExpression>(doubleEmptyBlockStmt.Expression);
        Assert.Equal(2, doubleEmptyBlockExpr.Attributes.Count);
        Assert.Empty(doubleEmptyBlockExpr.Attributes[0].Attributes);
        Assert.Empty(doubleEmptyBlockExpr.Attributes[1].Attributes);
    }

    [Fact]
    public void Parse_StatementBracketDisambiguation_UnclosedBracketReportsError()
    {
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            func Foo() -> void
            {
                [x][;
            }
            """);

        Assert.Contains(diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0120");
    }

    [Fact]
    public void Parse_TopLevelBracketDisambiguation()
    {
        // Top-level [x]; with pragma parses as top-level expression statement
        var (_, diag1, _, root1) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            [x];
            """);
        Assert.Empty(diag1.Diagnostics);
        Assert.Single(root1.Members);
        var topLevelStmt = Assert.IsType<TopLevelExpressionStatement>(root1.Members[0]);
        Assert.IsType<CollectionExpression>(topLevelStmt.Expression);

        // Top-level [] int x = 1; parses as variable declaration with empty attribute list
        TopLevel topLevel1 = ParseSingleTopLevel("[] int x = 1;", typeof(TopLevelVariableDeclaration));
        TopLevelVariableDeclaration topVar = Assert.IsType<TopLevelVariableDeclaration>(topLevel1);
        Assert.Single(topVar.Declaration.Attributes);
        Assert.Empty(topVar.Declaration.Attributes[0].Attributes);

        // Top-level [][] int x = 1; parses as variable declaration with two empty attribute lists
        TopLevel topLevel2 = ParseSingleTopLevel("[][] int x = 1;", typeof(TopLevelVariableDeclaration));
        TopLevelVariableDeclaration topVar2 = Assert.IsType<TopLevelVariableDeclaration>(topLevel2);
        Assert.Equal(2, topVar2.Declaration.Attributes.Count);
        Assert.Empty(topVar2.Declaration.Attributes[0].Attributes);
        Assert.Empty(topVar2.Declaration.Attributes[1].Attributes);

        // Top-level [Marker] int x = 1; parses as variable declaration with attribute
        TopLevel topLevelAttributed = ParseSingleTopLevel("[Marker] int x = 1;", typeof(TopLevelVariableDeclaration));
        TopLevelVariableDeclaration attributedVar = Assert.IsType<TopLevelVariableDeclaration>(topLevelAttributed);
        Assert.Single(attributedVar.Declaration.Attributes);
        Assert.Equal("Marker", Assert.IsType<SimpleName>(Assert.Single(attributedVar.Declaration.Attributes[0].Attributes).Name).Name.Value);
    }

    [Fact]
    public void Parse_StatementTupleExpression()
    {
        Local local = ParseSingleLocal("(A * B, C);", typeof(LocalExpressionStatement));
        LocalExpressionStatement exprStmt = Assert.IsType<LocalExpressionStatement>(local);
        TupleExpression tuple = Assert.IsType<TupleExpression>(exprStmt.Expression);
        Assert.Equal(2, tuple.Arguments.Count);
    }

    [Fact]
    public void Parse_StatementTupleDeclaration()
    {
        Local local = ParseSingleLocal("(int, string) pair = (1, \"hello\");", typeof(LocalVariableDeclarationStatement));
        LocalVariableDeclarationStatement varStmt = Assert.IsType<LocalVariableDeclarationStatement>(local);
        TupleType tupleType = Assert.IsType<TupleType>(varStmt.Declaration.Type);
        Assert.Equal(2, tupleType.Elements.Count);
        Assert.NotNull(varStmt.Declaration.Declarator);
        Assert.IsType<TupleExpression>(varStmt.Declaration.Declarator.Initializer!.Initializer);
    }

    [Fact]
    public void Parse_ConditionalWithDeclarationAndExpression()
    {
        Local localIf = ParseSingleLocal("if (int x = 1; x > 0) return;", typeof(LocalExpressionStatement));
        LocalExpressionStatement ifStmt = Assert.IsType<LocalExpressionStatement>(localIf);
        IfExpression ifExpr = Assert.IsType<IfExpression>(ifStmt.Expression);
        Assert.NotNull(ifExpr.Declaration);
        Assert.NotNull(ifExpr.Semicolon);
        Assert.IsType<BinaryExpression>(ifExpr.Condition);

        Local localWhile = ParseSingleLocal("while (int x = 1; x > 0) call();", typeof(LocalExpressionStatement));
        LocalExpressionStatement whileStmt = Assert.IsType<LocalExpressionStatement>(localWhile);
        WhileExpression whileExpr = Assert.IsType<WhileExpression>(whileStmt.Expression);
        Assert.NotNull(whileExpr.Declaration);
        Assert.NotNull(whileExpr.Semicolon);
        Assert.IsType<BinaryExpression>(whileExpr.Condition);
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
    public void Parse_GlobalAndConstBlocks_PreserveAttachedKeywordAndMembers()
    {
        TopLevelBlockDeclaration block = Assert.IsType<TopLevelBlockDeclaration>(ParseSingleTopLevel("""
            global
            {
                public struct Example
                {
                    const
                    {
                        func Func() -> void { }
                    }
                }
            }
            """, typeof(TopLevelBlockDeclaration)));

        Assert.NotNull(block.AttachedKeyword);
        Assert.Equal("global", block.AttachedKeyword.Value);

        TopLevelTypeDeclaration topLevelType = Assert.IsType<TopLevelTypeDeclaration>(Assert.Single(block.Members));
        TypeBlockBody typeBody = Assert.IsType<TypeBlockBody>(topLevelType.Type.Body);
        MemberBlockDeclaration memberBlock = Assert.IsType<MemberBlockDeclaration>(Assert.Single(typeBody.Members));

        Assert.NotNull(memberBlock.AttachedKeyword);
        Assert.Equal("const", memberBlock.AttachedKeyword.Value);
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
    public void Parse_Blocks_WorkInNamespaceAndLocalScopes()
    {
        NamespaceDeclaration @namespace = Assert.IsType<NamespaceDeclaration>(ParseSingleTopLevel("""
            namespace Demo
            {
                global
                {
                    int value;
                }
            }
            """, typeof(NamespaceDeclaration)));

        NamespaceBlockBody namespaceBody = Assert.IsType<NamespaceBlockBody>(@namespace.Body);
        TopLevelBlockDeclaration topLevelBlock = Assert.IsType<TopLevelBlockDeclaration>(Assert.Single(namespaceBody.Members));

        Assert.NotNull(topLevelBlock.AttachedKeyword);
        Assert.Equal("global", topLevelBlock.AttachedKeyword.Value);

        LocalExpressionStatement localBlock = Assert.IsType<LocalExpressionStatement>(ParseSingleLocal("""
            [Attribute]
            unsafe
            {
                int nested;
            }
            """, typeof(LocalExpressionStatement)));

        BlockExpression blockExpr = Assert.IsType<BlockExpression>(localBlock.Expression);
        Assert.Single(blockExpr.Attributes);
        Assert.Contains(blockExpr.Modifiers, token => token.MatchingKind == MatchingKeywordKind.Unsafe);
    }

    [Fact]
    public void Parse_ObjectCreationWithClause_AttachesToConstructorCall()
    {
        LocalVariableDeclarationStatement local = Assert.IsType<LocalVariableDeclarationStatement>(ParseSingleLocal("""
            SomeType value = new SomeType(ctorValue) with { prop = "val" };
            """, typeof(LocalVariableDeclarationStatement)));

        Assert.NotNull(local.Declaration.Declarator);
        ConstructorCallExpression constructor = Assert.IsType<ConstructorCallExpression>(local.Declaration.Declarator.Initializer?.Initializer);
        ObjectWithClause withClause = Assert.IsType<ObjectWithClause>(constructor.WithClause);
        AssignmentExpression assignment = Assert.IsType<AssignmentExpression>(Assert.Single(withClause.Initializer.Expressions));

        Assert.Equal("prop", Assert.IsType<IdentifierNameExpression>(assignment.LhsExpression).Identifier.Value);
    }

    [Fact]
    public void Parse_ConstructorCall_WithAllocationTypeArgumentsAndArguments_ParsesCorrectly()
    {
        LocalVariableDeclarationStatement local = Assert.IsType<LocalVariableDeclarationStatement>(ParseSingleLocal("""
            var p = new<Arena>(pool) Point(10, 20);
            """, typeof(LocalVariableDeclarationStatement)));

        ConstructorCallExpression constructor = Assert.IsType<ConstructorCallExpression>(local.Declaration.Declarator.Initializer?.Initializer);
        Assert.Equal(ObjectCreationKind.New, constructor.Kind);
        Assert.NotNull(constructor.AllocationTypeArguments);
        Assert.Single(constructor.AllocationTypeArguments.Value);
        NamedExpressionGenericArgument genericArg = Assert.IsType<NamedExpressionGenericArgument>(constructor.AllocationTypeArguments.Value[0]);
        Assert.Equal("Arena", Assert.IsType<IdentifierNameExpression>(genericArg.Expression).Identifier.Value);
        Assert.NotNull(constructor.AllocationArguments);
        Assert.Single(constructor.AllocationArguments.Value);
        Assert.Equal("pool", Assert.IsType<IdentifierNameExpression>(constructor.AllocationArguments.Value[0]).Identifier.Value);
        Assert.Equal("Point", Assert.IsType<SimpleType>(constructor.Type).Name.Value);
        Assert.Equal(2, constructor.Arguments.Count);
    }

    [Fact]
    public void Parse_ConstructorCall_WithAllocationTypeArgumentsOnly_ParsesCorrectly()
    {
        LocalVariableDeclarationStatement local = Assert.IsType<LocalVariableDeclarationStatement>(ParseSingleLocal("""
            var p = new<Arena> Point(10, 20);
            """, typeof(LocalVariableDeclarationStatement)));

        ConstructorCallExpression constructor = Assert.IsType<ConstructorCallExpression>(local.Declaration.Declarator.Initializer?.Initializer);
        Assert.Equal(ObjectCreationKind.New, constructor.Kind);
        Assert.NotNull(constructor.AllocationTypeArguments);
        Assert.Single(constructor.AllocationTypeArguments.Value);
        NamedExpressionGenericArgument genericArg = Assert.IsType<NamedExpressionGenericArgument>(constructor.AllocationTypeArguments.Value[0]);
        Assert.Equal("Arena", Assert.IsType<IdentifierNameExpression>(genericArg.Expression).Identifier.Value);
        Assert.Null(constructor.AllocationArguments);
        Assert.Equal("Point", Assert.IsType<SimpleType>(constructor.Type).Name.Value);
        Assert.Equal(2, constructor.Arguments.Count);
    }

    [Fact]
    public void Parse_ConstructorCall_WithAllocationArgumentsOnly_ParsesCorrectly()
    {
        LocalVariableDeclarationStatement local = Assert.IsType<LocalVariableDeclarationStatement>(ParseSingleLocal("""
            var p = new(pool) Point(10, 20);
            """, typeof(LocalVariableDeclarationStatement)));

        ConstructorCallExpression constructor = Assert.IsType<ConstructorCallExpression>(local.Declaration.Declarator.Initializer?.Initializer);
        Assert.Equal(ObjectCreationKind.New, constructor.Kind);
        Assert.Null(constructor.AllocationTypeArguments);
        Assert.NotNull(constructor.AllocationArguments);
        Assert.Single(constructor.AllocationArguments.Value);
        Assert.Equal("pool", Assert.IsType<IdentifierNameExpression>(constructor.AllocationArguments.Value[0]).Identifier.Value);
        Assert.Equal("Point", Assert.IsType<SimpleType>(constructor.Type).Name.Value);
        Assert.Equal(2, constructor.Arguments.Count);
    }

    [Fact]
    public void Parse_ConstructorCall_PutWithAllocationTypeArgumentsAndArguments_ParsesCorrectly()
    {
        LocalVariableDeclarationStatement local = Assert.IsType<LocalVariableDeclarationStatement>(ParseSingleLocal("""
            var p = put<StackFrame>(offset) Point(10, 20);
            """, typeof(LocalVariableDeclarationStatement)));

        ConstructorCallExpression constructor = Assert.IsType<ConstructorCallExpression>(local.Declaration.Declarator.Initializer?.Initializer);
        Assert.Equal(ObjectCreationKind.Put, constructor.Kind);
        Assert.NotNull(constructor.AllocationTypeArguments);
        Assert.Single(constructor.AllocationTypeArguments.Value);
        NamedExpressionGenericArgument genericArg = Assert.IsType<NamedExpressionGenericArgument>(constructor.AllocationTypeArguments.Value[0]);
        Assert.Equal("StackFrame", Assert.IsType<IdentifierNameExpression>(genericArg.Expression).Identifier.Value);
        Assert.NotNull(constructor.AllocationArguments);
        Assert.Single(constructor.AllocationArguments.Value);
        Assert.Equal("offset", Assert.IsType<IdentifierNameExpression>(constructor.AllocationArguments.Value[0]).Identifier.Value);
        Assert.Equal("Point", Assert.IsType<SimpleType>(constructor.Type).Name.Value);
        Assert.Equal(2, constructor.Arguments.Count);
    }

    [Fact]
    public void Parse_LiteralFunctionDeclaration_IntrinsicAndUserDefined_ParsesCorrectly()
    {
        var u64Lit = Assert.IsType<MemberFunctionDeclaration>(ParseSingleMember("""
            [IntrinsicFunction("UInt64.<u64>")]
            public literal u64(int V);
            """, typeof(MemberFunctionDeclaration)));
        Assert.Equal(SpecialFunctionKind.Literal, u64Lit.Function.SpecialKind);
        Assert.Equal("u64", Assert.IsType<SimpleName>(u64Lit.Function.Signature.Identifier).Name.Value);
        Assert.Single(u64Lit.Function.Signature.Parameters);
        Assert.IsType<FunctionEmptyBody>(u64Lit.Function.Body);

        var distLit = Assert.IsType<MemberFunctionDeclaration>(ParseSingleMember("""
            public literal km(int V) => Distance.FromKm(V);
            """, typeof(MemberFunctionDeclaration)));
        Assert.Equal(SpecialFunctionKind.Literal, distLit.Function.SpecialKind);
        Assert.Equal("km", Assert.IsType<SimpleName>(distLit.Function.Signature.Identifier).Name.Value);
        Assert.IsType<FunctionLambdaBody>(distLit.Function.Body);
    }

    [Fact]
    public void Parse_ObjectCreationWithClause_AttachesToArrayCreation()
    {
        LocalVariableDeclarationStatement local = Assert.IsType<LocalVariableDeclarationStatement>(ParseSingleLocal("""
            int[] arr = put int[10] with { SomeProp = someVal };
            """, typeof(LocalVariableDeclarationStatement)));

        Assert.NotNull(local.Declaration.Declarator);
        ArrayCreationExpression array = Assert.IsType<ArrayCreationExpression>(local.Declaration.Declarator.Initializer?.Initializer);
        ObjectWithClause withClause = Assert.IsType<ObjectWithClause>(array.WithClause);
        AssignmentExpression assignment = Assert.IsType<AssignmentExpression>(Assert.Single(withClause.Initializer.Expressions));

        Assert.Equal("SomeProp", Assert.IsType<IdentifierNameExpression>(assignment.LhsExpression).Identifier.Value);
    }

}
