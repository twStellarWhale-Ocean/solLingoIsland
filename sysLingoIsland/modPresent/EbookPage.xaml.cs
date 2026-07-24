using System.IO;
using LingoIsland.Ebook;
using LingoIsland.Query;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using UserControl = System.Windows.Controls.UserControl;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using StackPanel = System.Windows.Controls.StackPanel;
using Grid = System.Windows.Controls.Grid;
using Border = System.Windows.Controls.Border;
using Image = System.Windows.Controls.Image;
using ColumnDefinition = System.Windows.Controls.ColumnDefinition;
using Orientation = System.Windows.Controls.Orientation;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using ContextMenuEventArgs = System.Windows.Controls.ContextMenuEventArgs;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using Thickness = System.Windows.Thickness;
using Visibility = System.Windows.Visibility;
using VerticalAlignment = System.Windows.VerticalAlignment;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextTrimming = System.Windows.TextTrimming;
using GridLength = System.Windows.GridLength;
using GridUnitType = System.Windows.GridUnitType;
using CornerRadius = System.Windows.CornerRadius;
using FontWeights = System.Windows.FontWeights;
using FontFamily = System.Windows.Media.FontFamily;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using Key = System.Windows.Input.Key;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxImage = System.Windows.MessageBoxImage;
using Stretch = System.Windows.Media.Stretch;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ImageSource = System.Windows.Media.ImageSource;
using BitmapImage = System.Windows.Media.Imaging.BitmapImage;
using BitmapCacheOption = System.Windows.Media.Imaging.BitmapCacheOption;

namespace LingoIsland.Present;

/// <summary>
/// 電子書擷取分頁（[modEbook模組] 呈現端，design ＜III.C.(C)＞，spec#4/#5/#6；比照 <see cref="ScreenCapturePage"/>／
/// <see cref="VideoCapturePage"/> 版面族）：兩子頁籤【獲得】（左書櫃｜右匯入卡片，本增量焦點）／【內容】（章節閱讀骨架佔位）以可見性切換。
/// 【獲得】右卡片選/拖 <c>.epub</c> → 逐檔預解析（<see cref="EbookReader.ParseAsync"/>）顯示就緒／已在書櫃／無法匯入狀態 →
/// 批次確認後逐檔 <see cref="EbookStore.Add"/>（略過已在書櫃/失敗、非同步不阻塞 UI）；左書櫃以封面縮圖卡列出（主題篩選／四鍵排序／
/// 右鍵標記主題或刪除／Delete 刪／清空）。資料層（切片1）之解析與持久化不在此重寫，僅接線。
/// </summary>
public partial class EbookPage : UserControl
{
    private readonly EbookStore _store;      // 書櫃持久化（切片1；spec#5/#6）
    private readonly ThemeStore _themes;     // 依 theme 篩選（多媒體主題管理·B）＋匯入時記錄使用中主題＋書卡右鍵標記主題（#173）
    private bool _populatingFilter;          // 重填篩選下拉期間抑制 SelectionChanged→重整
    private string? _selectedBookId;         // 目前選取書卡（刪除目標／重整後保持選取）
    private static string? _lastPickDir;     // 選檔對話框上次目錄（桌面慣例；程序生命期內記憶、不落地）
    private readonly List<EbookPick> _picked = new();   // 【獲得】已選之本機 .epub（選檔/拖入後之預解析結果）
    private const int MaxBatch = 50;         // 單批上限（逾限明訊拒收、不默默截斷；比照影片批次）

    public EbookPage(EbookStore store, ThemeStore themes)
    {
        InitializeComponent();
        _store = store;
        _themes = themes;

        // 子頁籤（版面統一）：獲得（書櫃＋匯入）／內容（骨架），以可見性切換
        EbookTabAcquire.Checked += (_, _) => ShowSubTab(acquire: true);
        EbookTabContent.Checked += (_, _) => ShowSubTab(acquire: false);

        // 匯入卡片：選檔／全部清除／匯入；拖放 .epub 至整張卡片（Preview 穿隧，子元素不各自實作）
        PickEpubBtn.Click += (_, _) => PickEpubFiles();
        EbookClearFilesBtn.Click += (_, _) => { _picked.Clear(); RefreshPickedFiles(); SetStatus("已清空待匯入清單。"); };
        EbookImportBtn.Click += (_, _) => _ = DoImportAsync();
        ImportCard.PreviewDragOver += (_, e) =>
        {
            e.Effects = DroppedEpubFiles(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true; // 必須攔下：預設不處理 FileDrop
        };
        ImportCard.PreviewDrop += (_, e) =>
        {
            var files = DroppedEpubFiles(e);
            if (files.Count == 0) { return; }
            e.Handled = true;
            _ = AddPickedFilesAsync(files);
        };

        // 書櫃：主題篩選＋四鍵排序 toggle＋選取＋右鍵（標記主題／刪除）＋Delete 鍵刪＋清空
        BookThemeFilter.SelectionChanged += (_, _) => { if (!_populatingFilter) { RefreshBookshelf(); } };
        EbookSortAddedBtn.Click += (_, _) => ToggleSort("Added");   // 書櫃排序 toggle（比照影片 #219）
        EbookSortTitleBtn.Click += (_, _) => ToggleSort("Title");
        EbookSortAuthorBtn.Click += (_, _) => ToggleSort("Author");
        EbookSortThemeBtn.Click += (_, _) => ToggleSort("Theme");
        ClearShelfBtn.Click += (_, _) => OnClearShelf();
        BookList.SelectionChanged += OnBookSelect;
        BookList.ContextMenu = new ContextMenu();
        BookList.ContextMenuOpening += OnBookContextMenuOpening;
        BookList.PreviewMouseRightButtonDown += ListDeleteSupport.SelectItemUnderMouse; // 右鍵作用於游標下之書卡
        BookList.KeyDown += (_, e) => { if (e.Key == Key.Delete) { DeleteSelectedBook(); } };

        // 切回本頁重填篩選（反映主題增刪改）並重整書櫃
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) { PopulateThemeFilter(); RefreshBookshelf(); } };

        PopulateThemeFilter();
        RefreshBookshelf();   // #219：四 toggle 鈕視覺於此同步（單一同步點）
        RefreshPickedFiles();
    }

    /// <summary>切換子頁籤：獲得（書櫃＋匯入）／內容（骨架），以可見性切換。</summary>
    private void ShowSubTab(bool acquire)
    {
        EbookAcquirePane.Visibility = acquire ? Visibility.Visible : Visibility.Collapsed;
        EbookContentPane.Visibility = acquire ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetStatus(string msg) => StatusText.Text = msg;

    // ---- 左：書櫃（spec#5） ----

    /// <summary>以目前主題重填「依 theme 篩選」下拉（圖文，共用 <see cref="ThemeFilter"/>）；期間抑制重整、保留選取。</summary>
    private void PopulateThemeFilter()
    {
        _populatingFilter = true;
        ThemeFilter.Populate(BookThemeFilter, _themes);
        _populatingFilter = false;
    }

    /// <summary>#219 家規：同模式再點＝翻該模式方向；點他模式＝切為該模式（沿用其記住之方向）。即時落地 ebooks.json、重排書櫃。</summary>
    private void ToggleSort(string mode)
    {
        var d = _store.Load();
        var s = d.Sort ?? new EbookSort();
        if (s.Mode == mode)
        {
            switch (mode) // 同模式再點 → 翻該模式方向
            {
                case "Title": s.TitleAsc = !s.TitleAsc; break;
                case "Author": s.AuthorAsc = !s.AuthorAsc; break;
                case "Theme": s.ThemeAsc = !s.ThemeAsc; break;
                default: s.AddedAsc = !s.AddedAsc; break;
            }
        }
        else
        {
            s.Mode = mode; // 切模式，方向沿用該模式記住值
        }
        _store.UpdateSort(s);
        RefreshBookshelf();
    }

    /// <summary>重載書櫃（封面縮圖卡＋書名/作者/章數/主題）；先依主題篩選、再依 <see cref="EbookStore.SortEbooks"/> 排序（呈現層投影）；目前選中書卡自動選中。空櫃顯提示。</summary>
    public void RefreshBookshelf()
    {
        var d = _store.Load();
        var themeId = ThemeFilter.SelectedThemeId(BookThemeFilter); // null＝All（B）
        var s = d.Sort ?? new EbookSort();
        var shown = EbookStore.SortEbooks(d.Items.Where(it => ThemeFilter.Match(themeId, it.ThemeId)), s); // 先篩後排
        UpdateSortButtons(s);
        BookList.SelectionChanged -= OnBookSelect;
        BookList.Items.Clear();
        foreach (var it in shown)
        {
            BookList.Items.Add(new ListBoxItem { Content = BookItemView(it), Tag = it, Padding = new Thickness(4) });
        }
        if (_selectedBookId is not null)
        {
            for (int i = 0; i < BookList.Items.Count; i++)
            {
                if ((BookList.Items[i] as ListBoxItem)?.Tag is EbookItem b && b.Id == _selectedBookId)
                {
                    BookList.SelectedIndex = i;
                    BookList.ScrollIntoView(BookList.Items[i]); // 換排序鍵後選中列捲入可見
                    break;
                }
            }
        }
        BookList.SelectionChanged += OnBookSelect;
        BookEmptyHint.Text = d.Items.Count == 0
            ? "書櫃還是空的。用右側的匯入卡片選擇或拖入 .epub 檔加入藏書。"
            : "此主題尚無藏書。"; // 有藏書但本 theme 無
        BookEmptyHint.Visibility = shown.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>#219：依排序態更新四 toggle 鈕視覺（MDL2 字圖＋作用中 ▲/▼＋粗體深色；非顏色線索＝方向字圖存否，色盲友善）。</summary>
    private void UpdateSortButtons(EbookSort s)
    {
        SetSortBtn(EbookSortAddedBtn, "", s.Mode is not ("Title" or "Author" or "Theme"), s.AddedAsc); // Recent
        SetSortBtn(EbookSortTitleBtn, "", s.Mode == "Title", s.TitleAsc);                              // Font
        SetSortBtn(EbookSortAuthorBtn, "", s.Mode == "Author", s.AuthorAsc);                           // Contact
        SetSortBtn(EbookSortThemeBtn, "", s.Mode == "Theme", s.ThemeAsc);                              // Tag
    }

    private void SetSortBtn(Button btn, string glyph, bool active, bool ascending)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (active)
        {
            sp.Children.Add(new TextBlock
            {
                Text = ascending ? " ▲" : " ▼",
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        btn.Content = sp;
        btn.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
        btn.Foreground = (Brush)FindResource(active ? "PinkText" : "PinkSub");
    }

    /// <summary>一張書卡：封面縮圖（無封面→書本佔位字圖，比照影片離線縮圖回退）＋書名/作者/「{章數} 章 · 主題」。</summary>
    private StackPanel BookItemView(EbookItem it)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(MakeCover(it));
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        col.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(it.Title) ? "(未命名)" : it.Title,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brush("#3A2C33"),
        });
        if (!string.IsNullOrWhiteSpace(it.Author))
        {
            col.Children.Add(new TextBlock
            {
                Text = it.Author,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brush("#6A6A6A"),
            });
        }
        var meta = $"{it.ChapterCount} 章  ·  " + (string.IsNullOrWhiteSpace(it.ThemeName) ? "未分類" : it.ThemeName);
        col.Children.Add(new TextBlock
        {
            Text = meta,
            FontSize = 10,
            Foreground = Brush("#9A6A82"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        sp.Children.Add(col);
        return sp;
    }

    /// <summary>封面元素：有封面→縮圖 <see cref="Image"/>（<see cref="EbookItem.CoverFile"/> → <see cref="BitmapImage"/>）；無封面→書本佔位字圖。</summary>
    private System.Windows.FrameworkElement MakeCover(EbookItem it)
    {
        var src = LoadCover(it);
        if (src is not null)
        {
            return new Border
            {
                Width = 38, Height = 50, CornerRadius = new CornerRadius(2), ClipToBounds = true,
                Margin = new Thickness(0, 0, 8, 0), Background = Brush("#E8D8E0"),
                Child = new Image { Source = src, Stretch = Stretch.UniformToFill },
            };
        }
        return new Border // 無封面佔位（書本字圖）
        {
            Width = 38, Height = 50, CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 8, 0), Background = Brush("#F1E3EA"),
            BorderBrush = (Brush)FindResource("PinkAccent"), BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Foreground = (Brush)FindResource("PinkText"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>封面 ImageSource：自書資料夾之 <see cref="EbookItem.CoverFile"/> 惰性解碼載入（不鎖檔）；無檔／失敗回 null（呈現層退佔位、不致命）。</summary>
    private ImageSource? LoadCover(EbookItem it)
    {
        if (string.IsNullOrEmpty(it.CoverFile)) { return null; }
        try
        {
            var path = Path.Combine(_store.FolderPathFor(it), it.CoverFile);
            if (!File.Exists(path)) { return null; }
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad; // 不鎖檔
            bi.DecodePixelWidth = 80;                  // 縮圖解碼、省記憶體
            bi.StreamSource = new MemoryStream(File.ReadAllBytes(path));
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    private void OnBookSelect(object? sender, SelectionChangedEventArgs e)
        => _selectedBookId = ((BookList.SelectedItem as ListBoxItem)?.Tag as EbookItem)?.Id;

    /// <summary>書卡右鍵選單（依選取書卡動態填入）：標記主題（無主題＋各主題，目前者打勾；design intTest#60）＋刪除。無選取則不開。</summary>
    private void OnBookContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = BookList.ContextMenu!;
        menu.Items.Clear();
        var it = (BookList.SelectedItem as ListBoxItem)?.Tag as EbookItem;
        if (it is null) { e.Handled = true; return; } // 無選取→不開選單

        var themeMenu = new MenuItem { Header = "標記主題" };
        var none = new MenuItem { Header = "（無主題）", IsChecked = it.ThemeId is null };
        none.Click += (_, _) => { _store.UpdateTheme(it.Id, null, null); RefreshBookshelf(); };
        themeMenu.Items.Add(none);
        foreach (var t in _themes.Load().Items)
        {
            var tid = t.Id;
            var tname = t.Name;
            var mi = new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(tname) ? "(未命名)" : tname,
                IsChecked = tid == it.ThemeId,
            };
            mi.Click += (_, _) => { _store.UpdateTheme(it.Id, tid, tname); RefreshBookshelf(); };
            themeMenu.Items.Add(mi);
        }
        menu.Items.Add(themeMenu);
        menu.Items.Add(new Separator());
        var del = new MenuItem { Header = "刪除" };
        del.Click += (_, _) => DeleteSelectedBook();
        menu.Items.Add(del);
    }

    /// <summary>刪一本書（右鍵「刪除」或 Delete 鍵）：自書櫃與其藏書資料夾移除、重整清單。</summary>
    private void DeleteSelectedBook()
    {
        var it = (BookList.SelectedItem as ListBoxItem)?.Tag as EbookItem;
        if (it is null) { return; }
        _store.Remove(it.Id);
        if (_selectedBookId == it.Id) { _selectedBookId = null; }
        RefreshBookshelf();
        SetStatus($"已從書櫃移除「{(string.IsNullOrWhiteSpace(it.Title) ? it.Id : it.Title)}」。");
    }

    /// <summary>清空書櫃（含確認）：清清單（含排序態）並刪整個藏書根資料夾（原始 EPUB 來源檔不受影響）。</summary>
    private void OnClearShelf()
    {
        if (BookList.Items.Count == 0) { return; }
        if (MessageBox.Show(System.Windows.Window.GetWindow(this),
                "要清空整個書櫃嗎？這會移除所有藏書與其本機資料夾（你原本的 EPUB 來源檔不受影響）。",
                "清空書櫃", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        _store.Clear();
        _selectedBookId = null;
        RefreshBookshelf();
        SetStatus("已清空書櫃。");
    }

    // ---- 右：匯入卡片（spec#4） ----

    private static bool IsEpub(string path) => path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase);

    /// <summary>自拖放事件取出 <c>.epub</c> 檔路徑（純判斷、不讀檔）；非 FileDrop／資料夾／非 .epub 一律不計，供 DragOver 決定可放置或拒收。</summary>
    private static IReadOnlyList<string> DroppedEpubFiles(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { return Array.Empty<string>(); }
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) { return Array.Empty<string>(); }
        return paths.Where(p => !Directory.Exists(p) && IsEpub(p)).ToList();
    }

    /// <summary>開檔案對話框選一或多個本機 <c>.epub</c>（記憶上次目錄，桌面慣例）。</summary>
    private void PickEpubFiles()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "EPUB 電子書 (*.epub)|*.epub|所有檔案 (*.*)|*.*",
            Multiselect = true,
            Title = "選擇 EPUB 檔",
        };
        if (!string.IsNullOrEmpty(_lastPickDir) && Directory.Exists(_lastPickDir)) { dlg.InitialDirectory = _lastPickDir; }
        if (dlg.ShowDialog() != true) { return; }
        try { _lastPickDir = Path.GetDirectoryName(dlg.FileNames.FirstOrDefault() ?? ""); } catch { /* 記憶失敗不致命 */ }
        _ = AddPickedFilesAsync(dlg.FileNames);
    }

    /// <summary>併入選取／拖入之檔案並逐檔預解析（非同步、不阻塞 UI）：同完整路徑去重＋單批上限（逾限明訊拒收）；已解析者沿用、免重複 IO。</summary>
    private async Task AddPickedFilesAsync(IEnumerable<string> paths)
    {
        // 併入去重（同完整路徑不重複入列）＋單批上限（不默默截斷）
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>();
        foreach (var p in _picked.Select(x => x.Path).Concat(paths ?? Enumerable.Empty<string>()))
        {
            if (string.IsNullOrWhiteSpace(p)) { continue; }
            string key;
            try { key = Path.GetFullPath(p); } catch { key = p; }
            if (!seen.Add(key)) { continue; }
            merged.Add(p);
        }
        var rejected = Math.Max(0, merged.Count - MaxBatch);
        if (rejected > 0) { merged = merged.Take(MaxBatch).ToList(); }

        // 逐檔預解析（EbookReader.ParseAsync；已掃過者沿用）
        var scanned = new List<EbookPick>(merged.Count);
        foreach (var p in merged)
        {
            var known = _picked.FirstOrDefault(e => string.Equals(e.Path, p, StringComparison.OrdinalIgnoreCase));
            scanned.Add(known ?? await PreScanAsync(p));
        }
        _picked.Clear();
        _picked.AddRange(scanned);
        RefreshPickedFiles();
        SetStatus(rejected > 0
            ? $"已選 {_picked.Count} 個檔案；超過單批上限 {MaxBatch} 個，有 {rejected} 個未加入。"
            : $"已選 {_picked.Count} 個檔案。");
    }

    /// <summary>單檔預解析：副檔名檢查＋<see cref="EbookReader.ParseAsync"/>（不擲例外、失敗回明確 <see cref="EbookParseResult"/>）。「已在書櫃」於 <see cref="ComputeStatuses"/> 依當前書櫃另判。</summary>
    private static async Task<EbookPick> PreScanAsync(string path)
    {
        if (Directory.Exists(path) || !IsEpub(path))
        {
            return new EbookPick(path, null, "不是 EPUB 檔（副檔名須為 .epub）");
        }
        var res = await EbookReader.ParseAsync(path);
        if (!res.Success || res.Info is null)
        {
            return new EbookPick(path, null, string.IsNullOrWhiteSpace(res.Error) ? "解析失敗" : res.Error);
        }
        return new EbookPick(path, res.Info, null);
    }

    /// <summary>
    /// 依當前書櫃＋批內去重計算每檔顯示狀態（純判斷、不改 _picked）：解析失敗＝Failed；<see cref="EbookReader.DedupeKey"/> 已在書櫃或批內先前已見＝AlreadyExists；餘＝Ready。
    /// 依傳入順序判定批內重複（首見者 Ready、後見者 AlreadyExists），使清單顯示與匯入決策一致。
    /// </summary>
    private List<(EbookPick Pick, EbookPickStatus Status)> ComputeStatuses(IEnumerable<EbookPick> picks)
    {
        var data = _store.Load();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(EbookPick, EbookPickStatus)>();
        foreach (var p in picks)
        {
            if (!p.ParseOk) { result.Add((p, EbookPickStatus.Failed)); continue; }
            var key = EbookReader.DedupeKey(p.Info!.Identifier);
            if (!string.IsNullOrEmpty(key) && EbookStore.FindByKey(data, key) is not null)
            {
                result.Add((p, EbookPickStatus.AlreadyExists)); continue; // 已在書櫃
            }
            if (!string.IsNullOrEmpty(key) && !seen.Add(key))
            {
                result.Add((p, EbookPickStatus.AlreadyExists)); continue; // 批內重複（後見者略過）
            }
            result.Add((p, EbookPickStatus.Ready));
        }
        return result;
    }

    /// <summary>重建已選檔清單 UI（依檔名自然排序；每列檔名＋中繼＋狀態＋✕ 移除）＋更新計數與主鈕文案（隨可匯入檔數變）。</summary>
    private void RefreshPickedFiles()
    {
        EbookFilesList.Items.Clear();
        var ordered = _picked.OrderBy(p => p.FileName, Comparer<string>.Create(NotesStore.NaturalCompare)).ToList();
        var statuses = ComputeStatuses(ordered);
        foreach (var (pick, status) in statuses)
        {
            EbookFilesList.Items.Add(PickedFileRow(pick, status));
        }
        var any = _picked.Count > 0;
        EbookFilesCard.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EbookClearFilesBtn.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        var importable = statuses.Count(x => x.Status == EbookPickStatus.Ready);
        EbookFilesCount.Text = any ? $"已選 {_picked.Count} 檔（{importable} 可匯入）" : "";
        EbookImportBtn.Content = ImportButtonText(importable);
    }

    /// <summary>主行動鈕文案（隨可匯入檔數）：0＝「匯入電子書」／單檔＝「匯入並開啟書櫃」／N 檔＝「匯入 N 本」。</summary>
    private static string ImportButtonText(int importable) => importable switch
    {
        <= 0 => "＋ 匯入電子書",
        1 => "＋ 匯入並開啟書櫃",
        _ => $"＋ 匯入 {importable} 本",
    };

    /// <summary>已選檔清單之一列：檔名（＋中繼小字）｜狀態｜移除鈕。異常/略過列以暖色底標示（不只靠顏色——狀態文字亦明載原因）。</summary>
    private System.Windows.FrameworkElement PickedFileRow(EbookPick p, EbookPickStatus status)
    {
        var grid = new Grid { Margin = new Thickness(2, 1, 2, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var namecol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        namecol.Children.Add(new TextBlock
        {
            Text = p.FileName,
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = p.Path, // 完整路徑只在 tooltip（絕對路徑含使用者名稱，防隨截圖外洩）
            Foreground = Brush("#3A2C33"),
        });
        var meta = status == EbookPickStatus.Failed ? (p.Error ?? "") : (p.Info is not null ? MetaLine(p.Info) : "");
        if (meta.Length > 0)
        {
            namecol.Children.Add(new TextBlock
            {
                Text = meta,
                FontSize = 9.5,
                Foreground = Brush("#9A6A82"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        grid.Children.Add(namecol);

        var st = new TextBlock
        {
            Text = StatusPill(status),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = StatusBrush(status),
        };
        Grid.SetColumn(st, 1);
        grid.Children.Add(st);

        var del = new Button { Content = "✕", Width = 22, Height = 20, FontSize = 10, ToolTip = $"自清單移除 {p.FileName}" };
        del.Click += (_, _) => { _picked.Remove(p); RefreshPickedFiles(); };
        Grid.SetColumn(del, 2);
        grid.Children.Add(del);

        if (status is EbookPickStatus.Failed or EbookPickStatus.AlreadyExists)
        {
            var tint = status == EbookPickStatus.Failed
                ? Color.FromArgb(0x55, 0xF6, 0xDA, 0xDA)   // 暖紅：非 EPUB／解析失敗
                : Color.FromArgb(0x55, 0xFB, 0xE8, 0xCC);  // 暖黃：已在書櫃（略過）
            return new Border { Background = new SolidColorBrush(tint), CornerRadius = new CornerRadius(3), Padding = new Thickness(2), Child = grid };
        }
        return grid;
    }

    private static string MetaLine(EbookInfo info)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(info.Author)) { parts.Add(info.Author); }
        parts.Add($"{info.ChapterCount} 章");
        if (!string.IsNullOrWhiteSpace(info.Language)) { parts.Add(info.Language); }
        return string.Join(" · ", parts);
    }

    private static string StatusPill(EbookPickStatus s) => s switch
    {
        EbookPickStatus.Ready => "✓ 就緒",
        EbookPickStatus.AlreadyExists => "⚠ 已在書櫃",
        _ => "✕ 無法匯入",
    };

    private static Brush StatusBrush(EbookPickStatus s) => s switch
    {
        EbookPickStatus.Ready => Brush("#4A7A4A"),
        EbookPickStatus.AlreadyExists => Brush("#A06A20"),
        _ => Brush("#B03A3A"),
    };

    /// <summary>匯入：彙總確認 → 逐檔 <see cref="EbookStore.Add"/>（略過已在書櫃/失敗）→ 重整書櫃、回報成功/略過/失敗數。落地 IO 於背景執行緒、不阻塞 UI。</summary>
    private async Task DoImportAsync()
    {
        var ordered = _picked.OrderBy(p => p.FileName, Comparer<string>.Create(NotesStore.NaturalCompare)).ToList();
        var statuses = ComputeStatuses(ordered);
        var importable = statuses.Where(x => x.Status == EbookPickStatus.Ready).Select(x => x.Pick).ToList();
        if (importable.Count == 0)
        {
            SetStatus("目前沒有可匯入的電子書——請看清單上每個檔案的狀態說明。");
            return;
        }
        if (MessageBox.Show(System.Windows.Window.GetWindow(this), BuildImportConfirm(statuses),
                "匯入電子書", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            SetStatus("已取消匯入——已選的檔案仍保留在清單上。");
            return;
        }

        var active = ThemeStore.GetActive(_themes.Load()); // 匯入時記錄使用中主題（跨媒體主題歸屬）
        var themeId = active?.Id;
        var themeName = active?.Name;
        var now = DateTimeOffset.Now;
        int added = 0, skipped = 0, failed = 0;
        string? firstAddedId = null;
        SetStatus($"匯入中…（{importable.Count} 本）");

        await Task.Run(() =>
        {
            foreach (var p in importable)
            {
                try
                {
                    var r = _store.Add(p.Info!, p.Path, themeId, themeName, now); // 依 dc:identifier 去重、落地資料夾＋複本＋封面
                    if (r.Added) { added++; firstAddedId ??= r.Item.Id; }
                    else { skipped++; } // 匯入間之競態去重（同批同書）→ 略過
                }
                catch { failed++; }
            }
        });

        _picked.Clear();
        RefreshPickedFiles();
        if (firstAddedId is not null) { _selectedBookId = firstAddedId; } // 「匯入並開啟書櫃」：新書於左櫃選中
        RefreshBookshelf();
        SetStatus($"匯入完成：成功 {added} 本"
            + (skipped > 0 ? $"、略過 {skipped} 本" : "")
            + (failed > 0 ? $"、失敗 {failed} 本" : "") + "。");
    }

    /// <summary>匯入前彙總確認文字：逐列檔名／狀態，供一眼確認；末附略過與唯讀語意。</summary>
    private static string BuildImportConfirm(IReadOnlyList<(EbookPick Pick, EbookPickStatus Status)> statuses)
    {
        var importable = statuses.Count(x => x.Status == EbookPickStatus.Ready);
        var lines = statuses.Select(x =>
        {
            var mark = x.Status == EbookPickStatus.Ready ? "✔" : "⚠";
            return $"{mark} {x.Pick.FileName}\n     {DetailLine(x.Pick, x.Status)}";
        });
        return
            $"共 {statuses.Count} 個檔案，其中 {importable} 本可匯入：\n\n" +
            string.Join("\n", lines) +
            "\n\n已在書櫃者將略過、非 EPUB／解析失敗者不匯入。\n" +
            "匯入後會出現在左側書櫃；僅讀取解析，原檔複本會存到藏書資料夾（不改寫你的原檔）。\n\n要匯入嗎？";
    }

    private static string DetailLine(EbookPick p, EbookPickStatus status) => status switch
    {
        EbookPickStatus.Ready => p.Info is not null ? MetaLine(p.Info) : "就緒",
        EbookPickStatus.AlreadyExists => "已在書櫃" + (p.Info is not null ? " · " + MetaLine(p.Info) : ""),
        _ => p.Error ?? "無法解析",
    };

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    /// <summary>待匯入檔之處置狀態：就緒（可匯入）／已在書櫃（略過）／解析失敗（不匯入）。</summary>
    private enum EbookPickStatus { Ready, AlreadyExists, Failed }

    /// <summary>一個待匯入之本機 .epub 之預解析結果：路徑＋解析成功之 <see cref="EbookInfo"/>（失敗＝null＋<see cref="Error"/>）。「已在書櫃」於 <see cref="ComputeStatuses"/> 依當前書櫃另判、不入此。</summary>
    private sealed class EbookPick
    {
        public EbookPick(string path, EbookInfo? info, string? error)
        {
            Path = path;
            Info = info;
            Error = error;
        }

        public string Path { get; }
        public EbookInfo? Info { get; }
        public string? Error { get; }
        public bool ParseOk => Info is not null;
        public string FileName => System.IO.Path.GetFileName(Path);
    }
}
