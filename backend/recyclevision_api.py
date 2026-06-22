from __future__ import annotations

import csv
import io
import json
import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from fastapi import FastAPI, Query
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import HTMLResponse, StreamingResponse


BASE_DIR = Path(__file__).resolve().parent
DATA_DIR = BASE_DIR / "data"
DB_PATH = DATA_DIR / "recyclevision_logs.sqlite3"
JSONL_PATH = DATA_DIR / "sessions.jsonl"

DATA_DIR.mkdir(parents=True, exist_ok=True)

app = FastAPI(
    title="RecycleVision Lite API",
    version="1.0.0",
    description="Stores Unity recycling session logs for later analysis and visualisation.",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def connect() -> sqlite3.Connection:
    connection = sqlite3.connect(DB_PATH)
    connection.row_factory = sqlite3.Row
    return connection


def init_db() -> None:
    with connect() as connection:
        connection.execute(
            """
            CREATE TABLE IF NOT EXISTS sessions (
                session_id TEXT PRIMARY KEY,
                schema_version TEXT,
                project_name TEXT,
                session_mode TEXT,
                started_at_utc TEXT,
                ended_at_utc TEXT,
                received_at_utc TEXT,
                unity_version TEXT,
                total_attempts INTEGER,
                human_attempts INTEGER,
                auto_sorted_attempts INTEGER,
                human_accuracy REAL,
                ai_accuracy REAL,
                raw_json TEXT NOT NULL
            )
            """
        )
        connection.execute(
            """
            CREATE TABLE IF NOT EXISTS attempts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                attempt_index INTEGER,
                item_id TEXT,
                display_name TEXT,
                true_bin TEXT,
                selected_bin TEXT,
                predicted_bin TEXT,
                ai_confidence REAL,
                human_correct INTEGER,
                ai_correct INTEGER,
                was_auto_sorted INTEGER,
                visual_shape TEXT,
                visual_shape_index INTEGER,
                scale REAL,
                primary_color_json TEXT,
                accent_color_json TEXT,
                feature_vector_json TEXT,
                snapshot_png TEXT,
                FOREIGN KEY(session_id) REFERENCES sessions(session_id)
            )
            """
        )


@app.on_event("startup")
def on_startup() -> None:
    init_db()


@app.get("/")
def root() -> dict[str, str]:
    return {
        "service": "RecycleVision Lite API",
        "health": "/health",
        "sessions": "/recyclevision/sessions",
        "stats": "/recyclevision/stats",
        "csv": "/recyclevision/attempts.csv",
        "dashboard": "/dashboard",
        "sessions_page": "/sessions_page",
    }


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok", "time_utc": utc_now()}


@app.post("/recyclevision/session")
def receive_session(payload: dict[str, Any]) -> dict[str, Any]:
    init_db()

    session_id = str(payload.get("session_id") or f"session_{datetime.now(timezone.utc).timestamp()}")
    payload["session_id"] = session_id
    attempts = payload.get("attempts") or []
    received_at = utc_now()

    raw_json = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))

    with connect() as connection:
        connection.execute(
            """
            INSERT OR REPLACE INTO sessions (
                session_id,
                schema_version,
                project_name,
                session_mode,
                started_at_utc,
                ended_at_utc,
                received_at_utc,
                unity_version,
                total_attempts,
                human_attempts,
                auto_sorted_attempts,
                human_accuracy,
                ai_accuracy,
                raw_json
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                session_id,
                payload.get("schema_version"),
                payload.get("project_name"),
                payload.get("session_mode"),
                payload.get("started_at_utc"),
                payload.get("ended_at_utc"),
                received_at,
                payload.get("unity_version"),
                int(payload.get("total_attempts") or len(attempts)),
                int(payload.get("human_attempts") or 0),
                int(payload.get("auto_sorted_attempts") or 0),
                float(payload.get("human_accuracy") or 0.0),
                float(payload.get("ai_accuracy") or 0.0),
                raw_json,
            ),
        )
        connection.execute("DELETE FROM attempts WHERE session_id = ?", (session_id,))

        for attempt in attempts:
            insert_attempt(connection, session_id, attempt)

    with JSONL_PATH.open("a", encoding="utf-8") as handle:
        handle.write(raw_json + "\n")

    return {
        "ok": True,
        "session_id": session_id,
        "attempts_stored": len(attempts),
        "received_at_utc": received_at,
    }


def insert_attempt(connection: sqlite3.Connection, session_id: str, attempt: dict[str, Any]) -> None:
    connection.execute(
        """
        INSERT INTO attempts (
            session_id,
            attempt_index,
            item_id,
            display_name,
            true_bin,
            selected_bin,
            predicted_bin,
            ai_confidence,
            human_correct,
            ai_correct,
            was_auto_sorted,
            visual_shape,
            visual_shape_index,
            scale,
            primary_color_json,
            accent_color_json,
            feature_vector_json,
            snapshot_png
        )
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            session_id,
            int(attempt.get("attempt_index") or 0),
            attempt.get("item_id"),
            attempt.get("display_name"),
            attempt.get("true_bin") or attempt.get("correct_bin"),
            attempt.get("selected_bin") or attempt.get("human_bin"),
            attempt.get("predicted_bin") or attempt.get("ai_bin"),
            float(attempt.get("ai_confidence") or 0.0),
            int(bool(attempt.get("human_correct"))),
            int(bool(attempt.get("ai_correct"))),
            int(bool(attempt.get("was_auto_sorted"))),
            attempt.get("visual_shape"),
            int(attempt.get("visual_shape_index") or -1),
            float(attempt.get("scale") or 0.0),
            json.dumps(attempt.get("primary_color")),
            json.dumps(attempt.get("accent_color")),
            json.dumps(attempt.get("feature_vector")),
            attempt.get("snapshot_png"),
        ),
    )


@app.get("/recyclevision/sessions")
def list_sessions(limit: int = 20) -> list[dict[str, Any]]:
    """
    Returns latest sessions for UI/history screens.
    """
    init_db()

    with connect() as connection:
        rows = connection.execute(
            """
            SELECT
                session_id,
                project_name,
                session_mode,
                started_at_utc,
                ended_at_utc,
                received_at_utc,
                total_attempts,
                human_accuracy,
                ai_accuracy
            FROM sessions
            ORDER BY received_at_utc DESC
            LIMIT ?
            """,
            (limit,),
        ).fetchall()

    return [dict(row) for row in rows]


@app.get("/recyclevision/session/{session_id}")
def get_session_detail(session_id: str) -> dict[str, Any]:
    """Returns full detail for a single session including its attempts."""
    init_db()

    with connect() as connection:
        session_row = connection.execute(
            "SELECT * FROM sessions WHERE session_id = ?", (session_id,)
        ).fetchone()

        if session_row is None:
            return {"error": "Session not found", "session_id": session_id}

        attempt_rows = connection.execute(
            """
            SELECT * FROM attempts
            WHERE session_id = ?
            ORDER BY attempt_index
            """,
            (session_id,),
        ).fetchall()

    return {
        "session": dict(session_row),
        "attempts": [dict(row) for row in attempt_rows],
    }


@app.get("/recyclevision/stats")
def stats() -> dict[str, Any]:
    """
    Aggregated statistics for dashboards.
    The payload is intentionally structured so the UI can render:
    - per-class bars (by_class + class_support)
    - confusion cards (top_ai_confusions + top_human_confusions)
    - recent activity/timeline (timeline + recent_sessions)
    """
    init_db()

    with connect() as connection:
        totals = connection.execute(
            """
            SELECT
                COUNT(DISTINCT session_id) AS session_count,
                COUNT(*) AS attempt_count,
                AVG(ai_correct) AS ai_accuracy,
                AVG(CASE WHEN was_auto_sorted = 0 THEN human_correct END) AS human_accuracy
            FROM attempts
            """
        ).fetchone()

        # Per-class performance
        by_class = connection.execute(
            """
            SELECT
                true_bin,
                COUNT(*) AS attempts,
                AVG(ai_correct) AS ai_accuracy,
                AVG(ai_confidence) AS avg_confidence
            FROM attempts
            GROUP BY true_bin
            ORDER BY true_bin
            """
        ).fetchall()

        # Per-class human accuracy
        by_class_human = connection.execute(
            """
            SELECT
                true_bin,
                AVG(CASE WHEN was_auto_sorted = 0 THEN human_correct END) AS human_accuracy,
                COUNT(*) AS human_attempts
            FROM attempts
            WHERE was_auto_sorted = 0
            GROUP BY true_bin
            ORDER BY true_bin
            """
        ).fetchall()

        # Support counts for bar sizing
        class_support = connection.execute(
            """
            SELECT
                true_bin,
                COUNT(*) AS support
            FROM attempts
            GROUP BY true_bin
            ORDER BY true_bin
            """
        ).fetchall()

        # Full confusion matrix for AI
        ai_confusion_matrix = connection.execute(
            """
            SELECT
                true_bin,
                predicted_bin,
                COUNT(*) AS count
            FROM attempts
            GROUP BY true_bin, predicted_bin
            ORDER BY true_bin, predicted_bin
            """
        ).fetchall()

        # Full confusion matrix for Human
        human_confusion_matrix = connection.execute(
            """
            SELECT
                true_bin,
                selected_bin AS predicted_bin,
                COUNT(*) AS count
            FROM attempts
            WHERE was_auto_sorted = 0
            GROUP BY true_bin, selected_bin
            ORDER BY true_bin, selected_bin
            """
        ).fetchall()

        # Top AI confusions: where AI predicted wrong
        top_ai_confusions = connection.execute(
            """
            SELECT
                true_bin,
                predicted_bin,
                COUNT(*) AS count,
                AVG(ai_confidence) AS avg_confidence
            FROM attempts
            WHERE true_bin != predicted_bin
            GROUP BY true_bin, predicted_bin
            ORDER BY count DESC
            LIMIT 8
            """
        ).fetchall()

        # Top human confusions (manual only)
        top_human_confusions = connection.execute(
            """
            SELECT
                true_bin,
                selected_bin AS mistaken_bin,
                COUNT(*) AS count
            FROM attempts
            WHERE was_auto_sorted = 0
              AND true_bin != selected_bin
            GROUP BY true_bin, selected_bin
            ORDER BY count DESC
            LIMIT 8
            """
        ).fetchall()

        # Recent sessions for UI cards
        recent_sessions = connection.execute(
            """
            SELECT
                session_id,
                session_mode,
                received_at_utc,
                total_attempts,
                human_accuracy,
                ai_accuracy
            FROM sessions
            ORDER BY received_at_utc DESC
            LIMIT 6
            """
        ).fetchall()

        # Timeline by session
        timeline_sessions = connection.execute(
            """
            SELECT
                received_at_utc,
                total_attempts,
                human_accuracy,
                ai_accuracy,
                session_mode
            FROM sessions
            ORDER BY received_at_utc ASC
            """
        ).fetchall()

        # Build confusion matrix arrays
        category_names = ["Plastic", "Paper / Cardboard", "Glass", "Organic"]
        ai_matrix = [[0] * 4 for _ in range(4)]
        human_matrix = [[0] * 4 for _ in range(4)]

        for row in ai_confusion_matrix:
            try:
                t = category_names.index(row["true_bin"])
                p = category_names.index(row["predicted_bin"])
                ai_matrix[t][p] = row["count"]
            except ValueError:
                pass

        for row in human_confusion_matrix:
            try:
                t = category_names.index(row["true_bin"])
                p = category_names.index(row["predicted_bin"])
                human_matrix[t][p] = row["count"]
            except ValueError:
                pass

    return {
        "session_count": int(totals["session_count"] or 0),
        "attempt_count": int(totals["attempt_count"] or 0),
        "ai_accuracy": float(totals["ai_accuracy"] or 0.0),
        "human_accuracy": float(totals["human_accuracy"] or 0.0),
        "by_class": [dict(row) for row in by_class],
        "by_class_human": [dict(row) for row in by_class_human],
        "class_support": [dict(row) for row in class_support],
        "top_ai_confusions": [dict(row) for row in top_ai_confusions],
        "top_human_confusions": [dict(row) for row in top_human_confusions],
        "recent_sessions": [dict(row) for row in recent_sessions],
        "timeline": [dict(row) for row in timeline_sessions],
        "confusion_matrix_ai": ai_matrix,
        "confusion_matrix_human": human_matrix,
        "category_names": category_names,
    }


@app.get("/recyclevision/attempts.csv")
def attempts_csv() -> StreamingResponse:
    init_db()

    with connect() as connection:
        rows = connection.execute(
            """
            SELECT
                session_id,
                attempt_index,
                item_id,
                display_name,
                true_bin,
                selected_bin,
                predicted_bin,
                ai_confidence,
                human_correct,
                ai_correct,
                was_auto_sorted,
                visual_shape,
                visual_shape_index,
                scale,
                feature_vector_json
            FROM attempts
            ORDER BY session_id, attempt_index
            """
        ).fetchall()

    output = io.StringIO()
    writer = csv.writer(output)
    writer.writerow(rows[0].keys() if rows else [
        "session_id",
        "attempt_index",
        "item_id",
        "display_name",
        "true_bin",
        "selected_bin",
        "predicted_bin",
        "ai_confidence",
        "human_correct",
        "ai_correct",
        "was_auto_sorted",
        "visual_shape",
        "visual_shape_index",
        "scale",
        "feature_vector_json",
    ])

    for row in rows:
        writer.writerow([row[key] for key in row.keys()])

    output.seek(0)
    return StreamingResponse(
        iter([output.getvalue()]),
        media_type="text/csv",
        headers={"Content-Disposition": "attachment; filename=recyclevision_attempts.csv"},
    )


# ─── Interactive HTML Dashboard ─────────────────────────────────────────────


DASHBOARD_HTML = r"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>RecycleVision Dashboard</title>
<script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.7/dist/chart.umd.min.js"></script>
<style>
  :root {
    --bg: #0b0e14;
    --surface: #131a24;
    --surface2: #1a2330;
    --accent: #f0c84a;
    --green: #3ce07a;
    --red: #f05a4a;
    --blue: #4a8af0;
    --text: #d8e0ee;
    --text-dim: #7a8aa0;
  }
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body {
    font-family: 'Segoe UI', system-ui, -apple-system, sans-serif;
    background: var(--bg);
    color: var(--text);
    padding: 24px;
    min-height: 100vh;
  }
  h1 { font-size: 28px; margin-bottom: 4px; display: flex; align-items: center; gap: 12px; }
  h1 small { font-size: 14px; color: var(--text-dim); font-weight: 400; }
  h2 { font-size: 18px; color: var(--accent); margin-bottom: 14px; display: flex; align-items: center; gap: 8px; }
  .subtitle { color: var(--text-dim); margin-bottom: 24px; font-size: 14px; }
  .top-bar { display: flex; gap: 16px; flex-wrap: wrap; margin-bottom: 24px; }
  .stat-card {
    background: var(--surface);
    border: 1px solid #1f2b3a;
    border-radius: 12px;
    padding: 18px 22px;
    min-width: 140px;
    flex: 1;
  }
  .stat-card .label { font-size: 12px; text-transform: uppercase; letter-spacing: 1px; color: var(--text-dim); }
  .stat-card .value { font-size: 32px; font-weight: 700; margin-top: 4px; }
  .stat-card .value.green { color: var(--green); }
  .stat-card .value.blue { color: var(--blue); }
  .stat-card .value.accent { color: var(--accent); }
  .stat-card .value.red { color: var(--red); }
  .grid-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; margin-bottom: 24px; }
  .grid-3 { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 20px; margin-bottom: 24px; }
  @media (max-width: 900px) { .grid-2, .grid-3 { grid-template-columns: 1fr; } }
  .card {
    background: var(--surface);
    border: 1px solid #1f2b3a;
    border-radius: 12px;
    padding: 18px;
  }
  .card.full { grid-column: 1 / -1; }
  canvas { width: 100% !important; height: auto !important; max-height: 300px; }
  .confusion-table { width: 100%; border-collapse: collapse; font-size: 13px; }
  .confusion-table th, .confusion-table td { padding: 8px 12px; text-align: center; border: 1px solid #1f2b3a; }
  .confusion-table th { background: var(--surface2); color: var(--text-dim); font-weight: 600; font-size: 11px; text-transform: uppercase; letter-spacing: 0.5px; }
  .confusion-table td { font-weight: 600; }
  .session-list { display: flex; flex-direction: column; gap: 8px; max-height: 360px; overflow-y: auto; }
  .session-item {
    background: var(--surface2);
    border-radius: 8px;
    padding: 10px 14px;
    display: flex;
    justify-content: space-between;
    align-items: center;
    font-size: 13px;
    cursor: pointer;
    transition: background 0.15s;
    border: 1px solid transparent;
  }
  .session-item:hover { border-color: var(--accent); background: #1e2a3a; }
  .session-item .left { display: flex; flex-direction: column; gap: 2px; }
  .session-item .session-id { font-family: monospace; font-size: 11px; color: var(--text-dim); }
  .session-item .session-meta { display: flex; gap: 12px; font-size: 12px; }
  .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 10px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.3px; }
  .badge.training { background: #1a3a5c; color: #6ab0f0; }
  .badge.quicksort { background: #3a3a1a; color: #f0d060; }
  .badge.green { background: #143a20; color: var(--green); }
  .confusion-list { display: flex; flex-direction: column; gap: 6px; }
  .confusion-item {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 6px 10px;
    background: var(--surface2);
    border-radius: 6px;
    font-size: 12px;
  }
  .confusion-item .arrow { color: var(--text-dim); margin: 0 6px; }
  .color-dot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; margin-right: 6px; vertical-align: middle; }
  .nav-tabs { display: flex; gap: 4px; margin-bottom: 20px; background: var(--surface); border-radius: 10px; padding: 4px; width: fit-content; }
  .nav-tabs a { padding: 8px 18px; border-radius: 8px; text-decoration: none; color: var(--text-dim); font-size: 13px; font-weight: 500; transition: all 0.15s; }
  .nav-tabs a.active { background: var(--accent); color: #0b0e14; }
  .nav-tabs a:hover:not(.active) { color: var(--text); background: var(--surface2); }
  .loading { text-align: center; padding: 60px; color: var(--text-dim); font-size: 16px; }
  .error { text-align: center; padding: 40px; color: var(--red); }
  .progress-bar { height: 6px; background: var(--surface2); border-radius: 3px; overflow: hidden; margin: 4px 0; }
  .progress-bar .fill { height: 100%; border-radius: 3px; transition: width 0.5s; }
  /* Scrollbar */
  ::-webkit-scrollbar { width: 6px; }
  ::-webkit-scrollbar-track { background: transparent; }
  ::-webkit-scrollbar-thumb { background: #2a3a4a; border-radius: 3px; }
</style>
</head>
<body>
  <h1>♻ RecycleVision <small>Sorting Analytics Dashboard</small></h1>
  <p class="subtitle">Real-time recycling performance metrics, confusion analysis, and session history.</p>

  <div class="nav-tabs">
    <a href="#" class="active" onclick="switchTab('overview')">Overview</a>
    <a href="#" onclick="switchTab('sessions')">Sessions</a>
    <a href="#" onclick="switchTab('confusion')">Confusion Analysis</a>
  </div>

  <div id="tab-overview">
    <div class="top-bar" id="stat-cards"></div>

    <div class="grid-2">
      <div class="card">
        <h2>📊 Accuracy Comparison</h2>
        <canvas id="accuracyChart"></canvas>
      </div>
      <div class="card">
        <h2>🎯 Per-Class Accuracy</h2>
        <canvas id="classChart"></canvas>
      </div>
    </div>

    <div class="grid-2">
      <div class="card full">
        <h2>📈 Performance Timeline</h2>
        <canvas id="timelineChart"></canvas>
      </div>
    </div>
  </div>

  <div id="tab-sessions" style="display:none;">
    <div class="card full">
      <h2>📋 Recent Sessions</h2>
      <div class="session-list" id="session-list"></div>
    </div>
  </div>

  <div id="tab-confusion" style="display:none;">
    <div class="grid-2">
      <div class="card">
        <h2>🤖 AI Confusion Matrix</h2>
        <div id="ai-confusion-table"></div>
      </div>
      <div class="card">
        <h2>🧑 Human Confusion Matrix</h2>
        <div id="human-confusion-table"></div>
      </div>
    </div>
    <div class="grid-2">
      <div class="card">
        <h2>⚠ Top AI Confusions</h2>
        <div id="ai-confusion-list"></div>
      </div>
      <div class="card">
        <h2>⚠ Top Human Confusions</h2>
        <div id="human-confusion-list"></div>
      </div>
    </div>
  </div>

<script>
// ── Color helpers ──
const CAT_COLORS = {
  'Plastic': '#4a8af0',
  'Paper / Cardboard': '#f0c84a',
  'Glass': '#3ce07a',
  'Organic': '#d07a40'
};
const CAT_SHORT = {'Plastic':'Plastic','Paper / Cardboard':'Paper','Glass':'Glass','Organic':'Organic'};

// ── Chart instances ──
let accuracyChart = null, classChart = null, timelineChart = null;

// ── Tab switching ──
function switchTab(name) {
  document.querySelectorAll('.nav-tabs a').forEach(a => a.classList.remove('active'));
  document.querySelectorAll('[id^="tab-"]').forEach(el => el.style.display = 'none');
  document.getElementById('tab-' + name).style.display = 'block';
  document.querySelector(`.nav-tabs a[onclick*="${name}"]`).classList.add('active');
  // Resize charts when tab becomes visible
  setTimeout(() => {
    if (accuracyChart) accuracyChart.resize();
    if (classChart) classChart.resize();
    if (timelineChart) timelineChart.resize();
  }, 100);
}

// ── Render stat cards ──
function renderStats(data) {
  const container = document.getElementById('stat-cards');
  const humanPct = (data.human_accuracy * 100).toFixed(1);
  const aiPct = (data.ai_accuracy * 100).toFixed(1);
  container.innerHTML = `
    <div class="stat-card"><div class="label">Sessions</div><div class="value blue">${data.session_count}</div></div>
    <div class="stat-card"><div class="label">Total Attempts</div><div class="value accent">${data.attempt_count}</div></div>
    <div class="stat-card"><div class="label">AI Accuracy</div><div class="value green">${aiPct}%</div></div>
    <div class="stat-card"><div class="label">Human Accuracy</div><div class="value ${data.human_accuracy >= 0.5 ? 'green' : 'red'}">${data.human_accuracy > 0 ? humanPct + '%' : 'N/A'}</div></div>
  `;
}

// ── Render accuracy comparison chart ──
function renderAccuracyChart(data) {
  const ctx = document.getElementById('accuracyChart').getContext('2d');
  if (accuracyChart) accuracyChart.destroy();

  const labels = data.by_class.map(c => CAT_SHORT[c.true_bin] || c.true_bin);
  const aiAcc = data.by_class.map(c => (c.ai_accuracy || 0) * 100);
  const humanAccMap = {};
  (data.by_class_human || []).forEach(c => { humanAccMap[c.true_bin] = (c.human_accuracy || 0) * 100; });
  const humanAcc = labels.map((_, i) => humanAccMap[data.by_class[i].true_bin] || 0);

  accuracyChart = new Chart(ctx, {
    type: 'bar',
    data: {
      labels,
      datasets: [
        { label: 'AI Accuracy', data: aiAcc, backgroundColor: 'rgba(60, 224, 122, 0.7)', borderColor: '#3ce07a', borderWidth: 1 },
        { label: 'Human Accuracy', data: humanAcc, backgroundColor: 'rgba(74, 138, 240, 0.7)', borderColor: '#4a8af0', borderWidth: 1 }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: true,
      plugins: { legend: { labels: { color: '#d8e0ee', font: { size: 11 } } } },
      scales: {
        x: { ticks: { color: '#7a8aa0', font: { size: 10 } }, grid: { color: '#1f2b3a' } },
        y: { min: 0, max: 100, ticks: { color: '#7a8aa0', callback: v => v + '%' }, grid: { color: '#1f2b3a' } }
      }
    }
  });
}

// ── Render per-class accuracy chart ──
function renderClassChart(data) {
  const ctx = document.getElementById('classChart').getContext('2d');
  if (classChart) classChart.destroy();

  const labels = data.by_class.map(c => CAT_SHORT[c.true_bin] || c.true_bin);
  const support = {};
  data.class_support.forEach(c => { support[c.true_bin] = c.support; });
  const supports = data.by_class.map(c => support[c.true_bin] || 0);
  const confidences = data.by_class.map(c => (c.avg_confidence || 0) * 100);
  const attempts = data.by_class.map(c => c.attempts || 0);
  const colors = labels.map(l => CAT_COLORS[Object.keys(CAT_COLORS).find(k => CAT_SHORT[k] === l) || l] || '#4a8af0');

  classChart = new Chart(ctx, {
    type: 'radar',
    data: {
      labels,
      datasets: [{
        label: 'Avg Confidence',
        data: confidences,
        borderColor: '#f0c84a',
        backgroundColor: 'rgba(240, 200, 74, 0.15)',
        pointBackgroundColor: colors,
        pointBorderColor: colors,
        borderWidth: 2
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: true,
      plugins: {
        legend: { labels: { color: '#d8e0ee', font: { size: 11 } } },
        tooltip: {
          callbacks: {
            afterLabel: function(context) {
              const i = context.dataIndex;
              return `Attempts: ${attempts[i]}\nSupport: ${supports[i]}`;
            }
          }
        }
      },
      scales: {
        r: {
          min: 0, max: 100,
          ticks: { color: '#7a8aa0', backdropColor: 'transparent', font: { size: 9 } },
          grid: { color: '#1f2b3a' },
          angleLines: { color: '#1f2b3a' },
          pointLabels: { color: '#d8e0ee', font: { size: 11 } }
        }
      }
    }
  });
}

// ── Render timeline chart ──
function renderTimelineChart(data) {
  const ctx = document.getElementById('timelineChart').getContext('2d');
  if (timelineChart) timelineChart.destroy();

  const timeline = data.timeline || [];
  if (timeline.length === 0) {
    document.querySelector('#timelineChart').parentNode.innerHTML = '<p style="color:var(--text-dim);padding:20px;text-align:center;">No session data yet. Upload a session from Unity to see the timeline.</p>';
    return;
  }

  const labels = timeline.map(s => {
    const d = new Date(s.received_at_utc);
    return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'});
  });
  const humanAcc = timeline.map(s => (s.human_accuracy || 0) * 100);
  const aiAcc = timeline.map(s => (s.ai_accuracy || 0) * 100);
  const attempts = timeline.map(s => s.total_attempts || 0);
  const modes = timeline.map(s => (s.session_mode || '').toLowerCase());

  timelineChart = new Chart(ctx, {
    type: 'line',
    data: {
      labels,
      datasets: [
        {
          label: 'AI Accuracy',
          data: aiAcc,
          borderColor: '#3ce07a',
          backgroundColor: 'rgba(60, 224, 122, 0.1)',
          fill: false,
          tension: 0.3,
          pointRadius: 4,
          pointHoverRadius: 6,
          borderWidth: 2
        },
        {
          label: 'Human Accuracy',
          data: humanAcc,
          borderColor: '#4a8af0',
          backgroundColor: 'rgba(74, 138, 240, 0.1)',
          fill: false,
          tension: 0.3,
          pointRadius: 4,
          pointHoverRadius: 6,
          borderWidth: 2,
          borderDash: [5, 5]
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: true,
      interaction: { mode: 'index', intersect: false },
      plugins: {
        legend: { labels: { color: '#d8e0ee', font: { size: 11 } } },
        tooltip: {
          callbacks: {
            afterBody: function(context) {
              const i = context[0].dataIndex;
              return `Attempts: ${attempts[i]} | Mode: ${modes[i]}`;
            }
          }
        }
      },
      scales: {
        x: { ticks: { color: '#7a8aa0', font: { size: 9, maxTicksLimit: 10 } }, grid: { color: '#1f2b3a' } },
        y: { min: 0, max: 100, ticks: { color: '#7a8aa0', callback: v => v + '%' }, grid: { color: '#1f2b3a' } }
      }
    }
  });
}

// ── Render confusion matrices ──
function renderConfusionMatrices(data) {
  const cats = data.category_names || ['Plastic', 'Paper / Cardboard', 'Glass', 'Organic'];
  const catShort = cats.map(c => CAT_SHORT[c] || c);

  // AI Confusion Matrix
  const aiMatrix = data.confusion_matrix_ai || [[0,0,0,0],[0,0,0,0],[0,0,0,0],[0,0,0,0]];
  const aiMax = Math.max(...aiMatrix.flat(), 1);
  let aiHtml = '<table class="confusion-table"><tr><th>True \\ Pred</th>';
  catShort.forEach(c => { aiHtml += `<th>${c}</th>`; });
  aiHtml += '</tr>';
  aiMatrix.forEach((row, i) => {
    aiHtml += `<tr><th>${catShort[i]}</th>`;
    row.forEach((val, j) => {
      const intensity = val / aiMax;
      const bgColor = i === j
        ? `rgba(60, 224, 122, ${0.15 + intensity * 0.6})`
        : `rgba(240, 90, 74, ${intensity * 0.5})`;
      const textColor = intensity > 0.6 ? '#fff' : '#d8e0ee';
      aiHtml += `<td style="background:${bgColor};color:${textColor}">${val}</td>`;
    });
    aiHtml += '</tr>';
  });
  aiHtml += '</table>';
  document.getElementById('ai-confusion-table').innerHTML = aiHtml;

  // Human Confusion Matrix
  const humanMatrix = data.confusion_matrix_human || [[0,0,0,0],[0,0,0,0],[0,0,0,0],[0,0,0,0]];
  const humanMax = Math.max(...humanMatrix.flat(), 1);
  let humanHtml = '<table class="confusion-table"><tr><th>True \\ Pred</th>';
  catShort.forEach(c => { humanHtml += `<th>${c}</th>`; });
  humanHtml += '</tr>';
  humanMatrix.forEach((row, i) => {
    humanHtml += `<tr><th>${catShort[i]}</th>`;
    row.forEach((val, j) => {
      const intensity = val / humanMax;
      const bgColor = i === j
        ? `rgba(60, 224, 122, ${0.15 + intensity * 0.6})`
        : `rgba(240, 90, 74, ${intensity * 0.5})`;
      const textColor = intensity > 0.6 ? '#fff' : '#d8e0ee';
      humanHtml += `<td style="background:${bgColor};color:${textColor}">${val}</td>`;
    });
    humanHtml += '</tr>';
  });
  humanHtml += '</table>';
  document.getElementById('human-confusion-table').innerHTML = humanHtml;
}

// ── Render confusion lists ──
function renderConfusionLists(data) {
  // AI confusions
  const aiContainer = document.getElementById('ai-confusion-list');
  const aiConfusions = data.top_ai_confusions || [];
  if (aiConfusions.length === 0) {
    aiContainer.innerHTML = '<p style="color:var(--text-dim);font-size:13px;">No AI confusions recorded.</p>';
  } else {
    aiContainer.innerHTML = aiConfusions.map(c => {
      const color = CAT_COLORS[c.true_bin] || '#4a8af0';
      return `<div class="confusion-item">
        <span><span class="color-dot" style="background:${color}"></span>${c.true_bin} <span class="arrow">→</span> ${c.predicted_bin}</span>
        <span><strong>${c.count}x</strong> &nbsp;(conf: ${(c.avg_confidence * 100).toFixed(0)}%)</span>
      </div>`;
    }).join('');
  }

  // Human confusions
  const humanContainer = document.getElementById('human-confusion-list');
  const humanConfusions = data.top_human_confusions || [];
  if (humanConfusions.length === 0) {
    humanContainer.innerHTML = '<p style="color:var(--text-dim);font-size:13px;">No human confusions recorded.</p>';
  } else {
    humanContainer.innerHTML = humanConfusions.map(c => {
      const color = CAT_COLORS[c.true_bin] || '#4a8af0';
      return `<div class="confusion-item">
        <span><span class="color-dot" style="background:${color}"></span>${c.true_bin} <span class="arrow">→</span> ${c.mistaken_bin}</span>
        <span><strong>${c.count}x</strong></span>
      </div>`;
    }).join('');
  }
}

// ── Render sessions list ──
function renderSessions(data) {
  const container = document.getElementById('session-list');
  const sessions = data.recent_sessions || [];
  if (sessions.length === 0) {
    container.innerHTML = '<p style="color:var(--text-dim);padding:20px;text-align:center;">No sessions recorded yet. Run a sorting session in Unity first.</p>';
    return;
  }

  container.innerHTML = sessions.map(s => {
    const isTraining = (s.session_mode || '').toLowerCase().includes('training');
    const humanPct = s.human_accuracy != null ? (s.human_accuracy * 100).toFixed(1) + '%' : 'N/A';
    const aiPct = s.ai_accuracy != null ? (s.ai_accuracy * 100).toFixed(1) + '%' : 'N/A';
    const date = new Date(s.received_at_utc);
    const dateStr = date.toLocaleDateString() + ' ' + date.toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'});
    return `<div class="session-item">
      <div class="left">
        <span class="session-meta">
          <span class="badge ${isTraining ? 'training' : 'quicksort'}">${s.session_mode || 'Unknown'}</span>
          <span>${s.total_attempts} attempts</span>
        </span>
        <span class="session-id">${s.session_id}</span>
      </div>
      <div style="text-align:right;">
        <div style="font-size:12px;">👤 ${humanPct} &nbsp;|&nbsp; 🤖 ${aiPct}</div>
        <div style="font-size:10px;color:var(--text-dim);">${dateStr}</div>
      </div>
    </div>`;
  }).join('');
}

// ── Main fetch and render ──
async function fetchAndRender() {
  try {
    const response = await fetch('/recyclevision/stats');
    if (!response.ok) throw new Error('Failed to fetch stats');
    const data = await response.json();

    renderStats(data);
    renderAccuracyChart(data);
    renderClassChart(data);
    renderTimelineChart(data);
    renderConfusionMatrices(data);
    renderConfusionLists(data);
    renderSessions(data);
  } catch (err) {
    document.body.innerHTML = `<div class="error"><h2>❌ Error Loading Dashboard</h2><p>${err.message}</p><p style="margin-top:12px;font-size:13px;">Make sure the backend server is running and has collected some session data.</p></div>`;
  }
}

document.addEventListener('DOMContentLoaded', fetchAndRender);

// Auto-refresh every 30 seconds
setInterval(fetchAndRender, 30000);
</script>
</body>
</html>
"""


@app.get("/dashboard", response_class=HTMLResponse)
def dashboard_page() -> str:
    return DASHBOARD_HTML


# ── Standalone Sessions Page ──

SESSIONS_PAGE_HTML = r"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>RecycleVision - Sessions</title>
<style>
  :root {
    --bg: #0b0e14;
    --surface: #131a24;
    --surface2: #1a2330;
    --accent: #f0c84a;
    --green: #3ce07a;
    --red: #f05a4a;
    --blue: #4a8af0;
    --text: #d8e0ee;
    --text-dim: #7a8aa0;
  }
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body { font-family: 'Segoe UI', system-ui, sans-serif; background: var(--bg); color: var(--text); padding: 24px; }
  h1 { font-size: 24px; margin-bottom: 20px; }
  .back-link { color: var(--accent); text-decoration: none; font-size: 14px; display: inline-block; margin-bottom: 16px; }
  .back-link:hover { text-decoration: underline; }
  table { width: 100%; border-collapse: collapse; font-size: 13px; }
  th, td { padding: 10px 14px; text-align: left; border-bottom: 1px solid #1f2b3a; }
  th { color: var(--text-dim); font-weight: 600; font-size: 11px; text-transform: uppercase; letter-spacing: 0.5px; background: var(--surface); position: sticky; top: 0; }
  tr:hover td { background: var(--surface2); }
  .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 10px; font-weight: 600; text-transform: uppercase; }
  .badge.training { background: #1a3a5c; color: #6ab0f0; }
  .badge.quicksort { background: #3a3a1a; color: #f0d060; }
  .green { color: var(--green); }
  .red { color: var(--red); }
  .dim { color: var(--text-dim); }
  .loading { text-align: center; padding: 60px; color: var(--text-dim); }
</style>
</head>
<body>
  <a href="/dashboard" class="back-link">← Back to Dashboard</a>
  <h1>📋 Session History</h1>
  <div id="loading" class="loading">Loading sessions...</div>
  <div id="content" style="display:none;">
    <div style="overflow-x:auto;">
      <table>
        <thead><tr>
          <th>Session ID</th>
          <th>Mode</th>
          <th>Date</th>
          <th>Attempts</th>
          <th>Human Accuracy</th>
          <th>AI Accuracy</th>
        </tr></thead>
        <tbody id="table-body"></tbody>
      </table>
    </div>
  </div>
<script>
async function loadSessions() {
  try {
    const r = await fetch('/recyclevision/sessions?limit=50');
    const sessions = await r.json();
    document.getElementById('loading').style.display = 'none';
    document.getElementById('content').style.display = 'block';
    const tbody = document.getElementById('table-body');
    if (sessions.length === 0) {
      tbody.innerHTML = '<tr><td colspan="6" class="dim" style="text-align:center;padding:40px;">No sessions found.</td></tr>';
      return;
    }
    tbody.innerHTML = sessions.map(s => {
      const isTraining = (s.session_mode || '').toLowerCase().includes('training');
      const date = new Date(s.received_at_utc);
      const humanPct = s.human_accuracy != null ? (s.human_accuracy * 100).toFixed(1) : 'N/A';
      const aiPct = s.ai_accuracy != null ? (s.ai_accuracy * 100).toFixed(1) : 'N/A';
      return `<tr>
        <td style="font-family:monospace;font-size:11px;">${s.session_id}</td>
        <td><span class="badge ${isTraining ? 'training' : 'quicksort'}">${s.session_mode || 'Unknown'}</span></td>
        <td>${date.toLocaleDateString()} ${date.toLocaleTimeString([],{hour:'2-digit',minute:'2-digit'})}</td>
        <td>${s.total_attempts}</td>
        <td class="${s.human_accuracy > 0.5 ? 'green' : 'red'}">${humanPct}${s.human_accuracy != null ? '%' : ''}</td>
        <td class="${s.ai_accuracy > 0.5 ? 'green' : 'red'}">${aiPct}${s.ai_accuracy != null ? '%' : ''}</td>
      </tr>`;
    }).join('');
  } catch(e) {
    document.getElementById('loading').textContent = 'Failed to load: ' + e.message;
  }
}
loadSessions();
</script>
</body>
</html>
"""


@app.get("/sessions_page", response_class=HTMLResponse)
def sessions_page() -> str:
    return SESSIONS_PAGE_HTML