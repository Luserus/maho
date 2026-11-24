using System.Linq;
using Maho.Resolution;
using Xunit;

namespace Maho.Tests;

public sealed class CrossProjectDynTests
{
    private const string LibPrelude = """
        namespace Lib;

        intrinsic attribute IntrinsicType(Str name);
        intrinsic attribute IntrinsicFunction(Str signature);
        intrinsic attribute OperatorFunction(Str name);

        [IntrinsicType("Int8")]
        public struct int8;
        [IntrinsicType("Int32")]
        public struct Int32;
        [IntrinsicType("NUInt")]
        public struct nuint;

        [IntrinsicType("Reference")]
        public struct Reference<T>
        {
            nuint raw;
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T>
        {
            nuint addr;
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

        public struct Base
        {
            public Int32 id;
        }

        public struct MiddleA : Base
        {
            public Int32 a;
        }

        public struct MiddleB : Base
        {
            public Int32 b;
        }
        """;

    [Fact]
    public void ExternalProject_ExtendingDiamondBases_LinearizesAndEnforcesContiguity()
    {
        var libCompilation = Compilation.FromSource(LibPrelude, "Lib.mh");
        Assert.False(libCompilation.HasErrors, string.Join("\n", libCompilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var appSource = """
            namespace App;
            using Lib;

            public struct Derived : MiddleA, MiddleB
            {
                public Int32 d;
            }

            public func TestContiguous(Derived& d) -> MiddleA&
            {
                MiddleA& refA = d;
                return refA;
            }

            public func TestDynSplit(Derived& d) -> dyn MiddleB&
            {
                dyn MiddleB& refB = d;
                return refB;
            }
            """;

        var appCompilation = Compilation.FromSource(
            appSource,
            "App.mh",
            referencedCompilations: [libCompilation]);

        Assert.False(appCompilation.HasErrors, string.Join("\n", appCompilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var derivedSym = appCompilation.Context.TypeSymbols.First(t => t.Name.Text == "Derived");
        var baseNames = derivedSym.LinearizedBases
            .Skip(1)
            .Select(bh => appCompilation.Context.GetTypeSymbol(bh)?.Name.Text)
            .Where(n => n is not null)
            .ToList();

        Assert.Equal("MiddleA", baseNames[0]);
        Assert.Equal("MiddleB", baseNames[1]);
        Assert.Equal("Base", baseNames[2]);

        Assert.Equal("MiddleA", baseNames[0]);
        Assert.Equal("MiddleB", baseNames[1]);
        Assert.Equal("Base", baseNames[2]);
    }

    [Fact]
    public void ExternalProject_ThinReferenceToSplitBase_EmitsMH0555()
    {
        var libCompilation = Compilation.FromSource(LibPrelude, "Lib.mh");
        Assert.False(libCompilation.HasErrors);

        var appSource = """
            namespace App;
            using Lib;

            public struct Derived : MiddleA, MiddleB
            {
                public Int32 d;
            }

            public func TestThinSplit(Derived& d) -> ()
            {
                MiddleB& refB = d;
            }
            """;

        var appCompilation = Compilation.FromSource(
            appSource,
            "App.mh",
            referencedCompilations: [libCompilation]);

        Assert.True(appCompilation.HasErrors);
        Assert.Contains(appCompilation.Diagnostics, d => d.Code == "MH0555" && d.Message.Contains("MiddleB"));
    }
}
