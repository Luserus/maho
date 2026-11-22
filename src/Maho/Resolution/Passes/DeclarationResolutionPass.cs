using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

/// <summary>
/// Resolution Pass 3: Declaration Resolution.
/// <para>
/// Resolves declaration-owned references and signatures after all declarations have been discovered:
/// <list type="bullet">
/// <item><description>Resolves base types, interface implementations, and sum type constituents.</description></item>
/// <item><description>Resolves field and global variable declared types.</description></item>
/// <item><description>Resolves parameter types, return types, and generic parameter constraints on functions and methods.</description></item>
/// <item><description>Resolves attributes applied to types, functions, parameters, and variables.</description></item>
/// <item><description>Validates mutual exclusivity of operator semantics (such as Destructor vs. Copy and Copy vs. Move).</description></item>
/// </list>
/// </para>
/// </summary>
internal sealed partial class DeclarationResolutionPass : ResolutionPass
{
    private record struct FunctionSignatureInfo(Symbol Symbol, FunctionFlags Flags, FunctionDeclaration? Syntax, IReadOnlyList<SymbolHandle> GenericParameters, List<SymbolHandle> Parameters, TypeRef ReturnType);

    private ResolutionContext context = null!;
    private Symbol? currentTypeSymbol;
    private readonly HashSet<SyntaxNode> resolvedBodies = [];
    private readonly Dictionary<(TypeSyntax, Scope, bool, bool), TypeRef> resolvedTypes = [];
    private readonly HashSet<VariableDeclarator> resolvedDeclarators = [];
    private readonly HashSet<ParameterSymbol> resolvedParameters = [];
    private readonly Dictionary<(VariableDeclaration, Scope), List<SymbolHandle>> resolvedVariableAttributes = [];

    /// <summary>
    /// Executes declaration resolution across all discovered symbols.
    /// </summary>
    /// <param name="context">The shared compilation resolution context.</param>
    public override void Resolve(ResolutionContext context)
    {
        this.context = context;
        resolvedBodies.Clear();
        resolvedTypes.Clear();
        resolvedDeclarators.Clear();
        resolvedParameters.Clear();
        resolvedVariableAttributes.Clear();

        foreach (var attribute in context.AttributeSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(attribute));
            ResolveAttributeDeclaration(attribute, attribute.Syntax);
        }

        foreach (var attribute in context.NestedAttributeSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(attribute));
            ResolveAttributeDeclaration(attribute, attribute.Syntax);
        }

        foreach (var type in context.TypeSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(type));
            ResolveType(type, type.Syntax);
        }

        foreach (var type in context.NestedTypeSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(type));
            ResolveType(type, type.Syntax);
        }

        foreach (var alias in context.AliasSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(alias));
            ResolveAlias(alias);
        }

        foreach (var function in context.FunctionSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(function));
            ResolveFunction(function);
        }

        foreach (var method in context.MethodSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(method));
            ResolveMethod(method);
        }

        foreach (var parameter in context.ParameterSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(parameter));
            ResolveParameter(parameter);
        }

        foreach (var global in context.GlobalVariableSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(global));
            ResolveVariable(global, global.Syntax);
        }

        foreach (var constant in context.ConstantSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(constant));
            ResolveConstant(constant);
        }

        foreach (var field in context.FieldSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(field));
            ResolveVariable(field, field.Syntax);
        }

        foreach (var local in context.LocalVariableSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(local));
            ResolveVariable(local, local.Syntax);
        }

        foreach (var property in context.PropertySymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(property));
            ResolveProperty(property);
        }

        // Resolve throwaway sink declarations so their signatures and bodies resolve error-free
        foreach (var attribute in context.SinkSymbols.AttributeSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(attribute));
            ResolveAttributeDeclaration(attribute, attribute.Syntax);
        }

        foreach (var attribute in context.SinkSymbols.NestedAttributeSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(attribute));
            ResolveAttributeDeclaration(attribute, attribute.Syntax);
        }

        foreach (var type in context.SinkSymbols.TypeSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(type));
            ResolveType(type, type.Syntax);
        }

        foreach (var type in context.SinkSymbols.NestedTypeSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(type));
            ResolveType(type, type.Syntax);
        }

        foreach (var alias in context.SinkSymbols.AliasSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(alias));
            ResolveAlias(alias);
        }

        foreach (var function in context.SinkSymbols.FunctionSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(function));
            ResolveFunction(function);
        }

        foreach (var method in context.SinkSymbols.MethodSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(method));
            ResolveMethod(method);
        }

        foreach (var parameter in context.SinkSymbols.ParameterSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(parameter));
            ResolveParameter(parameter);
        }

        foreach (var global in context.SinkSymbols.GlobalVariableSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(global));
            ResolveVariable(global, global.Syntax);
        }

        foreach (var constant in context.SinkSymbols.ConstantSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(constant));
            ResolveConstant(constant);
        }

        foreach (var field in context.SinkSymbols.FieldSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(field));
            ResolveVariable(field, field.Syntax);
        }

        foreach (var local in context.SinkLocalVariableSymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(local));
            ResolveVariable(local, local.Syntax);
        }

        foreach (var property in context.SinkSymbols.PropertySymbols)
        {
            context.CurrentTreeFilePath = context.GetSymbolTreeFilePath(ResolutionContext.GetHandle(property));
            ResolveProperty(property);
        }

        foreach (var tree in context.SyntaxTrees)
        {
            context.CurrentTreeFilePath = tree.FilePath;
            ResolveTopLevelStatements(tree.Root);
        }

        context.CurrentTreeFilePath = null;

        CheckDuplicateTypeDeclarations();
        CheckCyclicTypeHierarchies();
        LinearizeAndDeduplicateAllBases();
        ValidateConstructorBaseInitializers();
        CheckDuplicateAndPartialFunctions();
        CheckDuplicateVariables();
        CheckDuplicateProperties();
        ValidateInterfaceImplementations();
        ValidateModifiers();
    }

    private void ResolveTopLevelStatements(CompilationUnit root)
    {
        var scope = context.GetSyntaxScope(root, context.GlobalScope);
        SymbolHandle? main = null;

        foreach (var function in context.FunctionSymbols)
        {
            if (function.Syntax is null && ReferenceEquals(GetOwnedScope(function), scope))
            {
                main = ResolutionContext.GetHandle(function);
                break;
            }
        }

        if (main is null)
            return;

        foreach (var member in root.Members)
            ResolveTopLevel(member, scope, main.Value);
    }

    private void ResolveTopLevel(TopLevel node, Scope scope, SymbolHandle containingFunction)
    {
        switch (node)
        {
            case TopLevelBlockDeclaration block:
                {
                    var blockScope = context.GetSyntaxScope(block, scope);
                    foreach (var member in block.Members)
                        ResolveTopLevel(member, blockScope, containingFunction);
                    break;
                }
            case NamespaceDeclaration declaration when declaration.Body is NamespaceBlockBody body:
                {
                    var blockScope = context.GetSyntaxScope(body, scope);
                    foreach (var member in body.Members)
                        ResolveTopLevel(member, blockScope, containingFunction);
                    break;
                }
            case TopLevelExpressionStatement statement:
                ResolveExpression(statement.Expression, scope, containingFunction);
                break;
            case TopLevelReturnStatement statement:
                ResolveExpression(statement.Statement.Expression, scope, containingFunction);
                break;
            case TopLevelGotoStatement statement:
                ResolveLabelReference(statement, new SymbolPart(statement.Identifier), containingFunction);
                break;
        }
    }

    private void ResolveAttributeDeclaration(AttributeSymbol symbol, AttributeSignature? syntax)
    {
        if (syntax is null)
            return;

        symbol.Attributes = ResolveAttributes(syntax.Attributes, symbol.EnclosingScope);

        foreach (var handle in symbol.Parameters)
        {
            if (context.GetParameterSymbol(handle) is { } parameter)
                ResolveParameter(parameter);
        }
    }

    private void ResolveAttributeDeclaration(NestedAttributeSymbol symbol, AttributeSignature? syntax)
    {
        if (syntax is null)
            return;

        symbol.Attributes = ResolveAttributes(syntax.Attributes, symbol.EnclosingScope);

        foreach (var handle in symbol.Parameters)
        {
            if (context.GetParameterSymbol(handle) is { } parameter)
                ResolveParameter(parameter);
        }
    }

    private void ResolveType(TypeSymbol symbol, TypeDeclaration? syntax)
    {
        if (syntax is null)
            return;

        currentTypeSymbol = symbol;

        var scope = GetOwnedScope(symbol);
        symbol.Attributes = ResolveAttributes(syntax.Attributes, symbol.EnclosingScope);
        symbol.BaseTypes = ResolveTypes(syntax.Base?.BaseTypes ?? new SeparatedSyntaxList<TypeSyntax>([]), scope, isTraitBoundContext: true);
        ApplyImplicitBases(symbol, syntax);
        ValidateLifecycleOperatorCombinations(symbol.BaseTypes, ResolutionContext.GetHandle(symbol), symbol.Name.Text, syntax.Name.GetSpan() ?? default, syntax.GetSource());
        ValidateCallableImplementation(symbol.BaseTypes, ResolutionContext.GetHandle(symbol), symbol.Name.Text, syntax.Name.GetSpan() ?? default, syntax.GetSource());

        ResolveTypeConstraints(syntax.Constraints, symbol.GenericParameters, scope);
        ResolveGenericParameterDeclarations(syntax.Name, symbol.GenericParameters);

        if (symbol is ISumTypeSymbol sumType)
            ResolveSumType(sumType, symbol, syntax, scope);
        else
            PopulateProductMembers(symbol);
    }

    private void ApplyImplicitBases(TypeSymbol symbol, TypeDeclaration? syntax)
    {
        if (symbol.TypeKind == TypeKind.Struct)
        {
            var handle = ResolutionContext.GetHandle(symbol);
            ApplyImplicitStructBasesCore(handle, symbol.Name.Text, symbol.BaseTypes, syntax, b => symbol.BaseTypes = b);
        }
        else if (symbol.TypeKind == TypeKind.Enum)
        {
            var handle = ResolutionContext.GetHandle(symbol);
            ApplyImplicitEnumBasesCore(handle, symbol.Name.Text, symbol.BaseTypes, syntax, b => symbol.BaseTypes = b);
        }
    }

    private void ApplyImplicitBases(NestedTypeSymbol symbol, TypeDeclaration? syntax)
    {
        if (symbol.TypeKind == TypeKind.Struct)
        {
            var handle = ResolutionContext.GetHandle(symbol);
            ApplyImplicitStructBasesCore(handle, symbol.Name.Text, symbol.BaseTypes, syntax, b => symbol.BaseTypes = b);
        }
        else if (symbol.TypeKind == TypeKind.Enum)
        {
            var handle = ResolutionContext.GetHandle(symbol);
            ApplyImplicitEnumBasesCore(handle, symbol.Name.Text, symbol.BaseTypes, syntax, b => symbol.BaseTypes = b);
        }
    }

    /// <summary>
    /// Implements implicit deriving rules for structs:
    /// <list type="bullet">
    ///   <item><description><c>ValType</c> has no base type.</description></item>
    ///   <item><description><c>Struct</c>, <c>Array</c>, <c>Enum</c>, <c>Function</c>, <c>Delegate</c>, <c>Unit</c>, and <c>Tuple</c> derive directly from <c>ValType</c>.</description></item>
    ///   <item><description>All other structs (user structs and numeric intrinsics) implicitly derive from <c>Struct</c> (and transitively from <c>ValType</c>).</description></item>
    /// </list>
    /// </summary>
    private void ApplyImplicitStructBasesCore(SymbolHandle handle, string typeName, IReadOnlyList<TypeRef> existingBases, TypeDeclaration? syntax, Action<List<TypeRef>> setBaseTypes)
    {
        bool isValueType = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.ValueType, out var valTypeH) && handle == valTypeH;
        bool isNever = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Never, out var neverH) && handle == neverH;
        bool isStruct = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Struct, out var structH) && handle == structH;
        bool isArray = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Array, out var arrayH) && handle == arrayH;
        bool isFixedArray = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.FixedArray, out var fixedArrH) && handle == fixedArrH;
        bool isMetadata = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Metadata, out var metaH) && handle == metaH;
        bool isEnum = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Enum, out var enumH) && handle == enumH;
        bool isFunction = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Function, out var fnH) && handle == fnH;
        bool isDelegate = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Delegate, out var delH) && handle == delH;
        bool isUnit = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Unit, out var unitH) && handle == unitH;
        bool isTuple = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Tuple, out var tupleH) && handle == tupleH;
        bool isPointer = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Pointer, out var ptrH) && handle == ptrH;
        bool isMutPointer = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.MutablePointer, out var mutPtrH) && handle == mutPtrH;
        bool isReference = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Reference, out var refH) && handle == refH;
        bool isMutReference = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.MutableReference, out var mutRefH) && handle == mutRefH;

        if (isValueType || isNever || isStruct || isArray || isFixedArray || isMetadata || isEnum || isFunction || isDelegate || isUnit || isTuple || isPointer || isMutPointer || isReference || isMutReference)
            return;

        // All other structs (user structs and intrinsics like integers, floats, etc.) derive from Struct
        if (structH != default)
        {
            if (valTypeH != default && existingBases.Any(b => b.Handle == valTypeH))
                return;

            var structRef = TypeRef.Resolved(structH);
            if (!existingBases.Any(b => b.Handle == structH))
            {
                var updated = new List<TypeRef>(existingBases) { structRef };
                setBaseTypes(updated);
            }
        }
        else if (context.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "struct declaration", KnownIntrinsics.Struct, syntax?.GetSource());
        }
    }

    /// <summary>
    /// Implements implicit deriving rules for enums: all user-defined enum declarations implicitly derive from <c>Enum</c>.
    /// </summary>
    private void ApplyImplicitEnumBasesCore(SymbolHandle handle, string typeName, IReadOnlyList<TypeRef> existingBases, TypeDeclaration? syntax, Action<List<TypeRef>> setBaseTypes)
    {
        bool isValueType = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.ValueType, out var valTypeH) && handle == valTypeH;
        bool isEnum = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Enum, out var enumH) && handle == enumH;

        if (isValueType || isEnum)
            return;

        if (enumH != default)
        {
            var enumRef = TypeRef.Resolved(enumH);
            if (!existingBases.Any(b => b.Handle == enumH))
            {
                var updated = new List<TypeRef>(existingBases) { enumRef };
                setBaseTypes(updated);
            }
        }
        else if (context.DiscoveredIntrinsics.IntrinsicTypeAttribute is not null)
        {
            var span = syntax?.Name.GetSpan() ?? default;
            context.Diagnostics.ReportMissingIntrinsicForSyntax(span, "enum declaration", KnownIntrinsics.Enum, syntax?.GetSource());
        }
    }

    /// <summary>
    /// Validates lifecycle operator exclusivity rules: Destructor NAND Copy (MH0243) and Copy NAND Move (MH0244).
    /// </summary>
    private void ValidateLifecycleOperatorCombinations(IReadOnlyList<TypeRef> baseTypes, SymbolHandle typeHandle, string typeName, TextSpan span, SourceText? source)
    {
        bool hasDestructor = false;
        bool hasCopy = false;
        bool hasMove = false;

        foreach (var baseType in baseTypes)
        {
            if (!baseType.IsResolved || baseType.Handle is not { } ifaceHandle)
                continue;

            if (HasOperatorOnInterface(ifaceHandle, OperatorKind.Destructor))
                hasDestructor = true;
            if (HasOperatorOnInterface(ifaceHandle, OperatorKind.Copy))
                hasCopy = true;
            if (HasOperatorOnInterface(ifaceHandle, OperatorKind.Move))
                hasMove = true;
        }

        if (HasOperatorOnType(typeHandle, OperatorKind.Destructor))
            hasDestructor = true;
        if (HasOperatorOnType(typeHandle, OperatorKind.Copy))
            hasCopy = true;
        if (HasOperatorOnType(typeHandle, OperatorKind.Move))
            hasMove = true;

        if (hasDestructor && hasCopy)
            context.Diagnostics.ReportDestructorNandCopy(span, typeName, source);

        if (hasCopy && hasMove)
            context.Diagnostics.ReportCopyNandMove(span, typeName, source);
    }

    /// <summary>
    /// Validates that <c>Function</c> and <c>Delegate</c> types implement the <c>Callable</c> operator interface.
    /// </summary>
    private void ValidateCallableImplementation(IReadOnlyList<TypeRef> baseTypes, SymbolHandle typeHandle, string typeName, TextSpan span, SourceText? source)
    {
        bool isFunction = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Function, out var fnH) && typeHandle == fnH;
        bool isDelegate = context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Delegate, out var delH) && typeHandle == delH;

        if (!isFunction && !isDelegate)
            return;

        bool implementsCallable = false;
        foreach (var baseType in baseTypes)
        {
            if (baseType.Handle is { } ifaceHandle && HasOperatorOnInterface(ifaceHandle, OperatorKind.Callable))
            {
                implementsCallable = true;
                break;
            }
        }

        if (!implementsCallable)
        {
            string intrinsicName = isFunction ? KnownIntrinsics.Function : KnownIntrinsics.Delegate;
            context.Diagnostics.ReportIntrinsicAbiMismatch(span, typeName, intrinsicName, "must implement an operator interface marked with [OperatorFunction(\"Callable\")]", source);
        }
    }

    private bool HasOperatorOnInterface(SymbolHandle ifaceHandle, OperatorKind opKind)
    {
        var list = context.DiscoveredOperators.GetOperators(opKind);
        foreach (var op in list)
        {
            if (op.InterfaceHandle == ifaceHandle)
                return true;
        }
        return false;
    }

    private bool HasOperatorOnType(SymbolHandle typeHandle, OperatorKind opKind)
    {
        var list = context.DiscoveredOperators.GetOperators(opKind);
        foreach (var op in list)
        {
            if (op.InterfaceHandle == typeHandle)
                return true;
        }
        return false;
    }

    private void ResolveType(NestedTypeSymbol symbol, TypeDeclaration? syntax)
    {
        if (syntax is null)
            return;

        currentTypeSymbol = symbol;

        var scope = GetOwnedScope(symbol);
        symbol.Attributes = ResolveAttributes(syntax.Attributes, symbol.EnclosingScope);
        symbol.BaseTypes = ResolveTypes(syntax.Base?.BaseTypes ?? new SeparatedSyntaxList<TypeSyntax>([]), scope, isTraitBoundContext: true);
        ApplyImplicitBases(symbol, syntax);
        ValidateLifecycleOperatorCombinations(symbol.BaseTypes, ResolutionContext.GetHandle(symbol), symbol.Name.Text, syntax.Name.GetSpan() ?? default, syntax.GetSource());
        ValidateCallableImplementation(symbol.BaseTypes, ResolutionContext.GetHandle(symbol), symbol.Name.Text, syntax.Name.GetSpan() ?? default, syntax.GetSource());

        ResolveTypeConstraints(syntax.Constraints, symbol.GenericParameters, scope);
        ResolveGenericParameterDeclarations(syntax.Name, symbol.GenericParameters);

        if (symbol is ISumTypeSymbol sumType)
            ResolveSumType(sumType, symbol, syntax, scope);
        else
            PopulateProductMembers(symbol);
    }

    private void ResolveSumType(ISumTypeSymbol sumType, Symbol symbol, TypeDeclaration syntax, Scope scope)
    {
        PopulateSumTypeMembers(sumType, scope);

        if (syntax.DiscriminantType is not null)
            sumType.DiscriminantType = ResolveType(syntax.DiscriminantType, scope);

        if (syntax.UnionConstituents is { } uConst)
            sumType.UnionConstituents = ResolveTypes(uConst, scope);

        foreach (var variantHandle in sumType.EnumVariants)
        {
            if (context.GetEnumVariantSymbol(variantHandle) is { } vSym)
            {
                if (vSym.ExplicitValue is not null)
                    ResolveExpression(vSym.ExplicitValue, scope, ResolutionContext.GetHandle(symbol));

                var variantScope = GetOwnedScope(vSym);
                foreach (var paramHandle in vSym.Parameters)
                {
                    if (context.GetParameterSymbol(paramHandle) is { } pSym && pSym.Syntax is { } pSyntax)
                        pSym.Type = ResolveType(pSyntax.Declarator.Type, variantScope);
                }
                foreach (var fieldHandle in vSym.Fields)
                {
                    if (context.GetFieldSymbol(fieldHandle) is { } fSym && fSym.Syntax is { } fSyntax)
                        fSym.Type = ResolveType(fSyntax.Type, variantScope);
                }
            }
        }
    }

    private void ResolveAlias(AliasSymbol symbol)
    {
        if (symbol.Syntax is null)
            return;

        var scope = GetOwnedScope(symbol);

        if (scope.Parent == context.GlobalScope && scope.UsingNamespaces.Count == 0)
        {
            foreach (var childNs in context.GlobalNamespace.Next.Values)
                scope.UsingNamespaces.Add(childNs);
        }

        ResolveTypeConstraints(symbol.Syntax.Constraints, symbol.GenericParameters, scope);
        ResolveGenericParameterDeclarations(symbol.Syntax.Name, symbol.GenericParameters);
        var target = ResolveType(symbol.Syntax.Target, scope, reportDiagnostics: !symbol.IsGlobalAlias);

        symbol.HasCompatibleConstraints = target.IsResolved &&
            ((target.IsSpecial && target.SpecialType is not GenericSemanticType) ||
             (target.Handle is { } handle && AliasConstraintsAreCompatible(handle, symbol.Syntax.Target, scope)));
        symbol.Target = symbol.HasCompatibleConstraints ? target : (symbol.IsGlobalAlias && !target.IsResolved ? TypeRef.Unresolved : TypeRef.Error);
    }

    private bool AliasConstraintsAreCompatible(SymbolHandle target, TypeSyntax targetSyntax, Scope scope)
    {
        var targetParameters = GetGenericParameters(target);
        var targetArguments = GetGenericArguments(targetSyntax);

        if (targetParameters.Count == 0)
            return true;
        if (targetArguments.Count != targetParameters.Count)
            return false;

        for (int index = 0; index < targetParameters.Count; index++)
        {
            if (targetParameters[index].Kind is not SymbolKind.GenericParameter)
                return false;

            GenericParameterSymbol parameter = context.GenericParameterSymbols[targetParameters[index].ID];

            if (parameter.ParameterKind is not GenericParameterKind.Type || targetArguments[index] is LiteralGenericArgument)
                continue;

            SymbolHandle? argument = ResolveGenericArgumentAsType(targetArguments[index], scope);

            if (argument is null || !SatisfiesAllConstraints(argument.Value, parameter.Constraints))
                return false;
        }

        return true;
    }

    private IReadOnlyList<SymbolHandle> GetGenericParameters(SymbolHandle handle) => handle.Kind switch
    {
        SymbolKind.Type => context.GetTypeSymbol(handle)?.GenericParameters ?? [],
        SymbolKind.NestedType => context.GetNestedTypeSymbol(handle)?.GenericParameters ?? [],
        SymbolKind.Alias => context.GetAliasSymbol(handle)?.GenericParameters ?? [],
        _ => []
    };

    private static IReadOnlyList<TypeSyntax> GetGenericArguments(TypeSyntax syntax)
    {
        switch (syntax)
        {
            case GenericType generic:
                var arguments = new List<TypeSyntax>(generic.GenericArguments.Count);

                foreach (var argument in generic.GenericArguments)
                    arguments.Add(argument);

                return arguments;
            case QualifiedType qualified:
                return GetGenericArguments(qualified.Right);
            case ModifiedType modified:
                return GetGenericArguments(modified.Type);
            case PrefixModifiedType prefixModified:
                return GetGenericArguments(prefixModified.Type);
            default:
                return [];
        }
    }

    private bool SatisfiesAllConstraints(SymbolHandle candidate, IReadOnlyList<TypeRef> required)
    {
        foreach (var constraint in required)
        {
            if (constraint.IsError)
                continue;

            if (constraint.IsResolved && constraint.Handle is { } constraintHandle && !SatisfiesConstraint(candidate, constraintHandle, []))
                return false;
        }

        return true;
    }

    private bool SatisfiesConstraint(SymbolHandle candidate, SymbolHandle required, HashSet<SymbolHandle> visited)
    {
        if (candidate == required)
            return true;

        if (!visited.Add(candidate))
            return false;

        switch (candidate.Kind)
        {
            case SymbolKind.GenericParameter:
                if (context.GetGenericParameterSymbol(candidate) is { } gp)
                {
                    foreach (var constraint in gp.Constraints)
                        if (constraint.IsResolved && constraint.Handle is { } constraintHandle && SatisfiesConstraint(constraintHandle, required, visited))
                            return true;
                }
                break;
            case SymbolKind.Type:
                if (context.GetTypeSymbol(candidate) is { } typeSym)
                {
                    foreach (var baseType in typeSym.BaseTypes)
                        if (baseType.IsResolved && baseType.Handle is { } baseHandle && SatisfiesConstraint(baseHandle, required, visited))
                            return true;
                }
                break;
            case SymbolKind.NestedType:
                if (context.GetNestedTypeSymbol(candidate) is { } nestedTypeSym)
                {
                    foreach (var baseType in nestedTypeSym.BaseTypes)
                        if (baseType.IsResolved && baseType.Handle is { } baseHandle && SatisfiesConstraint(baseHandle, required, visited))
                            return true;
                }
                break;
            case SymbolKind.Alias when context.GetAliasSymbol(candidate)?.Target is { IsResolved: true, Handle: { } target }:
                return SatisfiesConstraint(target, required, visited);
        }

        return false;
    }

}
