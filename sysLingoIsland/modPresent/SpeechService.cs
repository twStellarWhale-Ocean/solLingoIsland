using System.Globalization;
using System.Speech.Synthesis;

namespace LingoIsland.Present;

/// <summary>朗讀抽象（[techItem語音合成]）——介面化使單元測試可攔截、不實際發聲。</summary>
public interface ISpeechService
{
    /// <summary>依語言（culture，如 "en-US"／"zh-TW"）朗讀；stopPrevious 為 true 時先停前次。</summary>
    void Speak(string text, string culture, bool stopPrevious = true);

    /// <summary>
    /// 當前段（句）<b>朗讀完成</b>時觸發（增量2 spec#8）：供電子書內容頁逐段 TTS <b>唸完自動前進</b>到下一段續唸。
    /// <see cref="SpeakDoneEventArgs.Cancelled"/>＝true 表被 <c>stopPrevious</c>／<c>SpeakAsyncCancelAll</c> 中止
    /// （切書／跳讀／暫停即止）——訂閱端<b>不應據此自動前進</b>（比照 [EbookPage] 契約 generation-token／僅完成段仍＝當前游標才前進）。
    /// </summary>
    event EventHandler<SpeakDoneEventArgs>? SpeakCompleted;
}

/// <summary>朗讀完成事件參數（增量2）：<see cref="Cancelled"/> 區分自然唸完（false，可自動前進）與被中止（true，不前進）。</summary>
public sealed class SpeakDoneEventArgs : EventArgs
{
    public bool Cancelled { get; }
    public SpeakDoneEventArgs(bool cancelled) => Cancelled = cancelled;
}

/// <summary>Windows 內建語音合成（SAPI，離線）之 ISpeechService 實作，依 culture 選語言。</summary>
public sealed class SpeechService : ISpeechService, IDisposable
{
    private readonly SpeechSynthesizer _synth = new();

    /// <inheritdoc/>
    public event EventHandler<SpeakDoneEventArgs>? SpeakCompleted;

    public SpeechService(string? voice)
    {
        _synth.SetOutputToDefaultAudioDevice();
        // SAPI 之 SpeakCompleted（含中止亦觸發，e.Cancelled 區分）→ 再拋為本服務事件，驅動切片2 逐段自動前進。
        _synth.SpeakCompleted += OnSynthSpeakCompleted;
        // appsettings 指定語音則優先；否則各次 Speak 依 culture 自動選
        if (!string.IsNullOrWhiteSpace(voice))
        {
            try { _synth.SelectVoice(voice); }
            catch { /* 指定語音缺失，退回系統預設 */ }
        }
    }

    /// <summary>轉拋 SAPI 完成事件為 <see cref="SpeakCompleted"/>（保留 Cancelled 語意：被 SpeakAsyncCancelAll 中止時 Cancelled=true）。</summary>
    private void OnSynthSpeakCompleted(object? sender, SpeakCompletedEventArgs e) =>
        SpeakCompleted?.Invoke(this, new SpeakDoneEventArgs(e.Cancelled));

    public void Speak(string text, string culture, bool stopPrevious = true)
    {
        if (stopPrevious)
        {
            _synth.SpeakAsyncCancelAll(); // 重複觸發先停前次
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        _synth.Rate = SpeechRateSettings.SapiRate; // v1.0.1（USR 回饋）：每次朗讀套用工具列 Speed（50–200%→SAPI Rate）

        try
        {
            // #252：單一 PromptBuilder、一次 SpeakAsync——不得改為逐段多次 SpeakAsync，
            // 否則 SpeakCompleted 每段各觸發一次、[EbookPage]「唸完自動前進」會在段落中途誤進。
            _synth.SpeakAsync(BuildPrompt(text, culture));
        }
        catch
        {
            // 整體建構或合成失敗 → 退回預設語音直接念（最後防線）
            _synth.SpeakAsync(text);
        }
    }

    /// <summary>
    /// 依 <see cref="SpeechSegmenter"/> 之切段結果建出**單一** <see cref="PromptBuilder"/>（#252）：
    /// 逐段 <c>StartVoice</c>／<c>AppendText</c>／<c>EndVoice</c>，中文段用中文語音、英文段用英文語音。
    /// <para>
    /// 某段之 culture 無對應已裝語音時**不 <c>StartVoice</c>**、以預設語音念（不略過——略過會讓使用者
    /// 誤以為程式壞了），並經 <see cref="MissingVoiceCulture"/> 讓呼叫端一次性告知。
    /// </para>
    /// <para>
    /// <b>零回歸</b>：純英文（或純中文）文本切段結果為單一段，產生之 prompt 與改版前等價。
    /// 本方法無副作用（不發聲），供單元測試以 <c>ToXml()</c> 斷言換聲確實發生。
    /// </para>
    /// </summary>
    public static PromptBuilder BuildPrompt(string text, string culture, IReadOnlyCollection<string>? availableCultures = null)
    {
        var available = availableCultures ?? InstalledCultures();
        var pb = new PromptBuilder();
        foreach (var seg in SpeechSegmenter.Split(text, culture))
        {
            var usable = available.Count == 0 || available.Contains(seg.Culture);
            if (usable)
            {
                pb.StartVoice(new CultureInfo(seg.Culture));
                pb.AppendText(seg.Text);
                pb.EndVoice();
            }
            else
            {
                MissingVoiceCulture?.Invoke(null, seg.Culture);
                pb.AppendText(seg.Text); // 無該語言語音：以預設語音念，不略過
            }
        }
        return pb;
    }

    /// <summary>
    /// 遇「該段語言無對應已裝語音」時觸發（#252），參數＝缺少之 culture 名。
    /// 訂閱端負責**一次性**告知使用者（如指引至「設定 → 語言 → 語音」加裝），本服務不重複去抖。
    /// </summary>
    public static event EventHandler<string>? MissingVoiceCulture;

    /// <summary>
    /// 系統已安裝且啟用之語音所涵蓋的 culture 名集合（#252 可用性偵測）——同時含精確名（<c>zh-TW</c>）
    /// 與雙字母語言碼（<c>zh</c>），使 <c>zh-HK</c> 語音亦能滿足 <c>zh-TW</c> 之需求判定。
    /// 取不到時回空集合，呼叫端據此視為「不設限」（一律 <c>StartVoice</c>、由 SAPI 自行決定）。
    /// </summary>
    public static IReadOnlyCollection<string> InstalledCultures()
    {
        try
        {
            using var s = new SpeechSynthesizer();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in s.GetInstalledVoices().Where(v => v.Enabled))
            {
                var c = v.VoiceInfo.Culture;
                set.Add(c.Name);                          // zh-TW
                set.Add(c.TwoLetterISOLanguageName);      // zh
                // zh-HK 之語音亦可服務 zh-TW 之需求（同語族），故補齊同語族之常見變體
                foreach (var sib in CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                             .Where(x => x.TwoLetterISOLanguageName == c.TwoLetterISOLanguageName))
                {
                    set.Add(sib.Name);
                }
            }
            return set;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Dispose()
    {
        _synth.SpeakCompleted -= OnSynthSpeakCompleted;
        _synth.Dispose();
    }

    /// <summary>列舉系統已安裝且啟用的語音名稱（供設定選單；不實際發聲）。</summary>
    public static IReadOnlyList<string> InstalledVoiceNames()
    {
        try
        {
            using var s = new SpeechSynthesizer();
            return s.GetInstalledVoices()
                .Where(v => v.Enabled)
                .Select(v => v.VoiceInfo.Name)
                .ToList();
        }
        catch
        {
            return System.Array.Empty<string>();
        }
    }
}
