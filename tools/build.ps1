param(
    [string]$GameDir = 'E:\SteamLibrary\steamapps\common\Shroom and Gloom'
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
New-Item -ItemType Directory -Path '.tools' -Force | Out-Null
$archive = Join-Path (Get-Location) '.tools\BepInEx-6.0.0-be.788.zip'
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri 'https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne 'F4CC496BD098A0DF4164B81E3737297707F13A47C2478DBA2F60EEFAB784817A') {
    throw 'BepInEx-Download hat eine unerwartete Prüfsumme.'
}
if (-not (Test-Path -LiteralPath '.tools\loader\BepInEx\core\BepInEx.Core.dll')) {
    Expand-Archive -LiteralPath $archive -DestinationPath '.tools\loader'
}
python -m pip install --target '.tools/python' -r requirements.txt --disable-pip-version-check --quiet
if ($LASTEXITCODE -ne 0) { throw 'Python-Abhängigkeiten konnten nicht vorbereitet werden.' }
$env:PYTHONPATH = Join-Path (Get-Location) '.tools\python'
python tools/export_texts.py --game $GameDir
if ($LASTEXITCODE -ne 0) { throw 'Textexport fehlgeschlagen.' }
python tools/build_translation.py
if ($LASTEXITCODE -ne 0) { throw 'Übersetzungsprüfung fehlgeschlagen.' }
python tools/build_languages.py --only de en --workers 1
if ($LASTEXITCODE -ne 0) { throw 'Basis-Sprachpakete konnten nicht aktualisiert werden.' }
python tools/validate_languages.py
if ($LASTEXITCODE -ne 0) { throw 'Sprachpaketprüfung fehlgeschlagen.' }
dotnet run --project tools/InteropPrep -- $GameDir '.tools\loader'
if ($LASTEXITCODE -ne 0) { throw 'Offline-Interop-Generierung fehlgeschlagen.' }
dotnet build src/ShroomGerman -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Plugin-Build fehlgeschlagen.' }
python tools/package.py
if ($LASTEXITCODE -ne 0) { throw 'Modpaket konnte nicht erstellt werden.' }
dotnet publish src/Installer -c Release -o dist --nologo
if ($LASTEXITCODE -ne 0) { throw 'Installer-Build fehlgeschlagen.' }
Get-FileHash -LiteralPath 'dist\ShroomAndGloom-FanTranslations-Setup.exe' -Algorithm SHA256
