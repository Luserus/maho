using Maho.Resolution;

namespace Maho.Tests;

public sealed class ThisGenericParameterAndDefaultsTests
{
    [Fact]
    public void ThisGeneric_InvalidPlacementOnStructAndFunction_ReportsMH0584()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct BadStruct<T : this>
            {
                public int32 v;
            }

            public func BadFn<T : this>() -> ()
            {
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Equal(2, compilation.Diagnostics.Count(d => d.Code == "MH0584"));
    }

    [Fact]
    public void ThisGeneric_ExactTypeEnforcement_AcceptsSelf_RejectsBaseOrUnrelated_MH0585()
    {
        var valid = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Clonable<T : this>
            {
                func Clone(&this) -> T;
            }

            public struct Base : Clonable<Base>
            {
                public int32 id;
                public func Clone(&this) -> Base { return put Base { id = this.id }; }
            }
            """);

        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var wrongDerivedBaseArg = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Clonable<T : this>
            {
                func Clone(&this) -> T;
            }

            public struct Base : Clonable<Base>
            {
                public int32 id;
                public func Clone(&this) -> Base { return put Base { id = this.id }; }
            }

            public struct Derived : Base, Clonable<Base>
            {
                public func Clone(&this) -> Base { return put Base { id = 1 }; }
            }
            """);

        Assert.Contains(wrongDerivedBaseArg.Diagnostics, d => d.Code == "MH0585");

        var unrelated = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Clonable<T : this>
            {
                func Clone(&this) -> T;
            }

            public struct Cat { public int32 age; }
            public struct Dog : Clonable<Cat>
            {
                public func Clone(&this) -> Cat { return put Cat { age = 3 }; }
            }
            """);

        Assert.Contains(unrelated.Diagnostics, d => d.Code == "MH0585");
    }

    [Fact]
    public void ThisGeneric_DefaultSelf_AllowsShorthand_WhileWithoutDefaultRequiresExplicit_MH0586()
    {
        var shorthand = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Eq<T : this = this>
            {
                func Same(&this, &T other) -> bool;
            }

            public struct Point : Eq
            {
                public int32 x;
                public func Same(&this, &Point other) -> bool { return true; }
            }

            public func Check<U>(U a, &U b) -> bool where U : Eq
            {
                return a.Same(b);
            }

            public func Run() -> bool
            {
                Point p = put Point { x = 5 };
                return Check::<Point>(p, p);
            }
            """);

        Assert.False(shorthand.HasErrors, string.Join("\n", shorthand.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var missingExplicit = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Eq<T : this>
            {
                func Same(&this, &T other) -> bool;
            }

            public struct Point : Eq
            {
                public int32 x;
                public func Same(&this, &Point other) -> bool { return true; }
            }
            """);

        Assert.Contains(missingExplicit.Diagnostics, d => d.Code == "MH0586");
    }

    [Fact]
    public void ThisGeneric_DynExistentialWithConcreteArg_Succeeds_WhileDynMethodThisParam_ReportsMH0587()
    {
        var okDyn = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Eq<T : this = this>
            {
                func Same(&this, &T other) -> bool;
            }

            public struct Point : Eq
            {
                public int32 x;
                public func Same(&this, &Point other) -> bool { return true; }
            }

            public func CompareWithDyn(dyn &Eq<Point> item, &Point other) -> bool
            {
                return item.Same(other);
            }
            """);

        Assert.False(okDyn.HasErrors, string.Join("\n", okDyn.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var badDynMethod = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Comparable
            {
                func Cmp<T : this>(&this, &T other) -> bool;
            }

            public struct Point : Comparable
            {
                public int32 x;
                public func Cmp<T : this>(&this, &T other) -> bool { return true; }
            }

            public func BadCall(dyn &Comparable item, &Point other) -> bool
            {
                return item.Cmp::<Point>(other);
            }
            """);

        Assert.Contains(badDynMethod.Diagnostics, d => d.Code == "MH0587");
    }

    [Fact]
    public void ThisGeneric_MethodLevelMismatch_ReportsMH0585()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Item
            {
                public int32 v;
                public func Check<T : this>(&this, &T other) -> bool { return true; }
            }

            public func Run() -> bool
            {
                Item it = put Item { v = 1 };
                int32 num = 2;
                return it.Check::<int32>(num);
            }
            """);

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0585");
    }

    [Fact]
    public void DefaultInterfaceMethod_InheritedWhenOmitted_AndOverridable_InConformanceMap()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Logger
            {
                func Tag(&this) -> int32
                {
                    return 7;
                }
            }

            public struct DefaultUser : Logger
            {
                public int32 id;
            }

            public struct CustomUser : Logger
            {
                public int32 id;
                func Logger.Tag(&this) -> int32
                {
                    return 99;
                }
            }

            public func CallConstrained<T>(&T item) -> int32 where T : Logger
            {
                return item.Tag();
            }

            public func Run() -> int32
            {
                DefaultUser d = put DefaultUser { id = 1 };
                CustomUser c = put CustomUser { id = 2 };
                (d as Logger).Tag();
                CallConstrained::<CustomUser>(c);
                return (c as Logger).Tag();
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var directCallHidden = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Logger
            {
                func Tag(&this) -> int32
                {
                    return 7;
                }
            }

            public struct DefaultUser : Logger
            {
                public int32 id;
            }

            public struct CustomUser : Logger
            {
                public int32 id;
                func Logger.Tag(&this) -> int32
                {
                    return 99;
                }
            }

            public func Run() -> int32
            {
                DefaultUser d = put DefaultUser { id = 1 };
                CustomUser c = put CustomUser { id = 2 };
                d.Tag();
                return c.Tag();
            }
            """);

        Assert.Equal(2, directCallHidden.Diagnostics.Count(d => d.Code == "MH2014"));

        var loggerIface = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.Text == "Logger");
        var defaultUser = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.Text == "DefaultUser");
        var customUser = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.Text == "CustomUser");
        var ifaceMethod = Assert.Single(compilation.Context.MethodSymbols, m => m.Parent == ResolutionContext.GetHandle(loggerIface));
        var customMethod = Assert.Single(compilation.Context.MethodSymbols, m => m.Parent == ResolutionContext.GetHandle(customUser));

        var defaultConf = Assert.Single(compilation.Context.Conformances, c => c.OwnerHandle == ResolutionContext.GetHandle(defaultUser) && c.InterfaceHandle == ResolutionContext.GetHandle(loggerIface));
        var customConf = Assert.Single(compilation.Context.Conformances, c => c.OwnerHandle == ResolutionContext.GetHandle(customUser) && c.InterfaceHandle == ResolutionContext.GetHandle(loggerIface));

        Assert.Equal(ResolutionContext.GetHandle(ifaceMethod), defaultConf.MethodMap[ResolutionContext.GetHandle(ifaceMethod)]);
        Assert.Equal(ResolutionContext.GetHandle(customMethod), customConf.MethodMap[ResolutionContext.GetHandle(ifaceMethod)]);
    }

    [Fact]
    public void IntrinsicType_MissingDeclaredInterfaceMethod_ReportsMH0550()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(&Str name);
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("UInt8")] public struct uint8;
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("Int32")] public struct int32;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;

            public interface NeedsTag
            {
                func Tag(&this) -> int32;
            }

            [IntrinsicType("Str")]
            public struct Str : NeedsTag
            {
                public uint8[] data;
            }
            """);

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0550");
    }
}
