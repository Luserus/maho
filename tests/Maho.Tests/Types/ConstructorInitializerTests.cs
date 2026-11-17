using System.Linq;
using Maho.Resolution;
using Xunit;

namespace Maho.Tests;

public sealed class ConstructorInitializerTests
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
    public void BaseInitializer_OnOrdinaryFunction_EmitsMH0251()
    {
        var source = Prelude + """
            public struct Base;

            public func OrdinaryFunction() -> Unit : Base()
            {
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0251");
    }

    [Fact]
    public void BaseInitializer_OnOrdinaryMethod_EmitsMH0251()
    {
        var source = Prelude + """
            public struct Base;

            public struct Derived : Base
            {
                public func NormalMethod() -> Unit : Base()
                {
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0251");
    }

    [Fact]
    public void BaseInitializer_OnConstructor_Succeeds()
    {
        var source = Prelude + """
            public struct Base
            {
                public Int32 x;
                public func Construct(Int32 val)
                {
                    x = val;
                }
            }

            public struct Derived : Base
            {
                public func Construct(Int32 v) : Base(v)
                {
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void BaseInitializer_NonBaseType_EmitsMH0252()
    {
        var source = Prelude + """
            public struct Other;

            public struct Derived
            {
                public func Construct() : Other()
                {
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0252");
    }

    [Fact]
    public void BaseInitializer_DuplicateBase_EmitsMH0253()
    {
        var source = Prelude + """
            public struct Base;

            public struct Derived : Base
            {
                public func Construct() : Base(), Base()
                {
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0253");
    }

    [Fact]
    public void SharedBase_WithoutDefaultConstructor_RequiresExplicitInit_MH0250()
    {
        var source = Prelude + """
            public struct Root
            {
                public Int32 id;
                public func Construct(Int32 i)
                {
                    id = i;
                }
            }

            public struct MiddleA : Root;
            public struct MiddleB : Root;

            public struct Derived : MiddleA, MiddleB
            {
                public func Construct()
                {
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0250" && d.Message.Contains("Root"));
    }

    [Fact]
    public void Diamond_MostDerivedExplicitlyInitializesRoot_Succeeds()
    {
        var source = Prelude + """
            public struct Root
            {
                public Int32 id;
                public func Construct(Int32 i)
                {
                    id = i;
                }
            }

            public struct MiddleA : Root;
            public struct MiddleB : Root;

            public struct Derived : MiddleA, MiddleB
            {
                public func Construct(Int32 i) : Root(i)
                {
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }
}
