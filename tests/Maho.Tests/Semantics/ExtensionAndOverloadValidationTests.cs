namespace Maho.Tests;

public sealed class ExtensionAndOverloadValidationTests
{
    private const string Prelude = CompilerTestBed.FullPrelude;

    [Fact]
    public void SpecializedExtension_MethodCallable()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct MyList<T> { public int32 len; }

            public extension MyList<int32>
            {
                public func SumElements(&this) -> int32
                {
                    return this.len;
                }
            }

            public func Main() -> int32
            {
                MyList<int32> list;
                return list.SumElements();
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void GenericExtension_MethodCallableOnAnyTypeArg()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Container<T> { public int32 count; }

            public extension<T> Container<T>
            {
                public func GetCount(&this) -> int32
                {
                    return this.count;
                }
            }

            public func Main() -> int32
            {
                Container<int32> c;
                return c.GetCount();
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void ConstrainedGenericExtension_SatisfyingConstraint_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public interface Printable { public func Print(&this) -> (); }

            public struct Item : Printable
            {
                public func Print(&this) -> () { }
            }

            public struct Wrapper<T> { }

            public extension<T> Wrapper<T> where T : Printable
            {
                public func Display(&this) -> int32 { return 42; }
            }

            public func Main() -> int32
            {
                Wrapper<Item> w;
                return w.Display();
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void ConstrainedGenericExtension_ViolatingConstraint_ReportsMemberNotFound()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public interface Printable { public func Print(&this) -> (); }

            public struct Plain { }

            public struct Wrapper<T> { }

            public extension<T> Wrapper<T> where T : Printable
            {
                public func Display(&this) -> int32 { return 42; }
            }

            public func Main() -> int32
            {
                Wrapper<Plain> w;
                return w.Display();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2014");
    }

    [Fact]
    public void PartiallySpecializedExtension_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Pair<T, U> { }

            public extension<T> Pair<T, int32>
            {
                public func IntSecond(&this) -> int32 { return 100; }
            }

            public func Main() -> int32
            {
                Pair<bool, int32> p;
                return p.IntSecond();
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void StaticExtensionMethod_VisibleOnTypeItself()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Point { public int32 x; public int32 y; }

            public extension Point
            {
                public func Zero() -> int32 { return 0; }
            }

            public func Main() -> int32
            {
                return Point.Zero();
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void ExtensionImplementsInterface_SatisfiesContract()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public interface Identifiable { public func Id(&this) -> int32; }

            public struct Entity { public int32 id; }

            public extension Entity : Identifiable
            {
                public func Id(&this) -> int32 { return this.id; }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void ExtensionDeclaresField_ReportsMH0170()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Foo { }

            public extension Foo
            {
                public int32 extra;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0170");
    }

    [Fact]
    public void ExtensionSpecifiesBaseStruct_ReportsMH0171()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct BaseStruct { }

            public struct Foo { }

            public extension Foo : BaseStruct
            {
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0171");
    }

    [Fact]
    public void TwoDimensionalEncapsulation_ExtField_AccessibleInExtension()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Encapsulated
            {
                ext int32 secret;
            }

            public extension Encapsulated
            {
                public func GetSecret(&this) -> int32
                {
                    return this.secret;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void TwoDimensionalEncapsulation_ExtField_InaccessibleDirectlyOutside()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Encapsulated
            {
                ext int32 secret;
            }

            public struct Host
            {
                public func Test(Encapsulated e) -> int32
                {
                    return e.secret;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0520");
    }

    [Fact]
    public void TwoDimensionalEncapsulation_InternalExtField_AccessibleDirectlyInModule()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Encapsulated
            {
                internal ext int32 data;
            }

            public struct Host
            {
                public func Test(Encapsulated e) -> int32
                {
                    return e.data;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void MethodOverload_SolelyByReceiver_ReportsMH0564()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Foo
            {
                public func Bar(&this) -> () { }
                public func Bar(mut &this) -> () { }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0564");
    }

    [Fact]
    public void MethodOverload_ValueAndAlias_Ambiguity_ReportsMH0532()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Foo
            {
                public func Bar(int32 x) -> () { }
                public func Bar(&int32 x) -> () { }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0532");
    }

    [Fact]
    public void MethodOverload_ReferentMutability_DistinctTypes_Allowed()
    {
        var compilation = Compilation.FromSource($$"""
            {{Prelude}}

            public struct Foo
            {
                public func Bar(int32& x) -> () { }
                public func Bar(int32 mut& x) -> () { }
            }
            """);

        Assert.False(compilation.HasErrors);
    }
}
