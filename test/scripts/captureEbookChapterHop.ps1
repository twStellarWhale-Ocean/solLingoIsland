#requires -Version 7
<#
  design intTest#71（電子書內容頁·章界雙擊換章）之實機接線驗證。
  判定本體之全分支由單元測試 ChapterHopDeciderTests 涵蓋；本腳本只驗「接線確實生效」——
  章末雙擊 ↓／Space 確實換章、章中雙擊不換章、章首雙擊 ↑ 回上一章。
  桌面 UIA e2e 工法依 [modTechStackWinApp] ＜III＞；共用定義見 uiaCommon.ps1。
#>

param(
  [string]$ExePath = "",
  [int]$TapGapMs = 120        # 兩下之間隔（須 < ChapterHopDecider.DoubleTapWindowMs 400ms）
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

#region I.主旨目的 ================================
Write-Host "# I.主旨目的 ================================" -ForegroundColor Blue
Write-Host "* 驗證電子書【內容】頁之章界雙擊換章接線（design intTest#71）："
Write-Host "  章末快速連按兩下 ↓／Space → 進下一章；章首連按兩下 ↑ → 回上一章；章中連按兩下 → 不換章。"
#endregion

#region II.參考準備 ================================
Write-Host "# II.參考準備 ================================" -ForegroundColor Blue

  #region A.參數準備 --------------------------------
  Write-Host "## A.參數準備 --------------------------------" -ForegroundColor Cyan
  $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
  if (-not $ExePath) { $ExePath = Join-Path $repoRoot "sysLingoIsland\bin\Debug\net9.0-windows10.0.19041.0\LingoIsland.exe" }
  $appData   = Join-Path $env:APPDATA "LingoIsland"
  $backupDir = Join-Path $env:TEMP ("LingoIsland-backup-hop-" + (Get-Date -Format "yyyyMMddHHmmss"))
  $sampleFolder = "zz-test-chapterhop"
  $sampleTitle  = "ZZ 換章測試樣書"
  $VK_DOWN = 0x28; $VK_UP = 0x26; $VK_SPACE = 0x20
  Write-Host "* ExePath   = $ExePath"
  Write-Host "* TapGapMs  = $TapGapMs（門檻 400ms）"
  Write-Host "* 備份      = $backupDir"
  if (-not (Test-Path $ExePath)) { Write-Host "* [錯誤] 找不到建置產物：$ExePath（請先 dotnet build）" -ForegroundColor Red; exit 1 }
  #endregion

  #region B.型別準備（共用定義） --------------------------------
  Write-Host "## B.型別準備（共用定義） --------------------------------" -ForegroundColor Cyan
  . "$PSScriptRoot\uiaCommon.ps1"
  Write-Host "* DPI 一致化（PER_MONITOR_AWARE_V2）＝$([Win32Ui]::MakeDpiAware())"
  #endregion
#endregion

#region III.內容程序 ================================
Write-Host "# III.內容程序 ================================" -ForegroundColor Blue

$fails = @()
$app = $null
try {

  #region A.APPDATA 備份＋植入兩章樣書 --------------------------------
  Write-Host "## A.APPDATA 備份＋植入兩章樣書 --------------------------------" -ForegroundColor Cyan
  Get-Process -Name "LingoIsland" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit(5000) }
  Start-Sleep -Milliseconds 500
  Copy-Item -Path $appData -Destination $backupDir -Recurse -Force
  Write-Host "* 已備份 $appData → $backupDir"

  # 兩章 × 三段之最小 EPUB3：段數固定才好精準走到章界；各章帶可辨識標記供斷言判別當前章。
  $sampleDir = Join-Path $appData ("ebook\" + $sampleFolder)
  New-Item -ItemType Directory -Path $sampleDir -Force | Out-Null
  $epubPath = Join-Path $sampleDir "zz-chapterhop.epub"
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
  & $addEntry "mimetype" "application/epub+zip" $true
  & $addEntry "META-INF/container.xml" '<?xml version="1.0" encoding="UTF-8"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>' $false
  & $addEntry "OEBPS/content.opf" '<?xml version="1.0" encoding="UTF-8"?><package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="bookid">urn:uuid:zz-chapterhop-sample</dc:identifier><dc:title>ZZ 換章測試樣書</dc:title><dc:language>en</dc:language><meta property="dcterms:modified">2026-07-29T00:00:00Z</meta></metadata><manifest><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/><item id="ch1" href="ch1.xhtml" media-type="application/xhtml+xml"/><item id="ch2" href="ch2.xhtml" media-type="application/xhtml+xml"/></manifest><spine><itemref idref="ch1"/><itemref idref="ch2"/></spine></package>' $false
  & $addEntry "OEBPS/nav.xhtml" '<?xml version="1.0" encoding="UTF-8"?><html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>Contents</title></head><body><nav epub:type="toc"><ol><li><a href="ch1.xhtml">Chapter One</a></li><li><a href="ch2.xhtml">Chapter Two</a></li></ol></nav></body></html>' $false
  & $addEntry "OEBPS/ch1.xhtml" '<?xml version="1.0" encoding="UTF-8"?><html xmlns="http://www.w3.org/1999/xhtml"><head><title>C1</title></head><body><p>Anna: MARKERALPHA first paragraph of the first chapter.</p><p>Ben: MARKERALPHA second paragraph of the first chapter.</p><p>Anna: MARKERALPHA third and last paragraph of the first chapter.</p></body></html>' $false
  & $addEntry "OEBPS/ch2.xhtml" '<?xml version="1.0" encoding="UTF-8"?><html xmlns="http://www.w3.org/1999/xhtml"><head><title>C2</title></head><body><p>Ben: MARKERBRAVO first paragraph of the second chapter.</p><p>Anna: MARKERBRAVO second paragraph of the second chapter.</p><p>Ben: MARKERBRAVO third and last paragraph of the second chapter.</p></body></html>' $false
  $zip.Dispose()

  $ebooksJson = Join-Path $appData "ebooks.json"
  $shelf = Get-Content $ebooksJson -Raw -Encoding UTF8 | ConvertFrom-Json
  $shelf.Items = @($shelf.Items) + ([pscustomobject]@{
    Id = "zzzz1111111111111111111111111111"; DcIdentifier = "urn:uuid:zz-chapterhop-sample"
    Title = $sampleTitle; Author = "LingoIsland Test"; Language = "en"; ChapterCount = 2
    ThemeId = $null; ThemeName = $null; CoverFile = $null
    AddedAt = "2026-07-29T00:00:00.0000000+08:00"; Folder = $sampleFolder
    LastReadChapter = 0; LastReadParagraph = 0
  })
  ($shelf | ConvertTo-Json -Depth 8) | Set-Content -Path $ebooksJson -Encoding UTF8
  Write-Host "* 已植入兩章樣書「$sampleTitle」（各章 3 段、帶 MARKERALPHA／MARKERBRAVO 標記）"
  #endregion

  #region B.啟動 App、開書 --------------------------------
  Write-Host "## B.啟動 App、開書 --------------------------------" -ForegroundColor Cyan
  # 取窗工法見 uiaCommon.ps1 之 Start-AppAndGetWindow（Issue #270）：以「具主視窗之 LingoIsland 行程」
  # 為錨、非 Start-Process 回傳之行程，故對 dev build 與 Velopack 打包成品皆適用。
  $app  = Start-AppAndGetWindow -ExePath $ExePath -TimeoutSec 30
  $hwnd = $app.Hwnd
  Write-Host ("* 受測行程 pid={0}（自主視窗反查）" -f $app.ProcessId)
  $fg = Set-WindowForeground -Hwnd $hwnd
  if (-not $fg) { throw "主視窗無法取得前景——本腳本以真實鍵盤事件驅動，鍵盤焦點必須在受測視窗，否則按鍵會送到別的程式（不可放行）" }
  Write-Host "* 主視窗 hwnd=$hwnd 已在前景"
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

  # 最大化（Issue #270）：視窗沿用 ui-state.json 之保存尺寸時可能偏小，植入之樣書排在書櫃清單末端、
  # 書卡中心落在視窗可視範圍外——該點屬桌面或他窗，下方點擊命中斷言即判「被他窗覆蓋」而中止
  # （實撞：本機保存尺寸下書卡中心 y=909 落在視窗外）。比照 captureEbookSplitter.ps1，
  # 須在 App 套用 ui-state.json「之後」才最大化，否則會被其覆寫。
  $wr = Set-WindowMaximized -Hwnd $hwnd
  Write-Host ("* 視窗尺寸＝{0}x{1}" -f ($wr.Right - $wr.Left), ($wr.Bottom - $wr.Top))

  $tabEbook = Find-ByAutomationId -Root $root -Id "TabEbook"
  if ($null -eq $tabEbook) { throw "找不到「電子書」分頁鈕" }
  $tabEbook.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 800

  $bookList = Find-ByAutomationId -Root $root -Id "BookList"
  if ($null -eq $bookList) { throw "找不到書櫃清單" }
  $textCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Text)
  $target = $null
  foreach ($it in $bookList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
    foreach ($t in $it.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) {
      if ($t.Current.Name -like "*換章測試樣書*") { $target = $it; break }
    }
    if ($null -ne $target) { break }
  }
  if ($null -eq $target) { throw "書櫃中找不到植入之樣書「$sampleTitle」" }

  $scroller = $null
  for ($try = 1; $try -le 3 -and $null -eq $scroller; $try++) {
    Set-WindowForeground -Hwnd $hwnd | Out-Null
    # 先捲到可見再取座標（Issue #270）：植入之樣書排在書櫃清單末端，清單可視高度不足以容納全部書卡時
    # 該卡 IsOffscreen=True，UIA 仍給出「虛擬」BoundingRectangle——其中心落在清單可視區之外，
    # 點下去屬別的控件，命中斷言即判「被他窗覆蓋」（實撞：書卡 y=873 落在清單可視範圍 512–828 之外）。
    if ($target.GetSupportedPatterns() -contains [System.Windows.Automation.ScrollItemPattern]::Pattern) {
      $target.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView()
      Start-Sleep -Milliseconds 600
    }
    $r = $target.Current.BoundingRectangle
    if ($target.Current.IsOffscreen) { throw ("樣書書卡捲動後仍不可見（rect={0}、清單 rect={1}）——書櫃清單可視高度不足" -f $r, $bookList.Current.BoundingRectangle) }
    $cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)
    $pidAt = [Win32Ui]::PidAtPoint($cx, $cy)
    if ($pidAt -ne [uint32]$app.ProcessId) {
      $pt = New-Object Win32Ui+POINT -Property @{ X = $cx; Y = $cy }
      throw ("點擊命中斷言失敗：({0},{1}) 屬 pid={2}、非受測行程 pid={3}｜書卡 rect={4} offscreen={5}｜書櫃清單 rect={6}｜視窗 rect=L{7} T{8} R{9} B{10}｜該點視窗標題='{11}'" -f
             $cx, $cy, $pidAt, $app.ProcessId, $r, $target.Current.IsOffscreen, $bookList.Current.BoundingRectangle,
             $wr.Left, $wr.Top, $wr.Right, $wr.Bottom, [Win32Ui]::WindowTitle([Win32Ui]::WindowFromPoint($pt)))
    }
    Write-Host "* 第 $try 輪：雙擊樣書書卡於 ($cx,$cy)"
    [Win32Ui]::DoubleClick($cx, $cy)
    for ($i = 0; $i -lt 16; $i++) { Start-Sleep -Milliseconds 500; $scroller = Find-ByAutomationId -Root $root -Id "ReadingScroller"; if ($null -ne $scroller) { break } }
  }
  if ($null -eq $scroller) { throw "開書逾時：閱讀區未出現" }
  Write-Host "* 閱讀器已就緒"
  #endregion

  #region C.判別當前章之輔助 --------------------------------
  Write-Host "## C.判別當前章 --------------------------------" -ForegroundColor Cyan
  function Get-CurrentChapterMarker {
    $sc = Find-ByAutomationId -Root $root -Id "ReadingScroller"
    if ($null -eq $sc) { return "(閱讀區不存在)" }
    $joined = ""
    foreach ($t in $sc.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) { $joined += " " + $t.Current.Name }
    if ($joined -match "MARKERBRAVO") { return "CH2" }
    if ($joined -match "MARKERALPHA") { return "CH1" }
    return "(無標記)"
  }
  $m0 = Get-CurrentChapterMarker
  Write-Host "* 開書後當前章＝$m0"
  if ($m0 -ne "CH1") { throw "開書後未落在第一章（實得 $m0），後續斷言前提不成立" }
  #endregion

  #region D.章中連按兩下 → 不換章 --------------------------------
  Write-Host "## D.章中連按兩下 → 不換章 --------------------------------" -ForegroundColor Cyan
  # 游標在第 1 段（章首、非章末）：連按兩下 ↓ 應只前進段落、留在本章
  [Win32Ui]::KeyTap($VK_DOWN, 40); Start-Sleep -Milliseconds $TapGapMs; [Win32Ui]::KeyTap($VK_DOWN, 40)
  Start-Sleep -Milliseconds 900
  $m1 = Get-CurrentChapterMarker
  if ($m1 -ne "CH1") { $fails += "章中連按兩下 ↓ 竟換章（$m0 → $m1）——應只前進段落" }
  else { Write-Host "* PASS：章中連按兩下 ↓ 仍在 CH1（未換章）" -ForegroundColor Green }
  #endregion

  #region E.章末連按兩下 ↓ → 下一章 --------------------------------
  Write-Host "## E.章末連按兩下 ↓ → 下一章 --------------------------------" -ForegroundColor Cyan
  # 上一步已走到第 3 段＝章末；此處兩下皆在章末，應換章
  [Win32Ui]::KeyTap($VK_DOWN, 40); Start-Sleep -Milliseconds $TapGapMs; [Win32Ui]::KeyTap($VK_DOWN, 40)
  Start-Sleep -Milliseconds 1200
  $m2 = Get-CurrentChapterMarker
  if ($m2 -ne "CH2") { $fails += "章末連按兩下 ↓ 未換章（停在 $m2）" }
  else { Write-Host "* PASS：章末連按兩下 ↓ → CH2" -ForegroundColor Green }
  #endregion

  #region F.章首連按兩下 ↑ → 上一章 --------------------------------
  Write-Host "## F.章首連按兩下 ↑ → 上一章 --------------------------------" -ForegroundColor Cyan
  # 換章後游標歸 0＝章首；兩下 ↑ 應回上一章（且換章後狀態已重置，第一下不與前一次配對）
  [Win32Ui]::KeyTap($VK_UP, 40); Start-Sleep -Milliseconds $TapGapMs; [Win32Ui]::KeyTap($VK_UP, 40)
  Start-Sleep -Milliseconds 1200
  $m3 = Get-CurrentChapterMarker
  if ($m3 -ne "CH1") { $fails += "章首連按兩下 ↑ 未回上一章（停在 $m3）" }
  else { Write-Host "* PASS：章首連按兩下 ↑ → CH1" -ForegroundColor Green }
  #endregion

  #region G.章末連按兩下 Space → 下一章 --------------------------------
  Write-Host "## G.章末連按兩下 Space → 下一章 --------------------------------" -ForegroundColor Cyan
  # 先以 PageDown 之外的方式走到章末：連按 ↓ 兩次（章中→章末），再以 Space 雙擊換章
  [Win32Ui]::KeyTap($VK_DOWN, 40); Start-Sleep -Milliseconds 600; [Win32Ui]::KeyTap($VK_DOWN, 40)
  Start-Sleep -Milliseconds 800
  if ((Get-CurrentChapterMarker) -ne "CH1") { throw "前置步驟已離開 CH1，Space 斷言前提不成立" }
  [Win32Ui]::KeyTap($VK_SPACE, 40); Start-Sleep -Milliseconds $TapGapMs; [Win32Ui]::KeyTap($VK_SPACE, 40)
  Start-Sleep -Milliseconds 1200
  $m4 = Get-CurrentChapterMarker
  if ($m4 -ne "CH2") { $fails += "章末連按兩下 Space 未換章（停在 $m4）" }
  else { Write-Host "* PASS：章末連按兩下 Space → CH2" -ForegroundColor Green }
  #endregion

  #region H.結果 --------------------------------
  Write-Host "## H.結果 --------------------------------" -ForegroundColor Cyan
  if ($fails.Count -gt 0) {
    Write-Host "* 結果：FAIL" -ForegroundColor Red
    foreach ($f in $fails) { Write-Host "  - $f" -ForegroundColor Red }
  } else {
    Write-Host "* 結果：PASS（章中不換章／章末 ↓·Space 換下一章／章首 ↑ 回上一章，接線全數生效）" -ForegroundColor Green
  }
  #endregion

} finally {
  #region IV.收尾 ================================
  Write-Host "# IV.收尾 ================================" -ForegroundColor Blue
  # 收尾一律以行程名掃殺（涵蓋外殼另起之 app 行程）；不再另留 $proc.Kill()，該行原與本行重複。
  Get-Process -Name "LingoIsland" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit(5000) }
  Start-Sleep -Milliseconds 500
  if (Test-Path $backupDir) {
    Remove-Item -Path $appData -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item -Path $backupDir -Destination $appData -Recurse -Force
    Remove-Item -Path $backupDir -Recurse -Force
    Write-Host "* 已自備份還原 $appData（樣書與測試資料不留存）"
  }
  #endregion
}

if ($fails.Count -gt 0) { exit 1 }
exit 0
#endregion
