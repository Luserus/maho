using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class ExpressionResolutionTests
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
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
                public func Abs() -> Concrete { return this; }
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void CallableOperator_InvocationOnInstance_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public interface Callable
            {
                [OperatorFunction("Callable")]
                public func Call(int32 x, int32 y) -> int32;
            }

            public struct Multiplier : Callable
            {
                public int32 Factor;

                public func Callable.Call(int32 x, int32 y) -> int32
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
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

        Assert.True(!compilation.HasErrors, string.Join("; ", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
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
}
