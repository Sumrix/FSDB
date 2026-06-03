using System.Collections.Generic;
using FSDB.Collections;

namespace FSDB.Tests;

public class MappedDictionaryViewTests
{
    [Fact]
    public void Index_WhenSourceValueMaps_ReturnsMappedValue()
    {
        var view = CreateView(new Dictionary<string, int>
        {
            ["a"] = 1
        });

        Assert.True(view.TryGetValue("a", out var value));
        Assert.Equal("1", value);
        Assert.Equal("1", view["a"]);
    }

    [Fact]
    public void Index_WhenSourceValueDoesNotMap_ExcludesKey()
    {
        var view = CreateView(new Dictionary<string, int>
        {
            ["a"] = -1
        });

        Assert.False(view.ContainsKey("a"));
        Assert.False(view.TryGetValue("a", out _));
        Assert.Empty(view);
    }

    [Fact]
    public void Enumerate_WhenMixedSourceValues_ReturnsOnlyMappedValues()
    {
        var view = CreateView(new Dictionary<string, int>
        {
            ["a"] = 1,
            ["b"] = -1,
            ["c"] = 2
        });

        Assert.Equal(2, view.Count);
        Assert.Equal(["a", "c"], view.Keys);
        Assert.Equal(["1", "2"], view.Values);
    }

    private static MappedDictionaryView<string, int, string> CreateView(IReadOnlyDictionary<string, int> source)
    {
        return new(source, TryMapPositive);
    }

    private static bool TryMapPositive(int source, out string value)
    {
        if (source > 0)
        {
            value = source.ToString();
            return true;
        }

        value = null!;
        return false;
    }
}
