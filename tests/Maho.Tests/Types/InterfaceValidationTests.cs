using System.Linq;
using Maho.Diagnostics;
using Maho.Resolution;
using Maho.Syntax;
using Xunit;

namespace Maho.Tests;

public sealed class InterfaceValidationTests
{
    private const string StdPrelude = """
        [IntrinsicType("Layout")] public interface Layout;
        [IntrinsicType("Sized")] public interface Sized : Layout;
        [IntrinsicType("Unsized")] public interface Unsized : Layout;

        intrinsic attribute IntrinsicType(&Str name);
        intrinsic attribute IntrinsicFunction(&Str signature);
        intrinsic attribute OperatorFunction(&Str name);

        [IntrinsicType("UInt8")] public struct UInt8;
        [IntrinsicType("Int8")] public struct Int8;
        [IntrinsicType("Int32")] public struct Int32;
        [IntrinsicType("Int64")] public struct Int64;
        [IntrinsicType("NUInt")] public struct NUInt;
        [IntrinsicType("Bool")] public struct Bool;

        [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
        [IntrinsicType("Metadata")] public struct Metadata<T> where T : Layout;

        [IntrinsicType("Reference")]
        public struct Reference<T> where T : Layout { NUInt raw; Metadata<T> metadata; }

        [IntrinsicType("Str")]
        public struct Str { UInt8[] data; }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> where T : Layout { NUInt addr; Metadata<T> metadata; }

        [IntrinsicType("ValueType")] public struct ValType;
        [IntrinsicType("Struct")] public struct Struct : ValType;

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            public func Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            NUInt codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            NUInt codeAddress;
            Pointer<ValType> env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }
        """;

    [Fact]
    public void UnimplementedAbstractMethod_EmitsMH0550()
    {
        var compilation = Compilation.FromSource("""
            public interface IFoo
            {
                public func Bar();
            }

            public struct MyStruct : IFoo;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0550");
        Assert.Contains("'MyStruct' does not implement interface member 'IFoo.Bar'", diag.Message);
    }

    [Fact]
    public void MultipleUnimplementedMethods_EmitsMH0550ForEach()
    {
        var compilation = Compilation.FromSource("""
            public interface IFoo
            {
                public func First();
                public func Second();
            }

            public struct MyStruct : IFoo;
            """);

        Assert.True(compilation.HasErrors);
        var diags = compilation.Diagnostics.Where(d => d.Code == "MH0550").ToList();
        Assert.Equal(2, diags.Count);
        Assert.Contains(diags, d => d.Message.Contains("IFoo.First"));
        Assert.Contains(diags, d => d.Message.Contains("IFoo.Second"));
    }

    [Fact]
    public void DefaultInterfaceMethod_WithBlockOrLambda_DoesNotEmitMH0550()
    {
        var compilation = Compilation.FromSource("""
            public struct Int32;

            public interface IFoo
            {
                public func MethodWithBlock()
                {
                }

                public func MethodWithLambda() => 42;
            }

            public struct MyStruct : IFoo;
            """);

        var diags = compilation.Diagnostics.Where(d => d.Code == "MH0550").ToList();
        Assert.Empty(diags);
    }

    [Fact]
    public void ExplicitInterfaceMethod_SatisfiesInterface()
    {
        var compilation = Compilation.FromSource("""
            public interface IFoo
            {
                public func Bar();
            }

            public struct MyStruct : IFoo
            {
                public func IFoo.Bar()
                {
                }
            }
            """);

        var diags = compilation.Diagnostics.Where(d => d.Code == "MH0550").ToList();
        Assert.Empty(diags);
    }

    [Fact]
    public void ImplicitPublicMethod_SatisfiesInterface()
    {
        var compilation = Compilation.FromSource("""
            public interface IFoo
            {
                public func Bar();
            }

            public struct MyStruct : IFoo
            {
                public func Bar()
                {
                }
            }
            """);

        var diags = compilation.Diagnostics.Where(d => d.Code == "MH0550").ToList();
        Assert.Empty(diags);
    }

    [Fact]
    public void MethodSignatureMismatch_ParamType_EmitsMH0550()
    {
        var compilation = Compilation.FromSource("""
            public struct Int32;
            public struct Str;

            public interface IFoo
            {
                public func Bar(Int32 x) -> Int32;
            }

            public struct MyStruct : IFoo
            {
                public func Bar(Str x) -> Int32
                {
                    return 0;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0550");
        Assert.Contains("IFoo.Bar", diag.Message);
    }

    [Fact]
    public void StaticInterfaceMethod_RequiresStaticImplementation()
    {
        var compilation = Compilation.FromSource("""
            public interface IFoo
            {
                public func Bar();
            }

            public struct InstanceStruct : IFoo
            {
                public func Bar(&this)
                {
                }
            }

            public struct StaticStruct : IFoo
            {
                public func Bar()
                {
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diags = compilation.Diagnostics.Where(d => d.Code == "MH0550").ToList();
        var diag = Assert.Single(diags);
        Assert.Contains("InstanceStruct", diag.Message);
        Assert.DoesNotContain("StaticStruct", diag.Message);
    }

    [Fact]
    public void GenericInterface_SubstitutedSignature_SatisfiesInterface()
    {
        var compilation = Compilation.FromSource("""
            public struct Int32;

            public interface IFoo<T>
            {
                public func Bar(T x);
            }

            public struct MyStruct : IFoo<Int32>
            {
                public func Bar(Int32 x)
                {
                }
            }
            """);

        var diags = compilation.Diagnostics.Where(d => d.Code == "MH0550").ToList();
        Assert.Empty(diags);
    }

    [Fact]
    public void GenericInterface_SubstitutedSignatureMismatch_EmitsMH0550()
    {
        var compilation = Compilation.FromSource("""
            public struct Int32;
            public struct Str;

            public interface IFoo<T>
            {
                public func Bar(T x);
            }

            public struct MyStruct : IFoo<Int32>
            {
                public func Bar(Str x)
                {
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0550");
        Assert.Contains("IFoo.Bar", diag.Message);
    }

    [Fact]
    public void TransitiveInterfaceInheritance_RequiresAllAbstractMembers()
    {
        var compilation = Compilation.FromSource("""
            public interface IBase
            {
                public func A();
            }

            public interface IDerived : IBase
            {
                public func B();
            }

            public struct PartialStruct : IDerived
            {
                public func B()
                {
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0550");
        Assert.Contains("IBase.A", diag.Message);
    }

    [Fact]
    public void BaseStructImplementation_SatisfiesInterfaceOnDerived()
    {
        var compilation = Compilation.FromSource("""
            public interface IFoo
            {
                public func Bar();
            }

            public struct Base
            {
                public func Bar()
                {
                }
            }

            public struct Derived : Base, IFoo;
            """);

        var diags = compilation.Diagnostics.Where(d => d.Code == "MH0550").ToList();
        Assert.Empty(diags);
    }

    [Fact]
    public void InterfaceType_InheritingAnotherInterface_DoesNotRequireImplementingAbstractMembers()
    {
        var compilation = Compilation.FromSource("""
            public interface IBase
            {
                public func A();
            }

            public interface IDerived : IBase
            {
                public func B();
            }
            """);

        var diags = compilation.Diagnostics.Where(d => d.Code == "MH0550").ToList();
        Assert.Empty(diags);
    }

    [Fact]
    public void UnimplementedInterfaceProperty_EmitsMH0550()
    {
        var compilation = Compilation.FromSource("""
            public struct Int32;

            public interface IFoo
            {
                public Int32 Value { get; set; }
            }

            public struct MyStruct : IFoo;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0550");
        Assert.Contains("'MyStruct' does not implement interface member 'IFoo.Value'", diag.Message);
    }

    [Fact]
    public void AutoProperty_SatisfiesInterfaceProperty()
    {
        var compilation = Compilation.FromSource("""
            public struct Int32;

            public interface IFoo
            {
                public Int32 Value { get; set; }
            }

            public struct MyStruct : IFoo
            {
                public Int32 Value { get; set; }
            }
            """);

        var diags = compilation.Diagnostics.Where(d => d.Code == "MH0550").ToList();
        Assert.Empty(diags);
    }

    [Fact]
    public void CustomPropertyAccessors_SatisfiesInterfaceProperty()
    {
        var compilation = Compilation.FromSource("""
            public struct Int32;

            public interface IFoo
            {
                public Int32 Value { get; set; }
            }

            public struct MyStruct : IFoo
            {
                Int32 _v;
                public Int32 Value
                {
                    get => _v;
                    set => _v = value;
                }
            }
            """);

        var diags = compilation.Diagnostics.Where(d => d.Code == "MH0550").ToList();
        Assert.Empty(diags);
    }

    [Fact]
    public void ExplicitInterfaceProperty_SatisfiesInterfaceProperty()
    {
        var compilation = Compilation.FromSource("""
            public struct Int32;

            public interface IFoo
            {
                public Int32 Value { get; set; }
            }

            public struct MyStruct : IFoo
            {
                Int32 _v;
                public Int32 IFoo.Value
                {
                    get => _v;
                    set => _v = value;
                }
            }
            """);

        var diags = compilation.Diagnostics.Where(d => d.Code == "MH0550").ToList();
        Assert.Empty(diags);
    }

    [Fact]
    public void PropertyMissingSetter_EmitsMH0550()
    {
        var compilation = Compilation.FromSource("""
            public struct Int32;

            public interface IFoo
            {
                public Int32 Value { get; set; }
            }

            public struct MyStruct : IFoo
            {
                public Int32 Value { get; }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0550");
        Assert.Contains("IFoo.Value", diag.Message);
    }

    [Fact]
    public void ReadOnlyInterfaceProperty_SatisfiedByGetterOnly_OrGetSet()
    {
        var compilation = Compilation.FromSource("""
            public struct Int32;

            public interface IFoo
            {
                public Int32 Value { get; }
            }

            public struct S1 : IFoo
            {
                public Int32 Value { get; }
            }

            public struct S2 : IFoo
            {
                public Int32 Value { get; set; }
            }
            """);

        var diags = compilation.Diagnostics.Where(d => d.Code == "MH0550").ToList();
        Assert.Empty(diags);
    }

    [Fact]
    public void DefaultComputedInterfaceProperty_NotRequiredInImplementingType()
    {
        var compilation = Compilation.FromSource("""
            public struct Int32;

            public interface IFoo
            {
                public Int32 Value { get => 42; }
            }

            public struct MyStruct : IFoo;
            """);

        var diags = compilation.Diagnostics.Where(d => d.Code == "MH0550").ToList();
        Assert.Empty(diags);
    }

    [Fact]
    public void PropertyThisAndValue_ResolveCleanlyInCustomSetters()
    {
        var compilation = Compilation.FromSource(StdPrelude + """
            public struct Container
            {
                public Int32 field;

                public Int32 Prop
                {
                    get { return this.field; }
                    set { this.field = value; }
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }
}

