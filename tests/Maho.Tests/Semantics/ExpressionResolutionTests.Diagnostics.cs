using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed partial class ExpressionResolutionTests
{
    [Fact]
    public void Diagnostic_UntaggedUnionMethodCall_ReportsMH0608()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            [IntrinsicType("Enum")]
            public struct Enum : ValType;

            public enum E
            {
                A,
                B;
                public func NormalMethod(this&) -> () { }
            }

            public struct Host
            {
                public func Test(untagged E e) -> ()
                {
                    e.NormalMethod();
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0608");
    }

    [Fact]
    public void Diagnostic_EnumVariantMethodInvocation_ReportsMH0609()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public enum Shape
            {
                Circle { public func Draw() -> () {} }
            }

            public struct Host
            {
                public func Test(Shape s) -> ()
                {
                    s.Circle.Draw();
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0609");
    }

    [Fact]
    public void Literals_LibraryDefinedSuffixFunction_BindsMethodReference()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Meters
            {
                public int32 Value;
                public literal m(int32 v)
                {
                    Meters res;
                    res.Value = v;
                    return res;
                }
            }

            public struct Host
            {
                public func Test() -> Meters
                {
                    return 50m;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var context = compilation.Context!;
        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = body.Members.OfType<MemberFunctionDeclaration>().First();
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var ret = Assert.Single(funcBody.Locals.OfType<LocalReturnStatement>());
        var lit = Assert.IsType<LiteralExpression>(ret.Statement.Expression);

        Assert.True(context.ResolvedTree.TryGetReference(lit, out var boundHandle));
        var boundSym = context.GetSymbol(boundHandle);
        var litMethod = Assert.IsAssignableFrom<MethodSymbol>(boundSym);
        Assert.Equal("m", litMethod.Name.Text);
        Assert.True(litMethod.Flags.HasFlag(FunctionFlags.Literal));
    }

    [Fact]
    public void RetroactiveResolution_PropagatesToAllOperations_SucceedsWhenMethodsDefined()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Concrete : Add<Concrete, Concrete, Concrete>
            {
                public literal int() { Concrete res; return res; }
                func Add.Add(Concrete l, Concrete r) -> Concrete { return l; }
                public func Abs(this) -> Concrete { return this; }
            }

            public struct Host
            {
                public func Test() -> ()
                {
                    var x = 10;
                    var y = x + 10.Abs();
                    Concrete c = y;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void LiteralMethodInvocation_WithByRefReceiver_ReportsMH2027()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Concrete
            {
                public literal int() { Concrete res; return res; }
                public func Abs(&this) -> Concrete { return this; }
            }

            public struct Host
            {
                public func Test() -> ()
                {
                    Concrete c = 10.Abs();
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2027");
    }

    [Fact]
    public void RetroactiveResolution_ReportsMH2014_WhenTargetLacksMember()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Concrete : Add<Concrete, Concrete, Concrete>
            {
                public literal int() { Concrete res; return res; }
                func Add.Add(Concrete l, Concrete r) -> Concrete { return l; }
            }

            public struct Host
            {
                public func Test() -> ()
                {
                    var x = 10;
                    var y = x + 10.Abs();
                    Concrete c = y;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2014" && d.Message.Contains("Abs"));
    }

    [Fact]
    public void SequentialCall_CollapsesOnFirstCall_ReportsMH2004OnSecondCall()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Host
            {
                public func FromInt32(int32 x) -> () {}
                public func FromInt64(int64 x) -> () {}

                public func Test() -> ()
                {
                    var x = 10;
                    FromInt32(x);
                    FromInt64(x);
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2004");
    }

    [Fact]
    public void UnconstrainedLiteral_CollapsesToDefaultIntrinsic_ReportsMH2014_WhenIntrinsicLacksMember()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Host
            {
                public func Test() -> ()
                {
                    var x = 10;
                    x.NonExistentMethod();
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2014" && d.Message.Contains("NonExistentMethod") && d.Message.Contains("int32"));
    }

    [Fact]
    public void CanonicalBooleans_ParseAsLiteral_ResolveToCanonicalBool()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Host
            {
                public func Test() -> ()
                {
                    var t = true;
                    var f = false;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var host = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>(), t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var test = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(test.Function.Body);
        var varT = Assert.IsType<LocalVariableDeclarationStatement>(funcBody.Locals[0]);
        var varF = Assert.IsType<LocalVariableDeclarationStatement>(funcBody.Locals[1]);
        Assert.IsType<LiteralExpression>(varT.Declaration.Declarator.Initializer!.Initializer);
        Assert.IsType<LiteralExpression>(varF.Declaration.Declarator.Initializer!.Initializer);
    }

    [Fact]
    public void LiteralAddition_NoConstantFolding_BindsToResolvedOperator()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Host
            {
                public func Test() -> ()
                {
                    var res = 10 + 20;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var host = Assert.Single(compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>(), t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var test = Assert.Single(body.Members.OfType<MemberFunctionDeclaration>());
        var funcBody = Assert.IsType<FunctionBlockBody>(test.Function.Body);
        var varStmt = Assert.IsType<LocalVariableDeclarationStatement>(funcBody.Locals[0]);
        var bin = Assert.IsType<BinaryExpression>(varStmt.Declaration.Declarator.Initializer!.Initializer);
        Assert.True(compilation.Context.ResolvedTree.TryGetReference(bin, out var opHandle));
        var opSym = compilation.Context.GetSymbol(opHandle);
        var method = Assert.IsAssignableFrom<MethodSymbol>(opSym);
        Assert.Equal("Add", method.Name.Text);
    }

    [Fact]
    public void Conditionals_StrictBranchMatching_SameBaseType_ReportsMH2011()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Animal { }
            public struct Dog : Animal { }
            public struct Cat : Animal { }

            public struct Host
            {
                public func Test(bool cond, Dog d, Cat c) -> ()
                {
                    var res = if (cond) d else c;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2011");
    }

    [Fact]
    public void Conditionals_StrictBranchMatching_ExactMatch_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Host
            {
                public func Test(bool cond, int32 a, int32 b) -> int32
                {
                    return if (cond) a else b;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Allocation_New_WithAllocationArgs_ResolvesOperators()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Arena;

            public interface New<TArg, TTarget : this = this>
            {
                [OperatorFunction("New")]
                func New(TArg arg) -> TTarget;
            }

            public struct Point : New<Arena, Point>
            {
                public int32 X;
                public int32 Y;

                public func Construct(this mut&, int32 x, int32 y) -> ()
                {
                    this->X = x;
                    this->Y = y;
                }

                public func New(Arena pool) -> Point
                {
                    Point p = default;
                    return p;
                }
            }

            public extension Point : New<int32, Point>
            {
                public func New(int32 offset) -> Point
                {
                    Point p = default;
                    return p;
                }
            }

            public struct Host
            {
                public func Test(Arena pool) -> ()
                {
                    var p1 = new(pool) Point(10, 20);
                    Point p2 = new(4) Point(30, 40);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void CallableOperator_InvocationOnInstance_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Multiplier : Callable<int32, int32, int32>
            {
                public int32 Factor;

                func Callable.Invoke(this&, int32 x, int32 y) -> int32
                {
                    return x;
                }
            }

            public struct Host
            {
                public func Test(Multiplier m) -> int32
                {
                    return m(10, 20);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void CallableOperator_TypeNotCallable_ReportsMH0611()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct NonCallable;

            public struct Host
            {
                public func Test(NonCallable n) -> ()
                {
                    n(1, 2);
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0611");
    }

    [Fact]
    public void LiteralOverload_Ambiguity_ReportsMH2013()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Overloaded
            {
                public literal lit1(int32 v) { Overloaded res; return res; }
                public literal lit2(int32 v) { Overloaded res; return res; }
            }

            public struct Host
            {
                public func Test() -> ()
                {
                    Overloaded o = 42;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2013");
    }

    [Fact]
    public void CFG_ControlFlowConstraintPropagation_CollapsesLiteralVariable()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Host
            {
                public func ExpectInt64(int64 val) -> () { }

                public func Test() -> ()
                {
                    var x = 10;
                    ExpectInt64(x);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var context = compilation.Context!;
        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int64, out var hInt64));

        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = body.Members.OfType<MemberFunctionDeclaration>().First(m => m.Function.Signature.Identifier.ToString() == "Test");
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var scope = context.GetSyntaxScope(funcBody, context.Scopes[0]);

        var symX = Assert.IsType<LocalVariableSymbol>(scope[new SymbolName(new SymbolPart("x"))][0]);
        Assert.Equal(hInt64, symX.Type.Handle);
    }

    [Fact]
    public void IntrinsicFunction_OperatorAndLiteralOverloads_ResolveCorrectly()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            [IntrinsicType("UInt64")]
            public struct uint64 : Add<uint64, int32, uint64>
            {
                [IntrinsicFunction("UInt64.Add(UInt64, Int32)")]
                func Add.Add(uint64 l, int32 r) -> uint64;

                [IntrinsicFunction("UInt64.<u64>")]
                public literal u64(int32 v);
            }

            public struct Host
            {
                public func Test() -> uint64
                {
                    var lit = 100u64;
                    uint64 a = 10u64;
                    int32 b = 5i32;
                    return a + b;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var context = compilation.Context!;
        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = body.Members.OfType<MemberFunctionDeclaration>().First(m => m.Function.Signature.Identifier.ToString() == "Test");
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);

        var varLit = funcBody.Locals.OfType<LocalVariableDeclarationStatement>().First();
        var litExpr = Assert.IsType<LiteralExpression>(varLit.Declaration.Declarator.Initializer!.Initializer);
        Assert.True(context.ResolvedTree.TryGetReference(litExpr, out var litHandle));
        var litSym = Assert.IsAssignableFrom<MethodSymbol>(context.GetSymbol(litHandle));
        Assert.Equal("UInt64.<u64>", litSym.IntrinsicSignature);

        var ret = funcBody.Locals.OfType<LocalReturnStatement>().First();
        var binExpr = Assert.IsType<BinaryExpression>(ret.Statement.Expression);
        Assert.True(context.ResolvedTree.TryGetReference(binExpr, out var opHandle));
        var opSym = Assert.IsAssignableFrom<MethodSymbol>(context.GetSymbol(opHandle));
        Assert.Equal("UInt64.Add(UInt64, Int32)", opSym.IntrinsicSignature);
    }

    [Fact]
    public void Assignment_ConcreteToLiteralVariable_CollapsesLocalVariable()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Host
            {
                public func Some() -> int32
                {
                    return 10;
                }

                public func Run() -> int32
                {
                    var x = 100;
                    x = Some();
                    return x;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var context = compilation.Context!;
        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int32, out var hInt32));

        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = body.Members.OfType<MemberFunctionDeclaration>().First(m => m.Function.Signature.Identifier.ToString() == "Run");
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var scope = context.GetSyntaxScope(funcBody, context.Scopes[0]);

        var symX = Assert.IsType<LocalVariableSymbol>(scope[new SymbolName(new SymbolPart("x"))][0]);
        Assert.Equal(hInt32, symX.Type.Handle);
    }

    [Fact]
    public void Assignment_LiteralToLiteralVariable_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Host
            {
                public func Run() -> int32
                {
                    var x = 100;
                    x = 200;
                    return x;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Assignment_IncompatibleToLiteralVariable_ReportsMH2003()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Host
            {
                public func Main() -> ()
                {
                    var x = 100;
                    x = true;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2003");
    }

    [Fact]
    public void QuotedLiterals_ValidEscapesAndSingleScalarChars_SucceedWithoutErrors()
    {
        var compilation = Compilation.FromSource($$"""
            {{CompilerTestBed.FullPrelude}}
            [IntrinsicType("Char")] public struct char;

            public struct Tag : ValType
            {
                public literal u8(int32 v) { Tag t; return t; }
            }

            public struct Host
            {
                public func Run() -> ()
                {
                    var a = 'a';
                    var nl = '\n';
                    var hex = '\x41';
                    var bmp = '\u00E9';
                    var astral = '\u{1F600}';
                    var rocket = '🚀';
                    var suff = 'a'u8;
                    var suffEsc = '\u{1F600}'u8;
                    var str = "hello\n\t\r\0\a\b\f\v\'\"\\\x41\u0042\u{1F680}";
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Theory]
    [InlineData("'ab'")]
    [InlineData("'ab'u8")]
    [InlineData("'a\\n'")]
    [InlineData("'a\\n'u8")]
    [InlineData("'\\x41\\x42'")]
    [InlineData("'👨‍👩‍👧'")]
    public void CharLiteral_MultiScalar_ReportsMH0107Uniformly(string literalExpr)
    {
        var compilation = Compilation.FromSource($$"""
            {{CompilerTestBed.FullPrelude}}
            [IntrinsicType("Char")] public struct char;

            public struct Tag : ValType
            {
                public literal u8(int32 v) { Tag t; return t; }
            }

            public struct Host
            {
                public func Run() -> ()
                {
                    var bad = {{literalExpr}};
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0107");
    }

    [Theory]
    [InlineData("'\\q'", "MH0105")]
    [InlineData("'\\q'u8", "MH0105")]
    [InlineData("\"bad\\q\"", "MH0105")]
    [InlineData("\"bad\\x1\"", "MH0105")]
    [InlineData("\"bad\\u12\"", "MH0105")]
    [InlineData("\"bad\\u{}\"", "MH0105")]
    [InlineData("\"surrogate\\u{D800}\"", "MH0106")]
    [InlineData("'\\uDFFF'", "MH0106")]
    [InlineData("\"overflow\\u{110000}\"", "MH0106")]
    public void QuotedLiteral_InvalidOrOutOfRangeEscape_ReportsExpectedDiagnostic(string literalExpr, string expectedCode)
    {
        var compilation = Compilation.FromSource($$"""
            {{CompilerTestBed.FullPrelude}}
            [IntrinsicType("Char")] public struct char;

            public struct Tag : ValType
            {
                public literal u8(int32 v) { Tag t; return t; }
            }

            public struct Host
            {
                public func Run() -> ()
                {
                    var bad = {{literalExpr}};
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == expectedCode);
    }
}
