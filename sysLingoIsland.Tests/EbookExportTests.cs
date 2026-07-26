using LingoIsland.Query;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>#241：匯出 LingoIsland 書本包之建議檔名（純函式；書名安全化＋.lingobook.zip）。</summary>
public class EbookExportTests
{
    [Fact]
    public void SuggestExportFileName_AppendsPackageExtension()
        => Assert.Equal("Peter Pan.lingobook.zip", EbookStore.SuggestExportFileName("Peter Pan"));

    [Fact]
    public void SuggestExportFileName_SanitizesInvalidChars()
    {
        var name = EbookStore.SuggestExportFileName("A/B:C*D?");
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain('*', name);
        Assert.DoesNotContain('?', name);
        Assert.EndsWith(".lingobook.zip", name);
    }

    [Fact]
    public void SuggestExportFileName_EmptyTitleFallsBackToUntitled()
        => Assert.Equal("untitled.lingobook.zip", EbookStore.SuggestExportFileName("   "));
}
