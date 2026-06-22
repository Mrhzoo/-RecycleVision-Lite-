#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.InferenceEngine;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;

namespace RecycleVision.Editor
{
    [InitializeOnLoad]
    public static class RecycleVisionMlAgentAutoSetup
    {
        private const string ModelAssetPath = "Assets/RecycleVision/Models/RecycleVisionSorter.onnx";
        private const string BehaviorName = "RecycleVisionSorter";
        private const string UseInferencePrefKey = "RecycleVision.MlAgent.UseInference";
        private static bool isApplying;

        static RecycleVisionMlAgentAutoSetup()
        {
            EditorApplication.projectChanged += TryAssignModel;
            EditorApplication.hierarchyChanged += TryAssignModel;
            EditorApplication.delayCall += TryAssignModel;
        }

        [MenuItem("Tools/RecycleVision/ML Agents/Use Inference (Model)")]
        private static void UseInference()
        {
            EditorPrefs.SetBool(UseInferencePrefKey, true);
            TryAssignModel();
        }

        [MenuItem("Tools/RecycleVision/ML Agents/Use Trainer (Default)")]
        private static void UseTrainer()
        {
            EditorPrefs.SetBool(UseInferencePrefKey, false);
            TryAssignModel();
        }

        private static void TryAssignModel()
        {
            if (isApplying)
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            isApplying = true;

            try
            {
                TryAssignModelInternal();
            }
            finally
            {
                isApplying = false;
            }
        }

        private static void TryAssignModelInternal()
        {

            RecycleVisionMlAgent agent = Object.FindFirstObjectByType<RecycleVisionMlAgent>();
            if (agent == null)
            {
                return;
            }

            BehaviorParameters[] behaviors = agent.GetComponents<BehaviorParameters>();
            if (behaviors.Length == 0)
            {
                behaviors = new[] { agent.gameObject.AddComponent<BehaviorParameters>() };
            }

            bool changed = false;
            bool useInference = EditorPrefs.GetBool(UseInferencePrefKey, true);
            ModelAsset model = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelAssetPath);
            BehaviorParameters firstBehavior = behaviors[0];

            foreach (BehaviorParameters behavior in behaviors)
            {
                if (behavior == null)
                {
                    continue;
                }

                if (behavior.BehaviorName != BehaviorName)
                {
                    behavior.BehaviorName = BehaviorName;
                    changed = true;
                }

                if (behavior.BrainParameters.VectorObservationSize != RecycleVisionMlAgent.VectorObservationSize)
                {
                    behavior.BrainParameters.VectorObservationSize = RecycleVisionMlAgent.VectorObservationSize;
                    changed = true;
                }

                if (behavior.BrainParameters.NumStackedVectorObservations != 1)
                {
                    behavior.BrainParameters.NumStackedVectorObservations = 1;
                    changed = true;
                }

                if (!behavior.UseChildSensors)
                {
                    behavior.UseChildSensors = true;
                    changed = true;
                }

                ActionSpec actionSpec = behavior.BrainParameters.ActionSpec;
                bool actionSpecNeedsRepair = actionSpec.BranchSizes == null
                    || actionSpec.BranchSizes.Length != 1
                    || actionSpec.BranchSizes[0] != RecycleVisionMlAgent.DiscreteActionCount
                    || actionSpec.NumContinuousActions != 0;

                if (actionSpecNeedsRepair)
                {
                    behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(RecycleVisionMlAgent.DiscreteActionCount);
                    changed = true;
                }

                if (useInference && model != null && behavior.Model != model)
                {
                    behavior.Model = model;
                    changed = true;
                }
                else if (!useInference && behavior.Model != null)
                {
                    behavior.Model = null;
                    changed = true;
                }

                BehaviorType targetType = useInference && model != null ? BehaviorType.InferenceOnly : BehaviorType.Default;
                if (behavior.BehaviorType != targetType)
                {
                    behavior.BehaviorType = targetType;
                    changed = true;
                }
            }

            if (changed)
            {
                foreach (BehaviorParameters behavior in behaviors)
                {
                    if (behavior != null)
                    {
                        EditorUtility.SetDirty(behavior);
                    }
                }

                if (agent.gameObject.scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(agent.gameObject.scene);
                }
            }

            for (int index = 1; index < behaviors.Length; index++)
            {
                if (behaviors[index] == null)
                {
                    continue;
                }

                Object.DestroyImmediate(behaviors[index]);
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(firstBehavior);
                if (agent.gameObject.scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(agent.gameObject.scene);
                }
            }
        }
    }
}
#endif
