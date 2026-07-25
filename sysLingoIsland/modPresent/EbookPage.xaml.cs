using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using LingoIsland.Ebook;
using LingoIsland.Query;
using LingoIsland.Video;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Run = System.Windows.Documents.Run;
using Hyperlink = System.Windows.Documents.Hyperlink;
using Cursors = System.Windows.Input.Cursors;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using RoutedEventArgs = System.Windows.RoutedEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using FontWeight = System.Windows.FontWeight;
using TextWrapping = System.Windows.TextWrapping;
using CollectionViewSource = System.Windows.Data.CollectionViewSource;
using UserControl = System.Windows.Controls.UserControl;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using TreeViewItem = System.Windows.Controls.TreeViewItem;
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

    private readonly Func<ISpeechService?> _speechProvider; // 【內容】逐段 TTS（比照 VideoCapturePage）：委派取現行語音服務（設定換聲後仍取到新實例）

    /// <summary>【內容】段落逐字點選單字＝查該字（App 導向獨立字典視窗，沿用既有查詢，比照影片頁）。</summary>
    public event Action<string>? WordLookupRequested;

    /// <summary>【內容】加入我的筆記（當前段原文；App 重譯後入既有 NotesStore，比照影片頁）。</summary>
    public event Action<string>? AddToNotesRequested;

    /// <summary>【內容】某說話人所有段落原文批次收藏至指定資料夾（比照影片頁 #189-checklist；免 AI 翻譯由 App 端確認費用後逐句處理）：參數＝(資料夾名, 段落原文清單依段序)。</summary>
    public event Action<string, IReadOnlyList<string>>? AddSpeakerNotesRequested;

    public EbookPage(EbookStore store, ThemeStore themes, Func<ISpeechService?> speechProvider)
    {
        InitializeComponent();
        _store = store;
        _themes = themes;
        _speechProvider = speechProvider;

        // 子頁籤（版面統一）：獲得（書櫃＋匯入）／內容（三欄閱讀器），以可見性切換
        EbookTabAcquire.Checked += (_, _) => ShowSubTab(acquire: true);
        EbookTabContent.Checked += (_, _) => ShowSubTab(acquire: false);
        InitReader(); // 【內容】三欄閱讀器（切片2）之事件接線與初值

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

        // 切回本頁重填篩選＋reader「主題：」picker（反映主題增刪改）並重整書櫃
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) { PopulateThemeFilter(); if (_openBook is not null) { PopulateReaderThemePicker(_openBook); } RefreshBookshelf(); } };
        Focusable = true;
        PreviewKeyDown += OnReaderHotkey; // #6 內容頁快速鍵：←/→＝上/下一段、Space＝播放/繼續（不劫持輸入框/下拉）

        PopulateThemeFilter();
        RefreshBookshelf();   // #219：四 toggle 鈕視覺於此同步（單一同步點）
        RefreshPickedFiles();
    }

    /// <summary>切換子頁籤：獲得（書櫃＋匯入）／內容（三欄閱讀器），以可見性切換。切到內容時若尚未開書而書櫃有選取則自動開該書；切離內容即停止朗讀。</summary>
    private void ShowSubTab(bool acquire)
    {
        EbookAcquirePane.Visibility = acquire ? Visibility.Visible : Visibility.Collapsed;
        EbookContentPane.Visibility = acquire ? Visibility.Collapsed : Visibility.Visible;
        if (acquire) { StopTts(); return; } // 切離內容：停朗讀（切書/暫停即止家規）
        // 切到內容：尚未開書但書櫃有選取→自動開該書（單擊選書＋點內容子頁即讀）
        if (_openBookId is null && _selectedBookId is not null) { _ = OpenBookInReaderAsync(_selectedBookId); }
        Dispatcher.BeginInvoke(new Action(() => Focus()), System.Windows.Threading.DispatcherPriority.Input); // #6：取焦點供 ←→/Space
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

    // ============================================================================================================
    // 【內容】三欄逐段導讀閱讀器（切片2；[EbookPage] 內容閱讀器契約，spec#7–#10；比照 VideoCapturePage 內容頁三欄）
    // 左章節目錄｜中閱讀區（當前段放大高亮＋逐字可點）＋控制列｜右逐段清單＋說話人勾選面板。
    // line-stepped 導讀：以段序游標（章 index／段 index）前進，沿用 PauseDecider 家族（純函式 ParagraphStepper）。
    // ============================================================================================================

    private string? _openBookId;                                   // 目前開啟閱讀之書 Id（切書判定／進度存取鍵）
    private EbookItem? _openBook;                                  // 目前開啟之書卡（主題指派用）
    private IReadOnlyList<IReadOnlyList<SubtitleCue>> _chapters = System.Array.Empty<IReadOnlyList<SubtitleCue>>(); // 各章段落 cue（外層＝spine 章、內層＝段）
    private EbookBookContent? _content;                                                   // 增量3：整本內容（段落＋依位置場景圖＋圖片位元組）；_chapters 之 cue 由此投影
    private readonly Dictionary<string, System.Windows.Media.Imaging.BitmapImage> _imageCache = new(); // 場景圖解碼快取（key＝圖檔名）
    private readonly Dictionary<int, TreeViewItem> _chapterNodeBySpine = new(); // spine 章 index → 目錄樹節點（供高亮）；增量3 多層可收合目錄
    private bool _syncingChapterTree;                                            // 程式選取樹節點期間抑制 SelectedItemChanged→跳章
    private int _chapterIndex = -1;                                // 當前章（spine index；LastReadChapter）
    private int _cursor = -1;                                      // 當前段游標（章內段 index；LastReadParagraph）
    private bool _loadingBook;                                     // 開書中（防重入）
    private readonly List<Border> _paraViews = new();             // 中閱讀區各段之容器（供游標移動時就地重繪高亮，免全章重建）

    private readonly ObservableCollection<ParaRow> _paraRows = new(); // 右逐段清單（比照影片 CueRow）
    private ICollectionView? _paraView;                           // _paraRows 之檢視，套顯示模式篩選

    private readonly ObservableCollection<SpeakerCheck> _speakerChecks = new(); // 說話人勾選面板（全書；篩選/顯示/暫停共用）
    private SpeakerCheck? _everyoneCheck;
    private SpeakerCheck? _noSpeakerCheck;
    private bool _populatingModes;                                // 填下拉/勾選面板期間抑制 SelectionChanged/勾選事件
    private bool _syncingChecks;                                  // Everyone↔各列連動時抑制遞迴
    private readonly HashSet<string> _checkedNames = new(StringComparer.OrdinalIgnoreCase); // 已勾原子說話人（快取）
    private Dictionary<string, string> _speakerColorHex = new(StringComparer.OrdinalIgnoreCase); // 原子說話人→主題色 hex

    private enum RFilterMode { ShowAll, ShowSelected, BoldSelected, ColorSelected }
    private enum RPauseMode { Off, Selected }
    private RFilterMode _filterMode = RFilterMode.ShowAll;
    private RPauseMode _pauseMode = RPauseMode.Off;               // 預設不暫停（每段皆停＝逐段導讀）
    private bool _pausedAtStop;                                    // 於勾選者暫停停下時＝true；[繼續] 據此自 _cursor 之後起念、不重念暫停段（#234）

    private bool _ttsReading;                                      // 連續朗讀（唸完自動前進）中
    private int _ttsParagraph = -1;                               // 目前朗讀之段（供完成事件比對，防手動導航後誤前進）
    private ISpeechService? _subscribedSpeech;                     // 目前已訂閱 SpeakCompleted 之語音服務（換聲時改訂）

    private const string ReaderNoSpeaker = "（無說話人）";
    private const string ReaderEveryone = "（全部說話人）";

    /// <summary>目前章之段落 cue（章 index 越界回空）。</summary>
    private IReadOnlyList<SubtitleCue> CurCues => _chapterIndex >= 0 && _chapterIndex < _chapters.Count
        ? _chapters[_chapterIndex] : System.Array.Empty<SubtitleCue>();

    /// <summary>【內容】三欄閱讀器事件接線與初值（建構時呼叫一次）。</summary>
    private void InitReader()
    {
        // 書櫃雙擊＝開該書於閱讀器並切至內容子頁
        BookList.MouseDoubleClick += (_, _) =>
        {
            if ((BookList.SelectedItem as ListBoxItem)?.Tag is not EbookItem it) { return; }
            _selectedBookId = it.Id;
            EbookTabContent.IsChecked = true;    // 切至內容頁（已在則無效果、下一句直接開）
            _ = OpenBookInReaderAsync(it.Id);     // 直接開該書（切書換載；同書已開則 no-op；_loadingBook 防重入）
        };

        ChapterTree.SelectedItemChanged += (_, e) => { if (!_syncingChapterTree && e.NewValue is TreeViewItem ti && ti.Tag is int idx && idx >= 0) { JumpToChapter(idx); } }; // 增量3：多層可收合目錄→點章依 spine index 跳（純標題節點 idx=-1 不跳）

        ReaderPrevBtn.Click += (_, _) => StepPrev();
        ReaderNextBtn.Click += (_, _) => StepNext();
        ReaderResumeBtn.Click += (_, _) => TogglePlay();     // 播放/繼續：連續朗讀（只念對話、章末停）
        ReaderSpeakBtn.Click += (_, _) => ReadCurrentOnce(); // 朗讀單段：只念當前段一次
        ReaderAddNoteBtn.Click += (_, _) => AddCurrentParagraphNote();

        PopulateReaderSpeed();
        ReaderSpeed.SelectionChanged += (_, _) => { if (!_populatingModes) { ApplyReaderSpeed(); } };
        ReaderThemePicker.SelectionChanged += (_, _) => { if (!_populatingModes) { OnReaderThemeChanged(); } };

        ReaderSpeakerFilter.SelectionChanged += (_, _) => { if (!_populatingModes) { ApplyReaderFilterMode(); } };
        ReaderPauseAtSpeaker.SelectionChanged += (_, _) => { if (!_populatingModes) { ApplyReaderPauseMode(); } };
        _paraView = CollectionViewSource.GetDefaultView(_paraRows);
        _paraView.Filter = ParaRowFilter;
        ParaList.ItemsSource = _paraView;
        ParaList.MouseDoubleClick += (_, _) => { if (ParaList.SelectedItem is ParaRow r) { JumpToParagraph(r.Index); } };
        ParaList.ContextMenu = new ContextMenu();
        ParaList.ContextMenuOpening += OnParaContextMenuOpening;
        ParaList.PreviewMouseRightButtonDown += ListDeleteSupport.SelectItemUnderMouse; // 右鍵作用於游標下之段
        ReaderSpeakerChecks.ItemsSource = _speakerChecks;

        SetReaderControlsEnabled(false);
    }

    // ---- 開書／章節載入 ----

    /// <summary>開一本書於閱讀器：載入 info.json＋藏書 .epub 複本→逐章段落 cue，還原上次章/段（GetReadingProgress），建三欄。已開同書＝no-op。失敗明訊、不當機、唯讀原檔。</summary>
    private async Task OpenBookInReaderAsync(string bookId)
    {
        if (_loadingBook) { return; }
        if (_openBookId == bookId && _chapters.Count > 0) { return; } // 同書已開
        _loadingBook = true;
        StopTts();
        try
        {
            var item = _store.Load().Items.FirstOrDefault(i => i.Id == bookId);
            if (item is null) { SetStatus("找不到這本書，可能已被移除。"); return; }
            var display = string.IsNullOrWhiteSpace(item.Title) ? item.Id : item.Title;
            SetStatus($"開啟「{display}」中…");

            var (storedInfo, epubPath) = LocateBookFiles(item);
            if (storedInfo is null || epubPath is null)
            {
                SetStatus("無法開啟這本書——找不到藏書資料夾內的內容檔（.epub）。");
                return;
            }
            // 增量3：重新解析取新鮮目錄樹（含 Href——舊 info.json 無此鍵）＋一致 SpineHrefs；失敗退回 info.json。
            var info = (await EbookReader.ParseAsync(epubPath)).Info ?? storedInfo;
            var content = await EbookContentReader.ReadContentAsync(epubPath, info); // 增量3：讀整本→逐章段落＋依位置場景圖＋圖片位元組
            var chapters = content.Chapters
                .Select(ch => (IReadOnlyList<SubtitleCue>)ch.Select(p => p.Cue).ToList())
                .ToList();

            _openBookId = bookId;
            _openBook = item;
            _content = content;
            _imageCache.Clear();
            _chapters = chapters;
            SetupSceneImageBlock(); // 增量3：依本書有無圖固定圖塊（有→常駐固定、無→純文字），跨章不跳動
            _chapterIndex = -1; _cursor = -1;
            ReaderBookTitle.Text = display;
            BuildChapterTree(info);
            PopulateReaderThemePicker(item);
            BuildSpeakerChecks();       // 全書說話人（跨章）→ 主題色

            var firstReadable = NextNonEmptyChapter(0);
            if (firstReadable < 0)      // 整本無可閱讀段落（純圖像書／空 EPUB）
            {
                SetReaderControlsEnabled(false);
                ReadingPanel.Children.Clear(); _paraViews.Clear(); _paraRows.Clear();
                UpdateSceneImage(); // 純圖像書/空章：collapse 場景圖列（_cursor=-1→無 key）
                SetStatus("這本書沒有可閱讀的文字段落（可能是純圖像書）。");
                return;
            }

            var (ch, para) = _store.GetReadingProgress(bookId);   // 還原上次章/段
            ch = Math.Clamp(ch, 0, _chapters.Count - 1);
            if (_chapters[ch].Count == 0) { ch = NextNonEmptyChapter(ch); if (ch < 0) { ch = firstReadable; } para = 0; }
            SetReaderControlsEnabled(true);
            LoadChapter(ch);
            SetCursor(ParagraphStepper.ClampCursor(para, CurCues.Count), save: false);
            SetStatus($"已開啟「{display}」，共 {_chapters.Count} 章。點單字查詞、按喇叭朗讀（唸完自動前進）。");
        }
        catch (Exception ex)
        {
            SetStatus("開啟這本書時發生問題：" + ex.Message);
        }
        finally { _loadingBook = false; }
    }

    /// <summary>定位某書藏書資料夾內之 info.json（→<see cref="EbookInfo"/>）與 .epub 複本路徑；缺 .epub 回 (null,null)（唯讀、不改寫）。</summary>
    private (EbookInfo? Info, string? EpubPath) LocateBookFiles(EbookItem item)
    {
        try
        {
            var folder = _store.FolderPathFor(item);
            if (!Directory.Exists(folder)) { return (null, null); }
            var epub = Directory.EnumerateFiles(folder, "*.epub").FirstOrDefault();
            if (epub is null) { return (null, null); }
            var infoPath = Path.Combine(folder, "info.json");
            EbookInfo? info = File.Exists(infoPath)
                ? System.Text.Json.JsonSerializer.Deserialize<EbookInfo>(File.ReadAllText(infoPath))
                : null;
            // info.json 缺/毀：給空殼（SpineHrefs 空→ReadChaptersAsync 退回 EPUB ReadingOrder）
            info ??= new EbookInfo { Title = item.Title, Author = item.Author, Language = item.Language };
            return (info, epub);
        }
        catch { return (null, null); }
    }

    /// <summary>建左章節目錄（增量3 多層可收合樹狀）：走 <see cref="EbookInfo.Toc"/> 樹產生 <see cref="TreeViewItem"/>（Tag＝對應 spine 章 index）並建 spine→節點對照；無目錄樹退回每 spine 章一列。</summary>
    private void BuildChapterTree(EbookInfo info)
    {
        _chapterNodeBySpine.Clear();
        ChapterTree.Items.Clear();
        var byPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < info.SpineHrefs.Count; i++) { byPath[info.SpineHrefs[i]] = i; }

        TreeViewItem MakeNode(EbookTocNode n)
        {
            var spineIdx = ResolveSpineIndex(n.Href, byPath, info.SpineHrefs);
            var ti = new TreeViewItem
            {
                Header = string.IsNullOrWhiteSpace(n.Title) ? "—" : n.Title.Trim(),
                Tag = spineIdx,
                IsExpanded = true, // 預設展開、使用者可收合
            };
            if (spineIdx >= 0 && !_chapterNodeBySpine.ContainsKey(spineIdx)) { _chapterNodeBySpine[spineIdx] = ti; }
            foreach (var c in n.Children) { ti.Items.Add(MakeNode(c)); }
            return ti;
        }

        foreach (var n in info.Toc) { ChapterTree.Items.Add(MakeNode(n)); }
        if (ChapterTree.Items.Count == 0) // 無目錄樹（如壞 nav）→ 退回每 spine 章一列
        {
            for (int i = 0; i < _chapters.Count; i++)
            {
                var ti = new TreeViewItem { Header = $"第 {i + 1} 章", Tag = i };
                _chapterNodeBySpine[i] = ti;
                ChapterTree.Items.Add(ti);
            }
        }
        ReaderEmptyHint.Visibility = ChapterTree.Items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        HighlightChapter(_chapterIndex);
    }

    /// <summary>目錄項之目標檔 → spine 章 index：先比對完整 FilePath，退比對檔名；查無回 -1（純標題節點）。</summary>
    private static int ResolveSpineIndex(string? href, Dictionary<string, int> byPath, IReadOnlyList<string> spine)
    {
        if (string.IsNullOrWhiteSpace(href)) { return -1; }
        if (byPath.TryGetValue(href, out var idx)) { return idx; }
        var name = TocFileName(href);
        for (int i = 0; i < spine.Count; i++) { if (TocFileName(spine[i]) == name) { return i; } }
        return -1;
    }

    /// <summary>取路徑最後一段檔名（小寫）；純函式。</summary>
    private static string TocFileName(string path)
    {
        var s = path.Replace('\\', '/');
        var slash = s.LastIndexOf('/');
        return (slash >= 0 ? s[(slash + 1)..] : s).ToLowerInvariant();
    }

    /// <summary>高亮當前章所屬之目錄節點：其直接對應者，或前一個有對應之章（含未列於 TOC 之章，歸其前一章節段）；程式選取以旗標抑制 SelectedItemChanged 誤跳。</summary>
    private void HighlightChapter(int ch)
    {
        if (ch < 0 || _chapterNodeBySpine.Count == 0) { return; }
        TreeViewItem? target = null;
        for (int s = ch; s >= 0 && target is null; s--) { _chapterNodeBySpine.TryGetValue(s, out target); }
        if (target is null) { return; }
        _syncingChapterTree = true;
        target.IsSelected = true;
        target.BringIntoView();
        _syncingChapterTree = false;
    }

    /// <summary>載入某章之段落到中閱讀區與右逐段清單（不動游標；呼叫端隨後 SetCursor）。</summary>
    private void LoadChapter(int ch)
    {
        _chapterIndex = ch;
        var cues = CurCues;

        ReadingPanel.Children.Clear();
        _paraViews.Clear();
        for (int i = 0; i < cues.Count; i++)
        {
            int idx = i;
            var border = new Border
            {
                Tag = idx,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 2),
                CornerRadius = new CornerRadius(4),
                Cursor = Cursors.Hand,
                ContextMenu = BuildParagraphContextMenu(idx),
            };
            border.MouseLeftButtonUp += (_, e) => OnParagraphClicked(idx, e);
            _paraViews.Add(border);
            ReadingPanel.Children.Add(border);
            RenderParagraphInto(border, idx);
        }

        _paraRows.Clear();
        for (int i = 0; i < cues.Count; i++) { _paraRows.Add(new ParaRow(i, cues[i])); }
        RefreshParaColors();
        _paraView?.Refresh();
        HighlightChapter(ch);
    }

    // ---- 中閱讀區渲染 ----

    /// <summary>把某段渲染入其容器：當前段＝放大、粉底高亮、逐字可點（Hyperlink→查詞）＋說話人前綴上色；其餘段＝正常字級、淡色、純文字（點選成為當前段）。無前綴段不標說話人。</summary>
    private void RenderParagraphInto(Border border, int index)
    {
        var cues = CurCues;
        if (index < 0 || index >= cues.Count) { return; }
        var cue = cues[index];
        var isCurrent = index == _cursor;
        if (IsHeadingAt(index)) // 增量3：h1–h6 標題段渲染為章節標題（粗體、放大、非對白／不逐字可點）
        {
            border.Child = new TextBlock
            {
                Text = cue.Text,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.Bold,
                FontSize = isCurrent ? 19 : 16,
                Foreground = BrushOfHex("#8A4B63"),
                Margin = new Thickness(0, 8, 0, 2),
            };
            border.Background = isCurrent ? ReaderCurrentBg : System.Windows.Media.Brushes.Transparent;
            return;
        }
        var hex = ColorForSpeaker(cue.Speaker);
        if (_filterMode == RFilterMode.ColorSelected && !SpeakerChecked(cue.Speaker)) { hex = null; } // 只著色勾選者：未勾選者不上色
        var speakerBrush = hex is not null ? BrushOfHex(hex) : ReaderTextBrush;

        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = isCurrent ? 26 : 20 };
        if (!string.IsNullOrWhiteSpace(cue.Speaker))
        {
            tb.Inlines.Add(new Run(SpeakerPrefix(cue.Speaker)) { FontWeight = FontWeights.Bold, Foreground = speakerBrush });
        }
        if (isCurrent)
        {
            tb.FontSize = 17;
            foreach (var tok in EnglishWordTokenizer.Tokenize(cue.Text))
            {
                if (tok.IsWord)
                {
                    var word = tok.Text;
                    var link = new Hyperlink(new Run(word)) { Foreground = speakerBrush, Cursor = Cursors.Hand, TextDecorations = null };
                    link.Click += (_, _) => WordLookupRequested?.Invoke(word);
                    tb.Inlines.Add(link);
                }
                else { tb.Inlines.Add(new Run(tok.Text) { Foreground = speakerBrush }); }
            }
            border.Background = ReaderCurrentBg;
        }
        else
        {
            tb.FontSize = 13;
            tb.Inlines.Add(new Run(cue.Text) { Foreground = ReaderMutedBrush });
            border.Background = System.Windows.Media.Brushes.Transparent;
        }
        border.Child = tb;
    }

    /// <summary>中閱讀區點某段：當前段之單字點選由 Hyperlink 處理（此處不攔）；點其他段＝設為當前段（跳讀→停朗讀）。</summary>
    private void OnParagraphClicked(int index, MouseButtonEventArgs e)
    {
        if (index == _cursor) { return; }
        JumpToParagraph(index);
    }

    // ---- 段序游標與導航（line-stepped） ----

    /// <summary>移動段序游標：重繪舊/新當前段（放大高亮）、捲入中閱讀區、選中右清單對應列、存閱讀進度（SetReadingProgress）。</summary>
    private void SetCursor(int para, bool save = true)
    {
        var cues = CurCues;
        if (cues.Count == 0) { _cursor = -1; return; }
        para = ParagraphStepper.ClampCursor(para, cues.Count);
        var old = _cursor;
        _cursor = para;
        _pausedAtStop = false; // 任何游標移動＝離開暫停點（#234）
        UpdateSceneImage(); // 增量3：當前段之場景圖（隨閱讀位置換）
        if (old >= 0 && old < _paraViews.Count && old != para) { RenderParagraphInto(_paraViews[old], old); }
        if (para >= 0 && para < _paraViews.Count)
        {
            RenderParagraphInto(_paraViews[para], para);
            _paraViews[para].BringIntoView();
        }
        SelectParaRow(para);
        if (save && _openBookId is not null) { _store.SetReadingProgress(_openBookId, _chapterIndex, _cursor); }
    }

    private void SelectParaRow(int index)
    {
        var row = _paraRows.FirstOrDefault(r => r.Index == index);
        ParaList.SelectedItem = row; // row 可能為 null（被說話人篩選濾掉）→ 不選、正常
        if (row is not null) { ParaList.ScrollIntoView(row); }
    }

    // ---- 增量3：場景圖（隨閱讀位置換、整片圖為主，spec#11） ----

    /// <summary>當前段落生效之場景圖 key（圖檔名；無 <c>&lt;img&gt;</c>／無 _content／游標無效回 null）。</summary>
    private string? CurrentImageKey()
    {
        if (_content is null || _chapterIndex < 0 || _chapterIndex >= _content.Chapters.Count) { return null; }
        var ch = _content.Chapters[_chapterIndex];
        return _cursor >= 0 && _cursor < ch.Count ? ch[_cursor].ImageHref : null;
    }

    /// <summary>當前章第 <paramref name="index"/> 段是否為標題（h1–h6，增量3）：供 <see cref="RenderParagraphInto"/> 渲染為章節標題。</summary>
    private bool IsHeadingAt(int index)
    {
        if (_content is null || _chapterIndex < 0 || _chapterIndex >= _content.Chapters.Count) { return false; }
        var ch = _content.Chapters[_chapterIndex];
        return index >= 0 && index < ch.Count && ch[index].IsHeading;
    }

    /// <summary>依當前段更新場景圖之<b>影像來源</b>（前景完整＋背景模糊）；<b>圖塊大小/去留不在此改</b>——由 <see cref="SetupSceneImageBlock"/> 依本書有無圖固定，避免逐段跳動。該段無圖則清空影像（塊仍固定在）。</summary>
    private void UpdateSceneImage()
    {
        var key = CurrentImageKey();
        if (key is null || _content is null || !_content.Images.TryGetValue(key, out var bytes))
        {
            ReaderSceneImage.Source = null;
            ReaderSceneImageBg.Source = null; // 該段無圖：塊固定不動、僅清空影像（本書有圖時塊仍在）
            return;
        }
        var bmp = GetSceneBitmap(key, bytes);
        ReaderSceneImage.Source = bmp;    // 前景：完整整張（Uniform、不裁切）
        ReaderSceneImageBg.Source = bmp;  // 背景：模糊填滿留白（UniformToFill＋Blur）
    }

    /// <summary>依<b>本書</b>有無圖片決定<b>固定圖塊</b>（增量3）：有圖→圖塊常駐、可拖拉且高度跨章固定（不隨各章有無圖跳動）；純文字書→整塊收起。開書時呼叫一次。</summary>
    private void SetupSceneImageBlock()
    {
        if (_content is { Images.Count: > 0 })
        {
            ReaderSceneImageBox.Visibility = System.Windows.Visibility.Visible;
            ReaderImageSplitter.Visibility = System.Windows.Visibility.Visible;
            if (ReaderImageRow.Height.Value <= 0) { ReaderImageRow.Height = new System.Windows.GridLength(1.4, System.Windows.GridUnitType.Star); } // 保留使用者拖拉後高度
            ReaderImageSplitterRow.Height = System.Windows.GridLength.Auto;
        }
        else
        {
            ReaderSceneImageBox.Visibility = System.Windows.Visibility.Collapsed;
            ReaderImageSplitter.Visibility = System.Windows.Visibility.Collapsed;
            ReaderImageRow.Height = new System.Windows.GridLength(0);
            ReaderImageSplitterRow.Height = new System.Windows.GridLength(0);
            ReaderSceneImage.Source = null;
            ReaderSceneImageBg.Source = null;
        }
    }

    /// <summary>解碼並快取場景圖（key＝圖檔名；OnLoad 不鎖檔、Freeze 跨執行緒安全、DecodePixelWidth 節省記憶體）。</summary>
    private System.Windows.Media.Imaging.BitmapImage GetSceneBitmap(string key, byte[] bytes)
    {
        if (_imageCache.TryGetValue(key, out var cached)) { return cached; }
        var bi = new System.Windows.Media.Imaging.BitmapImage();
        bi.BeginInit();
        bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; // 載入即解碼、不鎖來源
        bi.StreamSource = new System.IO.MemoryStream(bytes);
        bi.EndInit();
        bi.Freeze();
        _imageCache[key] = bi;
        return bi;
    }

    /// <summary>上一段（相鄰段，raw ±1；比照影片導航直達）；章首則退上一非空章末段。朗讀中則於新段續讀。</summary>
    private void StepPrev()
    {
        if (_chapters.Count == 0) { return; }
        if (CurCues.Count > 0 && _cursor > 0) { NavigateTo(_cursor - 1, keepReading: _ttsReading); return; }
        var pc = PrevNonEmptyChapter(_chapterIndex - 1);
        if (pc >= 0) { GoToChapter(pc, LastParagraphOf(pc), keepReading: _ttsReading); }
    }

    /// <summary>下一段（相鄰段，raw ±1）；章末則進下一非空章首段。朗讀中則於新段續讀。</summary>
    private void StepNext()
    {
        if (_chapters.Count == 0) { return; }
        if (CurCues.Count > 0 && _cursor + 1 < CurCues.Count) { NavigateTo(_cursor + 1, keepReading: _ttsReading); return; }
        var nc = NextNonEmptyChapter(_chapterIndex + 1);
        if (nc >= 0) { GoToChapter(nc, 0, keepReading: _ttsReading); }
    }

    /// <summary>某段是否為「對話」（可朗讀）：有說話人（<c>Name: …</c>）且非標題；旁白／<c>h1–h6</c> 標題／中文＝非對話、朗讀跳過。</summary>
    private bool IsDialogueAt(int index)
    {
        var cues = CurCues;
        if (index < 0 || index >= cues.Count || IsHeadingAt(index)) { return false; }
        return !string.IsNullOrEmpty(cues[index].Speaker);
    }

    /// <summary>自 <paramref name="from"/> 之後找下一個「可朗讀對話段」（只念對話＝有 <c>Name:</c> 且非標題）；無則 -1（章末）。委派純函式 <see cref="ParagraphStepper.NextDialogue"/>。<b>念全部對話、不因勾選跳過</b>——勾選只決定暫停點（見 <see cref="PauseAfterCurrent"/>）；修 #234（舊實作誤把勾選當讀取濾鏡→只念勾選者、其餘全跳過）。</summary>
    private int NextReadable(int from) => ParagraphStepper.NextDialogue(CurCues, CurHeadingFlags(), from);

    /// <summary>當前章各段是否標題之旗標（供 <see cref="NextReadable"/> 純函式判斷；隨章重算，段數不多、成本可忽略）。</summary>
    private IReadOnlyList<bool> CurHeadingFlags()
    {
        var n = CurCues.Count;
        var flags = new bool[n];
        for (int i = 0; i < n; i++) { flags[i] = IsHeadingAt(i); }
        return flags;
    }

    /// <summary>「於勾選者暫停」：唸完當前段後是否停下——開了暫停模式、非全勾、且該段說話人被勾選（委派 <see cref="ParagraphStepper.PauseAfterReading"/>）。未開/全勾＝連續念到章末（#234）。</summary>
    private bool PauseAfterCurrent()
    {
        if (_pauseMode != RPauseMode.Selected || _everyoneCheck?.IsChecked == true) { return false; }
        return ParagraphStepper.PauseAfterReading(CurCues, _cursor, _checkedNames, _noSpeakerCheck?.IsChecked == true);
    }

    /// <summary>[播放/繼續]：朗讀中→暫停；否則自當前段起連續朗讀對話（<b>念全部對話</b>、唸完自動前進、<b>於勾選者暫停</b>、<b>章末停</b>、再按停）。自暫停點續念時自其後起、不重念暫停段（#234）。</summary>
    private void TogglePlay()
    {
        if (_chapters.Count == 0) { return; }
        if (_ttsReading) { StopTts(); SetStatus("已暫停。"); return; }
        var from = _pausedAtStop ? _cursor : _cursor - 1; // 自暫停點續念→跳過暫停段自其後起；否則自當前段(含)起
        _pausedAtStop = false;
        var start = NextReadable(from);
        if (start < 0) { SetStatus("本章沒有可朗讀的對話段。"); return; }
        _ttsReading = true;
        UpdateSpeakButton();
        if (start != _cursor) { SetCursor(start); }
        SpeakCurrentTts(stopPrevious: true);
    }

    /// <summary>[朗讀單段]：只念當前段一次、不自動前進（明確單段動作；非對話段以英文語音念＝近乎靜音，無害）。</summary>
    private void ReadCurrentOnce()
    {
        var cues = CurCues;
        if (_cursor < 0 || _cursor >= cues.Count) { return; }
        StopTts(); // 先停任何連續朗讀
        var svc = _speechProvider();
        if (svc is null) { SetStatus("目前沒有可用的語音服務，無法朗讀。"); return; }
        var text = cues[_cursor].Text;
        if (!string.IsNullOrWhiteSpace(text)) { svc.Speak(text, "en-US", stopPrevious: true); }
    }

    /// <summary>內容頁快速鍵（#6）：←＝上一段、→＝下一段、Space＝播放/繼續。輸入框／下拉聚焦時不劫持。</summary>
    private void OnReaderHotkey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (EbookContentPane.Visibility != Visibility.Visible || _chapters.Count == 0) { return; }
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox) { return; }
        switch (e.Key)
        {
            case Key.Left: StepPrev(); e.Handled = true; break;
            case Key.Right: StepNext(); e.Handled = true; break;
            case Key.Space: TogglePlay(); e.Handled = true; break;
        }
    }

    /// <summary>手動導航到同章某段（上/下一段/繼續共用）：先同步停朗讀（作廢待觸發之自動前進），移游標；原在朗讀則於下一輪 Dispatcher 重啟朗讀（generation guard：待觸發完成事件已被 disarm 排空）。</summary>
    private void NavigateTo(int index, bool keepReading)
    {
        StopTts();
        SetCursor(index);
        if (keepReading) { Dispatcher.BeginInvoke(new Action(RestartReadingAtCursor), System.Windows.Threading.DispatcherPriority.Background); }
    }

    /// <summary>跨章移動（含 TOC 跳章、章末續讀滾章）：載入該章、設游標；keepReading 時於新段重啟朗讀，否則停朗讀。</summary>
    private void GoToChapter(int ch, int para, bool keepReading)
    {
        StopTts();
        if (ch < 0 || ch >= _chapters.Count) { return; }
        LoadChapter(ch);
        SetCursor(para < 0 ? 0 : para);
        if (keepReading) { Dispatcher.BeginInvoke(new Action(RestartReadingAtCursor), System.Windows.Threading.DispatcherPriority.Background); }
    }

    /// <summary>雙擊左章節目錄＝跳章（跳讀→停朗讀）。</summary>
    private void JumpToChapter(int ch) => GoToChapter(ch, 0, keepReading: false);

    /// <summary>雙擊右逐段清單／點中閱讀區某段＝跳段（跳讀→停朗讀）。</summary>
    private void JumpToParagraph(int index) { StopTts(); SetCursor(index); }

    private int NextNonEmptyChapter(int from) { for (int c = Math.Max(0, from); c < _chapters.Count; c++) { if (_chapters[c].Count > 0) { return c; } } return -1; }
    private int PrevNonEmptyChapter(int from) { for (int c = Math.Min(from, _chapters.Count - 1); c >= 0; c--) { if (_chapters[c].Count > 0) { return c; } } return -1; }
    private int LastParagraphOf(int ch) => ch >= 0 && ch < _chapters.Count ? Math.Max(0, _chapters[ch].Count - 1) : 0;
    private int FirstStopOf(int ch)
    {
        if (ch < 0 || ch >= _chapters.Count) { return 0; }
        var (targets, noSpeaker) = EffectivePauseTargets();
        var s = ParagraphStepper.NextStop(_chapters[ch], -1, targets, noSpeaker);
        return s < 0 ? 0 : s;
    }

    // ---- TTS 逐段朗讀（唸完自動前進；比照影片 #208 generation guard） ----

    /// <summary>朗讀當前段（en-US；記朗讀段＝游標供完成事件比對）；無語音服務明確降級不當機；空段（理論上不存在）跳略前進。</summary>
    private void SpeakCurrentTts(bool stopPrevious)
    {
        EnsureSpeechSubscription();
        var cues = CurCues;
        if (_cursor < 0 || _cursor >= cues.Count) { StopTts(); return; }
        var svc = _speechProvider();
        if (svc is null) { StopTts(); SetStatus("目前沒有可用的語音服務，無法朗讀。"); return; }
        _ttsParagraph = _cursor;
        var text = cues[_cursor].Text;
        if (string.IsNullOrWhiteSpace(text)) { AdvanceTtsAfterCurrent(); return; }
        svc.Speak(text, "en-US", stopPrevious);
    }

    /// <summary>重啟朗讀於當前段（手動導航後之續讀；由 Dispatcher 下一輪呼叫，確保待觸發完成事件已被 disarm 排空）。</summary>
    private void RestartReadingAtCursor()
    {
        var start = NextReadable(_cursor - 1); // 自當前(含)起第一個可念對話段
        if (start < 0) { StopTts(); return; }
        _ttsReading = true;
        UpdateSpeakButton();
        if (start != _cursor) { SetCursor(start); }
        SpeakCurrentTts(stopPrevious: true);
    }

    /// <summary>停止朗讀（切書/跳讀/暫停/章變即止）：清朗讀旗標、取消語音（SpeakAsyncCancelAll——以空文字＋stopPrevious 觸發，不改 SpeechService 內部）。</summary>
    private void StopTts()
    {
        _ttsReading = false;
        _ttsParagraph = -1;
        try { _speechProvider()?.Speak("", "en-US", stopPrevious: true); } catch { /* 取消盡力 */ }
        UpdateSpeakButton();
    }

    /// <summary>朗讀完成（比照影片 #208 generation guard）：唸完（未被取消）且完成段仍＝當前游標才自動前進；否則（被取消／手動導航移了游標／已停朗讀）不前進，避免雙重跳段/誤讀。跨執行緒→Dispatcher。</summary>
    private void OnSpeechCompleted(object? sender, SpeakDoneEventArgs e)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnSpeechCompletedUi(e.Cancelled))); return; }
        OnSpeechCompletedUi(e.Cancelled);
    }

    private void OnSpeechCompletedUi(bool cancelled)
    {
        if (cancelled || !_ttsReading) { return; }   // 被中止／已停朗讀→不前進
        if (_ttsParagraph != _cursor) { return; }     // 手動導航已移游標→此完成作廢（generation guard）
        AdvanceTtsAfterCurrent();
    }

    /// <summary>唸完當前段後：<b>於勾選者暫停</b>——若當前段為暫停點即停下（<see cref="PauseAfterCurrent"/>）、不自動前進（供跟讀、按繼續念下一段）；否則自動前進到下一可朗讀對話段續唸；<b>章末即停</b>（不自動滾下一章）。修 #234。</summary>
    private void AdvanceTtsAfterCurrent()
    {
        if (PauseAfterCurrent()) // 於勾選者暫停：唸完勾選者之段即停、不自動前進
        {
            StopTts();
            _pausedAtStop = true;
            SetStatus("已於指定說話人暫停，按繼續念下一段。");
            return;
        }
        var next = NextReadable(_cursor);
        if (next >= 0)
        {
            SetCursor(next);
            SpeakCurrentTts(stopPrevious: false); // 鏈接：前段已唸完、無需取消
        }
        else { StopTts(); SetStatus("已朗讀至本章結尾。"); } // 章末停（不自動滾下一章）
    }

    /// <summary>確保 SpeakCompleted 訂閱到目前語音服務（設定換聲→App 換新 SpeechService 實例，改訂新的、免殘留）。</summary>
    private void EnsureSpeechSubscription()
    {
        var svc = _speechProvider();
        if (ReferenceEquals(svc, _subscribedSpeech)) { return; }
        if (_subscribedSpeech is not null) { _subscribedSpeech.SpeakCompleted -= OnSpeechCompleted; }
        _subscribedSpeech = svc;
        if (_subscribedSpeech is not null) { _subscribedSpeech.SpeakCompleted += OnSpeechCompleted; }
    }

    private void UpdateSpeakButton() => ReaderResumeBtn.Content = _ttsReading ? "⏸ 暫停" : "▶ 播放/繼續"; // 播放鈕切 播放/繼續↔暫停

    // ---- 說話人勾選面板／上色（比照影片頁；全書說話人） ----

    /// <summary>建說話人勾選面板（全書跨章去重）：全部說話人＋各原子說話人（合唸拆開）+語句數＋（有旁白段時）無說話人。保留原勾選、預設全勾；同步快取與主題配色。</summary>
    private void BuildSpeakerChecks()
    {
        var all = _chapters.SelectMany(c => c).ToList(); // 全書段落 cue（供說話人統計與配色）
        var prevChecked = _speakerChecks.Count > 0
            ? new HashSet<string>(_speakerChecks.Where(s => s.IsChecked).Select(s => s.Name), StringComparer.OrdinalIgnoreCase)
            : null;
        bool WasChecked(string name) => prevChecked is null || prevChecked.Contains(name);

        _populatingModes = true;
        foreach (var sc in _speakerChecks) { sc.PropertyChanged -= OnSpeakerCheckPropChanged; }
        _speakerChecks.Clear();
        _everyoneCheck = null; _noSpeakerCheck = null;

        var hasNoSpeaker = all.Any(c => string.IsNullOrEmpty(c.Speaker));
        var lineCounts = SpeakerTally.CountBySpeaker(all);
        var atoms = SpeakerTally.OrderByLineCountDesc(
            all.Where(c => !string.IsNullOrEmpty(c.Speaker))
               .SelectMany(c => PauseDecider.SplitSpeakers(c.Speaker))
               .Distinct(StringComparer.OrdinalIgnoreCase),
            lineCounts, StringComparer.OrdinalIgnoreCase);

        if (atoms.Count > 0 || hasNoSpeaker)
        {
            _everyoneCheck = AddCheck(new SpeakerCheck(ReaderEveryone, isEveryone: true) { LineCount = SpeakerTally.TotalCount(all) });
            foreach (var a in atoms) { AddCheck(new SpeakerCheck(a) { IsChecked = WasChecked(a), LineCount = lineCounts.TryGetValue(a, out var n) ? n : 0 }); }
            if (hasNoSpeaker) { _noSpeakerCheck = AddCheck(new SpeakerCheck(ReaderNoSpeaker, isNoSpeaker: true) { IsChecked = WasChecked(ReaderNoSpeaker), LineCount = SpeakerTally.NoSpeakerCount(all) }); }
            _everyoneCheck.IsChecked = _speakerChecks.Where(x => !x.IsEveryone).All(x => x.IsChecked);
        }
        _populatingModes = false;

        RebuildCheckedNames();
        RebuildSpeakerColors();

        SpeakerCheck AddCheck(SpeakerCheck sc)
        {
            sc.RowStripe = _speakerChecks.Count % 2 == 0 ? ReaderRowStripeEven : ReaderRowStripeOdd;
            sc.PropertyChanged += OnSpeakerCheckPropChanged;
            _speakerChecks.Add(sc);
            return sc;
        }
    }

    private void OnSpeakerCheckPropChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_populatingModes || _syncingChecks || sender is not SpeakerCheck sc) { return; }
        _syncingChecks = true;
        if (sc.IsEveryone) { foreach (var other in _speakerChecks) { if (!other.IsEveryone) { other.IsChecked = sc.IsChecked; } } }
        else if (_everyoneCheck is not null) { _everyoneCheck.IsChecked = _speakerChecks.Where(x => !x.IsEveryone).All(x => x.IsChecked); }
        _syncingChecks = false;
        RebuildCheckedNames();
        RefreshParaView();
        RefreshParaColors();
    }

    private void RebuildCheckedNames()
    {
        _checkedNames.Clear();
        foreach (var sc in _speakerChecks) { if (sc.IsChecked && !sc.IsEveryone && !sc.IsNoSpeaker) { _checkedNames.Add(sc.Name); } }
    }

    private bool SpeakerChecked(string? cueSpeaker)
    {
        if (string.IsNullOrEmpty(cueSpeaker)) { return _noSpeakerCheck?.IsChecked == true; }
        foreach (var a in PauseDecider.SplitSpeakers(cueSpeaker)) { if (_checkedNames.Contains(a)) { return true; } }
        return false;
    }

    /// <summary>某段首說話人之主題色 hex（該段第一個有配色之原子說話人色）；無則 null（不上色）。</summary>
    private string? ColorForSpeaker(string? cueSpeaker)
    {
        if (string.IsNullOrEmpty(cueSpeaker)) { return null; }
        foreach (var a in PauseDecider.SplitSpeakers(cueSpeaker)) { if (_speakerColorHex.TryGetValue(a, out var hex)) { return hex; } }
        return null;
    }

    /// <summary>依現用主題 12 色描述建每原子說話人字型色（比照影片 RebuildSpeakerColors）：描述含說話人名（不分大小寫）即用該色 hex；無主題/無命中→不上色。主題切換即重算並刷新清單與當前段。</summary>
    private void RebuildSpeakerColors()
    {
        _speakerColorHex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var theme = CurrentReaderTheme();
        if (theme is not null)
        {
            ThemeColors.Ensure(theme);
            foreach (var sc in _speakerChecks)
            {
                if (sc.IsEveryone || sc.IsNoSpeaker) { continue; }
                foreach (var col in theme.Colors)
                {
                    if (!string.IsNullOrWhiteSpace(col.Description) && !string.IsNullOrWhiteSpace(col.Hex)
                        && col.Description.Contains(sc.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        _speakerColorHex[sc.Name] = col.Hex.Trim();
                        break;
                    }
                }
            }
        }
        ApplySpeakerListColors();
        RefreshParaColors();
        if (_cursor >= 0 && _cursor < _paraViews.Count) { RenderParagraphInto(_paraViews[_cursor], _cursor); } // 當前段字色即時反映
    }

    private void ApplySpeakerListColors()
    {
        foreach (var sc in _speakerChecks)
        {
            System.Windows.Media.Brush brush = ReaderDefaultCueBrush;
            if (!sc.IsEveryone && !sc.IsNoSpeaker && _speakerColorHex.TryGetValue(sc.Name, out var hex))
            {
                var readable = ColorMath.ReadableOnLight(hex);
                if (!string.IsNullOrEmpty(readable)) { brush = BrushOfHex(readable); }
            }
            sc.NameBrush = brush;
        }
    }

    /// <summary>刷新右逐段清單各列字型色（依段首說話人主題色，過淺壓暗至白底可讀）＋粗體（只加粗勾選模式且該段被勾選）。</summary>
    private void RefreshParaColors()
    {
        var boldMode = _filterMode == RFilterMode.BoldSelected;
        var colorSelectedOnly = _filterMode == RFilterMode.ColorSelected;
        foreach (var row in _paraRows)
        {
            var checkedSpk = SpeakerChecked(row.Cue.Speaker);
            var bold = boldMode && checkedSpk;
            var hex = ColorForSpeaker(row.Cue.Speaker);
            if (colorSelectedOnly && !checkedSpk) { hex = null; } // 只著色勾選者：未勾選者恢復預設色
            row.SetEmphasis(hex, bold);
        }
    }

    private void RefreshParaView() => _paraView?.Refresh();

    private bool ParaRowFilter(object o)
    {
        if (o is not ParaRow row) { return true; }
        if (_filterMode != RFilterMode.ShowSelected) { return true; }
        return SpeakerChecked(row.Cue.Speaker);
    }

    // ---- 顯示模式／暫停模式／語速／主題 ----

    private void ApplyReaderFilterMode()
    {
        _filterMode = ReaderSpeakerFilter.SelectedIndex switch { 1 => RFilterMode.ShowSelected, 2 => RFilterMode.BoldSelected, 3 => RFilterMode.ColorSelected, _ => RFilterMode.ShowAll };
        RefreshParaView();
        RefreshParaColors();
        if (_cursor >= 0 && _cursor < _paraViews.Count) { RenderParagraphInto(_paraViews[_cursor], _cursor); } // 只著色模式：當前段字色即時反映
    }

    private void ApplyReaderPauseMode() => _pauseMode = ReaderPauseAtSpeaker.SelectedIndex == 1 ? RPauseMode.Selected : RPauseMode.Off;

    private void SyncReaderModeSelectors()
    {
        _populatingModes = true;
        ReaderSpeakerFilter.SelectedIndex = _filterMode switch { RFilterMode.ShowSelected => 1, RFilterMode.BoldSelected => 2, RFilterMode.ColorSelected => 3, _ => 0 };
        ReaderPauseAtSpeaker.SelectedIndex = _pauseMode == RPauseMode.Selected ? 1 : 0;
        _populatingModes = false;
    }

    /// <summary>導讀前進之暫停對象（繼續／朗讀自動前進用）：不暫停或全勾＝(null,false)＝每段皆停；否則＝(已勾集合, 是否含無說話人)＝只停勾選者（空集合→不停）。</summary>
    private (IReadOnlyCollection<string>? Targets, bool NoSpeaker) EffectivePauseTargets()
    {
        if (_pauseMode == RPauseMode.Off) { return (null, false); }
        if (_everyoneCheck?.IsChecked == true) { return (null, false); }
        return (_checkedNames, _noSpeakerCheck?.IsChecked == true);
    }

    private void SetReaderControlsEnabled(bool on)
    {
        ReaderPrevBtn.IsEnabled = on;
        ReaderNextBtn.IsEnabled = on;
        ReaderResumeBtn.IsEnabled = on;
        ReaderSpeakBtn.IsEnabled = on;
        ReaderAddNoteBtn.IsEnabled = on;
        ReaderSpeed.IsEnabled = on;
        ReaderThemePicker.IsEnabled = on;
        ReaderSpeakerFilter.IsEnabled = on;
        ReaderPauseAtSpeaker.IsEnabled = on;
        ReaderSpeakerChecks.IsEnabled = on;
        if (on) { SyncReaderModeSelectors(); }
    }

    private void PopulateReaderSpeed()
    {
        _populatingModes = true;
        ReaderSpeed.Items.Clear();
        int sel = 0, i = 0;
        for (int pct = 50; pct <= 150; pct += 10)
        {
            ReaderSpeed.Items.Add(new ComboBoxItem { Content = pct + "%", Tag = pct });
            if (pct == Math.Clamp(SpeechRateSettings.Percent, 50, 150)) { sel = i; }
            i++;
        }
        ReaderSpeed.SelectedIndex = sel;
        _populatingModes = false;
    }

    private void ApplyReaderSpeed()
    {
        if ((ReaderSpeed.SelectedItem as ComboBoxItem)?.Tag is int pct) { SpeechRateSettings.Percent = pct; }
    }

    private void PopulateReaderThemePicker(EbookItem item)
    {
        _populatingModes = true;
        ThemeFilter.PopulatePicker(ReaderThemePicker, _themes, item.ThemeId);
        _populatingModes = false;
    }

    /// <summary>主題下拉改變：改指派此書所屬主題（存回書櫃）＋依新主題重算說話人上色。</summary>
    private void OnReaderThemeChanged()
    {
        if (_openBookId is null) { return; }
        var id = ThemeFilter.PickedThemeId(ReaderThemePicker);
        var name = id is null ? null : ThemeStore.Find(_themes.Load(), id)?.Name;
        _store.UpdateTheme(_openBookId, id, name);
        if (_openBook is not null) { _openBook.ThemeId = id; _openBook.ThemeName = name; }
        RebuildSpeakerColors();
    }

    private ThemeItem? CurrentReaderTheme()
    {
        var id = ThemeFilter.PickedThemeId(ReaderThemePicker);
        return id is null ? null : ThemeStore.Find(_themes.Load(), id);
    }

    // ---- 加入筆記／複製／說話人批次筆記 ----

    /// <summary>加入筆記：當前段原文→事件（App 重譯後入既有 NotesStore）。</summary>
    private void AddCurrentParagraphNote()
    {
        var cues = CurCues;
        if (_cursor < 0 || _cursor >= cues.Count) { return; }
        var t = cues[_cursor].Text;
        if (!string.IsNullOrWhiteSpace(t)) { AddToNotesRequested?.Invoke(t); }
    }

    private ContextMenu BuildParagraphContextMenu(int index)
    {
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "複製此段" };
        copy.Click += (_, _) => { if (index < CurCues.Count) { TryCopy(CurCues[index].Text); } };
        menu.Items.Add(copy);
        var note = new MenuItem { Header = "加入筆記" };
        note.Click += (_, _) => { if (index < CurCues.Count) { var t = CurCues[index].Text; if (!string.IsNullOrWhiteSpace(t)) { AddToNotesRequested?.Invoke(t); } } };
        menu.Items.Add(note);
        return menu;
    }

    /// <summary>右逐段清單右鍵選單（依游標下之段動態填入）：複製此段原文＋加入筆記。</summary>
    private void OnParaContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = ParaList.ContextMenu!;
        menu.Items.Clear();
        if (ParaList.SelectedItem is not ParaRow row) { e.Handled = true; return; }
        var copy = new MenuItem { Header = "複製此段" };
        copy.Click += (_, _) => TryCopy(row.Cue.Text);
        menu.Items.Add(copy);
        var note = new MenuItem { Header = "加入筆記" };
        note.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(row.Cue.Text)) { AddToNotesRequested?.Invoke(row.Cue.Text); } };
        menu.Items.Add(note);
    }

    private static void TryCopy(string? text)
    {
        if (string.IsNullOrEmpty(text)) { return; }
        try { System.Windows.Clipboard.SetText(text); } catch { /* 剪貼簿暫占用等—忽略 */ }
    }

    /// <summary>說話人面板某列「加入筆記」鈕：把該說話人全書所有段落原文收藏至〔書名-說話人〕資料夾（App 端確認費用後逐句翻譯）。全部說話人列不觸發。</summary>
    private void OnAddSpeakerNotesClick(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not SpeakerCheck sc || sc.IsEveryone) { return; }
        var label = sc.IsNoSpeaker ? "旁白" : sc.Name;
        var lines = ParagraphsForSpeaker(sc).ToList();
        if (lines.Count == 0) { SetStatus($"找不到「{label}」的任何段落。"); return; }
        AddSpeakerNotesRequested?.Invoke(SpeakerNotesFolder(label), lines);
    }

    private IEnumerable<string> ParagraphsForSpeaker(SpeakerCheck sc)
    {
        foreach (var c in _chapters.SelectMany(ch => ch))
        {
            var match = sc.IsNoSpeaker
                ? string.IsNullOrEmpty(c.Speaker)
                : !string.IsNullOrEmpty(c.Speaker) && PauseDecider.SplitSpeakers(c.Speaker).Any(a => string.Equals(a, sc.Name, StringComparison.OrdinalIgnoreCase));
            if (match && !string.IsNullOrWhiteSpace(c.Text)) { yield return c.Text; }
        }
    }

    private string SpeakerNotesFolder(string speaker)
    {
        var title = (_openBook?.Title ?? "").Trim();
        if (title.Length == 0) { title = _openBookId ?? "ebook"; }
        if (title.Length > 40) { title = title[..40].Trim(); }
        return $"{title} - {speaker}";
    }

    // ---- 閱讀器字型色（比照影片頁；凍結共用） ----

    private static readonly System.Windows.Media.SolidColorBrush ReaderTextBrush = FrozenBrush(0x3A, 0x2C, 0x33);   // 當前段內文預設色
    private static readonly System.Windows.Media.SolidColorBrush ReaderMutedBrush = FrozenBrush(0x8A, 0x7A, 0x82);  // 非當前段淡色
    private static readonly System.Windows.Media.Brush ReaderDefaultCueBrush = FrozenBrush(0x2A, 0x2A, 0x2A);       // 清單預設近黑
    private static readonly System.Windows.Media.Brush ReaderCurrentBg = FrozenBrush(0xFB, 0xE3, 0xEC);             // 當前段高亮粉底
    private static readonly System.Windows.Media.Brush ReaderRowStripeEven = System.Windows.Media.Brushes.Transparent;
    private static readonly System.Windows.Media.Brush ReaderRowStripeOdd = FrozenBrush(0xFA, 0xE8, 0xEF);

    private static System.Windows.Media.SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var br = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    private static readonly Dictionary<string, System.Windows.Media.Brush> ReaderHexBrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static System.Windows.Media.Brush BrushOfHex(string hex)
    {
        if (ReaderHexBrushCache.TryGetValue(hex, out var b)) { return b; }
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        var br = new System.Windows.Media.SolidColorBrush(color); br.Freeze();
        ReaderHexBrushCache[hex] = br;
        return br;
    }

    /// <summary>段首說話人前綴（有前綴才呼叫；無前綴段不標，契約 spec#10）。</summary>
    private static string SpeakerPrefix(string? speaker) => (speaker ?? "").Trim() + ": ";

    /// <summary>右逐段清單一列 view-model（比照影片 CueRow）：段 index＋段首說話人前綴＋原文＋字型色/字重（過淺壓暗至白底可讀）。</summary>
    private sealed class ParaRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public ParaRow(int index, SubtitleCue cue) { Index = index; Cue = cue; }
        public int Index { get; }
        public SubtitleCue Cue { get; }
        public string SpeakerLabel => string.IsNullOrWhiteSpace(Cue.Speaker) ? "" : Cue.Speaker + ": ";
        public string Text => Cue.Text;

        private System.Windows.Media.Brush _speakerBrush = ReaderDefaultCueBrush;
        public System.Windows.Media.Brush SpeakerBrush { get => _speakerBrush; private set { if (!ReferenceEquals(_speakerBrush, value)) { _speakerBrush = value; Raise(nameof(SpeakerBrush)); } } }
        private FontWeight _lineWeight = FontWeights.Normal;
        public FontWeight LineWeight { get => _lineWeight; private set { if (_lineWeight != value) { _lineWeight = value; Raise(nameof(LineWeight)); } } }

        /// <summary>設本列字型色（hex 非 null＝該說話人有主題色，過淺壓暗）＋是否加粗（只加粗勾選模式）。</summary>
        public void SetEmphasis(string? hex, bool bold)
        {
            var readable = hex is null ? null : ColorMath.ReadableOnLight(hex);
            SpeakerBrush = !string.IsNullOrEmpty(readable) ? BrushOfHex(readable) : ReaderDefaultCueBrush;
            LineWeight = bold ? FontWeights.Bold : FontWeights.Normal;
        }
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>說話人勾選面板一列（比照影片 SpeakerCheck）：名字＋是否 Everyone／(no speaker)＋語句數＋勾選態（TwoWay）＋列色。</summary>
    private sealed class SpeakerCheck : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public SpeakerCheck(string name, bool isEveryone = false, bool isNoSpeaker = false) { Name = name; IsEveryone = isEveryone; IsNoSpeaker = isNoSpeaker; }
        public string Name { get; }
        public bool IsEveryone { get; }
        public bool IsNoSpeaker { get; }
        public int LineCount { get; init; }
        public string DisplayName => $"{Name} ({LineCount})";
        public FontWeight Weight => IsEveryone ? FontWeights.Bold : FontWeights.Normal;
        public Visibility AddNotesVisibility => IsEveryone ? Visibility.Collapsed : Visibility.Visible;
        public System.Windows.Media.Brush RowStripe { get; set; } = System.Windows.Media.Brushes.Transparent;
        private System.Windows.Media.Brush _nameBrush = ReaderDefaultCueBrush;
        public System.Windows.Media.Brush NameBrush { get => _nameBrush; set { if (!ReferenceEquals(_nameBrush, value)) { _nameBrush = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameBrush))); } } }
        private bool _checked = true;
        public bool IsChecked { get => _checked; set { if (_checked != value) { _checked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); } } }
    }

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
