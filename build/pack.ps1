<#
.SYNOPSIS
  建置並打包 LingoIsland 發佈成品（Velopack）：dotnet publish -> vpk pack（帶 --icon）。

.DESCRIPTION
  依 VERSION 檔（或 -Version 參數）發佈 self-contained win-x64，再以 vpk 打包為
  Setup.exe / Portable.zip / <id>-<ver>-full.nupkg（有前版基準時另產 delta）/ releases.win.json。

  Issue #177：安裝檔須按業界常規帶「應用圖示」。vpk pack 以 --icon 指定 assets\app.ico，
  使 Setup.exe 及其建立之開始功能表／桌面捷徑、解除安裝項皆帶本應用圖示，而非 Velopack 預設圖示。
  安裝後之主程式 LingoIsland.exe 圖示另由 csproj <ApplicationIcon> 提供、兩者一致。

.PARAMETER Version
  發佈版號；預設讀 repo 根之 VERSION 檔。

.PARAMETER Configuration
  建置組態；預設 Release。

.NOTES
  需求：dotnet SDK、vpk（dotnet tool install -g vpk，Velopack CLI）。
  成品輸出至 repo 根之 Releases\。建置/測試 gate 由呼叫端（發佈列車）負責，本腳本專責 publish+pack。
  檔案編碼：UTF-8 with BOM（供 Windows PowerShell 5.1 正確讀取中文，不致亂碼）。
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'

#region I.主旨目的 ================================
Write-Host "# I.主旨目的 ================================" -ForegroundColor Blue
Write-Host "* 建置並打包 LingoIsland 發佈成品（Velopack）：dotnet publish -> vpk pack。"
Write-Host "* Issue #177：安裝檔 Setup.exe 依 --icon 帶應用圖示（assets\app.ico），非 Velopack 預設圖示。"
Write-Host "* 產物：Setup.exe / Portable.zip / *-full.nupkg / releases.win.json（輸出至 Releases\）。"
#endregion

#region II.參考準備 ================================
Write-Host "# II.參考準備 ================================" -ForegroundColor Blue

  #region A.參數準備 --------------------------------
  Write-Host "## A.參數準備 --------------------------------" -ForegroundColor Cyan

  $repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
  Set-Location $repo
  if ([string]::IsNullOrWhiteSpace($Version)) {
      $Version = (Get-Content (Join-Path $repo 'VERSION') -Raw).Trim()
  }
  $publishDir = Join-Path $repo 'publish'
  $outputDir  = Join-Path $repo 'Releases'
  $icon       = Join-Path $repo 'sysLingoIsland\assets\app.ico'

  Write-Host "* repo：$repo"
  Write-Host "* 版號：$Version（Configuration=$Configuration）"
  Write-Host "* 圖示：$icon"
  Write-Host "* 輸出：$outputDir"

  if (-not (Test-Path $icon)) {
      Write-Host "* 找不到應用圖示：$icon（Issue #177 安裝檔圖示所需）" -ForegroundColor Red
      exit 1
  }
  #endregion

#endregion

#region III.內容程序 ================================
Write-Host "# III.內容程序 ================================" -ForegroundColor Blue

  #region A.發佈（dotnet publish） --------------------------------
  Write-Host "## A.發佈（dotnet publish self-contained win-x64） --------------------------------" -ForegroundColor Cyan

  if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
  dotnet publish sysLingoIsland -c $Configuration -r win-x64 --self-contained -p:Version=$Version -o $publishDir
  if ($LASTEXITCODE -ne 0) {
      Write-Host "* dotnet publish 失敗（exit $LASTEXITCODE）" -ForegroundColor Red
      exit 1
  }
  Write-Host "* 發佈完成：$publishDir" -ForegroundColor Green
  #endregion

  #region B.簽章憑證（冪等：有則沿用、無則現產） --------------------------------
  Write-Host "## B.簽章憑證（冪等前置檢查） --------------------------------" -ForegroundColor Cyan

  # Issue #302：成品未簽章時 UAC 顯示「未知的發行者」。金鑰留在憑證存放區、
  # 標記不可匯出——**不產生任何 .pfx**，無金鑰檔案要保管、備份、防外洩。
  # 本段冪等：換機、重灌、憑證到期皆由發車當下自判補上，故不另建金鑰備份機制
  # （自簽憑證遺失之代價僅為重產一張＋重發 .cer）。
  $certSubject = 'CN=Carlton Chen (LingoIsland)'
  $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
      Where-Object { $_.Subject -eq $certSubject -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
      Sort-Object NotAfter -Descending | Select-Object -First 1
  if ($null -eq $cert) {
      Write-Host "* 無可用之簽章憑證，現產一張（NonExportable、傳統 CSP）。" -ForegroundColor Yellow
      # **傳統 CSP 為必要**：New-SelfSignedCertificate 預設之 CNG KSP 會使 signtool
      # 卡在 `After Private Key filter, 0 certs were left`
      # （MicrosoftDocs/windows-powershell-docs#1169）。
      $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $certSubject `
          -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable `
          -KeyUsage DigitalSignature `
          -Provider 'Microsoft Enhanced RSA and AES Cryptographic Provider' `
          -NotAfter (Get-Date).AddYears(10)
  }
  Write-Host "* 憑證：$($cert.Subject)｜指紋 $($cert.Thumbprint)｜到期 $($cert.NotAfter.ToString('yyyy-MM-dd'))"

  # 公開憑證入庫（非機密），供使用者端匯入以認得發行者；隨 Release 附出。
  $cerPath = Join-Path $PSScriptRoot 'LingoIsland-publisher.cer'
  Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
  Write-Host "* 公開憑證已匯出：$cerPath"

  # **signtool 來源＝Velopack 自帶**：本機未必裝 Windows SDK，而 vpk pack --signParams
  # 本就走這一支。**路徑動態解析取最新一版、不寫死 vpk 版本號**（升版後路徑會變）。
  $signtool = Get-ChildItem (Join-Path $env:USERPROFILE '.dotnet\tools\.store\vpk') `
      -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
      Sort-Object FullName -Descending | Select-Object -First 1
  if ($null -eq $signtool) {
      Write-Host "* 找不到 signtool.exe（預期在 vpk 之 vendor\signing 下）" -ForegroundColor Red
      exit 1
  }
  Write-Host "* signtool：$($signtool.FullName)"

  # **時戳為硬性**：無時戳者憑證一到期即所有舊版簽章一併失效，
  # 已發出去的安裝檔會在使用者端由「有效簽章」變成「無效簽章」，那是回收不了的。
  $signParams = "/sha1 $($cert.Thumbprint) /fd sha256 /tr http://timestamp.digicert.com /td sha256"
  #endregion

  #region C.打包（vpk pack --icon --signParams） --------------------------------
  Write-Host "## C.打包（vpk pack，帶 --icon 圖示與 --signParams 簽章） --------------------------------" -ForegroundColor Cyan

  vpk pack --packId LingoIsland --packVersion $Version --packDir $publishDir --mainExe LingoIsland.exe --icon $icon --outputDir $outputDir --signParams $signParams
  if ($LASTEXITCODE -ne 0) {
      Write-Host "* vpk pack 失敗（exit $LASTEXITCODE）" -ForegroundColor Red
      exit 1
  }
  Write-Host "* 打包完成。" -ForegroundColor Green
  #endregion

  #region D.驗簽（簽壞了與測不過同級，不出成品） --------------------------------
  Write-Host "## D.驗簽（Authenticode＋時戳） --------------------------------" -ForegroundColor Cyan

  # Issue #302：簽章壞掉與測試不過同級——**不發**。
  # **判準刻意不要求 Status -eq 'Valid'**：自簽憑證之根未被本機信任時 Status 為
  # UnknownError（訊息即「terminated in a root certificate which is not trusted」），
  # 那是**建置機有沒有匯入 .cer 的差別，不是成品的差別**；要求 Valid 會讓同一份成品
  # 在兩台機器得到不同判定。故驗三件事：① 簽章者指紋＝本次憑證 ② 帶 RFC3161 時戳
  # ③ 若已信任該根則須為 Valid（有匯入的機器順帶驗到底）。
  # **驗的是「散佈出去的那一份」，不是 publish\ 之暫存**——`vpk pack` 於打包過程簽的是
  # 它自己複製的副本，`publish\LingoIsland.exe` 自始至終未被簽（首版判準誤指該檔，
  # gate 當場判 NG，證明它不是空轉）。故解開 Portable.zip 驗其主程式與 Update.exe。
  $probe = Join-Path ([System.IO.Path]::GetTempPath()) ('vpkSignChk_' + [guid]::NewGuid().ToString('N'))
  $zip = Join-Path $outputDir 'LingoIsland-win-Portable.zip'
  $targets = @((Join-Path $outputDir 'LingoIsland-win-Setup.exe'))
  if (Test-Path $zip) {
      Expand-Archive $zip -DestinationPath $probe -Force
      $targets += (Get-ChildItem $probe -Filter '*.exe' -Recurse | Select-Object -Expand FullName)
  }
  $targets = $targets | Where-Object { Test-Path $_ }
  $signFail = 0
  foreach ($t in $targets) {
      $sig = Get-AuthenticodeSignature $t
      $who = $sig.SignerCertificate.Thumbprint
      $ts  = $sig.TimeStamperCertificate
      $okWho = ($who -eq $cert.Thumbprint)
      $okTs  = ($null -ne $ts)
      $okSt  = ($sig.Status -eq 'Valid') -or
               ($sig.StatusMessage -like '*not trusted*')
      if ($okWho -and $okTs -and $okSt) {
          Write-Host ("* [OK] {0}｜簽章者 {1}｜時戳 {2}" -f (Split-Path $t -Leaf), $who, $ts.Subject.Split(',')[0])
      } else {
          $signFail++
          Write-Host ("* [NG] {0}｜Status={1}｜簽章者={2}（期望 {3}）｜時戳={4}" -f `
              (Split-Path $t -Leaf), $sig.Status, $who, $cert.Thumbprint, $(if ($okTs) { '有' } else { '無' })) -ForegroundColor Red
          Write-Host ("       {0}" -f $sig.StatusMessage) -ForegroundColor Red
      }
  }
  if ($signFail -gt 0) {
      Write-Host "* 驗簽不過（$signFail 項）——**簽壞了與測不過同級，不出成品**。" -ForegroundColor Red
      exit 1
  }
  Remove-Item $probe -Recurse -Force -ErrorAction SilentlyContinue
  Write-Host "* 驗簽全過。" -ForegroundColor Green
  #endregion


#endregion

#region IV.備註紀錄 ================================
Write-Host "# IV.備註紀錄 ================================" -ForegroundColor Blue
Write-Host "* 成品（$outputDir）：" -ForegroundColor Green
Get-ChildItem $outputDir -File | Sort-Object Name | ForEach-Object { Write-Host "    - $($_.Name)" }
Write-Host "* Setup.exe 已帶應用圖示（--icon，Issue #177）；安裝後 LingoIsland.exe 圖示由 csproj <ApplicationIcon> 提供。" -ForegroundColor Green
#endregion
