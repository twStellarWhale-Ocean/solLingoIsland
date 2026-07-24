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
            var pb = new PromptBuilder();
            pb.StartVoice(new CultureInfo(culture));
            pb.AppendText(text);
            pb.EndVoice();
            _synth.SpeakAsync(pb);
        }
        catch
        {
            // 該語言語音缺失（如未裝中文 TTS）→ 退回預設語音直接念
            _synth.SpeakAsync(text);
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
