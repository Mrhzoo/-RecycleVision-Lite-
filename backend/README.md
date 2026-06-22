# RecycleVision Lite Backend

This FastAPI service stores Unity session logs for later analysis.

## Run

From the project root:

```powershell
.\backend\run_backend.ps1
```

Or manually:
cd c:\2025-2026\3D\RecycleVision
.venv\Scripts\python -m uvicorn backend.recyclevision_api:app --host 127.0.0.1 --port 8000 --reload




```powershell
.\.venv\Scripts\python.exe -m pip install -r backend\requirements.txt
.\.venv\Scripts\python.exe -m uvicorn backend.recyclevision_api:app --host 127.0.0.1 --port 8000 --reload
```

If you use the ML-Agents environment instead:

```powershell
.\.venv-mlagents\Scripts\python.exe -m pip install -r backend\requirements.txt
.\.venv-mlagents\Scripts\python.exe -m uvicorn backend.recyclevision_api:app --host 127.0.0.1 --port 8000 --reload
```

## Unity Endpoint

Unity posts completed sessions to:

```text
http://127.0.0.1:8000/recyclevision/session
```

The payload contains session metadata, true bin, selected bin, predicted bin,
AI confidence, item features, and the 13-value ML feature vector.

## Analysis URLs

```text
http://127.0.0.1:8000/health
http://127.0.0.1:8000/recyclevision/sessions
http://127.0.0.1:8000/recyclevision/stats
http://127.0.0.1:8000/recyclevision/attempts.csv

http://127.0.0.1:8000/dashboard

http://127.0.0.1:8000/sessions_page

```

Data is stored in:

```text
backend/data/recyclevision_logs.sqlite3
backend/data/sessions.jsonl
```
