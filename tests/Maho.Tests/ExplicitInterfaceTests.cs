using System.Linq;
using Maho.Diagnostics;
using Maho.Resolution;
using Maho.Syntax;
using Xunit;

namespace Maho.Tests;

public class ExplicitInterfaceTests
{
    private const string StandardPrelude = """
        [intrinsic]
        public attribute IntrinsicType(Str name);

        [intrinsic]
        public attribute OperatorFunction(Str name);

        [IntrinsicType("Void")]
        public struct void;

        [IntrinsicType("NUInt")]
        public struct nuint;

        [IntrinsicType("Int8")]
        public struct int8;

        [IntrinsicType("Int32")]
        public struct int32;

        [IntrinsicType("Int64")]
        public struct int64;

        [IntrinsicType("Reference")]
        public struct Reference<T>
        {
            nuint raw;
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            int8& buffer;
            int64 length;
        }
        """;

    [Fact]
    public void CustomVoidType_RecognizedAsVoidReturn_ForOperatorFunctions()
    {
        var compilation = Compilation.FromSource("""
            [intrinsic]
            public attribute IntrinsicType(Str name);

            [intrinsic]
            public attribute OperatorFunction(Str name);

            [IntrinsicType("NUInt")]
            public struct nuint;

            [IntrinsicType("Int8")]
            public struct int8;

            [IntrinsicType("Int64")]
            public struct int64;

            [IntrinsicType("Reference")]
            public struct Reference<T>
            {
                nuint raw;
            }

            [IntrinsicType("Str")]
            public struct Str
            {
                int8& buffer;
                int64 length;
            }

            // Custom void type name: Unit
            [IntrinsicType("Void")]
            public struct Unit;

            public interface Ctor<T>
            {
                [OperatorFunction("Constructor")]
                public Unit Construct();
            }

            public interface Dtor<T>
            {
                [OperatorFunction("Destructor")]
                public Unit Destruct();
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context;
        Assert.True(context.DiscoveredOperators.TryGetOperators(OperatorKind.Constructor, out var ctorOps));
        Assert.Single(ctorOps);
        Assert.True(context.DiscoveredOperators.TryGetOperators(OperatorKind.Destructor, out var dtorOps));
        Assert.Single(dtorOps);
    }

    [Fact]
    public void CustomReferenceType_RecognizedAsReferenceReturn_ForDereference()
    {
        var compilation = Compilation.FromSource("""
            [intrinsic]
            public attribute IntrinsicType(Str name);

            [intrinsic]
            public attribute OperatorFunction(Str name);

            [IntrinsicType("Void")]
            public struct void;

            [IntrinsicType("NUInt")]
            public struct nuint;

            [IntrinsicType("Int8")]
            public struct int8;

            [IntrinsicType("Int64")]
            public struct int64;

            // Custom reference type name: MyRef<T>
            [IntrinsicType("Reference")]
            public struct MyRef<T>
            {
                nuint raw;
            }

            [IntrinsicType("Str")]
            public struct Str
            {
                MyRef<int8> buffer;
                int64 length;
            }

            public interface Deref<TTarget>
            {
                [OperatorFunction("Dereference")]
                public MyRef<TTarget> Deref();
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context;
        Assert.True(context.DiscoveredOperators.TryGetOperators(OperatorKind.Dereference, out var derefOps));
        Assert.Single(derefOps);
    }

    [Fact]
    public void TypeAsExpression_ParsesCorrectly()
    {
        var compilation = Compilation.FromSource($$"""
            {{StandardPrelude}}

            public interface Add<T>
            {
                [OperatorFunction("Addition")]
                public static int32 Add(int32 left, int32 right);
            }

            public struct Int32 : Add<Int32>
            {
                public static int32 Add.Add(int32 left, int32 right) { return left; }
            }

            public class Host
            {
                public void Test()
                {
                    <Int32 as Add<Int32>>.Add(1, 2);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var testMethod = body.Members.OfType<MemberFunctionDeclaration>().First();
        var funcBody = Assert.IsType<FunctionBlockBody>(testMethod.Function.Body);
        var exprStmt = Assert.Single(funcBody.Locals.OfType<LocalExpressionStatement>());
        var call = Assert.IsType<CallExpression>(exprStmt.Expression);
        var memberAccess = Assert.IsType<MemberAccessExpression>(call.Callee);
        Assert.Equal("Add", memberAccess.Identifier.Value);
        var typeAs = Assert.IsType<TypeAsExpression>(memberAccess.Expression);
        var targetSimple = Assert.IsType<SimpleType>(typeAs.TargetType);
        Assert.Equal("Int32", targetSimple.Name.Value);
    }

    [Fact]
    public void ExplicitInterfaceMethod_InvisibleDirectlyOnType()
    {
        var compilation = Compilation.FromSource($$"""
            {{StandardPrelude}}

            public interface Add
            {
                [OperatorFunction("Addition")]
                public static int32 Add(int32 left, int32 right);
            }

            public struct Int32 : Add
            {
                public static int32 Add.Add(int32 left, int32 right) { return left; }
            }

            public class Host
            {
                public void Test()
                {
                    // Int32.Add should NOT resolve because Add.Add is interface-scoped!
                    Int32.Add(1, 2);
                }
            }
            """);

        var context = compilation.Context;
        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var testMethod = body.Members.OfType<MemberFunctionDeclaration>().First();
        var funcBody = Assert.IsType<FunctionBlockBody>(testMethod.Function.Body);
        var exprStmt = Assert.Single(funcBody.Locals.OfType<LocalExpressionStatement>());
        var call = Assert.IsType<CallExpression>(exprStmt.Expression);
        var memberAccess = Assert.IsType<MemberAccessExpression>(call.Callee);

        // Member access to Int32.Add must not be bound to the interface-scoped method
        Assert.False(context.ResolvedTree.TryGetReference(memberAccess, out _));
    }

    [Fact]
    public void ExplicitInterfaceMethod_ResolvesViaTypeAs()
    {
        var compilation = Compilation.FromSource($$"""
            {{StandardPrelude}}

            public interface Add
            {
                [OperatorFunction("Addition")]
                public static int32 Add(int32 left, int32 right);
            }

            public struct Int32 : Add
            {
                public static int32 Add.Add(int32 left, int32 right) { return left; }
            }

            public class Host
            {
                public void Test()
                {
                    <Int32 as Add>.Add(1, 2);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context;
        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var testMethod = body.Members.OfType<MemberFunctionDeclaration>().First();
        var funcBody = Assert.IsType<FunctionBlockBody>(testMethod.Function.Body);
        var exprStmt = Assert.Single(funcBody.Locals.OfType<LocalExpressionStatement>());
        var call = Assert.IsType<CallExpression>(exprStmt.Expression);
        var memberAccess = Assert.IsType<MemberAccessExpression>(call.Callee);

        Assert.True(context.ResolvedTree.TryGetReference(memberAccess, out var boundHandle));
        var boundMethod = Assert.IsAssignableFrom<MethodSymbol>(context.GetSymbol(boundHandle));
        Assert.True(boundMethod.Flags.HasFlag(FunctionFlags.InterfaceScoped));
        Assert.Equal("Add", boundMethod.ExplicitInterfaceName?.Last.Text);
    }

    [Fact]
    public void ExplicitInterfaceMethod_TakesPriorityOverOrdinaryMethod_InTypeAs()
    {
        var compilation = Compilation.FromSource($$"""
            {{StandardPrelude}}

            public interface Add
            {
                [OperatorFunction("Addition")]
                public static int32 Add(int32 left, int32 right);
            }

            public struct Int32 : Add
            {
                // Explicit interface implementation (priority 1)
                public static int32 Add.Add(int32 left, int32 right) { return left; }

                // Ordinary method with same name (priority 2)
                public static int32 Add(int32 left, int32 right) { return right; }
            }

            public class Host
            {
                public void Test()
                {
                    <Int32 as Add>.Add(1, 2);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context;
        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var testMethod = body.Members.OfType<MemberFunctionDeclaration>().First();
        var funcBody = Assert.IsType<FunctionBlockBody>(testMethod.Function.Body);
        var exprStmt = Assert.Single(funcBody.Locals.OfType<LocalExpressionStatement>());
        var call = Assert.IsType<CallExpression>(exprStmt.Expression);
        var memberAccess = Assert.IsType<MemberAccessExpression>(call.Callee);

        Assert.True(context.ResolvedTree.TryGetReference(memberAccess, out var boundHandle));
        var boundMethod = Assert.IsAssignableFrom<MethodSymbol>(context.GetSymbol(boundHandle));
        // Priority 1 must select the explicit interface implementation!
        Assert.True(boundMethod.Flags.HasFlag(FunctionFlags.InterfaceScoped));
    }

    [Fact]
    public void OrdinaryMethod_FallbackWhenNoExplicitMethod_InTypeAs()
    {
        var compilation = Compilation.FromSource($$"""
            {{StandardPrelude}}

            public interface Add
            {
                [OperatorFunction("Addition")]
                public static int32 Add(int32 left, int32 right);
            }

            public struct Int32 : Add
            {
                // Ordinary method only (no explicit Add.Add)
                public static int32 Add(int32 left, int32 right) { return left; }
            }

            public class Host
            {
                public void Test()
                {
                    <Int32 as Add>.Add(1, 2);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context;
        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var testMethod = body.Members.OfType<MemberFunctionDeclaration>().First();
        var funcBody = Assert.IsType<FunctionBlockBody>(testMethod.Function.Body);
        var exprStmt = Assert.Single(funcBody.Locals.OfType<LocalExpressionStatement>());
        var call = Assert.IsType<CallExpression>(exprStmt.Expression);
        var memberAccess = Assert.IsType<MemberAccessExpression>(call.Callee);

        Assert.True(context.ResolvedTree.TryGetReference(memberAccess, out var boundHandle));
        var boundMethod = Assert.IsAssignableFrom<MethodSymbol>(context.GetSymbol(boundHandle));
        // Priority 2 falls back to ordinary method!
        Assert.False(boundMethod.Flags.HasFlag(FunctionFlags.InterfaceScoped));
    }

    [Fact]
    public void OperatorRegistry_RegistersDiverseOperators_WithCorrectKinds()
    {
        var compilation = Compilation.FromSource($$"""
            {{StandardPrelude}}

            public interface Rot<T>
            {
                [OperatorFunction("RotateLeft")]
                public static int32 RotL(int32 val, int32 count);

                [OperatorFunction("RotateRight")]
                public static int32 RotR(int32 val, int32 count);
            }

            public interface Mut<T>
            {
                [OperatorFunction("PrefixIncrement")]
                public int32 Inc();

                [OperatorFunction("AdditionAssignment")]
                public void AddAssign(int32 rhs);
            }

            public interface Idx<T, TIdx>
            {
                [OperatorFunction("IndexerGetter")]
                public int32 Get(int32 index);

                [OperatorFunction("IndexerSetter")]
                public void Set(int32 val, int32 index);
            }

            public interface Cast<TFrom, TTo>
            {
                [OperatorFunction("ImplicitCast")]
                public static int64 ToInt64(int32 val);
            }
            """);

        Assert.False(compilation.HasErrors);
        var ops = compilation.Context.DiscoveredOperators;

        Assert.True(ops.TryGetOperators(OperatorKind.RotateLeft, out var rotLOps));
        Assert.Single(rotLOps);
        Assert.Equal("RotateLeft", rotLOps[0].FullName);

        Assert.True(ops.TryGetOperators(OperatorKind.RotateRight, out var rotROps));
        Assert.Single(rotROps);

        Assert.True(ops.TryGetOperators(OperatorKind.PrefixIncrement, out var incOps));
        Assert.Single(incOps);

        Assert.True(ops.TryGetOperators(OperatorKind.AdditionAssignment, out var addAssignOps));
        Assert.Single(addAssignOps);

        Assert.True(ops.TryGetOperators(OperatorKind.IndexerGetter, out var getOps));
        Assert.Single(getOps);

        Assert.True(ops.TryGetOperators(OperatorKind.IndexerSetter, out var setOps));
        Assert.Single(setOps);

        Assert.True(ops.TryGetOperators(OperatorKind.ImplicitCast, out var castOps));
        Assert.Single(castOps);
    }

    [Fact]
    public void QualifiedInterface_InTypeAs_ResolvesCorrectly()
    {
        var compilation = Compilation.FromSource($$"""
            {{StandardPrelude}}

            namespace Ops
            {
                public interface Add
                {
                    [OperatorFunction("Addition")]
                    public static int32 Add(int32 a, int32 b);
                }
            }

            public struct Int32 : Ops.Add
            {
                public static int32 Ops.Add.Add(int32 a, int32 b) { return a; }
            }

            public class Host
            {
                public void Test()
                {
                    <Int32 as Ops.Add>.Add(1, 2);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context;
        var host = compilation.SyntaxTrees[0].Root.Members.OfType<TopLevelTypeDeclaration>()
            .First(t => t.Type.Name.ToString() == "Host");
        var body = Assert.IsType<TypeBlockBody>(host.Type.Body);
        var testMethod = body.Members.OfType<MemberFunctionDeclaration>().First();
        var funcBody = Assert.IsType<FunctionBlockBody>(testMethod.Function.Body);
        var exprStmt = Assert.Single(funcBody.Locals.OfType<LocalExpressionStatement>());
        var call = Assert.IsType<CallExpression>(exprStmt.Expression);
        var memberAccess = Assert.IsType<MemberAccessExpression>(call.Callee);

        Assert.True(context.ResolvedTree.TryGetReference(memberAccess, out var boundHandle));
        var boundMethod = Assert.IsAssignableFrom<MethodSymbol>(context.GetSymbol(boundHandle));
        Assert.True(boundMethod.Flags.HasFlag(FunctionFlags.InterfaceScoped));
        Assert.Equal("Add", boundMethod.Name.Text);
        Assert.Equal("Add", boundMethod.ExplicitInterfaceName?.Last.Text);
    }
}
