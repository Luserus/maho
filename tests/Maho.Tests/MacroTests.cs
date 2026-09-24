using Maho.Syntax;

namespace Maho.Tests;

public sealed class MacroTests
{
    [Fact]
    public void ExpressionMacro_SimpleExpansion_Succeeds()
    {
        var compilation = Compilation.FromSource("""
            public macro $add {
                (@a: expr, @b: expr) => @a + @b;
            }

            public class Host
            {
                public struct int32;

                public int32 Test()
                {
                    return $add(1, 2);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var host = Assert.Single(compilation.SyntaxTrees[0].Roots[0].Members.OfType<TopLevelTypeDeclaration>());
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var ret = Assert.Single(funcBody.Locals.OfType<LocalReturnStatement>());
        var bin = Assert.IsType<BinaryExpression>(ret.Statement.Expression);
        Assert.NotNull(bin.ExpansionOrigin);
        Assert.Equal("add", bin.ExpansionOrigin.MacroName.ToString());
    }

    [Fact]
    public void ExpressionMacro_RecursiveExpansion_ReachesBaseCase()
    {
        var compilation = Compilation.FromSource("""
            public macro $nest {
                (0) => 42;
                (1) => $nest(0);
            }

            public class Host
            {
                public struct int32;

                public int32 Test()
                {
                    return $nest(1);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var host = Assert.Single(compilation.SyntaxTrees[0].Roots[0].Members.OfType<TopLevelTypeDeclaration>());
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var ret = Assert.Single(funcBody.Locals.OfType<LocalReturnStatement>());
        var lit = Assert.IsType<LiteralExpression>(ret.Statement.Expression);
        Assert.Equal("42", lit.Literal.Value);
    }

    [Fact]
    public void StatementMacro_MultiStatementAndHygiene_Succeeds()
    {
        var compilation = Compilation.FromSource("""
            public macro $swap {
                (@a: ident, @b: ident) => {
                    var temp = @a;
                    @a = @b;
                    @b = temp;
                }
            }

            public class Host
            {
                public struct void;
                public struct int32;

                public void Test()
                {
                    int32 x = 1;
                    int32 y = 2;
                    $swap(x, y);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var host = Assert.Single(compilation.SyntaxTrees[0].Roots[0].Members.OfType<TopLevelTypeDeclaration>());
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);

        // Original 2 variable declarations + 3 expanded statements from $swap
        Assert.Equal(5, funcBody.Locals.Count);

        // Third statement is 'var temp__m1 = x;'
        var varDecl = Assert.IsType<LocalVariableDeclarationStatement>(funcBody.Locals[2]);
        var declarator = Assert.Single(varDecl.Declaration.Declarators);
        Assert.StartsWith("temp__m", declarator.Identifier.ToString());
    }

    [Fact]
    public void MemberMacro_MixinExpandsFieldsIntoStruct()
    {
        var compilation = Compilation.FromSource("""
            public macro $Point2D {
                int32 x;
                int32 y;
            }

            public struct Point
            {
                public struct int32;

                $Point2D;
            }
            """);

        Assert.False(compilation.HasErrors);
        var structDecl = Assert.Single(compilation.SyntaxTrees[0].Roots[0].Members.OfType<TopLevelTypeDeclaration>());
        var body = Assert.IsType<TypeBlockBody>(structDecl.Type.Body);

        // Should contain two fields expanded from $Point2D;
        var fields = body.Members.OfType<MemberFieldDeclaration>().ToList();
        Assert.Equal(2, fields.Count);

        var names = fields.SelectMany(f => f.Declaration.Declarators).Select(d => d.Identifier.ToString()).ToList();
        Assert.Contains("x", names);
        Assert.Contains("y", names);

        // Symbol discovery should have found both fields in the compilation context
        var fieldSymbols = compilation.Context?.FieldSymbols.Select(f => f.Name.ToString()).ToList();
        Assert.NotNull(fieldSymbols);
        Assert.Contains("x", fieldSymbols);
        Assert.Contains("y", fieldSymbols);
    }

    [Fact]
    public void VariadicMacro_PackSpreadInCollection_Succeeds()
    {
        var compilation = Compilation.FromSource("""
            public macro $wrap {
                (@items: expr...) => [@items...];
            }

            public class Host
            {
                public struct void;

                public void Test()
                {
                    var arr = $wrap(10, 20, 30);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var host = Assert.Single(compilation.SyntaxTrees[0].Roots[0].Members.OfType<TopLevelTypeDeclaration>());
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);

        var varStmt = Assert.Single(funcBody.Locals.OfType<LocalVariableDeclarationStatement>());
        var collExpr = Assert.IsType<CollectionExpression>(varStmt.Declaration.Declarators[0].Initializer!.Initializer);
        Assert.Equal(3, collExpr.Expressions.Count);
    }

    [Fact]
    public void VariadicMacro_StatementRepetitionUnrolling_Succeeds()
    {
        var compilation = Compilation.FromSource("""
            public macro $call_all {
                (@fn: ident, @items: expr...) => {
                    $(@fn(@item);)...
                }
            }

            public class Host
            {
                public struct void;

                public void Test()
                {
                    $call_all(print, 1, 2, 3);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var host = Assert.Single(compilation.SyntaxTrees[0].Roots[0].Members.OfType<TopLevelTypeDeclaration>());
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);

        Assert.Equal(3, funcBody.Locals.Count);
        Assert.All(funcBody.Locals, l =>
        {
            var exprStmt = Assert.IsType<LocalExpressionStatement>(l);
            var call = Assert.IsType<CallExpression>(exprStmt.Expression);
            var id = Assert.IsType<IdentifierNameExpression>(call.Callee);
            Assert.Equal("print", id.Identifier.Value);
        });
    }

    [Fact]
    public void Diagnostic_MacroRecursionLimitExceeded_EmitsMH0600()
    {
        var options = new CompilationOptions { MacroRecursionLimit = 4 };
        var compilation = Compilation.FromSource("""
            public macro $loop {
                () => $loop();
            }

            public class Host
            {
                public int32 Test()
                {
                    return $loop();
                }
            }
            """, options: options);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0600");
        Assert.Contains("recursion limit of 4 exceeded while expanding macro '$loop'", diag.Message);
    }

    [Fact]
    public void Diagnostic_NoMatchingMacroArm_EmitsMH0601()
    {
        var compilation = Compilation.FromSource("""
            public macro $exact {
                (1) => 10;
            }

            public class Host
            {
                public int32 Test()
                {
                    return $exact(99);
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0601");
        Assert.Contains("no matching arm found for macro '$exact'", diag.Message);
    }

    [Fact]
    public void Diagnostic_UnresolvedMacro_EmitsMH0602()
    {
        var compilation = Compilation.FromSource("""
            public class Host
            {
                public int32 Test()
                {
                    return $unknown_macro(123);
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0602");
        Assert.Contains("could not resolve macro '$unknown_macro'", diag.Message);
        Assert.Equal("$unknown_macro".Length, diag.Span.Length);
        Assert.Equal("$unknown_macro".Length, Assert.Single(diag.Labels).Span.Length);
    }

    [Fact]
    public void Diagnostic_UnresolvedMacro_ReportedOnceEvenWhenOtherMacrosExpandIteratively()
    {
        var compilation = Compilation.FromSource("""
            public macro $valid {
                () => { 42 }
            }

            public class Host
            {
                public int32 Test()
                {
                    int32 a = $valid();
                    $Invalid;
                    return a;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        // $Invalid should only be reported ONCE even though $valid causes a second expansion iteration
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0602");
        Assert.Contains("could not resolve macro '$Invalid'", diag.Message);
        Assert.Equal("$Invalid".Length, diag.Span.Length);
    }

    [Fact]
    public void Diagnostic_InvalidMacroContext_EmitsMH0603()
    {
        var compilation = Compilation.FromSource("""
            public macro $block_macro {
                () => {
                    int32 a = 1;
                }
            }

            public class Host
            {
                public int32 Test()
                {
                    return $block_macro();
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0603");
        Assert.Contains("macro '$block_macro' produces statements and cannot be used in an expression context", diag.Message);
    }

    [Fact]
    public void ParameterlessMacro_InvokedWithoutParentheses_Succeeds()
    {
        var compilation = Compilation.FromSource("""
            public macro $here => 42;

            public class Host
            {
                public struct int32;

                public int32 Test()
                {
                    return $here;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var host = Assert.Single(compilation.SyntaxTrees[0].Roots[0].Members.OfType<TopLevelTypeDeclaration>());
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var ret = Assert.Single(funcBody.Locals.OfType<LocalReturnStatement>());
        var lit = Assert.IsType<LiteralExpression>(ret.Statement.Expression);
        Assert.Equal("42", lit.Literal.Value);
    }

    [Fact]
    public void EscapedIdentifier_MacroDeclarationAndInvocation_Succeeds()
    {
        var compilation = Compilation.FromSource("""
            public macro $`macro` => 100;

            public class Host
            {
                public struct int32;

                public int32 Test()
                {
                    return $`macro`;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var host = Assert.Single(compilation.SyntaxTrees[0].Roots[0].Members.OfType<TopLevelTypeDeclaration>());
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var ret = Assert.Single(funcBody.Locals.OfType<LocalReturnStatement>());
        var lit = Assert.IsType<LiteralExpression>(ret.Statement.Expression);
        Assert.Equal("100", lit.Literal.Value);
    }

    [Fact]
    public void MacroInNamespace_ResolvesAndExpands()
    {
        var compilation = Compilation.FromSource("""
            namespace Sample
            {
                public macro $sq {
                    (@x: expr) => @x * @x;
                }

                public class Calculator
                {
                    public struct int32;

                    public int32 Calc()
                    {
                        return $sq(7);
                    }
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var ns = Assert.Single(compilation.SyntaxTrees[0].Roots[0].Members.OfType<NamespaceDeclaration>());
        var nsBody = Assert.IsType<NamespaceBlockBody>(ns.Body);
        var calc = Assert.Single(nsBody.Members.OfType<TopLevelTypeDeclaration>());
        var typeBody = Assert.IsType<TypeBlockBody>(calc.Type.Body);
        var method = Assert.Single(typeBody.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var ret = Assert.Single(funcBody.Locals.OfType<LocalReturnStatement>());
        var bin = Assert.IsType<BinaryExpression>(ret.Statement.Expression);
        Assert.Equal("sq", bin.ExpansionOrigin?.MacroName.ToString());
    }

    [Fact]
    public void MacroRecursionLimit_ZeroMeansUnbounded_Succeeds()
    {
        // 5-step recursive chain: $step5 -> $step4 -> $step3 -> $step2 -> $step1 -> 777
        string source = """
            public macro $step {
                (1) => 777;
                (2) => $step(1);
                (3) => $step(2);
                (4) => $step(3);
                (5) => $step(4);
            }

            public class Host
            {
                public struct int32;

                public int32 Test()
                {
                    return $step(5);
                }
            }
            """;

        // Limit = 2 should fail
        var limitedComp = Compilation.FromSource(source, options: new CompilationOptions { MacroRecursionLimit = 2 });
        Assert.True(limitedComp.HasErrors);
        Assert.Contains(limitedComp.Diagnostics, d => d.Code == "MH0600");

        // Limit = 0 (unbounded) should succeed
        var unboundedComp = Compilation.FromSource(source, options: new CompilationOptions { MacroRecursionLimit = 0 });
        Assert.False(unboundedComp.HasErrors);
        var host = Assert.Single(unboundedComp.SyntaxTrees[0].Roots[0].Members.OfType<TopLevelTypeDeclaration>());
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var ret = Assert.Single(funcBody.Locals.OfType<LocalReturnStatement>());
        var lit = Assert.IsType<LiteralExpression>(ret.Statement.Expression);
        Assert.Equal("777", lit.Literal.Value);
    }

    [Fact]
    public void LocalMacroDeclaration_InFunctionBody_Succeeds()
    {
        var compilation = Compilation.FromSource("""
            public class Host
            {
                public struct int32;

                public int32 Test()
                {
                    macro $local_add {
                        (@a: expr, @b: expr) => @a + @b;
                    }

                    return $local_add(10, 20);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var host = Assert.Single(compilation.SyntaxTrees[0].Roots[0].Members.OfType<TopLevelTypeDeclaration>());
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var ret = Assert.Single(funcBody.Locals.OfType<LocalReturnStatement>());
        var bin = Assert.IsType<BinaryExpression>(ret.Statement.Expression);
        Assert.Equal("local_add", bin.ExpansionOrigin?.MacroName.ToString());
    }

    [Fact]
    public void BlockPattern_InExpressionContext_ExpandsToBlockExpressionAndEvaluatesLastValue()
    {
        var compilation = Compilation.FromSource("""
            public macro $compute {
                (@a: expr, @b: expr) => {
                    var x = @a * 2;
                    var y = @b * 3;
                    x + y
                };
            }

            public class Host
            {
                public struct int32;

                public int32 Test()
                {
                    return $compute(10, 20);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var host = Assert.Single(compilation.SyntaxTrees[0].Roots[0].Members.OfType<TopLevelTypeDeclaration>());
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var ret = Assert.Single(funcBody.Locals.OfType<LocalReturnStatement>());
        var block = Assert.IsType<BlockExpression>(ret.Statement.Expression);
        Assert.Equal(2, block.Locals.Count);
        Assert.NotNull(block.FinalExpression);
        var finalBin = Assert.IsType<BinaryExpression>(block.FinalExpression);
        Assert.Equal("compute", block.ExpansionOrigin?.MacroName.ToString());
    }

    [Fact]
    public void BlockPattern_InStatementContext_ExpandsWithoutBlockInSameScope()
    {
        var compilation = Compilation.FromSource("""
            public macro $declare_variables {
                () => {
                    var a = 10;
                    var b = 20;
                };
            }

            public class Host
            {
                public struct int32;

                public int32 Test()
                {
                    $declare_variables();
                    return a + b;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var host = Assert.Single(compilation.SyntaxTrees[0].Roots[0].Members.OfType<TopLevelTypeDeclaration>());
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);

        // a and b must be in funcBody.Locals directly (same scope), not nested in a block statement!
        Assert.Equal(3, funcBody.Locals.Count);
        var varA = Assert.IsType<LocalVariableDeclarationStatement>(funcBody.Locals[0]);
        var varB = Assert.IsType<LocalVariableDeclarationStatement>(funcBody.Locals[1]);
        var ret = Assert.IsType<LocalReturnStatement>(funcBody.Locals[2]);

        Assert.Equal("declare_variables", varA.ExpansionOrigin?.MacroName.ToString());
        Assert.Equal("declare_variables", varB.ExpansionOrigin?.MacroName.ToString());
    }

    [Fact]
    public void MacroExpansion_DiagnosticInExpandedCode_IncludesMacroTrace()
    {
        var compilation = Compilation.FromSource("""
            public macro $DefineType {
                (@name: ident) => {
                    public struct @name {
                        public UnknownType val;
                    }
                }
            }

            $DefineType(MyStruct);
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0500");
        Assert.NotNull(diag.MacroTrace);
        Assert.Equal("DefineType", diag.MacroTrace.MacroName);
        Assert.True(diag.MacroTrace.InvocationSpan.StartLocation.Line > 1);
        Assert.NotNull(diag.MacroTrace.DefinitionSpan);
        Assert.Equal(1, diag.MacroTrace.DefinitionSpan.Value.StartLocation.Line);
    }
}
