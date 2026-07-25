using System.IO;
using System.Text.Json;

namespace LingoIsland.Present;

/// <summary>
/// 電子書閱讀器之使用者偏好持久化（跨啟動記憶；#245）。存於 %APPDATA%\LingoIsland\reader-prefs.json
/// （使用者可寫、不隨改建覆蓋，比照 <see cref="UiStateStore"/>）。讀寫失敗一律退回預設、不致命。
/// </summary>
public sealed class ReaderPrefsStore
{
    /// <summary>
    /// 暫停模式（對映 [EbookPage] <c>RPauseMode</c> 之 int 值，<b>勿改順序</b>——已持久化）：
    /// 0＝不暫停、1＝發言前暫停、2＝發言後暫停、3＝暫停並屏蔽指定說話人發言（角色扮演）。
    /// </summary>
    public int PauseMode { get; set; }  // 預設 0＝不暫停

    /// <summary>朗讀範圍（#251）：false＝只朗讀對話（預設）、true＝讀全部（連旁白）。</summary>
    public bool ReadAll { get; set; }

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LingoIsland");

    private static string FilePath => Path.Combine(Dir, "reader-prefs.json");

    public static ReaderPrefsStore Load()
    {
        try
        {
            return JsonSerializer.Deserialize<ReaderPrefsStore>(File.ReadAllText(FilePath)) ?? new ReaderPrefsStore();
        }
        catch
        {
            return new ReaderPrefsStore(); // 缺檔或格式壞 → 預設
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 持久化失敗（權限等）不影響主流程
        }
    }
}
