#requires -Version 7
<#
  Issue #294（說話人清單右鍵指定顏色）之實機走查＋證據擷取。

  驗的是**本件行為本身**，不是「腳本跑得動」：
    (1) 說話人清單列**右鍵喚得出「指定顏色」選單**，且選單含 12 個色槽項（AutomationId＝SpeakerColorSlot0..11）
        與「不指定顏色（清除）」項（SpeakerColorClear）。
    (2) 點某色槽後，**清單該說話人的名字實際變色**——以視窗截圖對該列做像素比對：
        指派前該列無「彩色」像素（預設近黑字），指派後出現彩色像素（R/G/B 極差 > 門檻）。
        UIA 讀不到 Foreground，故顏色只能以像素證明；門檻與取樣範圍逐項印出、可覆查。
    (3) **主題檔對應色槽描述含該說話人名**——直接讀回 %APPDATA%\LingoIsland\themes.json 斷言。
    (4) **同名正規化**：改指派到另一色槽後，原色槽描述不再含該名（同名只留一處）。
    (5) 影片頁同一選單亦喚得出（兩頁共用 SpeakerColorMenu，非各留一份複製品）。

  桌面 UIA e2e 工法依 [modTechStackWinApp] ＜III＞；取窗／最大化／右鍵／截圖沿用 uiaCommon.ps1，不重造。
  %APPDATA% 起手備份、finally 還原（本腳本會植入樣書與探針主題並改主題資料）。
#>

param(
  [string]$ExePath = "",
  [string]$OutDir  = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

#region I.主旨目的 ================================
Write-Host "# I.主旨目的 ================================" -ForegroundColor Blue
Write-Host "* 驗證說話人清單右鍵指定顏色（Issue #294）於實機成立：選單喚出→指定→清單變色→主題檔落地→同名正規化。"
#endregion

#region II.參考準備 ================================
Write-Host "# II.參考準備 ================================" -ForegroundColor Blue

  #region A.參數準備 --------------------------------
  Write-Host "## A.參數準備 --------------------------------" -ForegroundColor Cyan
  $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
  if (-not $ExePath) { $ExePath = Join-Path $repoRoot "sysLingoIsland\bin\Release\net9.0-windows10.0.19041.0\LingoIsland.exe" }
  if (-not $OutDir)  { $OutDir  = Join-Path $env:TEMP "LingoIsland-speakercolor-evidence" }
  $appData    = Join-Path $env:APPDATA "LingoIsland"
  $backupDir  = Join-Path $env:TEMP ("LingoIsland-backup-spkcolor-" + (Get-Date -Format "yyyyMMddHHmmss"))
  $themeId    = "zzzz294294294294294294294294294c"
  $themeName  = "ZZ-SPEAKERCOLOR-PROBE"
  $speaker    = "Zola"          # 樣書說話人；刻意與樣書另一說話人 Zolanda 同前綴，順帶驗邊界比對不誤命中
  $sampleFolder = "zz-test-speakercolor"
  $sampleTitle  = "ZZ 說話人配色測試樣書"
  $firstSlot  = 9    # #1E88E5（藍）
  $secondSlot = 0    # #E53935（紅）——驗改指派之同名正規化
  Write-Host "* ExePath = $ExePath"
  Write-Host "* OutDir  = $OutDir"
  Write-Host "* 探針主題＝$themeName／受測說話人＝$speaker／色槽 $firstSlot → $secondSlot"
  if (-not (Test-Path $ExePath)) { Write-Host "* [錯誤] 找不到建置產物：$ExePath（請先 dotnet build -c Release）" -ForegroundColor Red; exit 1 }
  if (-not (Test-Path $OutDir))  { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
  #endregion

  #region B.型別準備（共用定義） --------------------------------
  Write-Host "## B.型別準備（共用定義） --------------------------------" -ForegroundColor Cyan
  . "$PSScriptRoot\uiaCommon.ps1"
  Add-Type -AssemblyName System.Drawing
  Write-Host "* DPI 一致化（PER_MONITOR_AWARE_V2）＝$([Win32Ui]::MakeDpiAware())"
  $AE = [System.Windows.Automation.AutomationElement]
  $TS = [System.Windows.Automation.TreeScope]
  $CT = [System.Windows.Automation.ControlType]

  function Click-Element {
    param([System.Windows.Automation.AutomationElement]$El)
    $r = $El.Current.BoundingRectangle
    [Win32Ui]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 120
    [Win32Ui]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Win32Ui]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 400
  }

  function Switch-Tab {
    param([System.Windows.Automation.AutomationElement]$Root, [string]$TabId, [IntPtr]$Hwnd)
    for ($i = 0; $i -lt 5; $i++) {
      Set-WindowForeground -Hwnd $Hwnd | Out-Null
      $tab = Find-ByAutomationId -Root $Root -Id $TabId
      if ($null -eq $tab) { Start-Sleep -Milliseconds 500; continue }
      $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
      Start-Sleep -Milliseconds 900
      if ($tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { return $true }
    }
    return $false
  }

  # 目前彈出之 ContextMenu（本行程）；無則 null
  function Get-OpenMenu {
    param([int]$ProcessId)
    $cond = New-Object System.Windows.Automation.AndCondition(
      (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Menu)),
      (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $ProcessId)))
    return $AE::RootElement.FindFirst($TS::Descendants, $cond)
  }

  # 截視窗、回 Bitmap 物件（座標換算：螢幕座標 → 圖內座標，扣 DWM 隱形邊框 7/1，同 Save-WindowShot）
  function Get-WindowBitmap {
    param([IntPtr]$Hwnd, [string]$Path)
    Save-WindowShot -Hwnd $Hwnd -Path $Path
    return [System.Drawing.Bitmap]::FromFile($Path)
  }

  # 某螢幕矩形內之「彩色」像素數（R/G/B 極差 > $MinChroma 且非近白）——UIA 讀不到 Foreground，字色只能以像素證明
  function Measure-ChromaPixels {
    param([System.Drawing.Bitmap]$Bmp, [IntPtr]$Hwnd, $Rect, [int]$MinChroma = 40)
    $wr = New-Object Win32Ui+RECT
    [Win32Ui]::GetWindowRect($Hwnd, [ref]$wr) | Out-Null
    $x0 = [int]($Rect.X - $wr.Left - 7); $y0 = [int]($Rect.Y - $wr.Top - 1)
    $x1 = [int]($x0 + $Rect.Width);      $y1 = [int]($y0 + $Rect.Height)
    $x0 = [Math]::Max(0, $x0); $y0 = [Math]::Max(0, $y0)
    $x1 = [Math]::Min($Bmp.Width - 1, $x1); $y1 = [Math]::Min($Bmp.Height - 1, $y1)
    $n = 0
    for ($y = $y0; $y -le $y1; $y++) {
      for ($x = $x0; $x -le $x1; $x++) {
        $p = $Bmp.GetPixel($x, $y)
        $mx = [Math]::Max($p.R, [Math]::Max($p.G, $p.B)); $mn = [Math]::Min($p.R, [Math]::Min($p.G, $p.B))
        if (($mx - $mn) -gt $MinChroma -and $mx -lt 245) { $n++ }
      }
    }
    return $n
  }

  # 某螢幕矩形內之「藍字」像素數——受測色槽 #1E88E5 為藍，而清單底色（粉色斑馬紋）與預設近黑字皆非藍，
  # 故「藍字像素由 0 變正」比泛用彩度差更能證明**這一次指派**造成了字色改變（粉色底恆有彩度、只看彩度訊號弱）。
  function Measure-BluishPixels {
    # $LeftSkip＝略過列首的核取方塊本身（WPF 打勾之方塊為系統藍，計入會使基準不為 0、削弱斷言）——只量名字文字區。
    param([System.Drawing.Bitmap]$Bmp, [IntPtr]$Hwnd, $Rect, [int]$LeftSkip = 26)
    $wr = New-Object Win32Ui+RECT
    [Win32Ui]::GetWindowRect($Hwnd, [ref]$wr) | Out-Null
    $x0 = [Math]::Max(0, [int]($Rect.X - $wr.Left - 7 + $LeftSkip)); $y0 = [Math]::Max(0, [int]($Rect.Y - $wr.Top - 1))
    $x1 = [Math]::Min($Bmp.Width - 1, [int]($Rect.X - $wr.Left - 7 + $Rect.Width)); $y1 = [Math]::Min($Bmp.Height - 1, [int]($y0 + $Rect.Height))
    $n = 0
    for ($y = $y0; $y -le $y1; $y++) {
      for ($x = $x0; $x -le $x1; $x++) {
        $p = $Bmp.GetPixel($x, $y)
        if (($p.B - $p.R) -gt 40 -and ($p.B - $p.G) -gt 20 -and $p.B -lt 245) { $n++ }
      }
    }
    return $n
  }

  function Get-SlotDescription {
    param([int]$Slot)
    $j = Get-Content (Join-Path $appData "themes.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    $t = @($j.Items) | Where-Object { $_.Id -eq $themeId } | Select-Object -First 1
    if ($null -eq $t) { return $null }
    return [string]$t.Colors[$Slot].Description
  }
  #endregion
#endregion

#region III.內容程序 ================================
Write-Host "# III.內容程序 ================================" -ForegroundColor Blue

$fails = @()
$notes = @()
try {

  #region A.APPDATA 備份＋植入探針主題與樣書 --------------------------------
  Write-Host "## A.APPDATA 備份＋植入探針主題與樣書 --------------------------------" -ForegroundColor Cyan
  Get-Process -Name "LingoIsland" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit(5000) }
  Start-Sleep -Milliseconds 500
  Copy-Item -Path $appData -Destination $backupDir -Recurse -Force
  Write-Host "* 已備份 $appData → $backupDir"

  # 探針主題：12 色槽全預設 hex、描述全空（＝受測說話人起手無配色，故「變色」是本件造成的）
  $hexDefaults = @("#E53935","#F4511E","#FB8C00","#FDD835","#C0CA33","#7CB342","#43A047","#00897B","#00ACC1","#1E88E5","#5E35B1","#D81B60")
  $themesJson = Join-Path $appData "themes.json"
  $themes = if (Test-Path $themesJson) { Get-Content $themesJson -Raw -Encoding UTF8 | ConvertFrom-Json } else { [pscustomobject]@{ Items = @() } }
  $themes.Items = @(@($themes.Items) | Where-Object { $_.Id -ne $themeId })
  foreach ($t in $themes.Items) { $t.IsActive = $false }
  $themes.Items = @($themes.Items) + ([pscustomobject]@{
    Id = $themeId; Name = $themeName; Text = ""; Keywords = ""; BlockedWords = ""
    Image = $null; IsActive = $true; ColorRules = [pscustomobject]@{}
    Colors = @($hexDefaults | ForEach-Object { [pscustomobject]@{ Hex = $_; Description = "" } })
  })
  ($themes | ConvertTo-Json -Depth 8) | Set-Content -Path $themesJson -Encoding UTF8
  Write-Host "* 已植入探針主題「$themeName」（12 色槽描述全空）"

  # 樣書：兩位說話人 Zola／Zolanda（同前綴，順帶佐證邊界比對不誤命中）
  $ebooksJson = Join-Path $appData "ebooks.json"
  $shelf = Get-Content $ebooksJson -Raw -Encoding UTF8 | ConvertFrom-Json
  $shelf.Items = @(@($shelf.Items) | Where-Object { $_.Folder -ne $sampleFolder })
  $sampleDir = Join-Path $appData ("ebook\" + $sampleFolder)
  New-Item -ItemType Directory -Path $sampleDir -Force | Out-Null
  $epubPath = Join-Path $sampleDir "zz-speakercolor.epub"
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
  & $addEntry "OEBPS/content.opf" '<?xml version="1.0" encoding="UTF-8"?><package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="bookid">urn:uuid:zz-speakercolor-sample</dc:identifier><dc:title>ZZ 說話人配色測試樣書</dc:title><dc:language>en</dc:language><meta property="dcterms:modified">2026-08-10T00:00:00Z</meta></metadata><manifest><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/><item id="ch1" href="ch1.xhtml" media-type="application/xhtml+xml"/></manifest><spine><itemref idref="ch1"/></spine></package>' $false
  & $addEntry "OEBPS/nav.xhtml" '<?xml version="1.0" encoding="UTF-8"?><html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>Contents</title></head><body><nav epub:type="toc"><ol><li><a href="ch1.xhtml">Chapter One</a></li></ol></nav></body></html>' $false
  & $addEntry "OEBPS/ch1.xhtml" '<?xml version="1.0" encoding="UTF-8"?><html xmlns="http://www.w3.org/1999/xhtml"><head><title>C1</title></head><body><p>Zola: Good morning.</p><p>Zolanda: Good morning to you.</p><p>Zola: Let us begin the lesson.</p></body></html>' $false
  $zip.Dispose()
  $shelf.Items = @($shelf.Items) + ([pscustomobject]@{
    Id = "zzzz2942942942942942942942942942"; DcIdentifier = "urn:uuid:zz-speakercolor-sample"
    Title = $sampleTitle; Author = "LingoIsland Test"; Language = "en"; ChapterCount = 1
    ThemeId = $themeId; ThemeName = $themeName; CoverFile = $null
    AddedAt = "2026-08-10T00:00:00.0000000+08:00"; Folder = $sampleFolder
    LastReadChapter = 0; LastReadParagraph = 0
  })
  ($shelf | ConvertTo-Json -Depth 8) | Set-Content -Path $ebooksJson -Encoding UTF8
  Write-Host "* 已植入樣書「$sampleTitle」（說話人 Zola／Zolanda）"
  #endregion

  #region B.啟動 App、開書、取說話人列 --------------------------------
  Write-Host "## B.啟動 App、開書 --------------------------------" -ForegroundColor Cyan
  $app  = Start-AppAndGetWindow -ExePath $ExePath -TimeoutSec 30
  $hwnd = $app.Hwnd
  Set-WindowMaximized -Hwnd $hwnd | Out-Null
  Set-WindowForeground -Hwnd $hwnd | Out-Null
  $root = $AE::FromHandle($hwnd)

  if (-not (Switch-Tab -Root $root -TabId "TabEbook" -Hwnd $hwnd)) { throw "切不到電子書分頁（TabEbook）" }
  Start-Sleep -Milliseconds 900

  # 書櫃為虛擬化清單（未實現之項不在 UIA 樹），故先以主題篩選收斂到只剩樣書——否則樣書可能落在捲動區外找不到
  $bfilter = Find-ByAutomationId -Root $root -Id "BookThemeFilter"
  if ($null -eq $bfilter) { throw "找不到書櫃主題篩選（BookThemeFilter）" }
  $ec = $bfilter.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
  $ec.Expand(); Start-Sleep -Milliseconds 600
  $opt = @($bfilter.FindAll($TS::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Text))) |
          Where-Object { $_.Current.Name -eq $themeName }) | Select-Object -First 1
  if ($null -eq $opt) { $ec.Collapse(); throw "書櫃主題篩選找不到探針主題「$themeName」——前置狀態不成立" }
  Click-Element -El $opt
  Start-Sleep -Milliseconds 900
  Write-Host "* 書櫃已篩選至主題「$themeName」"

  $bookList = Find-ByAutomationId -Root $root -Id "BookList"
  if ($null -eq $bookList) { throw "找不到書櫃清單（BookList）" }
  $titleCond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "BookCardTitle")
  $target = $null
  foreach ($it in $bookList.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
    $t = $it.FindFirst($TS::Descendants, $titleCond)
    if ($null -ne $t -and $t.Current.Name -like "*說話人配色*") { $target = $it; break }
  }
  if ($null -eq $target) {
    $shelfTitles = @($bookList.FindAll($TS::Descendants, $titleCond) | ForEach-Object { $_.Current.Name })
    throw "書櫃找不到樣書「$sampleTitle」——前置狀態不成立（書櫃實得 $($shelfTitles.Count) 張：$($shelfTitles -join '｜')）"
  }
  $tr = $target.Current.BoundingRectangle
  [Win32Ui]::DoubleClick([int]($tr.X + $tr.Width / 2), [int]($tr.Y + $tr.Height / 2))
  Start-Sleep -Milliseconds 2500
  Write-Host "* 已開書（雙擊書卡）"

  # 說話人清單（ReaderSpeakerChecks）之受測列：以 CheckBox 名稱前綴定位（DisplayName＝「Zola (2)」）
  $panel = Find-ByAutomationId -Root $root -Id "ReaderSpeakerChecks"
  if ($null -eq $panel) { throw "找不到說話人清單面板（ReaderSpeakerChecks）——書可能未開到內容頁" }
  $row = $null
  foreach ($cb in $panel.FindAll($TS::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::CheckBox)))) {
    if ($cb.Current.Name -like "$speaker (*") { $row = $cb; break }
  }
  if ($null -eq $row) {
    $names = @($panel.FindAll($TS::Descendants,
      (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::CheckBox))) |
      ForEach-Object { $_.Current.Name })
    throw "說話人清單找不到「$speaker」（實得：$($names -join '｜')）——前置狀態不成立"
  }
  Write-Host "* 受測說話人列＝「$($row.Current.Name)」"
  $rowRect = $row.Current.BoundingRectangle

  $beforePng = Join-Path $OutDir "01-before-assign.png"
  $bmpBefore = Get-WindowBitmap -Hwnd $hwnd -Path $beforePng
  $chromaBefore = Measure-ChromaPixels -Bmp $bmpBefore -Hwnd $hwnd -Rect $rowRect
  $blueBefore   = Measure-BluishPixels  -Bmp $bmpBefore -Hwnd $hwnd -Rect $rowRect
  $bmpBefore.Dispose()
  Write-Host "* 指派前：該列彩色像素數＝$chromaBefore（含粉色斑馬紋底）／**藍字像素數＝$blueBefore**"
  if ($blueBefore -gt 5) {
    $notes += "指派前該列名字區已有 $blueBefore 個藍色像素（探針主題描述全空、理應近 0）——藍字斷言之基準不乾淨，請覆查取樣範圍"
  }
  #endregion

  #region C.訴求1：右鍵喚出「指定顏色」選單 --------------------------------
  Write-Host "## C.訴求1 右鍵喚出選單 --------------------------------" -ForegroundColor Cyan
  $rcx = [int]($rowRect.X + $rowRect.Width / 2); $rcy = [int]($rowRect.Y + $rowRect.Height / 2)
  if ([Win32Ui]::PidAtPoint($rcx, $rcy) -ne [uint32]$app.ProcessId) { throw "右鍵座標 ($rcx,$rcy) 被他窗覆蓋——本輪無法判定" }
  [Win32Ui]::RightClick($rcx, $rcy)
  Start-Sleep -Milliseconds 900
  $menu = Get-OpenMenu -ProcessId $app.ProcessId
  if ($null -eq $menu) { $fails += "訴求1：說話人列右鍵未喚出選單（#294 未落地）" }
  else {
    $slotItems = @($menu.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
      Where-Object { $_.Current.AutomationId -like "SpeakerColorSlot*" })
    $clearItem = @($menu.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
      Where-Object { $_.Current.AutomationId -eq "SpeakerColorClear" })
    Write-Host "* 選單色槽項數＝$($slotItems.Count)／清除項數＝$($clearItem.Count)"
    if ($slotItems.Count -ne 12) { $fails += "訴求1：右鍵選單色槽項數＝$($slotItems.Count)，應為 12" }
    if ($clearItem.Count -ne 1)  { $fails += "訴求1：右鍵選單缺「不指定顏色（清除）」項（SpeakerColorClear）" }
    if ($slotItems.Count -eq 12 -and $clearItem.Count -eq 1) {
      Write-Host "* [OK] 右鍵選單喚出，12 色槽＋清除項齊備" -ForegroundColor Green
    }
    Save-WindowShot -Hwnd $hwnd -Path (Join-Path $OutDir "02-context-menu-open.png")
  }
  #endregion

  #region D.訴求2＋3：指定顏色→清單變色＋主題檔落地 --------------------------------
  Write-Host "## D.訴求2＋3 指定顏色 --------------------------------" -ForegroundColor Cyan
  if ($null -eq $menu) { $notes += "訴求2／3：選單未喚出，指定顏色**驗不到**" }
  else {
    $slot = @($menu.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
      Where-Object { $_.Current.AutomationId -eq ("SpeakerColorSlot" + $firstSlot) }) | Select-Object -First 1
    if ($null -eq $slot) { $fails += "訴求2：找不到色槽項 SpeakerColorSlot$firstSlot" }
    else {
      $slot.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()   # 滑鼠座標易落空，直呼 Invoke
      Start-Sleep -Milliseconds 1600

      # 訴求3：主題檔落地
      $desc = Get-SlotDescription -Slot $firstSlot
      Write-Host "* 主題檔色槽 $firstSlot 描述＝「$desc」"
      if ([string]::IsNullOrWhiteSpace($desc) -or -not $desc.Contains($speaker)) {
        $fails += "訴求3：主題檔色槽 $firstSlot 描述未含「$speaker」（實得「$desc」）＝寫入未落地"
      } else { Write-Host "* [OK] 主題檔色槽 $firstSlot 描述已含「$speaker」" -ForegroundColor Green }

      # 訴求2：清單該說話人顏色實際改變（像素）
      $afterPng = Join-Path $OutDir "03-after-assign.png"
      $bmpAfter = Get-WindowBitmap -Hwnd $hwnd -Path $afterPng
      $chromaAfter = Measure-ChromaPixels -Bmp $bmpAfter -Hwnd $hwnd -Rect $rowRect
      $blueAfter   = Measure-BluishPixels  -Bmp $bmpAfter -Hwnd $hwnd -Rect $rowRect
      $bmpAfter.Dispose()
      Write-Host "* 指派後：該列彩色像素數＝$chromaAfter（指派前 $chromaBefore）／**藍字像素數＝$blueAfter（指派前 $blueBefore）**"
      if ($blueAfter -lt 10) {
        $fails += "訴求2：清單「$speaker」未見指派色（#1E88E5 藍）之字色像素（藍字像素 $blueBefore → $blueAfter，門檻 10）＝推送或重繪未生效"
      } elseif ($blueAfter -le $blueBefore) {
        $fails += "訴求2：清單「$speaker」藍字像素未增加（$blueBefore → $blueAfter）＝字色非本次指派所致"
      } else { Write-Host "* [OK] 清單「$speaker」字色已變為指派之藍（藍字像素 $blueBefore → $blueAfter）" -ForegroundColor Green }
    }
  }
  #endregion

  #region E.訴求4：改指派之同名正規化（原色槽不再含該名） --------------------------------
  Write-Host "## E.訴求4 同名正規化 --------------------------------" -ForegroundColor Cyan
  $d1 = Get-SlotDescription -Slot $firstSlot
  if ([string]::IsNullOrWhiteSpace($d1) -or -not $d1.Contains($speaker)) {
    $notes += "訴求4：首次指派未落地，改指派之正規化**驗不到**"
  } else {
    [Win32Ui]::RightClick($rcx, $rcy)
    Start-Sleep -Milliseconds 900
    $menu2 = Get-OpenMenu -ProcessId $app.ProcessId
    if ($null -eq $menu2) { $fails += "訴求4：第二次右鍵未喚出選單" }
    else {
      $slot2 = @($menu2.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.AutomationId -eq ("SpeakerColorSlot" + $secondSlot) }) | Select-Object -First 1
      if ($null -eq $slot2) { $fails += "訴求4：找不到色槽項 SpeakerColorSlot$secondSlot" }
      else {
        $slot2.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 1600
        $newDesc = Get-SlotDescription -Slot $secondSlot
        $oldDesc = Get-SlotDescription -Slot $firstSlot
        Write-Host "* 改指派後：色槽 $secondSlot 描述＝「$newDesc」／色槽 $firstSlot 描述＝「$oldDesc」"
        if ([string]::IsNullOrWhiteSpace($newDesc) -or -not $newDesc.Contains($speaker)) {
          $fails += "訴求4：改指派後色槽 $secondSlot 描述未含「$speaker」（實得「$newDesc」）"
        }
        if (-not [string]::IsNullOrWhiteSpace($oldDesc) -and $oldDesc.Contains($speaker)) {
          $fails += "訴求4：改指派後原色槽 $firstSlot 仍含「$speaker」（實得「$oldDesc」）＝同名未正規化，兩槽並存"
        }
        if ($newDesc -and $newDesc.Contains($speaker) -and -not ($oldDesc -and $oldDesc.Contains($speaker))) {
          Write-Host "* [OK] 同名正規化成立：只留色槽 $secondSlot 一處" -ForegroundColor Green
        }
        Save-WindowShot -Hwnd $hwnd -Path (Join-Path $OutDir "04-after-reassign.png")
      }
    }
  }
  #endregion

  #region F.訴求5：影片頁同一選單亦喚得出（兩頁共用） --------------------------------
  Write-Host "## F.訴求5 影片頁同一選單 --------------------------------" -ForegroundColor Cyan
  if (-not (Switch-Tab -Root $root -TabId "TabVideo" -Hwnd $hwnd)) { $fails += "訴求5：切不到影片分頁（TabVideo）" }
  else {
    Start-Sleep -Milliseconds 900
    $vpanel = Find-ByAutomationId -Root $root -Id "SpeakerChecks"
    if ($null -eq $vpanel) {
      $notes += "訴求5：影片頁說話人面板（SpeakerChecks）不在 UIA 樹——未載入影片時該面板為空，**驗不到**；兩頁共用同一 SpeakerColorMenu 由原始碼與單元測試保證"
    } else {
      $vrows = @($vpanel.FindAll($TS::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::CheckBox))) |
        Where-Object { $_.Current.Name -notlike "*全部*" -and $_.Current.Name -notlike "*無說話人*" })
      if ($vrows.Count -eq 0) {
        $notes += "訴求5：影片頁說話人清單為空（未載入字幕），右鍵選單**驗不到**——空輸入之比對不算證據"
      } else {
        $vr = $vrows[0].Current.BoundingRectangle
        $vx = [int]($vr.X + $vr.Width / 2); $vy = [int]($vr.Y + $vr.Height / 2)
        [Win32Ui]::RightClick($vx, $vy)
        Start-Sleep -Milliseconds 900
        $vmenu = Get-OpenMenu -ProcessId $app.ProcessId
        if ($null -eq $vmenu) { $fails += "訴求5：影片頁說話人列右鍵未喚出選單（兩頁未同步）" }
        else {
          $vslots = @($vmenu.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
            Where-Object { $_.Current.AutomationId -like "SpeakerColorSlot*" })
          if ($vslots.Count -ne 12) { $fails += "訴求5：影片頁右鍵選單色槽項數＝$($vslots.Count)，應為 12" }
          else { Write-Host "* [OK] 影片頁同一選單亦喚得出（12 色槽）" -ForegroundColor Green }
          [Win32Ui]::KeyTap(0x1B, 40)   # Esc 關閉，不改影片頁資料
          Start-Sleep -Milliseconds 300
        }
        Save-WindowShot -Hwnd $hwnd -Path (Join-Path $OutDir "05-video-page-menu.png")
      }
    }
  }
  #endregion

}
finally {
  #region G.收尾：關程式並還原 APPDATA --------------------------------
  Write-Host "## G.收尾 --------------------------------" -ForegroundColor Cyan
  Get-Process -Name "LingoIsland" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit(5000) }
  Start-Sleep -Milliseconds 500
  if (Test-Path $backupDir) {
    Remove-Item -Path $appData -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item -Path $backupDir -Destination $appData -Recurse -Force
    Remove-Item -Path $backupDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "* 已還原 $appData"
  }
  #endregion
}
#endregion

#region IV.完成條件 ================================
Write-Host "# IV.完成條件 ================================" -ForegroundColor Blue
$notes | ForEach-Object { Write-Host "  [註] $_" -ForegroundColor Yellow }
if ($fails.Count -gt 0) {
  Write-Host "* 結果：FAIL（$($fails.Count) 項）" -ForegroundColor Red
  $fails | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
  exit 1
}
Write-Host "* 結果：PASS（訴求 1–5 於可驗範圍內全數成立；驗不到者見上方註記）" -ForegroundColor Green
exit 0
#endregion
