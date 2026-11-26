using System.Linq;
using Maho.Diagnostics;
using Maho.Resolution;
using Maho.Syntax;
using Xunit;

namespace Maho.Tests;

public class ExplicitInterfaceTests
{
    private const string StandardPrelude = """
        [IntrinsicType("Layout")]
        public interface Layout;
        [IntrinsicType("Sized")]
        public interface Sized : Layout;
        [IntrinsicType("Unsized")]
        public interface Unsized : Layout;

        intrinsic attribute IntrinsicType(&Str name);

        intrinsic attribute OperatorFunction(&Str name);

        [IntrinsicType("UInt8")]
        public struct uint8;

        [IntrinsicType("NUInt")]
        public struct nuint;

        [IntrinsicType("Int8")]
        public struct int8;

        [IntrinsicType("Int32")]
        public struct int32;

        [IntrinsicType("Int64")]
        public struct int64;

        [IntrinsicType("Slice")]
        public struct Slice<T, D : int = 1>;

        [IntrinsicType("Metadata")]
        public struct Metadata<T> where T : Layout;

        [IntrinsicType("Reference")]
        public struct Reference<T> where T : Layout
        {
            nuint raw;
            Metadata<T> metadata;
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            uint8[] data;
        }
        intrinsic attribute IntrinsicFunction(&Str signature);

        [IntrinsicType("Pointer")]
        public struct Pointer<T> where T : Layout
        {
            nuint addr;
            Metadata<T> metadata;
        }

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            public func Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            Pointer<ValType> env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }
        """;

    [Fact]
    public void CustomVoidType_RecognizedAsVoidReturn_ForOperatorFunctions()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")]
            public interface Layout;
            [IntrinsicType("Sized")]
            public interface Sized : Layout;
            [IntrinsicType("Unsized")]
            public interface Unsized : Layout;

            intrinsic attribute IntrinsicType(&Str name);

            intrinsic attribute OperatorFunction(&Str name);

            [IntrinsicType("ValueType")]
            public struct ValType;

            [IntrinsicType("Struct")]
            public struct Struct : ValType;

            [IntrinsicType("NUInt")]
            public struct nuint;

            [IntrinsicType("UInt8")]
            public struct uint8;

            [IntrinsicType("Int8")]
            public struct int8;

            [IntrinsicType("Int64")]
            public struct int64;

            [IntrinsicType("Slice")]
            public struct Slice<T, D : int = 1>;

            [IntrinsicType("Metadata")]
            public struct Metadata<T> where T : Layout;

            [IntrinsicType("Reference")]
            public struct Reference<T> where T : Layout
            {
                nuint raw;
                Metadata<T> metadata;
            }

            [IntrinsicType("Str")]
            public struct Str
            {
                uint8[] data;
            }

            // Custom void type name: Unit

            public interface Ctor<T>
            {
                [OperatorFunction("Constructor")]
                public func Construct(&this) -> ();
            }

            public interface Dtor<T>
            {
                [OperatorFunction("Destructor")]
                public func Destruct(&this) -> ();
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
            [IntrinsicType("Layout")]
            public interface Layout;
            [IntrinsicType("Sized")]
            public interface Sized : Layout;
            [IntrinsicType("Unsized")]
            public interface Unsized : Layout;

            intrinsic attribute IntrinsicType(&Str name);

            intrinsic attribute OperatorFunction(&Str name);

            [IntrinsicType("ValueType")]
            public struct ValType;

            [IntrinsicType("Struct")]
            public struct Struct : ValType;


            [IntrinsicType("NUInt")]
            public struct nuint;

            [IntrinsicType("UInt8")]
            public struct uint8;

            [IntrinsicType("Int8")]
            public struct int8;

            [IntrinsicType("Int64")]
            public struct int64;

            [IntrinsicType("Slice")]
            public struct Slice<T, D : int = 1>;

            [IntrinsicType("Metadata")]
            public struct Metadata<T> where T : Layout;

            // Custom reference type name: MyRef<T>
            [IntrinsicType("Reference")]
            public struct MyRef<T> where T : Layout
            {
                nuint raw;
                Metadata<T> metadata;
            }

            [IntrinsicType("Str")]
            public struct Str
            {
                uint8[] data;
            }

            public interface Deref<TTarget>
            {
                [OperatorFunction("Dereference")]
                public func Deref(&this) -> MyRef<TTarget>;
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
                public func Add(int32 left, int32 right) -> int32;
            }

            public struct Int32 : Add<Int32>
            {
                public func Add.Add(int32 left, int32 right) -> int32 { return left; }
            }

            public struct Host
            {
                public func Test() -> ()
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
                public func Add(int32 left, int32 right) -> int32;
            }

            public struct Int32 : Add
            {
                public func Add.Add(int32 left, int32 right) -> int32 { return left; }
            }

            public struct Host
            {
                public func Test() -> ()
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
                public func Add(int32 left, int32 right) -> int32;
            }

            public struct Int32 : Add
            {
                public func Add.Add(int32 left, int32 right) -> int32 { return left; }
            }

            public struct Host
            {
                public func Test() -> ()
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
                public func Add(int32 left, int32 right) -> int32;
            }

            public struct Int32 : Add
            {
                // Explicit interface implementation (priority 1)
                public func Add.Add(int32 left, int32 right) -> int32 { return left; }

                // Ordinary method with same name (priority 2)
                public func Add(int32 left, int32 right) -> int32 { return right; }
            }

            public struct Host
            {
                public func Test() -> ()
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
                public func Add(int32 left, int32 right) -> int32;
            }

            public struct Int32 : Add
            {
                // Ordinary method only (no explicit Add.Add)
                public func Add(int32 left, int32 right) -> int32 { return left; }
            }

            public struct Host
            {
                public func Test() -> ()
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
                public func RotL(int32 val, int32 count) -> int32;

                [OperatorFunction("RotateRight")]
                public func RotR(int32 val, int32 count) -> int32;
            }

            public interface Mut<T>
            {
                [OperatorFunction("PrefixIncrement")]
                public func Inc(&this) -> int32;

                [OperatorFunction("AdditionAssignment")]
                public func AddAssign(&this, int32 rhs) -> ();
            }

            public interface Idx<T, TIdx>
            {
                [OperatorFunction("IndexerGetter")]
                public func Get(&this, int32 index) -> int32;

                [OperatorFunction("IndexerSetter")]
                public func Set(&this, int32 val, int32 index) -> ();
            }

            public interface Cast<TFrom, TTo>
            {
                [OperatorFunction("ImplicitCast")]
                public func ToInt64(int32 val) -> int64;
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
                    public func Add(int32 a, int32 b) -> int32;
                }
            }

            public struct Int32 : Ops.Add
            {
                public func Ops.Add.Add(int32 a, int32 b) -> int32 { return a; }
            }

            public struct Host
            {
                public func Test() -> ()
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
