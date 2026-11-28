namespace Maho.Syntax;

/// <summary> Top-level wrapper around an extension declaration. </summary>
internal sealed class TopLevelExtensionDeclaration : TopLevelDeclaration
{
    public ExtensionDeclaration Extension { get; }

    public TopLevelExtensionDeclaration(ExtensionDeclaration extension) => Extension = extension;
}

