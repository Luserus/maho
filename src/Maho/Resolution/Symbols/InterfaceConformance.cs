using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// Represents a single scoped interface implementation attached to a type declaration or
/// <c>extension</c> block, carrying visibility, defining module, concrete member bindings,
/// and a unique mangled identity for linker symbol generation and vtable dispatch.
/// </summary>
internal sealed class InterfaceConformance
{
    public int SerialId { get; }
    public TypeRef TargetType { get; }
    public TypeRef InterfaceType { get; }
    public SymbolHandle InterfaceHandle { get; }
    public Accessibility Accessibility { get; }
    public SymbolHandle OwnerHandle { get; }
    public bool IsExtension { get; }
    public string ModuleName { get; }
    public int ModuleProjectId { get; }
    public BaseTypeItemSyntax? Syntax { get; }
    public string MangledConformanceId { get; }

    /// <summary>
    /// Maps each interface member handle (method or property) to the concrete implementing
    /// member handle resolved for this conformance.
    /// </summary>
    public Dictionary<SymbolHandle, SymbolHandle> MethodMap { get; } = [];

    public InterfaceConformance(
        int serialId,
        TypeRef targetType,
        TypeRef interfaceType,
        SymbolHandle interfaceHandle,
        Accessibility accessibility,
        SymbolHandle ownerHandle,
        bool isExtension,
        string moduleName,
        int moduleProjectId,
        BaseTypeItemSyntax? syntax)
    {
        SerialId = serialId;
        TargetType = targetType;
        InterfaceType = interfaceType;
        InterfaceHandle = interfaceHandle;
        Accessibility = accessibility;
        OwnerHandle = ownerHandle;
        IsExtension = isExtension;
        ModuleName = moduleName;
        ModuleProjectId = moduleProjectId;
        Syntax = syntax;

        string visTag = accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            _ => "private"
        };
        MangledConformanceId = $"{moduleName}_{visTag}_{serialId}";
    }
}

