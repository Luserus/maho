using System.Collections;
using System.Reflection;
using Maho;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class ParserTests
{
    [Theory]
    [InlineData("namespace Demo;", typeof(NamespaceDeclaration), typeof(NamespaceEmptyBody))]
    [InlineData("namespace Demo { public class Inner; }", typeof(NamespaceDeclaration), typeof(NamespaceBlockBody))]
    [InlineData("public class Box;", typeof(TopLevelTypeDeclaration), typeof(TypeEmptyBody))]
    [InlineData("public attribute Marker;", typeof(TopLevelTypeDeclaration), typeof(TypeEmptyBody))]
    [InlineData("public class Box { public int Value; }", typeof(TopLevelTypeDeclaration), typeof(TypeBlockBody))]
    [InlineData("public static int Main();", typeof(TopLevelFunctionDeclaration), typeof(FunctionEmptyBody))]
    [InlineData("public static int Main() { return 0; }", typeof(TopLevelFunctionDeclaration), typeof(FunctionBlockBody))]
    [InlineData("public int value;", typeof(TopLevelVariableDeclaration), null)]
    public void Parse_TopLevelDeclarationKinds(string source, Type expectedType, Type? expectedBodyType)
    {
        TopLevel topLevel = ParseSingleTopLevel(source, expectedType);

        if (expectedBodyType is null)
            return;

        object body = topLevel switch
        {
            NamespaceDeclaration @namespace => @namespace.Body,
            TopLevelTypeDeclaration typeDeclaration => typeDeclaration.Type.Body,
            TopLevelFunctionDeclaration functionDeclaration => functionDeclaration.Function.Body,
            _ => throw new Xunit.Sdk.XunitException($"Top-level node '{topLevel.GetType().Name}' does not expose a body.")
        };

        Assert.IsType(expectedBodyType, body);
    }

    [Theory]
    [InlineData("public struct Nested;", typeof(MemberTypeDeclaration), typeof(TypeEmptyBody))]
    [InlineData("public struct Nested { public int Value; }", typeof(MemberTypeDeclaration), typeof(TypeBlockBody))]
    [InlineData("public static int Compute();", typeof(MemberFunctionDeclaration), typeof(FunctionEmptyBody))]
    [InlineData("public static int Compute() { return 0; }", typeof(MemberFunctionDeclaration), typeof(FunctionBlockBody))]
    [InlineData("public int Value { get; set; }", typeof(MemberPropertyDeclaration), null)]
    [InlineData("public int Value;", typeof(MemberFieldDeclaration), null)]
    public void Parse_MemberDeclarationKinds(string source, Type expectedType, Type? expectedBodyType)
    {
        Member member = ParseSingleMember(source, expectedType);

        if (expectedBodyType is null)
            return;

        object body = member switch
        {
            MemberTypeDeclaration typeDeclaration => typeDeclaration.Type.Body,
            MemberFunctionDeclaration functionDeclaration => functionDeclaration.Function.Body,
            _ => throw new Xunit.Sdk.XunitException($"Member node '{member.GetType().Name}' does not expose a body.")
        };

        Assert.IsType(expectedBodyType, body);
    }

    [Theory]
    [InlineData("public class LocalBox;", typeof(LocalTypeDeclaration), typeof(TypeEmptyBody))]
    [InlineData("public class LocalBox { public int Value; }", typeof(LocalTypeDeclaration), typeof(TypeBlockBody))]
    [InlineData("public static int Local();", typeof(LocalFunctionDeclaration), typeof(FunctionEmptyBody))]
    [InlineData("public static int Local() { return 0; }", typeof(LocalFunctionDeclaration), typeof(FunctionBlockBody))]
    public void Parse_LocalDeclarationKinds(string source, Type expectedType, Type? expectedBodyType)
    {
        Local local = ParseSingleLocal(source, expectedType);

        if (expectedBodyType is null)
            return;

        object body = local switch
        {
            LocalTypeDeclaration typeDeclaration => typeDeclaration.Type.Body,
            LocalFunctionDeclaration functionDeclaration => functionDeclaration.Function.Body,
            _ => throw new Xunit.Sdk.XunitException($"Local node '{local.GetType().Name}' does not expose a body.")
        };

        Assert.IsType(expectedBodyType, body);
    }

    [Theory]
    [InlineData("call();", typeof(TopLevelExpressionStatement))]
    [InlineData("if (1) return; else ;", typeof(TopLevelIfStatement))]
    [InlineData("while (1) ;", typeof(TopLevelWhileStatement))]
    [InlineData("{ int value = 1; }", typeof(TopLevelBlockStatement))]
    [InlineData("return 0;", typeof(TopLevelReturnStatement))]
    [InlineData("return value;", typeof(TopLevelReturnStatement))]
    [InlineData(";", typeof(TopLevelEmptyStatement))]
    public void Parse_TopLevelStatementKinds(string source, Type expectedType)
    {
        TopLevel statement = ParseSingleTopLevel(source, expectedType);
        Assert.IsType(expectedType, statement);
    }

    [Theory]
    [InlineData("int value = 1;", typeof(LocalVariableDeclarationStatement))]
    [InlineData("call();", typeof(LocalExpressionStatement))]
    [InlineData("if (1) return; else ;", typeof(LocalIfStatement))]
    [InlineData("while (1) ;", typeof(LocalWhileStatement))]
    [InlineData("{ int nested = 1; }", typeof(LocalBlockStatement))]
    [InlineData("return 0;", typeof(LocalReturnStatement))]
    [InlineData(";", typeof(LocalEmptyStatement))]
    public void Parse_LocalStatementKinds(string source, Type expectedType)
    {
        Local statement = ParseSingleLocal(source, expectedType);
        Assert.IsType(expectedType, statement);
    }

    [Theory]
    [InlineData("PointerType * value;", typeof(TopLevelAmbiguousPointerDeclaration), typeof(AmbiguousPointerDeclaration))]
    [InlineData("ReferenceType & value;", typeof(TopLevelAmbiguousReferenceDeclaration), typeof(AmbiguousReferenceDeclaration))]
    public void Parse_TopLevelAmbiguousDeclarationKinds(string source, Type expectedType, Type expectedDeclarationType)
    {
        TopLevel topLevel = ParseSingleTopLevel(source, expectedType);

        object declaration = topLevel switch
        {
            TopLevelAmbiguousPointerDeclaration pointer => pointer.Declaration,
            TopLevelAmbiguousReferenceDeclaration reference => reference.Declaration,
            _ => throw new Xunit.Sdk.XunitException($"Top-level node '{topLevel.GetType().Name}' does not expose an ambiguous declaration.")
        };

        Assert.IsType(expectedDeclarationType, declaration);
    }

    [Theory]
    [InlineData("PointerType * value;", typeof(LocalAmbiguousPointerDeclarationStatement), typeof(AmbiguousPointerDeclaration))]
    [InlineData("ReferenceType & value;", typeof(LocalAmbiguousReferenceDeclarationStatement), typeof(AmbiguousReferenceDeclaration))]
    public void Parse_LocalAmbiguousDeclarationKinds(string source, Type expectedType, Type expectedDeclarationType)
    {
        Local local = ParseSingleLocal(source, expectedType);

        object declaration = local switch
        {
            LocalAmbiguousPointerDeclarationStatement pointer => pointer.Declaration,
            LocalAmbiguousReferenceDeclarationStatement reference => reference.Declaration,
            _ => throw new Xunit.Sdk.XunitException($"Local node '{local.GetType().Name}' does not expose an ambiguous declaration.")
        };

        Assert.IsType(expectedDeclarationType, declaration);
    }

    [Theory]
    [InlineData("[Marker] PointerType * value;")]
    [InlineData("public ReferenceType & value;")]
    public void Parse_TopLevelAttributedOrModifiedPointerReferenceDeclarations_AreUnambiguousDeclarations(string source)
    {
        TopLevel topLevel = ParseSingleTopLevel(source, typeof(TopLevelVariableDeclaration));
        TopLevelVariableDeclaration variable = Assert.IsType<TopLevelVariableDeclaration>(topLevel);

        Assert.IsType<ModifiedType>(variable.Declaration.Type);
    }

    [Theory]
    [InlineData("[Marker] PointerType * value;")]
    [InlineData("static ReferenceType & value;")]
    public void Parse_LocalAttributedOrModifiedPointerReferenceDeclarations_AreUnambiguousDeclarations(string source)
    {
        Local local = ParseSingleLocal(source, typeof(LocalVariableDeclarationStatement));
        LocalVariableDeclarationStatement variable = Assert.IsType<LocalVariableDeclarationStatement>(local);

        Assert.IsType<ModifiedType>(variable.Declaration.Type);
    }

    [Fact]
    public void Parse_VariableDeclaration_RejectsMultipleDeclarators()
    {
        var (_, diagnostics, _, _) = CompilerTestBed.Parse("""
            public int first, second;
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Parse_ObjectCreationWithClause_AttachesToConstructorCall()
    {
        LocalVariableDeclarationStatement local = Assert.IsType<LocalVariableDeclarationStatement>(ParseSingleLocal("""
            SomeType value = new SomeType(ctorValue) with { prop = "val" };
            """, typeof(LocalVariableDeclarationStatement)));

        ConstructorCallExpression constructor = Assert.IsType<ConstructorCallExpression>(local.Declaration.Initializer?.Initializer);
        ObjectWithClause withClause = Assert.IsType<ObjectWithClause>(constructor.WithClause);
        AssignmentExpression assignment = Assert.IsType<AssignmentExpression>(Assert.Single(withClause.Initializer.Expressions));

        Assert.Equal("prop", Assert.IsType<IdentifierNameExpression>(assignment.LhsExpression).Identifier.Value);
    }

    [Fact]
    public void Parse_ObjectCreationWithClause_AttachesToArrayCreation()
    {
        LocalVariableDeclarationStatement local = Assert.IsType<LocalVariableDeclarationStatement>(ParseSingleLocal("""
            int[] arr = put int[10] with { SomeProp = someVal };
            """, typeof(LocalVariableDeclarationStatement)));

        ArrayCreationExpression array = Assert.IsType<ArrayCreationExpression>(local.Declaration.Initializer?.Initializer);
        ObjectWithClause withClause = Assert.IsType<ObjectWithClause>(array.WithClause);
        AssignmentExpression assignment = Assert.IsType<AssignmentExpression>(Assert.Single(withClause.Initializer.Expressions));

        Assert.Equal("SomeProp", Assert.IsType<IdentifierNameExpression>(assignment.LhsExpression).Identifier.Value);
    }

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
    public void Parse_CastFollowedByPrefixInfixOperator_IsAmbiguous(string source, TokenKind expectedOperator)
    {
        LocalReturnStatement local = Assert.IsType<LocalReturnStatement>(ParseSingleLocal(source, typeof(LocalReturnStatement)));

        AmbiguousCastOrParenthesizedExpression ambiguous = Assert.IsType<AmbiguousCastOrParenthesizedExpression>(local.Statement.Expression);
        UnaryExpression castOperand = Assert.IsType<UnaryExpression>(ambiguous.CastExpression.Expression);
        BinaryExpression parenthesizedAlternative = Assert.IsType<BinaryExpression>(ambiguous.ParenthesizedExpression);

        Assert.Equal(expectedOperator, castOperand.OperatorToken.Kind);
        Assert.Equal(expectedOperator, parenthesizedAlternative.OperatorToken.Kind);
        Assert.IsType<ParenthesizedExpression>(parenthesizedAlternative.LeftExpression);
    }

    [Fact]
    public void Parse_CastFollowedByIdentifier_IsUnambiguousCast()
    {
        LocalReturnStatement local = Assert.IsType<LocalReturnStatement>(ParseSingleLocal("""
            return (A)B;
            """, typeof(LocalReturnStatement)));

        CastExpression cast = Assert.IsType<CastExpression>(local.Statement.Expression);
        Assert.IsType<IdentifierNameExpression>(cast.Expression);
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
            public class Box<T> : Base<T>, Outer.Inner
                where T: FirstConstraint, Second.Constraint;
            """);

        GenericName name = Assert.IsType<GenericName>(type.Name);
        Assert.Equal("Box", name.Name.Value);
        Assert.Single(name.TypeParameters);
        Assert.Equal("T", name.TypeParameters[0].Name.Value);

        TypeBaseClause baseClause = Assert.IsType<TypeBaseClause>(type.Base);
        Assert.Equal(2, baseClause.BaseTypes.Count);

        GenericType firstBaseType = Assert.IsType<GenericType>(baseClause.BaseTypes[0]);
        Assert.Equal("Base", firstBaseType.Name.Value);
        SimpleType firstBaseArgument = Assert.IsType<SimpleType>(firstBaseType.TypeArguments[0]);
        Assert.Equal("T", firstBaseArgument.Name.Value);

        QualifiedType secondBaseType = Assert.IsType<QualifiedType>(baseClause.BaseTypes[1]);
        Assert.Equal("Outer", Assert.IsType<SimpleType>(secondBaseType.Left).Name.Value);
        Assert.Equal("Inner", Assert.IsType<SimpleType>(secondBaseType.Right).Name.Value);

        TypeConstraintClause constraintClause = Assert.Single(type.Constraints);
        Assert.Equal("T", constraintClause.TypeParameter.Name.Value);
        Assert.Equal(2, constraintClause.Constraints.Count);

        TypeTypeConstraint firstConstraint = Assert.IsType<TypeTypeConstraint>(constraintClause.Constraints[0]);
        Assert.Equal("FirstConstraint", Assert.IsType<SimpleType>(firstConstraint.Type).Name.Value);

        TypeTypeConstraint secondConstraint = Assert.IsType<TypeTypeConstraint>(constraintClause.Constraints[1]);
        QualifiedType secondConstraintType = Assert.IsType<QualifiedType>(secondConstraint.Type);
        Assert.Equal("Second", Assert.IsType<SimpleType>(secondConstraintType.Left).Name.Value);
        Assert.Equal("Constraint", Assert.IsType<SimpleType>(secondConstraintType.Right).Name.Value);
    }

    [Fact]
    public void Parse_FunctionDeclaration_WithTypeConstraints()
    {
        FunctionDeclaration function = ParseSingleTopLevelFunction("""
            public static TResult Build<TInput, TResult>(TInput input)
                where TInput: Source
                where TResult: Output<TInput>;
            """);

        GenericName identifier = Assert.IsType<GenericName>(function.Signature.Identifier);
        Assert.Equal("Build", identifier.Name.Value);
        Assert.Equal(2, identifier.TypeParameters.Count);
        Assert.Equal("TInput", identifier.TypeParameters[0].Name.Value);
        Assert.Equal("TResult", identifier.TypeParameters[1].Name.Value);

        Assert.Equal(2, function.Signature.Constraints.Count);

        TypeConstraintClause inputConstraint = function.Signature.Constraints[0];
        Assert.Equal("TInput", inputConstraint.TypeParameter.Name.Value);
        TypeTypeConstraint inputTypeConstraint = Assert.IsType<TypeTypeConstraint>(Assert.Single(inputConstraint.Constraints));
        Assert.Equal("Source", Assert.IsType<SimpleType>(inputTypeConstraint.Type).Name.Value);

        TypeConstraintClause resultConstraint = function.Signature.Constraints[1];
        Assert.Equal("TResult", resultConstraint.TypeParameter.Name.Value);
        TypeTypeConstraint resultTypeConstraint = Assert.IsType<TypeTypeConstraint>(Assert.Single(resultConstraint.Constraints));
        GenericType resultConstraintType = Assert.IsType<GenericType>(resultTypeConstraint.Type);
        Assert.Equal("Output", resultConstraintType.Name.Value);
        Assert.Equal("TInput", Assert.IsType<SimpleType>(resultConstraintType.TypeArguments[0]).Name.Value);
    }

    [Fact]
    public void Parse_DeclarationAttributes_SupportQualifiedNamesAndConstructorArguments()
    {
        TypeDeclaration type = ParseSingleTopLevelType("""
            [Marker]
            [Standard.IntrinsicType("Int32", 32)]
            public attribute SignedInt;
            """);

        Assert.Equal(TypeKind.Attribute, type.Kind);
        Assert.Equal(2, type.Attributes.Count);

        AttributeApplication simpleAttribute = Assert.Single(type.Attributes[0].Attributes);
        Assert.Equal("Marker", Assert.IsType<SimpleName>(simpleAttribute.Name).Name.Value);
        Assert.Empty(simpleAttribute.Arguments);
        Assert.Null(simpleAttribute.OpenParen);
        Assert.Null(simpleAttribute.CloseParen);

        AttributeApplication qualifiedAttribute = Assert.Single(type.Attributes[1].Attributes);
        QualifiedName qualifiedName = Assert.IsType<QualifiedName>(qualifiedAttribute.Name);
        Assert.Equal(2, qualifiedName.Parts.Count);
        Assert.Equal("Standard", Assert.IsType<SimpleName>(qualifiedName.Parts[0]).Name.Value);
        Assert.Equal("IntrinsicType", Assert.IsType<SimpleName>(qualifiedName.Parts[1]).Name.Value);
        Assert.Equal(2, qualifiedAttribute.Arguments.Count);
        Assert.IsType<LiteralExpression>(qualifiedAttribute.Arguments[0]);
        Assert.IsType<LiteralExpression>(qualifiedAttribute.Arguments[1]);
    }

    [Fact]
    public void Parse_IntrinsicModifier_IsValidOnlyForAttributeDeclarations()
    {
        TypeDeclaration intrinsicAttribute = ParseSingleTopLevelType("""
            public intrinsic attribute Intrinsic;
            """);

        Assert.Equal(TypeKind.Attribute, intrinsicAttribute.Kind);
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
    public void Parse_TypeDeclaration_RecoversFromTrailingCommaBeforeConstraintClause()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public class Box<T> : Base<T>,
                where T: Constraint
            {
                public int Value;
            }
            """);

        Assert.NotEmpty(diagnostics.Diagnostics);

        TopLevelTypeDeclaration declaration = Assert.Single(root.Members.OfType<TopLevelTypeDeclaration>());
        TypeDeclaration type = declaration.Type;

        TypeBaseClause baseClause = Assert.IsType<TypeBaseClause>(type.Base);
        Assert.Single(baseClause.BaseTypes);
        Assert.Single(type.Constraints);

        TypeBlockBody body = Assert.IsType<TypeBlockBody>(type.Body);
        Assert.Single(body.Members);
        Assert.IsType<MemberFieldDeclaration>(body.Members[0]);
    }

    [Fact]
    public void Parse_SupportedSyntaxSurface_BuildsCurrentNodeSet()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            namespace Outer;

            public int topValue = 1;
            public int topOther;
            PointerCandidate * topPointer;
            ReferenceCandidate & topReference;

            namespace Extra
            {
                public class Nested;
            }

            public struct Box<T> : BaseBox<T>, Extra.Nested where T: Extra.Nested, Constraint<T>
            {
                public T Value;
                public class Nested;

                public static int Transform<TInput>(int[] items, int* pointer, int? maybe, int& reference, Extra.Nested nested, TInput input) where TInput: Extra.Nested
                {
                    public class LocalBox<TLocal> : Scoped where TLocal: Extra.Nested;
                    public static int LocalFunc<TLocal>(TLocal input) where TLocal: Extra.Nested
                    {
                        return 0;
                    }

                    int local = 1;
                    int[] numbers = new int[3] { 1, 2, 3 };
                    PointerLocal * localPointer;
                    ReferenceLocal & localReference;
                    local = -(local + 1) + (int)items[0];
                    local = (local) - local;
                    local = { int last = 2; 3 };
                    local = [1, 2, 3] with(capacity: 10)[0];
                    local = new Box<int>(local) with { Value = local }.Value;
                    numbers = put int[3] with { Length = local };
                    local = put Box<int>(local).Value;
                    local = if (local) local else 0;
                    local = identity<int>(value: local);

                    if (local) return local; else ;
                    while (local) ;
                    { int scoped = 0; scoped = local; }
                    return local;
                }
            }

            public static int identity<T>(T value) where T: Extra.Nested
            {
                return value;
            }

            public static int Main()
            {
                return 0;
            }

            public static void Forward();

            call();
            if (1) return; else ;
            while (1) ;
            { int blockValue = 0; }
            return 0;
            ;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        HashSet<Type> nodeTypes = CollectNodeTypes(root);

        AssertIncludesNodeTypes(
            nodeTypes,
            typeof(NamespaceDeclaration),
            typeof(NamespaceEmptyBody),
            typeof(NamespaceBlockBody),
            typeof(TopLevelTypeDeclaration),
            typeof(TopLevelFunctionDeclaration),
            typeof(TopLevelVariableDeclaration),
            typeof(TopLevelAmbiguousPointerDeclaration),
            typeof(TopLevelAmbiguousReferenceDeclaration),
            typeof(TypeDeclaration),
            typeof(TypeBlockBody),
            typeof(TypeEmptyBody),
            typeof(FunctionDeclaration),
            typeof(FunctionBlockBody),
            typeof(FunctionEmptyBody),
            typeof(MemberFieldDeclaration),
            typeof(MemberFunctionDeclaration),
            typeof(MemberTypeDeclaration),
            typeof(LocalTypeDeclaration),
            typeof(LocalFunctionDeclaration),
            typeof(LocalVariableDeclarationStatement),
            typeof(LocalAmbiguousPointerDeclarationStatement),
            typeof(LocalAmbiguousReferenceDeclarationStatement),
            typeof(TopLevelExpressionStatement),
            typeof(TopLevelIfStatement),
            typeof(TopLevelElseStatement),
            typeof(TopLevelWhileStatement),
            typeof(TopLevelBlockStatement),
            typeof(TopLevelReturnStatement),
            typeof(TopLevelEmptyStatement),
            typeof(LocalExpressionStatement),
            typeof(LocalIfStatement),
            typeof(LocalElseStatement),
            typeof(LocalWhileStatement),
            typeof(LocalBlockStatement),
            typeof(LocalReturnStatement),
            typeof(LocalEmptyStatement),
            typeof(VariableDeclaration),
            typeof(AmbiguousPointerDeclaration),
            typeof(AmbiguousReferenceDeclaration),
            typeof(AssignmentClause),
            typeof(Parameter),
            typeof(ParameterVariableDeclarator),
            typeof(SimpleName),
            typeof(GenericName),
            typeof(SimpleType),
            typeof(GenericType),
            typeof(QualifiedType),
            typeof(ModifiedType),
            typeof(TypeBaseClause),
            typeof(TypeConstraintClause),
            typeof(TypeTypeConstraint),
            typeof(ArrayTypeModifier),
            typeof(PointerTypeModifier),
            typeof(OptionalTypeModifier),
            typeof(ReferenceTypeModifier),
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
            typeof(CastExpression),
            typeof(AmbiguousCastOrParenthesizedExpression),
            typeof(BlockExpression),
            typeof(CollectionExpression),
            typeof(CollectionConstructorModifier),
            typeof(IfExpression),
            typeof(ElseExpression),
            typeof(ConstructorCallExpression),
            typeof(ArrayCreationExpression),
            typeof(ObjectWithClause),
            typeof(CollectionInitializer),
            typeof(NamedArgumentExpression));
    }

    [Fact]
    public void Parse_RecoversAndStillParsesLaterDeclarations()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static int Broken(int argc, char*[] argv
            {
                call(1, 2, ;
                return 0
            }

            public static int Next()
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
            public class Host
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
            public static int Main()
            {
                {{source}}
            }
            """, typeof(TopLevelFunctionDeclaration)));

        FunctionBlockBody body = Assert.IsType<FunctionBlockBody>(wrapper.Function.Body);
        Local local = Assert.Single(body.Locals);
        Assert.IsType(expectedType, local);
        return local;
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
}
