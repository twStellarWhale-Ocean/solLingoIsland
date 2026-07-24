using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using LingoIsland.Ebook;

namespace LingoIsland.Query;

/// <summary>一本已入櫃之電子書（spec#5／#6）：中繼欄位＋主題快照＋封面檔名＋加入時間＋藏書資料夾名。</summary>
public sealed class EbookItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DcIdentifier { get; set; } = "";  // dc:identifier（或缺時之「書名|作者」基底）；去重以其正規化
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Language { get; set; } = "";
    public int ChapterCount { get; set; }
    public string? ThemeId { get; set; }
    public string? ThemeName { get; set; }
    public string? CoverFile { get; set; }          // 封面圖檔名（相對書資料夾，如 cover.png）；無封面＝null→呈現層佔位
    public string AddedAt { get; set; } = "";        // ISO 8601
    public string Folder { get; set; } = "";         // 藏書資料夾名（相對 ebook\ 根）；供 Remove 刪夾與跨啟動定位（撞名以 Id 短碼綴尾）
}

/// <summary>書櫃清單根結構（新在前）。</summary>
public sealed class EbooksData
{
    public List<EbookItem> Items { get; set; } = new();
    public EbookSort? Sort { get; set; }             // null＝預設（Added 新→舊）
}

/// <summary>
/// 書櫃排序態（spec#5；比照影片 <see cref="VideoSort"/>／筆記 <c>FolderSort</c> 家規）：<see cref="Mode"/> 四選一、
/// **每模式各自記方向**；預設 Added 新→舊。隨 ebooks.json 留存、跨啟動沿用；舊檔無鍵＝預設。
/// </summary>
public sealed class EbookSort
{
    public string Mode { get; set; } = "Added";      // Added|Title|Author|Theme
    public bool AddedAsc { get; set; }               // false＝新→舊（預設）；true＝舊→新
    public bool TitleAsc { get; set; } = true;       // true＝A→Z
    public bool AuthorAsc { get; set; } = true;      // true＝A→Z
    public bool ThemeAsc { get; set; } = true;       // true＝主題名 A→Z（未歸屬恆排末）

    /// <summary>目前模式之方向（供 ▲/▼ 顯示與投影）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool CurrentAscending => Mode switch
    {
        "Title" => TitleAsc,
        "Author" => AuthorAsc,
        "Theme" => ThemeAsc,
        _ => AddedAsc,
    };
}

/// <summary>加入結果：<see cref="Added"/>＝true＝新入櫃（<see cref="Item"/>＝新書卡）；false＝同書已在櫃（<see cref="Item"/>＝既有書卡、不重複落地）。</summary>
public sealed record EbookAddResult(bool Added, EbookItem Item);

/// <summary>
/// 電子書書櫃本機儲存（[EbookStore]，[modQuery模組]，spec#5／#6；比照 <see cref="VideoStore"/>／<see cref="ScreenshotStore"/>）：
/// 書櫃清單存 <c>%APPDATA%\LingoIsland\ebooks.json</c>；每本內容落地一資料夾 <c>%APPDATA%\LingoIsland\ebook\{yyyyMMdd 標題}\</c>
/// （<c>info.json</c>＋原始 <c>.epub</c> 複本＋封面圖）。<see cref="Add"/> 依 <c>dc:identifier</c> 正規化跨全櫃去重；
/// 資料夾同日同標題撞名以書卡 <see cref="EbookItem.Id"/> 短碼綴尾消歧。清單增刪／去重／排序為純函式（可單元測試、不觸檔案）；
/// 讀寫失敗退空/降級不致命。跨啟動以 ebooks.json＋各書資料夾還原。
/// </summary>
public sealed class EbookStore
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    private const string InfoFile = "info.json";

    private readonly string _path;
    private readonly string _root;   // 藏書資料夾根（ebook\）

    public EbookStore(string? path = null, string? root = null)
    {
        _path = path ?? DefaultPath;
        _root = root ?? DefaultRoot;
    }

    private static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LingoIsland");

    public static string DefaultPath => Path.Combine(DefaultDir, "ebooks.json");

    /// <summary>藏書資料夾根：<c>%APPDATA%\LingoIsland\ebook</c>（各書子資料夾在其下）。</summary>
    public static string DefaultRoot => Path.Combine(DefaultDir, "ebook");

    public EbooksData Load()
    {
        try { return JsonSerializer.Deserialize<EbooksData>(File.ReadAllText(_path)) ?? new EbooksData(); }
        catch { return new EbooksData(); }
    }

    public void Save(EbooksData d)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(d, Opts));
        }
        catch { /* 寫入失敗不影響主流程 */ }
    }

    /// <summary>某本書資料夾之完整路徑（供開啟／落地）；未落地者依 <see cref="EbookItem.Folder"/> 推得。</summary>
    public string FolderPathFor(EbookItem item) => Path.Combine(_root, item.Folder);

    /// <summary>
    /// 加入一本書：以 <c>dc:identifier</c> 正規化跨全櫃去重——同書已在櫃即回 <c>Added=false</c>＋既有書卡、**不重複落地**；
    /// 新書則落地資料夾（<c>info.json</c>＋原始 <c>.epub</c> 複本＋封面圖）、於清單最前插入、存回。回加入結果。
    /// </summary>
    public EbookAddResult Add(EbookInfo info, string? sourceEpubPath, string? themeId, string? themeName, DateTimeOffset addedAt)
    {
        var d = Load();
        var key = EbookReader.DedupeKey(info.Identifier);
        var existing = FindByKey(d, key);
        if (existing is not null) { return new EbookAddResult(false, existing); } // 已在書櫃、不重複入櫃

        var item = new EbookItem
        {
            Id = string.IsNullOrEmpty(info.Id) ? Guid.NewGuid().ToString("N") : info.Id,
            DcIdentifier = info.Identifier,
            Title = info.Title,
            Author = info.Author,
            Language = info.Language,
            ChapterCount = info.ChapterCount,
            ThemeId = themeId,
            ThemeName = string.IsNullOrWhiteSpace(themeName) ? null : themeName!.Trim(),
            AddedAt = addedAt.ToString("o"),
        };

        // 落地藏書資料夾（失敗仍記清單，呈現層縮圖顯佔位、不致命）。
        try
        {
            var folder = CreateBookFolder(item, addedAt);
            item.Folder = Path.GetFileName(folder);
            File.WriteAllText(Path.Combine(folder, InfoFile), JsonSerializer.Serialize(info, Opts)); // 中繼（CoverBytes 已 JsonIgnore）
            if (!string.IsNullOrWhiteSpace(sourceEpubPath) && File.Exists(sourceEpubPath))
            {
                var epubName = SafeEpubFileName(sourceEpubPath, item.Title);
                File.Copy(sourceEpubPath, Path.Combine(folder, epubName), overwrite: true); // 原始複本（唯讀來源、不改寫原檔）
            }
            if (info.CoverBytes is { Length: > 0 })
            {
                var coverName = "cover" + CoverExtension(info.CoverBytes);
                File.WriteAllBytes(Path.Combine(folder, coverName), info.CoverBytes);
                item.CoverFile = coverName;
            }
        }
        catch { /* 落地失敗不致命 */ }

        AddToList(d, item);
        Save(d);
        return new EbookAddResult(true, item);
    }

    /// <summary>刪一本書：自清單移除並刪其藏書資料夾。回被移除書卡、無則 null。</summary>
    public EbookItem? Remove(string id)
    {
        var d = Load();
        var it = RemoveFromList(d, id);
        if (it is not null)
        {
            DeleteBookFolder(it);
            Save(d);
        }
        return it;
    }

    /// <summary>清空書櫃：清清單（含排序態，比照 <see cref="VideoStore.Clear"/>）並刪整個藏書根資料夾。</summary>
    public void Clear()
    {
        Save(new EbooksData());
        try { if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); } } catch { /* 盡力 */ }
    }

    /// <summary>回寫某本所屬主題（主題重指派）；<paramref name="themeId"/>＝null＝改為未歸屬。</summary>
    public void UpdateTheme(string id, string? themeId, string? themeName)
    {
        var d = Load();
        if (SetTheme(d, id, themeId, themeName)) { Save(d); }
    }

    /// <summary>回寫書櫃排序態；跨啟動沿用。</summary>
    public void UpdateSort(EbookSort sort)
    {
        var d = Load();
        d.Sort = sort;
        Save(d);
    }

    // ---- 純函式（可單元測試，不觸檔案） ----

    /// <summary>於清單最前插入（新在前）。</summary>
    public static void AddToList(EbooksData d, EbookItem item) => d.Items.Insert(0, item);

    /// <summary>自清單移除指定 id；回被移除項（供呼叫端刪其資料夾）、無則 null。</summary>
    public static EbookItem? RemoveFromList(EbooksData d, string id)
    {
        var it = d.Items.FirstOrDefault(i => i.Id == id);
        if (it is not null) { d.Items.Remove(it); }
        return it;
    }

    /// <summary>跨全櫃找同書（去重）：以 <see cref="EbookReader.DedupeKey"/> 正規化後比對 <see cref="EbookItem.DcIdentifier"/>；空 key 一律視為找不到（不誤併）。</summary>
    public static EbookItem? FindByKey(EbooksData d, string dedupeKey)
    {
        if (string.IsNullOrEmpty(dedupeKey)) { return null; }
        return d.Items.FirstOrDefault(i => EbookReader.DedupeKey(i.DcIdentifier) == dedupeKey);
    }

    /// <summary>設定某本所屬主題（純函式）：找到即改 ThemeId／ThemeName（名稱去空白、空白視為 null）、回 true；無此 id 回 false。</summary>
    public static bool SetTheme(EbooksData d, string id, string? themeId, string? themeName)
    {
        var it = d.Items.FirstOrDefault(i => i.Id == id);
        if (it is null) { return false; }
        it.ThemeId = themeId;
        it.ThemeName = string.IsNullOrWhiteSpace(themeName) ? null : themeName!.Trim();
        return true;
    }

    /// <summary>
    /// 書櫃排序（純函式、穩定排序、不改動傳入序）：呈現層投影、清單仍存插入序。
    /// <c>Added</c>＝插入序（新在前；反向＝舊在前）；<c>Title</c>／<c>Author</c>＝自然排序（沿筆記 <see cref="NotesStore.NaturalCompare"/> 家規：
    /// 大小寫不敏感、數字段依數值）；<c>Theme</c>＝主題名自然排序群組（**未歸屬恆排末**）、組內維持插入序（新在前）。
    /// </summary>
    public static List<EbookItem> SortEbooks(IEnumerable<EbookItem> items, EbookSort? sort)
    {
        var s = sort ?? new EbookSort();
        var list = items.ToList();
        switch (s.Mode)
        {
            case "Title":
                return s.TitleAsc
                    ? list.OrderBy(i => i.Title ?? "", NaturalTitleComparer.Instance).ToList()
                    : list.OrderByDescending(i => i.Title ?? "", NaturalTitleComparer.Instance).ToList();
            case "Author":
                return s.AuthorAsc
                    ? list.OrderBy(i => i.Author ?? "", NaturalTitleComparer.Instance).ToList()
                    : list.OrderByDescending(i => i.Author ?? "", NaturalTitleComparer.Instance).ToList();
            case "Theme":
            {
                var grouped = list.Where(i => !string.IsNullOrWhiteSpace(i.ThemeName));
                var ordered = s.ThemeAsc ? grouped.OrderBy(i => i.ThemeName!, NaturalTitleComparer.Instance)
                                         : grouped.OrderByDescending(i => i.ThemeName!, NaturalTitleComparer.Instance);
                return ordered.Concat(list.Where(i => string.IsNullOrWhiteSpace(i.ThemeName))).ToList(); // 未歸屬恆排末；組內插入序（穩定）
            }
            default:
                if (s.AddedAsc) { var r = new List<EbookItem>(list); r.Reverse(); return r; } // 舊→新
                return list; // 新→舊（插入序）
        }
    }

    /// <summary>封面副檔名（純函式）：依 magic bytes 判 PNG／JPEG／GIF；無法辨識退 <c>.png</c>。</summary>
    public static string CoverExtension(byte[]? cover)
    {
        if (cover is null || cover.Length < 4) { return ".png"; }
        if (cover[0] == 0x89 && cover[1] == 0x50 && cover[2] == 0x4E && cover[3] == 0x47) { return ".png"; } // \x89PNG
        if (cover[0] == 0xFF && cover[1] == 0xD8 && cover[2] == 0xFF) { return ".jpg"; }                      // JPEG SOI
        if (cover[0] == 0x47 && cover[1] == 0x49 && cover[2] == 0x46 && cover[3] == 0x38) { return ".gif"; }  // GIF8
        return ".png";
    }

    // ---- 內部檔案 IO ----

    /// <summary>建立某本書資料夾：名＝<c>{addedAt:yyyyMMdd} {標題}</c>；撞名（同日同標題、不同書）→ 綴書卡 Id 短碼消歧。回完整路徑。</summary>
    private string CreateBookFolder(EbookItem item, DateTimeOffset addedAt)
    {
        Directory.CreateDirectory(_root);
        var baseName = addedAt.ToString("yyyyMMdd") + " " + SanitizeName(item.Title);
        var name = baseName;
        if (Directory.Exists(Path.Combine(_root, name)))
        {
            name = baseName + " " + ShortId(item.Id); // 撞名消歧（不同 dc:identifier 之同名書同日匯入不互相覆寫）
        }
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private void DeleteBookFolder(EbookItem it)
    {
        if (string.IsNullOrEmpty(it.Folder)) { return; }
        try
        {
            var path = Path.Combine(_root, it.Folder);
            if (Directory.Exists(path)) { Directory.Delete(path, recursive: true); }
        }
        catch { /* 不致命 */ }
    }

    private static string ShortId(string? id) => string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N")[..8]
                                                                          : (id.Length >= 8 ? id[..8] : id);

    /// <summary>原始 .epub 複本檔名：保留來源檔名（已是合法檔名）；無效則以標題安全化＋.epub。</summary>
    private static string SafeEpubFileName(string sourcePath, string? title)
    {
        var name = Path.GetFileName(sourcePath);
        if (!string.IsNullOrWhiteSpace(name)) { return name; }
        return SanitizeName(title) + ".epub";
    }

    /// <summary>資料夾/檔名安全化：去除檔名非法字元、收合空白、截長；空→untitled（比照 <c>SubtitleStore</c>）。</summary>
    private static string SanitizeName(string? s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((s ?? "").Select(c => invalid.Contains(c) ? ' ' : c).ToArray());
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim().TrimEnd('.'); // 尾點在 Windows 資料夾名會被吞
        if (cleaned.Length > 80) { cleaned = cleaned[..80].Trim(); }
        return cleaned.Length == 0 ? "untitled" : cleaned;
    }

    /// <summary>包 <see cref="NotesStore.NaturalCompare"/> 為 IComparer（供 OrderBy 穩定排序用；比照 <see cref="VideoStore"/>）。</summary>
    private sealed class NaturalTitleComparer : IComparer<string>
    {
        public static readonly NaturalTitleComparer Instance = new();
        public int Compare(string? x, string? y) => NotesStore.NaturalCompare(x ?? "", y ?? "");
    }
}
