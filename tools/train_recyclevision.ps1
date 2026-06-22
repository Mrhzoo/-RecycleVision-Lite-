param(
    [string]$RunId = "recyclevision",
    [string]$ConfigPath = ".\\config\\RecycleVision.yaml",
    [int]$TimeScale = 20,
    [string]$PythonExe = "",
    [switch]$Force,
    [switch]$Resume
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Require-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $projectRoot

$pythonExe = $PythonExe

if ([string]::IsNullOrWhiteSpace($pythonExe)) {
    $preferredVenv = Join-Path $projectRoot ".venv-mlagents\Scripts\python.exe"
    $fallbackVenv = Join-Path $projectRoot ".venv\Scripts\python.exe"

    if (Test-Path $preferredVenv) {
        $pythonExe = $preferredVenv
    } elseif (Test-Path $fallbackVenv) {
        $pythonExe = $fallbackVenv
    } else {
        $pythonExe = "python"
    }
}
Require-Command $pythonExe

$shimPath = Join-Path $PSScriptRoot "python_shims"
if (Test-Path $shimPath) {
    if ($env:PYTHONPATH) {
        $env:PYTHONPATH = "$shimPath;$env:PYTHONPATH"
    } else {
        $env:PYTHONPATH = $shimPath
    }
}

if (-not (Test-Path $ConfigPath)) {
    throw "Config not found: $ConfigPath"
}

if ($Force -and $Resume) {
    throw "Only one of -Force or -Resume can be specified."
}

Write-Host "Starting training with run id '$RunId'."
Write-Host "When the trainer says it is waiting for Unity, press Play in the Editor."

$learnArgs = @(
    $ConfigPath,
    "--run-id",
    $RunId,
    "--time-scale",
    $TimeScale
)

if ($Force) {
    $learnArgs += "--force"
}

if ($Resume) {
    $learnArgs += "--resume"
}

& $pythonExe -m mlagents.trainers.learn @learnArgs

if ($LASTEXITCODE -ne 0) {
    throw "Training failed with exit code $LASTEXITCODE."
}

$resultsDir = Join-Path $projectRoot "results\\$RunId"
$onnxSource = Join-Path $resultsDir "RecycleVisionSorter.onnx"

if (-not (Test-Path $onnxSource)) {
    throw "Model not found: $onnxSource"
}

$destDir = Join-Path $projectRoot "Assets\\RecycleVision\\Models"
$destPath = Join-Path $destDir "RecycleVisionSorter.onnx"

New-Item -ItemType Directory -Path $destDir -Force | Out-Null
Copy-Item -Path $onnxSource -Destination $destPath -Force

Write-Host "Copied model to $destPath"
Write-Host "Unity will import it automatically; the editor auto-setup will assign it to RecycleVisionMlAgent."
