using LingoIsland.Ebook;
using Xunit;
using Hop = LingoIsland.Ebook.ChapterHopDecider.Hop;
using HopKey = LingoIsland.Ebook.ChapterHopDecider.HopKey;

namespace LingoIsland.Tests;

/// <summary>
/// 章界雙擊換章判定（design intTest#71／#267）：純函式全分支——章界內外、門檻內外、換鍵中斷、
/// 空章、以及 <see cref="ChapterHopState"/> 之首次按鍵與換章後重置（防連跳兩章）。
/// </summary>
public class ChapterHopDeciderTests
{
    /// <summary>測試輔助：以游標／段數推出「兩下皆在該位置」之章界旗標後呼叫判定（模擬章界處按鍵游標不動之實況）。</summary>
    private static Hop DecideAt(HopKey key, HopKey lastKey, long elapsedMs, int cursor, int cueCount)
        => ChapterHopDecider.Decide(key, lastKey, elapsedMs,
               ChapterHopDecider.AtBoundary(key, cursor, cueCount),
               ChapterHopDecider.AtBoundary(lastKey, cursor, cueCount));

    // ── 章末雙擊 → 下一章（Space／Down 皆然）────────────────────────────────
    [Theory]
    [InlineData(HopKey.Down)]
    [InlineData(HopKey.Space)]
    public void 章末於門檻內連按兩下同鍵_進下一章(HopKey key)
    {
        Assert.Equal(Hop.NextChapter, DecideAt(key, key, elapsedMs: 200, cursor: 4, cueCount: 5));
    }

    [Fact]
    public void 章首連按兩下上鍵_回上一章()
    {
        Assert.Equal(Hop.PrevChapter, DecideAt(HopKey.Up, HopKey.Up, elapsedMs: 200, cursor: 0, cueCount: 5));
    }

    // ── 章中不吃雙擊（單擊語意不變，本增量之核心邊界）──────────────────────
    [Theory]
    [InlineData(HopKey.Down)]
    [InlineData(HopKey.Space)]
    public void 章中連按兩下_不換章(HopKey key)
    {
        Assert.Equal(Hop.None, DecideAt(key, key, elapsedMs: 100, cursor: 2, cueCount: 5));
    }

    [Fact]
    public void 非章首連按兩下上鍵_不換章()
    {
        Assert.Equal(Hop.None, DecideAt(HopKey.Up, HopKey.Up, elapsedMs: 100, cursor: 3, cueCount: 5));
    }

    [Fact]
    public void 章末連按兩下上鍵_不換章_方向不符()
    {
        Assert.Equal(Hop.None, DecideAt(HopKey.Up, HopKey.Up, elapsedMs: 100, cursor: 4, cueCount: 5));
    }

    [Fact]
    public void 章首連按兩下下鍵_不換章_方向不符()
    {
        Assert.Equal(Hop.None, DecideAt(HopKey.Down, HopKey.Down, elapsedMs: 100, cursor: 0, cueCount: 5));
    }

    // ── 門檻邊界 ────────────────────────────────────────────────────────────
    [Fact]
    public void 恰在門檻上_仍成立()
    {
        Assert.Equal(Hop.NextChapter,
            DecideAt(HopKey.Down, HopKey.Down, ChapterHopDecider.DoubleTapWindowMs, cursor: 4, cueCount: 5));
    }

    [Fact]
    public void 超過門檻_不成立()
    {
        Assert.Equal(Hop.None,
            DecideAt(HopKey.Down, HopKey.Down, ChapterHopDecider.DoubleTapWindowMs + 1, cursor: 4, cueCount: 5));
    }

    [Fact]
    public void 間隔為負_不成立()
    {
        Assert.Equal(Hop.None, DecideAt(HopKey.Down, HopKey.Down, elapsedMs: -1, cursor: 4, cueCount: 5));
    }

    // ── 換鍵即中斷、非參與鍵一律不換章 ──────────────────────────────────────
    [Fact]
    public void 換鍵即中斷_不成雙擊()
    {
        Assert.Equal(Hop.None, DecideAt(HopKey.Down, HopKey.Space, elapsedMs: 100, cursor: 4, cueCount: 5));
    }

    [Fact]
    public void 非參與鍵_一律不換章()
    {
        Assert.Equal(Hop.None, DecideAt(HopKey.Other, HopKey.Other, elapsedMs: 100, cursor: 4, cueCount: 5));
    }

    // ── 空章 ────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void 空章_不換章(int cueCount)
    {
        Assert.Equal(Hop.None, DecideAt(HopKey.Down, HopKey.Down, elapsedMs: 100, cursor: -1, cueCount));
    }

    [Fact]
    public void 單段章_同一段既是章首也是章末()
    {
        Assert.Equal(Hop.NextChapter, DecideAt(HopKey.Down, HopKey.Down, elapsedMs: 100, cursor: 0, cueCount: 1));
        Assert.Equal(Hop.PrevChapter, DecideAt(HopKey.Up, HopKey.Up, elapsedMs: 100, cursor: 0, cueCount: 1));
    }

    [Fact]
    public void 自倒數第二段連按兩下_不跳章_末段不被跳過()
    {
        // 第一下在倒數第二段（非章界）→ 走到章末；第二下雖已在章末，但「上一下不在章界」故不成立。
        // 只驗第二下會讓最後一段被整段跳過——本測即該漏洞之回歸防線。
        var st = new ChapterHopState();
        Assert.Equal(Hop.None, st.Press(HopKey.Down, 1000, cursor: 3, cueCount: 5));  // 第一下：倒數第二段
        Assert.Equal(Hop.None, st.Press(HopKey.Down, 1100, cursor: 4, cueCount: 5));  // 第二下：已到章末，仍不換章
        Assert.Equal(Hop.NextChapter, st.Press(HopKey.Down, 1200, cursor: 4, cueCount: 5)); // 第三下：兩下皆在章末→換章
    }

    // ── ChapterHopState：首次按鍵、序列、換章後重置 ────────────────────────
    [Fact]
    public void 首次按鍵_不成雙擊()
    {
        var st = new ChapterHopState();
        Assert.Equal(Hop.None, st.Press(HopKey.Down, nowTick: 1000, cursor: 4, cueCount: 5));
    }

    [Fact]
    public void 章末連續兩下_第二下換章()
    {
        var st = new ChapterHopState();
        Assert.Equal(Hop.None, st.Press(HopKey.Down, 1000, 4, 5));
        Assert.Equal(Hop.NextChapter, st.Press(HopKey.Down, 1200, 4, 5));
    }

    [Fact]
    public void 換章後重置_第三下不再連跳()
    {
        var st = new ChapterHopState();
        st.Press(HopKey.Down, 1000, 4, 5);
        Assert.Equal(Hop.NextChapter, st.Press(HopKey.Down, 1200, 4, 5));
        st.Reset();                                   // 呼叫端於換章後重置
        // 換章後游標回 0；縱使該章只有一段（0 亦為章末），第三下也因記錄已清而不成雙擊
        Assert.Equal(Hop.None, st.Press(HopKey.Down, 1300, 0, 1));
    }

    [Fact]
    public void 三連按未重置_第三下與第二下另成一次雙擊()
    {
        // 記錄語意驗證：Press 一律更新記錄，故未重置時第三下會與第二下配對。
        // 實機不致連跳——EbookPage 於 GoToChapter 內即 Reset（涵蓋所有換章路徑）。
        var st = new ChapterHopState();
        st.Press(HopKey.Down, 1000, 4, 5);
        Assert.Equal(Hop.NextChapter, st.Press(HopKey.Down, 1100, 4, 5));
        Assert.Equal(Hop.NextChapter, st.Press(HopKey.Down, 1200, 4, 5));
    }

    [Fact]
    public void 中間插入他鍵_中斷雙擊()
    {
        var st = new ChapterHopState();
        st.Press(HopKey.Down, 1000, 4, 5);
        st.Press(HopKey.Other, 1050, 4, 5);           // 例如按了 →
        Assert.Equal(Hop.None, st.Press(HopKey.Down, 1100, 4, 5));
    }
}
