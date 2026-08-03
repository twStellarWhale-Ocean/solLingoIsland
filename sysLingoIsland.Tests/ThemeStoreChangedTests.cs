using System.IO;
using LingoIsland.Query;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>spec#12：主題存放區寫回成功後須發出變更通知，消費頁據此即時重算配色。</summary>
public class ThemeStoreChangedTests
{
    private static (ThemeStore store, string dir) NewStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LingoIslandTest-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return (new ThemeStore(Path.Combine(dir, "themes.json")), dir);
    }

    [Fact]
    public void 寫回成功即發出變更通知()
    {
        var (store, dir) = NewStore();
        try
        {
            var hits = 0;
            store.Changed += () => hits++;
            var d = new ThemesData();
            ThemeStore.Add(d, "T1");

            Assert.True(store.TrySave(d, out _));
            Assert.Equal(1, hits);

            Assert.True(store.TrySave(d, out _));
            Assert.Equal(2, hits);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void 寫回失敗不發出通知免半成品外溢()
    {
        var (store, dir) = NewStore();
        try
        {
            var locked = Path.Combine(dir, "locked", "themes.json");
            Directory.CreateDirectory(Path.GetDirectoryName(locked)!);
            using var hold = File.Open(locked, FileMode.Create, FileAccess.Write, FileShare.None);

            var blocked = new ThemeStore(locked);
            var hits = 0;
            blocked.Changed += () => hits++;

            Assert.False(blocked.TrySave(new ThemesData(), out var err));
            Assert.False(string.IsNullOrWhiteSpace(err));
            Assert.Equal(0, hits);
        }
        finally { Directory.Delete(dir, true); }
    }
}
