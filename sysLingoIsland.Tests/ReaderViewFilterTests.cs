using LingoIsland.Ebook;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>#235：閱讀區「只顯示勾選者」可見性純函式（移除右逐段清單後，顯示模式改作用於中閱讀區）。</summary>
public class ReaderViewFilterTests
{
    [Fact]
    public void NonShowSelectedMode_AlwaysVisible()
    {
        Assert.True(ReaderViewFilter.ParaVisible(showSelectedMode: false, speakerChecked: false, isCurrent: false, isHeading: false));
        Assert.True(ReaderViewFilter.ParaVisible(showSelectedMode: false, speakerChecked: true, isCurrent: false, isHeading: false));
    }

    [Fact]
    public void ShowSelected_UncheckedHidden_CheckedVisible()
    {
        Assert.False(ReaderViewFilter.ParaVisible(showSelectedMode: true, speakerChecked: false, isCurrent: false, isHeading: false));
        Assert.True(ReaderViewFilter.ParaVisible(showSelectedMode: true, speakerChecked: true, isCurrent: false, isHeading: false));
    }

    [Fact]
    public void ShowSelected_HeadingAndCurrentAlwaysVisible()
    {
        Assert.True(ReaderViewFilter.ParaVisible(showSelectedMode: true, speakerChecked: false, isCurrent: false, isHeading: true));  // 章節標題＝結構脈絡恆顯
        Assert.True(ReaderViewFilter.ParaVisible(showSelectedMode: true, speakerChecked: false, isCurrent: true, isHeading: false));  // 當前段（游標）恆顯
    }
}
