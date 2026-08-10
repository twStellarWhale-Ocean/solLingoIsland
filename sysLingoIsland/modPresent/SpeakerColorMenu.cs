using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using LingoIsland.Query;

namespace LingoIsland.Present;

/// <summary>
/// 說話人清單「右鍵指定顏色」之共用選單（#294）——**電子書頁與影片頁共用同一份，不各留一份複製品**。
/// <para>
/// 主題配色不是「說話人→顏色」字典，而是 12 個色槽各帶一段描述文字，以描述是否指名該說話人決定顏色
/// （<see cref="ThemeStore.DescribesSpeaker"/>）。故「指定顏色」＝把名字寫入該色槽描述、並自其他色槽移除同名
/// （<see cref="ThemeStore.AssignSpeakerColor"/> 之正規化），而後 <see cref="ThemeStore.TrySave"/>。
/// </para>
/// <para>
/// 存檔成功即觸發 <c>ThemeStore.Changed</c>，兩頁皆已透過 <c>IThemeConsumerPage</c> 收推送並重算配色，
/// **本類別不必自行通知任何頁面**。載入→修改→存回一律在點擊當下對**同一份實例**進行，避免覆蓋他處變更。
/// </para>
/// </summary>
internal static class SpeakerColorMenu
{
    /// <summary>右鍵選單各項之 AutomationId 前綴（e2e 以此定位色槽項）。</summary>
    public const string SlotAutomationIdPrefix = "SpeakerColorSlot";

    /// <summary>「不指定顏色」項之 AutomationId。</summary>
    public const string ClearAutomationId = "SpeakerColorClear";

    /// <summary>
    /// 依目前主題填滿右鍵選單：12 個色槽（色票＋描述，目前所屬者打勾）＋「不指定顏色」。
    /// 無主題（<paramref name="themeId"/> 為 null 或解析不到）時填一列停用提示，供使用者知道為何無得可選。
    /// </summary>
    /// <param name="menu">要填的選單（會先清空）。</param>
    /// <param name="store">主題存放區（點擊時以其 Load／TrySave 讀寫同一份實例）。</param>
    /// <param name="themeId">目前頁面所選主題 id。</param>
    /// <param name="speakerName">受指派之說話人名。</param>
    /// <param name="onError">存檔失敗時之回報（null＝靜默）。</param>
    /// <returns>是否有可點的指派項（false＝只填了提示列）。</returns>
    public static bool Fill(ContextMenu menu, ThemeStore store, string? themeId, string speakerName, Action<string>? onError = null)
    {
        menu.Items.Clear();
        if (string.IsNullOrWhiteSpace(speakerName)) { return false; }

        var data = store.Load();
        var theme = string.IsNullOrWhiteSpace(themeId) ? null : ThemeStore.Find(data, themeId!);
        if (theme is null)
        {
            menu.Items.Add(new MenuItem { Header = "（尚未選定主題，無色盤可指定）", IsEnabled = false });
            return false;
        }

        ThemeColors.Ensure(theme);
        var current = ThemeStore.SlotOfSpeaker(theme, speakerName);

        menu.Items.Add(new MenuItem { Header = $"指定「{speakerName}」的顏色", IsEnabled = false });
        menu.Items.Add(new Separator());

        for (int i = 0; i < theme.Colors.Count; i++)
        {
            var slot = i;
            var col = theme.Colors[slot];
            var desc = string.IsNullOrWhiteSpace(col.Description) ? "（無描述）" : col.Description.Trim();
            var item = new MenuItem
            {
                Header = $"{col.Hex}　{desc}",
                Icon = Swatch(col.Hex),
                IsCheckable = false,
                IsChecked = slot == current,
            };
            AutomationProperties.SetAutomationId(item, SlotAutomationIdPrefix + slot);
            item.Click += (_, _) => Apply(store, themeId!, speakerName, slot, onError);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "不指定顏色（清除）", IsEnabled = current >= 0 };
        AutomationProperties.SetAutomationId(clear, ClearAutomationId);
        clear.Click += (_, _) => Apply(store, themeId!, speakerName, -1, onError);
        menu.Items.Add(clear);
        return true;
    }

    /// <summary>色票方塊（選單項 Icon）；hex 不合法時回 null（不因色值壞掉而整個選單開不出來）。</summary>
    private static UIElement? Swatch(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) { return null; }
        try
        {
            var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex.Trim()));
            brush.Freeze();
            return new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(2),
                Background = brush,
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
            };
        }
        catch { return null; }
    }

    /// <summary>載入→改→存回同一份實例（<paramref name="slotIndex"/> 負值＝清除）。存檔成功即由 Changed 推送兩頁重繪。</summary>
    private static void Apply(ThemeStore store, string themeId, string speakerName, int slotIndex, Action<string>? onError)
    {
        var data = store.Load();                       // 點擊當下重讀：避免以開選單那一刻的舊快照覆蓋他處變更
        var theme = ThemeStore.Find(data, themeId);
        if (theme is null) { return; }
        if (!ThemeStore.AssignSpeakerColor(theme, speakerName, slotIndex)) { return; }
        if (!store.TrySave(data, out var err)) { onError?.Invoke(err); }
    }
}
