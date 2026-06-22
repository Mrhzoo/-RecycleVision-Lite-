using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RecycleVision
{
    public class RecycleVisionHud : MonoBehaviour
    {
        public Text titleText;
        public Text modeText;
        public Text itemText;
        public Text hintText;
        public Text aiSuggestionText;
        public Text summaryText;
        public Text logStatusText;
        public Text feedbackText;
        public Text timerText;
        public Image timerRingImage;
        public Text reportText;
        public GameObject reportPanel;
        public Button trainingButton;
        public Button quickSortButton;
        public Button restartButton;
        public Button closeReportButton;

        private SortingStationManager manager;

        public void Initialize(SortingStationManager stationManager)
        {
            manager = stationManager;

            if (titleText != null)
            {
                titleText.text = "RecycleVision Lite";
            }

            BindButton(trainingButton, manager.StartTrainingMode);
            BindButton(quickSortButton, manager.StartQuickSortMode);
            BindButton(restartButton, manager.RestartCurrentMode);
            BindButton(closeReportButton, manager.CloseReportPanel);
            HideReport();
            ClearFeedback();
            SetupTimerRing();
        }

        public void SetMode(SessionMode mode, int currentAttemptCount, int targetAttemptCount)
        {
            if (modeText == null)
            {
                return;
            }

            string suffix = targetAttemptCount > 0
                ? $"   |   {currentAttemptCount}/{targetAttemptCount}"
                : string.Empty;

            modeText.text = $"{mode.ToShortLabel()}{suffix}";
        }

        public void SetCurrentItem(WasteItemDefinition definition, bool showHint)
        {
            if (itemText != null)
            {
                itemText.text = $"Current item: {definition.DisplayName}";
            }

            if (hintText != null)
            {
                hintText.text = showHint
                    ? definition.TrainingHint
                    : "Sort quickly, but keep accuracy higher than speed.";
            }
        }

        public void SetAwaitingAi()
        {
            if (aiSuggestionText != null)
            {
                aiSuggestionText.text = "AI suggestion: analyzing camera view...";
            }
        }

        public void SetAiSuggestion(AiSuggestion suggestion)
        {
            if (aiSuggestionText == null)
            {
                return;
            }

            string confidenceLabel = suggestion.IsEstimatedConfidence ? "est. " : string.Empty;
            aiSuggestionText.text =
                $"AI suggestion: {suggestion.PredictedBin.ToDisplayName()} ({confidenceLabel}{suggestion.Confidence * 100f:0}%)\n{suggestion.Explanation}";
        }

        public void SetSummary(SortingSessionStats stats)
        {
            if (summaryText != null)
            {
                summaryText.text = stats.BuildCompactSummary();
            }
        }

        public void SetLoggingEnabled(bool isEnabled, bool includeFeatures, bool includeFrames)
        {
            if (logStatusText == null)
            {
                return;
            }

            string status = isEnabled ? "On" : "Off";
            string features = includeFeatures ? "features" : "no features";
            string frames = includeFrames ? "frames" : "no frames";
            logStatusText.text = $"Logging: {status} ({features}, {frames}) - press L to toggle";
        }

        public void SetFeedback(string message, Color color)
        {
            if (feedbackText == null)
            {
                return;
            }

            feedbackText.text = message;
            feedbackText.color = color;
        }

        public void ClearFeedback()
        {
            if (feedbackText != null)
            {
                feedbackText.text = string.Empty;
            }
        }

        public void SetTimer(float remainingSeconds, float totalSeconds, bool isVisible)
        {
            if (timerText == null)
            {
                return;
            }

            timerText.enabled = isVisible;

            if (timerRingImage != null)
            {
                timerRingImage.enabled = isVisible;
                if (isVisible)
                {
                    float fill = totalSeconds > 0f ? Mathf.Clamp01(remainingSeconds / totalSeconds) : 0f;
                    timerRingImage.fillAmount = fill;
                }
            }

            if (isVisible)
            {
                timerText.text = $"Time left: {remainingSeconds:0.0}s";
            }
        }

        private void SetupTimerRing()
        {
            if (timerRingImage == null)
            {
                return;
            }

            if (timerRingImage.sprite == null)
            {
                timerRingImage.sprite = CreateRingSprite(128, 12);
            }

            timerRingImage.type = Image.Type.Filled;
            timerRingImage.fillMethod = Image.FillMethod.Radial360;
            timerRingImage.fillOrigin = (int)Image.Origin360.Top;
            timerRingImage.fillClockwise = false;
            timerRingImage.fillAmount = 1f;
            timerRingImage.raycastTarget = false;
        }

        private static Sprite CreateRingSprite(int size, int thickness)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float outerRadius = size * 0.5f;
            float innerRadius = outerRadius - thickness;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(center, new Vector2(x, y));
                    bool inRing = dist <= outerRadius && dist >= innerRadius;
                    texture.SetPixel(x, y, inRing ? Color.white : new Color(0f, 0f, 0f, 0f));
                }
            }

            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;

            Rect rect = new Rect(0f, 0f, size, size);
            return Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), size);
        }

        public void ShowReport(string reportBody)
        {
            if (reportText != null)
            {
                reportText.text = reportBody;
            }

            if (reportPanel != null)
            {
                reportPanel.SetActive(true);
            }
        }

        public void HideReport()
        {
            if (reportPanel != null)
            {
                reportPanel.SetActive(false);
            }
        }

        private static void BindButton(Button button, UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }
    }
}
