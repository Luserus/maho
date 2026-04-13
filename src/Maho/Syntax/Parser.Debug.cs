using System.Collections.Generic;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Debug serialization helpers for parser output. </summary>
internal sealed partial class Parser
{
    /// <summary> Serializes the parsed syntax tree into the stable debug schema. </summary>
    public override string ToString()
    {
        return DebugJson.Serialize(new DebugParserInfo("parser", Root is null ? null : CreateNodeView(Root)));
    }

    /// <summary> Projects one syntax node into a recursive debug DTO tree. </summary>
    private DebugParserNodeInfo CreateNodeView(SyntaxNode node)
    {
        TextSpan? span = node.GetSpan();
        List<(string Name, SyntaxNode Node)> children = SyntaxSpan.GetChildren(node);
        DebugParserChildInfo[] childItems = new DebugParserChildInfo[children.Count];

        for (int i = 0; i < children.Count; i++)
        {
            var (name, child) = children[i];
            childItems[i] = new DebugParserChildInfo(name, CreateNodeView(child));
        }

        if (node is Token token)
        {
            return new DebugParserNodeInfo(
                node.GetType().Name,
                DebugJson.CreateSpan(text, token.Span),
                token.Kind.ToString(),
                token.Value,
                DebugJson.GetDisplayText(token),
                DebugJson.GetMatchingKind(token.MatchingKind),
                DebugJson.CreateTrivia(text, token.LeadingTrivia),
                DebugJson.CreateTrivia(text, token.TrailingTrivia),
                childItems);
        }

        return new DebugParserNodeInfo(
            node.GetType().Name,
            span is TextSpan concreteSpan ? DebugJson.CreateSpan(text, concreteSpan) : null,
            null,
            null,
            null,
            null,
            null,
            null,
            childItems);
    }
}
