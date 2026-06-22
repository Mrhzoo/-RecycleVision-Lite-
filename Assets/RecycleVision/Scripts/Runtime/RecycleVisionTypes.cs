using System;
using UnityEngine;

namespace RecycleVision
{
    public enum RecycleCategory
    {
        Plastic = 0,
        PaperCardboard = 1,
        Glass = 2,
        Organic = 3
    }

    public enum SessionMode
    {
        Training = 0,
        QuickSort = 1
    }

    public enum WasteVisualShape
    {
        Bottle = 0,
        Box = 1,
        Jar = 2,
        FlatPack = 3,
        Scrap = 4,
        Cup = 5
    }

    [Serializable]
    public struct AiSuggestion
    {
        public RecycleCategory PredictedBin;
        public float Confidence;
        public string SourceLabel;
        public string Explanation;
        public bool IsEstimatedConfidence;

        public AiSuggestion(
            RecycleCategory predictedBin,
            float confidence,
            string sourceLabel,
            string explanation,
            bool isEstimatedConfidence)
        {
            PredictedBin = predictedBin;
            Confidence = confidence;
            SourceLabel = sourceLabel;
            Explanation = explanation;
            IsEstimatedConfidence = isEstimatedConfidence;
        }
    }

    [Serializable]
    public struct SortAttemptRecord
    {
        public string ItemId;
        public string DisplayName;
        public RecycleCategory CorrectBin;
        public RecycleCategory HumanBin;
        public RecycleCategory AiBin;
        public float AiConfidence;
        public bool HumanCorrect;
        public bool AiCorrect;
        public bool WasAutoSorted;
        public WasteVisualShape VisualShape;
        public Color PrimaryColor;
        public Color AccentColor;
        public float Scale;
        public string SnapshotPng;

        public SortAttemptRecord(
            string itemId,
            string displayName,
            RecycleCategory correctBin,
            RecycleCategory humanBin,
            RecycleCategory aiBin,
            float aiConfidence,
            bool humanCorrect,
            bool aiCorrect,
            bool wasAutoSorted,
            WasteVisualShape visualShape,
            Color primaryColor,
            Color accentColor,
            float scale,
            string snapshotPng)
        {
            ItemId = itemId;
            DisplayName = displayName;
            CorrectBin = correctBin;
            HumanBin = humanBin;
            AiBin = aiBin;
            AiConfidence = aiConfidence;
            HumanCorrect = humanCorrect;
            AiCorrect = aiCorrect;
            WasAutoSorted = wasAutoSorted;
            VisualShape = visualShape;
            PrimaryColor = primaryColor;
            AccentColor = accentColor;
            Scale = scale;
            SnapshotPng = snapshotPng;
        }
    }

    public static class RecycleVisionExtensions
    {
        public static string ToDisplayName(this RecycleCategory category)
        {
            return category switch
            {
                RecycleCategory.Plastic => "Plastic",
                RecycleCategory.PaperCardboard => "Paper / Cardboard",
                RecycleCategory.Glass => "Glass",
                RecycleCategory.Organic => "Organic",
                _ => category.ToString()
            };
        }

        public static string ToShortLabel(this SessionMode mode)
        {
            return mode == SessionMode.Training ? "Training Mode" : "Quick-Sort Mode";
        }
    }
}
