using Maho.Resolution;
using Maho.Resolution.Layout;

namespace Maho.Tests;

public sealed class MultipleSharedBaseTests
{
    private const string Prelude = """
        [IntrinsicType("Layout")]
        public interface Layout;
        [IntrinsicType("Sized")]
        public interface Sized : Layout;
        [IntrinsicType("Unsized")]
        public interface Unsized : Layout;

        intrinsic attribute IntrinsicType(&Str name);
        intrinsic attribute IntrinsicFunction(&Str signature);
        intrinsic attribute OperatorFunction(&Str name);

        [IntrinsicType("UInt8")]
        public struct uint8;
        [IntrinsicType("Int8")]
        public struct int8;
        [IntrinsicType("Int32")]
        public struct Int32;
        [IntrinsicType("NUInt")]
        public struct nuint;

        [IntrinsicType("Slice")]
        public struct Slice<T, D : int = 1>;

        [IntrinsicType("Metadata")]
        public struct Metadata<T> where T : Layout;

        public interface Deref<T> where T : Layout
        {
            [OperatorFunction("Dereference")]
            func Deref(&this) -> &T;
        }

        public interface Arrow<T> where T : Layout
        {
            [OperatorFunction("IndirectMemberAccess")]
            func Arrow(&this) -> &T;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T> : Arrow<T>, Deref<T> where T : Layout
        {
            nuint raw;
            Metadata<T> metadata;

            [IntrinsicFunction("Reference.Deref()")]
            func Deref.Deref(&this) -> &T;

            func Arrow.Arrow(&this) -> &T
            {
                return *this;
            }
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> : Arrow<T>, Deref<T> where T : Layout
        {
            nuint addr;
            Metadata<T> metadata;

            [IntrinsicFunction("Pointer.Deref()")]
            func Deref.Deref(&this) -> &T;

            func Arrow.Arrow(&this) -> &T
            {
                return *this;
            }
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            uint8[] data;
        }

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

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
        """;

    [Fact]
    public void TwoSharedRoots_DeduplicatedInC3AndLayout()
    {
        var source = Prelude + """
            public struct RootA
            {
                public Int32 a;
            }

            public struct RootB
            {
                public Int32 b;
            }

            public struct M1 : RootA, RootB
            {
                public Int32 m1;
            }

            public struct M2 : RootA
            {
                public Int32 m2;
            }

            public struct M3 : RootB
            {
                public Int32 m3;
            }

            public struct Derived : M1, M2, M3
            {
                public Int32 d;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var derivedSym = compilation.Context.TypeSymbols.First(t => t.Name.Text == "Derived");
        var baseNames = derivedSym.LinearizedBases
            .Skip(1)
            .Select(bh => compilation.Context.GetTypeSymbol(bh)?.Name.Text)
            .Where(n => n is not null)
            .ToList();

        // RootA and RootB must each appear exactly once
        Assert.Single(baseNames, n => n == "RootA");
        Assert.Single(baseNames, n => n == "RootB");
        Assert.Single(baseNames, n => n == "M1");
        Assert.Single(baseNames, n => n == "M2");
        Assert.Single(baseNames, n => n == "M3");

        var derivedHandle = ResolutionContext.GetHandle(derivedSym);
        var rootAHandle = ResolutionContext.GetHandle(compilation.Context.TypeSymbols.First(t => t.Name.Text == "RootA"));

        Assert.True(TypeContiguityAnalyzer.IsBaseContiguous(derivedHandle, rootAHandle, compilation.Context));
    }

    [Fact]
    public void TwoSharedRoots_RequiresInitializingBothInConstructor()
    {
        var source = Prelude + """
            public struct RootA
            {
                public Int32 a;
                public func Construct(Int32 val) { a = val; }
            }

            public struct RootB
            {
                public Int32 b;
                public func Construct(Int32 val) { b = val; }
            }

            public struct M1 : RootA, RootB;
            public struct M2 : RootA;

            public struct Derived : M1, M2
            {
                public func Construct(Int32 a, Int32 b) : RootA(a), RootB(b)
                {
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }
}

