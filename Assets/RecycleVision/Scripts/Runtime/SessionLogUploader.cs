using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace RecycleVision
{
    public class SessionLogUploader : MonoBehaviour
    {
        [Serializable]
        private class AttemptPayload
        {
            public int attempt_index;
            public string item_id;
            public string display_name;
            public string true_bin;
            public string selected_bin;
            public string predicted_bin;
            public string correct_bin;
            public string human_bin;
            public string ai_bin;
            public float ai_confidence;
            public bool human_correct;
            public bool ai_correct;
            public bool was_auto_sorted;
            public string visual_shape;
            public int visual_shape_index;
            public float[] primary_color;
            public float[] accent_color;
            public float scale;
            public float[] feature_vector;
            public string snapshot_png;
        }

        [Serializable]
        private class SessionPayload
        {
            public string schema_version;
            public string project_name;
            public string session_id;
            public string session_mode;
            public string started_at_utc;
            public string ended_at_utc;
            public string unity_version;
            public int total_attempts;
            public int human_attempts;
            public int auto_sorted_attempts;
            public float human_accuracy;
            public float ai_accuracy;
            public List<AttemptPayload> attempts;
        }

        public bool uploadEnabled = true;
        public bool includeItemFeatures = true;
        public bool includeCameraFrame;
        public int snapshotWidth = 128;
        public int snapshotHeight = 128;
        public Camera snapshotCamera;
        public string endpointUrl = "http://127.0.0.1:8000/recyclevision/session";

        private readonly List<SortAttemptRecord> pendingAttempts = new List<SortAttemptRecord>();
        private string sessionId;
        private string sessionStartedAtUtc;
        private string lastUploadStatus = "Not uploaded yet.";

        public string LastUploadStatus => lastUploadStatus;

        public void ResetSessionQueue()
        {
            pendingAttempts.Clear();
            sessionId = Guid.NewGuid().ToString("N");
            sessionStartedAtUtc = DateTime.UtcNow.ToString("o");
            lastUploadStatus = "Session queue reset.";
        }

        public void QueueAttempt(SortAttemptRecord attempt)
        {
            pendingAttempts.Add(attempt);
        }

        public bool ToggleUploadEnabled()
        {
            uploadEnabled = !uploadEnabled;
            return uploadEnabled;
        }

        public string CaptureSnapshot()
        {
            if (!includeCameraFrame || !uploadEnabled)
            {
                return null;
            }

            Camera camera = snapshotCamera;

            if (camera == null)
            {
                RecycleVisionMlAgent agent = FindFirstObjectByType<RecycleVisionMlAgent>();
                if (agent != null)
                {
                    camera = agent.GetComponentInChildren<Camera>(true);
                }
            }

            if (camera == null)
            {
                return null;
            }

            RenderTexture previous = camera.targetTexture;
            RenderTexture renderTexture = RenderTexture.GetTemporary(snapshotWidth, snapshotHeight, 16, RenderTextureFormat.ARGB32);
            RenderTexture active = RenderTexture.active;

            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();

                Texture2D snapshot = new Texture2D(snapshotWidth, snapshotHeight, TextureFormat.RGB24, false);
                snapshot.ReadPixels(new Rect(0f, 0f, snapshotWidth, snapshotHeight), 0, 0);
                snapshot.Apply();
                byte[] png = snapshot.EncodeToPNG();
                Destroy(snapshot);
                return Convert.ToBase64String(png);
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        public void UploadSession(SortingSessionStats stats)
        {
            if (!uploadEnabled || string.IsNullOrWhiteSpace(endpointUrl) || stats == null)
            {
                lastUploadStatus = "Upload skipped.";
                return;
            }

            StartCoroutine(UploadRoutine(stats));
        }

        private IEnumerator UploadRoutine(SortingSessionStats stats)
        {
            SessionPayload payload = new SessionPayload
            {
                schema_version = "1.0",
                project_name = "RecycleVision Lite",
                session_id = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId,
                session_mode = stats.Mode.ToShortLabel(),
                started_at_utc = string.IsNullOrWhiteSpace(sessionStartedAtUtc) ? DateTime.UtcNow.ToString("o") : sessionStartedAtUtc,
                ended_at_utc = DateTime.UtcNow.ToString("o"),
                unity_version = Application.unityVersion,
                total_attempts = stats.TotalAttempts,
                human_attempts = stats.HumanAttemptCount,
                auto_sorted_attempts = stats.AutoSortedCount,
                human_accuracy = stats.HumanAccuracy,
                ai_accuracy = stats.AiAccuracy,
                attempts = BuildPayloadAttempts()
            };

            string json = JsonUtility.ToJson(payload);
            byte[] rawBody = Encoding.UTF8.GetBytes(json);

            using UnityWebRequest request = new UnityWebRequest(endpointUrl, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(rawBody);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 5;
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                lastUploadStatus = $"Uploaded {payload.attempts.Count} attempts to backend.";
                Debug.Log($"RecycleVision session uploaded: {request.downloadHandler.text}");
            }
            else
            {
                lastUploadStatus = $"Upload failed: {request.error}";
                Debug.LogWarning($"RecycleVision backend upload failed ({request.responseCode}): {request.error}");
            }
        }

        private List<AttemptPayload> BuildPayloadAttempts()
        {
            List<AttemptPayload> attempts = new List<AttemptPayload>(pendingAttempts.Count);

            for (int index = 0; index < pendingAttempts.Count; index++)
            {
                SortAttemptRecord attempt = pendingAttempts[index];
                attempts.Add(new AttemptPayload
                {
                    attempt_index = index + 1,
                    item_id = attempt.ItemId,
                    display_name = attempt.DisplayName,
                    true_bin = attempt.CorrectBin.ToString(),
                    selected_bin = attempt.HumanBin.ToString(),
                    predicted_bin = attempt.AiBin.ToString(),
                    correct_bin = attempt.CorrectBin.ToDisplayName(),
                    human_bin = attempt.HumanBin.ToDisplayName(),
                    ai_bin = attempt.AiBin.ToDisplayName(),
                    ai_confidence = attempt.AiConfidence,
                    human_correct = attempt.HumanCorrect,
                    ai_correct = attempt.AiCorrect,
                    was_auto_sorted = attempt.WasAutoSorted,
                    visual_shape = includeItemFeatures ? attempt.VisualShape.ToString() : null,
                    visual_shape_index = includeItemFeatures ? (int)attempt.VisualShape : -1,
                    primary_color = includeItemFeatures ? ToRgb(attempt.PrimaryColor) : null,
                    accent_color = includeItemFeatures ? ToRgb(attempt.AccentColor) : null,
                    scale = includeItemFeatures ? attempt.Scale : 0f,
                    feature_vector = includeItemFeatures ? BuildFeatureVector(attempt) : null,
                    snapshot_png = includeCameraFrame ? attempt.SnapshotPng : null
                });
            }

            return attempts;
        }

        private static float[] ToRgb(Color color)
        {
            return new[] { color.r, color.g, color.b };
        }

        private static float[] BuildFeatureVector(SortAttemptRecord attempt)
        {
            float[] vector = new float[RecycleVisionMlAgent.VectorObservationSize];
            int shapeIndex = (int)attempt.VisualShape;

            for (int index = 0; index < 6 && index < vector.Length; index++)
            {
                vector[index] = shapeIndex == index ? 1f : 0f;
            }

            vector[6] = attempt.PrimaryColor.r;
            vector[7] = attempt.PrimaryColor.g;
            vector[8] = attempt.PrimaryColor.b;
            vector[9] = attempt.AccentColor.r;
            vector[10] = attempt.AccentColor.g;
            vector[11] = attempt.AccentColor.b;
            vector[12] = Mathf.Clamp01(attempt.Scale / 1.5f);
            return vector;
        }
    }
}
