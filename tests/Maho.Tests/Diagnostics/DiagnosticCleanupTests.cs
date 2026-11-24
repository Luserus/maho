using System.Linq;
using Maho.Diagnostics;
using Maho.Resolution;
using Maho.Syntax;
using Xunit;

namespace Maho.Tests;

public sealed class DiagnosticCleanupTests
{
    private const string StdPrelude = """
        namespace Std
        {
            intrinsic attribute IntrinsicType(Str name);
            intrinsic attribute IntrinsicFunction(Str signature);
            intrinsic attribute OperatorFunction(Str name);

            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int16")] public struct Int16;
            [IntrinsicType("Int32")] public struct Int32;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("NUInt")] public struct NUInt;
            [IntrinsicType("Bool")] public struct Bool;

            [IntrinsicType("Reference")]
            public struct Reference<T> { NUInt raw; }

            [IntrinsicType("Str")]
            public struct Str { Int8& buffer; NUInt length; }

            [IntrinsicType("Pointer")]
            public struct Pointer<T> { NUInt addr; }

            [IntrinsicType("ValueType")] public struct ValType;
            [IntrinsicType("Struct")] public struct Struct : ValType;
            [IntrinsicType("Enum")] public struct Enum : ValType;
            [IntrinsicType("Tuple")] public struct Tuple<T...> : ValType;

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
        }
        """;

    [Fact]
    public void TypeMismatch_MainMessageHasFullyQualifiedType_LabelHasImportedOrDeclaredType()
    {
        var compilation = Compilation.FromSource(StdPrelude + """
            using Std;

            public func Test() -> ()
            {
                Int32 x;
                Int64 y = x;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH2003");
        Assert.Contains("cannot assign expression of type 'Std.Int32' to variable of type 'Std.Int64'", diag.Message);
        var primary = diag.Labels.First(l => l.Style == DiagnosticLabelStyle.Primary);
        Assert.Equal("Int32", primary.Message);
    }

    [Fact]
    public void TypeMismatch_AliasedType_LabelShowsAliasName_MainMessageShowsQualifiedType()
    {
        var compilation = Compilation.FromSource(StdPrelude + """
            using Std;
            using MyInt = Std.Int32;
            using MyLong = Std.Int64;

            public func Test() -> ()
            {
                MyInt x;
                MyLong y = x;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH2003");
        Assert.Contains("cannot assign expression of type 'Std.Int32' to variable of type 'Std.Int64'", diag.Message);
        var primary = diag.Labels.First(l => l.Style == DiagnosticLabelStyle.Primary);
        Assert.Equal("MyInt", primary.Message);
    }

    [Fact]
    public void MemberNotFound_OnFunctionPointer_ShowsFunctionPointerInLabel_FullyQualifiedInMain()
    {
        var compilation = Compilation.FromSource(StdPrelude + """
            using Std;

            public func Test(func(Int32) -> Int64 fp) -> ()
            {
                fp.NonExistent;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH2014");
        Assert.Contains("type 'Std.Function<Int64, Int32>' does not contain a definition for 'NonExistent'", diag.Message);
        var primary = diag.Labels.First(l => l.Style == DiagnosticLabelStyle.Primary);
        Assert.Equal("'NonExistent' not found on 'func(Int32) -> Int64'", primary.Message);
    }

    [Fact]
    public void MemberNotFound_OnDirectFunction_ShowsFunctionSyntaxInLabel_FullyQualifiedInMain()
    {
        var compilation = Compilation.FromSource(StdPrelude + """
            using Std;

            public func TargetFn(Int32 a) -> Int64
            {
                Int64 res;
                return res;
            }

            public func Test() -> ()
            {
                TargetFn.NonExistent;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH2014");
        Assert.Contains("type 'Std.Function<Int64, Int32>' does not contain a definition for 'NonExistent'", diag.Message);
        var primary = diag.Labels.First(l => l.Style == DiagnosticLabelStyle.Primary);
        Assert.Equal("'NonExistent' not found on 'func(Int32) -> Int64'", primary.Message);
    }

    [Fact]
    public void MemberNotFound_OnGenericStructSyntax_ShowsGenericStructInLabel_FullyQualifiedInMain()
    {
        var compilation = Compilation.FromSource(StdPrelude + """
            using Std;

            public func Test(Function<Int64, Int32> f) -> ()
            {
                f.NonExistent;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH2014");
        Assert.Contains("type 'Std.Function<Int64, Int32>' does not contain a definition for 'NonExistent'", diag.Message);
        var primary = diag.Labels.First(l => l.Style == DiagnosticLabelStyle.Primary);
        Assert.Equal("'NonExistent' not found on 'Function<Int64, Int32>'", primary.Message);
    }

    [Fact]
    public void CustomNamespace_IntrinsicDelegate_QualifiesToCustomNamespace()
    {
        var compilation = Compilation.FromSource("""
            namespace TestLib
            {
                intrinsic attribute IntrinsicType(Str name);
                intrinsic attribute IntrinsicFunction(Str signature);
                intrinsic attribute OperatorFunction(Str name);

                [IntrinsicType("Int8")] public struct MyInt8;
                [IntrinsicType("NUInt")] public struct MyNUInt;
                [IntrinsicType("Reference")] public struct Reference<T> { MyNUInt raw; }
                [IntrinsicType("Str")] public struct Str { MyInt8& buffer; MyNUInt length; }
                [IntrinsicType("Pointer")] public struct Pointer<T> { MyNUInt addr; }
                [IntrinsicType("ValueType")] public struct ValType;

                [IntrinsicType("Int32")] public struct MyInt32;
                public struct MyUnit;

                public interface Callable<TReturn, TParams...>
                {
                    [OperatorFunction("Callable")]
                    public func Invoke(&this, TParams args) -> TReturn;
                }

                [IntrinsicType("Delegate")]
                public struct MyDelegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
                {
                    MyNUInt codeAddress;
                    Pointer<ValType> env;
                    [IntrinsicFunction("MyDelegate.Invoke(TParams...)")]
                    public func Callable.Invoke(&this, TParams args) -> TReturn;
                }
            }

            using TestLib;

            public struct Host
            {
                public func Test(delegate(MyInt32) -> MyUnit del) -> MyUnit
                {
                    del.NonExistent;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH2014");
        Assert.Contains("type 'TestLib.MyDelegate<MyUnit, MyInt32>' does not contain a definition for 'NonExistent'", diag.Message);
        var primary = diag.Labels.First(l => l.Style == DiagnosticLabelStyle.Primary);
        Assert.Equal("'NonExistent' not found on 'delegate(MyInt32) -> MyUnit'", primary.Message);
    }

    [Fact]
    public void BinaryOperatorMismatch_SuggestsOperatorFunctionHelp()
    {
        var compilation = Compilation.FromSource(StdPrelude + """
            using Std;

            public struct CustomPoint;

            public func Test() -> ()
            {
                CustomPoint a;
                CustomPoint b;
                var c = a + b;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH2001");
        Assert.NotEmpty(diag.HelpMessages);
        Assert.Contains("implement an operator interface marked with [OperatorFunction(\"Addition\")]", diag.HelpMessages[0].Message);
    }
}
