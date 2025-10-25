using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Maho.Syntax;

internal interface ISeparatedSyntaxList
{
    public IReadOnlyList<ISyntaxNode> NodesWithSeparators { get; }
}

internal readonly struct SeparatedSyntaxList<T> : ISeparatedSyntaxList, IEnumerable<T> where T : ISyntaxNode
{
    public IReadOnlyList<ISyntaxNode> NodesAndSeparators { get; }

    public SeparatedSyntaxList(IReadOnlyList<ISyntaxNode> nodesAndSeparators) => NodesAndSeparators = nodesAndSeparators;

    public int Count => NodesAndSeparators.Count(n => n is T);

    public IReadOnlyList<ISyntaxNode> NodesWithSeparators => NodesAndSeparators;

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