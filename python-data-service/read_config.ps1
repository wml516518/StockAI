# PowerShell script to read Python path from config file
param(
    [string]$ConfigFile
)

if (-not $ConfigFile) {
    $ConfigFile = Join-Path $PSScriptRoot "python_path.conf"
}

if (Test-Path $ConfigFile) {
    $content = Get-Content $ConfigFile -ErrorAction SilentlyContinue
    $line = $content | Where-Object { $_ -match '^PYTHON_CMD=' -and $_ -notmatch '^#' } | Select-Object -First 1
    if ($line) {
        $value = ($line -split '=',2)[1].Trim()
        Write-Output $value
    }
}
