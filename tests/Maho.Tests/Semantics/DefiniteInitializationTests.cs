using System.Linq;
using Xunit;

namespace Maho.Tests.Semantics;

public sealed class DefiniteInitializationTests
{
    private const string StructPrelude = CompilerTestBed.FullPrelude + """
        public struct Empty
        {
            public func Ping(&this) -> int32 { return 7; }
        }

        public struct Pair
        {
            public int32 a;
            public int32 b;

            public func First(&this) -> int32 { return this.a; }
            public func Second(&this) -> int32 { return this.b; }
        }

        public struct Box<T>
        {
            public T val;
        }
        """;

    [Fact]
    public void UninitializedScalarLocal_Read_ReportsMH1000()
    {
        var compilation = Compilation.FromSource(StructPrelude + """
            public func Test() -> int32
            {
                int32 x;
                return x;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1000");
    }

    [Fact]
    public void UninitializedStruct_MethodCallOrPartialRead_ReportsMH1000()
    {
        var wholeCall = Compilation.FromSource(StructPrelude + """
            public func Test() -> int32
            {
                Pair p;
                return p.First();
            }
            """);

        Assert.True(wholeCall.HasErrors);
        Assert.Contains(wholeCall.Diagnostics, d => d.Code == "MH1000");

        var partialCall = Compilation.FromSource(StructPrelude + """
            public func Test() -> int32
            {
                Pair p;
                p.a = 10;
                return p.First();
            }
            """);

        Assert.True(partialCall.HasErrors);
        Assert.Contains(partialCall.Diagnostics, d => d.Code == "MH1000");

        var unreadField = Compilation.FromSource(StructPrelude + """
            public func Test() -> int32
            {
                Pair p;
                p.a = 10;
                return p.b;
            }
            """);

        Assert.True(unreadField.HasErrors);
        Assert.Contains(unreadField.Diagnostics, d => d.Code == "MH1000");
    }

    [Fact]
    public void ZeroSizedEmptyStruct_AndEmptyTuple_AlwaysInitialized_Succeeds()
    {
        var compilation = Compilation.FromSource(StructPrelude + """
            public func Test() -> int32
            {
                Empty e;
                () u;
                Empty d = default;
                Empty c = put Empty();
                return e.Ping();
            }
            """);

        Assert.True(!compilation.HasErrors, string.Join("\n", compilation.Diagnostics));
    }

    [Fact]
    public void PiecewiseFieldInitialization_PromotesStructWhenAllFieldsAssigned()
    {
        var compilation = Compilation.FromSource(StructPrelude + """
            public func Test() -> int32
            {
                Pair p;
                p.a = 10;
                int32 first = p.a;
                p.b = 20;
                int32 second = p.Second();
                return first;
            }
            """);

        Assert.True(!compilation.HasErrors, string.Join("\n", compilation.Diagnostics));
    }

    [Fact]
    public void DirectStructInitializer_ValidAndMissingOrDuplicateFields_ChecksMH0578()
    {
        var valid = Compilation.FromSource(StructPrelude + """
            public func Test() -> int32
            {
                Pair p = put Pair { a = 3, b = 4 };
                Box<int32> bx = put Box<int32> { val = 5 };
                return p.First();
            }
            """);

        Assert.True(!valid.HasErrors, string.Join("\n", valid.Diagnostics));

        var missing = Compilation.FromSource(StructPrelude + """
            public func Test() -> Pair
            {
                return put Pair { a = 3 };
            }
            """);

        Assert.True(missing.HasErrors);
        Assert.Contains(missing.Diagnostics, d => d.Code == "MH0578");

        var duplicate = Compilation.FromSource(StructPrelude + """
            public func Test() -> Pair
            {
                return put Pair { a = 3, a = 4, b = 5 };
            }
            """);

        Assert.True(duplicate.HasErrors);
        Assert.Contains(duplicate.Diagnostics, d => d.Code == "MH0578");
    }

    [Fact]
    public void PiecewiseOrDirectInit_WithInaccessibleField_ReportsMH0574()
    {
        const string sourceWithSecret = StructPrelude + """
            public struct Secret
            {
                private int32 hidden;
                public int32 shown;

                public func MakeInside(&this) -> Secret
                {
                    Secret s;
                    s.hidden = 1;
                    s.shown = 2;
                    return s;
                }
            }
            """;

        var validInside = Compilation.FromSource(sourceWithSecret + """
            public func Test() -> int32
            {
                Secret s = default;
                Secret inside = s.MakeInside();
                return inside.shown;
            }
            """);

        Assert.True(!validInside.HasErrors, string.Join("\n", validInside.Diagnostics));

        var piecewiseOutside = Compilation.FromSource(sourceWithSecret + """
            public func BadPiecewise() -> int32
            {
                Secret s;
                s.shown = 1;
                return 0;
            }
            """);

        Assert.True(piecewiseOutside.HasErrors);
        Assert.Contains(piecewiseOutside.Diagnostics, d => d.Code == "MH0574");

        var directInitOutside = Compilation.FromSource(sourceWithSecret + """
            public func BadDirect() -> Secret
            {
                return put Secret { hidden = 1, shown = 2 };
            }
            """);

        Assert.True(directInitOutside.HasErrors);
        Assert.Contains(directInitOutside.Diagnostics, d => d.Code == "MH0574");
    }

    [Fact]
    public void ConditionalInitialization_ReportsMH1002_WhileMoveReportsMH1001()
    {
        var uninitBranch = Compilation.FromSource(StructPrelude + """
            public func Test(bool cond) -> int32
            {
                int32 x;
                if (cond)
                {
                    x = 1;
                }
                return x;
            }
            """);

        Assert.True(uninitBranch.HasErrors);
        Assert.Contains(uninitBranch.Diagnostics, d => d.Code == "MH1002");

        var bothBranches = Compilation.FromSource(StructPrelude + """
            public func Test(bool cond) -> int32
            {
                int32 x;
                if (cond)
                {
                    x = 1;
                }
                else
                {
                    x = 2;
                }
                return x;
            }
            """);

        Assert.True(!bothBranches.HasErrors, string.Join("\n", bothBranches.Diagnostics));

        var conditionalMove = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> ();
            }

            public struct Handle : Move<Handle>
            {
                public int32 id;
                public func Move(Handle& dest, Handle& source) -> () {}
            }

            public func Consume(Handle h) -> () {}

            public func Test(bool cond) -> ()
            {
                Handle h = put Handle { id = 1 };
                if (cond)
                {
                    Consume(h);
                }
                Consume(h);
            }
            """);

        Assert.True(conditionalMove.HasErrors);
        Assert.Contains(conditionalMove.Diagnostics, d => d.Code == "MH1001");
    }
}
