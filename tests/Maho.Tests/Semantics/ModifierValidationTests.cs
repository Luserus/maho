namespace Maho.Tests;

public sealed class ModifierValidationTests
{
    private const string Prelude = CompilerTestBed.FullPrelude;

    [Fact]
    public void StaticOnFunction_ReportsMH0128()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public static func Calculate() -> int32
            {
                return 42;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0128" && d.Message.Contains("static"));
    }

    [Fact]
    public void StaticOnMethod_ReportsMH0128()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Box
            {
                public static func Calculate() -> int32
                {
                    return 42;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0128" && d.Message.Contains("static"));
    }

    [Fact]
    public void GlobalVariable_WithoutStatic_ReportsMH0128()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public int32 GlobalCount = 10;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0128" && d.Message.Contains("static"));
    }

    [Fact]
    public void GlobalVariable_WithStatic_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public static int32 GlobalCount = 10;
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void OutOfOrderModifiers_ReportsMH0129()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            static public int32 GlobalCount = 10;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0129");
    }

    [Fact]
    public void DuplicateModifiers_ReportsMH0130()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public public struct DoublePublic;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0130");
    }

    [Fact]
    public void ConflictingModifiers_PublicAndPrivate_ReportsMH0131()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Outer
            {
                public private struct Inner;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0131");
    }

    [Fact]
    public void ConflictingModifiers_ConstAndMut_ReportsMH0131()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public static const mut int32 BadVar = 10;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0131");
    }

    [Fact]
    public void TopLevel_PrivateStruct_ReportsMH0128()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            private struct TopSecret;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0128" && d.Message.Contains("private"));
    }

    [Fact]
    public void TopLevel_ProtectedFunction_ReportsMH0128()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            protected func SecretFunc() -> () { }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0128" && d.Message.Contains("protected"));
    }

    [Fact]
    public void UntaggedModifier_ValidOnEnumAndUnion_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public untagged enum Status
            {
                Active,
                Inactive;

                public func Process(untagged this&) -> () { }
            }

            public untagged union Shape
            {
                int32 circle;
                int32 square;

                public func Describe(untagged this&) -> () { }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void UntaggedModifier_DisallowedOnStructClassInterface_ReportsMH0128()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public untagged struct S;
            public untagged class C;
            public untagged interface I;
            """);

        Assert.True(compilation.HasErrors);
        var errors = compilation.Diagnostics.Where(d => d.Code == "MH0128" && d.Message.Contains("untagged")).ToList();
        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void UntaggedModifier_DisallowedOnFunctionAndMethod_ReportsMH0128()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public untagged func TopFunc() -> () { }

            public struct S
            {
                public untagged func MemberMethod(this&) -> () { }
            }
            """);

        Assert.True(compilation.HasErrors);
        var errors = compilation.Diagnostics.Where(d => d.Code == "MH0128" && d.Message.Contains("untagged")).ToList();
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void UntaggedModifier_DisallowedOnReceiverInNonSumType_ReportsMH0128()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct S
            {
                public func Method(untagged this&) -> () { }
            }
            """);

        Assert.True(compilation.HasErrors);
        var err = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0128");
        Assert.Contains("untagged", err.Message);
        Assert.Contains("receiver parameter", err.Message);
    }

    [Fact]
    public void UntaggedModifier_DisallowedOnOrdinaryParameter_ReportsMH0128()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public func TestFunc(untagged int32 x) -> () { }
            """);

        Assert.True(compilation.HasErrors);
        var err = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0128");
        Assert.Contains("untagged", err.Message);
        Assert.Contains("int32", err.Message);
    }

    [Fact]
    public void UntaggedModifier_DisallowedOnFieldsAndGlobals_ReportsMH0128()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct S
            {
                public untagged int32 Field;
            }

            public static untagged int32 GlobalVar;
            """);

        Assert.True(compilation.HasErrors);
        var errors = compilation.Diagnostics.Where(d => d.Code == "MH0128" && d.Message.Contains("untagged")).ToList();
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void InterfaceAndExplicitImplVisibilityModifiers_ReportMH0128_WhileSignatureImplAllowsThem()
    {
        var invalid = Compilation.FromSource($$"""
            {{Prelude}}

            public interface IService
            {
                public func Run(this&) -> ();
                internal func Helper(this&) -> ();
                public int32 Value { public get; }
            }

            public struct Impl : IService
            {
                private int32 raw;

                public func IService.Run(this&) -> () { }
                internal func IService.Helper(this&) -> () { }
                public int32 IService.Value { protected get { return this.raw; } }
            }
            """);

        Assert.True(invalid.HasErrors);
        var mh0128 = invalid.Diagnostics.Where(d => d.Code == "MH0128").ToList();
        Assert.Equal(8, mh0128.Count);

        var validSignatureImpl = Compilation.FromSource($$"""
            {{Prelude}}

            public interface IService
            {
                func Run(this&) -> ();
                int32 Value { get; }
            }

            public struct Impl : IService
            {
                private int32 raw;

                public func Run(this&) -> () { }
                public int32 Value { public get { return this.raw; } }
            }
            """);

        Assert.False(validSignatureImpl.HasErrors, string.Join("\n", validSignatureImpl.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }
}
