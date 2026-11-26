namespace Maho.Tests;

public sealed class ExceptionsStaticConstructorsAndEntryPointTests
{
    private const string Prelude = CompilerTestBed.FullPrelude;

    [Fact]
    public void Throw_ValidThrowableType_CompilesSuccessfully()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct CustomError : Throwable;

            public func Fail() -> ()
            {
                CustomError err;
                throw err;
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void Throw_NonThrowableType_ReportsMH2030()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct NotAnError;

            public func Fail() -> ()
            {
                NotAnError val;
                throw val;
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2030");
    }

    [Fact]
    public void Throw_BareThrowOutsideCatch_ReportsMH2031()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Fail() -> ()
            {
                throw;
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2031");
    }

    [Fact]
    public void Throw_ReferenceToThrowable_ReportsMH2032()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct CustomError : Throwable;

            public func Fail() -> ()
            {
                CustomError err;
                var ref = &err;
                throw ref;
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2032");
    }

    [Fact]
    public void Throw_UnconditionalThrow_SatisfiesNonVoidReturn()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct CustomError : Throwable;

            public func Compute() -> int32
            {
                CustomError err;
                    throw err;
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void StaticConstructor_ValidImplementation_DiscoveredInContext()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct MyType : StaticConstructor
            {
                public func Init() -> ()
                {
                }
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.False(compilation.HasErrors);
        Assert.Single(compilation.Context.StaticConstructors);
    }

    [Fact]
    public void StaticConstructor_WithParameters_ReportsMH0560()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute OperatorFunction(Str name);

            public interface BadStaticConstructor
            {
                [OperatorFunction("StaticConstructor")]
                public func Init(int32 x) -> ();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560");
    }

    [Fact]
    public void StaticConstructor_WithReceiver_ReportsMH0560()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute OperatorFunction(Str name);

            public interface BadStaticConstructor
            {
                [OperatorFunction("StaticConstructor")]
                public func Init(&this) -> ();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560");
    }

    [Fact]
    public void StaticConstructor_WithStaticModifier_ReportsMH0128()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct MyType : StaticConstructor
            {
                public static func Init() -> ()
                {
                }
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0128" && d.Message.Contains("static"));
    }

    [Fact]
    public void Main_ValidSignatures_CompilesAndSetsEntryPoint()
    {
        // 1. Parameterless void
        var c1 = Compilation.FromSource(Prelude + """
            public func Main() -> ()
            {
            }
            """);
        Assert.False(c1.HasErrors);
        Assert.NotNull(c1.Context.EntryPoint);

        // 2. Parameterless int32
        var c2 = Compilation.FromSource(Prelude + """
            public func Main() -> int32
            {
                return 0;
            }
            """);
        Assert.False(c2.HasErrors);
        Assert.NotNull(c2.Context.EntryPoint);

        // 3. Slice<&Str> -> int32
        var c3 = Compilation.FromSource(Prelude + """
            public func Main(&Slice<&Str> args) -> int32
            {
                return 0;
            }
            """);
        Assert.False(c3.HasErrors);
        Assert.NotNull(c3.Context.EntryPoint);

        // 4. (&Str)[] -> int32
        var c4 = Compilation.FromSource(Prelude + """
            public func Main(&(&Str)[] args) -> int32
            {
                return 0;
            }
            """);
        Assert.False(c4.HasErrors);
        Assert.NotNull(c4.Context.EntryPoint);
    }

    [Fact]
    public void Main_InsideStruct_ReportsMH0162()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct Program
            {
                public func Main() -> int32
                {
                    return 0;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0162");
    }

    [Fact]
    public void Main_InvalidSignature_ReportsMH0163()
    {
        // Invalid return type
        var c1 = Compilation.FromSource(Prelude + """
            public func Main() -> bool
            {
                return true;
            }
            """);
        Assert.True(c1.HasErrors);
        Assert.Contains(c1.Diagnostics, d => d.Code == "MH0163");

        // Invalid parameter type
        var c2 = Compilation.FromSource(Prelude + """
            public func Main(int32 code) -> int32
            {
                return code;
            }
            """);
        Assert.True(c2.HasErrors);
        Assert.Contains(c2.Diagnostics, d => d.Code == "MH0163");
    }

    [Fact]
    public void Main_MultipleFiles_WithoutEntryFile_ReportsMH0164()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "maho_multi_main_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var preludeFile = Path.Combine(tempDir, "Prelude.mh");
            var file1 = Path.Combine(tempDir, "Prog1.mh");
            var file2 = Path.Combine(tempDir, "Prog2.mh");

            File.WriteAllText(preludeFile, Prelude);
            File.WriteAllText(file1, "public func Main() -> int32 { return 1; }");
            File.WriteAllText(file2, "public func Main() -> int32 { return 2; }");

            var compilation = MahoCompiler.AnalyzeFiles([preludeFile, file1, file2], "MultiMainTest");

            Assert.True(compilation.HasErrors);
            Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0164");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Main_MultipleFiles_WithEntryFile_DisambiguatesEntryPoint()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "maho_entryfile_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var preludeFile = Path.Combine(tempDir, "Prelude.mh");
            var file1 = Path.Combine(tempDir, "Prog1.mh");
            var file2 = Path.Combine(tempDir, "Prog2.mh");

            File.WriteAllText(preludeFile, Prelude);
            File.WriteAllText(file1, "public func Main() -> int32 { return 1; }");
            File.WriteAllText(file2, "public func Main() -> int32 { return 2; }");

            var options = new CompilationOptions { EntryFile = "Prog2.mh" };
            var compilation = MahoCompiler.AnalyzeFiles([preludeFile, file1, file2], "EntryFileTest", options);

            Assert.False(compilation.HasErrors);
            Assert.NotNull(compilation.Context.EntryPoint);

            var entryFn = compilation.Context.GetFunctionSymbol(compilation.Context.EntryPoint.Value);
            Assert.NotNull(entryFn);
            Assert.Contains("Prog2.mh", entryFn.Syntax?.GetSource()?.FilePath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryCatch_ConcreteStruct_CompilesSuccessfully()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct CustomError : Throwable;

            public func Test() -> ()
            {
                try
                {
                    CustomError err;
                    throw err;
                }
                catch (CustomError e)
                {
                    var x = e;
                }
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void TryCatch_DynReference_CompilesSuccessfully()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct CustomError : Throwable;

            public func Test() -> ()
            {
                try
                {
                    CustomError err;
                    throw err;
                }
                catch (dyn &Throwable e)
                {
                }
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void TryCatch_BaseReference_CompilesSuccessfully()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct CustomError : Throwable;

            public func Test() -> ()
            {
                try
                {
                    CustomError err;
                    throw err;
                }
                catch (&CustomError e)
                {
                }
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void TryCatch_CatchAll_CompilesSuccessfully()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct CustomError : Throwable;

            public func Test() -> ()
            {
                try
                {
                    CustomError err;
                    throw err;
                }
                catch
                {
                }

                try
                {
                    CustomError err;
                    throw err;
                }
                catch (_)
                {
                }
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void TryCatch_NonThrowableType_ReportsMH2030()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> ()
            {
                try
                {
                }
                catch (int32 e)
                {
                }
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2030");
    }

    [Fact]
    public void TryFinally_CompilesSuccessfully()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> ()
            {
                try
                {
                    var x = 1;
                }
                finally
                {
                    var y = 2;
                }
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void TryCatchFinally_BareThrowInsideCatch_CompilesSuccessfully()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct CustomError : Throwable;

            public func Test() -> ()
            {
                try
                {
                    CustomError err;
                    throw err;
                }
                catch (CustomError e)
                {
                    throw;
                }
                finally
                {
                    var clean = 1;
                }
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void TryFinally_ReturnInsideFinally_ReportsMH2033()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> int32
            {
                try
                {
                    return 1;
                }
                finally
                {
                    return 2;
                }
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2033");
    }

    [Fact]
    public void TryFinally_BareThrowInsideFinally_ReportsMH2031()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct CustomError : Throwable;

            public func Test() -> ()
            {
                try
                {
                    CustomError err;
                    throw err;
                }
                catch (CustomError e)
                {
                }
                finally
                {
                    throw;
                }
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2031");
    }

    [Fact]
    public void Try_WithoutCatchOrFinally_ReportsSyntaxError()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> ()
            {
                try
                {
                    var x = 1;
                }
            }

            public func Main() -> int32
            {
                return 0;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0120" && d.Message.Contains("catch") && d.Message.Contains("finally"));
    }
}
