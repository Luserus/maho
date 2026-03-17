using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Maho.Syntax;

internal readonly struct SeparatedSyntaxList<T> : IEnumerable<T> where T : SyntaxNode
{
    public IReadOnlyList<SyntaxNode> NodesAndSeparators { get; }

    public SeparatedSyntaxList(IReadOnlyList<SyntaxNode> nodesAndSeparators) => NodesAndSeparators = nodesAndSeparators;

    public int Count => NodesAndSeparators.Count(n => n is T);

    public IReadOnlyList<SyntaxNode> NodesWithSeparators => NodesAndSeparators;

    public T this[int index] => (T)NodesAndSeparators[index * 2];

    public Token? GetSeparator(int index)
    {
        int separatorIndex = index * 2 + 1;

        if (separatorIndex >= NodesAndSeparators.Count)
            return null;

        return (Token)NodesAndSeparators[separatorIndex];
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}