using System.Linq;
using Maho.Resolution;
using Miryo.Build;
using Xunit;

namespace Maho.Tests.Semantics;

public sealed class ScopedConformanceAndModuleResolutionTests
{
    private const string SharedIfaceModule = CompilerTestBed.FullPrelude + """
        public interface Bar
        {
            func Baz(this&) -> int32;
        }

        public struct Foo
        {
            public int32 v;
        }

        public struct Holder<T : Bar>
        {
            public int32 tag;
        }
        """;

    [Fact]
    public void DefaultBaseListVisibility_IsPublic_AcrossModules()
    {
        var modA = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Greetable
            {
                func Greet(this&) -> int32;
            }

            public struct Person : Greetable
            {
                public int32 id;
                public func Greet(this&) -> int32 { return this->id; }
            }
            """,
            "ModA.mh",
            new CompilationOptions { ModuleName = "ModA" });

        Assert.False(modA.HasErrors);

        var modB = Compilation.FromSource("""
            public func ReadGreeting(Greetable& g) -> int32
            {
                return g->Greet();
            }

            public func Run() -> int32
            {
                Person p = new Person { id = 42 };
                return ReadGreeting(&p);
            }
            """,
            "ModB.mh",
            new CompilationOptions { ModuleName = "ModB" },
            referencedCompilations: [modA]);

        Assert.False(modB.HasErrors);
    }

    [Fact]
    public void SingleDefinitionDuplicateInterface_ReportsMH0565()
    {
        var onStruct = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Bar { func Baz(this&) -> int32; }

            public struct Foo : private Bar, public Bar
            {
                func Bar.Baz(this&) -> int32 { return 1; }
            }
            """);

        Assert.True(onStruct.HasErrors);
        Assert.Contains(onStruct.Diagnostics, d => d.Code == "MH0565");

        var onExt = Compilation.FromSource(SharedIfaceModule + """
            public extension Foo : private Bar, Bar
            {
                func Bar.Baz(this&) -> int32 { return 1; }
            }
            """);

        Assert.True(onExt.HasErrors);
        Assert.Contains(onExt.Diagnostics, d => d.Code == "MH0565");
    }

    [Fact]
    public void MultipleNonPrivateConformancesInSameModule_ReportMH0565()
    {
        var structAndExt = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Bar { func Baz(this&) -> int32; }

            public struct Foo : Bar
            {
                func Bar.Baz(this&) -> int32 { return 1; }
            }

            public extension Foo : internal Bar
            {
                func Bar.Baz(this&) -> int32 { return 2; }
            }
            """);

        Assert.True(structAndExt.HasErrors);
        Assert.Contains(structAndExt.Diagnostics, d => d.Code == "MH0565");

        var twoExts = Compilation.FromSource(SharedIfaceModule + """
            public extension Foo : Bar
            {
                func Bar.Baz(this&) -> int32 { return 1; }
            }

            public extension Foo : internal Bar
            {
                func Bar.Baz(this&) -> int32 { return 2; }
            }
            """);

        Assert.True(twoExts.HasErrors);
        Assert.Contains(twoExts.Diagnostics, d => d.Code == "MH0565");
    }

    [Fact]
    public void MultipleSeparatePrivateExtensions_CoexistWithDistinctSemanticConformances()
    {
        var compilation = Compilation.FromSource(SharedIfaceModule + """
            public func Consume(Bar& b) -> int32 { return b->Baz(); }

            public extension Foo : private Bar
            {
                func Bar.Baz(this&) -> int32 { return 10; }
                private func RunA(this&) -> int32 { return Consume(this); }
                private func CallBazA(this&) -> int32 { return (this as Bar&)->Baz(); }
            }

            public extension Foo : private Bar
            {
                func Bar.Baz(this&) -> int32 { return 20; }
                private func RunB(this&) -> int32 { return Consume(this); }
                private func CallBazB(this&) -> int32 { return (this as Bar&)->Baz(); }
            }
            """,
            "MyMod.mh",
            new CompilationOptions { ModuleName = "MyMod" });

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var fooConfs = compilation.Context.Conformances
            .Where(c => c.TargetType.ToDisplayString(compilation.Context) == "Foo")
            .ToList();
        Assert.Equal(2, fooConfs.Count);
        Assert.NotSame(fooConfs[0], fooConfs[1]);
        Assert.NotEqual(fooConfs[0].OwnerHandle, fooConfs[1].OwnerHandle);
        Assert.All(fooConfs, c => Assert.Equal(Accessibility.Private, c.Accessibility));
        Assert.Equal(2, fooConfs.SelectMany(c => c.MethodMap.Values).Distinct().Count());
    }

    [Fact]
    public void PrivateConformance_StrictlyBodyScoped_ShadowsOuterAndRejectsOutside_MH0566()
    {
        var shadowsOuter = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Bar { func Baz(this&) -> int32; }

            public struct Foo : Bar
            {
                public int32 v;
                func Bar.Baz(this&) -> int32 { return 1; }
            }

            public func TakeBar(Bar& b) -> int32 { return b->Baz(); }

            public extension Foo : private Bar
            {
                func Bar.Baz(this&) -> int32 { return 2; }
                public func InsideShadow(this&) -> int32
                {
                    return TakeBar(this);
                }
            }

            public func OutsidePublic() -> int32
            {
                Foo f = new Foo { v = 0 };
                return TakeBar(&f);
            }
            """,
            "ModShadow.mh",
            new CompilationOptions { ModuleName = "ModShadow" });

        Assert.False(shadowsOuter.HasErrors);
        var fooConfs = shadowsOuter.Context.Conformances
            .Where(c => c.TargetType.ToDisplayString(shadowsOuter.Context) == "Foo")
            .ToList();
        Assert.Equal(2, fooConfs.Count);
        var publicConf = fooConfs.Single(c => c.Accessibility == Accessibility.Public);
        var privateConf = fooConfs.Single(c => c.Accessibility == Accessibility.Private);

        var boundConfs = shadowsOuter.Context.ResolvedTree.Conformances.Values.ToList();
        Assert.Contains(privateConf, boundConfs);
        Assert.Contains(publicConf, boundConfs);

        var outsideInaccessible = Compilation.FromSource(SharedIfaceModule + """
            public func TakeBar(Bar& b) -> int32 { return b->Baz(); }

            public extension Foo : private Bar
            {
                func Bar.Baz(this&) -> int32 { return 2; }
            }

            public func Run() -> int32
            {
                Foo f = new Foo { v = 0 };
                return TakeBar(&f);
            }
            """);

        Assert.True(outsideInaccessible.HasErrors);
        Assert.Contains(outsideInaccessible.Diagnostics, d => d.Code == "MH0566");
    }

    [Fact]
    public void LowerVisibilityConformanceReturn_WithoutDyn_Succeeds_WhileDynOnInterface_ReportsMH0583()
    {
        var validReturn = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Bar { func Baz(this&) -> int32; }

            public struct Foo : Bar
            {
                func Bar.Baz(this&) -> int32 { return 1; }
            }

            public extension Foo : private Bar
            {
                func Bar.Baz(this&) -> int32 { return 2; }
                public func Escape(this&) -> Bar&
                {
                    return this;
                }
            }
            """);

        Assert.False(validReturn.HasErrors, string.Join("\n", validReturn.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var badDynOnInterface = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Bar { func Baz(this&) -> int32; }

            public struct Foo : Bar
            {
                func Bar.Baz(this&) -> int32 { return 1; }
            }

            public extension Foo : private Bar
            {
                func Bar.Baz(this&) -> int32 { return 2; }

                public func EscapeDynRef(this&) -> dyn Bar&
                {
                    return this;
                }
            }
            """);

        Assert.True(badDynOnInterface.HasErrors);
        Assert.Contains(badDynOnInterface.Diagnostics, d => d.Code == "MH0583");
    }

    [Fact]
    public void CrossModuleConformances_InternalIsolated_AndPublicDisambiguatedWithModuleQualifier()
    {
        var baseTypes = Compilation.FromSource(SharedIfaceModule + """
            public func TakeBar(Bar& b) -> int32 { return b->Baz(); }
            public func Pick<T : Bar>(T& item) -> int32 { return 1; }
            """,
            "Core.mh",
            new CompilationOptions { ModuleName = "Core" });
        Assert.False(baseTypes.HasErrors);

        var modInternalA = Compilation.FromSource("""
            public extension Foo : internal Bar
            {
                func Bar.Baz(this&) -> int32 { return 100; }
            }

            public func RunA() -> int32
            {
                Foo f = new Foo { v = 1 };
                return TakeBar(&f);
            }
            """,
            "ModInternalA.mh",
            new CompilationOptions { ModuleName = "ModInternalA" },
            referencedCompilations: [baseTypes]);
        Assert.False(modInternalA.HasErrors);

        var modInternalB = Compilation.FromSource("""
            public extension Foo : internal Bar
            {
                func Bar.Baz(this&) -> int32 { return 200; }
            }

            public func RunB() -> int32
            {
                Foo f = new Foo { v = 2 };
                return TakeBar(&f);
            }
            """,
            "ModInternalB.mh",
            new CompilationOptions { ModuleName = "ModInternalB" },
            referencedCompilations: [baseTypes]);
        Assert.False(modInternalB.HasErrors);

        var modPubA = Compilation.FromSource("""
            public extension Foo : public Bar
            {
                func Bar.Baz(this&) -> int32 { return 10; }
            }
            """,
            "ModPubA.mh",
            new CompilationOptions { ModuleName = "ModPubA" },
            referencedCompilations: [baseTypes]);
        Assert.False(modPubA.HasErrors);

        var modPubB = Compilation.FromSource("""
            public extension Foo : public Bar
            {
                func Bar.Baz(this&) -> int32 { return 20; }
            }
            """,
            "ModPubB.mh",
            new CompilationOptions { ModuleName = "ModPubB" },
            referencedCompilations: [baseTypes]);
        Assert.False(modPubB.HasErrors);

        var modCAmbiguous = Compilation.FromSource("""
            public func Run() -> int32
            {
                Foo f = new Foo { v = 0 };
                return TakeBar(&f);
            }
            """,
            "ModC.mh",
            new CompilationOptions { ModuleName = "ModC" },
            referencedCompilations: [baseTypes, modPubA, modPubB]);

        Assert.True(modCAmbiguous.HasErrors);
        Assert.Contains(modCAmbiguous.Diagnostics, d => d.Code == "MH0567");

        var modCDisambig = Compilation.FromSource("""
            public struct PairHolders
            {
                public Holder<Foo as ModPubA::Bar> first;
                public Holder<Foo as ModPubB::Bar> second;
            }

            public func Run() -> int32
            {
                Foo f = new Foo { v = 0 };
                int32 a = (f as ModPubA::Bar).Baz();
                int32 b = TakeBar(&f as ModPubB::Bar&);
                int32 c = Pick::<Foo as ModPubA::Bar>(&f);
                return a;
            }
            """,
            "ModC.mh",
            new CompilationOptions { ModuleName = "ModC" },
            referencedCompilations: [baseTypes, modPubA, modPubB]);

        Assert.False(modCDisambig.HasErrors, string.Join("\n", modCDisambig.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var pairSym = modCDisambig.Context.TypeSymbols.Single(t => t.Name.Text == "PairHolders");
        var pairScope = pairSym.EnclosingScope.GetChildScope(ResolutionContext.GetHandle(pairSym))!;
        var firstField = pairScope.Symbols.Values.OfType<FieldSymbol>().Single(f => f.Name.Text == "first");
        var secondField = pairScope.Symbols.Values.OfType<FieldSymbol>().Single(f => f.Name.Text == "second");
        Assert.NotEqual(firstField.Type, secondField.Type);

        var modCLocalOverride = Compilation.FromSource("""
            public extension Foo : internal Bar
            {
                func Bar.Baz(this&) -> int32 { return 30; }
            }

            public func Run() -> int32
            {
                Foo f = new Foo { v = 0 };
                return TakeBar(&f);
            }
            """,
            "ModCOverride.mh",
            new CompilationOptions { ModuleName = "ModCOverride" },
            referencedCompilations: [baseTypes, modPubA, modPubB]);

        Assert.False(modCLocalOverride.HasErrors);
    }

    [Fact]
    public void SymbolicModules_DuplicateNamesWithExternUsing_AndGlobalSelfLookup()
    {
        var stdMod = Compilation.FromSource(CompilerTestBed.FullPrelude,
            "Std.mh",
            new CompilationOptions { ModuleName = "Std" });

        var modDupOne = Compilation.FromSource("""
            public func OnlyOne() -> int32 { return 1; }
            public func Shared() -> int32 { return 10; }
            """,
            "DupOne.mh",
            new CompilationOptions { ModuleName = "SameMod" },
            referencedCompilations: [stdMod]);

        var modDupTwo = Compilation.FromSource("""
            public func OnlyTwo() -> int32 { return 2; }
            public func Shared() -> int32 { return 20; }
            """,
            "DupTwo.mh",
            new CompilationOptions { ModuleName = "SameMod" },
            referencedCompilations: [stdMod]);

        var clash = Compilation.FromSource("""
            public func Test() -> int32
            {
                return SameMod::Shared();
            }
            """,
            "Consumer.mh",
            new CompilationOptions { ModuleName = "Consumer" },
            referencedCompilations: [stdMod, modDupOne, modDupTwo]);

        Assert.True(clash.HasErrors);
        Assert.Contains(clash.Diagnostics, d => d.Code == "MH0575");

        var aliased = Compilation.FromSource("""
            extern using LibOne;
            extern using LibTwo;

            public struct SameMod
            {
                public int32 localField;
            }

            public func LocalHelper() -> int32 { return 99; }

            public func Test() -> int32
            {
                SameMod local = new SameMod { localField = 3 };
                int32 LocalHelper = 1;
                int32 a = SameMod::OnlyOne();
                int32 b = SameMod::OnlyTwo();
                int32 s1 = LibOne::Shared();
                int32 s2 = LibTwo::Shared();
                int32 g = global::LocalHelper();
                return LocalHelper;
            }
            """,
            "Consumer.mh",
            new CompilationOptions
            {
                ModuleName = "Consumer",
                ReferencedAliases = [null, "LibOne", "LibTwo"]
            },
            referencedCompilations: [stdMod, modDupOne, modDupTwo]);

        Assert.False(aliased.HasErrors);

        var badExternUsing = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            extern using NonExistent;
            public func Test() -> () {}
            """);

        Assert.True(badExternUsing.HasErrors);
        Assert.Contains(badExternUsing.Diagnostics, d => d.Code == "MH0576");
    }

    [Fact]
    public void MiryoProjectParser_SeparatesModuleNameAndArtifactName()
    {
        string mhpr = """
            ModuleName : "MySymbolicModule";
            ArtifactName : "my-custom-binary";
            """;

        MahoProjectConfiguration config = MahoProjectFileParser.Parse(mhpr);

        Assert.Equal("MySymbolicModule", config.ModuleName);
        Assert.Equal("my-custom-binary", config.ArtifactName);
    }
}
