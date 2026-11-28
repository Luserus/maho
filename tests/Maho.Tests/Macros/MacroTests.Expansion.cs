using Maho.Syntax;

namespace Maho.Tests;

public sealed partial class MacroTests
{
    [Fact]
    public void EscapedIdentifier_MacroDeclarationAndInvocation_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $`macro` => 100;

            public struct Host
            {
                public func Test() -> int32
                {
                    return $`macro`;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var host = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>(), t => t.Type.Name.ToString() == "Host");
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
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            namespace Sample
            {
                public macro $sq {
                    (@x: expr) => @x * @x;
                }

                public struct Calculator
                {
                    public func Calc() -> int32
                    {
                        return $sq(7);
                    }
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var ns = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<NamespaceDeclaration>());
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
        string source = $$"""
            {{MacroPrelude}}

            public macro $step {
                (1) => 777;
                (2) => $step(1);
                (3) => $step(2);
                (4) => $step(3);
                (5) => $step(4);
            }

            public struct Host
            {
                public func Test() -> int32
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
        var host = Assert.Single(unboundedComp.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>(), t => t.Type.Name.ToString() == "Host");
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
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public struct Host
            {
                public func Test() -> int32
                {
                    macro $local_add {
                        (@a: expr, @b: expr) => @a + @b;
                    }

                    return $local_add(10, 20);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var host = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>(), t => t.Type.Name.ToString() == "Host");
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
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $compute {
                (@a: expr, @b: expr) => {
                    var x = @a * 2;
                    var y = @b * 3;
                    x + y
                };
            }

            public struct Host
            {
                public func Test() -> int32
                {
                    return $compute(10, 20);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var host = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>(), t => t.Type.Name.ToString() == "Host");
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
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $declare_variables {
                () => {
                    int32 a = 10;
                    int32 b = 20;
                };
            }

            public struct Host
            {
                public func Test() -> int32
                {
                    $declare_variables();
                    return a + b;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var host = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>(), t => t.Type.Name.ToString() == "Host");
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

    [Fact]
    public void Macro_TokenStreamParameter_ExpandsArbitraryTokens()
    {
        var compilation = Compilation.FromSource("""
            public macro $WrapStruct {
                (@name: ident, @body: tokens) => {
                    public struct @name {
                        @body
                    }
                }
            }

            public struct int32;

            $WrapStruct(Point, public int32 X; public int32 Y;);
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var structs = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>().ToList();
        var pointStruct = Assert.Single(structs, s => s.Type.Name.ToString() == "Point");
        var body = Assert.IsType<TypeBlockBody>(pointStruct.Type.Body);
        Assert.Equal(2, body.Members.OfType<MemberFieldDeclaration>().Count());
    }

    [Fact]
    public void Macro_SingleTokenParameter_MatchesSingleToken()
    {
        var compilation = Compilation.FromSource("""
            public macro $ApplyModifier {
                (@mod: token, @name: ident) => {
                    @mod struct @name;
                }
            }

            $ApplyModifier(internal, MyInternal);
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var structs = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>().ToList();
        var myStruct = Assert.Single(structs, s => s.Type.Name.ToString() == "MyInternal");
        Assert.Single(myStruct.Type.Modifiers);
        Assert.Equal(MatchingKeywordKind.Internal, myStruct.Type.Modifiers[0].MatchingKind);
    }

    [Fact]
    public void Macro_SingleTokenParameter_RejectsMultipleTokens()
    {
        var compilation = Compilation.FromSource("""
            public macro $TakesSingle {
                (@tok: token) => {
                    public struct Ok;
                }
            }

            $TakesSingle(a + b);
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0601"); // No matching arm found
    }

    [Fact]
    public void Macro_VariadicTokenStream_ExpandsPacks()
    {
        var compilation = Compilation.FromSource("""
            public macro $Chain {
                (@fields: tokens...) => {
                    public struct Entity {
                        $(public @fields;)...
                    }
                }
            }

            public struct int32;
            public struct string8;

            $Chain(int32 id, string8 name);
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var structs = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>().ToList();
        var entity = Assert.Single(structs, s => s.Type.Name.ToString() == "Entity");
        var body = Assert.IsType<TypeBlockBody>(entity.Type.Body);
        Assert.Equal(2, body.Members.OfType<MemberFieldDeclaration>().Count());
    }

    [Fact]
    public void Macro_TokenConcatenation_GluesIdentifiers()
    {
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $MakeMethod {
                (@prefix: ident, @suffix: ident) => {
                    public func @prefix##@suffix() -> () {}
                }
            }

            public struct Host
            {
                $MakeMethod(Try, Parse);
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var host = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>(), t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        Assert.Equal("TryParse", method.Function.Signature.Identifier.ToString());
    }

    [Fact]
    public void Macro_TokenConcatenation_ChainedAndWithNumbers()
    {
        var compilation = Compilation.FromSource("""
            public macro $MakeType {
                (@name: ident, @bits: token) => {
                    public struct @name##_##@bits;
                }
            }

            $MakeType(Int, 64);
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var structs = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>().ToList();
        var myStruct = Assert.Single(structs, s => s.Type.Name.ToString() == "Int_64");
    }

    [Fact]
    public void Macro_TokenConcatenation_TriviaBetweenHashes_DoesNotConcatenate()
    {
        var compilation = Compilation.FromSource("""
            public macro $NaiveHashes {
                (@name: ident) => {
                    public void Foo # # @name() {}
                }
            }

            public struct Host
            {
                $NaiveHashes(Bar);
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.DoesNotContain(compilation.Diagnostics, d => d.Code == "MH0230");
    }

    [Fact]
    public void Macro_TokenConcatenation_InvalidPasting_ReportsDiagnostic()
    {
        var compilation = Compilation.FromSource("""
            public macro $BadPaste {
                (@a: token, @b: token) => {
                    public void @a##@b() {}
                }
            }

            public struct Host
            {
                $BadPaste(12, @);
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0230");
    }

    [Fact]
    public void Macro_TokenConcatenation_SuffixedLiteral_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $MakeSuffixed {
                (@num: token, @suf: token) => {
                    var x = @num##@suf;
                }
            }

            public struct Host
            {
                public func Test() -> ()
                {
                    $MakeSuffixed(123, i32);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Macro_Nameof_FoldsParameterToStringLiteral()
    {
        var compilation = Compilation.FromSource("""
            public macro $DefineIntrinsic {
                (@name: ident) => {
                    [IntrinsicType(nameof(@name))]
                    public struct @name;
                }
            }

            $DefineIntrinsic(int32);
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var structs = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>().ToList();
        var myStruct = Assert.Single(structs, s => s.Type.Name.ToString() == "int32");
        var attrList = Assert.Single(myStruct.Type.Attributes);
        var attr = Assert.Single(attrList.Attributes);
        Assert.Equal("IntrinsicType", attr.Name.ToString());
        var arg = Assert.Single(attr.Arguments);
        var lit = Assert.IsType<LiteralExpression>(arg);
        Assert.Equal("\"int32\"", lit.Literal.Value);
    }

    [Fact]
    public void Expression_Nameof_ParsesAndResolvesCorrectly()
    {
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public struct Host
            {
                public func Test() -> ()
                {
                    int32 x = 42;
                    var name = nameof(x);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var host = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>(), t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var varStmt = Assert.IsType<LocalVariableDeclarationStatement>(funcBody.Locals[1]);
        var init = varStmt.Declaration.Declarator.Initializer!.Initializer;
        var nameofExpr = Assert.IsType<NameofExpression>(init);
        var arg = Assert.IsType<IdentifierNameExpression>(nameofExpr.Argument);
        Assert.Equal("x", arg.Identifier.Value);
    }

    [Fact]
    public void Macro_AttributeOnType_ExpandsAndDecoratesType()
    {
        var compilation = Compilation.FromSource("""
            public macro $DeriveHelper {
                (@target: tokens) => {
                    @target
                    public struct Helper;
                }
            }

            [$DeriveHelper]
            public struct Entity;
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var structs = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>().ToList();
        Assert.Equal(2, structs.Count);
        Assert.Contains(structs, s => s.Type.Name.ToString() == "Entity");
        Assert.Contains(structs, s => s.Type.Name.ToString() == "Helper");
    }

    [Fact]
    public void Macro_AttributeWithArguments_PassesArgsAndTarget()
    {
        var compilation = Compilation.FromSource("""
            public macro $AddSuffix {
                (@suf: ident, @target: tokens) => {
                    public struct Tag_##@suf;
                    @target
                }
            }

            [$AddSuffix(Special)]
            public struct Original;
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var structs = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>().ToList();
        Assert.Equal(2, structs.Count);
        Assert.Contains(structs, s => s.Type.Name.ToString() == "Tag_Special");
        Assert.Contains(structs, s => s.Type.Name.ToString() == "Original");
    }

    [Fact]
    public void Macro_AttributeOnMember_ExpandsInsideTypeBody()
    {
        var compilation = Compilation.FromSource("""
            public macro $DuplicateField {
                (@target: tokens) => {
                    @target
                    public int32 Backup;
                }
            }

            public struct int32;

            public struct Container {
                [$DuplicateField]
                public int32 Value;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var container = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>(), s => s.Type.Name.ToString() == "Container");
        var body = Assert.IsType<TypeBlockBody>(container.Type.Body);
        var fields = body.Members.OfType<MemberFieldDeclaration>().ToList();
        Assert.Equal(2, fields.Count);
    }

    [Fact]
    public void Macro_DefinitionWithAttributes_ParsesSuccessfully()
    {
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            [Obsolete]
            public macro $OldMacro {
                () => 42;
            }

            public struct Host
            {
                public func Test() -> int32
                {
                    return $OldMacro();
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }
}

