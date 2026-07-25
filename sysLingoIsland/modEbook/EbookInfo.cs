using System.Text.Json.Serialization;

namespace LingoIsland.Ebook;

/// <summary>
/// 一本解析成功之電子書中繼（[modEbook模組]，spec#4）：由 <see cref="EbookReader.ParseAsync"/> 產出、亦為每本書資料夾之
/// <c>info.json</c> 落地模型（<see cref="CoverBytes"/> 以 <see cref="JsonIgnoreAttribute"/> 排除、封面另存為圖檔）。
/// 欄位：<see cref="Id"/>（新生成唯一碼）／<see cref="Identifier"/>（dc:identifier，缺則「書名|作者」）／<see cref="Title"/>／
/// <see cref="Author"/>（AuthorList join）／<see cref="Language"/>／<see cref="ChapterCount"/>（目錄樹葉節點數）／
/// <see cref="SpineHrefs"/>（spine 順序）／<see cref="Toc"/>（目錄樹）／<see cref="CoverBytes"/>（byte[]?，null 合法）。
/// </summary>
public sealed class EbookInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Identifier { get; set; } = "";     // dc:identifier；缺則以「書名|作者」為識別基底（去重之基準）
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";          // AuthorList 以「, 」串接
    public string Language { get; set; } = "";
    public int ChapterCount { get; set; }             // 目錄（nav/ncx 統一樹）之葉節點計數
    public List<string> SpineHrefs { get; set; } = new(); // spine 閱讀順序（各內容檔相對路徑；供後續章節渲染增量）
    public List<EbookTocNode> Toc { get; set; } = new();  // 目錄樹（供 info.json 追溯／後續導讀增量）

    /// <summary>封面原始位元組（缺封面＝null，合法）；不入 info.json（另存 cover.png／cover.jpg）。</summary>
    [JsonIgnore]
    public byte[]? CoverBytes { get; set; }
}

/// <summary>目錄樹節點（[modEbook模組]）：nav（EPUB3）與 ncx（EPUB2）由 VersOne.Epub 統一為遞迴樹後投影之可序列化節點。</summary>
public sealed class EbookTocNode
{
    public string Title { get; set; } = "";
    /// <summary>此目錄項指向之內容檔路徑（增量3；供對應 spine 章跳讀）；去 <c>#anchor</c>、可空（純標題節點無連結）。舊 info.json 無此鍵→反序列化為空。</summary>
    public string Href { get; set; } = "";
    public List<EbookTocNode> Children { get; set; } = new();
}

/// <summary>
/// 解析結果（[modEbook模組]）：明確以成功/失敗表達，**不向上層擲未捕例外**——壞檔／非 EPUB／解析失敗回
/// <see cref="Success"/>＝false＋<see cref="Error"/>，上層可略過該檔、不中斷整批。
/// </summary>
public sealed class EbookParseResult
{
    public bool Success { get; private init; }
    public EbookInfo? Info { get; private init; }
    public string? Error { get; private init; }

    public static EbookParseResult Ok(EbookInfo info) => new() { Success = true, Info = info };
    public static EbookParseResult Fail(string error) => new() { Success = false, Error = error };
}
