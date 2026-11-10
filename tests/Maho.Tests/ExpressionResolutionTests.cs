using System.Linq;
using Maho.Diagnostics;
using Maho.Resolution;
using Maho.Syntax;
using Xunit;

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
        public struct int32;

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
}
