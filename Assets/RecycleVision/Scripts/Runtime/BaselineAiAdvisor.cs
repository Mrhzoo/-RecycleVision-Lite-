using System;
using UnityEngine;

namespace RecycleVision
{
    public class BaselineAiAdvisor : MonoBehaviour
    {
        [Range(0.05f, 0.35f)] public float confidenceVariance = 0.08f;

        public AiSuggestion BuildSuggestion(WasteItemDefinition item, SessionMode mode, int attemptIndex)
        {
            int seed = item.Id.GetHashCode() ^ (attemptIndex * 397) ^ ((int)mode * 911);
            System.Random random = new System.Random(seed);
            bool misclassify = random.NextDouble() < item.ConfusionChance;
            RecycleCategory predicted = misclassify ? item.CommonMistakeBin : item.CorrectBin;

            float variance = ((float)random.NextDouble() * 2f - 1f) * confidenceVariance;
            float confidence = Mathf.Clamp01(item.BaseAiConfidence + variance - (misclassify ? 0.14f : 0f));
            confidence = Mathf.Clamp(confidence, 0.35f, 0.98f);

            string explanation = mode == SessionMode.Training
                ? BuildTrainingExplanation(item, predicted, misclassify)
                : $"Pattern matched most strongly with {predicted.ToDisplayName()}.";

            return new AiSuggestion(predicted, confidence, "AI Assist", explanation, true);
        }

        private static string BuildTrainingExplanation(
            WasteItemDefinition item,
            RecycleCategory predicted,
            bool misclassify)
        {
            if (!misclassify)
            {
                return item.TrainingHint;
            }

            return $"This item can visually resemble {predicted.ToDisplayName()}, so it is a good example of an edge case.";
        }
    }
}
