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
            public func Test() -> ()
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
            public func Test() -> ()
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
            public struct CustomFixedArray : Callable<int32>
            {
                public func Callable.Invoke(&this) -> int32
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

    [Fact]
    public void CollectionExpression_MultiTerm_UntypedInfersFixedArrayOfTuples()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> ()
            {
                var map = [1 : 2, 3 : 4];
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var fn = Assert.Single(compilation.Context!.FunctionSymbols, f => f.Name.Text == "Test");
        var local = Assert.Single(fn.LocalVariables);
        Assert.Equal(TypeRefKind.FixedArray, local.Type.Kind);
        var fixedArr = Assert.IsType<FixedArraySemanticType>(local.Type.SpecialType);
        Assert.Equal(TypeRefKind.Tuple, fixedArr.ElementType.Kind);
        var tupleSem = Assert.IsType<TupleSemanticType>(fixedArr.ElementType.SpecialType);
        Assert.Equal(2, tupleSem.Elements.Count);
    }

    [Fact]
    public void CollectionExpression_FourTerms_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> ()
            {
                var quads = [1 : 2 : 3 : 4, 5 : 6 : 7 : 8];
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var fn = Assert.Single(compilation.Context!.FunctionSymbols, f => f.Name.Text == "Test");
        var local = Assert.Single(fn.LocalVariables);
        var fixedArr = Assert.IsType<FixedArraySemanticType>(local.Type.SpecialType);
        var tupleSem = Assert.IsType<TupleSemanticType>(fixedArr.ElementType.SpecialType);
        Assert.Equal(4, tupleSem.Elements.Count);
    }

    [Fact]
    public void CollectionExpression_InconsistentTermCount_ReportsMH0568()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> ()
            {
                var bad = [1 : 2, 3];
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0568");
    }

    [Fact]
    public void CollectionExpression_TargetTypedContract_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface Collection<TKey, TValue>
            {
                [OperatorFunction("CollectionAdd")]
                public func Add(mut &this, TKey key, TValue value) -> ();
            }

            public struct MyMap : Collection<int32, int64>
            {
                public func Add(mut &this, int32 key, int64 value) -> () {}
            }

            public func Test() -> ()
            {
                MyMap m = [1 : 100, 2 : 200];
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void CollectionExpression_MissingCollectionContract_ReportsMH0570()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface Collection<T>
            {
                [OperatorFunction("CollectionAdd")]
                public func Add(mut &this, T item) -> ();
            }

            public struct NotACollection;

            public func Test() -> ()
            {
                NotACollection c = [1, 2, 3];
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0570");
    }

    [Fact]
    public void CollectionExpression_WithTrailer_WithoutCollectionWith_ReportsMH0569()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface Collection<T>
            {
                [OperatorFunction("CollectionAdd")]
                public func Add(mut &this, T item) -> ();
            }

            public struct MyList : Collection<int32>
            {
                public func Add(mut &this, int32 item) -> () {}
            }

            public func Test() -> ()
            {
                MyList list = [1, 2] with(10);
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0569");
    }

    [Fact]
    public void CollectionExpression_WithTrailer_WithCollectionWith_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface Collection<T>
            {
                [OperatorFunction("CollectionAdd")]
                public func Add(mut &this, T item) -> ();
            }

            public interface CollectionWith<TCount>
            {
                [OperatorFunction("CollectionWith")]
                public func With(mut &this, TCount capacity) -> ();
            }

            public struct MyList : Collection<int32>, CollectionWith<int32>
            {
                public func Add(mut &this, int32 item) -> () {}
                public func With(mut &this, int32 capacity) -> () {}
            }

            public func Test() -> ()
            {
                MyList list = [1, 2] with(10);
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void MultidimensionalArray_RankAndIndexer_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            [IntrinsicType("Array")]
            public struct Array<T, D : int32>
            {
                T* data;
                nuint len;
            }

            public func Test(int32[,] grid, int32[,,] cube) -> int32
            {
                int32 x = grid[1, 2];
                return x;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var fn = Assert.Single(compilation.Context!.FunctionSymbols, f => f.Name.Text == "Test");
        var gridParam = compilation.Context.GetParameterSymbol(fn.Parameters[0])!;
        Assert.Equal(TypeRefKind.Array, gridParam.Type.Kind);
        var arrSem = Assert.IsType<ArraySemanticType>(gridParam.Type.SpecialType);
        Assert.Equal(2, arrSem.Dimension);

        var cubeParam = compilation.Context.GetParameterSymbol(fn.Parameters[1])!;
        Assert.Equal(TypeRefKind.Array, cubeParam.Type.Kind);
        var cubeSem = Assert.IsType<ArraySemanticType>(cubeParam.Type.SpecialType);
        Assert.Equal(3, cubeSem.Dimension);
    }
}
