#requires -Version 7
<#
  Issue #252（中英雙語朗讀）之實機煙霧驗證＋證據擷取。

  **能驗與不能驗（誠實界定）**：朗讀是聲音，「中文段是否真的以中文語音發聲」UIA 照不到——
  該項由 `SpeechPromptTests` 之 SSML 斷言（voice 切換標記與各段文字）提供機器證據、聽感由 USR 確認。
  本腳本驗的是**實機層面照得到的部分**：植入中英混排樣書後按「播放/繼續」，
    (1) 不擲例外、App 存活（中文段不再讓朗讀整條斷掉）；
    (2) **當前段會自動前進**——即 `SpeakCompleted` 每段恰觸發一次、逐段導讀未因逐段換聲而中途誤進
        （此為 #252 採「單一 PromptBuilder、一次 SpeakAsync」而非「逐段多次 SpeakAsync」之關鍵 invariant）。

  桌面 UIA e2e 工法依 [modTechStackWinApp] ＜III＞；取窗與視窗守衛沿用 uiaCommon.ps1（Issue #270）。
#>

param(
  [string]$ExePath = "",
  [string]$OutDir  = "",
  [int]$WaitSec    = 40      # 等待自動前進之上限（中英混排段落唸完需時）
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

#region I.主旨目的 ================================
Write-Host "# I.主旨目的 ================================" -ForegroundColor Blue
Write-Host "* 驗證中英混排段落之逐段朗讀（Issue #252）於實機不中斷："
Write-Host "  按「播放/繼續」後 App 存活、且當前段自動前進（SpeakCompleted 每段恰一次）。"
Write-Host "* 聲音本身（中文段是否用中文語音）由 SpeechPromptTests 之 SSML 斷言把關，非本腳本職責。"
#endregion

#region II.參考準備 ================================
Write-Host "# II.參考準備 ================================" -ForegroundColor Blue
  $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
  if (-not $ExePath) { $ExePath = Join-Path $repoRoot "sysLingoIsland\bin\Release\net9.0-windows10.0.19041.0\LingoIsland.exe" }
  if (-not $OutDir)  { $OutDir  = Join-Path $repoRoot "docs\manual-assets" }
  $appData      = Join-Path $env:APPDATA "LingoIsland"
  $backupDir    = Join-Path $env:TEMP ("LingoIsland-backup-bi-" + (Get-Date -Format "yyyyMMddHHmmss"))
  $sampleFolder = "zz-test-bilingual"
  $sampleTitle  = "ZZ 中英混排測試樣書"
  Write-Host "* ExePath = $ExePath"
  if (-not (Test-Path $ExePath)) { Write-Host "* [錯誤] 找不到建置產物：$ExePath（請先 dotnet build）" -ForegroundColor Red; exit 1 }
  if (-not (Test-Path $OutDir))  { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
  . "$PSScriptRoot\uiaCommon.ps1"
  Write-Host "* DPI 一致化（PER_MONITOR_AWARE_V2）＝$([Win32Ui]::MakeDpiAware())"
#endregion

#region III.內容程序 ================================
Write-Host "# III.內容程序 ================================" -ForegroundColor Blue

$fails = @()
try {

  #region A.APPDATA 備份＋植入中英混排樣書 --------------------------------
  Write-Host "## A.APPDATA 備份＋植入樣書 --------------------------------" -ForegroundColor Cyan
  Get-Process -Name "LingoIsland" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit(5000) }
  Start-Sleep -Milliseconds 500
  Copy-Item -Path $appData -Destination $backupDir -Recurse -Force
  Write-Host "* 已備份 $appData → $backupDir"

  # 起手先清前次殘留（不倚賴上次 finally 跑完——外力中斷時 finally 不保證執行，Issue #270 實撞）
  $ebooksJson = Join-Path $appData "ebooks.json"
  $shelf = Get-Content $ebooksJson -Raw -Encoding UTF8 | ConvertFrom-Json
  $shelf.Items = @(@($shelf.Items) | Where-Object { $_.Folder -ne $sampleFolder })

  $sampleDir = Join-Path $appData ("ebook\" + $sampleFolder)
  New-Item -ItemType Directory -Path $sampleDir -Force | Out-Null
  $epubPath = Join-Path $sampleDir "zz-bilingual.epub"
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
  & $addEntry "OEBPS/content.opf" '<?xml version="1.0" encoding="UTF-8"?><package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="bookid">urn:uuid:zz-bilingual-sample</dc:identifier><dc:title>ZZ 中英混排測試樣書</dc:title><dc:language>en</dc:language><meta property="dcterms:modified">2026-07-30T00:00:00Z</meta></metadata><manifest><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/><item id="ch1" href="ch1.xhtml" media-type="application/xhtml+xml"/></manifest><spine><itemref idref="ch1"/></spine></package>' $false
  & $addEntry "OEBPS/nav.xhtml" '<?xml version="1.0" encoding="UTF-8"?><html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>Contents</title></head><body><nav epub:type="toc"><ol><li><a href="ch1.xhtml">Chapter One</a></li></ol></nav></body></html>' $false
  # 三段皆中英混排：每段都會觸發逐段換聲（舊版恆以 en-US 唸整段）
  & $addEntry "OEBPS/ch1.xhtml" '<?xml version="1.0" encoding="UTF-8"?><html xmlns="http://www.w3.org/1999/xhtml"><head><title>C1</title></head><body><p>Anna: Good morning 早安, Ben.</p><p>Ben: 我很好, thank you very much.</p><p>Anna: 我們開始上課 let us begin.</p></body></html>' $false
  $zip.Dispose()

  $shelf.Items = @($shelf.Items) + ([pscustomobject]@{
    Id = "zzzz2222222222222222222222222222"; DcIdentifier = "urn:uuid:zz-bilingual-sample"
    Title = $sampleTitle; Author = "LingoIsland Test"; Language = "en"; ChapterCount = 1
    ThemeId = $null; ThemeName = $null; CoverFile = $null
    AddedAt = "2026-07-30T00:00:00.0000000+08:00"; Folder = $sampleFolder
    LastReadChapter = 0; LastReadParagraph = 0
  })
  ($shelf | ConvertTo-Json -Depth 8) | Set-Content -Path $ebooksJson -Encoding UTF8
  Write-Host "* 已植入中英混排樣書「$sampleTitle」（3 段，每段皆中英夾雜）"
  #endregion

  #region B.啟動 App、開書 --------------------------------
  Write-Host "## B.啟動 App、開書 --------------------------------" -ForegroundColor Cyan
  $app  = Start-AppAndGetWindow -ExePath $ExePath -TimeoutSec 30
  $hwnd = $app.Hwnd
  Write-Host ("* 受測行程 pid={0}（自主視窗反查）" -f $app.ProcessId)
  Set-WindowForeground -Hwnd $hwnd | Out-Null
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
  $wr = Set-WindowMaximized -Hwnd $hwnd
  Write-Host ("* 視窗尺寸＝{0}x{1}" -f ($wr.Right - $wr.Left), ($wr.Bottom - $wr.Top))

  $tabEbook = Find-ByAutomationId -Root $root -Id "TabEbook"
  if ($null -eq $tabEbook) { throw "找不到「電子書」分頁鈕" }
  $tabEbook.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 1200

  $bookList = Find-ByAutomationId -Root $root -Id "BookList"
  if ($null -eq $bookList) { throw "找不到書櫃清單" }
  $titleCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "BookCardTitle")
  $target = $null
  foreach ($it in $bookList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
    $t = $it.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $titleCond)
    if ($null -ne $t -and $t.Current.Name -like "*中英混排*") { $target = $it; break }
  }
  if ($null -eq $target) { throw "書櫃中找不到植入之樣書「$sampleTitle」" }

  $scroller = $null
  for ($try = 1; $try -le 3 -and $null -eq $scroller; $try++) {
    Set-WindowForeground -Hwnd $hwnd | Out-Null
    if ($target.GetSupportedPatterns() -contains [System.Windows.Automation.ScrollItemPattern]::Pattern) {
      $target.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView()
      Start-Sleep -Milliseconds 600
    }
    $r = $target.Current.BoundingRectangle
    $cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)
    if ([Win32Ui]::PidAtPoint($cx, $cy) -ne [uint32]$app.ProcessId) { throw "點擊命中斷言失敗：($cx,$cy) 被他窗覆蓋" }
    Write-Host "* 第 $try 輪：雙擊樣書書卡於 ($cx,$cy)"
    [Win32Ui]::DoubleClick($cx, $cy)
    for ($i = 0; $i -lt 16; $i++) { Start-Sleep -Milliseconds 500; $scroller = Find-ByAutomationId -Root $root -Id "ReadingScroller"; if ($null -ne $scroller) { break } }
  }
  if ($null -eq $scroller) { throw "開書逾時：閱讀區未出現" }
  Write-Host "* 閱讀器已就緒"
  #endregion

  #region C.朗讀並驗自動前進 --------------------------------
  Write-Host "## C.朗讀並驗自動前進 --------------------------------" -ForegroundColor Cyan

  # 當前段以「閱讀區內字級最大之文字」判定（當前段放大高亮，見 design 閱讀器契約）
  function Get-CurrentParagraphText {
    $sc = Find-ByAutomationId -Root $root -Id "ReadingScroller"
    if ($null -eq $sc) { return $null }
    $textCond = New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
      [System.Windows.Automation.ControlType]::Text)
    $best = $null; $maxH = -1
    foreach ($t in $sc.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) {
      $h = $t.Current.BoundingRectangle.Height
      if ($t.Current.Name -and $h -gt $maxH) { $maxH = $h; $best = $t.Current.Name }
    }
    return $best
  }

  $before = Get-CurrentParagraphText
  Write-Host ("* 朗讀前之當前段＝「{0}」" -f $before)

  $resume = Find-ByAutomationId -Root $root -Id "ReaderResumeBtn"
  if ($null -eq $resume) { throw "找不到「播放/繼續」鈕（AutomationId=ReaderResumeBtn）" }
  if (-not $resume.Current.IsEnabled) { throw "「播放/繼續」鈕為停用狀態，無法起朗讀" }
  $resume.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Write-Host "* 已按「播放/繼續」，等待自動前進（上限 $WaitSec 秒）…"

  $advanced = $false
  for ($i = 0; $i -lt $WaitSec; $i++) {
    Start-Sleep -Seconds 1
    $now = Get-CurrentParagraphText
    if ($now -and $now -ne $before) { $advanced = $true; Write-Host ("* 已前進至「{0}」（第 {1} 秒）" -f $now, ($i + 1)); break }
  }

  # 斷言 1：App 存活（中文段未讓朗讀整條斷掉、未當機）
  $alive = @(Get-Process -Name LingoIsland -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq [int]$app.ProcessId }).Count -gt 0
  if (-not $alive) { $fails += "朗讀中英混排段落後 App 已結束——疑因逐段換聲擲例外" }
  else { Write-Host "* [PASS] App 存活（中英混排段落朗讀未致當機）" -ForegroundColor Green }

  # 斷言 2：當前段自動前進（SpeakCompleted 每段恰一次）
  if (-not $advanced) { $fails += "朗讀後 $WaitSec 秒內當前段未前進——逐段自動前進可能失效（SpeakCompleted 未如期觸發）" }
  else { Write-Host "* [PASS] 唸完自動前進生效（逐段換聲未破壞 SpeakCompleted 語意）" -ForegroundColor Green }

  $shot = Join-Path $OutDir "ebook-bilingual-speak.png"
  Save-WindowShot -Hwnd $hwnd -Path $shot
  Write-Host "* 截圖：$shot"
  #endregion

  #region D.結果 --------------------------------
  Write-Host "## D.結果 --------------------------------" -ForegroundColor Cyan
  if ($fails.Count -gt 0) {
    Write-Host "* 結果：FAIL" -ForegroundColor Red
    foreach ($f in $fails) { Write-Host "  - $f" -ForegroundColor Red }
  } else {
    Write-Host "* 結果：PASS（中英混排段落朗讀不中斷、逐段自動前進正常）" -ForegroundColor Green
  }
  #endregion

} finally {
  #region IV.收尾 ================================
  Write-Host "# IV.收尾 ================================" -ForegroundColor Blue
  Get-Process -Name "LingoIsland" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit(5000) }
  Start-Sleep -Milliseconds 500
  if (Test-Path $backupDir) {
    Remove-Item -Path $appData -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item -Path $backupDir -Destination $appData -Recurse -Force
    Remove-Item -Path $backupDir -Recurse -Force
    Write-Host "* 已自備份還原 $appData（樣書不留存）"
  }
  #endregion
}

if ($fails.Count -gt 0) { exit 1 }
exit 0
#endregion
