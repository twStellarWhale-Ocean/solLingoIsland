using System.IO;
using LingoIsland.Ebook;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>
/// 電子書解析（[modEbook模組] <see cref="EbookReader"/>，spec#4）：EPUB2（ncx）／EPUB3（nav）解析、章數（葉節點）、
/// 封面有/無、壞檔略過（不擲例外）、去重 key 純函式。以程式產最小合法 <c>.epub</c> 夾具注入（檔案 IO 僅 smoke）。
/// </summary>
public class EbookReaderTests
{
    private static string NewTempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"lingo-ebk-parse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    // ---- EPUB3（nav 目錄） ----

    [Fact]
    public async Task ParseAsync_Epub3_ExtractsMetadataChaptersAndCover()
    {
        var dir = NewTempDir();
        try
        {
            var path = EpubTestFixtures.WriteEpub3(dir, "urn:uuid:E3-0001", "Test Book Three", "Ada Lovelace", "en",
                new[] { "Chapter 1", "Chapter 2" }, EpubTestFixtures.SamplePng);

            var r = await EbookReader.ParseAsync(path);

            Assert.True(r.Success, r.Error);
            var info = r.Info!;
            Assert.Equal("urn:uuid:E3-0001", info.Identifier);
            Assert.Equal("Test Book Three", info.Title);
            Assert.Equal("Ada Lovelace", info.Author);
            Assert.Equal("en", info.Language);
            Assert.Equal(2, info.ChapterCount);           // nav 兩葉
            Assert.Equal(2, info.SpineHrefs.Count);        // spine 兩項
            Assert.NotNull(info.CoverBytes);
            Assert.Equal(0x89, info.CoverBytes![0]);       // PNG magic
            Assert.Equal(0x50, info.CoverBytes![1]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ParseAsync_Epub3_NoCover_CoverBytesNull()
    {
        var dir = NewTempDir();
        try
        {
            var path = EpubTestFixtures.WriteEpub3(dir, "id-nocover", "No Cover Book", "Someone", "fr",
                new[] { "Only Chapter" }, cover: null);

            var r = await EbookReader.ParseAsync(path);

            Assert.True(r.Success, r.Error);
            Assert.Null(r.Info!.CoverBytes);               // 缺封面回 null（合法）
            Assert.Equal(1, r.Info!.ChapterCount);
            Assert.Equal("fr", r.Info!.Language);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ParseAsync_Epub3_NoIdentifier_FallsBackToTitleAuthor()
    {
        var dir = NewTempDir();
        try
        {
            // 省略 dc:identifier 值 → 退以「書名|作者」為識別基底
            var path = EpubTestFixtures.WriteEpub3(dir, identifier: null, "Fallback Title", "Fallback Author", "en",
                new[] { "C1" }, cover: null);

            var r = await EbookReader.ParseAsync(path);

            Assert.True(r.Success, r.Error);
            Assert.Equal("Fallback Title|Fallback Author", r.Info!.Identifier);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- EPUB2（ncx 目錄） ----

    [Fact]
    public async Task ParseAsync_Epub2_NcxChapterCountAndMetadata()
    {
        var dir = NewTempDir();
        try
        {
            var path = EpubTestFixtures.WriteEpub2(dir, "978-TEST-EPUB2", "Test Book Two", "Grace Hopper", "en",
                new[] { "Ch A", "Ch B", "Ch C" }, EpubTestFixtures.SamplePng);

            var r = await EbookReader.ParseAsync(path);

            Assert.True(r.Success, r.Error);
            Assert.Equal("978-TEST-EPUB2", r.Info!.Identifier);
            Assert.Equal("Test Book Two", r.Info!.Title);
            Assert.Equal("Grace Hopper", r.Info!.Author);
            Assert.Equal(3, r.Info!.ChapterCount);         // ncx 三 navPoint（皆葉）
            Assert.NotNull(r.Info!.CoverBytes);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- 壞檔／缺檔（明確失敗、不擲例外） ----

    [Fact]
    public async Task ParseAsync_BrokenFile_FailsWithoutThrow()
    {
        var dir = NewTempDir();
        try
        {
            var path = EpubTestFixtures.WriteBrokenFile(dir);
            var r = await EbookReader.ParseAsync(path);        // 不得擲例外
            Assert.False(r.Success);
            Assert.NotNull(r.Error);
            Assert.Null(r.Info);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ParseAsync_MissingFile_Fails()
    {
        var r = await EbookReader.ParseAsync(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.epub"));
        Assert.False(r.Success);
    }

    [Fact]
    public async Task ParseAsync_EmptyOrNullPath_Fails()
    {
        Assert.False((await EbookReader.ParseAsync("")).Success);
        Assert.False((await EbookReader.ParseAsync("   ")).Success);
        Assert.False((await EbookReader.ParseAsync(null)).Success);
    }

    // ---- 純函式：DeriveIdentifier ----

    [Fact]
    public void DeriveIdentifier_PrefersFirstNonEmptyDcIdentifier()
    {
        Assert.Equal("urn:isbn:42", EbookReader.DeriveIdentifier(new[] { "  urn:isbn:42 " }, "T", "A")); // 去頭尾
        Assert.Equal("second", EbookReader.DeriveIdentifier(new[] { "", "  ", "second" }, "T", "A"));     // 略過空白項
    }

    [Fact]
    public void DeriveIdentifier_NoIdentifier_FallsBackToTitleAuthor()
    {
        Assert.Equal("Title|Author", EbookReader.DeriveIdentifier(Array.Empty<string?>(), "Title", "Author"));
        Assert.Equal("Title|Author", EbookReader.DeriveIdentifier(new string?[] { null, "  " }, " Title ", " Author "));
    }

    // ---- 純函式：DedupeKey ----

    [Fact]
    public void DedupeKey_NormalizesCaseAndWhitespace()
    {
        var a = EbookReader.DedupeKey("  URN:ISBN:42  ");
        var b = EbookReader.DedupeKey("urn:isbn:42");
        Assert.Equal(b, a);                                            // 大小寫/空白正規化後同鍵
        Assert.Equal("a b c", EbookReader.DedupeKey("A   B\tC"));      // 折疊連續空白
        Assert.Equal("", EbookReader.DedupeKey(""));                   // 空→空
        Assert.Equal("", EbookReader.DedupeKey(null));
    }

    // ---- 純函式：CountLeaves ----

    [Fact]
    public void CountLeaves_FlatTree_CountsEach()
    {
        var toc = new List<EbookTocNode> { new() { Title = "1" }, new() { Title = "2" }, new() { Title = "3" } };
        Assert.Equal(3, EbookReader.CountLeaves(toc));
    }

    [Fact]
    public void CountLeaves_NestedTree_CountsOnlyLeaves()
    {
        // Part One → [1.1, 1.2]；Part Two → [2.1]；頂層 header 不計、僅計葉 → 3
        var toc = new List<EbookTocNode>
        {
            new() { Title = "Part One", Children = { new() { Title = "1.1" }, new() { Title = "1.2" } } },
            new() { Title = "Part Two", Children = { new() { Title = "2.1" } } },
        };
        Assert.Equal(3, EbookReader.CountLeaves(toc));
    }

    [Fact]
    public void CountLeaves_Empty_Zero()
    {
        Assert.Equal(0, EbookReader.CountLeaves(new List<EbookTocNode>()));
    }
}
