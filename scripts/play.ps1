# Portable launcher for the unchanged Ztrx runtime.
param(
    [Parameter(Position = 0)][string]$Game = "",
    [string]$Cache = "$env:LOCALAPPDATA\ztrx\cache",
    [string]$Data = "$env:LOCALAPPDATA\ztrx\data",
    [string]$Exe = "",
    [switch]$CheckOnly
)
$ErrorActionPreference = "Stop"
$packageRoot = Split-Path -Parent $PSScriptRoot
$logFile = $null
try {
    $logDir = Join-Path $packageRoot "logs"
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $logFile = Join-Path $logDir ("ztrx-" + (Get-Date -Format "yyyyMMdd-HHmmss-fff") + ".log")

    if ([string]::IsNullOrWhiteSpace($Exe)) {
        $Exe = Join-Path $packageRoot "victory.exe"
        if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
            $Exe = Join-Path $packageRoot "target\release\victory.exe"
        }
    }
    if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
        throw "victory.exe nao encontrado. Extraia o ZIP inteiro antes de abrir Iniciar.cmd."
    }
    $Exe = (Resolve-Path -LiteralPath $Exe).ProviderPath

    if ($CheckOnly) {
        $nativeArguments = @("--help")
        Write-Host "Verificando a abertura de victory.exe; o jogo nao sera executado."
    } else {
        if ([string]::IsNullOrWhiteSpace($Game)) {
            $Game = "D:\GTA6\GTA6.Xbox.v1.0.2.1-CYBERLEEK"
            if (-not (Test-Path -LiteralPath $Game)) {
                $Game = (Read-Host "Cole o caminho da pasta do jogo ou de game_scarlett_master.exe").Trim('"')
            }
        }
        if (Test-Path -LiteralPath $Game -PathType Container) {
            $Game = Join-Path $Game "game_scarlett_master.exe"
        }
        if (-not (Test-Path -LiteralPath $Game -PathType Leaf)) {
            throw "Executavel do jogo nao encontrado em: $Game"
        }
        $Game = (Resolve-Path -LiteralPath $Game).ProviderPath
        New-Item -ItemType Directory -Force -Path $Cache, $Data | Out-Null
        $env:VICTORY_CACHE_DIR = (Resolve-Path -LiteralPath $Cache).ProviderPath
        $env:VICTORY_DATA_DIR = (Resolve-Path -LiteralPath $Data).ProviderPath
        foreach ($name in @("VICTORY_KEYS", "VICTORY_CAPTURE", "VICTORY_CAPTURE_FRAMES", "VICTORY_DUMP_TARGETS", "VICTORY_RELAY", "VICTORY_LATE_WAIT", "VICTORY_WIDE_BACK")) {
            Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        }
        $nativeArguments = @("run", $Game, "--execute")
        Write-Host "Jogo: $Game"
        Write-Host "Cache: $env:VICTORY_CACHE_DIR"
        Write-Host "Saves: $env:VICTORY_DATA_DIR"
    }
    Write-Host "Executavel: $Exe"
    Write-Host "Log: $logFile"
    Push-Location -LiteralPath (Split-Path -Parent $Exe)
    try {
        # Windows PowerShell 5.1 represents native stderr as ErrorRecord.
        # Keep normal runtime logging on stderr from aborting the launcher.
        $ErrorActionPreference = "Continue"
        & $Exe @nativeArguments 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $logFile | Out-Host
        $nativeExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = "Stop"
        Pop-Location
    }
    Write-Host ""
    Write-Host "Processo encerrado com codigo $nativeExitCode."
    Write-Host "Log: $logFile"
    exit $nativeExitCode
} catch {
    $failure = $_.Exception.Message
    Write-Host ""
    Write-Host "ERRO: $failure" -ForegroundColor Red
    if ($logFile) {
        Add-Content -LiteralPath $logFile -Value ("ERRO: " + $failure) -ErrorAction SilentlyContinue
        Write-Host "Log: $logFile"
    }
    exit 1
}
