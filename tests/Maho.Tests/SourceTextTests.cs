using Maho.Text;

namespace Maho.Tests;

public sealed class SourceTextTests
{
    [Fact]
    public void InMemoryText_ParsesLinesAndLineIndexes()
    {
        using SourceText text = new("alpha\r\nbeta\n");

        Assert.Equal(3, text.Lines.Length);
        Assert.Equal("alpha", text.Lines[0].ToString());
        Assert.Equal("beta", text.Lines[1].ToString());
        Assert.Equal(string.Empty, text.Lines[2].ToString());
        Assert.Equal(0, text.GetLineIndex(0));
        Assert.Equal(1, text.GetLineIndex(text.Lines[1].Start));
        Assert.Equal(2, text.GetLineIndex(text.Length));
    }

    [Fact]
    public void FileBackedLazyText_LoadsSpansWithoutChangingLineMath()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"maho-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string filePath = Path.Combine(tempDirectory, "sample.mh");
            File.WriteAllText(filePath, "one\ntwo");

            using SourceText text = new(new SourceFile(filePath, SourceTextLoadMode.LazyCached));

            Assert.Equal(2, text.Lines.Length);
            Assert.Equal('o', text[0]);
            Assert.Equal("one", text.Lines[0].ToString());
            Assert.Equal("wo", text.ToString(new TextSpan(5, 2)));
            Assert.Equal("two", text.AsSpan(new TextSpan(4, 3)).ToString());
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
