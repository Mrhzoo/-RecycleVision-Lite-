using System.Collections;
using UnityEngine;

namespace RecycleVision
{
    public class RecycleVisionSortingAgent : MonoBehaviour
    {
        public SortingStationManager manager;
        public BaselineAiAdvisor baselineAi;
        public float simulatedDecisionDelay = 0.12f;
        public bool describeAsCameraAnalysis = true;

        private Coroutine pendingRoutine;

        public bool HasModelAssigned => false;

        public void RequestSuggestion(WasteItem item, SessionMode mode, int attemptIndex)
        {
            if (item == null)
            {
                return;
            }

            if (pendingRoutine != null)
            {
                StopCoroutine(pendingRoutine);
            }
            pendingRoutine = StartCoroutine(GenerateSuggestion(item, mode, attemptIndex));
        }

        private IEnumerator GenerateSuggestion(WasteItem item, SessionMode mode, int attemptIndex)
        {
            if (simulatedDecisionDelay > 0f)
            {
                yield return new WaitForSeconds(simulatedDecisionDelay);
            }

            AiSuggestion suggestion = baselineAi != null
                ? baselineAi.BuildSuggestion(item.Definition, mode, attemptIndex)
                : new AiSuggestion(item.Definition.CorrectBin, 0.6f, "AI Assist", item.Definition.TrainingHint, true);

            if (describeAsCameraAnalysis)
            {
                suggestion.SourceLabel = "AI Camera Assist";
                suggestion.Explanation = $"Camera view favors {suggestion.PredictedBin.ToDisplayName()}. {suggestion.Explanation}";
            }

            manager?.ReceiveAgentSuggestion(item, suggestion);
            pendingRoutine = null;
        }
    }
}
