using System.Linq;
using Maho;
using Maho.Resolution;
using Maho.Syntax;
using Xunit;

namespace Maho.Tests;

public sealed class CollectionExpressionTests
{
    private const string Prelude = """
        intrinsic attribute IntrinsicType(Str name);
        intrinsic attribute IntrinsicFunction(Str signature);
        intrinsic attribute OperatorFunction(Str name);

        [IntrinsicType("Int8")]
        public struct int8;
        [IntrinsicType("Int32")]
        public struct int32;
        [IntrinsicType("Int64")]
        public struct int64;
        [IntrinsicType("NUInt")]
        public struct nuint;
        [IntrinsicType("Unit")]
        public struct Unit;
        [IntrinsicType("Bool")]
        public struct bool;

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

        [IntrinsicType("Pointer")]
        public struct Pointer<T>
        {
            nuint addr;
        }

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        [IntrinsicType("FixedArray")]
        public struct FixedArray<T, N : int32> : ValType;

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
            ValType* env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }
        """;

    [Fact]
    public void CollectionExpression_Unconstrained_InfersFixedArray()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> Unit
            {
                var arr = [1, 2, 3];
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
        var fn = Assert.Single(compilation.Context!.FunctionSymbols, f => f.Name.Text == "Test");
        var local = Assert.Single(fn.LocalVariables);
        Assert.Equal(TypeRefKind.FixedArray, local.Type.Kind);
        Assert.NotNull(local.Type.SpecialType);
        var fixedArr = Assert.IsType<FixedArraySemanticType>(local.Type.SpecialType);
        var lit = Assert.IsType<LiteralExpression>(fixedArr.Size);
        Assert.Equal("3", lit.Literal.Value);
    }

    [Fact]
    public void CollectionExpression_MissingFixedArrayIntrinsic_ReportsDiagnostic()
    {
        const string PreludeWithoutFixedArray = """
            intrinsic attribute IntrinsicType(Str name);
            intrinsic attribute IntrinsicFunction(Str signature);
            intrinsic attribute OperatorFunction(Str name);

            [IntrinsicType("Int8")]
            public struct int8;
            [IntrinsicType("Int32")]
            public struct int32;
            [IntrinsicType("NUInt")]
            public struct nuint;
            [IntrinsicType("Unit")]
            public struct Unit;
            [IntrinsicType("Bool")]
            public struct bool;

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

            [IntrinsicType("Pointer")]
            public struct Pointer<T>
            {
                nuint addr;
            }

            [IntrinsicType("ValueType")]
            public struct ValType;

            [IntrinsicType("Struct")]
            public struct Struct : ValType;
            """;

        var compilation = Compilation.FromSource(PreludeWithoutFixedArray + """
            public func Test() -> Unit
            {
                var arr = [1, 2, 3];
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0021");
    }

    [Fact]
    public void CollectionExpression_CallableFixedArray_InvokesCallable()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface ArrayCallable
            {
                [OperatorFunction("Callable")]
                public func Invoke(&this) -> int32;
            }

            public struct CustomFixedArray : ValType, ArrayCallable
            {
                public func ArrayCallable.Invoke(&this) -> int32
                {
                    return 42;
                }
            }

            public func Test() -> int32
            {
                CustomFixedArray c;
                int32 res = c();
                return res;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }
}
