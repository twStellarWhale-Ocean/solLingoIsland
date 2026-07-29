#requires -Version 7
<#
  design intTest#70（電子書內容頁·場景圖分隔線拖曳）之機判＋證據擷取管線。
  桌面 UIA e2e 工法依 [modTechStackWinApp] ＜III＞：PrintWindow 直取、前景確保重試、%APPDATA% 起手備份 finally 還原。
#>

param(
  [string]$ExePath = "",
  [string]$OutDir  = "",
  [string]$Tag     = "after",
  [int]$DragUpPx   = 160,
  [string]$BookKeyword = "PHPCI"
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

#region I.主旨目的 ================================
Write-Host "# I.主旨目的 ================================" -ForegroundColor Blue
Write-Host "* 驗證電子書【內容】頁場景圖下方分隔線之拖曳語意（design intTest#70）："
Write-Host "  往上拖 → 場景圖塊變矮、閱讀區同步變高（互為消長）；「顯示：」篩選列高度不變、不出現空白帶。"
Write-Host "* 同時產出拖曳前／後之實機截圖，供 README 產品手冊佐證。"
#endregion

#region II.參考準備 ================================
Write-Host "# II.參考準備 ================================" -ForegroundColor Blue

  #region A.參數準備 --------------------------------
  Write-Host "## A.參數準備 --------------------------------" -ForegroundColor Cyan

  $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
  if (-not $ExePath) {
    $ExePath = Join-Path $repoRoot "sysLingoIsland\bin\Debug\net9.0-windows10.0.19041.0\LingoIsland.exe"
  }
  if (-not $OutDir) { $OutDir = Join-Path $repoRoot "docs\manual-assets" }
  $appData     = Join-Path $env:APPDATA "LingoIsland"
  $backupDir   = Join-Path $env:TEMP ("LingoIsland-backup-" + (Get-Date -Format "yyyyMMddHHmmss"))
  $tolerancePx = 6      # 版面量測容差（DPI 取整）
  $minDeltaPx  = 40     # 視為「確實有消長」之最小位移

  Write-Host "* ExePath      = $ExePath"
  Write-Host "* OutDir       = $OutDir"
  Write-Host "* Tag          = $Tag"
  Write-Host "* DragUpPx     = $DragUpPx"
  Write-Host "* BookKeyword  = $BookKeyword"
  Write-Host "* APPDATA 備份 = $backupDir"

  if (-not (Test-Path $ExePath)) {
    Write-Host "* [錯誤] 找不到建置產物：$ExePath" -ForegroundColor Red
    Write-Host "  請先執行：dotnet build LingoIsland.slnx"
    exit 1
  }
  if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
  #endregion

  #region B.型別準備（P/Invoke／UIA） --------------------------------
  Write-Host "## B.型別準備（P/Invoke／UIA） --------------------------------" -ForegroundColor Cyan

  Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
  Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class Win32Ui
{
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    // DPI 一致化（必須早於任何座標取用）：本行程若為 DPI-unaware，GetWindowRect／SetCursorPos 會取得
    // 虛擬化（縮放後）座標，而 UIA BoundingRectangle 恆為實體像素——兩者對不上會使點擊落空、截圖失真。
    public static bool MakeDpiAware()
    {
        return SetProcessDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2
    }

    // 前景鎖繞道：背景行程呼叫 SetForegroundWindow 常被 Windows 前景鎖拒絕——
    // 先送一次 ALT 鍵（解除前景鎖之慣用法）再 SwitchToThisWindow＋SetForegroundWindow。
    public static void ForceForeground(IntPtr hWnd)
    {
        ShowWindow(hWnd, 9); // SW_RESTORE
        keybd_event(0x12, 0, 0, IntPtr.Zero);        // ALT down
        keybd_event(0x12, 0, 0x0002, IntPtr.Zero);   // ALT up
        // AttachThreadInput：把本行程輸入佇列接到當前前景視窗之執行緒，繞過前景鎖後再切換
        uint fgTid = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        uint myTid = GetCurrentThreadId();
        bool attached = (fgTid != myTid) && AttachThreadInput(myTid, fgTid, true);
        BringWindowToTop(hWnd);
        SwitchToThisWindow(hWnd, true);
        SetForegroundWindow(hWnd);
        if (attached) { AttachThreadInput(myTid, fgTid, false); }
    }

    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] public static extern uint GetWindowPid(IntPtr hWnd, out uint pid);

    public static uint PidOf(IntPtr hWnd) { uint pid; GetWindowPid(hWnd, out pid); return pid; }

    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    // 命中斷言（取代「必須是前景視窗」）：直接問 OS「螢幕上這個點是誰的視窗」——
    // 點若不屬受測行程，代表被他窗（Topmost 覆蓋卡等）擋住，點下去即假通過。
    public static uint PidAtPoint(int x, int y)
    {
        POINT p; p.X = x; p.Y = y;
        return PidOf(WindowFromPoint(p));
    }
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder s, int n);

    public static string WindowTitle(IntPtr hWnd)
    {
        var sb = new System.Text.StringBuilder(260);
        GetWindowTextW(hWnd, sb, 260);
        return sb.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;

    public static void DragVertical(int x, int yFrom, int yTo)
    {
        SetCursorPos(x, yFrom);
        System.Threading.Thread.Sleep(120);
        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(120);
        int step = yTo > yFrom ? 8 : -8;
        for (int y = yFrom; (step > 0 ? y < yTo : y > yTo); y += step)
        {
            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(12);
        }
        SetCursorPos(x, yTo);
        System.Threading.Thread.Sleep(150);
        mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(250);
    }

    public static void DoubleClick(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero); mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero); mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(400);
    }
}
"@

  $dpiOk = [Win32Ui]::MakeDpiAware()
  Write-Host "* DPI 一致化（PER_MONITOR_AWARE_V2）＝$dpiOk"

  # 截圖：PrintWindow 直取視窗表面（不受 Z 序影響），GDI+ 操作留在 PowerShell 端免 C# 參照相依
  function Save-WindowShot {
    param([IntPtr]$Hwnd, [string]$Path)
    $r = New-Object Win32Ui+RECT
    [Win32Ui]::GetWindowRect($Hwnd, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $full = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($full)
    $hdc = $g.GetHdc()
    [Win32Ui]::PrintWindow($Hwnd, $hdc, 0x2) | Out-Null   # PW_RENDERFULLCONTENT
    $g.ReleaseHdc($hdc); $g.Dispose()
    # DWM 隱形邊框內縮（左右各 7px、上 1px、下 7px）：畫布嚴格＝視窗裁切區、不留邊緣 padding
    $crop = New-Object System.Drawing.Rectangle(7, 1, [Math]::Max(1, $w - 14), [Math]::Max(1, $h - 8))
    $out = $full.Clone($crop, $full.PixelFormat)
    $out.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $out.Dispose(); $full.Dispose()
  }
  #endregion
#endregion

#region III.內容程序 ================================
Write-Host "# III.內容程序 ================================" -ForegroundColor Blue

$restored = $false
$proc = $null
try {

  #region A.APPDATA 起手備份（測試會改動使用者真實資料） --------------------------------
  Write-Host "## A.APPDATA 起手備份 --------------------------------" -ForegroundColor Cyan
  Get-Process -Name "LingoIsland" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit(5000) }
  Start-Sleep -Milliseconds 500
  if (Test-Path $appData) {
    Copy-Item -Path $appData -Destination $backupDir -Recurse -Force
    Write-Host "* 已備份 $appData → $backupDir"
  } else {
    Write-Host "* $appData 不存在，略過備份"
  }
  #endregion

  #region B.啟動 App 並前景確保 --------------------------------
  Write-Host "## B.啟動 App 並前景確保 --------------------------------" -ForegroundColor Cyan
  $proc = Start-Process -FilePath $ExePath -PassThru
  $hwnd = [IntPtr]::Zero
  for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $proc.MainWindowHandle; break }
  }
  if ($hwnd -eq [IntPtr]::Zero) { throw "主視窗未出現（逾時 30 秒）" }
  for ($i = 0; $i -lt 12; $i++) {
    [Win32Ui]::ForceForeground($hwnd)
    Start-Sleep -Milliseconds 400
    if ([Win32Ui]::GetForegroundWindow() -eq $hwnd) { break }
    # 最小化→還原：SetForegroundWindow 被前景鎖拒絕時（實撞：GameInputSvc 之隱形
    # GameInputServiceWindow 持有前景），此法可讓 Windows 主動把視窗帶到前景。
    if ($i -ge 2) {
      [Win32Ui]::ShowWindow($hwnd, 6) | Out-Null   # SW_MINIMIZE
      Start-Sleep -Milliseconds 300
      [Win32Ui]::ShowWindow($hwnd, 9) | Out-Null   # SW_RESTORE
      Start-Sleep -Milliseconds 500
      if ([Win32Ui]::GetForegroundWindow() -eq $hwnd) { break }
    }
  }
  $fg = [Win32Ui]::GetForegroundWindow()
  if ($fg -eq $hwnd) {
    Write-Host "* 主視窗已在前景"
  } else {
    Write-Host ("* [提示] 前景為 hwnd=$fg 標題＝「" + [Win32Ui]::WindowTitle($fg) + "」；改以「點擊命中斷言」保證不被覆蓋（見下）")
  }
  Write-Host "* 主視窗 hwnd=$hwnd 已在前景"
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
  #endregion

  #region C.導航：電子書分頁 → 雙擊書卡開書 --------------------------------
  Write-Host "## C.導航：電子書分頁 → 開書 --------------------------------" -ForegroundColor Cyan
  $byId = { param($id)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
  }

  $tabEbook = & $byId "TabEbook"
  if ($null -eq $tabEbook) { throw "找不到「電子書」分頁鈕（AutomationId=TabEbook）" }
  $tabEbook.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 800

  $bookList = & $byId "BookList"
  if ($null -eq $bookList) { throw "找不到書櫃清單（AutomationId=BookList）" }
  $items = $bookList.FindAll([System.Windows.Automation.TreeScope]::Children,
             [System.Windows.Automation.Condition]::TrueCondition)
  # 書卡 Content 為視覺面板、ListBoxItem 自身 Name 落回型別名——改讀其下 TextBlock（ControlType.Text）文字比對
  $textCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Text)
  $target = $null; $seen = @()
  foreach ($it in $items) {
    $texts = $it.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)
    foreach ($t in $texts) {
      $n = $t.Current.Name
      if ($n) { $seen += $n }
      if ($n -like "*$BookKeyword*") { $target = $it; break }
    }
    if ($null -ne $target) { break }
  }
  if ($null -eq $target) {
    throw ("書櫃中找不到含「$BookKeyword」之書；現有書卡文字：" + (($seen | Select-Object -Unique) -join " / "))
  }
  # 開書為非同步（ReadBookAsync）：雙擊後輪詢閱讀區出現才續行（Collapsed 元素不在 UIA 樹，可據此判 pane 已切換）；
  # 實體點擊（SendInput）偶有落空，故最多重試 3 輪、每輪點擊前重新取 rect 並確保前景。
  $scroller = $null
  for ($try = 1; $try -le 3 -and $null -eq $scroller; $try++) {
    [Win32Ui]::ForceForeground($hwnd); Start-Sleep -Milliseconds 300
    $r = $target.Current.BoundingRectangle
    $cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)
    Write-Host ("* 第 {0} 輪：雙擊書卡（含「{1}」）於 ({2},{3})" -f $try, $BookKeyword, $cx, $cy)
    $pidAt = [Win32Ui]::PidAtPoint($cx, $cy)
    if ($pidAt -ne [uint32]$proc.Id) { throw "點擊命中斷言失敗：($cx,$cy) 屬 pid=$pidAt、非受測行程 pid=$($proc.Id)——該點被他窗覆蓋，點下去即假通過" }
    [Win32Ui]::DoubleClick($cx, $cy)
    for ($i = 0; $i -lt 16; $i++) {
      Start-Sleep -Milliseconds 500
      $scroller = & $byId "ReadingScroller"
      if ($null -ne $scroller) { break }
    }
  }
  if ($null -eq $scroller) { throw "開書逾時：閱讀區（ReadingScroller）未出現，【內容】子頁籤可能未切換" }
  Write-Host "* 閱讀器已就緒"

  # 最大化（須在 App 套用 ui-state.json 之保存視窗尺寸「之後」才不會被覆寫）：
  # 小視窗下中欄可分配高度不足（閱讀區已在 MinHeight、圖塊被壓扁），拖曳無空間可分配、測不出消長。
  $wr = New-Object Win32Ui+RECT
  for ($i = 0; $i -lt 6; $i++) {
    [Win32Ui]::ShowWindow($hwnd, 3) | Out-Null   # SW_MAXIMIZE
    Start-Sleep -Milliseconds 900
    [Win32Ui]::GetWindowRect($hwnd, [ref]$wr) | Out-Null
    if (($wr.Bottom - $wr.Top) -gt 900) { break }
  }
  Write-Host ("* 視窗尺寸＝{0}x{1}" -f ($wr.Right - $wr.Left), ($wr.Bottom - $wr.Top))
  if (($wr.Bottom - $wr.Top) -le 900) { throw "視窗未能放大至可供拖曳之高度（目前高 $($wr.Bottom - $wr.Top) px）" }
  Start-Sleep -Milliseconds 600
  #endregion

  #region D.量測（拖曳前）＋截圖 --------------------------------
  Write-Host "## D.量測（拖曳前）＋截圖 --------------------------------" -ForegroundColor Cyan
  $splitter = $null
  for ($i = 0; $i -lt 20; $i++) {
    $splitter = & $byId "ReaderImageSplitter"
    if ($null -ne $splitter) { break }
    Start-Sleep -Milliseconds 500
  }
  $filter = & $byId "ReaderSpeakerFilter"
  if ($null -eq $splitter) {
    $ids = @()
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
             [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) { if ($e.Current.AutomationId -like "Reader*") { $ids += $e.Current.AutomationId } }
    throw ("找不到場景圖分隔線（ReaderImageSplitter，Collapsed 元素不入 UIA 樹）——本書可能無內嵌圖。現有 Reader* 元素：" + (($ids | Select-Object -Unique) -join " / "))
  }
  if ($null -eq $filter) { throw "找不到顯示篩選下拉（AutomationId=ReaderSpeakerFilter）" }

  $sp0 = $splitter.Current.BoundingRectangle
  $sc0 = $scroller.Current.BoundingRectangle
  $fl0 = $filter.Current.BoundingRectangle
  $gap0 = [math]::Round($fl0.Y - ($sp0.Y + $sp0.Height), 1)
  Write-Host ("* 分隔線 Y={0:N1}｜閱讀區 高={1:N1}｜篩選列 高={2:N1}｜分隔線→篩選列間距={3:N1}" -f $sp0.Y, $sc0.Height, $fl0.Height, $gap0)

  # 拖曳前之對照圖僅供比對、不入手冊（手冊只放最終態）：落暫存目錄、不進版控
  $before = Join-Path $env:TEMP ("ebook-splitter-$Tag-1-before-drag.png")
  Save-WindowShot -Hwnd $hwnd -Path $before
  Write-Host "* 截圖：$before"
  #endregion

  #region E.拖曳分隔線（往上 $DragUpPx px） --------------------------------
  Write-Host "## E.拖曳分隔線 --------------------------------" -ForegroundColor Cyan
  $cx = [int]($sp0.X + $sp0.Width / 2)
  $cy = [int]($sp0.Y + $sp0.Height / 2)
  $pidAt = [Win32Ui]::PidAtPoint($cx, $cy)
  if ($pidAt -ne [uint32]$proc.Id) { throw "拖曳命中斷言失敗：分隔線座標 ($cx,$cy) 屬 pid=$pidAt、非受測行程 pid=$($proc.Id)——被他窗覆蓋" }
  [Win32Ui]::DragVertical($cx, $cy, $cy - $DragUpPx)
  Start-Sleep -Milliseconds 500
  Write-Host "* 已自 ($cx,$cy) 往上拖 $DragUpPx px"
  #endregion

  #region F.量測（拖曳後）＋截圖 --------------------------------
  Write-Host "## F.量測（拖曳後）＋截圖 --------------------------------" -ForegroundColor Cyan
  $sp1 = $splitter.Current.BoundingRectangle
  $sc1 = $scroller.Current.BoundingRectangle
  $fl1 = $filter.Current.BoundingRectangle
  $gap1 = [math]::Round($fl1.Y - ($sp1.Y + $sp1.Height), 1)
  Write-Host ("* 分隔線 Y={0:N1}｜閱讀區 高={1:N1}｜篩選列 高={2:N1}｜分隔線→篩選列間距={3:N1}" -f $sp1.Y, $sc1.Height, $fl1.Height, $gap1)

  # 最終態（拖曳後）＝README 產品手冊證據圖，固定檔名、納入版控
  $after = Join-Path $OutDir ("ebook-splitter-drag.png")
  Save-WindowShot -Hwnd $hwnd -Path $after
  Write-Host "* 截圖：$after"
  #endregion

  #region G.斷言（intTest#70） --------------------------------
  Write-Host "## G.斷言（intTest#70） --------------------------------" -ForegroundColor Cyan
  $imgShrink  = [math]::Round($sp0.Y - $sp1.Y, 1)          # 場景圖塊縮小量（分隔線上移量）
  $readGrow   = [math]::Round($sc1.Height - $sc0.Height, 1) # 閱讀區增高量
  $filterDiff = [math]::Round([math]::Abs($fl1.Height - $fl0.Height), 1)
  $gapDiff    = [math]::Round([math]::Abs($gap1 - $gap0), 1)

  $fails = @()
  Write-Host ("* [量] 場景圖塊縮小 {0} px／閱讀區增高 {1} px／篩選列高度差 {2} px／分隔線-篩選列間距差 {3} px" -f $imgShrink, $readGrow, $filterDiff, $gapDiff)

  if ($imgShrink -lt $minDeltaPx) { $fails += "場景圖塊未確實縮小（$imgShrink px < $minDeltaPx px）——分隔線可能未被拖到" }
  if ($readGrow  -lt $minDeltaPx) { $fails += "閱讀區未隨之變高（$readGrow px < $minDeltaPx px）——分隔線之 Next 目標錯置（本缺陷 #265 之病徵）" }
  if ([math]::Abs($imgShrink - $readGrow) -gt $tolerancePx) { $fails += "圖塊縮小量與閱讀區增高量不相稱（差 $([math]::Abs($imgShrink - $readGrow)) px > 容差 $tolerancePx px）——高度被第三者吃掉" }
  if ($filterDiff -gt $tolerancePx) { $fails += "「顯示：」篩選列高度被拖曳改變（差 $filterDiff px）——篩選列不應參與分配" }
  if ($gapDiff    -gt $tolerancePx) { $fails += "分隔線與篩選列之間距被撐開（差 $gapDiff px）——出現空白帶" }

  if ($fails.Count -gt 0) {
    Write-Host "* 結果：FAIL" -ForegroundColor Red
    foreach ($f in $fails) { Write-Host "  - $f" -ForegroundColor Red }
  } else {
    Write-Host "* 結果：PASS（場景圖塊與閱讀區互為消長、篩選列不受影響、無空白帶）" -ForegroundColor Green
  }
  #endregion

} finally {
  #region IV.備註記錄（收尾：關閉 App、還原 APPDATA） ================================
  Write-Host "# IV.收尾 ================================" -ForegroundColor Blue
  if ($null -ne $proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) }
  Get-Process -Name "LingoIsland" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit(5000) }
  Start-Sleep -Milliseconds 500
  if (Test-Path $backupDir) {
    Remove-Item -Path $appData -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item -Path $backupDir -Destination $appData -Recurse -Force
    Remove-Item -Path $backupDir -Recurse -Force
    Write-Host "* 已自備份還原 $appData（測試資料不留存）"
    $restored = $true
  }
  #endregion
}

if ($fails -and $fails.Count -gt 0) { exit 1 }
exit 0
#endregion
