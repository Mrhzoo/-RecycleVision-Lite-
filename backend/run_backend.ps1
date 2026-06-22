$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location (Split-Path -Parent $scriptDir)

if (Test-Path ".venv\Scripts\python.exe") {
    $python = ".venv\Scripts\python.exe"
} elseif (Test-Path ".venv-mlagents\Scripts\python.exe") {
    $python = ".venv-mlagents\Scripts\python.exe"
} else {
    $python = "python"
}

& $python -m pip install -r backend\requirements.txt
& $python -m uvicorn backend.recyclevision_api:app --host 127.0.0.1 --port 8000 --reload
