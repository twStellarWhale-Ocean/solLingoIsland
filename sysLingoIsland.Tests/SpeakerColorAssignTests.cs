using System;
using System.IO;
using LingoIsland.Query;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>
/// Issue #294：說話人清單右鍵指定顏色之**純函式**契約——邊界比對（取代裸 Contains）、
/// 指派時之同名正規化（自其他色槽移除）、清除、以及目前所屬色槽之查詢。
/// UI（右鍵選單喚起、清單顏色實際改變）由 <c>test/scripts/captureSpeakerColorAssign.ps1</c> 於實機驗。
/// </summary>
public class SpeakerColorAssignTests
{
    private static ThemeItem NewTheme()
    {
        var t = new ThemeItem { Name = "T" };
        ThemeColors.Ensure(t);
        return t;
    }

    // ---- DescribesSpeaker：邊界比對 ----

    [Theory]
    [InlineData("Ann", "Ann", true)]
    [InlineData("ann 的顏色", "Ann", true)]          // 大小寫不敏感
    [InlineData("旁白，Ann", "Ann", true)]           // 逗號分段
    [InlineData("Annie", "Ann", false)]              // #294 風險1：不得被較長同前綴名吃掉
    [InlineData("Mary-Ann", "Ann", true)]            // 連字號非字內字元→仍算邊界
    [InlineData("xAnn", "Ann", false)]
    [InlineData("Ann2", "Ann", false)]
    [InlineData("小明的顏色", "小明", true)]          // CJK 不套拉丁字邊界（否則既有中文描述整批失效）
    [InlineData("", "Ann", false)]
    [InlineData("Ann", "", false)]
    [InlineData(null, "Ann", false)]
    public void DescribesSpeaker_邊界比對(string? desc, string name, bool expected)
        => Assert.Equal(expected, ThemeStore.DescribesSpeaker(desc, name));

    [Fact]
    public void DescribesSpeaker_同描述中前有較長同名後有正確名_仍命中()
        => Assert.True(ThemeStore.DescribesSpeaker("Annie, Ann", "Ann"));

    // ---- AssignSpeakerColor：指派與正規化 ----

    [Fact]
    public void 指派_名字寫入目標色槽描述()
    {
        var t = NewTheme();
        Assert.True(ThemeStore.AssignSpeakerColor(t, "Ann", 3));
        Assert.Equal("Ann", t.Colors[3].Description);
        Assert.Equal(3, ThemeStore.SlotOfSpeaker(t, "Ann"));
    }

    [Fact]
    public void 指派_保留目標色槽原有描述並以逗號相接()
    {
        var t = NewTheme();
        t.Colors[2].Description = "主角";
        ThemeStore.AssignSpeakerColor(t, "Ann", 2);
        Assert.Equal("主角，Ann", t.Colors[2].Description);
    }

    [Fact]
    public void 改指派_自舊色槽移除同名_只留一處()
    {
        var t = NewTheme();
        ThemeStore.AssignSpeakerColor(t, "Ann", 1);
        ThemeStore.AssignSpeakerColor(t, "Ann", 7);
        Assert.Equal("", t.Colors[1].Description);
        Assert.Equal("Ann", t.Colors[7].Description);
        Assert.Equal(7, ThemeStore.SlotOfSpeaker(t, "Ann"));
    }

    [Fact]
    public void 改指派_不動同槽其他人之描述()
    {
        var t = NewTheme();
        t.Colors[1].Description = "Ann，Ben";
        ThemeStore.AssignSpeakerColor(t, "Ann", 5);
        Assert.Equal("Ben", t.Colors[1].Description);
        Assert.Equal("Ann", t.Colors[5].Description);
        Assert.Equal(1, ThemeStore.SlotOfSpeaker(t, "Ben"));
    }

    [Fact]
    public void 指派_不誤刪同前綴之他人描述()
    {
        var t = NewTheme();
        t.Colors[0].Description = "Annie";
        ThemeStore.AssignSpeakerColor(t, "Ann", 4);
        Assert.Equal("Annie", t.Colors[0].Description);   // Annie 不因指派 Ann 而被清掉
        Assert.Equal("Ann", t.Colors[4].Description);
    }

    [Fact]
    public void 清除_負值槽號只移除不指派()
    {
        var t = NewTheme();
        ThemeStore.AssignSpeakerColor(t, "Ann", 6);
        Assert.True(ThemeStore.AssignSpeakerColor(t, "Ann", -1));
        Assert.Equal(-1, ThemeStore.SlotOfSpeaker(t, "Ann"));
        Assert.All(t.Colors, c => Assert.False(ThemeStore.DescribesSpeaker(c.Description, "Ann")));
    }

    [Fact]
    public void 指派_主題為null或名字空白_不變更()
    {
        Assert.False(ThemeStore.AssignSpeakerColor(null, "Ann", 1));
        Assert.False(ThemeStore.AssignSpeakerColor(NewTheme(), "  ", 1));
    }

    [Fact]
    public void 指派_槽號超界_僅執行清除()
    {
        var t = NewTheme();
        ThemeStore.AssignSpeakerColor(t, "Ann", 2);
        ThemeStore.AssignSpeakerColor(t, "Ann", 99);
        Assert.Equal(-1, ThemeStore.SlotOfSpeaker(t, "Ann"));
    }

    [Fact]
    public void 指派_恆補滿十二槽()
    {
        var t = new ThemeItem { Name = "空槽主題" };
        ThemeStore.AssignSpeakerColor(t, "Ann", 11);
        Assert.Equal(ThemeColors.Count, t.Colors.Count);
        Assert.Equal("Ann", t.Colors[11].Description);
    }

    [Fact]
    public void 所屬色槽_無hex之槽不計()
    {
        var t = NewTheme();
        t.Colors[0].Hex = "";
        t.Colors[0].Description = "Ann";
        Assert.Equal(-1, ThemeStore.SlotOfSpeaker(t, "Ann"));
    }

    // ---- 存回後推送（與 UI 之接點） ----

    [Fact]
    public void 指派後存回_觸發Changed推送()
    {
        var path = Path.Combine(Path.GetTempPath(), "li-294-" + Guid.NewGuid().ToString("N"), "themes.json");
        var store = new ThemeStore(path);
        var d = new ThemesData();
        var item = ThemeStore.Add(d, "T");
        store.Save(d);

        var fired = 0;
        store.Changed += () => fired++;

        var loaded = store.Load();
        ThemeStore.AssignSpeakerColor(ThemeStore.Find(loaded, item.Id), "Ann", 3);
        Assert.True(store.TrySave(loaded, out _));
        Assert.Equal(1, fired);

        var reread = store.Load();
        Assert.Equal(3, ThemeStore.SlotOfSpeaker(ThemeStore.Find(reread, item.Id), "Ann"));

        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}
