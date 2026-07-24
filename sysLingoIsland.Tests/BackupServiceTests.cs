using System.IO;
using System.IO.Compression;
using LingoIsland.Query;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>資料備份與搬遷（BackupService，#206）：整包 zip 匯出／匯入 roundtrip、備份識別（防誤選任意 zip）、預設檔名。</summary>
public class BackupServiceTests
{
    private static string NewTempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"lingo-bk-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void DefaultBackupFileName_StampsDate()
    {
        var name = BackupService.DefaultBackupFileName(new DateTimeOffset(2026, 7, 23, 21, 5, 0, TimeSpan.Zero));
        Assert.Equal("LingoIsland-backup-20260723-2105.zip", name);
    }

    [Fact]
    public void IsLingoBackup_CoreFileAtRoot_True_OtherwiseFalse()
    {
        Assert.True(BackupService.IsLingoBackup(new[] { "notes.json", "themes/a.png" }));
        Assert.True(BackupService.IsLingoBackup(new[] { "appsettings.json" }));
        Assert.False(BackupService.IsLingoBackup(new[] { "readme.txt", "photos/cat.jpg" })); // 任意 zip → 拒
        Assert.False(BackupService.IsLingoBackup(Array.Empty<string>()));
    }

    [Fact]
    public void CreateAndRestore_Roundtrip_PreservesFilesAndSubdirs()
    {
        var src = NewTempDir(); var dst = NewTempDir();
        var zip = Path.Combine(Path.GetTempPath(), $"lingo-bk-{Guid.NewGuid():N}.zip");
        try
        {
            File.WriteAllText(Path.Combine(src, "notes.json"), """{"n":1}""");
            Directory.CreateDirectory(Path.Combine(src, "themes"));
            File.WriteAllText(Path.Combine(src, "themes", "t.json"), "theme");

            BackupService.CreateBackup(src, zip);
            File.WriteAllText(Path.Combine(dst, "history.json"), "keep");   // 目的地既有非同名檔 → 保留
            File.WriteAllText(Path.Combine(dst, "notes.json"), "stale");    // 同名檔 → 覆蓋
            BackupService.RestoreBackup(zip, dst);

            Assert.Equal("""{"n":1}""", File.ReadAllText(Path.Combine(dst, "notes.json")));
            Assert.Equal("theme", File.ReadAllText(Path.Combine(dst, "themes", "t.json")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(dst, "history.json")));
        }
        finally
        {
            Directory.Delete(src, true); Directory.Delete(dst, true);
            if (File.Exists(zip)) { File.Delete(zip); }
        }
    }

    [Fact]
    public void RestoreBackup_NonLingoZip_ThrowsAndLeavesTargetUntouched()
    {
        var src = NewTempDir(); var dst = NewTempDir();
        var zip = Path.Combine(Path.GetTempPath(), $"lingo-bk-{Guid.NewGuid():N}.zip");
        try
        {
            File.WriteAllText(Path.Combine(src, "random.txt"), "junk");
            ZipFile.CreateFromDirectory(src, zip);
            File.WriteAllText(Path.Combine(dst, "notes.json"), "mine");

            Assert.Throws<InvalidDataException>(() => BackupService.RestoreBackup(zip, dst));
            Assert.Equal("mine", File.ReadAllText(Path.Combine(dst, "notes.json"))); // 資料未動
            Assert.False(File.Exists(Path.Combine(dst, "random.txt")));
        }
        finally
        {
            Directory.Delete(src, true); Directory.Delete(dst, true);
            if (File.Exists(zip)) { File.Delete(zip); }
        }
    }

    [Fact]
    public void CreateBackup_TargetInsideSourceDir_DoesNotEatItself()
    {
        var src = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(src, "appsettings.json"), "cfg");
            var zip = Path.Combine(src, "backup.zip"); // 目的地選在資料夾自身內
            BackupService.CreateBackup(src, zip);
            using var z = ZipFile.OpenRead(zip);
            Assert.DoesNotContain(z.Entries, e => e.FullName.EndsWith("backup.zip")); // 先壓暫存再搬置 → zip 不含自己
            Assert.Contains(z.Entries, e => e.FullName == "appsettings.json");
        }
        finally { Directory.Delete(src, true); }
    }

    // ---- 電子書藏書納入整包備份（spec#6） ----

    [Fact]
    public void IsLingoBackup_RecognizesEbooksJson()
    {
        Assert.True(BackupService.IsLingoBackup(new[] { "ebooks.json" }));
        Assert.True(BackupService.IsLingoBackup(new[] { "ebook/20260724 A/info.json", "ebooks.json" }));
    }

    [Fact]
    public void Roundtrip_CoversEbooksJsonAndEbookFolder()
    {
        var src = NewTempDir(); var dst = NewTempDir();
        var zip = Path.Combine(Path.GetTempPath(), $"lingo-bk-{Guid.NewGuid():N}.zip");
        try
        {
            // 模擬藏書：ebooks.json＋每本一資料夾（info.json＋原始 .epub 複本＋封面）
            File.WriteAllText(Path.Combine(src, "ebooks.json"), """{"Items":[{"Id":"x","Title":"A"}]}""");
            var book = Path.Combine(src, "ebook", "20260724 A");
            Directory.CreateDirectory(book);
            File.WriteAllText(Path.Combine(book, "info.json"), """{"Title":"A"}""");
            File.WriteAllText(Path.Combine(book, "A.epub"), "epub-bytes");
            File.WriteAllBytes(Path.Combine(book, "cover.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            BackupService.CreateBackup(src, zip);
            BackupService.RestoreBackup(zip, dst);

            var rbook = Path.Combine(dst, "ebook", "20260724 A");
            Assert.Equal("""{"Items":[{"Id":"x","Title":"A"}]}""", File.ReadAllText(Path.Combine(dst, "ebooks.json")));
            Assert.True(File.Exists(Path.Combine(rbook, "info.json")));
            Assert.True(File.Exists(Path.Combine(rbook, "A.epub")));               // 原始 .epub 複本一併帶走
            Assert.True(File.Exists(Path.Combine(rbook, "cover.png")));            // 封面一併帶走
        }
        finally
        {
            Directory.Delete(src, true); Directory.Delete(dst, true);
            if (File.Exists(zip)) { File.Delete(zip); }
        }
    }
}
