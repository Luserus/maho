using Maho.Syntax;

namespace Maho.Tests;

public sealed partial class MacroTests
{
    private const string MacroPrelude = """
        intrinsic attribute IntrinsicType(Str name);

        intrinsic attribute IntrinsicFunction(Str signature);

        intrinsic attribute OperatorFunction(Str name);

        [IntrinsicType("Unit")]
        public struct void;

        [IntrinsicType("NUInt")]
        public struct nuint;

        [IntrinsicType("Int8")]
        public struct int8;

        [IntrinsicType("Int32")]
        public struct int32 : Add<int32>, Mul<int32>
        {
            [IntrinsicFunction("Int32.Add(Int32, Int32)")]
            public func Add.Add(int32 left, int32 right) -> int32;
            [IntrinsicFunction("Int32.Mul(Int32, Int32)")]
            public func Mul.Mul(int32 left, int32 right) -> int32;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T>
        {
            nuint raw;
        }

        [IntrinsicType("Int64")]
        public struct int64;

        [IntrinsicType("Str")]
        public struct Str
        {
            int8& buffer;
            nuint length;
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T>
        {
            nuint addr;
        }

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            public func Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            Pointer<ValType> env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        public interface Add<T>
        {
            [OperatorFunction("Addition")]
            public func Add(int32 left, int32 right) -> int32;
        }

        public interface Mul<T>
        {
            [OperatorFunction("Multiplication")]
            public func Mul(int32 left, int32 right) -> int32;
        }
        """;

    [Fact]
    public void ExpressionMacro_SimpleExpansion_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $add {
                (@a: expr, @b: expr) => @a + @b;
            }

            public struct Host
            {
                public func Test() -> int32
                {
                    return $add(1, 2);
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
        Assert.NotNull(bin.ExpansionOrigin);
        Assert.Equal("add", bin.ExpansionOrigin.MacroName.ToString());
    }

    [Fact]
    public void ExpressionMacro_RecursiveExpansion_ReachesBaseCase()
    {
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $nest {
                (0) => 42;
                (1) => $nest(0);
            }

            public struct Host
            {
                public func Test() -> int32
                {
                    return $nest(1);
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
        Assert.Equal("42", lit.Literal.Value);
    }

    [Fact]
    public void StatementMacro_MultiStatementAndHygiene_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $swap {
                (@a: ident, @b: ident) => {
                    var temp = @a;
                    @a = @b;
                    @b = temp;
                }
            }

            public struct Host
            {
                public func Test() -> void
                {
                    int32 x = 1;
                    int32 y = 2;
                    $swap(x, y);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var host = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>(), t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);

        // Original 2 variable declarations + 3 expanded statements from $swap
        Assert.Equal(5, funcBody.Locals.Count);

        // Third statement is 'var temp = x;' with uncollidable HygieneId (no name mangling)
        var varDecl = Assert.IsType<LocalVariableDeclarationStatement>(funcBody.Locals[2]);
        var declarator = varDecl.Declaration.Declarator;
        Assert.Equal("temp", declarator.Identifier.ToString());
        Assert.True(compilation.Context.GetHygiene(declarator.Identifier).IsMacroGenerated);

        var tempSymbol = Assert.Single(compilation.Context.LocalVariableSymbols, s => s.Name.ToString() == "temp");
        Assert.True(tempSymbol.IsMacroGenerated);
        Assert.True(tempSymbol.Hygiene.IsMacroGenerated);
    }

    [Fact]
    public void MacroHygiene_UncollidableWithUserSymbols_NoClash()
    {
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $swap {
                (@a: ident, @b: ident) => {
                    var temp = @a;
                    @a = @b;
                    @b = temp;
                }
            }

            public struct Host
            {
                public func Test() -> void
                {
                    int32 x = 1;
                    int32 y = 2;
                    int32 temp = 999;
                    $swap(x, y);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var tempSymbols = compilation.Context.LocalVariableSymbols.Where(s => s.Name.ToString() == "temp").ToList();
        Assert.Equal(2, tempSymbols.Count);

        var userTemp = Assert.Single(tempSymbols, s => !s.IsMacroGenerated);
        var macroTemp = Assert.Single(tempSymbols, s => s.IsMacroGenerated);

        Assert.True(userTemp.Hygiene.IsRoot);
        Assert.True(macroTemp.Hygiene.IsMacroGenerated);
        Assert.NotEqual(userTemp.Hygiene, macroTemp.Hygiene);
    }

    [Fact]
    public void MacroHygiene_PreventsVariableCapture()
    {
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $swap {
                (@a: ident, @b: ident) => {
                    var temp = @a;
                    @a = @b;
                    @b = temp;
                }
            }

            public struct Host
            {
                public func Test() -> void
                {
                    int32 temp = 1;
                    int32 other = 2;
                    $swap(temp, other);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var tempSymbols = compilation.Context.LocalVariableSymbols.Where(s => s.Name.ToString() == "temp").ToList();
        Assert.Equal(2, tempSymbols.Count);

        var callerTemp = Assert.Single(tempSymbols, s => !s.IsMacroGenerated);
        var macroTemp = Assert.Single(tempSymbols, s => s.IsMacroGenerated);

        Assert.True(callerTemp.Hygiene.IsRoot);
        Assert.True(macroTemp.Hygiene.IsMacroGenerated);
    }

    [Fact]
    public void MacroHygiene_MultipleExpansionsHaveDistinctHygieneIds()
    {
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $swap {
                (@a: ident, @b: ident) => {
                    var temp = @a;
                    @a = @b;
                    @b = temp;
                }
            }

            public struct Host
            {
                public func Test() -> void
                {
                    int32 x = 1;
                    int32 y = 2;
                    int32 a = 3;
                    int32 b = 4;
                    $swap(x, y);
                    $swap(a, b);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var macroTemps = compilation.Context.LocalVariableSymbols.Where(s => s.Name.ToString() == "temp").ToList();
        Assert.Equal(2, macroTemps.Count);
        Assert.All(macroTemps, s => Assert.True(s.IsMacroGenerated));
        Assert.NotEqual(macroTemps[0].Hygiene, macroTemps[1].Hygiene);
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

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var structDecl = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>());
        var body = Assert.IsType<TypeBlockBody>(structDecl.Type.Body);

        // Should contain two fields expanded from $Point2D;
        var fields = body.Members.OfType<MemberFieldDeclaration>().ToList();
        Assert.Equal(2, fields.Count);

        var names = fields.Select(f => f.Declaration.Declarator.Identifier.ToString()).ToList();
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
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $wrap {
                (@items: expr...) => [@items...];
            }

            public struct Host
            {
                public struct List;

                public func Test() -> void
                {
                    List arr = $wrap(10, 20, 30);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var host = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>(), t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);

        var varStmt = Assert.Single(funcBody.Locals.OfType<LocalVariableDeclarationStatement>());
        var collExpr = Assert.IsType<CollectionExpression>(varStmt.Declaration.Declarator.Initializer!.Initializer);
        Assert.Equal(3, collExpr.Expressions.Count);
    }

    [Fact]
    public void VariadicMacro_StatementRepetitionUnrolling_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $call_all {
                (@fn: ident, @items: expr...) => {
                    $(@fn(@item);)...
                }
            }

            public struct Host
            {
                public func Test() -> void
                {
                    $call_all(print, 1, 2, 3);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var host = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>(), t => t.Type.Name.ToString() == "Host");
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

            public struct Host
            {
                public func Test() -> int32
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

            public struct Host
            {
                public func Test() -> int32
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
            public struct Host
            {
                public func Test() -> int32
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

            public struct Host
            {
                public func Test() -> int32
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

            public struct Host
            {
                public func Test() -> int32
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
        var compilation = Compilation.FromSource($$"""
            {{MacroPrelude}}

            public macro $here => 42;

            public struct Host
            {
                public func Test() -> int32
                {
                    return $here;
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
        Assert.Equal("42", lit.Literal.Value);
    }

}
