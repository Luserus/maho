using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed partial class ExpressionResolutionTests
{
    private const string Prelude = """
        intrinsic attribute IntrinsicType(Str name);

        intrinsic attribute OperatorFunction(Str name);

        [IntrinsicType("Unit")]
        public struct void;

        [IntrinsicType("Bool")]
        public struct bool;

        [IntrinsicType("Int8")]
        public struct int8;

        [IntrinsicType("Int16")]
        public struct int16;

        [IntrinsicType("Int32")]
        public struct int32 : Add<int32>, Sub<int32>, Mul<int32>, Div<int32>, Rem<int32>,
                              BitwiseAnd<int32>, BitwiseOr<int32>, BitwiseXor<int32>, BitwiseNot<int32>,
                              LeftShift<int32>, RightShift<int32>,
                              Eq<int32>, Neq<int32>, Lt<int32>, Le<int32>, Gt<int32>, Ge<int32>
        {
            [IntrinsicFunction("Int32.Add(Int32, Int32)")] public static func Add.Add(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Sub(Int32, Int32)")] public static func Sub.Sub(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Mul(Int32, Int32)")] public static func Mul.Mul(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Div(Int32, Int32)")] public static func Div.Div(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Rem(Int32, Int32)")] public static func Rem.Rem(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.BitwiseAnd(Int32, Int32)")] public static func BitwiseAnd.BitwiseAnd(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.BitwiseOr(Int32, Int32)")] public static func BitwiseOr.BitwiseOr(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.BitwiseXor(Int32, Int32)")] public static func BitwiseXor.BitwiseXor(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.BitwiseNot(Int32)")] public static func BitwiseNot.BitwiseNot(int32 o) -> int32;
            [IntrinsicFunction("Int32.LeftShift(Int32, Int32)")] public static func LeftShift.LeftShift(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.RightShift(Int32, Int32)")] public static func RightShift.RightShift(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Eq(Int32, Int32)")] public static func Eq.Eq(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.Neq(Int32, Int32)")] public static func Neq.Neq(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.Lt(Int32, Int32)")] public static func Lt.Lt(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.Le(Int32, Int32)")] public static func Le.Le(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.Gt(Int32, Int32)")] public static func Gt.Gt(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.Ge(Int32, Int32)")] public static func Ge.Ge(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.<i32>")] public literal i32(int32 v);
        }

        public interface Add<T> { [OperatorFunction("Addition")] public static func Add(int32 l, int32 r) -> int32; }
        public interface Sub<T> { [OperatorFunction("Subtraction")] public static func Sub(int32 l, int32 r) -> int32; }
        public interface Mul<T> { [OperatorFunction("Multiplication")] public static func Mul(int32 l, int32 r) -> int32; }
        public interface Div<T> { [OperatorFunction("Division")] public static func Div(int32 l, int32 r) -> int32; }
        public interface Rem<T> { [OperatorFunction("Remainder")] public static func Rem(int32 l, int32 r) -> int32; }
        public interface BitwiseAnd<T> { [OperatorFunction("BitwiseAnd")] public static func BitwiseAnd(int32 l, int32 r) -> int32; }
        public interface BitwiseOr<T> { [OperatorFunction("BitwiseOr")] public static func BitwiseOr(int32 l, int32 r) -> int32; }
        public interface BitwiseXor<T> { [OperatorFunction("BitwiseXor")] public static func BitwiseXor(int32 l, int32 r) -> int32; }
        public interface BitwiseNot<T> { [OperatorFunction("BitwiseNot")] public static func BitwiseNot(int32 o) -> int32; }
        public interface LeftShift<T> { [OperatorFunction("LeftShift")] public static func LeftShift(int32 l, int32 r) -> int32; }
        public interface RightShift<T> { [OperatorFunction("RightShift")] public static func RightShift(int32 l, int32 r) -> int32; }
        public interface Eq<T> { [OperatorFunction("Equality")] public static func Eq(int32 l, int32 r) -> bool; }
        public interface Neq<T> { [OperatorFunction("Inequality")] public static func Neq(int32 l, int32 r) -> bool; }
        public interface Lt<T> { [OperatorFunction("LessThan")] public static func Lt(int32 l, int32 r) -> bool; }
        public interface Le<T> { [OperatorFunction("LessThanOrEqual")] public static func Le(int32 l, int32 r) -> bool; }
        public interface Gt<T> { [OperatorFunction("GreaterThan")] public static func Gt(int32 l, int32 r) -> bool; }
        public interface Ge<T> { [OperatorFunction("GreaterThanOrEqual")] public static func Ge(int32 l, int32 r) -> bool; }

        [IntrinsicType("Int64")]
        public struct int64;

        [IntrinsicType("UInt32")]
        public struct uint32;

        [IntrinsicType("Float32")]
        public struct float32;

        [IntrinsicType("Float64")]
        public struct float64;

        [IntrinsicType("Char")]
        public struct char;

        [IntrinsicType("NUInt")]
        public struct nuint;

        [IntrinsicType("Reference")]
        public struct Reference<T>
        {
            nuint raw;
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            int8& buffer;
            nuint length;
        }
        intrinsic attribute IntrinsicFunction(Str signature);

        [IntrinsicType("Pointer")]
        public struct Pointer<T>
        {
            nuint addr;
        }

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        [IntrinsicType("Tuple")]
        public struct Tuple<T...> : ValType;

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            public func Invoke(TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            public func Callable.Invoke(TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            Pointer<ValType> env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(TParams args) -> TReturn;
        }
        """;

    [Fact]
    public void Inference_Variables_InferredFromLiterals()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Test() -> void
                {
                    var a = 42;
                    var b = true;
                    var c = "hello";
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context!;
        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int32, out var hInt32));
        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Bool, out var hBool));

        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = body.Members.OfType<MemberFunctionDeclaration>().First();
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var scope = context.GetSyntaxScope(funcBody, context.Scopes[0]);

        var symA = Assert.IsType<LocalVariableSymbol>(scope[new SymbolName(new SymbolPart("a"))][0]);
        Assert.Equal(hInt32, symA.Type.Handle);

        var symB = Assert.IsType<LocalVariableSymbol>(scope[new SymbolName(new SymbolPart("b"))][0]);
        Assert.Equal(hBool, symB.Type.Handle);
    }

    [Fact]
    public void Inference_TupleDestructuring_InfersEachElement()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Test() -> void
                {
                    var (x, y) = (10, true);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context!;
        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int32, out var hInt32));
        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Bool, out var hBool));

        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = body.Members.OfType<MemberFunctionDeclaration>().First();
        var funcBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        var scope = context.GetSyntaxScope(funcBody, context.Scopes[0]);

        var symX = Assert.IsType<LocalVariableSymbol>(scope[new SymbolName(new SymbolPart("x"))][0]);
        Assert.Equal(hInt32, symX.Type.Handle);

        var symY = Assert.IsType<LocalVariableSymbol>(scope[new SymbolName(new SymbolPart("y"))][0]);
        Assert.Equal(hBool, symY.Type.Handle);
    }

    [Fact]
    public void Void_UnitType_UsableInVariablesAndExpressions()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Helper() -> void
                {
                    return;
                }

                public func Test() -> void
                {
                    void v = Helper();
                    var inferredVoid = v;
                    return inferredVoid;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void PrimitiveOperators_Arithmetic_Bitwise_Comparison_Logic()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Test() -> void
                {
                    var add = 10 + 20;
                    var sub = 30 - 5;
                    var mul = 4 * 2;
                    var div = 100 / 10;
                    var rem = 17 % 5;

                    var lt = 10 < 20;
                    var le = 10 <= 20;
                    var gt = 10 > 20;
                    var ge = 10 >= 20;
                    var eq = 10 == 10;
                    var ne = 10 != 10;

                    var and = true && false;
                    var or = true || false;
                    var not = !true;

                    var band = 10 & 2;
                    var bor = 10 | 2;
                    var bxor = 10 ^ 2;
                    var bnot = ~10;
                    var shl = 1 << 2;
                    var shr = 8 >> 1;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void OperatorOverload_ExplicitInterface_ResolvesCorrectly()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public interface Add
            {
                [OperatorFunction("Addition")]
                public static func Add(Vector left, Vector right) -> Vector;
            }

            public struct Vector : Add
            {
                public int32 X;
                public int32 Y;

                public static func Add.Add(Vector left, Vector right) -> Vector
                {
                    Vector result;
                    return result;
                }
            }

            public class Host
            {
                public func Test(Vector a, Vector b) -> Vector
                {
                    return a + b;
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
        var retStmt = Assert.Single(funcBody.Locals.OfType<LocalReturnStatement>());
        var bin = Assert.IsType<BinaryExpression>(retStmt.Statement.Expression);

        Assert.True(context.ResolvedTree.TryGetReference(bin, out var boundHandle));
        var boundMethod = Assert.IsAssignableFrom<MethodSymbol>(context.GetSymbol(boundHandle));
        Assert.True(boundMethod.Flags.HasFlag(FunctionFlags.InterfaceScoped));
        Assert.Equal("Add", boundMethod.ExplicitInterfaceName?.Last.Text);
    }

    [Fact]
    public void Diagnostic_BinaryTypeMismatch_ReportsMH2001()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Test() -> void
                {
                    var x = 10 + true;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2001");
    }

    [Fact]
    public void Diagnostic_UnaryTypeMismatch_ReportsMH2002()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Test() -> void
                {
                    var x = -true;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2002");
    }

    [Fact]
    public void Diagnostic_AssignmentTypeMismatch_ReportsMH2003()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Test() -> void
                {
                    int32 x = true;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2003");
    }

    [Fact]
    public void Diagnostic_ArgumentTypeMismatch_ReportsMH2004()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Foo(int32 x) -> void { }

                public func Test() -> void
                {
                    Foo(true);
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2004");
    }

    [Fact]
    public void Diagnostic_ArgumentCountMismatch_ReportsMH2005()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Foo(int32 x, int32 y) -> void { }

                public func Test() -> void
                {
                    Foo(1);
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2005");
    }

    [Fact]
    public void Diagnostic_ReturnTypeMismatch_ReportsMH2006()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Test() -> int32
                {
                    return true;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2006");
    }

    [Fact]
    public void Diagnostic_ConditionTypeMismatch_ReportsMH2007()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Test() -> void
                {
                    if (42)
                    {
                    }
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2007");
    }

    [Fact]
    public void Diagnostic_CannotInferType_WithoutInitializer_ReportsMH2008()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Test() -> void
                {
                    var x;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2008");
    }

    [Fact]
    public void Diagnostic_CannotAssignToNonLValue_ReportsMH2009()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Test() -> void
                {
                    10 = 20;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2009");
    }

    [Fact]
    public void Diagnostic_TupleDestructureMismatch_ReportsMH2010()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public func Test() -> void
                {
                    var (a, b) = 42;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2010");
    }

    [Fact]
    public void Diagnostic_UntaggedSumTypeMatch_ReportsMH0606()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public untagged enum UntaggedData
            {
                A(int32),
                B(int32)
            }

            public class Host
            {
                public func Test(UntaggedData d) -> void
                {
                    match (d)
                    {
                        A(val) => {},
                        _ => {}
                    }
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0606");
    }

}
