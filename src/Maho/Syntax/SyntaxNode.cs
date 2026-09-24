using System.Diagnostics.CodeAnalysis;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Base class for all syntax nodes in the syntax tree. </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
internal abstract class SyntaxNode
{
    /// <summary> Computes the source span covered by this node when one can be reconstructed. </summary>
    public TextSpan? GetSpan() => SyntaxSpan.GetSpan(this);

    /// <summary> Resolves the backing source for this node from its first token, when available. </summary>
    public SourceText? GetSource() => SyntaxSpan.GetSource(this);

    /// <summary> Gets or sets the macro expansion origin if this node was produced by macro expansion. </summary>
    public ExpansionOrigin? ExpansionOrigin { get; set; }
}