using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RecycleVision
{
    public class SortingSessionStats
    {
        private readonly int[,] humanConfusions = new int[4, 4];
        private readonly int[,] aiConfusions = new int[4, 4];
        private readonly int[] humanClassTotals = new int[4];
        private readonly int[] aiClassTotals = new int[4];
        private readonly int[] humanClassCorrect = new int[4];
        private readonly int[] aiClassCorrect = new int[4];
        private readonly List<SortAttemptRecord> attempts = new List<SortAttemptRecord>();
        private readonly List<bool> humanResults = new List<bool>();

        public SessionMode Mode { get; }
        public IReadOnlyList<SortAttemptRecord> Attempts => attempts;
        public SortAttemptRecord? LastAttempt => attempts.Count > 0 ? attempts[^1] : null;
        public int TotalAttempts => attempts.Count;
        public int HumanAttemptCount { get; private set; }
        public int AiAttemptCount { get; private set; }
        public int AutoSortedCount => TotalAttempts - HumanAttemptCount;
        public int HumanCorrectCount { get; private set; }
        public int AiCorrectCount { get; private set; }

        public float HumanAccuracy => HumanAttemptCount == 0 ? 0f : HumanCorrectCount / (float)HumanAttemptCount;
        public float AiAccuracy => AiAttemptCount == 0 ? 0f : AiCorrectCount / (float)AiAttemptCount;

        public SortingSessionStats(SessionMode mode)
        {
            Mode = mode;
        }

        public void RecordAttempt(
            WasteItemDefinition item,
            RecycleCategory humanBin,
            AiSuggestion aiSuggestion,
            bool wasAutoSorted,
            string snapshotPng)
        {
            int correctIndex = (int)item.CorrectBin;
            int humanIndex = (int)humanBin;
            int aiIndex = (int)aiSuggestion.PredictedBin;
            bool humanCorrect = correctIndex == humanIndex;
            bool aiCorrect = correctIndex == aiIndex;

            aiClassTotals[correctIndex]++;
            aiConfusions[correctIndex, aiIndex]++;
            AiAttemptCount++;

            if (!wasAutoSorted)
            {
                humanClassTotals[correctIndex]++;
                humanConfusions[correctIndex, humanIndex]++;
                humanResults.Add(humanCorrect);
                HumanAttemptCount++;
            }

            if (humanCorrect && !wasAutoSorted)
            {
                HumanCorrectCount++;
                humanClassCorrect[correctIndex]++;
            }

            if (aiCorrect)
            {
                AiCorrectCount++;
                aiClassCorrect[correctIndex]++;
            }

            attempts.Add(new SortAttemptRecord(
                item.Id,
                item.DisplayName,
                item.CorrectBin,
                humanBin,
                aiSuggestion.PredictedBin,
                aiSuggestion.Confidence,
                humanCorrect,
                aiCorrect,
                wasAutoSorted,
                item.VisualShape,
                item.PrimaryColor,
                item.AccentColor,
                item.Scale,
                snapshotPng));
        }

        public float GetHumanAccuracyForClass(RecycleCategory category)
        {
            int index = (int)category;
            return humanClassTotals[index] == 0 ? 0f : humanClassCorrect[index] / (float)humanClassTotals[index];
        }

        public float GetAiAccuracyForClass(RecycleCategory category)
        {
            int index = (int)category;
            return aiClassTotals[index] == 0 ? 0f : aiClassCorrect[index] / (float)aiClassTotals[index];
        }

        public float GetImprovementDelta()
        {
            if (humanResults.Count < 4)
            {
                return 0f;
            }

            int midpoint = humanResults.Count / 2;
            float firstHalf = AverageRange(0, midpoint);
            float secondHalf = AverageRange(midpoint, humanResults.Count - midpoint);
            return secondHalf - firstHalf;
        }

        public bool TryGetTopHumanConfusion(out RecycleCategory expected, out RecycleCategory mistakenAs, out int count)
        {
            return TryGetTopConfusion(humanConfusions, out expected, out mistakenAs, out count);
        }

        public bool TryGetTopAiConfusion(out RecycleCategory expected, out RecycleCategory mistakenAs, out int count)
        {
            return TryGetTopConfusion(aiConfusions, out expected, out mistakenAs, out count);
        }

        public string BuildCompactSummary()
        {
            string humanLabel = HumanAttemptCount == 0
                ? "Human n/a (0 manual)"
                : $"Human {HumanCorrectCount}/{HumanAttemptCount} ({HumanAccuracy * 100f:0}%)";
            string aiLabel = AiAttemptCount == 0
                ? "AI n/a"
                : $"AI {AiCorrectCount}/{AiAttemptCount} ({AiAccuracy * 100f:0}%)";
            return $"{humanLabel}   |   {aiLabel}";
        }

        public string BuildReport(IReadOnlyList<float> priorHistory)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"{Mode.ToShortLabel()} report");
            builder.AppendLine();
            builder.AppendLine(HumanAttemptCount == 0
                ? "Human accuracy: n/a (0 manual attempts)"
                : $"Human accuracy: {HumanAccuracy * 100f:0}%");
            builder.AppendLine($"AI accuracy: {AiAccuracy * 100f:0}%");
            builder.AppendLine($"Items sorted: {TotalAttempts}");
            builder.AppendLine($"Manual attempts: {HumanAttemptCount}");
            builder.AppendLine($"Auto-sorted items: {AutoSortedCount}");

            float improvement = GetImprovementDelta() * 100f;
            builder.AppendLine(improvement >= 0f
                ? $"In-session improvement: +{improvement:0}%"
                : $"In-session improvement: {improvement:0}%");

            if (TryGetTopHumanConfusion(out RecycleCategory expected, out RecycleCategory mistakenAs, out int count))
            {
                builder.AppendLine($"Most common human mix-up: {expected.ToDisplayName()} -> {mistakenAs.ToDisplayName()} ({count}x)");
            }
            else
            {
                builder.AppendLine("Most common human mix-up: none this session");
            }

            if (TryGetTopAiConfusion(out RecycleCategory aiExpected, out RecycleCategory aiMistakenAs, out int aiCount))
            {
                builder.AppendLine($"Most common AI mix-up: {aiExpected.ToDisplayName()} -> {aiMistakenAs.ToDisplayName()} ({aiCount}x)");
            }
            else
            {
                builder.AppendLine("Most common AI mix-up: none this session");
            }

            if (priorHistory != null && priorHistory.Count > 0)
            {
                builder.AppendLine($"Session history: {FormatHistory(priorHistory)}");
            }

            return builder.ToString().TrimEnd();
        }

        private static string FormatHistory(IReadOnlyList<float> priorHistory)
        {
            StringBuilder builder = new StringBuilder();

            for (int index = 0; index < priorHistory.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append("   ");
                }

                builder.Append($"S{index + 1}:{priorHistory[index] * 100f:0}%");
            }

            return builder.ToString();
        }

        private float AverageRange(int start, int count)
        {
            if (count <= 0)
            {
                return 0f;
            }

            int total = 0;

            for (int index = start; index < start + count; index++)
            {
                if (humanResults[index])
                {
                    total++;
                }
            }

            return total / (float)count;
        }

        private static bool TryGetTopConfusion(
            int[,] confusionMatrix,
            out RecycleCategory expected,
            out RecycleCategory mistakenAs,
            out int count)
        {
            expected = RecycleCategory.Plastic;
            mistakenAs = RecycleCategory.Plastic;
            count = 0;

            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (row == column)
                    {
                        continue;
                    }

                    if (confusionMatrix[row, column] > count)
                    {
                        count = confusionMatrix[row, column];
                        expected = (RecycleCategory)row;
                        mistakenAs = (RecycleCategory)column;
                    }
                }
            }

            return count > 0;
        }
    }
}
