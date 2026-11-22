using System.Linq;
using Maho;
using Maho.Resolution;
using Maho.Syntax;
using Xunit;

namespace Maho.Tests;

public sealed class ForLoopAndIteratorTests
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
        public struct FixedArray<T, N : int32> : ValType, Iterator<T>;

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

        public interface Deconstruct<T...>
        {
            [OperatorFunction("Deconstruct")]
            func Deconstruct(&this) -> (T...);
        }

        [IntrinsicType("Tuple")]
        public struct Tuple<T...> : ValType, Deconstruct<T...>
        {
            [IntrinsicFunction("Tuple.Deconstruct()")]
            public func Deconstruct.Deconstruct(&this) -> (T...);
        }

        public interface Iterable<TItem, TIterator>
        {
            [OperatorFunction("Iterable")]
            func Iterate(&this) -> TIterator;
        }

        public interface Iterator<TItem>
        {
            [OperatorFunction("Iterator")]
            func Next(mut &this) -> TItem;
        }
        """;

    [Fact]
    public void ForLoop_DeclaringVariable_FixedArray_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> Unit
            {
                var arr = [1, 2, 3];
                for (var x in arr)
                {
                    int32 val = x;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void ForLoop_ReusingExistingVariable_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> Unit
            {
                var arr = [1, 2, 3];
                int32 x = 0;
                for (x in arr)
                {
                    int32 val = x;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void ForLoop_CustomIterable_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct MyIter : Iterator<int32>
            {
                public int32 current;

                public func Iterator.Next(mut &this) -> int32
                {
                    return this.current;
                }
            }

            public struct MyCollection : Iterable<int32, MyIter>
            {
                public func Iterable.Iterate(&this) -> MyIter
                {
                    MyIter iter;
                    return iter;
                }
            }

            public func Test(MyCollection col) -> Unit
            {
                for (var item in col)
                {
                    int32 val = item;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void ForLoop_TupleDestructuringInHeader_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> Unit
            {
                var pairs = [(1, 10), (2, 20)];
                for (var (k, v) in pairs)
                {
                    int32 key = k;
                    int32 val = v;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void ForLoop_NonIterableType_ReportsMH0571()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct NotIterable
            {
                public int32 val;
            }

            public func Test(NotIterable col) -> Unit
            {
                for (var x in col)
                {
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0571");
    }

    [Fact]
    public void Generator_YieldInIteratorFunction_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct SimpleGen : Iterator<int32>
            {
                public func Iterator.Next(mut &this) -> int32
                {
                    return 0;
                }
            }

            public func Generate() -> SimpleGen
            {
                yield 1;
                yield 2;
                yield break;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Generator_YieldInNonIteratorFunction_ReportsMH0572()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> Unit
            {
                yield 1;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0572");
    }

    [Fact]
    public void Generator_YieldTypeMismatch_ReportsMH0573()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct SimpleGen : Iterator<int32>
            {
                public func Iterator.Next(mut &this) -> int32
                {
                    return 0;
                }
            }

            public func Generate(Str s) -> SimpleGen
            {
                yield s;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0573");
    }
}
