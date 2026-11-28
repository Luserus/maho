using System.Collections;
using System.Reflection;
using Maho.Diagnostics;
using Maho.Syntax;

namespace Maho.Tests;

public sealed partial class ParserTests
{
    [Fact]
    public void Parse_CollectionExpressionModifier_WithConstructorArguments()
    {
        LocalReturnStatement local = Assert.IsType<LocalReturnStatement>(ParseSingleLocal("""
            return [val1, val2, val3] with(capacity: 10);
            """, typeof(LocalReturnStatement)));

        CollectionExpression collection = Assert.IsType<CollectionExpression>(local.Statement.Expression);
        CollectionConstructorModifier modifier = Assert.IsType<CollectionConstructorModifier>(Assert.Single(collection.Modifiers));
        NamedArgumentExpression argument = Assert.IsType<NamedArgumentExpression>(Assert.Single(modifier.Arguments));

        Assert.Equal("capacity", argument.Name.Value);
        Assert.IsType<LiteralExpression>(argument.Value);
    }

    [Fact]
    public void Parse_NamedArgumentExpression_InCallableArgumentList()
    {
        LocalExpressionStatement local = Assert.IsType<LocalExpressionStatement>(ParseSingleLocal("""
            call(capacity: 10);
            """, typeof(LocalExpressionStatement)));

        CallExpression call = Assert.IsType<CallExpression>(local.Expression);
        NamedArgumentExpression argument = Assert.IsType<NamedArgumentExpression>(Assert.Single(call.Arguments));

        Assert.Equal("capacity", argument.Name.Value);
        Assert.IsType<LiteralExpression>(argument.Value);
    }

    [Theory]
    [InlineData("return (A) - B;", TokenKind.Minus)]
    [InlineData("return (A) * B;", TokenKind.Asterisk)]
    public void Parse_ParenthesizedExpressionFollowedByInfixOperator_IsUnambiguousBinary(string source, TokenKind expectedOperator)
    {
        LocalReturnStatement local = Assert.IsType<LocalReturnStatement>(ParseSingleLocal(source, typeof(LocalReturnStatement)));

        BinaryExpression binary = Assert.IsType<BinaryExpression>(local.Statement.Expression);
        Assert.Equal(expectedOperator, binary.OperatorToken.Kind);
        Assert.IsType<ParenthesizedExpression>(binary.LeftExpression);
    }

    [Fact]
    public void Parse_AsExpression_IsUnambiguousAsExpression()
    {
        LocalReturnStatement local = Assert.IsType<LocalReturnStatement>(ParseSingleLocal("""
            return B as A;
            """, typeof(LocalReturnStatement)));

        AsExpression asExpr = Assert.IsType<AsExpression>(local.Statement.Expression);
        Assert.IsType<IdentifierNameExpression>(asExpr.Expression);
        Assert.Equal("A", Assert.IsType<SimpleType>(asExpr.Type).Name.Value);
    }

    [Fact]
    public void Parse_ParenthesizedExpressionFollowedByNonPrefixInfixOperator_IsUnambiguousBinary()
    {
        LocalReturnStatement local = Assert.IsType<LocalReturnStatement>(ParseSingleLocal("""
            return (A) / B;
            """, typeof(LocalReturnStatement)));

        BinaryExpression binary = Assert.IsType<BinaryExpression>(local.Statement.Expression);
        Assert.Equal(TokenKind.ForwardSlash, binary.OperatorToken.Kind);
        Assert.IsType<ParenthesizedExpression>(binary.LeftExpression);
    }

    [Fact]
    public void Parse_ParenthesizedExpressionFollowedByMemberAccess_IsUnambiguousMemberAccess()
    {
        LocalReturnStatement local = Assert.IsType<LocalReturnStatement>(ParseSingleLocal("""
            return (A).B;
            """, typeof(LocalReturnStatement)));

        MemberAccessExpression memberAccess = Assert.IsType<MemberAccessExpression>(local.Statement.Expression);
        Assert.IsType<ParenthesizedExpression>(memberAccess.Expression);
        Assert.Equal("B", memberAccess.Identifier.Value);
    }

    [Fact]
    public void Parse_TypeDeclaration_WithBaseClauseAndConstraints()
    {
        TypeDeclaration type = ParseSingleTopLevelType("""
            public struct Box<T> : Base<T>, Outer.Inner
                where T: FirstConstraint, Second.Constraint;
            """);

        GenericName name = Assert.IsType<GenericName>(type.Name);
        Assert.Equal("Box", name.Name.Value);
        Assert.Single(name.GenericParameters);
        Assert.Equal("T", name.GenericParameters[0].Identifier.Value);

        TypeBaseClause baseClause = Assert.IsType<TypeBaseClause>(type.Base);
        Assert.Equal(2, baseClause.BaseTypes.Count);

        GenericType firstBaseType = Assert.IsType<GenericType>(baseClause.BaseTypes[0].Type);
        Assert.Equal("Base", firstBaseType.Name.Value);
        NamedExpressionGenericArgument firstBaseArgument = Assert.IsType<NamedExpressionGenericArgument>(firstBaseType.GenericArguments[0]);
        Assert.Equal("T", firstBaseArgument.Expression.Identifier.Value);

        QualifiedType secondBaseType = Assert.IsType<QualifiedType>(baseClause.BaseTypes[1].Type);
        Assert.Equal("Outer", Assert.IsType<SimpleType>(secondBaseType.Left).Name.Value);
        Assert.Equal("Inner", Assert.IsType<SimpleType>(secondBaseType.Right).Name.Value);

        TypeConstraintClause constraintClause = Assert.Single(type.Constraints);
        Assert.Equal("T", constraintClause.GenericParameter.Name.Value);
        Assert.Equal(2, constraintClause.Constraints.Count);

        TypeTypeConstraint firstConstraint = Assert.IsType<TypeTypeConstraint>(constraintClause.Constraints[0]);
        Assert.Equal("FirstConstraint", Assert.IsType<SimpleType>(firstConstraint.Type).Name.Value);

        TypeTypeConstraint secondConstraint = Assert.IsType<TypeTypeConstraint>(constraintClause.Constraints[1]);
        QualifiedType secondConstraintType = Assert.IsType<QualifiedType>(secondConstraint.Type);
        Assert.Equal("Second", Assert.IsType<SimpleType>(secondConstraintType.Left).Name.Value);
        Assert.Equal("Constraint", Assert.IsType<SimpleType>(secondConstraintType.Right).Name.Value);
    }

    [Fact]
    public void Parse_QualifiedDeclarationNamesAllowGenericsOnlyOnTheFinalTypeName()
    {
        var (_, validDiagnostics, _, _) = CompilerTestBed.Parse("""
            struct A.B<T>;
            """);
        var (_, invalidTypeDiagnostics, _, _) = CompilerTestBed.Parse("""
            struct A.B<T>.C;
            """);
        var (_, invalidNamespaceDiagnostics, _, _) = CompilerTestBed.Parse("""
            namespace A<T>;
            """);

        Assert.Empty(validDiagnostics.Diagnostics);
        Assert.NotEmpty(invalidTypeDiagnostics.Diagnostics);
        Assert.NotEmpty(invalidNamespaceDiagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_FunctionDeclaration_WithTypeConstraints()
    {
        FunctionDeclaration function = ParseSingleTopLevelFunction("""
            public static func Build<TInput, TResult>(TInput input) -> TResult
                where TInput: Source
                where TResult: Output<TInput>;
            """);

        GenericName identifier = Assert.IsType<GenericName>(function.Signature.Identifier);
        Assert.Equal("Build", identifier.Name.Value);
        Assert.Equal(2, identifier.GenericParameters.Count);
        Assert.Equal("TInput", identifier.GenericParameters[0].Identifier.Value);
        Assert.Equal("TResult", identifier.GenericParameters[1].Identifier.Value);

        Assert.Equal(2, function.Signature.Constraints.Count);

        TypeConstraintClause inputConstraint = function.Signature.Constraints[0];
        Assert.Equal("TInput", inputConstraint.GenericParameter.Name.Value);
        TypeTypeConstraint inputTypeConstraint = Assert.IsType<TypeTypeConstraint>(Assert.Single(inputConstraint.Constraints));
        Assert.Equal("Source", Assert.IsType<SimpleType>(inputTypeConstraint.Type).Name.Value);

        TypeConstraintClause resultConstraint = function.Signature.Constraints[1];
        Assert.Equal("TResult", resultConstraint.GenericParameter.Name.Value);
        TypeTypeConstraint resultTypeConstraint = Assert.IsType<TypeTypeConstraint>(Assert.Single(resultConstraint.Constraints));
        GenericType resultConstraintType = Assert.IsType<GenericType>(resultTypeConstraint.Type);
        Assert.Equal("Output", resultConstraintType.Name.Value);
        Assert.Equal("TInput", Assert.IsType<NamedExpressionGenericArgument>(resultConstraintType.GenericArguments[0]).Expression.Identifier.Value);
    }

    [Fact]
    public void Parse_TrailingCommas_AreAllowedOutsideVariableDeclarators()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            [First, Second,]
            public struct Box<T, N: int,> : Base<T,>, Other,
                where T : Constraint, OtherConstraint,
            {
                public func Function(Int32 a, Int32 b,) -> ()
                {
                    Call(a, b,);
                    new Int32[] { a, b, };
                }
            }

            public Box<Int32, 100,> value;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        TypeDeclaration box = Assert.IsType<TopLevelTypeDeclaration>(root.Members[0]).Type;
        GenericName name = Assert.IsType<GenericName>(box.Name);
        TypeBaseClause baseClause = Assert.IsType<TypeBaseClause>(box.Base);
        TypeConstraintClause constraint = Assert.Single(box.Constraints);
        FunctionDeclaration function = Assert.IsType<MemberFunctionDeclaration>(Assert.IsType<TypeBlockBody>(box.Body).Members[0]).Function;
        FunctionBlockBody body = Assert.IsType<FunctionBlockBody>(function.Body);
        GenericType variableType = Assert.IsType<GenericType>(Assert.IsType<TopLevelVariableDeclaration>(root.Members[1]).Declaration.Type);

        AssertTrailingComma(box.Attributes[0].Attributes);
        AssertTrailingComma(name.GenericParameters);
        AssertTrailingComma(baseClause.BaseTypes);
        AssertTrailingComma(Assert.IsType<GenericType>(baseClause.BaseTypes[0].Type).GenericArguments);
        AssertTrailingComma(constraint.Constraints);
        AssertTrailingComma(function.Signature.Parameters);
        AssertTrailingComma(Assert.IsType<CallExpression>(Assert.IsType<LocalExpressionStatement>(body.Locals[0]).Expression).Arguments);
        AssertTrailingComma(Assert.IsType<ArrayCreationExpression>(Assert.IsType<LocalExpressionStatement>(body.Locals[1]).Expression).Initializer!.Expressions);
        AssertTrailingComma(variableType.GenericArguments);
    }


    [Fact]
    public void Parse_GenericDeclaration_SupportsCompileTimeAndVariadicParameters()
    {
        TypeDeclaration declaration = ParseSingleTopLevelType("""
            struct Example<T, N: int, F: float, C: const, Rest...> where N : Std.Int32;
            """);

        GenericName name = Assert.IsType<GenericName>(declaration.Name);
        Assert.Equal(5, name.GenericParameters.Count);
        Assert.Equal(GenericParameterKind.Type, name.GenericParameters[0].Kind);
        Assert.Equal(GenericParameterKind.Integer, name.GenericParameters[1].Kind);
        Assert.Equal(GenericParameterKind.Float, name.GenericParameters[2].Kind);
        Assert.Equal(GenericParameterKind.Constant, name.GenericParameters[3].Kind);
        Assert.True(name.GenericParameters[4].IsVariadic);
        Assert.Equal(3, name.GenericParameters[4].Ellipsis.Count);

        TypeConstraintClause constraint = Assert.Single(declaration.Constraints);
        Assert.Equal("N", constraint.GenericParameter.Name.Value);
        Assert.IsType<QualifiedType>(Assert.IsType<TypeTypeConstraint>(Assert.Single(constraint.Constraints)).Type);
    }

    [Fact]
    public void Parse_DeclarationAttributes_SupportQualifiedNamesAndConstructorArguments()
    {
        AttributeSignature attribute = ParseSingleTopLevelAttribute("""
            [Marker]
            [Standard.IntrinsicType("Int32", 32)]
            public attribute SignedInt;
            """);

        Assert.Equal(2, attribute.Attributes.Count);

        AttributeApplication simpleAttribute = Assert.Single(attribute.Attributes[0].Attributes);
        Assert.Equal("Marker", Assert.IsType<SimpleName>(simpleAttribute.Name).Name.Value);
        Assert.Empty(simpleAttribute.Arguments);
        Assert.Null(simpleAttribute.OpenParen);
        Assert.Null(simpleAttribute.CloseParen);

        AttributeApplication qualifiedAttribute = Assert.Single(attribute.Attributes[1].Attributes);
        QualifiedName qualifiedName = Assert.IsType<QualifiedName>(qualifiedAttribute.Name);
        Assert.Equal(2, qualifiedName.Parts.Count);
        Assert.Equal("Standard", Assert.IsType<SimpleName>(qualifiedName.Parts[0]).Name.Value);
        Assert.Equal("IntrinsicType", Assert.IsType<SimpleName>(qualifiedName.Parts[1]).Name.Value);
        Assert.Equal(2, qualifiedAttribute.Arguments.Count);
        Assert.IsType<LiteralExpression>(qualifiedAttribute.Arguments[0]);
        Assert.IsType<LiteralExpression>(qualifiedAttribute.Arguments[1]);
    }

    [Fact]
    public void Parse_FunctionParameter_WithAttributes()
    {
        FunctionDeclaration function = ParseSingleTopLevelFunction("""
            func Foo([NotNull] int x, [First][Second(1)] string y = "default") -> ();
            """);

        Assert.Equal(2, function.Signature.Parameters.Count);

        Parameter param1 = function.Signature.Parameters[0];
        Assert.Single(param1.Attributes);
        AttributeApplication attr1 = Assert.Single(param1.Attributes[0].Attributes);
        Assert.Equal("NotNull", Assert.IsType<SimpleName>(attr1.Name).Name.Value);
        Assert.Equal("x", Assert.IsType<SimpleName>(param1.Declarator.Identifier).Name.Value);

        Parameter param2 = function.Signature.Parameters[1];
        Assert.Equal(2, param2.Attributes.Count);
        Assert.Equal("First", Assert.IsType<SimpleName>(Assert.Single(param2.Attributes[0].Attributes).Name).Name.Value);
        AttributeApplication second = Assert.Single(param2.Attributes[1].Attributes);
        Assert.Equal("Second", Assert.IsType<SimpleName>(second.Name).Name.Value);
        Assert.Single(second.Arguments);
        Assert.NotNull(param2.Initializer);
    }

    [Fact]
    public void Parse_IntrinsicModifier_IsValidOnlyForAttributeDeclarations()
    {
        AttributeSignature intrinsicAttribute = ParseSingleTopLevelAttribute("""
            public intrinsic attribute Intrinsic;
            """);

        Assert.Equal(2, intrinsicAttribute.Modifiers.Count);
        Assert.Contains(intrinsicAttribute.Modifiers, token => token.MatchingKind == MatchingKeywordKind.Intrinsic);

        TopLevelVariableDeclaration variable = Assert.IsType<TopLevelVariableDeclaration>(ParseSingleTopLevel("""
            public intrinsic value;
            """, typeof(TopLevelVariableDeclaration)));

        SimpleType variableType = Assert.IsType<SimpleType>(variable.Declaration.Type);
        Assert.Equal("intrinsic", variableType.Name.Value);
        Assert.DoesNotContain(variable.Declaration.Modifiers, token => token.MatchingKind == MatchingKeywordKind.Intrinsic);
    }

    [Fact]
    public void Parse_PropertyDeclaration_SupportsAccessorModifiersAndBodies()
    {
        MemberPropertyDeclaration property = Assert.IsType<MemberPropertyDeclaration>(ParseSingleMember("""
            [Meta]
            public int Value
            {
                get;
                private set
                {
                    return;
                }
            }
            """, typeof(MemberPropertyDeclaration)));

        Assert.Single(property.Attributes);
        Assert.Equal("Value", Assert.IsType<SimpleName>(property.Identifier).Name.Value);
        Assert.Equal(2, property.Body.Accessors.Count);
        Assert.Equal(PropertyAccessorKind.Get, property.Body.Accessors[0].Kind);
        Assert.IsType<FunctionEmptyBody>(property.Body.Accessors[0].Body);
        Assert.Equal(PropertyAccessorKind.Set, property.Body.Accessors[1].Kind);
        Assert.Single(property.Body.Accessors[1].Modifiers);
        Assert.Equal(MatchingKeywordKind.Private, property.Body.Accessors[1].Modifiers[0].MatchingKind);
        Assert.IsType<FunctionBlockBody>(property.Body.Accessors[1].Body);
    }

    [Fact]
    public void Parse_TypeDeclaration_AllowsTrailingCommaBeforeConstraintClause()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Box<T> : Base<T>,
                where T: Constraint
            {
                public int Value;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        TopLevelTypeDeclaration declaration = Assert.Single(root.Members.OfType<TopLevelTypeDeclaration>());
        TypeDeclaration type = declaration.Type;

        TypeBaseClause baseClause = Assert.IsType<TypeBaseClause>(type.Base);
        Assert.Single(baseClause.BaseTypes);
        AssertTrailingComma(baseClause.BaseTypes);
        Assert.Single(type.Constraints);

        TypeBlockBody body = Assert.IsType<TypeBlockBody>(type.Body);
        Assert.Single(body.Members);
        Assert.IsType<MemberFieldDeclaration>(body.Members[0]);
    }

    [Fact]
    public void Parse_SupportedSyntaxSurface_BuildsCurrentNodeSet()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            using Extra;
            namespace Outer;

            public int topValue = 1;
            public int topOther;
            PointerCandidate * topPointer;
            ReferenceCandidate & topReference;
            using TopAlias = Extra.Nested;
            using Extra;

            namespace Extra
            {
                public struct Nested;
            }

            public struct Box<T> : BaseBox<T>, Extra.Nested where T: Extra.Nested, Constraint<T>
            {
                using Extra;
                using MemberAlias = Extra.Nested;
                public T Value;
                public struct Nested;

                public static func Transform<TInput>(int[] items, int* pointer, int? maybe, int& reference, (&int)[] groupedSliceParam, &int aliasParam, *int unsafeAliasParam, untagged int untaggedParam, Extra.Nested nested, TInput input) -> int where TInput: Extra.Nested
                {
                    using Extra;
                    using LocalAlias = Extra.Nested;
                    public struct LocalBox<TLocal> : Scoped where TLocal: Extra.Nested;
                    public static func LocalFunc<TLocal>(TLocal input) -> int where TLocal: Extra.Nested
                    {
                        return 0;
                    }

                    int local = 1;
                    int[] numbers = new int[3] { 1, 2, 3 };
                    int[3] fixedNumbers;
                    (int, int) pair = (1, 2);
                    PointerLocal * localPointer;
                    ReferenceLocal & localReference;
                    local = -(local + 1) + (items[0] as int);
                    local = (local) - local;
                    local = { int last = 2; 3 };
                    local = [1, 2, 3] with(capacity: 10)[0];
                    local = new Box<int>(local) with { Value = local }.Value;
                    numbers = new int[3] with { Length = local };
                    local = new Box<int>(local).Value;
                    local = if (local) local else 0;
                    local = identity<int>(value: local);
                    (int | string) anonUnion;
                    untagged (int | string) untaggedUnion;
                    Box<untagged int> boxOfUntagged;
                    untagged int untaggedLocal = 0;
                    local = match (local)
                    {
                        MyEnum.UnitVar => 1,
                        MyEnum.TupleVar(val) => val,
                        MyEnum.StructVar { x } => x,
                        1 => 2,
                        int t => t,
                        _ => 0
                    };
                    var created = MyEnum.StructVar { x = local };

                    if (int testCond = local; testCond) local = 1; else local = 2;
                    ;
                    while (int testLoop = local; testLoop) local = 1;
                    { int scoped = 0; scoped = local; }
                    return local;
                }

                const
                {
                    func UnsafeFunc() -> () { }
                }
            }

            public enum MyEnum with int
            {
                UnitVar = 1,
                TupleVar(int x) = 2,
                StructVar { int x; } = 3;
                public func Method() -> () { }
            }

            public union MyUnion (int | string)
            {
                public func Method() -> () { }
            }

            public static func identity<T>(T value) -> int where T: Extra.Nested
            {
                return value;
            }

            public static func Main() -> int
            {
                return 0;
            }

            public static func Forward() -> ();

            global { using Extra; int globalBlockValue = 0; }
            call();
            if (1) 2 else 3;
            while (1) call();
            { int blockValue = 0; }
            return 0;
            ;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        HashSet<Type> nodeTypes = CollectNodeTypes(root);

        AssertIncludesNodeTypes(
            nodeTypes,
            typeof(PragmaDirective),
            typeof(UsingDirective),
            typeof(TopLevelUsingDirective),
            typeof(TopLevelAliasDeclaration),
            typeof(MemberUsingDirective),
            typeof(MemberAliasDeclaration),
            typeof(LocalUsingDirective),
            typeof(LocalAliasDeclaration),
            typeof(AliasDeclaration),
            typeof(NamespaceDeclaration),
            typeof(NamespaceEmptyBody),
            typeof(NamespaceBlockBody),
            typeof(TopLevelTypeDeclaration),
            typeof(TopLevelFunctionDeclaration),
            typeof(TopLevelVariableDeclaration),
            typeof(TypeDeclaration),
            typeof(TypeBlockBody),
            typeof(TypeEmptyBody),
            typeof(FunctionDeclaration),
            typeof(FunctionBlockBody),
            typeof(FunctionEmptyBody),
            typeof(MemberFieldDeclaration),
            typeof(MemberFunctionDeclaration),
            typeof(MemberTypeDeclaration),
            typeof(MemberBlockDeclaration),
            typeof(LocalTypeDeclaration),
            typeof(LocalFunctionDeclaration),
            typeof(LocalVariableDeclarationStatement),
            typeof(TopLevelExpressionStatement),
            typeof(TopLevelBlockDeclaration),
            typeof(TopLevelReturnStatement),
            typeof(TopLevelEmptyStatement),
            typeof(LocalExpressionStatement),
            typeof(LocalReturnStatement),
            typeof(LocalEmptyStatement),
            typeof(VariableDeclaration),
            typeof(AssignmentClause),
            typeof(Parameter),
            typeof(ParameterVariableDeclarator),
            typeof(SimpleName),
            typeof(GenericName),
            typeof(SimpleType),
            typeof(GenericType),
            typeof(QualifiedType),
            typeof(ModifiedType),
            typeof(PrefixModifiedType),
            typeof(TupleType),
            typeof(TypeBaseClause),
            typeof(TypeConstraintClause),
            typeof(TypeTypeConstraint),
            typeof(SliceTypeModifier),
            typeof(ArrayTypeModifier),
            typeof(PointerTypeModifier),
            typeof(OptionalTypeModifier),
            typeof(ReferenceTypeModifier),
            typeof(GroupedType),
            typeof(SafeAliasTypeModifier),
            typeof(UnsafeAliasTypeModifier),
            typeof(LiteralExpression),
            typeof(IdentifierNameExpression),
            typeof(GenericNameExpression),
            typeof(CallExpression),
            typeof(IndexExpression),
            typeof(MemberAccessExpression),
            typeof(UnaryExpression),
            typeof(BinaryExpression),
            typeof(AssignmentExpression),
            typeof(ParenthesizedExpression),
            typeof(TupleExpression),
            typeof(AsExpression),
            typeof(BlockExpression),
            typeof(CollectionExpression),
            typeof(CollectionConstructorModifier),
            typeof(IfExpression),
            typeof(ElseExpression),
            typeof(WhileExpression),
            typeof(ConstructorCallExpression),
            typeof(ArrayCreationExpression),
            typeof(ObjectWithClause),
            typeof(TypeInitializer),
            typeof(NamedArgumentExpression),
            typeof(EnumUnitVariant),
            typeof(EnumTupleVariant),
            typeof(EnumStructVariant),
            typeof(TypeEnumBody),
            typeof(UnionType),
            typeof(MatchExpression),
            typeof(MatchArm),
            typeof(VariantCreationExpression),
            typeof(WildcardPattern),
            typeof(LiteralPattern),
            typeof(FieldPatternSyntax),
            typeof(EnumVariantPattern),
            typeof(TypePattern),
            typeof(VariablePattern),
            typeof(UntaggedTypeModifier));
    }

    [Fact]
    public void Parse_RecoversAndStillParsesLaterDeclarations()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static func Broken(int argc, char*[] argv
            {
                call(1, 2, ;
                return 0
            }

            public static func Next() -> int
            {
                return 1;
            }
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);

        TopLevelFunctionDeclaration nextFunction = Assert.Single(
            root.Members.OfType<TopLevelFunctionDeclaration>(),
            function => function.Function.Signature.Identifier is SimpleName { Name.Value: "Next" });

        Assert.IsType<FunctionBlockBody>(nextFunction.Function.Body);
    }

    private static TopLevel ParseSingleTopLevel(string source, Type expectedType)
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse(source);

        Assert.Empty(diagnostics.Diagnostics);

        TopLevel topLevel = Assert.Single(root.Members);
        Assert.IsType(expectedType, topLevel);
        return topLevel;
    }

    private static Member ParseSingleMember(string source, Type expectedType)
    {
        TopLevelTypeDeclaration wrapper = Assert.IsType<TopLevelTypeDeclaration>(ParseSingleTopLevel($$"""
            public struct Host
            {
                {{source}}
            }
            """, typeof(TopLevelTypeDeclaration)));

        TypeBlockBody body = Assert.IsType<TypeBlockBody>(wrapper.Type.Body);
        Member member = Assert.Single(body.Members);
        Assert.IsType(expectedType, member);
        return member;
    }

    private static Local ParseSingleLocal(string source, Type expectedType)
    {
        TopLevelFunctionDeclaration wrapper = Assert.IsType<TopLevelFunctionDeclaration>(ParseSingleTopLevel($$"""
            public static func Main() -> int
            {
                {{source}}
            }
            """, typeof(TopLevelFunctionDeclaration)));

        FunctionBlockBody body = Assert.IsType<FunctionBlockBody>(wrapper.Function.Body);
        Local local = Assert.Single(body.Locals);
        Assert.IsType(expectedType, local);
        return local;
    }

    private static AttributeSignature ParseSingleTopLevelAttribute(string source)
    {
        TopLevelAttributeDeclaration declaration = Assert.IsType<TopLevelAttributeDeclaration>(ParseSingleTopLevel(source, typeof(TopLevelAttributeDeclaration)));
        return declaration.Attribute;
    }

    private static TypeDeclaration ParseSingleTopLevelType(string source)
    {
        TopLevelTypeDeclaration declaration = Assert.IsType<TopLevelTypeDeclaration>(ParseSingleTopLevel(source, typeof(TopLevelTypeDeclaration)));
        return declaration.Type;
    }

    private static FunctionDeclaration ParseSingleTopLevelFunction(string source)
    {
        TopLevelFunctionDeclaration declaration = Assert.IsType<TopLevelFunctionDeclaration>(ParseSingleTopLevel(source, typeof(TopLevelFunctionDeclaration)));
        return declaration.Function;
    }

    private static HashSet<Type> CollectNodeTypes(SyntaxNode root)
    {
        HashSet<Type> types = [];
        CollectNodeTypes(root, types);
        return types;
    }

    private static void CollectNodeTypes(SyntaxNode node, ISet<Type> nodeTypes)
    {
        nodeTypes.Add(node.GetType());

        foreach (PropertyInfo property in node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).OrderBy(static property => property.MetadataToken))
        {
            object? value = property.GetValue(node);

            if (value is null or string)
                continue;

            if (value is SyntaxNode child)
            {
                CollectNodeTypes(child, nodeTypes);
                continue;
            }

            if (value is not IEnumerable sequence)
                continue;

            foreach (object? item in sequence)
            {
                if (item is SyntaxNode sequenceChild)
                    CollectNodeTypes(sequenceChild, nodeTypes);
            }
        }
    }

    private static void AssertIncludesNodeTypes(HashSet<Type> actualTypes, params Type[] expectedTypes)
    {
        foreach (Type expectedType in expectedTypes)
            Assert.Contains(expectedType, actualTypes);
    }

    private static void AssertTrailingComma<T>(SeparatedSyntaxList<T> list) where T : SyntaxNode
    {
        Assert.NotEqual(0, list.Count);
        Assert.Equal(TokenKind.Comma, Assert.IsType<Token>(list.GetSeparator(list.Count - 1)).Kind);
    }

    [Fact]
    public void Parse_ErrorSpans_PointToUnexpectedTokenOnSameLineAndEndOfLineForMissingTokens()
    {
        var (text, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            namespace Sample;

            some err
            nice = }
            foo.;
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);

        // 1. "some err" should be parsed as TopLevelVariableDeclaration missing a semicolon
        var someErrMember = Assert.IsType<TopLevelVariableDeclaration>(root.Members[1]);
        Assert.Equal("some", Assert.IsType<SimpleType>(someErrMember.Declaration.Type).Name.Value);
        Assert.Equal("err", Assert.IsType<SimpleName>(someErrMember.Declaration.Declarator.Identifier).Name.Value);

        // Diagnostic for missing ';' after "some err" points to unexpected token on line 5 (1-based line 5:1) with line 4 as Context
        var missingSemiDiag = Assert.Single(diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0120" && d.Message.Contains("variable declaration"));
        Assert.Equal(5, missingSemiDiag.Span.GetStartLine(text) + 1);
        Assert.Equal(1, missingSemiDiag.Span.GetStartColumn(text) + 1);
        Assert.Equal(2, missingSemiDiag.Labels.Count);
        Assert.Equal(DiagnosticLabelStyle.Context, missingSemiDiag.Labels[0].Style);
        Assert.Equal(4, missingSemiDiag.Labels[0].Span.GetStartLine(text) + 1);
        Assert.Equal(9, missingSemiDiag.Labels[0].Span.GetStartColumn(text) + 1);
        Assert.Equal(DiagnosticLabelStyle.Primary, missingSemiDiag.Labels[1].Style);
        Assert.Equal(5, missingSemiDiag.Labels[1].Span.GetStartLine(text) + 1);
        Assert.Equal(1, missingSemiDiag.Labels[1].Span.GetStartColumn(text) + 1);

        // 2. "nice = }" should be parsed as an expression statement (assignment expression)
        var niceStmt = Assert.IsType<TopLevelExpressionStatement>(root.Members[2]);
        var assignExpr = Assert.IsType<AssignmentExpression>(niceStmt.Expression);
        Assert.Equal("nice", Assert.IsType<IdentifierNameExpression>(assignExpr.LhsExpression).Identifier.Value);

        // Diagnostic for missing expression after '=' on the same line points to '}' (1-based line 5:8)
        var missingExprDiag = Assert.Single(diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0121");
        Assert.Equal(5, missingExprDiag.Span.GetStartLine(text) + 1);
        Assert.Equal(8, missingExprDiag.Span.GetStartColumn(text) + 1);

        // Diagnostic for missing ';' after "nice = }" points to unexpected token on line 6 (1-based line 6:1) with line 5 as Context
        var missingSemiNiceDiag = Assert.Single(diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0120" && d.Message.Contains("top-level expression"));
        Assert.Equal(6, missingSemiNiceDiag.Span.GetStartLine(text) + 1);
        Assert.Equal(1, missingSemiNiceDiag.Span.GetStartColumn(text) + 1);
        Assert.Equal(2, missingSemiNiceDiag.Labels.Count);
        Assert.Equal(DiagnosticLabelStyle.Context, missingSemiNiceDiag.Labels[0].Style);
        Assert.Equal(5, missingSemiNiceDiag.Labels[0].Span.GetStartLine(text) + 1);
        Assert.Equal(9, missingSemiNiceDiag.Labels[0].Span.GetStartColumn(text) + 1);
        Assert.Equal(DiagnosticLabelStyle.Primary, missingSemiNiceDiag.Labels[1].Style);
        Assert.Equal(6, missingSemiNiceDiag.Labels[1].Span.GetStartLine(text) + 1);
        Assert.Equal(1, missingSemiNiceDiag.Labels[1].Span.GetStartColumn(text) + 1);

        // 3. "foo.;" should have diagnostic pointing to ';' (1-based line 6:5)
        var missingIdentDiag = Assert.Single(diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0122");
        Assert.Equal(6, missingIdentDiag.Span.GetStartLine(text) + 1);
        Assert.Equal(5, missingIdentDiag.Span.GetStartColumn(text) + 1);
    }

    [Fact]
    public void Parse_MacroDeclaration_MultiArm()
    {
        var topLevel = ParseSingleTopLevel("""
            public macro $fact {
                (0) => 1;
                (@n: expr) => @n * $fact(@n - 1);
            }
            """, typeof(TopLevelMacroDeclaration));

        var macroDecl = Assert.IsType<TopLevelMacroDeclaration>(topLevel).Macro;
        Assert.Equal("fact", macroDecl.Name.Value);
        Assert.Equal(2, macroDecl.Arms.Count);

        var arm0 = macroDecl.Arms[0];
        Assert.NotNull(arm0.Pattern);
        Assert.Single(arm0.Pattern.Parameters);
        Assert.Equal(MacroParameterKind.Literal, arm0.Pattern.Parameters[0].Kind);

        var arm1 = macroDecl.Arms[1];
        Assert.NotNull(arm1.Pattern);
        Assert.Single(arm1.Pattern.Parameters);
        Assert.Equal("n", arm1.Pattern.Parameters[0].Name.Value);
        Assert.Equal(MacroParameterKind.Expression, arm1.Pattern.Parameters[0].Kind);
        Assert.False(arm1.Pattern.Parameters[0].IsVariadic);
    }

    [Fact]
    public void Parse_MacroDeclaration_ShorthandBody()
    {
        var topLevel = ParseSingleTopLevel("""
            public macro $Point2D {
                int32 x;
                int32 y;
            }
            """, typeof(TopLevelMacroDeclaration));

        var macroDecl = Assert.IsType<TopLevelMacroDeclaration>(topLevel).Macro;
        Assert.Equal("Point2D", macroDecl.Name.Value);
        var arm = Assert.Single(macroDecl.Arms);
        Assert.Null(arm.Pattern);
        Assert.False(arm.Template.IsExpression);
        Assert.NotEmpty(arm.Template.Tokens);
    }

    [Fact]
    public void Parse_MacroDeclaration_ArrowExpression()
    {
        var topLevel = ParseSingleTopLevel("""
            public macro $Answer => 42;
            """, typeof(TopLevelMacroDeclaration));

        var macroDecl = Assert.IsType<TopLevelMacroDeclaration>(topLevel).Macro;
        Assert.Equal("Answer", macroDecl.Name.Value);
        var arm = Assert.Single(macroDecl.Arms);
        Assert.Null(arm.Pattern);
        Assert.True(arm.Template.IsExpression);
        Assert.Single(arm.Template.Tokens);
        Assert.Equal("42", arm.Template.Tokens[0].Value);
    }

    [Fact]
    public void Parse_MacroDeclaration_VariadicParameter()
    {
        var topLevel = ParseSingleTopLevel("""
            public macro $ListInit {
                (@items: expr...) => [@items...];
            }
            """, typeof(TopLevelMacroDeclaration));

        var macroDecl = Assert.IsType<TopLevelMacroDeclaration>(topLevel).Macro;
        var arm = Assert.Single(macroDecl.Arms);
        Assert.NotNull(arm.Pattern);
        var param = Assert.Single(arm.Pattern.Parameters);
        Assert.Equal("items", param.Name.Value);
        Assert.Equal(MacroParameterKind.Expression, param.Kind);
        Assert.True(param.IsVariadic);
    }

    [Fact]
    public void Parse_MemberMacroInvocation_AndDeclaration()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Point
            {
                public macro $InnerMixin {
                    int32 z;
                }
                $Point2D;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var typeDecl = Assert.IsType<TopLevelTypeDeclaration>(Assert.Single(root.Members));
        var body = Assert.IsType<TypeBlockBody>(typeDecl.Type.Body);
        Assert.Equal(2, body.Members.Count);
        Assert.IsType<MemberMacroDeclaration>(body.Members[0]);
        var inv = Assert.IsType<MemberMacroInvocationDeclaration>(body.Members[1]);
        Assert.Equal("Point2D", inv.Invocation.Name.Value);
        Assert.Empty(inv.Invocation.Arguments);
    }

    [Fact]
    public void Parse_BacktickEscapedIdentifier_TreatedAsIdentifier()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct `class`
            {
                public int32 `macro`;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var typeDecl = Assert.IsType<TopLevelTypeDeclaration>(Assert.Single(root.Members));
        var simpleName = Assert.IsType<SimpleName>(typeDecl.Type.Name);
        Assert.Equal("class", simpleName.Name.Value);
    }

    [Fact]
    public void Parse_ContiguousCombinedOperator_ParsesAsCombined()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            var a = 1 == 2;
            var b = 3 <= 4;
            var c = 5 != 6;
            """);

        Assert.Empty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_SeparatedCombinedOperator_DoesNotParseAsCombined()
    {
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            var a = 1 = = 2;
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_SeparatedLessThanEquals_DoesNotParseAsCombined()
    {
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            var a = 1 < = 2;
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_ContiguousArrow_ParsesInMacroArm()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            macro $test => 42;
            macro $test2 {
                () => 42;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        Assert.Equal(2, root.Members.Count);
        var macro1 = Assert.IsType<TopLevelMacroDeclaration>(root.Members[0]);
        Assert.Equal(TokenKind.EqualsGreaterThan, macro1.Macro.Arms[0].ArrowToken!.Kind);
        var macro2 = Assert.IsType<TopLevelMacroDeclaration>(root.Members[1]);
        Assert.Equal(TokenKind.EqualsGreaterThan, macro2.Macro.Arms[0].ArrowToken!.Kind);
    }

    [Fact]
    public void Parse_SeparatedArrow_FailsInMacroArm()
    {
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            macro $test = > 42;
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);
        Assert.Contains(diagnostics.Diagnostics, d => d.Message.Contains("'=>'"));
    }

    [Fact]
    public void Parse_ArrowInUnexpectedExpressionContext_ReportsArrowInDiagnostic()
    {
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            var a = 1 => 2;
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);
        Assert.Contains(diagnostics.Diagnostics, d => d.Message.Contains("'=>'"));
    }

    [Fact]
    public void Parse_GenericDeclarationInStatementContext_ParsesAsDeclaration()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func Test() -> ()
            {
                A<B> C;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var func = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members)).Function;
        var body = Assert.IsType<FunctionBlockBody>(func.Body);
        var decl = Assert.IsType<LocalVariableDeclarationStatement>(Assert.Single(body.Locals));
        var genType = Assert.IsType<GenericType>(decl.Declaration.Type);
        Assert.Equal("A", genType.Name.Value);
        Assert.Equal("C", Assert.IsType<SimpleName>(decl.Declaration.Declarator.Identifier).Name.Value);
    }

    [Theory]
    [InlineData("++x", typeof(UnaryExpression))]
    [InlineData("x++", typeof(UnaryExpression))]
    [InlineData("--x", typeof(UnaryExpression))]
    [InlineData("x--", typeof(UnaryExpression))]
    [InlineData("!x", typeof(UnaryExpression))]
    [InlineData("x!", typeof(UnaryExpression))]
    [InlineData("x?", typeof(UnaryExpression))]
    [InlineData("~x", typeof(UnaryExpression))]
    [InlineData("a & b", typeof(BinaryExpression))]
    [InlineData("a | b", typeof(BinaryExpression))]
    [InlineData("a ^ b", typeof(BinaryExpression))]
    [InlineData("a << b", typeof(BinaryExpression))]
    [InlineData("a >> b", typeof(BinaryExpression))]
    [InlineData("a -> b", typeof(MemberAccessExpression))]
    [InlineData("a += b", typeof(AssignmentExpression))]
    [InlineData("a -= b", typeof(AssignmentExpression))]
    [InlineData("a *= b", typeof(AssignmentExpression))]
    [InlineData("a /= b", typeof(AssignmentExpression))]
    [InlineData("a %= b", typeof(AssignmentExpression))]
    public void Parse_SpecialExpressionKinds(string source, Type expectedType)
    {
        var stmt = ParseSingleLocal($"var x = {source};", typeof(LocalVariableDeclarationStatement));
        var decl = (LocalVariableDeclarationStatement)stmt;
        Assert.IsType(expectedType, decl.Declaration.Declarator.Initializer!.Initializer);
    }

    [Fact]
    public void Parse_ExpressionGenericsComparison_ParsesAsBinaryExpressions()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func Test() -> ()
            {
                var r1 = A<B>(x);
                var r2 = A<B>(x, y);
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var func = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members)).Function;
        var body = Assert.IsType<FunctionBlockBody>(func.Body);

        var stmt1 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[0]);
        var bin1 = Assert.IsType<BinaryExpression>(stmt1.Declaration.Declarator.Initializer!.Initializer);
        Assert.Equal(TokenKind.GreaterThanSign, bin1.OperatorToken.Kind);
        var leftBin1 = Assert.IsType<BinaryExpression>(bin1.LeftExpression);
        Assert.Equal(TokenKind.LessThanSign, leftBin1.OperatorToken.Kind);

        var stmt2 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[1]);
        var bin2 = Assert.IsType<BinaryExpression>(stmt2.Declaration.Declarator.Initializer!.Initializer);
        Assert.Equal(TokenKind.GreaterThanSign, bin2.OperatorToken.Kind);
        Assert.IsType<TupleExpression>(bin2.RightExpression);
    }

    [Fact]
    public void Parse_ExpressionGenericsCall_ParsesAsCallExpression()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func Test() -> ()
            {
                var r1 = A<B, C>(x);
                var r2 = A<B>(p: x);
                var r3 = A::<B>(x);
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var func = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members)).Function;
        var body = Assert.IsType<FunctionBlockBody>(func.Body);

        var stmt1 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[0]);
        var call1 = Assert.IsType<CallExpression>(stmt1.Declaration.Declarator.Initializer!.Initializer);
        var gen1 = Assert.IsType<GenericNameExpression>(call1.Callee);
        Assert.Null(gen1.ColonColonToken);

        var stmt2 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[1]);
        var call2 = Assert.IsType<CallExpression>(stmt2.Declaration.Declarator.Initializer!.Initializer);
        var gen2 = Assert.IsType<GenericNameExpression>(call2.Callee);
        Assert.Null(gen2.ColonColonToken);

        var stmt3 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[2]);
        var call3 = Assert.IsType<CallExpression>(stmt3.Declaration.Declarator.Initializer!.Initializer);
        var gen3 = Assert.IsType<GenericNameExpression>(call3.Callee);
        Assert.NotNull(gen3.ColonColonToken);
    }

    [Fact]
    public void Parse_TurbofishOnVariableInstance_ParsesAsGenericNameExpression()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func Test() -> ()
            {
                var v = A::<B>;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var func = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members)).Function;
        var body = Assert.IsType<FunctionBlockBody>(func.Body);
        var stmt = Assert.IsType<LocalVariableDeclarationStatement>(Assert.Single(body.Locals));
        var gen = Assert.IsType<GenericNameExpression>(stmt.Declaration.Declarator.Initializer!.Initializer);
        Assert.NotNull(gen.ColonColonToken);
        Assert.Equal("A", gen.Identifier.Value);
    }

    [Fact]
    public void Parse_TupleSyntax_SupportsNamedElementsAndUniformTuple()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func Test() -> ()
            {
                (Type a, Type2 b, Type3 c) var1;
                Type (a, b, c) var2;
                Type (a, b, c) = (1, 2, 3);
                (Type a, Type2 b, Type3 c) = (1, 2, 3);
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var func = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members)).Function;
        var body = Assert.IsType<FunctionBlockBody>(func.Body);

        var s1 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[0]);
        var tupleType = Assert.IsType<TupleType>(s1.Declaration.Type);
        Assert.Equal(3, tupleType.Elements.Count);
        Assert.Equal("a", tupleType.Elements[0].Name!.Value);
        Assert.Equal("b", tupleType.Elements[1].Name!.Value);
        Assert.Equal("c", tupleType.Elements[2].Name!.Value);

        var s2 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[1]);
        var uniformTupleType = Assert.IsType<UniformTupleType>(s2.Declaration.Type);
        Assert.Equal(3, uniformTupleType.Elements.Count);

        var s3 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[2]);
        var tupleName = Assert.IsType<TupleName>(s3.Declaration.Declarator.Identifier);
        Assert.Equal(3, tupleName.Elements.Count);

        var s4 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[3]);
        var s4Type = Assert.IsType<TupleType>(s4.Declaration.Type);
        Assert.Equal(3, s4Type.Elements.Count);
        var s4TupleName = Assert.IsType<TupleName>(s4.Declaration.Declarator.Identifier);
        Assert.Equal(3, s4TupleName.Elements.Count);
        Assert.Equal("a", Assert.IsType<SimpleName>(s4TupleName.Elements[0]).Name.Value);
        Assert.Equal("b", Assert.IsType<SimpleName>(s4TupleName.Elements[1]).Name.Value);
        Assert.Equal("c", Assert.IsType<SimpleName>(s4TupleName.Elements[2]).Name.Value);
        Assert.NotNull(s4.Declaration.Declarator.Initializer);
    }

    [Fact]
    public void Parse_SuffixedLiteralsInExpressions_ParsesAsLiteralExpressionsWithSuffixProperties()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func Test() -> ()
            {
                var a = 123f32 + 456i32;
                var b = 1.5e-3f32 * 2_000e1_0f64;
                var c = 'a'u8;
                var d = "hello"s;
                foo(0x10_u32, 0b1010u8);
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var func = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members)).Function;
        var body = Assert.IsType<FunctionBlockBody>(func.Body);

        // a = 123f32 + 456i32
        var s1 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[0]);
        var bin = Assert.IsType<BinaryExpression>(s1.Declaration.Declarator.Initializer!.Initializer);
        var left = Assert.IsType<LiteralExpression>(bin.LeftExpression);
        Assert.Equal(TokenKind.SuffixedInteger, left.Literal.Kind);
        Assert.True(left.HasSuffix);
        Assert.Equal("f32", left.Suffix);
        Assert.Equal("123", left.ValueWithoutSuffix);

        var right = Assert.IsType<LiteralExpression>(bin.RightExpression);
        Assert.Equal(TokenKind.SuffixedInteger, right.Literal.Kind);
        Assert.True(right.HasSuffix);
        Assert.Equal("i32", right.Suffix);
        Assert.Equal("456", right.ValueWithoutSuffix);

        // b = 1.5e-3f32 * 2_000e1_0f64
        var s2 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[1]);
        var mul = Assert.IsType<BinaryExpression>(s2.Declaration.Declarator.Initializer!.Initializer);
        var mulLeft = Assert.IsType<LiteralExpression>(mul.LeftExpression);
        Assert.Equal(TokenKind.SuffixedFloat, mulLeft.Literal.Kind);
        Assert.True(mulLeft.HasSuffix);
        Assert.Equal("f32", mulLeft.Suffix);
        Assert.Equal("1.5e-3", mulLeft.ValueWithoutSuffix);

        var mulRight = Assert.IsType<LiteralExpression>(mul.RightExpression);
        Assert.Equal(TokenKind.SuffixedFloat, mulRight.Literal.Kind);
        Assert.True(mulRight.HasSuffix);
        Assert.Equal("f64", mulRight.Suffix);
        Assert.Equal("2_000e1_0", mulRight.ValueWithoutSuffix);

        // c = 'a'u8
        var s3 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[2]);
        var charLit = Assert.IsType<LiteralExpression>(s3.Declaration.Declarator.Initializer!.Initializer);
        Assert.Equal(TokenKind.SuffixedChar, charLit.Literal.Kind);
        Assert.True(charLit.HasSuffix);
        Assert.Equal("u8", charLit.Suffix);
        Assert.Equal("'a'", charLit.ValueWithoutSuffix);

        // d = "hello"s
        var s4 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[3]);
        var strLit = Assert.IsType<LiteralExpression>(s4.Declaration.Declarator.Initializer!.Initializer);
        Assert.Equal(TokenKind.SuffixedString, strLit.Literal.Kind);
        Assert.True(strLit.HasSuffix);
        Assert.Equal("s", strLit.Suffix);
        Assert.Equal("\"hello\"", strLit.ValueWithoutSuffix);

        // foo(0x10_u32, 0b1010u8)
        var s5 = Assert.IsType<LocalExpressionStatement>(body.Locals[4]);
        var call = Assert.IsType<CallExpression>(s5.Expression);
        var arg0 = Assert.IsType<LiteralExpression>(call.Arguments[0]);
        Assert.Equal(TokenKind.SuffixedInteger, arg0.Literal.Kind);
        Assert.Equal("u32", arg0.Suffix);
        Assert.Equal("0x10", arg0.ValueWithoutSuffix);

        var arg1 = Assert.IsType<LiteralExpression>(call.Arguments[1]);
        Assert.Equal(TokenKind.SuffixedInteger, arg1.Literal.Kind);
        Assert.Equal("u8", arg1.Suffix);
        Assert.Equal("0b1010", arg1.ValueWithoutSuffix);
    }

    [Fact]
    public void Parse_GroupedTypeSliceInDeclarationContext_ParsesAsVariableDeclaration()
    {
        Local local = ParseSingleLocal("(&int32)[] x;", typeof(LocalVariableDeclarationStatement));
        var variable = Assert.IsType<LocalVariableDeclarationStatement>(local);
        var modified = Assert.IsType<ModifiedType>(variable.Declaration.Type);
        Assert.IsType<SliceTypeModifier>(modified.Modifier);
        var grouped = Assert.IsType<GroupedType>(modified.Type);
        var prefix = Assert.IsType<PrefixModifiedType>(grouped.InnerType);
        Assert.IsType<SafeAliasTypeModifier>(prefix.Modifier);
        Assert.Equal("int32", Assert.IsType<SimpleType>(prefix.Type).Name.Value);
        Assert.Equal("x", Assert.IsType<SimpleName>(variable.Declaration.Declarator.Identifier).Name.Value);
    }

    [Fact]
    public void Parse_SafeAliasDeclarationInDeclarationContext_ParsesAsVariableDeclaration()
    {
        Local local = ParseSingleLocal("&int32 x = y;", typeof(LocalVariableDeclarationStatement));
        var variable = Assert.IsType<LocalVariableDeclarationStatement>(local);
        var prefix = Assert.IsType<PrefixModifiedType>(variable.Declaration.Type);
        Assert.IsType<SafeAliasTypeModifier>(prefix.Modifier);
        var simple = Assert.IsType<SimpleType>(prefix.Type);
        Assert.Equal("int32", simple.Name.Value);
        Assert.Equal("x", Assert.IsType<SimpleName>(variable.Declaration.Declarator.Identifier).Name.Value);
    }

    [Fact]
    public void Parse_PrefixAmpersandInDeclarationContext_WhenNotFollowedByIdentifier_ParsesAsExpressionStatement()
    {
        Local local = ParseSingleLocal("&T + 1;", typeof(LocalExpressionStatement));
        var stmt = Assert.IsType<LocalExpressionStatement>(local);
        var bin = Assert.IsType<BinaryExpression>(stmt.Expression);
        var unary = Assert.IsType<UnaryExpression>(bin.LeftExpression);
        Assert.Equal(TokenKind.Ampersand, unary.OperatorToken.Kind);
        Assert.Equal("T", Assert.IsType<IdentifierNameExpression>(unary.Operand).Identifier.Value);
    }

    [Fact]
    public void Parse_PrefixAmpersandInExpressionContext_AlwaysParsesAsUnaryExpression()
    {
        Local local = ParseSingleLocal("var x = &T + 1;", typeof(LocalVariableDeclarationStatement));
        var variable = Assert.IsType<LocalVariableDeclarationStatement>(local);
        var init = variable.Declaration.Declarator.Initializer!.Initializer;
        var bin = Assert.IsType<BinaryExpression>(init);
        var unary = Assert.IsType<UnaryExpression>(bin.LeftExpression);
        Assert.Equal(TokenKind.Ampersand, unary.OperatorToken.Kind);
        Assert.Equal("T", Assert.IsType<IdentifierNameExpression>(unary.Operand).Identifier.Value);
    }

    [Fact]
    public void Parse_SliceTypeModifier_Vs_ArrayTypeModifier()
    {
        Local dynamicArr = ParseSingleLocal("int32[] x;", typeof(LocalVariableDeclarationStatement));
        var dynamicVar = Assert.IsType<LocalVariableDeclarationStatement>(dynamicArr);
        var dynamicMod = Assert.IsType<ModifiedType>(dynamicVar.Declaration.Type);
        Assert.IsType<SliceTypeModifier>(dynamicMod.Modifier);

        Local fixedArr = ParseSingleLocal("int32[10] y;", typeof(LocalVariableDeclarationStatement));
        var fixedVar = Assert.IsType<LocalVariableDeclarationStatement>(fixedArr);
        var fixedMod = Assert.IsType<ModifiedType>(fixedVar.Declaration.Type);
        var fixedArrMod = Assert.IsType<ArrayTypeModifier>(fixedMod.Modifier);
        var sizeLit = Assert.IsType<LiteralExpression>(fixedArrMod.Size);
        Assert.Equal("10", sizeLit.Literal.Value);
    }

    [Fact]
    public void Parse_ScopeResolution_TypesExpressionsDirectivesAndConformanceArgs()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            extern using Disamb;
            using ModA::Collections.Generic;
            using global::LocalNs;

            public struct Foo : private ModA::Bar, internal global::Baz, public Plain
            {
                public func ModA::Bar.Step(&this) -> () { }
            }

            public func Run() -> ()
            {
                Std.Int32& a = x;
                ModA::Ns.Item mut& b = y;
                global::MyStruct c = default;
                var d = default(ModA::Point);
                var e = new Point { x = 1, y = 2 };
                var f = new(alloc) Point { x = 3, y = 4 };
                HashSet<Foo as ModA::Hash> hs = default;
                var g = (ob as ModA::Bar).Step();
                var h = global::Helper::<int32>();
                var s = arr[1:5];
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        Assert.Single(root.ExternUsings);
        Assert.Equal("Disamb", Assert.IsType<SimpleName>(root.ExternUsings[0].AliasName).Name.Value);
        Assert.Equal(2, root.Usings.Count);
        Assert.IsType<ScopeQualifiedName>(root.Usings[0].Namespace);
        Assert.IsType<ScopeQualifiedName>(root.Usings[1].Namespace);

        var fooDecl = Assert.IsType<TopLevelTypeDeclaration>(root.Members[0]);
        var baseTypes = fooDecl.Type.Base!.BaseTypes;
        Assert.Equal(3, baseTypes.Count);
        Assert.Equal(MatchingKeywordKind.Private, baseTypes[0].VisibilityModifier!.MatchingKind);
        Assert.IsType<ScopeQualifiedType>(baseTypes[0].Type);
        Assert.Equal(MatchingKeywordKind.Internal, baseTypes[1].VisibilityModifier!.MatchingKind);
        Assert.IsType<ScopeQualifiedType>(baseTypes[1].Type);
        Assert.Equal(MatchingKeywordKind.Public, baseTypes[2].VisibilityModifier!.MatchingKind);

        var runDecl = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[1]);
        var body = Assert.IsType<FunctionBlockBody>(runDecl.Function.Body);

        // Std.Int32& has ReferenceTypeModifier wrapping QualifiedType (postfix outside qualification)
        var varA = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[0]);
        var modA = Assert.IsType<ModifiedType>(varA.Declaration.Type);
        Assert.IsType<ReferenceTypeModifier>(modA.Modifier);
        Assert.IsType<QualifiedType>(modA.Type);

        // ModA::Ns.Item mut&
        var varB = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[1]);
        var modB = Assert.IsType<ModifiedType>(varB.Declaration.Type);
        Assert.IsType<ReferenceTypeModifier>(modB.Modifier);
        var scopeB = Assert.IsType<ScopeQualifiedType>(modB.Type);
        Assert.Equal("ModA", scopeB.Qualifier.Value);
        Assert.IsType<QualifiedType>(scopeB.MemberType);

        // global::MyStruct = default
        var varC = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[2]);
        var scopeC = Assert.IsType<ScopeQualifiedType>(varC.Declaration.Type);
        Assert.Equal(MatchingKeywordKind.Global, scopeC.Qualifier.MatchingKind);
        Assert.IsType<DefaultExpression>(varC.Declaration.Declarator.Initializer!.Initializer);

        // default(ModA::Point)
        var varD = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[3]);
        var defD = Assert.IsType<DefaultExpression>(varD.Declaration.Declarator.Initializer!.Initializer);
        Assert.IsType<ScopeQualifiedType>(defD.ExplicitType);

        // new Point { x = 1, y = 2 } and new(alloc) Point { ... }
        var varE = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[4]);
        Assert.IsType<ObjectInitializerExpression>(varE.Declaration.Declarator.Initializer!.Initializer);
        var varF = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[5]);
        var initF = Assert.IsType<ObjectInitializerExpression>(varF.Declaration.Declarator.Initializer!.Initializer);
        Assert.NotNull(initF.AllocationArguments);

        // HashSet<Foo as ModA::Hash>
        var varHs = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[6]);
        var genHs = Assert.IsType<GenericType>(varHs.Declaration.Type);
        var confArg = Assert.IsType<ConformanceQualifiedType>(genHs.GenericArguments[0]);
        Assert.IsType<SimpleType>(confArg.TargetType);
        Assert.IsType<ScopeQualifiedType>(confArg.InterfaceType);

        // (ob as ModA::Bar).Step()
        var varG = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[7]);
        var callG = Assert.IsType<CallExpression>(varG.Declaration.Declarator.Initializer!.Initializer);
        var memberG = Assert.IsType<MemberAccessExpression>(callG.Callee);
        var parenG = Assert.IsType<ParenthesizedExpression>(memberG.Expression);
        var asG = Assert.IsType<AsExpression>(parenG.Expression);
        Assert.IsType<ScopeQualifiedType>(asG.Type);

        // global::Helper::<int32>()
        var varH = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[8]);
        var callH = Assert.IsType<CallExpression>(varH.Declaration.Declarator.Initializer!.Initializer);
        var scopeExprH = Assert.IsType<ScopeResolutionExpression>(callH.Callee);
        Assert.Equal(MatchingKeywordKind.Global, scopeExprH.Qualifier.MatchingKind);
        Assert.IsType<GenericNameExpression>(scopeExprH.Member);
    }
}
