using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace RecycleVision
{
    [RequireComponent(typeof(BehaviorParameters))]
    public class RecycleVisionMlAgent : Agent
    {
        public const int VectorObservationSize = 13;
        public const int DiscreteActionCount = 4;

        public SortingStationManager manager;
        public float correctReward = 2f;
        public float incorrectReward = -0.5f;
        public float stepPenalty = -0.001f;
        public bool endEpisodeAfterDecision = true;
        public bool requestDecisionOnNewItem = true;
        public bool useSortingRuleActionMask = true;

        private BehaviorParameters behaviorParameters;
        private WasteItem currentItem;

        public bool HasModelAssigned
        {
            get
            {
                EnsureBehaviorParameters();

                foreach (BehaviorParameters parameters in GetComponents<BehaviorParameters>())
                {
                    if (parameters != null && parameters.Model != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool ShouldControlDecisions => HasModelAssigned || Academy.Instance.IsCommunicatorOn;

        protected override void Awake()
        {
            base.Awake();
            EnsureCameraSensor();
            EnsureBehaviorParameters();
            ConfigureBehaviorParameters();
        }

        protected override void OnEnable()
        {
            EnsureCameraSensor();
            base.OnEnable();
        }

        public override void Initialize()
        {
            EnsureBehaviorParameters();
            ConfigureBehaviorParameters();
        }

        public void SetCurrentItem(WasteItem item)
        {
            currentItem = item;

            if (requestDecisionOnNewItem)
            {
                RequestDecision();
            }
        }

        public void ClearCurrentItem()
        {
            currentItem = null;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            WasteItemDefinition definition = currentItem != null ? currentItem.Definition : null;
            int shapeIndex = definition != null ? (int)definition.VisualShape : -1;

            for (int index = 0; index < 6; index++)
            {
                sensor.AddObservation(shapeIndex == index ? 1f : 0f);
            }

            Color primary = definition != null ? definition.PrimaryColor : Color.black;
            Color accent = definition != null ? definition.AccentColor : Color.black;
            sensor.AddObservation(primary.r);
            sensor.AddObservation(primary.g);
            sensor.AddObservation(primary.b);
            sensor.AddObservation(accent.r);
            sensor.AddObservation(accent.g);
            sensor.AddObservation(accent.b);
            sensor.AddObservation(definition != null ? Mathf.Clamp01(definition.Scale / 1.5f) : 0f);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (currentItem == null)
            {
                AddReward(stepPenalty);

                if (endEpisodeAfterDecision)
                {
                    EndEpisode();
                }

                return;
            }

            int action = actions.DiscreteActions[0];
            RecycleCategory predicted = (RecycleCategory)Mathf.Clamp(action, 0, DiscreteActionCount - 1);
            bool isCorrect = predicted == currentItem.Definition.CorrectBin;

            AddReward(isCorrect ? correctReward : incorrectReward);
            AddReward(stepPenalty);

            AiSuggestion suggestion = new AiSuggestion(
                predicted,
                isCorrect ? 0.9f : 0.45f,
                "ML Agent",
                BuildExplanation(predicted, isCorrect),
                false);

            manager?.ReceiveMlAgentDecision(currentItem, suggestion);

            if (endEpisodeAfterDecision)
            {
                EndEpisode();
            }
        }

        public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
        {
            if (!useSortingRuleActionMask || currentItem == null)
            {
                return;
            }

            bool[] allowedActions = BuildAllowedActions(currentItem.Definition);
            int branchSize = GetConfiguredDiscreteBranchSize();

            if (branchSize < allowedActions.Length)
            {
                return;
            }

            for (int action = 0; action < allowedActions.Length; action++)
            {
                if (!allowedActions[action])
                {
                    actionMask.SetActionEnabled(0, action, false);
                }
            }
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            int predicted = currentItem != null ? (int)currentItem.Definition.CorrectBin : 0;
            ActionSegment<int> discreteActions = actionsOut.DiscreteActions;
            discreteActions[0] = predicted;
        }

        private string BuildExplanation(RecycleCategory predicted, bool isCorrect)
        {
            if (currentItem == null)
            {
                return "No item available.";
            }

            if (isCorrect)
            {
                return $"Model matched {predicted.ToDisplayName()} for {currentItem.Definition.DisplayName}.";
            }

            return $"Model confused {currentItem.Definition.DisplayName} with {predicted.ToDisplayName()}.";
        }

        private void EnsureBehaviorParameters()
        {
            if (behaviorParameters == null)
            {
                BehaviorParameters[] parameters = GetComponents<BehaviorParameters>();

                if (parameters.Length == 0)
                {
                    behaviorParameters = gameObject.AddComponent<BehaviorParameters>();
                    return;
                }

                behaviorParameters = parameters[0];
            }
        }

        private void ConfigureBehaviorParameters()
        {
            BehaviorParameters[] parameters = GetComponents<BehaviorParameters>();

            if (parameters.Length == 0)
            {
                behaviorParameters = gameObject.AddComponent<BehaviorParameters>();
                parameters = new[] { behaviorParameters };
            }

            BehaviorParameters modelSource = null;

            foreach (BehaviorParameters parametersComponent in parameters)
            {
                if (parametersComponent != null && parametersComponent.Model != null)
                {
                    modelSource = parametersComponent;
                    break;
                }
            }

            behaviorParameters = parameters[0];

            foreach (BehaviorParameters parametersComponent in parameters)
            {
                if (parametersComponent == null)
                {
                    continue;
                }

                parametersComponent.BehaviorName = "RecycleVisionSorter";
                parametersComponent.UseChildSensors = true;
                parametersComponent.BrainParameters.VectorObservationSize = VectorObservationSize;
                parametersComponent.BrainParameters.NumStackedVectorObservations = 1;
                parametersComponent.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(DiscreteActionCount);

                if (modelSource != null)
                {
                    parametersComponent.Model = modelSource.Model;
                    parametersComponent.BehaviorType = modelSource.BehaviorType;
                }
            }
        }

        private void EnsureCameraSensor()
        {
            CameraSensorComponent sensor = GetComponent<CameraSensorComponent>();
            Camera sensorCamera = GetComponentInChildren<Camera>(true);

            if (sensorCamera == null)
            {
                GameObject cameraGo = new GameObject("AgentCamera");
                cameraGo.transform.SetParent(transform, false);
                cameraGo.transform.localPosition = Vector3.zero;
                cameraGo.transform.localRotation = Quaternion.identity;
                sensorCamera = cameraGo.AddComponent<Camera>();
            }

            sensorCamera.fieldOfView = 42f;
            sensorCamera.nearClipPlane = 0.1f;
            sensorCamera.farClipPlane = 20f;
            sensorCamera.clearFlags = CameraClearFlags.SolidColor;
            sensorCamera.backgroundColor = new Color(0.08f, 0.1f, 0.13f);

            if (sensor == null)
            {
                sensor = gameObject.AddComponent<CameraSensorComponent>();
            }

            sensor.Camera = sensorCamera;
            sensor.SensorName = "RecycleVisionCamera";
            sensor.Width = 84;
            sensor.Height = 84;
            sensor.Grayscale = false;
            sensor.ObservationStacks = 1;
            sensor.RuntimeCameraEnable = true;
        }

        private int GetConfiguredDiscreteBranchSize()
        {
            EnsureBehaviorParameters();

            ActionSpec actionSpec = behaviorParameters != null
                ? behaviorParameters.BrainParameters.ActionSpec
                : ActionSpec.MakeDiscrete(DiscreteActionCount);

            if (actionSpec.BranchSizes == null || actionSpec.BranchSizes.Length == 0)
            {
                return 0;
            }

            return actionSpec.BranchSizes[0];
        }

        private static bool[] BuildAllowedActions(WasteItemDefinition definition)
        {
            bool[] allowed = new bool[4];

            if (definition == null)
            {
                AllowAll(allowed);
                return allowed;
            }

            switch (definition.VisualShape)
            {
                case WasteVisualShape.Bottle:
                    allowed[(int)RecycleCategory.Plastic] = true;
                    allowed[(int)RecycleCategory.Glass] = true;
                    break;
                case WasteVisualShape.Box:
                    allowed[(int)RecycleCategory.PaperCardboard] = true;
                    break;
                case WasteVisualShape.Jar:
                    allowed[(int)RecycleCategory.Glass] = true;
                    break;
                case WasteVisualShape.FlatPack:
                    allowed[(int)RecycleCategory.Plastic] = true;
                    allowed[(int)RecycleCategory.PaperCardboard] = true;
                    break;
                case WasteVisualShape.Scrap:
                    allowed[(int)RecycleCategory.Organic] = true;
                    break;
                case WasteVisualShape.Cup:
                    allowed[(int)RecycleCategory.Plastic] = true;
                    break;
                default:
                    AllowAll(allowed);
                    break;
            }

            bool anyAllowed = false;

            for (int index = 0; index < allowed.Length; index++)
            {
                anyAllowed |= allowed[index];
            }

            if (!anyAllowed)
            {
                allowed[(int)definition.CorrectBin] = true;
            }

            return allowed;
        }

        private static void AllowAll(bool[] allowed)
        {
            for (int index = 0; index < allowed.Length; index++)
            {
                allowed[index] = true;
            }
        }
    }
}
