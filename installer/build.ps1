<#
    Publie ZiaConvert (App + CLI), signe les executables si un certificat est present
    dans signing/, puis compile et signe l'installateur Inno Setup.

    Sans signing/ZiaConvert-selfsigned.pfx (jamais versionne, voir .gitignore), le
    build reste possible : executables et installateur sortent simplement non signes.
#>
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$pfx = Join-Path $root "signing\ZiaConvert-selfsigned.pfx"
$pfxPasswordFile = Join-Path $root "signing\cert-password.txt"

if (-not $SkipPublish) {
    Write-Host "Publication de ZiaConvert.exe..."
    & $dotnet publish "$root\src\ZiaConvert.App" -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$root\dist"

    Write-Host "Publication de zia.exe..."
    & $dotnet publish "$root\src\ZiaConvert.Cli" -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$root\dist"
}

# LICENSE.txt et THIRD-PARTY-NOTICES.md doivent toujours venir de la racine du depot,
# jamais d'une copie manuelle dans dist/ : une copie oubliee finit par mentir (verifie
# une fois : dist/THIRD-PARTY-NOTICES.md affirmait ImageMagick et Real-ESRGAN
# embarques alors qu'ils ne le sont pas encore).
Copy-Item "$root\LICENSE.txt" "$root\dist\LICENSE.txt" -Force
Copy-Item "$root\THIRD-PARTY-NOTICES.md" "$root\dist\THIRD-PARTY-NOTICES.md" -Force
Copy-Item "$root\packaging\LISEZ-MOI.txt" "$root\dist\LISEZ-MOI.txt" -Force

$canSign = (Test-Path $pfx) -and (Test-Path $pfxPasswordFile) -and (Test-Path $signtool)

if ($canSign) {
    $password = (Get-Content $pfxPasswordFile -Raw).Trim()

    Write-Host "Signature des executables..."
    & $signtool sign /f $pfx /p $password /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
        /d "ZiaConvert" "$root\dist\ZiaConvert.exe" "$root\dist\zia.exe"
    if ($LASTEXITCODE -ne 0) { throw "Echec de la signature des executables." }
}
else {
    Write-Warning "Pas de certificat dans signing/ : executables et installateur resteront non signes."
}

Write-Host "Compilation de l'installateur..."
if ($canSign) {
    # $q est le marqueur de guillemet litteral d'Inno Setup pour l'option /S : les
    # vrais guillemets entreraient en conflit avec ceux que PowerShell ajoute deja en
    # passant cet argument a ISCC.exe.
    $signCommand = '$q' + $signtool + '$q sign /f $q' + $pfx + '$q /p $q' + $password + '$q /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /d $qZiaConvert$q $f'
    & $iscc "/Ssigntool=$signCommand" "$PSScriptRoot\ZiaConvert.iss"
}
else {
    & $iscc "$PSScriptRoot\ZiaConvert.iss"
}
if ($LASTEXITCODE -ne 0) { throw "Echec de la compilation de l'installateur." }

Write-Host "Installateur pret dans installer\output\"
