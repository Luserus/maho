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

            public enum E
            {
                A,
                B;
                public func NormalMethod() -> void { }
            }

            public class Host
            {
                public func Test(untagged E e) -> void
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
                Circle { public func Draw() -> void {} }
            }

            public class Host
            {
                public func Test(Shape s) -> void
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

            public class Host
            {
                public func Test() -> Meters
                {
                    return 50m;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
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

            public interface ConcreteAdd
            {
                [OperatorFunction("Addition")]
                public static func Add(Concrete l, Concrete r) -> Concrete;
            }

            public struct Concrete : ConcreteAdd
            {
                public literal int() { Concrete res; return res; }
                public static func ConcreteAdd.Add(Concrete l, Concrete r) -> Concrete { return l; }
                public func Abs(&this) -> Concrete { return this; }
            }

            public class Host
            {
                public func Test() -> void
                {
                    var x = 10;
                    var y = x + 10.Abs();
                    Concrete c = y;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void RetroactiveResolution_ReportsMH2014_WhenTargetLacksMember()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public interface ConcreteAdd
            {
                [OperatorFunction("Addition")]
                public static func Add(Concrete l, Concrete r) -> Concrete;
            }

            public struct Concrete : ConcreteAdd
            {
                public literal int() { Concrete res; return res; }
                public static func ConcreteAdd.Add(Concrete l, Concrete r) -> Concrete { return l; }
            }

            public class Host
            {
                public func Test() -> void
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

            public class Host
            {
                public func FromInt32(int32 x) -> void {}
                public func FromInt64(int64 x) -> void {}

                public func Test() -> void
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

            public class Host
            {
                public func Test() -> void
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

            public class Host
            {
                public func Test() -> void
                {
                    var t = true;
                    var f = false;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
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

            public class Host
            {
                public func Test() -> void
                {
                    var res = 10 + 20;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
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

            public class Animal { }
            public class Dog : Animal { }
            public class Cat : Animal { }

            public class Host
            {
                public func Test(bool cond, Dog d, Cat c) -> void
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

            public class Host
            {
                public func Test(bool cond, int32 a, int32 b) -> int32
                {
                    return if (cond) a else b;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void Allocation_NewAndPut_WithAllocationArgs_ResolvesOperators()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Arena;

            public interface New
            {
                [OperatorFunction("New")]
                public static func New(Arena pool) -> Point;
            }

            public interface Put
            {
                [OperatorFunction("Put")]
                public static func Put(int32 offset) -> void;
            }

            public struct Point : New, Put
            {
                public int32 X;
                public int32 Y;

                public static func Construct(int32 x, int32 y) -> Point
                {
                    Point p;
                    p.X = x;
                    p.Y = y;
                    return p;
                }

                public static func New.New(Arena pool) -> Point
                {
                    Point p;
                    return p;
                }

                public static func Put.Put(int32 offset) -> void
                {
                }
            }

            public class Host
            {
                public func Test(Arena pool) -> void
                {
                    var p1 = new(pool) Point(10, 20);
                    put(4) Point(30, 40);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void CallableOperator_InvocationOnInstance_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public interface Callable
            {
                [OperatorFunction("Callable")]
                public func Call(&this, int32 x, int32 y) -> int32;
            }

            public struct Multiplier : Callable
            {
                public int32 Factor;

                public func Callable.Call(&this, int32 x, int32 y) -> int32
                {
                    return x;
                }
            }

            public class Host
            {
                public func Test(Multiplier m) -> int32
                {
                    return m(10, 20);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void CallableOperator_TypeNotCallable_ReportsMH0611()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct NonCallable;

            public class Host
            {
                public func Test(NonCallable n) -> void
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

            public class Host
            {
                public func Test() -> void
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

            public class Host
            {
                public func ExpectInt64(int64 val) -> void { }

                public func Test() -> void
                {
                    var x = 10;
                    ExpectInt64(x);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
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

            public interface AddMixed<TLhs, TRhs, TReturn>
            {
                [OperatorFunction("Addition")]
                public static func Add(TLhs l, TRhs r) -> TReturn;
            }

            [IntrinsicType("UInt64")]
            public struct uint64 : AddMixed<uint64, int32, uint64>
            {
                [IntrinsicFunction("UInt64.Add(UInt64, Int32)")]
                public static func AddMixed.Add(uint64 l, int32 r) -> uint64;

                [IntrinsicFunction("UInt64.<u64>")]
                public literal u64(int32 v);
            }

            public class Host
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

        Assert.False(compilation.HasErrors);
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

            public class Host
            {
                public func Some() -> int32
                {
                    return 10;
                }

                public func Main() -> int32
                {
                    var x = 100;
                    x = Some();
                    return x;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context!;
        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int32, out var hInt32));

        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = body.Members.OfType<MemberFunctionDeclaration>().First(m => m.Function.Signature.Identifier.ToString() == "Main");
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

            public class Host
            {
                public func Main() -> int32
                {
                    var x = 100;
                    x = 200;
                    return x;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void Assignment_IncompatibleToLiteralVariable_ReportsMH2003()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Main() -> void
                {
                    var x = 100;
                    x = true;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2003");
    }
}
