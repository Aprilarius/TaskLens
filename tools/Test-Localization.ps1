$ErrorActionPreference = 'Stop'
$localizationRoot = Join-Path $PSScriptRoot '..\Localization'
$languages = @('en', 'ru', 'az')
$catalogs = @{}

foreach ($language in $languages) {
    $path = Join-Path $localizationRoot "Resources.$language.resx"
    [xml]$document = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    $catalog = @{}
    foreach ($entry in $document.root.data) {
        $key = [string]$entry.name
        $value = [string]$entry.value
        if ([string]::IsNullOrWhiteSpace($key) -or [string]::IsNullOrWhiteSpace($value)) {
            throw "Empty localization entry in $path"
        }
        if ($catalog.ContainsKey($key)) {
            throw "Duplicate localization key '$key' in $path"
        }
        $catalog[$key] = $value
    }
    $catalogs[$language] = $catalog
}

$referenceKeys = @($catalogs.en.Keys | Sort-Object)
foreach ($language in $languages) {
    $keys = @($catalogs[$language].Keys | Sort-Object)
    $missing = @($referenceKeys | Where-Object { -not $catalogs[$language].ContainsKey($_) })
    $extra = @($keys | Where-Object { -not $catalogs.en.ContainsKey($_) })
    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        throw "Localization key mismatch for '$language'. Missing: $($missing -join ', '); extra: $($extra -join ', ')"
    }
}

Write-Host "Localization catalogs verified: $($referenceKeys.Count) keys in $($languages.Count) languages."
