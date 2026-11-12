using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class ExpressionResolutionTests
{
    private const string Prelude = """
        [intrinsic]
        public attribute IntrinsicType(Str name);

        [intrinsic]
        public attribute OperatorFunction(Str name);

        [IntrinsicType("Void")]
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
            public static int32 Add.Add(int32 l, int32 r) { return l; }
            public static int32 Sub.Sub(int32 l, int32 r) { return l; }
            public static int32 Mul.Mul(int32 l, int32 r) { return l; }
            public static int32 Div.Div(int32 l, int32 r) { return l; }
            public static int32 Rem.Rem(int32 l, int32 r) { return l; }
            public static int32 BitwiseAnd.BitwiseAnd(int32 l, int32 r) { return l; }
            public static int32 BitwiseOr.BitwiseOr(int32 l, int32 r) { return l; }
            public static int32 BitwiseXor.BitwiseXor(int32 l, int32 r) { return l; }
            public static int32 BitwiseNot.BitwiseNot(int32 o) { return o; }
            public static int32 LeftShift.LeftShift(int32 l, int32 r) { return l; }
            public static int32 RightShift.RightShift(int32 l, int32 r) { return l; }
            public static bool Eq.Eq(int32 l, int32 r) { return true; }
            public static bool Neq.Neq(int32 l, int32 r) { return false; }
            public static bool Lt.Lt(int32 l, int32 r) { return true; }
            public static bool Le.Le(int32 l, int32 r) { return true; }
            public static bool Gt.Gt(int32 l, int32 r) { return false; }
            public static bool Ge.Ge(int32 l, int32 r) { return false; }
        }

        public interface Add<T> { [OperatorFunction("Addition")] public static int32 Add(int32 l, int32 r); }
        public interface Sub<T> { [OperatorFunction("Subtraction")] public static int32 Sub(int32 l, int32 r); }
        public interface Mul<T> { [OperatorFunction("Multiplication")] public static int32 Mul(int32 l, int32 r); }
        public interface Div<T> { [OperatorFunction("Division")] public static int32 Div(int32 l, int32 r); }
        public interface Rem<T> { [OperatorFunction("Remainder")] public static int32 Rem(int32 l, int32 r); }
        public interface BitwiseAnd<T> { [OperatorFunction("BitwiseAnd")] public static int32 BitwiseAnd(int32 l, int32 r); }
        public interface BitwiseOr<T> { [OperatorFunction("BitwiseOr")] public static int32 BitwiseOr(int32 l, int32 r); }
        public interface BitwiseXor<T> { [OperatorFunction("BitwiseXor")] public static int32 BitwiseXor(int32 l, int32 r); }
        public interface BitwiseNot<T> { [OperatorFunction("BitwiseNot")] public static int32 BitwiseNot(int32 o); }
        public interface LeftShift<T> { [OperatorFunction("LeftShift")] public static int32 LeftShift(int32 l, int32 r); }
        public interface RightShift<T> { [OperatorFunction("RightShift")] public static int32 RightShift(int32 l, int32 r); }
        public interface Eq<T> { [OperatorFunction("Equality")] public static bool Eq(int32 l, int32 r); }
        public interface Neq<T> { [OperatorFunction("Inequality")] public static bool Neq(int32 l, int32 r); }
        public interface Lt<T> { [OperatorFunction("LessThan")] public static bool Lt(int32 l, int32 r); }
        public interface Le<T> { [OperatorFunction("LessThanOrEqual")] public static bool Le(int32 l, int32 r); }
        public interface Gt<T> { [OperatorFunction("GreaterThan")] public static bool Gt(int32 l, int32 r); }
        public interface Ge<T> { [OperatorFunction("GreaterThanOrEqual")] public static bool Ge(int32 l, int32 r); }

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
            int64 length;
        }
        """;

    [Fact]
    public void Inference_Variables_InferredFromLiterals()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public void Test()
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
                public void Test()
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
                public void Helper()
                {
                    return;
                }

                public void Test()
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
                public void Test()
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
                public static Vector Add(Vector left, Vector right);
            }

            public struct Vector : Add
            {
                public int32 X;
                public int32 Y;

                public static Vector Add.Add(Vector left, Vector right)
                {
                    Vector result;
                    return result;
                }
            }

            public class Host
            {
                public Vector Test(Vector a, Vector b)
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
                public void Test()
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
                public void Test()
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
                public void Test()
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
                public void Foo(int32 x) { }

                public void Test()
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
                public void Foo(int32 x, int32 y) { }

                public void Test()
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
                public int32 Test()
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
                public void Test()
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
                public void Test()
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
                public void Test()
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
                public void Test()
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
                public void Test(UntaggedData d)
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
                public void NormalMethod() { }
            }

            public class Host
            {
                public void Test(untagged E e)
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
                Circle { public void Draw() {} }
            }

            public class Host
            {
                public void Test(Shape s)
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
                public Meters Test()
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
                public static Concrete Add(Concrete l, Concrete r);
            }

            public struct Concrete : ConcreteAdd
            {
                public literal int();
                public static Concrete ConcreteAdd.Add(Concrete l, Concrete r) { return l; }
                public Concrete Abs() { return this; }
            }

            public class Host
            {
                public void Test()
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
                public static Concrete Add(Concrete l, Concrete r);
            }

            public struct Concrete : ConcreteAdd
            {
                public literal int();
                public static Concrete ConcreteAdd.Add(Concrete l, Concrete r) { return l; }
            }

            public class Host
            {
                public void Test()
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
                public void FromInt32(int32 x) {}
                public void FromInt64(int64 x) {}

                public void Test()
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
                public void Test()
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
                public void Test()
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
        Assert.IsType<LiteralExpression>(varT.Declaration.Declarators[0].Initializer!.Initializer);
        Assert.IsType<LiteralExpression>(varF.Declaration.Declarators[0].Initializer!.Initializer);
    }

    [Fact]
    public void LiteralAddition_NoConstantFolding_BindsToResolvedOperator()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public void Test()
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
        var bin = Assert.IsType<BinaryExpression>(varStmt.Declaration.Declarators[0].Initializer!.Initializer);
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
                public void Test(bool cond, Dog d, Cat c)
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
                public int32 Test(bool cond, int32 a, int32 b)
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
                public static Point New(Arena pool);
            }

            public interface Put
            {
                [OperatorFunction("Put")]
                public static void Put(int32 offset);
            }

            public struct Point : New, Put
            {
                public int32 X;
                public int32 Y;

                public static Point Construct(int32 x, int32 y)
                {
                    Point p;
                    p.X = x;
                    p.Y = y;
                    return p;
                }

                public static Point New.New(Arena pool)
                {
                    Point p;
                    return p;
                }

                public static void Put.Put(int32 offset)
                {
                }
            }

            public class Host
            {
                public void Test(Arena pool)
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
                public int32 Call(int32 x, int32 y);
            }

            public struct Multiplier : Callable
            {
                public int32 Factor;

                public int32 Callable.Call(int32 x, int32 y)
                {
                    return x;
                }
            }

            public class Host
            {
                public int32 Test(Multiplier m)
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
                public void Test(NonCallable n)
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
                public literal lit1(int32 v);
                public literal lit2(int32 v);
            }

            public class Host
            {
                public void Test()
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
                public void ExpectInt64(int64 val) { }

                public void Test()
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
    public void CFG_ReturnInference_ConflictingReturns_ReportsMH2012()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public var Test(bool cond)
                {
                    if (cond)
                    {
                        return 10;
                    }
                    else
                    {
                        return true;
                    }
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2012");
    }

    [Fact]
    public void CFG_ReturnInference_UnifiedReturns_InfersExactType()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public class Host
            {
                public var Test(bool cond, int32 a, int32 b)
                {
                    if (cond)
                    {
                        return a;
                    }
                    else
                    {
                        return b;
                    }
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context!;
        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Int32, out var hInt32));

        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var method = body.Members.OfType<MemberFunctionDeclaration>().First(m => m.Function.Signature.Identifier.ToString() == "Test");
        var methodSym = Assert.IsAssignableFrom<MethodSymbol>(context.GetSymbol(ResolutionContext.GetHandle(context.MethodSymbols.First(m => m.Name.Text == "Test"))));
        Assert.Equal(hInt32, methodSym.ReturnType.Handle);
    }
}
