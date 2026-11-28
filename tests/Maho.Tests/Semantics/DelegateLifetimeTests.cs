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
    public void BoundInstanceMethodClosure_BorrowsReceiverAccordingToThisMutability()
    {
        var mutMethodConflictsWithRead = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Counter
            {
                public int32 val;
                public func Inc(this mut&) -> int32
                {
                    this->val = 1;
                    return this->val;
                }
            }

            public func Test() -> ()
            {
                Counter c = new Counter { val = 0 };
                var d = c.Inc;
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
                public func Read(this&) -> int32
                {
                    return this->val;
                }

                public func GetUnboundReader() -> func(Counter&) -> int32
                {
                    return Counter.Read;
                }
            }

            public func Test() -> int32
            {
                Counter c = new Counter { val = 7 };
                var d = c.Read;
                &Counter view = c;
                d();
                return view.val;
            }
            """);
        Assert.False(sharedMethodAllowsSharedBorrow.HasErrors, string.Join("\n", sharedMethodAllowsSharedBorrow.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Closure_MoveTransfer_PreventsUseAfterMove_AndPreservesLoan()
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
    public void ClosureBorrow_OutlivingCapturedBlockLocal_ReportsMH1004()
    {
        var blockLocalClosureBorrow = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Test() -> int32
            {
                int32 seed = 0;
                var initClosure = () => seed;
                Callable<int32>& saved = &initClosure;
                {
                    int32 local = 42;
                    var localClosure = () => local;
                    saved = &localClosure;
                }
                return saved();
            }
            """);
        Assert.Contains(blockLocalClosureBorrow.Diagnostics, d => d.Code == "MH1004");

        var tempClosureBorrow = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func TestTemp() -> int32
            {
                int32 local = 42;
                Callable<int32>& saved = &(() => local);
                return saved();
            }
            """);
        Assert.Contains(tempClosureBorrow.Diagnostics, d => d.Code == "MH1008");
        Assert.DoesNotContain(tempClosureBorrow.Diagnostics, d => d.Code == "MH1000");
    }

    [Fact]
    public void ClosureAndCompositeBorrow_EscapeViaReturnAndThrow_ReportMH1004()
    {
        var directReturn = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func EscapeDirect() -> Callable<int32>&
            {
                int32 local = 10;
                return &(() => local);
            }
            """);
        Assert.Contains(directReturn.Diagnostics, d => d.Code == "MH1004");

        var varReturn = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func EscapeVar() -> Callable<int32>&
            {
                int32 local = 10;
                var fn = () => local;
                Callable<int32>& fnRef = &fn;
                return fnRef;
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
                return new BorrowHolder { r = &local };
            }
            """);
        Assert.Contains(compositeReturn.Diagnostics, d => d.Code == "MH1004");

        var throwEscape = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct LocalErr : Throwable
            {
                public Callable<int32>& fn;
            }

            public func EscapeThrow() -> ()
            {
                int32 local = 10;
                throw new LocalErr { fn = &(() => local) };
            }
            """);
        Assert.Contains(throwEscape.Diagnostics, d => d.Code == "MH1004");
    }

    [Fact]
    public void Closure_MoveCapture_ConsumesOriginal_AndCallableOnceMovesClosureOnInvoke()
    {
        var moveCaptureThenReuseOriginal = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func ConsumeBox(Box<int32> b) -> int32 { return *b; }

            public func Test() -> int32
            {
                Box<int32> b = new Box<int32>(10);
                var f = () => ConsumeBox(b);
                int32 first = *b;
                return f();
            }
            """);
        Assert.Contains(moveCaptureThenReuseOriginal.Diagnostics, d => d.Code == "MH1001");

        var invokeCallableOnceTwice = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func ConsumeBox(Box<int32> b) -> int32 { return *b; }

            public func Test() -> int32
            {
                Box<int32> b = new Box<int32>(10);
                var f = () => ConsumeBox(b);
                int32 first = f();
                return f();
            }
            """);
        Assert.Contains(invokeCallableOnceTwice.Diagnostics, d => d.Code == "MH1001");
    }

    [Fact]
    public void MoveCaptureClosure_And_BoxEmplace_EscapeSafely_WithoutMH1004()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func ConsumeBox(Box<int32> b) -> int32 { return *b; }

            public func MakeBoxedCallableOnce<F>(F f) -> Box<CallableOnce<int32>> where F : CallableOnce<int32>
            {
                return new Box<F>(f) as Box<CallableOnce<int32>>;
            }

            public func ReturnBoxWithEmplace(int32 start) -> Box<int32>
            {
                Box<int32> b = new Box<int32>(start);
                return Box::<int32>.Emplace(() => ConsumeBox(b));
            }

            public func ReturnMoveBoxedClosure(int32 start) -> Box<CallableOnce<int32>>
            {
                Box<int32> b = new Box<int32>(start);
                return MakeBoxedCallableOnce(() => ConsumeBox(b));
            }
            """);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void CallableMutClosure_AllowsRepeatedCalls_AndPreventsSharedBorrowConflict()
    {
        var repeatable = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Test() -> int32
            {
                int32 count = 0;
                var inc = () => { count = 1; return count; };
                int32 a = inc();
                return inc();
            }
            """);
        Assert.False(repeatable.HasErrors, string.Join("\n", repeatable.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var mutConflict = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Test() -> int32
            {
                int32 count = 0;
                var inc = () => { count = 1; return count; };
                count = 10;
                return inc();
            }
            """);
        Assert.Contains(mutConflict.Diagnostics, d => d.Code == "MH1003");
    }

    [Fact]
    public void BoundByValueMethodClosure_MovesReceiver_OnCaptureCreation()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Resource
            {
                public Box<int32> inner;
                public func Consume(this) -> int32
                {
                    return *this.inner;
                }
            }

            public func Test() -> int32
            {
                Resource r = new Resource { inner = new Box<int32>(7) };
                var fn = r.Consume;
                int32 again = *r.inner;
                return fn();
            }
            """);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1001");
    }
}
