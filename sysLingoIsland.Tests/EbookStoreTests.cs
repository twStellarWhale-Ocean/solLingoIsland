using System.IO;
using LingoIsland.Ebook;
using LingoIsland.Query;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>
/// 電子書書櫃儲存（[EbookStore]，spec#5／#6）：加入去重、每本一資料夾落地（info.json＋原始 .epub 複本＋封面）、
/// 資料夾撞名 Id 消歧、四鍵排序正反向、Remove/Clear、降級、跨啟動 roundtrip。清單/去重/排序純函式不觸檔案。
/// </summary>
public class EbookStoreTests
{
    private static string NewTempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"lingo-ebk-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private static (EbookStore store, string json, string root) NewStore(string dir)
    {
        var json = Path.Combine(dir, "ebooks.json");
        var root = Path.Combine(dir, "ebook");
        return (new EbookStore(json, root), json, root);
    }

    private static EbookInfo Info(string identifier, string title, string author = "Auth",
        string lang = "en", int chapters = 1, byte[]? cover = null, string? id = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString("N"),
        Identifier = identifier,
        Title = title,
        Author = author,
        Language = lang,
        ChapterCount = chapters,
        CoverBytes = cover,
    };

    private static string DummyEpub(string dir)
    {
        var p = Path.Combine(dir, $"src-{Guid.NewGuid():N}.epub");
        File.WriteAllText(p, "dummy-epub-bytes");
        return p;
    }

    private static readonly DateTimeOffset D = new(2026, 7, 24, 10, 0, 0, TimeSpan.Zero);

    // ---- Add：落地資料夾（info.json＋原始 .epub 複本＋封面） ----

    [Fact]
    public void Add_LandsFolder_InfoEpubCover_AndListsItem()
    {
        var dir = NewTempDir();
        try
        {
            var (store, json, root) = NewStore(dir);
            var src = DummyEpub(dir);
            var res = store.Add(Info("urn:isbn:1", "My Book", "AuthorX", "en", 5, EpubTestFixtures.SamplePng), src, "th", "Theme A", D);

            Assert.True(res.Added);
            var item = res.Item;
            Assert.Equal("My Book", item.Title);
            Assert.Equal("cover.png", item.CoverFile);
            Assert.Equal("th", item.ThemeId);
            Assert.Equal("Theme A", item.ThemeName);

            var folder = Path.Combine(root, item.Folder);
            Assert.True(Directory.Exists(folder));
            Assert.StartsWith("20260724 My Book", item.Folder);

            var infoJson = Path.Combine(folder, "info.json");
            Assert.True(File.Exists(infoJson));
            Assert.True(File.Exists(Path.Combine(folder, "cover.png")));
            Assert.True(File.Exists(Path.Combine(folder, Path.GetFileName(src))));  // 原始 .epub 複本

            var infoText = File.ReadAllText(infoJson);
            Assert.Contains("My Book", infoText);
            Assert.DoesNotContain("CoverBytes", infoText);                          // 封面 bytes 不入 info.json

            Assert.True(File.Exists(json));                                          // 書櫃清單落地
            Assert.Single(store.Load().Items);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Add_NoCover_CoverFileNull_NoCoverWritten()
    {
        var dir = NewTempDir();
        try
        {
            var (store, _, root) = NewStore(dir);
            var res = store.Add(Info("id-x", "NoCover", cover: null), DummyEpub(dir), null, null, D);
            Assert.Null(res.Item.CoverFile);
            var folder = Path.Combine(root, res.Item.Folder);
            Assert.False(File.Exists(Path.Combine(folder, "cover.png")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Add_SourceOriginalFile_NotModified()
    {
        var dir = NewTempDir();
        try
        {
            var (store, _, _) = NewStore(dir);
            var src = DummyEpub(dir);
            var before = File.ReadAllText(src);
            store.Add(Info("id-ro", "ReadOnly Src"), src, null, null, D);
            Assert.Equal(before, File.ReadAllText(src));   // 唯讀原檔、不改寫
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- 去重（dc:identifier 正規化、跨全櫃） ----

    [Fact]
    public void Add_DuplicateIdentifier_NotAddedTwice()
    {
        var dir = NewTempDir();
        try
        {
            var (store, _, root) = NewStore(dir);
            var first = store.Add(Info("urn:isbn:100", "Book One"), DummyEpub(dir), null, null, D);
            // 同 identifier（大小寫/空白不同）→ 視為同書、不重複入櫃
            var dup = store.Add(Info("  URN:ISBN:100 ", "Book One Again"), DummyEpub(dir), null, null, D);

            Assert.True(first.Added);
            Assert.False(dup.Added);
            Assert.Equal(first.Item.Id, dup.Item.Id);              // 回既有書卡
            Assert.Single(store.Load().Items);
            Assert.Single(Directory.GetDirectories(root));         // 只落地一夾
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- 資料夾撞名（同日同標題、不同 dc:identifier）→ Id 短碼消歧 ----

    [Fact]
    public void Add_SameDaySameTitle_DifferentBooks_FolderDisambiguatedById()
    {
        var dir = NewTempDir();
        try
        {
            var (store, _, root) = NewStore(dir);
            var a = store.Add(Info("id-A", "Same Title", id: "aaaaaaaa11112222"), DummyEpub(dir), null, null, D);
            var b = store.Add(Info("id-B", "Same Title", id: "bbbbbbbb33334444"), DummyEpub(dir), null, null, D);

            Assert.NotEqual(a.Item.Folder, b.Item.Folder);              // 不互相覆寫
            Assert.Equal("20260724 Same Title", a.Item.Folder);
            Assert.Equal("20260724 Same Title bbbbbbbb", b.Item.Folder); // 綴書卡 Id 短碼
            Assert.Equal(2, Directory.GetDirectories(root).Length);
            Assert.Equal(2, store.Load().Items.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- Remove（刪本連資料夾）／Clear ----

    [Fact]
    public void Remove_DeletesItemAndFolder()
    {
        var dir = NewTempDir();
        try
        {
            var (store, _, root) = NewStore(dir);
            var it = store.Add(Info("id-r", "To Remove"), DummyEpub(dir), null, null, D).Item;
            var folder = Path.Combine(root, it.Folder);
            Assert.True(Directory.Exists(folder));

            var removed = store.Remove(it.Id);
            Assert.NotNull(removed);
            Assert.Empty(store.Load().Items);
            Assert.False(Directory.Exists(folder));           // 連資料夾刪除
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Clear_EmptiesListAndDeletesRoot()
    {
        var dir = NewTempDir();
        try
        {
            var (store, _, root) = NewStore(dir);
            store.Add(Info("id-1", "A"), DummyEpub(dir), null, null, D);
            store.Add(Info("id-2", "B"), DummyEpub(dir), null, null, D);
            Assert.Equal(2, store.Load().Items.Count);

            store.Clear();
            Assert.Empty(store.Load().Items);
            Assert.False(Directory.Exists(root));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- 跨啟動 roundtrip ----

    [Fact]
    public void Roundtrip_NewStoreInstance_RestoresShelf()
    {
        var dir = NewTempDir();
        try
        {
            var json = Path.Combine(dir, "ebooks.json");
            var root = Path.Combine(dir, "ebook");
            var it = new EbookStore(json, root).Add(Info("id-persist", "Persisted", "PA", "de", 7), DummyEpub(dir), "t1", "T1", D).Item;

            var reloaded = new EbookStore(json, root).Load();          // 另一實例（模擬重啟）
            Assert.Single(reloaded.Items);
            var r = reloaded.Items[0];
            Assert.Equal(it.Id, r.Id);
            Assert.Equal("Persisted", r.Title);
            Assert.Equal("de", r.Language);
            Assert.Equal(7, r.ChapterCount);
            Assert.Equal("T1", r.ThemeName);
            Assert.Equal(it.Folder, r.Folder);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- 降級（讀寫失敗退空/不致命） ----

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var dir = NewTempDir();
        try
        {
            var (store, _, _) = NewStore(dir);
            Assert.Empty(store.Load().Items);                 // 缺檔退空結構
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_CorruptJson_ReturnsEmpty()
    {
        var dir = NewTempDir();
        try
        {
            var json = Path.Combine(dir, "ebooks.json");
            File.WriteAllText(json, "{ this is not valid json ]");
            Assert.Empty(new EbookStore(json, Path.Combine(dir, "ebook")).Load().Items);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Save_UnwritablePath_DoesNotThrow()
    {
        var dir = NewTempDir();
        try
        {
            var asFile = Path.Combine(dir, "blocker");
            File.WriteAllText(asFile, "x");                   // 以檔擋住路徑，建目錄將失敗
            var store = new EbookStore(Path.Combine(asFile, "sub", "ebooks.json"), Path.Combine(dir, "ebook"));
            var ex = Record.Exception(() => store.Save(new EbooksData { Items = { new EbookItem { Title = "X" } } }));
            Assert.Null(ex);                                  // 寫入失敗靜默降級、不擲例外
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- 主題重指派／排序偏好跨啟動沿用 ----

    [Fact]
    public void UpdateTheme_ReassignsAndPersists()
    {
        var dir = NewTempDir();
        try
        {
            var (store, _, _) = NewStore(dir);
            var it = store.Add(Info("id-t", "Book"), DummyEpub(dir), null, null, D).Item;
            store.UpdateTheme(it.Id, "th9", "Theme Nine");
            var reloaded = store.Load().Items[0];
            Assert.Equal("th9", reloaded.ThemeId);
            Assert.Equal("Theme Nine", reloaded.ThemeName);
            store.UpdateTheme(it.Id, null, null);             // 改為未歸屬
            Assert.Null(store.Load().Items[0].ThemeId);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UpdateSort_PersistsAcrossReload()
    {
        var dir = NewTempDir();
        try
        {
            var json = Path.Combine(dir, "ebooks.json");
            var root = Path.Combine(dir, "ebook");
            var store = new EbookStore(json, root);
            store.Add(Info("id-s", "Book"), DummyEpub(dir), null, null, D);
            store.UpdateSort(new EbookSort { Mode = "Title", TitleAsc = false });

            var sort = new EbookStore(json, root).Load().Sort;   // 另一實例（模擬重啟）
            Assert.NotNull(sort);
            Assert.Equal("Title", sort!.Mode);
            Assert.False(sort.TitleAsc);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- 純函式：FindByKey / SetTheme ----

    [Fact]
    public void FindByKey_MatchesNormalizedIdentifier_EmptyKeyNever()
    {
        var d = new EbooksData { Items = { new EbookItem { Id = "1", DcIdentifier = "urn:X" } } };
        Assert.NotNull(EbookStore.FindByKey(d, EbookReader.DedupeKey("URN:x")));   // 正規化後相符
        Assert.Null(EbookStore.FindByKey(d, ""));                                   // 空 key 不誤併
        Assert.Null(EbookStore.FindByKey(d, EbookReader.DedupeKey("other")));
    }

    [Fact]
    public void SetTheme_AssignsClearsAndBlankToNull()
    {
        var d = new EbooksData { Items = { new EbookItem { Id = "1" } } };
        Assert.True(EbookStore.SetTheme(d, "1", "th", "Theme"));
        Assert.Equal("Theme", d.Items[0].ThemeName);
        Assert.True(EbookStore.SetTheme(d, "1", null, null));
        Assert.Null(d.Items[0].ThemeName);
        Assert.True(EbookStore.SetTheme(d, "1", "th", "   "));
        Assert.Null(d.Items[0].ThemeName);                    // 空白→null
        Assert.False(EbookStore.SetTheme(d, "nope", "x", "X"));
    }

    // ---- 純函式：CoverExtension ----

    [Fact]
    public void CoverExtension_DetectsByMagicBytes()
    {
        Assert.Equal(".png", EbookStore.CoverExtension(EpubTestFixtures.SamplePng));
        Assert.Equal(".jpg", EbookStore.CoverExtension(EpubTestFixtures.SampleJpegMagic));
        Assert.Equal(".gif", EbookStore.CoverExtension(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }));
        Assert.Equal(".png", EbookStore.CoverExtension(new byte[] { 1, 2, 3, 4 }));   // 不明→.png
        Assert.Equal(".png", EbookStore.CoverExtension(new byte[] { 1 }));            // 過短→.png
        Assert.Equal(".png", EbookStore.CoverExtension(null));
    }

    // ---- 純函式：SortEbooks（四鍵正反向、主題未歸屬排末、穩定、不動輸入） ----

    private static EbookItem E(string title, string author = "", string? theme = null) =>
        new() { Title = title, Author = author, ThemeName = theme };

    private static EbookSort S(string mode, bool asc) => mode switch
    {
        "Title" => new EbookSort { Mode = mode, TitleAsc = asc },
        "Author" => new EbookSort { Mode = mode, AuthorAsc = asc },
        "Theme" => new EbookSort { Mode = mode, ThemeAsc = asc },
        _ => new EbookSort { Mode = mode, AddedAsc = asc },
    };

    [Fact]
    public void SortEbooks_Added_InsertionOrder_AscReverses()
    {
        var items = new[] { E("b"), E("a"), E("c") };
        Assert.Equal(new[] { "b", "a", "c" }, EbookStore.SortEbooks(items, null).Select(i => i.Title));                  // null＝預設 新→舊
        Assert.Equal(new[] { "b", "a", "c" }, EbookStore.SortEbooks(items, S("Added", false)).Select(i => i.Title));
        Assert.Equal(new[] { "c", "a", "b" }, EbookStore.SortEbooks(items, S("Added", true)).Select(i => i.Title));     // 反向＝舊→新
        Assert.Equal(new[] { "b", "a", "c" }, EbookStore.SortEbooks(items, S("Bogus", false)).Select(i => i.Title));    // 未知模式退預設
    }

    [Fact]
    public void SortEbooks_Title_NaturalCaseInsensitive_BothDirections()
    {
        var items = new[] { E("e10"), E("E2"), E("apple") };
        Assert.Equal(new[] { "apple", "E2", "e10" }, EbookStore.SortEbooks(items, S("Title", true)).Select(i => i.Title));
        Assert.Equal(new[] { "e10", "E2", "apple" }, EbookStore.SortEbooks(items, S("Title", false)).Select(i => i.Title));
    }

    [Fact]
    public void SortEbooks_Author_NaturalCaseInsensitive_BothDirections()
    {
        var items = new[] { E("x", "Zadie"), E("y", "adams"), E("z", "Brontë") };
        Assert.Equal(new[] { "adams", "Brontë", "Zadie" }, EbookStore.SortEbooks(items, S("Author", true)).Select(i => i.Author));
        Assert.Equal(new[] { "Zadie", "Brontë", "adams" }, EbookStore.SortEbooks(items, S("Author", false)).Select(i => i.Author));
    }

    [Fact]
    public void SortEbooks_Theme_GroupsByName_UnassignedLast_StableWithin()
    {
        var items = new[] { E("z", theme: "Zoo"), E("n1"), E("a2", theme: "Apple"), E("a1", theme: "Apple"), E("n2") };
        Assert.Equal(new[] { "a2", "a1", "z", "n1", "n2" }, EbookStore.SortEbooks(items, S("Theme", true)).Select(i => i.Title));
        Assert.Equal(new[] { "z", "a2", "a1", "n1", "n2" }, EbookStore.SortEbooks(items, S("Theme", false)).Select(i => i.Title)); // 反向：組序翻、組內序不翻、未歸屬仍末
    }

    [Fact]
    public void SortEbooks_DoesNotMutateInput()
    {
        var items = new List<EbookItem> { E("b"), E("a") };
        EbookStore.SortEbooks(items, S("Title", true));
        Assert.Equal(new[] { "b", "a" }, items.Select(i => i.Title));   // 呈現層投影、原清單不動
    }

    [Fact]
    public void EbookSort_CurrentAscending_FollowsMode()
    {
        Assert.False(new EbookSort().CurrentAscending);                                   // 預設 Added 新→舊
        Assert.True(new EbookSort { Mode = "Title" }.CurrentAscending);
        Assert.True(new EbookSort { Mode = "Author" }.CurrentAscending);
        Assert.False(new EbookSort { Mode = "Theme", ThemeAsc = false }.CurrentAscending);
    }

    [Fact]
    public void EbooksData_OldJsonWithoutSort_DeserializesToDefault()
    {
        var json = """{"Items":[{"Id":"x","DcIdentifier":"i","Title":"A","AddedAt":"t"}]}""";
        var d = System.Text.Json.JsonSerializer.Deserialize<EbooksData>(json)!;
        Assert.Null(d.Sort);                                   // 舊檔無 Sort 鍵＝預設
        Assert.Single(d.Items);
        Assert.Equal("A", d.Items[0].Title);
    }

    // ---- 閱讀進度（增量2 spec#7）：Set/Get roundtrip、舊檔預設 0/0、降級、不動排序/插入序 ----

    [Fact]
    public void ReadingProgress_SetGet_RoundtripAcrossReload()
    {
        var dir = NewTempDir();
        try
        {
            var json = Path.Combine(dir, "ebooks.json");
            var root = Path.Combine(dir, "ebook");
            var it = new EbookStore(json, root).Add(Info("id-rp", "Reader"), DummyEpub(dir), null, null, D).Item;

            new EbookStore(json, root).SetReadingProgress(it.Id, 3, 12);      // 記錄進度
            var (chapter, para) = new EbookStore(json, root).GetReadingProgress(it.Id);  // 另一實例（模擬重啟）
            Assert.Equal(3, chapter);
            Assert.Equal(12, para);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReadingProgress_UnknownId_ReturnsZeroZero_NoCreate()
    {
        var dir = NewTempDir();
        try
        {
            var (store, _, _) = NewStore(dir);
            store.Add(Info("id-a", "A"), DummyEpub(dir), null, null, D);
            store.SetReadingProgress("no-such-id", 5, 5);   // 無此 id → 不建立、不擲例外
            Assert.Equal((0, 0), store.GetReadingProgress("no-such-id"));
            Assert.Single(store.Load().Items);              // 未新增任何項
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReadingProgress_OldFileWithoutKeys_DefaultsZeroZero()
    {
        var dir = NewTempDir();
        try
        {
            var json = Path.Combine(dir, "ebooks.json");
            // 舊檔（無 LastReadChapter/LastReadParagraph 鍵）→ int 預設 0/0（從頭讀起）
            File.WriteAllText(json, """{"Items":[{"Id":"old","DcIdentifier":"i","Title":"Old","AddedAt":"t"}]}""");
            var store = new EbookStore(json, Path.Combine(dir, "ebook"));
            var item = store.Load().Items[0];
            Assert.Equal(0, item.LastReadChapter);
            Assert.Equal(0, item.LastReadParagraph);
            Assert.Equal((0, 0), store.GetReadingProgress("old"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReadingProgress_CorruptFile_GetReturnsZeroZero()
    {
        var dir = NewTempDir();
        try
        {
            var json = Path.Combine(dir, "ebooks.json");
            File.WriteAllText(json, "{ not valid json ]");
            // Load 退空 → id 找不到 → (0,0)、不致命
            Assert.Equal((0, 0), new EbookStore(json, Path.Combine(dir, "ebook")).GetReadingProgress("any"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReadingProgress_NegativeValues_ClampedToZero()
    {
        var d = new EbooksData { Items = { new EbookItem { Id = "1" } } };
        Assert.True(EbookStore.SetReadingProgress(d, "1", -5, -2));
        Assert.Equal((0, 0), EbookStore.GetReadingProgress(d, "1"));   // 負值夾為 0
    }

    [Fact]
    public void ReadingProgress_PureFunctions_SetGetByIdAcrossShelf()
    {
        var d = new EbooksData { Items = { new EbookItem { Id = "1" }, new EbookItem { Id = "2" } } };
        Assert.True(EbookStore.SetReadingProgress(d, "2", 4, 9));      // 跨全櫃依 id 換置
        Assert.False(EbookStore.SetReadingProgress(d, "nope", 1, 1));  // 無此 id → false
        Assert.Equal((4, 9), EbookStore.GetReadingProgress(d, "2"));
        Assert.Equal((0, 0), EbookStore.GetReadingProgress(d, "1"));   // 未設者仍 0/0
        Assert.Equal((0, 0), EbookStore.GetReadingProgress(d, "nope")); // 無此 id → (0,0)
    }

    [Fact]
    public void ReadingProgress_DoesNotTouchSortOrInsertionOrder()
    {
        var dir = NewTempDir();
        try
        {
            var json = Path.Combine(dir, "ebooks.json");
            var root = Path.Combine(dir, "ebook");
            var store = new EbookStore(json, root);
            var a = store.Add(Info("id-a", "Alpha"), DummyEpub(dir), null, null, D).Item;
            store.Add(Info("id-b", "Beta"), DummyEpub(dir), null, null, D);   // 插最前 → 存序 [Beta, Alpha]
            store.UpdateSort(new EbookSort { Mode = "Title", TitleAsc = false });

            store.SetReadingProgress(a.Id, 2, 7);   // 更新進度

            var reloaded = new EbookStore(json, root).Load();
            Assert.Equal(new[] { "Beta", "Alpha" }, reloaded.Items.Select(i => i.Title));  // 插入序不動
            Assert.Equal("Title", reloaded.Sort!.Mode);                                     // 排序態不動
            Assert.False(reloaded.Sort.TitleAsc);
            Assert.Equal((2, 7), new EbookStore(json, root).GetReadingProgress(a.Id));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReadingProgress_SaveFailure_DoesNotThrow()
    {
        var dir = NewTempDir();
        try
        {
            var asFile = Path.Combine(dir, "blocker");
            File.WriteAllText(asFile, "x");   // 以檔擋住路徑，Save 建目錄將失敗
            var store = new EbookStore(Path.Combine(asFile, "sub", "ebooks.json"), Path.Combine(dir, "ebook"));
            var ex = Record.Exception(() => store.SetReadingProgress("any", 1, 1));
            Assert.Null(ex);   // 寫入失敗靜默降級、不致命
        }
        finally { Directory.Delete(dir, true); }
    }
}
