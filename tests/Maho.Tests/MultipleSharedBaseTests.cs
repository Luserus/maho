using System.Linq;
using Maho.Resolution;
using Maho.Resolution.Layout;
using Xunit;

namespace Maho.Tests;

public sealed class MultipleSharedBaseTests
{
    private const string Prelude = """
        intrinsic attribute IntrinsicType(Str name);
        intrinsic attribute IntrinsicFunction(Str signature);
        intrinsic attribute OperatorFunction(Str name);

        [IntrinsicType("Int8")]
        public struct int8;
        [IntrinsicType("Int32")]
        public struct Int32;
        [IntrinsicType("NUInt")]
        public struct nuint;
        [IntrinsicType("Unit")]
        public struct Unit;

        [IntrinsicType("Metadata")]
        public struct Metadata<T>;

        public interface Arrow<T>
        {
            [OperatorFunction("IndirectMemberAccess")]
            public func Arrow() -> ref T;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T> : Arrow<T>
        {
            nuint raw;
            Metadata<T> metadata;

            [IntrinsicFunction("Reference.Deref()")]
            public func Arrow.Arrow() -> ref T;
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> : Arrow<T>
        {
            nuint addr;
            Metadata<T> metadata;

            [IntrinsicFunction("Pointer.Deref()")]
            public func Arrow.Arrow() -> ref T;
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            int8& buffer;
            nuint length;
        }

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

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
            ValType* env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(TParams args) -> TReturn;
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

