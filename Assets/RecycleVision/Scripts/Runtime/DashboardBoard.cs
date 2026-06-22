using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RecycleVision
{
    public class DashboardBoard : MonoBehaviour
    {
        public TextMesh titleText;
        public TextMesh modeText;
        public TextMesh humanAccuracyText;
        public TextMesh aiAccuracyText;
        public TextMesh confusionText;
        public TextMesh historyText;
        public TextMesh suggestionText;
        public Transform[] classBars;
        public TextMesh[] classBarLabels;

        // Neon glow border
        private Renderer boardSurfaceRenderer;
        private Material boardMaterial;

        // Sparkline for accuracy trend
        private LineRenderer sparklineRenderer;
        private List<float> accuracyTrend = new List<float>();

        // Bar animation targets
        private float[] barTargetScales;
        private float[] barCurrentScales;

        private Vector3[] baseBarScales;
        private Vector3[] baseBarPositions;
        private bool hasCachedBars;

        private const float BarAnimSpeed = 4f;
        private const int MaxTrendPoints = 10;
        private float glowTimer = 0f;

        private void Awake()
        {
            hasCachedBars = false;
            boardSurfaceRenderer = GetComponentInChildren<Renderer>();
            if (boardSurfaceRenderer != null)
                boardMaterial = boardSurfaceRenderer.material;

            // Create sparkline
            GameObject sparklineGo = new GameObject("AccuracySparkline");
            sparklineGo.transform.SetParent(transform, false);
            sparklineGo.transform.localPosition = new Vector3(-0.3f, -0.52f, -0.02f);
            sparklineRenderer = sparklineGo.AddComponent<LineRenderer>();
            sparklineRenderer.positionCount = 0;
            sparklineRenderer.startWidth = 0.015f;
            sparklineRenderer.endWidth = 0.015f;
            sparklineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            sparklineRenderer.startColor = new Color(0.32f, 0.84f, 0.62f);
            sparklineRenderer.endColor = new Color(0.22f, 0.62f, 0.97f);
            sparklineRenderer.enabled = false;
        }

        private void Update()
        {
            // Animate neon glow pulse on board
            if (boardMaterial != null && boardMaterial.HasProperty("_EmissionColor"))
            {
                glowTimer += Time.deltaTime * 0.8f;
                float pulse = 0.6f + Mathf.Sin(glowTimer) * 0.4f;
                Color glow = new Color(0.05f, 0.4f, 0.7f) * pulse;
                boardMaterial.SetColor("_EmissionColor", glow);
            }

            // Animate bars toward target scale
            if (barCurrentScales != null && hasCachedBars)
            {
                for (int i = 0; i < classBars.Length && i < barCurrentScales.Length; i++)
                {
                    barCurrentScales[i] = Mathf.Lerp(barCurrentScales[i], barTargetScales[i], Time.deltaTime * BarAnimSpeed);

                    float scaleY = barCurrentScales[i];
                    classBars[i].localScale = new Vector3(baseBarScales[i].x, scaleY, baseBarScales[i].z);
                    classBars[i].localPosition = new Vector3(
                        baseBarPositions[i].x,
                        baseBarPositions[i].y - (baseBarScales[i].y * 0.5f) + (scaleY * 0.5f),
                        baseBarPositions[i].z);
                }
            }
        }

        public void RecordTrendPoint(float accuracy)
        {
            accuracyTrend.Add(accuracy);
            if (accuracyTrend.Count > MaxTrendPoints)
                accuracyTrend.RemoveAt(0);

            UpdateSparkline();
        }

        private void UpdateSparkline()
        {
            if (sparklineRenderer == null || accuracyTrend.Count < 2)
            {
                if (sparklineRenderer != null) sparklineRenderer.enabled = false;
                return;
            }

            sparklineRenderer.enabled = true;
            sparklineRenderer.positionCount = accuracyTrend.Count;

            float spacing = 1.0f / Mathf.Max(accuracyTrend.Count - 1, 1);
            for (int i = 0; i < accuracyTrend.Count; i++)
            {
                float x = -0.5f + i * spacing;
                float y = accuracyTrend[i] * 0.3f;
                sparklineRenderer.SetPosition(i, new Vector3(x, y, 0));
            }
        }

        public void Refresh(
            SortingSessionStats stats,
            IReadOnlyList<float> sessionHistory,
            SessionMode mode,
            AiSuggestion? activeSuggestion = null)
        {
            CacheBarTransforms();

            if (modeText != null)
                modeText.text = mode.ToShortLabel();

            if (humanAccuracyText != null)
            {
                humanAccuracyText.text = stats.HumanAttemptCount == 0
                    ? "Human: n/a"
                    : $"Human: {stats.HumanAccuracy * 100f:0}%";
            }

            if (aiAccuracyText != null)
                aiAccuracyText.text = $"AI: {stats.AiAccuracy * 100f:0}%";

            if (confusionText != null)
                confusionText.text = BuildConfusionLine(stats);

            if (historyText != null)
            {
                historyText.text = sessionHistory != null && sessionHistory.Count > 0
                    ? $"Hist: {FormatHistory(sessionHistory)}"
                    : "Hist: none";
            }

            if (suggestionText != null)
            {
                suggestionText.text = activeSuggestion.HasValue
                    ? $"Sugg: {activeSuggestion.Value.PredictedBin.ToDisplayName()} ({activeSuggestion.Value.Confidence * 100f:0}%)"
                    : "Sugg: waiting...";
            }

            if (classBars == null || baseBarScales == null)
                return;

            // Update bars with smooth animation targets
            bool hasHuman = stats.HumanAttemptCount > 0;
            bool hasAi = stats.AiAttemptCount > 0;

            for (int i = 0; i < classBars.Length; i++)
            {
                RecycleCategory cat = (RecycleCategory)i;
                float accuracy = 0f;
                if (hasHuman)
                    accuracy = stats.GetHumanAccuracyForClass(cat);
                else if (hasAi)
                    accuracy = stats.GetAiAccuracyForClass(cat);

                float minScale = baseBarScales[i].y * 0.1f;
                float maxScale = baseBarScales[i].y;
                barTargetScales[i] = Mathf.Lerp(minScale, maxScale, accuracy);

                if (classBarLabels != null && i < classBarLabels.Length && classBarLabels[i] != null)
                {
                    bool showValue = hasHuman || hasAi;
                    classBarLabels[i].text = showValue ? $"{accuracy * 100f:0}%" : "--";
                }
            }
        }

        public void UpdateSuggestion(AiSuggestion suggestion)
        {
            if (suggestionText != null)
                suggestionText.text = $"Sugg: {suggestion.PredictedBin.ToDisplayName()} ({suggestion.Confidence * 100f:0}%)";
        }

        private void CacheBarTransforms()
        {
            if (classBars == null || classBars.Length == 0 || hasCachedBars) return;

            baseBarScales = new Vector3[classBars.Length];
            baseBarPositions = new Vector3[classBars.Length];
            barTargetScales = new float[classBars.Length];
            barCurrentScales = new float[classBars.Length];

            for (int i = 0; i < classBars.Length; i++)
            {
                baseBarScales[i] = classBars[i].localScale;
                baseBarPositions[i] = classBars[i].localPosition;
                barCurrentScales[i] = baseBarScales[i].y * 0.1f;
                barTargetScales[i] = baseBarScales[i].y * 0.1f;
            }
            hasCachedBars = true;
        }

        private static string BuildConfusionLine(SortingSessionStats stats)
        {
            StringBuilder sb = new StringBuilder();
            if (stats.TryGetTopHumanConfusion(out RecycleCategory exp, out RecycleCategory mis, out int cnt))
                sb.Append($"Mix: {exp.ToDisplayName()}\u2192{mis.ToDisplayName()} ({cnt}x)");
            else
                sb.Append("Mix: none");

            if (stats.TryGetTopAiConfusion(out RecycleCategory aiE, out RecycleCategory aiM, out int aiC))
                sb.Append($"\nAI: {aiE.ToDisplayName()}\u2192{aiM.ToDisplayName()} ({aiC}x)");
            else
                sb.Append("\nAI mix: none");

            return sb.ToString();
        }

        private static string FormatHistory(IReadOnlyList<float> h)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < h.Count; i++)
            {
                if (i > 0) sb.Append(" ");
                sb.Append($"S{i + 1}:{h[i] * 100f:0}%");
            }
            return sb.ToString();
        }
    }
}