namespace Maho.Tests;

public sealed class DelegateLifetimeTests
{
    [Fact]
    public void Lambda_MultiCapture_SharedAndMutableConflicts_ReportedAndClearedAfterLastUse()
    {
        var sharedConflict = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Test() -> ()
            {
                int32 x = 1;
                int32 y = 2;
                var f = () => { int32 a = x; return y; };
                x = 10;
                f();
            }
            """);
        Assert.Contains(sharedConflict.Diagnostics, d => d.Code == "MH1003");

        var mutableConflict = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Test() -> ()
            {
                int32 x = 1;
                var f = () => { x = 2; return x; };
                &int32 reader = x;
                f();
                reader;
            }
            """);
        Assert.Contains(mutableConflict.Diagnostics, d => d.Code == "MH1003");

        var sequentialAfterLastUse = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Test() -> ()
            {
                int32 x = 1;
                int32 y = 2;
                var f = () => { int32 a = x; return y; };
                f();
                x = 10;
                y = 20;
            }
            """);
        Assert.False(sequentialAfterLastUse.HasErrors, string.Join("\n", sequentialAfterLastUse.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void BoundInstanceMethodDelegate_BorrowsReceiverAccordingToThisMutability()
    {
        var mutMethodConflictsWithRead = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Counter
            {
                public int32 val;
                public func Inc(mut &this) -> int32
                {
                    this.val = 1;
                    return this.val;
                }
            }

            public func Test() -> ()
            {
                Counter c = put Counter { val = 0 };
                delegate() -> int32 d = c.Inc;
                &Counter view = c;
                d();
                view;
            }
            """);
        Assert.Contains(mutMethodConflictsWithRead.Diagnostics, d => d.Code == "MH1003");

        var sharedMethodAllowsSharedBorrow = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Counter
            {
                public int32 val;
                public func Read(&this) -> int32
                {
                    return this.val;
                }

                public func GetDelegate(&this) -> delegate() -> int32
                {
                    return Read;
                }
            }

            public func Test() -> int32
            {
                Counter c = put Counter { val = 7 };
                delegate() -> int32 d = c.Read;
                &Counter view = c;
                d();
                return view.val;
            }
            """);
        Assert.False(sharedMethodAllowsSharedBorrow.HasErrors, string.Join("\n", sharedMethodAllowsSharedBorrow.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Delegate_MoveTransfer_PreventsUseAfterMove_AndPreservesLoan()
    {
        var useAfterMove = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Test() -> ()
            {
                int32 x = 5;
                var f = () => x;
                var g = f;
                f();
                g();
            }
            """);
        Assert.Contains(useAfterMove.Diagnostics, d => d.Code == "MH1001");

        var loanFollowsMove = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Test() -> ()
            {
                int32 x = 5;
                var f = () => x;
                var g = f;
                x = 99;
                g();
            }
            """);
        Assert.Contains(loanFollowsMove.Diagnostics, d => d.Code == "MH1003");
    }

    [Fact]
    public void Delegate_OutlivingCapturedBlockLocal_ReportsMH1004()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Test() -> int32
            {
                delegate() -> int32 saved = () => 0;
                {
                    int32 local = 42;
                    saved = () => local;
                }
                return saved();
            }
            """);

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1004");
    }

    [Fact]
    public void DelegateAndCompositeBorrow_EscapeViaReturnAndThrow_ReportMH1004()
    {
        var directReturn = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func EscapeDirect() -> delegate() -> int32
            {
                int32 local = 10;
                return () => local;
            }
            """);
        Assert.Contains(directReturn.Diagnostics, d => d.Code == "MH1004");

        var varReturn = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func EscapeVar() -> delegate() -> int32
            {
                int32 local = 10;
                var fn = () => local;
                return fn;
            }
            """);
        Assert.Contains(varReturn.Diagnostics, d => d.Code == "MH1004");

        var compositeReturn = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct BorrowHolder
            {
                public int32& r;
            }

            public func EscapeStruct() -> BorrowHolder
            {
                int32 local = 10;
                return put BorrowHolder { r = &local };
            }
            """);
        Assert.Contains(compositeReturn.Diagnostics, d => d.Code == "MH1004");

        var throwEscape = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct LocalErr : Throwable
            {
                public delegate() -> int32 fn;
            }

            public func EscapeThrow() -> ()
            {
                int32 local = 10;
                throw put LocalErr { fn = () => local };
            }
            """);
        Assert.Contains(throwEscape.Diagnostics, d => d.Code == "MH1004");
    }
}
