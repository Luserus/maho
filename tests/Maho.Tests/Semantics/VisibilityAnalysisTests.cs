namespace Maho.Tests;

public sealed class VisibilityAnalysisTests
{
    private const string VisibilityPrelude = CompilerTestBed.FullPrelude;

    [Fact]
    public void PrivateMember_AccessibleFromWithinDeclaringType_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{VisibilityPrelude}}

            public struct SecretBox
            {
                private int32 secret;

                public func Read(&this) -> int32
                {
                    return this.secret;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void PrivateMember_InaccessibleFromOutside_ReportsMH0520()
    {
        var compilation = Compilation.FromSource($$"""
            {{VisibilityPrelude}}

            public struct SecretBox
            {
                private int32 secret;
            }

            public struct Host
            {
                public func Test(SecretBox box) -> int32
                {
                    return box.secret;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0520");
    }

    [Fact]
    public void PrivateMethod_InaccessibleFromOutside_ReportsMH0520()
    {
        var compilation = Compilation.FromSource($$"""
            {{VisibilityPrelude}}

            public struct SecretBox
            {
                private func Compute(&this) -> int32
                {
                    return 42;
                }
            }

            public struct Host
            {
                public func Test(SecretBox box) -> int32
                {
                    return box.Compute();
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0520");
    }

    [Fact]
    public void ProtectedMember_AccessibleInDerivedType_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{VisibilityPrelude}}

            public struct BaseBox
            {
                protected int32 sharedValue;
            }

            public struct DerivedBox : BaseBox
            {
                public func GetValue(&this) -> int32
                {
                    return this.sharedValue;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void ProtectedMember_InaccessibleInUnrelatedType_ReportsMH0521()
    {
        var compilation = Compilation.FromSource($$"""
            {{VisibilityPrelude}}

            public struct BaseBox
            {
                protected int32 sharedValue;
            }

            public struct Unrelated
            {
                public func Test(BaseBox b) -> int32
                {
                    return b.sharedValue;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0521");
    }

    [Fact]
    public void InternalMember_AccessibleWithinSameProject_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{VisibilityPrelude}}

            internal struct InternalData
            {
                public int32 x;
            }

            public struct Consumer
            {
                public func Test(InternalData data) -> int32
                {
                    return data.x;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void InternalMember_InaccessibleAcrossProjectBoundaries_ReportsMH0522()
    {
        var libCompilation = Compilation.FromSource($$"""
            {{VisibilityPrelude}}

            public struct LibraryBox
            {
                internal int32 internalField;
            }
            """, "Lib.mh");

        Assert.False(libCompilation.HasErrors);

        var consumerCompilation = Compilation.FromSource("""
            public struct Client
            {
                public func Test(LibraryBox b) -> int32
                {
                    return b.internalField;
                }
            }
            """, "Consumer.mh", referencedCompilations: [libCompilation]);

        Assert.True(consumerCompilation.HasErrors);
        Assert.Contains(consumerCompilation.Diagnostics, d => d.Code == "MH0522");
    }

    [Fact]
    public void InternalType_InaccessibleAcrossProjectBoundaries_ReportsMH0523()
    {
        var libCompilation = Compilation.FromSource($$"""
            {{VisibilityPrelude}}

            internal struct SecretLibraryType
            {
                public int32 id;
            }
            """, "Lib.mh");

        Assert.False(libCompilation.HasErrors);

        var consumerCompilation = Compilation.FromSource("""
            public struct Client
            {
                public func Test(SecretLibraryType s) -> int32
                {
                    return s.id;
                }
            }
            """, "Consumer.mh", referencedCompilations: [libCompilation]);

        Assert.True(consumerCompilation.HasErrors);
        Assert.Contains(consumerCompilation.Diagnostics, d => d.Code == "MH0523");
    }

    [Fact]
    public void PublicMember_AccessibleAcrossProjectBoundaries_Succeeds()
    {
        var libCompilation = Compilation.FromSource($$"""
            {{VisibilityPrelude}}

            public struct PublicLibraryType
            {
                public int32 id;
            }
            """, "Lib.mh");

        Assert.False(libCompilation.HasErrors);

        var consumerCompilation = Compilation.FromSource("""
            public struct Client
            {
                public func Test(PublicLibraryType s) -> int32
                {
                    return s.id;
                }
            }
            """, "Consumer.mh", referencedCompilations: [libCompilation]);

        Assert.False(consumerCompilation.HasErrors);
    }

    [Fact]
    public void PropertyAccessors_PublicGetPrivateSet_AllowedRead_ForbiddenWrite()
    {
        var compilation = Compilation.FromSource($$"""
            {{VisibilityPrelude}}

            public struct Config
            {
                public int32 Setting
                {
                    public get;
                    private set;
                }
            }

            public struct Host
            {
                public func Reader(Config c) -> int32
                {
                    return c.Setting;
                }

                public func Writer(mut Config c) -> void
                {
                    c.Setting = 10;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0520" && d.Message.Contains("set"));
        Assert.DoesNotContain(compilation.Diagnostics, d => d.Code == "MH0520" && d.Message.Contains("get"));
    }

    [Fact]
    public void NestedPrivateType_InaccessibleOutsideEnclosingType_ReportsMH0523()
    {
        var compilation = Compilation.FromSource($$"""
            {{VisibilityPrelude}}

            public struct Outer
            {
                private struct Inner
                {
                    public int32 val;
                }
            }

            public struct Host
            {
                public func Test(Outer.Inner i) -> int32
                {
                    return i.val;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0523");
    }

    [Fact]
    public void TopLevel_PrivateModifier_ReportsMH0128()
    {
        var compilation = Compilation.FromSource($$"""
            {{VisibilityPrelude}}

            private struct TopLevelSecret;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0128" && d.Message.Contains("private"));
    }

    [Fact]
    public void TopLevel_ProtectedModifier_ReportsMH0128()
    {
        var compilation = Compilation.FromSource($$"""
            {{VisibilityPrelude}}

            protected func TopLevelProtected() -> void { }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0128" && d.Message.Contains("protected"));
    }
}
