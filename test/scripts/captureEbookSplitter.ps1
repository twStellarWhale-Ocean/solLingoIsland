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
  [string]$BookKeyword = "PHPCI",
  [string]$TextOnlyBookKeyword = "ZZ 純文字測試樣書"
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

  . "$PSScriptRoot\uiaCommon.ps1"
  Write-Host "* DPI 一致化（PER_MONITOR_AWARE_V2）＝$([Win32Ui]::MakeDpiAware())"
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

  #region A2.植入純文字樣書（無內嵌圖；供 intTest#70 之「整塊收起」不回歸項） --------------------------------
  Write-Host "## A2.植入純文字樣書 --------------------------------" -ForegroundColor Cyan
  # 本機書櫃未必有純文字書（含封面/題名頁圖之書仍算有圖），故就地產一本最小 EPUB3 樣書。
  # %APPDATA% 已於 A 備份、finally 還原，樣書不會留在使用者書櫃。
  $sampleFolder = "zz-test-textonly"
  $sampleDir    = Join-Path $appData ("ebook\" + $sampleFolder)
  New-Item -ItemType Directory -Path $sampleDir -Force | Out-Null
  $epubPath = Join-Path $sampleDir "zz-textonly.epub"
  Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
  if (Test-Path $epubPath) { Remove-Item $epubPath -Force }
  $zip = [System.IO.Compression.ZipFile]::Open($epubPath, [System.IO.Compression.ZipArchiveMode]::Create)
  $addEntry = {
    param($name, $text, $store)
    $lvl = if ($store) { [System.IO.Compression.CompressionLevel]::NoCompression } else { [System.IO.Compression.CompressionLevel]::Optimal }
    $e = $zip.CreateEntry($name, $lvl)
    $sw = New-Object System.IO.StreamWriter($e.Open(), (New-Object System.Text.UTF8Encoding($false)))
    $sw.Write($text); $sw.Flush(); $sw.Dispose()
  }
  # mimetype 須為首筆且不壓縮（EPUB 規範）
  & $addEntry "mimetype" "application/epub+zip" $true
  & $addEntry "META-INF/container.xml" '<?xml version="1.0" encoding="UTF-8"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>' $false
  & $addEntry "OEBPS/content.opf" '<?xml version="1.0" encoding="UTF-8"?><package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="bookid">urn:uuid:zz-textonly-sample</dc:identifier><dc:title>ZZ 純文字測試樣書</dc:title><dc:language>en</dc:language><meta property="dcterms:modified">2026-07-29T00:00:00Z</meta></metadata><manifest><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/><item id="ch1" href="ch1.xhtml" media-type="application/xhtml+xml"/></manifest><spine><itemref idref="ch1"/></spine></package>' $false
  & $addEntry "OEBPS/nav.xhtml" '<?xml version="1.0" encoding="UTF-8"?><html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>Contents</title></head><body><nav epub:type="toc"><ol><li><a href="ch1.xhtml">Chapter One</a></li></ol></nav></body></html>' $false
  & $addEntry "OEBPS/ch1.xhtml" '<?xml version="1.0" encoding="UTF-8"?><html xmlns="http://www.w3.org/1999/xhtml"><head><title>Chapter One</title></head><body><h1>Chapter One</h1><p>Anna: Good morning. This sample book has no embedded images at all.</p><p>Ben: Right. The scene image block and its splitter should be collapsed entirely.</p><p>Anna: The speaker filter row should sit right at the top of the reading column.</p></body></html>' $false
  $zip.Dispose()

  $ebooksJson = Join-Path $appData "ebooks.json"
  $shelf = Get-Content $ebooksJson -Raw -Encoding UTF8 | ConvertFrom-Json
  $sampleTitle = "ZZ 純文字測試樣書"
  $entry = [pscustomobject]@{
    Id = "zzzz0000000000000000000000000000"; DcIdentifier = "urn:uuid:zz-textonly-sample"
    Title = $sampleTitle; Author = "LingoIsland Test"; Language = "en"; ChapterCount = 1
    ThemeId = $null; ThemeName = $null; CoverFile = $null
    AddedAt = "2026-07-29T00:00:00.0000000+08:00"; Folder = $sampleFolder
    LastReadChapter = 0; LastReadParagraph = 0
  }
  $shelf.Items = @($shelf.Items) + $entry
  ($shelf | ConvertTo-Json -Depth 8) | Set-Content -Path $ebooksJson -Encoding UTF8
  Write-Host "* 已植入純文字樣書「$sampleTitle」（$sampleFolder）"
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
  $fg = Set-WindowForeground -Hwnd $hwnd
  if ($fg) { Write-Host "* 主視窗 hwnd=$hwnd 已在前景" }
  else { Write-Host ("* [提示] 前景為 hwnd=" + [Win32Ui]::GetForegroundWindow() + "；改以「點擊命中斷言」保證不被覆蓋") }

  $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
  #endregion

  #region C.導航：電子書分頁 → 雙擊書卡開書 --------------------------------
  Write-Host "## C.導航：電子書分頁 → 開書 --------------------------------" -ForegroundColor Cyan
  $tabEbook = Find-ByAutomationId -Root $root -Id "TabEbook"
  if ($null -eq $tabEbook) { throw "找不到「電子書」分頁鈕（AutomationId=TabEbook）" }
  $tabEbook.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 800

  $bookList = Find-ByAutomationId -Root $root -Id "BookList"
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
      $scroller = Find-ByAutomationId -Root $root -Id "ReadingScroller"
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
    $splitter = Find-ByAutomationId -Root $root -Id "ReaderImageSplitter"
    if ($null -ne $splitter) { break }
    Start-Sleep -Milliseconds 500
  }
  $filter = Find-ByAutomationId -Root $root -Id "ReaderSpeakerFilter"
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

  #region G2.拖拉後高度跨章保留（intTest#70 之不回歸項） --------------------------------
  Write-Host "## G2.拖拉後高度跨章保留 --------------------------------" -ForegroundColor Cyan
  $tree = Find-ByAutomationId -Root $root -Id "ChapterTree"
  if ($null -eq $tree) { throw "找不到章節清單（AutomationId=ChapterTree）" }
  $treeItemCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::TreeItem)
  $nodes = $tree.FindAll([System.Windows.Automation.TreeScope]::Descendants, $treeItemCond)
  $jumped = $false
  foreach ($n in $nodes) {
    if (-not $n.Current.IsOffscreen -and $n.GetSupportedPatterns() -contains [System.Windows.Automation.SelectionItemPattern]::Pattern) {
      $n.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
      Start-Sleep -Milliseconds 1200
      $jumped = $true
      Write-Host ("* 已跳至章節節點「{0}」" -f $n.Current.Name)
      break
    }
  }
  if (-not $jumped) { throw "章節清單中找不到可選取之節點，無法驗跨章保留" }
  $sp2 = $splitter.Current.BoundingRectangle
  $keepDiff = [math]::Round([math]::Abs($sp2.Y - $sp1.Y), 1)
  if ($keepDiff -gt $tolerancePx) {
    $fails += "切章後圖塊高度未保留（分隔線 Y 位移 $keepDiff px > 容差 $tolerancePx px）"
    Write-Host "* 跨章保留斷言：FAIL（位移 $keepDiff px）" -ForegroundColor Red
  } else {
    Write-Host "* 跨章保留斷言：PASS（切章後分隔線 Y 位移 $keepDiff px、維持拖拉後高度）" -ForegroundColor Green
  }
  #endregion

  #region G3.反向拖曳（往下）：反向消長且閱讀區不低於下限 --------------------------------
  Write-Host "## G3.反向拖曳（往下） --------------------------------" -ForegroundColor Cyan
  $spD0 = $splitter.Current.BoundingRectangle
  $scD0 = $scroller.Current.BoundingRectangle
  $cxD = [int]($spD0.X + $spD0.Width / 2); $cyD = [int]($spD0.Y + $spD0.Height / 2)
  if ([Win32Ui]::PidAtPoint($cxD, $cyD) -ne [uint32]$proc.Id) { throw "反向拖曳命中斷言失敗：($cxD,$cyD) 被他窗覆蓋" }
  [Win32Ui]::DragVertical($cxD, $cyD, $cyD + ($DragUpPx * 4))   # 刻意拖過頭，驗下限攔得住
  Start-Sleep -Milliseconds 500
  $spD1 = $splitter.Current.BoundingRectangle
  $scD1 = $scroller.Current.BoundingRectangle
  $flD1 = $filter.Current.BoundingRectangle
  $gapD1 = [math]::Round($flD1.Y - ($spD1.Y + $spD1.Height), 1)
  Write-Host ("* [量] 分隔線 Y {0:N1}→{1:N1}｜閱讀區 高 {2:N1}→{3:N1}｜分隔線→篩選列間距={4:N1}" -f $spD0.Y, $spD1.Y, $scD0.Height, $scD1.Height, $gapD1)
  if (($spD1.Y - $spD0.Y) -lt $minDeltaPx) { $fails += "往下拖時場景圖塊未變高（分隔線僅下移 $([math]::Round($spD1.Y - $spD0.Y,1)) px）" }
  if ($scD1.Height -ge $scD0.Height)       { $fails += "往下拖時閱讀區未縮小（$($scD0.Height)→$($scD1.Height)）" }
  if ($scD1.Height -lt 90)                 { $fails += "閱讀區被壓過下限（高 $($scD1.Height) px < 90 px）——MinHeight 未攔住" }
  if ([math]::Abs($gapD1 - $gap0) -gt $tolerancePx) { $fails += "反向拖曳仍撐開空白帶（間距 $gapD1 px vs 基準 $gap0 px）" }
  if ($fails.Count -eq 0) { Write-Host "* 反向拖曳斷言：PASS（反向消長、閱讀區守住下限、無空白帶）" -ForegroundColor Green }
  #endregion

  #region H.純文字書：整塊收起（intTest#70 之不回歸項） --------------------------------
  Write-Host "## H.純文字書：整塊收起 --------------------------------" -ForegroundColor Cyan
  if (-not $TextOnlyBookKeyword) {
    Write-Host "* 略過（未指定 -TextOnlyBookKeyword；本機書櫃無確定之純文字樣書）" -ForegroundColor Yellow
    $skippedTextOnly = $true
  } else {
    $t2 = $null; $seen2 = @()
    $items2 = $bookList.FindAll([System.Windows.Automation.TreeScope]::Children,
                [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($it in $items2) {
      foreach ($t in $it.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) {
        $n = $t.Current.Name
        if ($n) { $seen2 += $n }
        if ($n -like "*$TextOnlyBookKeyword*") { $t2 = $it; break }
      }
      if ($null -ne $t2) { break }
    }
    if ($null -eq $t2) { throw ("書櫃中找不到含「$TextOnlyBookKeyword」之書；現有：" + (($seen2 | Select-Object -Unique) -join " / ")) }
    $r2 = $t2.Current.BoundingRectangle
    $cx2 = [int]($r2.X + $r2.Width / 2); $cy2 = [int]($r2.Y + $r2.Height / 2)
    if ([Win32Ui]::PidAtPoint($cx2, $cy2) -ne [uint32]$proc.Id) { throw "點擊命中斷言失敗：($cx2,$cy2) 被他窗覆蓋" }
    [Win32Ui]::DoubleClick($cx2, $cy2)
    Start-Sleep -Milliseconds 2500
    # Collapsed 元素離開 UIA 樹：分隔線「不在樹中」即為整塊收起之直接證據
    $sp2 = Find-ByAutomationId -Root $root -Id "ReaderImageSplitter"
    $fl2 = Find-ByAutomationId -Root $root -Id "ReaderSpeakerFilter"
    if ($null -ne $sp2) {
      $fails += "純文字書仍出現場景圖分隔線（應與圖塊一併收起）"
      Write-Host "* 純文字書斷言：FAIL（分隔線仍在 UIA 樹中）" -ForegroundColor Red
    } else {
      Write-Host ("* 純文字書斷言：PASS（分隔線已離開 UIA 樹；篩選列 Y={0:N1}）" -f $fl2.Current.BoundingRectangle.Y) -ForegroundColor Green
    }
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
