namespace Maho.Tests;

public sealed class ForLoopAndIteratorTests
{
    private const string Prelude = """
        intrinsic attribute IntrinsicType(&Str name);
        intrinsic attribute IntrinsicFunction(&Str signature);
        intrinsic attribute IntrinsicProperty(&Str signature);
        intrinsic attribute OperatorFunction(&Str name);

        [IntrinsicType("Layout")]
        public interface Layout;
        [IntrinsicType("Sized")]
        public interface Sized : Layout;
        [IntrinsicType("Unsized")]
        public interface Unsized : Layout;

        [IntrinsicType("UInt8")]
        public struct uint8;
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

        [IntrinsicType("Metadata")]
        public struct Metadata<T> where T : Layout;

        public interface Deref<TReturn>
        {
            [OperatorFunction("Dereference")]
            func Deref(&this) -> TReturn;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T> : Deref<T&> where T : Layout
        {
            public nuint raw;
            public Metadata<T> metadata;

            [IntrinsicFunction("Reference.Deref()")]
            func Deref.Deref(&this) -> T&;
        }

        public struct SliceIterator<T, D : int = 1> : Iterator<T>
        {
            public T[]& data;
            public nuint index;

            func Iterator.Next(mut &this) -> T
            {
                return (*this.data)[this.index];
            }
        }

        [IntrinsicType("Slice")]
        public struct Slice<T, D : int = 1> : Iterable<T, SliceIterator<T, D>>
        {
            [IntrinsicProperty("Slice.Length")]
            public nuint Length { get; }

            func Iterable.Iterate(&this) -> SliceIterator<T, D>
            {
                return put SliceIterator<T, D> { data = &this, index = 0 };
            }
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            uint8[] data;
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> where T : Layout
        {
            nuint addr;
            Metadata<T> metadata;
        }

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        [IntrinsicType("Array")]
        public struct Array<T, N : int, D : int = 1> : ValType, Iterable<T, SliceIterator<T, D>>
        {
            [IntrinsicFunction("Array.AsSlice()")]
            public func AsSlice(&this) -> T[]&;

            func Iterable.Iterate(&this) -> SliceIterator<T, D>
            {
                return put SliceIterator<T, D> { data = this.AsSlice(), index = 0 };
            }
        }

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            func Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            ValType* env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            func Callable.Invoke(&this, TParams args) -> TReturn;
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
            func Deconstruct.Deconstruct(&this) -> (T...);
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
            public func Test() -> ()
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
            public func Test() -> ()
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

                func Iterator.Next(mut &this) -> int32
                {
                    return this.current;
                }
            }

            public struct MyCollection : Iterable<int32, MyIter>
            {
                func Iterable.Iterate(&this) -> MyIter
                {
                    MyIter iter = default;
                    return iter;
                }
            }

            public func Test(MyCollection col) -> ()
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
            public func Test() -> ()
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

            public func Test(NotIterable col) -> ()
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
                func Iterator.Next(mut &this) -> int32
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
            public func Test() -> ()
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
                func Iterator.Next(mut &this) -> int32
                {
                    return 0;
                }
            }

            public func Generate(&Str s) -> SimpleGen
            {
                yield s;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0573");
    }
}
