using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;

namespace RecycleVision
{
    public class SortingStationManager : MonoBehaviour
    {
        private const string HistoryKey = "RecycleVisionLite.SessionHistory";
        private const int MaxStoredHistory = 6;
        private const int TrainerQueueSize = 32;

        public SessionMode startingMode = SessionMode.Training;
        public bool autoStartOnPlay = true;
        public int trainingItemsPerSession = 24;
        public int quickSortItemsPerSession = 24;
        public float quickSortDuration = 90f;
        public float interItemDelay = 0.85f;
        public Transform spawnPoint;
        public Transform itemContainer;
        public SortingBin[] bins;
        public RecycleVisionHud hud;
        public DashboardBoard dashboard;
        public BaselineAiAdvisor baselineAi;
        public RecycleVisionSortingAgent sortingAgent;
        public RecycleVisionMlAgent mlAgent;
        public StationCameraController stationCamera;
        public RobotSorterArmController robotArm;
        public SessionLogUploader logUploader;
        public SortEffects sortEffects;
        public ConveyorBeltAnimator conveyorBelt;
        public Color itemGlowColor = new Color(0.22f, 0.84f, 0.96f);
        public bool enableDifficultyProgression = true;
        public float minInterItemDelay = 0.45f;
        public float maxBeltSpeedMultiplier = 1.6f;
        public bool useItemPooling = true;
        public int maxPoolPerItem = 6;
        public bool robotAssistsInQuickSort = true;
        public List<WasteItemDefinition> itemDefinitions = new List<WasteItemDefinition>();

        private readonly List<WasteItemDefinition> sessionQueue = new List<WasteItemDefinition>();
        private readonly List<float> accuracyHistory = new List<float>();

        private WasteItem currentItem;
        private SortingSessionStats sessionStats;
        private AiSuggestion currentSuggestion;
        private bool hasSuggestion;
        private bool sessionActive;
        private float remainingTime;
        private int targetAttemptCount;
        private int sessionSeed;
        private SessionMode currentMode;
        private Coroutine advanceRoutine;
        private bool currentItemAutoSorted;
        private float currentInterItemDelay;

        private readonly Dictionary<string, Stack<WasteItem>> itemPool = new Dictionary<string, Stack<WasteItem>>();
        private Transform itemPoolRoot;

        // ✅ Cumulative stats across all sessions
        private SortingSessionStats cumulativeStats;
        private bool cumulativeStatsInitialized;

        public void Awake()
        {
            if (NeedsRuntimeBootstrap())
            {
                BootstrapIntoPlayableScene();
            }

            if (itemDefinitions == null || itemDefinitions.Count == 0)
            {
                RestoreDefaultDefinitions();
            }

            if (stationCamera == null)
            {
                stationCamera = FindFirstObjectByType<StationCameraController>();
            }

            EnsureMlAgentRig();

            EnsureItemPoolRoot();

            if (sortEffects == null)
            {
                sortEffects = GetComponent<SortEffects>();
                if (sortEffects == null)
                {
                    sortEffects = gameObject.AddComponent<SortEffects>();
                }
            }

            if (conveyorBelt == null)
            {
                conveyorBelt = FindFirstObjectByType<ConveyorBeltAnimator>();
            }

            if (robotArm != null)
            {
                robotArm.manager = this;
            }

            if (mlAgent != null)
            {
                mlAgent.manager = this;
            }

            if (logUploader != null && !logUploader.uploadEnabled)
            {
                logUploader.uploadEnabled = true;
            }

            LoadHistory();
            hud?.Initialize(this);
            hud?.SetLoggingEnabled(
                logUploader != null && logUploader.uploadEnabled,
                logUploader != null && logUploader.includeItemFeatures,
                logUploader != null && logUploader.includeCameraFrame);

            if (bins != null)
            {
                foreach (SortingBin bin in bins)
                {
                    if (bin == null)
                    {
                        continue;
                    }

                    bin.manager = this;
                    bin.SetSuggested(false);
                }
            }

            UpdateCameraInteractionState();
        }

        private void Start()
        {
            cumulativeStatsInitialized = false;

            if (autoStartOnPlay)
            {
                BeginSession(startingMode);
            }
            else
            {
                sessionStats = new SortingSessionStats(startingMode);
                hud?.SetMode(startingMode, 0, trainingItemsPerSession);
                hud?.SetSummary(sessionStats);
                dashboard?.Refresh(sessionStats, accuracyHistory, startingMode);
            }
        }

        private void Update()
        {
            if (logUploader != null && Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
            {
                logUploader.ToggleUploadEnabled();
                hud?.SetLoggingEnabled(logUploader.uploadEnabled, logUploader.includeItemFeatures, logUploader.includeCameraFrame);
            }

            if (!sessionActive)
            {
                return;
            }

            if (currentMode == SessionMode.QuickSort)
            {
                remainingTime = Mathf.Max(0f, remainingTime - Time.deltaTime);
                hud?.SetTimer(remainingTime, quickSortDuration, true);
                if (enableDifficultyProgression)
                {
                    RefreshDifficultyProgress();
                }

                if (remainingTime <= 0f)
                {
                    EndSession();
                    return;
                }
            }
            else
            {
                hud?.SetTimer(0f, quickSortDuration, false);
            }
        }

        [ContextMenu("Restore Default Item Definitions")]
        public void RestoreDefaultDefinitions()
        {
            itemDefinitions = WasteItemDefinition.CreateDefaults();
        }

        public void StartTrainingMode()
        {
            BeginSession(SessionMode.Training);
        }

        public void StartQuickSortMode()
        {
            BeginSession(SessionMode.QuickSort);
        }

        public void RestartCurrentMode()
        {
            BeginSession(currentMode);
        }

        public void CloseReportPanel()
        {
            hud?.HideReport();
        }

        public void HandleItemDroppedInBin(SortingBin bin, WasteItem item)
        {
            if (!sessionActive || bin == null || item == null || item != currentItem || item.IsResolved)
            {
                return;
            }

            if (!hasSuggestion)
            {
                AiSuggestion fallbackSuggestion = BuildFallbackSuggestion(item.Definition);
                ApplyAiSuggestion(fallbackSuggestion);
            }

            string snapshotPng = logUploader != null ? logUploader.CaptureSnapshot() : null;
            sessionStats.RecordAttempt(item.Definition, bin.BinType, currentSuggestion, currentItemAutoSorted, snapshotPng);

            // ✅ Also record in cumulative stats
            if (cumulativeStats != null)
            {
                cumulativeStats.RecordAttempt(item.Definition, bin.BinType, currentSuggestion, currentItemAutoSorted, snapshotPng);
            }

            SortAttemptRecord? lastAttempt = sessionStats.LastAttempt;

            if (lastAttempt.HasValue)
            {
                logUploader?.QueueAttempt(lastAttempt.Value);
            }

            bool wasCorrect = bin.BinType == item.Definition.CorrectBin;
            item.SnapTo(bin.DropAnchor);
            bin.FlashResult(wasCorrect);

            if (sortEffects != null)
            {
                Vector3 effectPos = bin.DropAnchor != null ? bin.DropAnchor.position : item.transform.position;
                if (wasCorrect)
                {
                    sortEffects.PlayCorrectEffect(effectPos);
                }
                else
                {
                    sortEffects.PlayWrongEffect(effectPos);
                }
            }

            item.SetGlow(false, itemGlowColor);

            string feedback = BuildFeedbackMessage(item.Definition, bin.BinType, wasCorrect);
            hud?.SetFeedback(feedback, wasCorrect ? new Color(0.33f, 0.9f, 0.48f) : new Color(1f, 0.48f, 0.39f));

            if (!wasCorrect)
            {
                SortingBin correctBin = FindBin(item.Definition.CorrectBin);
                if (correctBin != null)
                {
                    string hintMessage = currentMode == SessionMode.Training
                        ? $"Correct: {item.Definition.CorrectBin.ToDisplayName()}\n{item.Definition.TrainingHint}"
                        : $"Correct: {item.Definition.CorrectBin.ToDisplayName()}";
                    correctBin.ShowGhostHint(hintMessage, 1.6f);
                }
            }
            hud?.SetSummary(cumulativeStats ?? sessionStats);
            hud?.SetMode(currentMode, sessionStats.TotalAttempts, IsMlTrainerConnected() ? 0 : targetAttemptCount);

            ClearHighlights();

            // Refresh dashboard with session stats
            dashboard?.Refresh(sessionStats, accuracyHistory, currentMode, currentSuggestion);
            RefreshDifficultyProgress();

            hasSuggestion = false;
            currentItemAutoSorted = false;
            currentItem?.ClearAiOverlay();
            mlAgent?.ClearCurrentItem();

            bool reachedItemGoal = !IsMlTrainerConnected() && sessionStats.TotalAttempts >= targetAttemptCount;

            if (reachedItemGoal)
            {
                EndSession();
            }
            else
            {
                if (advanceRoutine != null)
                {
                    StopCoroutine(advanceRoutine);
                }

                advanceRoutine = StartCoroutine(AdvanceAfterDelay(item.gameObject));
            }
        }

        public void ReceiveAgentSuggestion(WasteItem item, AiSuggestion suggestion)
        {
            if (!sessionActive || currentItem == null || item != currentItem)
            {
                return;
            }

            ApplyAiSuggestion(suggestion);
        }

        public void ReceiveMlAgentDecision(WasteItem item, AiSuggestion suggestion)
        {
            if (!sessionActive || currentItem == null || item != currentItem)
            {
                return;
            }

            bool trainerConnected = IsMlTrainerConnected();
            ApplyAiSuggestion(suggestion, !trainerConnected);

            if (!trainerConnected)
            {
                return;
            }

            currentItemAutoSorted = true;
            SortingBin predictedBin = FindBin(suggestion.PredictedBin);

            if (predictedBin != null)
            {
                HandleItemDroppedInBin(predictedBin, item);
            }
        }

        public void ResetCurrentItemPosition()
        {
            if (currentItem == null || spawnPoint == null)
            {
                return;
            }

            if (currentItem.Body != null)
            {
                currentItem.Body.isKinematic = true;
                currentItem.Body.useGravity = false;
            }

            currentItem.transform.SetParent(itemContainer, true);
            currentItem.PlaceAt(spawnPoint.position, spawnPoint.rotation);
        }

        public SortingBin FindBin(RecycleCategory category)
        {
            if (bins == null)
            {
                return null;
            }

            foreach (SortingBin bin in bins)
            {
                if (bin != null && bin.BinType == category)
                {
                    return bin;
                }
            }

            return null;
        }

        private void BeginSession(SessionMode mode)
        {
            if (spawnPoint == null || itemContainer == null)
            {
                Debug.LogWarning("RecycleVision setup is missing spawn references.");
                return;
            }

            if (advanceRoutine != null)
            {
                StopCoroutine(advanceRoutine);
                advanceRoutine = null;
            }

            // ✅ Initialize cumulative stats once (never resets)
            if (!cumulativeStatsInitialized)
            {
                cumulativeStats = new SortingSessionStats(mode);
                cumulativeStatsInitialized = true;
            }

            currentMode = mode;
            sessionSeed++;
            sessionStats = new SortingSessionStats(mode);
            sessionActive = true;
            hasSuggestion = false;
            mlAgent?.ClearCurrentItem();
            targetAttemptCount = mode == SessionMode.Training ? trainingItemsPerSession : quickSortItemsPerSession;
            remainingTime = mode == SessionMode.QuickSort ? quickSortDuration : 0f;
            currentInterItemDelay = interItemDelay;
            sortEffects?.ResetStreak();

            logUploader?.ResetSessionQueue();
            hud?.HideReport();
            hud?.ClearFeedback();
            hud?.SetMode(currentMode, 0, targetAttemptCount);
            hud?.SetSummary(cumulativeStats ?? sessionStats);
            hud?.SetTimer(remainingTime, quickSortDuration, mode == SessionMode.QuickSort);
            UpdateCameraInteractionState();
            RefreshDifficultyProgress();

            ClearSpawnedItems();
            BuildSessionQueue(GetQueueBuildCount());

            // Refresh dashboard with session stats
            dashboard?.Refresh(sessionStats, accuracyHistory, currentMode);
            SpawnNextItem();
        }

        private void SpawnNextItem()
        {
            if (!sessionActive)
            {
                return;
            }

            if (currentItem != null)
            {
                ReturnItemToPool(currentItem);
                currentItem = null;
            }

            if (sessionQueue.Count == 0)
            {
                BuildSessionQueue(GetQueueBuildCount());
            }

            WasteItemDefinition definition = sessionQueue[0];
            sessionQueue.RemoveAt(0);

            currentItem = AcquireItem(definition);
            currentItem.PlaceAt(spawnPoint.position, spawnPoint.rotation);
            currentItem.ClearAiOverlay();
            currentItemAutoSorted = false;
            currentItem.SetGlow(true, itemGlowColor);

            hud?.SetCurrentItem(definition, currentMode == SessionMode.Training);
            hud?.SetMode(currentMode, sessionStats.TotalAttempts, IsMlTrainerConnected() ? 0 : targetAttemptCount);
            hud?.ClearFeedback();
            ClearHighlights();

            if (mlAgent != null && mlAgent.isActiveAndEnabled && mlAgent.ShouldControlDecisions)
            {
                hasSuggestion = false;
                hud?.SetAwaitingAi();
                mlAgent.SetCurrentItem(currentItem);
            }
            else if (sortingAgent != null && sortingAgent.isActiveAndEnabled)
            {
                hasSuggestion = false;
                hud?.SetAwaitingAi();
                sortingAgent.RequestSuggestion(currentItem, currentMode, sessionStats.TotalAttempts);
            }
            else
            {
                ApplyAiSuggestion(BuildFallbackSuggestion(definition));
            }
        }

        private void ApplyAiSuggestion(AiSuggestion suggestion, bool allowRobot = true)
        {
            currentSuggestion = suggestion;
            hasSuggestion = true;
            hud?.SetAiSuggestion(suggestion);
            dashboard?.UpdateSuggestion(suggestion);
            HighlightSuggestedBin(suggestion.PredictedBin);
            currentItem?.SetAiOverlay(suggestion);

            if (!allowRobot || !IsRobotHandlingCurrentMode() || currentItem == null || robotArm == null || robotArm.IsBusy)
            {
                return;
            }

            SortingBin targetBin = FindBin(suggestion.PredictedBin);

            if (targetBin != null && robotArm.TrySort(currentItem, targetBin))
            {
                currentItemAutoSorted = true;
                hud?.SetFeedback("Robot arm executing AI sort.", new Color(0.89f, 0.77f, 0.31f));
            }
            else if (IsRobotHandlingCurrentMode() && stationCamera != null)
            {
                stationCamera.allowItemGrab = true;
                hud?.SetFeedback("Robot arm could not take control. Manual sorting is enabled.", new Color(0.97f, 0.74f, 0.24f));
            }
        }

        private AiSuggestion BuildFallbackSuggestion(WasteItemDefinition definition)
        {
            if (baselineAi != null)
            {
                return baselineAi.BuildSuggestion(definition, currentMode, sessionStats.TotalAttempts);
            }

            return new AiSuggestion(
                definition.CorrectBin,
                0.6f,
                "Fallback AI",
                definition.TrainingHint,
                true);
        }

        private string BuildFeedbackMessage(WasteItemDefinition definition, RecycleCategory selectedBin, bool wasCorrect)
        {
            string correctBin = definition.CorrectBin.ToDisplayName();

            if (wasCorrect)
            {
                return currentMode == SessionMode.Training
                    ? $"Correct. {definition.DisplayName} belongs in {correctBin}. {definition.TrainingHint}"
                    : $"Correct. {definition.DisplayName} -> {correctBin}.";
            }

            return $"Not quite. {definition.DisplayName} belongs in {correctBin}, not {selectedBin.ToDisplayName()}.";
        }

        private void HighlightSuggestedBin(RecycleCategory category)
        {
            if (bins == null)
            {
                return;
            }

            foreach (SortingBin bin in bins)
            {
                if (bin != null)
                {
                    bin.SetSuggested(bin.BinType == category);
                }
            }
        }

        private void ClearHighlights()
        {
            if (bins == null)
            {
                return;
            }

            foreach (SortingBin bin in bins)
            {
                if (bin != null)
                {
                    bin.SetSuggested(false);
                }
            }
        }

        private void BuildSessionQueue(int count)
        {
            sessionQueue.Clear();

            if (itemDefinitions == null || itemDefinitions.Count == 0)
            {
                RestoreDefaultDefinitions();
            }

            System.Random random = new System.Random(sessionSeed * 733 + DateTime.Now.Millisecond);
            List<WasteItemDefinition> pool = new List<WasteItemDefinition>(itemDefinitions);

            if (pool.Count == 0 || count <= 0)
            {
                return;
            }

            Shuffle(pool, random);
            int poolIndex = 0;

            for (int index = 0; index < count; index++)
            {
                if (poolIndex >= pool.Count)
                {
                    Shuffle(pool, random);
                    poolIndex = 0;
                }

                sessionQueue.Add(pool[poolIndex]);
                poolIndex++;
            }
        }

        private int GetQueueBuildCount()
        {
            if (IsMlTrainerConnected())
            {
                return Mathf.Max(TrainerQueueSize, itemDefinitions != null ? itemDefinitions.Count : 0);
            }

            return targetAttemptCount;
        }

        private static void Shuffle<T>(IList<T> list, System.Random random)
        {
            for (int index = list.Count - 1; index > 0; index--)
            {
                int swapIndex = random.Next(index + 1);
                (list[index], list[swapIndex]) = (list[swapIndex], list[index]);
            }
        }

        private IEnumerator AdvanceAfterDelay(GameObject resolvedItem)
        {
            if (IsMlTrainerConnected())
            {
                yield return null;
            }
            else if (IsRobotHandlingCurrentMode() && robotArm != null)
            {
                while (robotArm.IsBusy)
                {
                    yield return null;
                }
            }
            else
            {
                float delay = currentInterItemDelay > 0f ? currentInterItemDelay : interItemDelay;
                yield return new WaitForSeconds(delay);
            }

            if (resolvedItem != null)
            {
                WasteItem resolvedWaste = resolvedItem.GetComponent<WasteItem>();
                if (resolvedWaste != null)
                {
                    ReturnItemToPool(resolvedWaste);
                }
                else
                {
                    SafeDestroy(resolvedItem);
                }
            }

            mlAgent?.ClearCurrentItem();
            currentItem = null;

            if (sessionActive)
            {
                SpawnNextItem();
            }
        }

        private void EndSession()
        {
            if (!sessionActive)
            {
                return;
            }

            sessionActive = false;
            ClearHighlights();
            mlAgent?.ClearCurrentItem();

            if (advanceRoutine != null)
            {
                StopCoroutine(advanceRoutine);
                advanceRoutine = null;
            }

            if (currentItem != null && !currentItem.IsResolved)
            {
                ReturnItemToPool(currentItem);
                currentItem = null;
            }

            accuracyHistory.Add(sessionStats.HumanAccuracy);

            dashboard?.RecordTrendPoint(sessionStats.HumanAccuracy);

            while (accuracyHistory.Count > MaxStoredHistory)
            {
                accuracyHistory.RemoveAt(0);
            }

            SaveHistory();
            string report = sessionStats.BuildReport(accuracyHistory);
            hud?.ShowReport(report);

            // After session ends, dashboard still shows cumulative stats
            dashboard?.Refresh(sessionStats, accuracyHistory, currentMode);
            logUploader?.UploadSession(sessionStats);
            sortEffects?.ResetStreak();
            UpdateCameraInteractionState();
        }

        private void ClearSpawnedItems()
        {
            ReturnItemsUnder(itemContainer);

            if (bins == null)
            {
                return;
            }

            foreach (SortingBin bin in bins)
            {
                if (bin != null && bin.DropAnchor != null)
                {
                    ReturnItemsUnder(bin.DropAnchor);
                }
            }
        }

        private static void DestroyChildren(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                SafeDestroy(parent.GetChild(index).gameObject);
            }
        }

        private void ReturnItemsUnder(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            WasteItem[] items = parent.GetComponentsInChildren<WasteItem>(true);
            foreach (WasteItem item in items)
            {
                ReturnItemToPool(item);
            }
        }

        private void EnsureItemPoolRoot()
        {
            if (itemPoolRoot != null)
            {
                return;
            }

            GameObject poolRoot = new GameObject("ItemPool");
            poolRoot.transform.SetParent(transform, false);
            itemPoolRoot = poolRoot.transform;
        }

        private WasteItem AcquireItem(WasteItemDefinition definition)
        {
            if (!useItemPooling)
            {
                return WasteItemFactory.Create(itemContainer, definition);
            }

            EnsureItemPoolRoot();
            string key = GetPoolKey(definition);

            if (itemPool.TryGetValue(key, out Stack<WasteItem> stack) && stack.Count > 0)
            {
                WasteItem item = stack.Pop();
                item.gameObject.SetActive(true);
                item.transform.SetParent(itemContainer, false);
                return item;
            }

            return WasteItemFactory.Create(itemContainer, definition);
        }

        private void ReturnItemToPool(WasteItem item)
        {
            if (item == null)
            {
                return;
            }

            if (!useItemPooling)
            {
                SafeDestroy(item.gameObject);
                return;
            }

            EnsureItemPoolRoot();
            string key = GetPoolKey(item.Definition);

            if (!itemPool.TryGetValue(key, out Stack<WasteItem> stack))
            {
                stack = new Stack<WasteItem>();
                itemPool[key] = stack;
            }

            if (stack.Count >= maxPoolPerItem)
            {
                SafeDestroy(item.gameObject);
                return;
            }

            item.SetHeld(false);
            item.ClearAiOverlay();
            item.SetGlow(false, itemGlowColor);
            item.ResetForPool(itemPoolRoot);
            stack.Push(item);
        }

        private static string GetPoolKey(WasteItemDefinition definition)
        {
            return definition != null && !string.IsNullOrWhiteSpace(definition.Id)
                ? definition.Id
                : "unknown";
        }

        private void LoadHistory()
        {
            accuracyHistory.Clear();
            string raw = PlayerPrefs.GetString(HistoryKey, string.Empty);

            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            string[] values = raw.Split('|', StringSplitOptions.RemoveEmptyEntries);

            foreach (string value in values)
            {
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    accuracyHistory.Add(parsed);
                }
            }
        }

        private void SaveHistory()
        {
            string[] values = new string[accuracyHistory.Count];

            for (int index = 0; index < accuracyHistory.Count; index++)
            {
                values[index] = accuracyHistory[index].ToString(CultureInfo.InvariantCulture);
            }

            PlayerPrefs.SetString(HistoryKey, string.Join("|", values));
            PlayerPrefs.Save();
        }

        private static void SafeDestroy(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private bool NeedsRuntimeBootstrap()
        {
            return spawnPoint == null
                || itemContainer == null
                || bins == null
                || bins.Length == 0
                || hud == null
                || dashboard == null;
        }

        private void BootstrapIntoPlayableScene()
        {
            Camera bootstrapCamera = GetComponent<Camera>();

            if (bootstrapCamera == null)
            {
                bootstrapCamera = Camera.main;
            }

            if (bootstrapCamera == null)
            {
                Debug.LogWarning("RecycleVision bootstrap requires a camera in the scene.");
                return;
            }
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Material floorMaterial = CreateRuntimeMaterial(new Color(0.12f, 0.14f, 0.17f), 0.35f, 0.02f, Color.black);
            Material wallMaterial = CreateRuntimeMaterial(new Color(0.07f, 0.11f, 0.18f), 0.58f, 0.02f, new Color(0.05f, 0.12f, 0.2f) * 0.25f);
            Material tableMaterial = CreateRuntimeMaterial(new Color(0.18f, 0.2f, 0.24f), 0.52f, 0.05f, Color.black);
            Material trayMaterial = CreateRuntimeMaterial(new Color(0.2f, 0.24f, 0.31f), 0.72f, 0.04f, new Color(0.08f, 0.17f, 0.34f) * 0.3f);
            Material trimMaterial = CreateRuntimeMaterial(new Color(0.94f, 0.78f, 0.26f), 0.65f, 0.14f, new Color(0.94f, 0.78f, 0.26f) * 0.22f);
            Material shadowMaterial = CreateRuntimeMaterial(new Color(0.08f, 0.08f, 0.1f), 0.1f, 0f, Color.black);
            Material boardMaterial = CreateRuntimeMaterial(new Color(0.14f, 0.17f, 0.22f), 0.66f, 0.06f, new Color(0.05f, 0.11f, 0.19f) * 0.22f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.28f, 0.34f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.16f, 0.19f, 0.24f);
            RenderSettings.ambientGroundColor = new Color(0.06f, 0.07f, 0.08f);

            GameObject root = new GameObject("RecycleVisionRuntime");
            GameObject environmentRoot = new GameObject("Environment");
            environmentRoot.transform.SetParent(root.transform, false);
            GameObject stationRoot = new GameObject("SortingStation");
            stationRoot.transform.SetParent(root.transform, false);
            GameObject systemsRoot = new GameObject("Systems");
            systemsRoot.transform.SetParent(root.transform, false);

            ConfigureLightRig(root.transform);
            CreateEnvironment(environmentRoot.transform, floorMaterial, wallMaterial, tableMaterial, trimMaterial);

            // Glass overlay on which the dashboard will be mounted.
            Transform observationGlass = CreateObservationGlass(environmentRoot.transform, trimMaterial);

            CreateStationSurface(stationRoot.transform, tableMaterial, trayMaterial, shadowMaterial, trimMaterial);

            itemContainer = new GameObject("ItemContainer").transform;
            itemContainer.SetParent(stationRoot.transform, false);
            spawnPoint = new GameObject("SpawnPoint").transform;
            spawnPoint.SetParent(stationRoot.transform, false);
            spawnPoint.localPosition = new Vector3(0f, 1.24f, -0.1f);

            bins = CreateBins(stationRoot.transform, font, shadowMaterial);

            dashboard = CreateDashboard(stationRoot.transform, font, boardMaterial);

            // Mount dashboard onto the observation glass
            if (observationGlass != null && dashboard != null)
            {
                dashboard.transform.SetParent(observationGlass, true);
                dashboard.transform.localPosition = new Vector3(0f, 0.08f, -0.01f);
                dashboard.transform.localRotation = Quaternion.identity;
                dashboard.transform.localScale = Vector3.one * 0.92f;
            }

            hud = CreateHud(font);
            EnsureEventSystem();
            ConfigureCameraRig(bootstrapCamera);

            baselineAi = systemsRoot.AddComponent<BaselineAiAdvisor>();
            logUploader = systemsRoot.AddComponent<SessionLogUploader>();
            sortingAgent = CreateAgentRig(systemsRoot.transform, baselineAi);
            sortingAgent.manager = this;
            mlAgent = CreateMlAgentRig(systemsRoot.transform);
            mlAgent.manager = this;
        }

        private void ConfigureCameraRig(Camera bootstrapCamera)
        {
            if (bootstrapCamera == null)
            {
                return;
            }

            if (bootstrapCamera.GetComponent<AudioListener>() == null)
            {
                bootstrapCamera.gameObject.AddComponent<AudioListener>();
            }

            GameObject rig = new GameObject("CameraRig");
            rig.transform.SetPositionAndRotation(new Vector3(0f, 2.05f, -4.75f), Quaternion.Euler(14f, 0f, 0f));
            bootstrapCamera.transform.SetParent(rig.transform, true);
            bootstrapCamera.transform.localPosition = Vector3.zero;
            bootstrapCamera.transform.localRotation = Quaternion.identity;
            bootstrapCamera.gameObject.tag = "MainCamera";

            Transform holdAnchor = new GameObject("HoldAnchor").transform;
            holdAnchor.SetParent(rig.transform, false);
            holdAnchor.localPosition = new Vector3(0f, -0.1f, 2.1f);

            StationCameraController controller = rig.GetComponent<StationCameraController>();

            if (controller == null)
            {
                controller = rig.AddComponent<StationCameraController>();
            }

            controller.playerCamera = bootstrapCamera;
            controller.holdAnchor = holdAnchor;
            controller.grabbableMask = ~0;
            stationCamera = controller;
        }

        private void UpdateCameraInteractionState()
        {
            if (stationCamera == null)
            {
                return;
            }

            stationCamera.allowItemGrab = !sessionActive || !IsRobotHandlingCurrentMode();
        }

        private void RefreshDifficultyProgress()
        {
            if (!enableDifficultyProgression)
            {
                currentInterItemDelay = interItemDelay;
                if (conveyorBelt != null)
                {
                    conveyorBelt.SetSpeedMultiplier(1f);
                }
                return;
            }

            float progress = 0f;

            if (targetAttemptCount > 0)
            {
                progress = Mathf.Clamp01(sessionStats.TotalAttempts / (float)targetAttemptCount);
            }

            if (currentMode == SessionMode.QuickSort && quickSortDuration > 0f)
            {
                float timeProgress = Mathf.Clamp01(1f - (remainingTime / quickSortDuration));
                progress = Mathf.Max(progress, timeProgress);
            }

            currentInterItemDelay = Mathf.Lerp(interItemDelay, minInterItemDelay, progress);

            if (conveyorBelt != null)
            {
                float speedMultiplier = Mathf.Lerp(1f, maxBeltSpeedMultiplier, progress);
                conveyorBelt.SetSpeedMultiplier(speedMultiplier);
            }
        }

        private bool IsRobotHandlingCurrentMode()
        {
            return robotAssistsInQuickSort
                && currentMode == SessionMode.QuickSort
                && robotArm != null
                && robotArm.isActiveAndEnabled;
        }

        private bool IsMlTrainerConnected()
        {
            return Academy.Instance != null && Academy.Instance.IsCommunicatorOn;
        }

        private void EnsureMlAgentRig()
        {
            if (mlAgent == null)
            {
                mlAgent = FindFirstObjectByType<RecycleVisionMlAgent>();
            }

            if (mlAgent == null)
            {
                mlAgent = CreateMlAgentRig(transform);
            }

            ConfigureMlAgent(mlAgent);
            mlAgent.manager = this;
        }

        private void ConfigureMlAgent(RecycleVisionMlAgent agent)
        {
            if (agent == null)
            {
                return;
            }

            BehaviorParameters behaviorParameters = agent.GetComponent<BehaviorParameters>();

            if (behaviorParameters == null)
            {
                behaviorParameters = agent.gameObject.AddComponent<BehaviorParameters>();
            }

            behaviorParameters.BehaviorName = "RecycleVisionSorter";
            behaviorParameters.BrainParameters.VectorObservationSize = RecycleVisionMlAgent.VectorObservationSize;
            behaviorParameters.BrainParameters.NumStackedVectorObservations = 1;
            behaviorParameters.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(RecycleVisionMlAgent.DiscreteActionCount);

            CameraSensorComponent sensor = agent.GetComponent<CameraSensorComponent>();
            Camera sensorCamera = agent.GetComponentInChildren<Camera>(true);

            if (sensorCamera == null)
            {
                GameObject sensorCameraGo = new GameObject("AgentCamera");
                sensorCameraGo.transform.SetParent(agent.transform, false);
                sensorCamera = sensorCameraGo.AddComponent<Camera>();
                sensorCamera.transform.localPosition = Vector3.zero;
                sensorCamera.transform.localRotation = Quaternion.identity;
            }

            sensorCamera.fieldOfView = 42f;
            sensorCamera.nearClipPlane = 0.1f;
            sensorCamera.farClipPlane = 20f;
            sensorCamera.clearFlags = CameraClearFlags.SolidColor;
            sensorCamera.backgroundColor = new Color(0.08f, 0.1f, 0.13f);
            sensorCamera.enabled = false;

            if (sensor == null)
            {
                sensor = agent.gameObject.AddComponent<CameraSensorComponent>();
            }

            sensor.Camera = sensorCamera;
            sensor.SensorName = "RecycleVisionCamera";
            sensor.Width = 84;
            sensor.Height = 84;
            sensor.Grayscale = false;
            sensor.ObservationStacks = 1;
            sensor.RuntimeCameraEnable = false;
        }

        private void ConfigureLightRig(Transform parent)
        {
            Light[] existingLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            Light directional = null;

            foreach (Light light in existingLights)
            {
                if (light.type == LightType.Directional)
                {
                    directional = light;
                    break;
                }
            }

            if (directional == null)
            {
                GameObject sun = new GameObject("Sun");
                sun.transform.SetParent(parent, false);
                directional = sun.AddComponent<Light>();
                directional.type = LightType.Directional;
            }

            directional.intensity = 1.25f;
            directional.shadows = LightShadows.Soft;
            directional.transform.rotation = Quaternion.Euler(42f, -28f, 0f);

            CreatePointLight(parent, "AccentLeft", new Vector3(-2.8f, 2.6f, -0.8f), new Color(0.32f, 0.75f, 1f), 12f, 6f);
            CreatePointLight(parent, "AccentRight", new Vector3(2.8f, 2.3f, -0.2f), new Color(1f, 0.72f, 0.26f), 10f, 6f);
        }

        private void CreatePointLight(
            Transform parent,
            string name,
            Vector3 position,
            Color color,
            float range,
            float intensity)
        {
            GameObject lightGo = new GameObject(name);
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = position;

            Light pointLight = lightGo.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = color;
            pointLight.range = range;
            pointLight.intensity = intensity;
        }

        private Transform CreateObservationGlass(Transform parent, Material trimMaterial)
        {
            Material glassMaterial = CreateRuntimeMaterial(new Color(0.44f, 0.78f, 0.96f, 0.36f), 0.85f, 0.04f, new Color(0.44f, 0.78f, 0.96f, 0.18f));

            GameObject glass = CreatePrimitive(
                parent,
                "ObservationGlass",
                PrimitiveType.Cube,
                new Vector3(0f, 2.15f, 4.48f),
                new Vector3(3.4f, 1.55f, 0.06f),
                glassMaterial);

            glass.transform.localPosition += new Vector3(0f, 0f, 0.01f);

            return glass != null ? glass.transform : null;
        }

        private void CreateEnvironment(
            Transform parent,
            Material floorMaterial,
            Material wallMaterial,
            Material tableMaterial,
            Material trimMaterial)
        {
            CreatePrimitive(parent, "Floor", PrimitiveType.Cube, Vector3.zero, new Vector3(8f, 0.1f, 8f), floorMaterial);
            CreatePrimitive(parent, "BackdropWall", PrimitiveType.Cube, new Vector3(0f, 1.7f, 2.4f), new Vector3(8f, 3.5f, 0.18f), wallMaterial);
            CreatePrimitive(parent, "HeaderBeam", PrimitiveType.Cube, new Vector3(0f, 3f, 1.9f), new Vector3(8f, 0.16f, 0.2f), trimMaterial);
            CreatePrimitive(parent, "LeftColumn", PrimitiveType.Cube, new Vector3(-3.4f, 1.5f, 1.7f), new Vector3(0.25f, 3f, 0.25f), tableMaterial);
            CreatePrimitive(parent, "RightColumn", PrimitiveType.Cube, new Vector3(3.4f, 1.5f, 1.7f), new Vector3(0.25f, 3f, 0.25f), tableMaterial);
        }

        private void CreateStationSurface(
            Transform parent,
            Material tableMaterial,
            Material trayMaterial,
            Material shadowMaterial,
            Material trimMaterial)
        {
            CreatePrimitive(parent, "TableTop", PrimitiveType.Cube, new Vector3(0f, 0.82f, 0f), new Vector3(6.4f, 0.18f, 3.2f), tableMaterial);
            CreatePrimitive(parent, "FrontTrim", PrimitiveType.Cube, new Vector3(0f, 0.92f, -1.55f), new Vector3(6.44f, 0.03f, 0.06f), trimMaterial);

            Vector3[] legPositions =
            {
                new Vector3(-2.95f, 0.38f, -1.35f),
                new Vector3(2.95f, 0.38f, -1.35f),
                new Vector3(-2.95f, 0.38f, 1.35f),
                new Vector3(2.95f, 0.38f, 1.35f)
            };

            foreach (Vector3 position in legPositions)
            {
                CreatePrimitive(parent, $"Leg_{position.x}_{position.z}", PrimitiveType.Cube, position, new Vector3(0.18f, 0.76f, 0.18f), tableMaterial);
            }

            CreatePrimitive(parent, "SpawnTray", PrimitiveType.Cube, new Vector3(0f, 1.02f, -0.1f), new Vector3(1.5f, 0.06f, 1.0f), trayMaterial);
            CreatePrimitive(parent, "SpawnInset", PrimitiveType.Cube, new Vector3(0f, 1.04f, -0.1f), new Vector3(1.32f, 0.025f, 0.84f), shadowMaterial);
        }

        private SortingBin[] CreateBins(Transform parent, Font font, Material shadowMaterial)
        {
            Color[] binColors =
            {
                new Color(0.22f, 0.62f, 0.97f),
                new Color(0.9f, 0.72f, 0.24f),
                new Color(0.32f, 0.84f, 0.62f),
                new Color(0.73f, 0.56f, 0.24f)
            };

            Vector3[] positions =
            {
                new Vector3(-2.6f, 0.96f, 0.7f),
                new Vector3(-1.2f, 0.96f, 0.7f),
                new Vector3(0.2f, 0.96f, 0.7f),
                new Vector3(1.6f, 0.96f, 0.7f)
            };

            SortingBin[] createdBins = new SortingBin[4];

            for (int index = 0; index < createdBins.Length; index++)
            {
                RecycleCategory category = (RecycleCategory)index;
                Material shellMaterial = CreateRuntimeMaterial(binColors[index], 0.52f, 0.06f, binColors[index] * 0.15f);

                GameObject binRoot = new GameObject(category.ToString());
                binRoot.transform.SetParent(parent, false);
                binRoot.transform.localPosition = positions[index];

                GameObject shell = CreatePrimitive(binRoot.transform, "Shell", PrimitiveType.Cube, new Vector3(0f, 0.28f, 0f), new Vector3(0.68f, 0.56f, 0.68f), shellMaterial);
                GameObject cavity = CreatePrimitive(binRoot.transform, "Cavity", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0f), new Vector3(0.5f, 0.26f, 0.5f), shadowMaterial);
                GameObject lip = CreatePrimitive(binRoot.transform, "Lip", PrimitiveType.Cube, new Vector3(0f, 0.62f, 0.3f), new Vector3(0.74f, 0.02f, 0.18f), shellMaterial);
                lip.transform.localRotation = Quaternion.Euler(-50f, 0f, 0f);

                Collider cavityCollider = cavity.GetComponent<Collider>();
                if (cavityCollider != null)
                {
                    UnityEngine.Object.Destroy(cavityCollider);
                }

                // ✅ FIX: Bigger drop zone, lower position, IgnoreRaycast layer
                GameObject trigger = new GameObject("DropZone");
                trigger.transform.SetParent(binRoot.transform, false);
                trigger.layer = 2; // IgnoreRaycast — so camera clicks don't hit it
                trigger.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
                triggerCollider.isTrigger = true;
                triggerCollider.size = new Vector3(1.1f, 0.65f, 1.1f);

                Transform dropAnchor = new GameObject("DropAnchor").transform;
                dropAnchor.SetParent(binRoot.transform, false);
                dropAnchor.localPosition = new Vector3(0f, 0.22f, 0f);

                TextMesh label = CreateTextMesh(binRoot.transform, $"{category}Label", FormatBinLabel(category), font, 44, TextAlignment.Center);
                label.transform.localPosition = new Vector3(0f, 0.88f, 0f);
                label.characterSize = 0.026f;

                SortingBin sortingBin = binRoot.AddComponent<SortingBin>();
                sortingBin.binType = category;
                sortingBin.dropAnchor = dropAnchor;
                sortingBin.renderers = new[] { shell.GetComponent<Renderer>(), lip.GetComponent<Renderer>() };
                sortingBin.label = label;
                sortingBin.RefreshPresentation();
                createdBins[index] = sortingBin;
            }

            return createdBins;
        }

        private DashboardBoard CreateDashboard(Transform parent, Font font, Material boardMaterial)
        {
            float boardWidth = 2.8f;
            float boardHeight = 1.2f;

            GameObject boardRoot = new GameObject("DashboardBoard");
            boardRoot.transform.SetParent(parent, false);
            boardRoot.transform.localPosition = new Vector3(0f, 0.08f, -0.01f);
            boardRoot.transform.localRotation = Quaternion.identity;
            boardRoot.transform.localScale = Vector3.one;

            CreatePrimitive(boardRoot.transform, "BoardSurface", PrimitiveType.Cube, Vector3.zero, new Vector3(boardWidth, boardHeight, 0.12f), boardMaterial);
            CreatePrimitive(boardRoot.transform, "BoardTrim", PrimitiveType.Cube, new Vector3(0f, boardHeight * 0.5f - 0.02f, 0.04f), new Vector3(boardWidth + 0.04f, 0.04f, 0.15f),
                CreateRuntimeMaterial(new Color(0.94f, 0.78f, 0.26f), 0.65f, 0.14f, new Color(0.94f, 0.78f, 0.26f) * 0.22f));

            DashboardBoard createdDashboard = boardRoot.AddComponent<DashboardBoard>();

            float titleY = boardHeight * 0.5f - 0.08f;
            createdDashboard.titleText = CreateTextMesh(boardRoot.transform, "Title", "Sorting Dashboard", font, 48, TextAlignment.Center);
            createdDashboard.titleText.transform.localPosition = new Vector3(0f, titleY, -0.03f);
            createdDashboard.titleText.characterSize = 0.04f;

            float modeY = titleY - 0.12f;
            createdDashboard.modeText = CreateTextMesh(boardRoot.transform, "Mode", "Training Mode", font, 32, TextAlignment.Center);
            createdDashboard.modeText.transform.localPosition = new Vector3(0f, modeY, -0.03f);
            createdDashboard.modeText.characterSize = 0.028f;
            createdDashboard.modeText.color = new Color(0.94f, 0.78f, 0.26f);

            float leftColX = -boardWidth * 0.5f + 0.18f;
            float accY = modeY - 0.14f;
            createdDashboard.humanAccuracyText = CreateTextMesh(boardRoot.transform, "HumanAccuracy", "Human: n/a", font, 26, TextAlignment.Left);
            createdDashboard.humanAccuracyText.anchor = TextAnchor.MiddleLeft;
            createdDashboard.humanAccuracyText.transform.localPosition = new Vector3(leftColX, accY, -0.03f);
            createdDashboard.humanAccuracyText.characterSize = 0.022f;

            float aiY = accY - 0.09f;
            createdDashboard.aiAccuracyText = CreateTextMesh(boardRoot.transform, "AiAccuracy", "AI: 0%", font, 26, TextAlignment.Left);
            createdDashboard.aiAccuracyText.anchor = TextAnchor.MiddleLeft;
            createdDashboard.aiAccuracyText.transform.localPosition = new Vector3(leftColX, aiY, -0.03f);
            createdDashboard.aiAccuracyText.characterSize = 0.022f;

            float sugY = aiY - 0.1f;
            createdDashboard.suggestionText = CreateTextMesh(boardRoot.transform, "Suggestion", "Suggestion: waiting...", font, 22, TextAlignment.Left);
            createdDashboard.suggestionText.anchor = TextAnchor.MiddleLeft;
            createdDashboard.suggestionText.transform.localPosition = new Vector3(leftColX, sugY, -0.03f);
            createdDashboard.suggestionText.characterSize = 0.019f;

            float confY = sugY - 0.09f;
            createdDashboard.confusionText = CreateTextMesh(boardRoot.transform, "Confusion", "Mix-up: none", font, 20, TextAlignment.Left);
            createdDashboard.confusionText.anchor = TextAnchor.MiddleLeft;
            createdDashboard.confusionText.transform.localPosition = new Vector3(leftColX, confY, -0.03f);
            createdDashboard.confusionText.characterSize = 0.018f;

            float histY = confY - 0.08f;
            createdDashboard.historyText = CreateTextMesh(boardRoot.transform, "History", "History: none", font, 18, TextAlignment.Left);
            createdDashboard.historyText.anchor = TextAnchor.MiddleLeft;
            createdDashboard.historyText.transform.localPosition = new Vector3(leftColX, histY, -0.03f);
            createdDashboard.historyText.characterSize = 0.016f;

            Transform[] bars = new Transform[4];
            TextMesh[] labels = new TextMesh[4];
            Color[] colors =
            {
                new Color(0.22f, 0.62f, 0.97f),
                new Color(0.9f, 0.72f, 0.24f),
                new Color(0.32f, 0.84f, 0.62f),
                new Color(0.73f, 0.56f, 0.24f)
            };

            float barStartX = 0.15f;
            float barSpacing = 0.48f;
            float barBottomY = -boardHeight * 0.5f + 0.2f;
            float barMaxHeight = 0.55f;
            float barWidth = 0.22f;

            for (int index = 0; index < 4; index++)
            {
                float x = barStartX + (index * barSpacing);
                GameObject bar = CreatePrimitive(
                    boardRoot.transform,
                    $"Bar_{index}",
                    PrimitiveType.Cube,
                    new Vector3(x, barBottomY + barMaxHeight * 0.5f, -0.03f),
                    new Vector3(barWidth, barMaxHeight, 0.1f),
                    CreateRuntimeMaterial(colors[index], 0.3f, 0.05f, colors[index] * 0.08f));

                bars[index] = bar.transform;
                labels[index] = CreateTextMesh(boardRoot.transform, $"BarLabel_{index}", ((RecycleCategory)index).ToDisplayName(), font, 20, TextAlignment.Center);
                labels[index].transform.localPosition = new Vector3(x, barBottomY - 0.05f, -0.03f);
                labels[index].characterSize = 0.018f;
                labels[index].color = new Color(0.9f, 0.92f, 0.96f);
            }

            createdDashboard.classBars = bars;
            createdDashboard.classBarLabels = labels;
            return createdDashboard;
        }

        private static string FormatBinLabel(RecycleCategory category)
        {
            string label = category.ToDisplayName();

            if (label.Contains(" / "))
            {
                label = label.Replace(" / ", "\n");
            }

            return label;
        }

        private RecycleVisionHud CreateHud(Font font)
        {
            GameObject canvasGo = new GameObject("HUDCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvasGo.AddComponent<GraphicRaycaster>();

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RecycleVisionHud createdHud = canvasGo.AddComponent<RecycleVisionHud>();

            Color panelColor = new Color(0.05f, 0.08f, 0.12f, 0.8f);
            Color accent = new Color(0.97f, 0.79f, 0.24f, 0.95f);

            GameObject leftPanel = CreateUiPanel("InfoPanel", canvasGo.transform, panelColor, new Vector2(0.03f, 0.54f), new Vector2(0.38f, 0.95f));
            createdHud.titleText = CreateUiText("TitleText", leftPanel.transform, font, 40, TextAnchor.UpperLeft, "RecycleVision Lite", Color.white, new Vector2(0.04f, 0.78f), new Vector2(0.96f, 0.96f));
            createdHud.titleText.fontStyle = FontStyle.Bold;
            createdHud.titleText.fontSize = 44;
            createdHud.modeText = CreateUiText("ModeText", leftPanel.transform, font, 26, TextAnchor.UpperLeft, "Training Mode", new Color(0.85f, 0.9f, 0.98f), new Vector2(0.04f, 0.64f), new Vector2(0.96f, 0.82f));
            createdHud.modeText.fontStyle = FontStyle.Bold;
            createdHud.modeText.fontSize = 28;
            createdHud.itemText = CreateUiText("ItemText", leftPanel.transform, font, 30, TextAnchor.UpperLeft, "Current item:", Color.white, new Vector2(0.04f, 0.46f), new Vector2(0.96f, 0.66f));
            createdHud.itemText.fontSize = 32;
            createdHud.hintText = CreateUiText("HintText", leftPanel.transform, font, 24, TextAnchor.UpperLeft, string.Empty, new Color(0.86f, 0.89f, 0.94f), new Vector2(0.04f, 0.18f), new Vector2(0.96f, 0.46f));
            createdHud.feedbackText = CreateUiText("FeedbackText", leftPanel.transform, font, 24, TextAnchor.UpperLeft, string.Empty, Color.white, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.18f));
            createdHud.feedbackText.fontSize = 24;

            GameObject rightPanel = CreateUiPanel("StatsPanel", canvasGo.transform, panelColor, new Vector2(0.66f, 0.54f), new Vector2(0.97f, 0.95f));
            createdHud.aiSuggestionText = CreateUiText("AISuggestionText", rightPanel.transform, font, 26, TextAnchor.UpperLeft, "AI suggestion: waiting for item", Color.white, new Vector2(0.05f, 0.44f), new Vector2(0.95f, 0.94f));
            createdHud.aiSuggestionText.fontStyle = FontStyle.Bold;
            createdHud.summaryText = CreateUiText("SummaryText", rightPanel.transform, font, 28, TextAnchor.UpperLeft, "Human n/a (0 manual)   |   AI 0/0 (0%)", new Color(0.93f, 0.95f, 1f), new Vector2(0.05f, 0.22f), new Vector2(0.95f, 0.4f));
            createdHud.summaryText.fontStyle = FontStyle.Bold;
            createdHud.summaryText.fontSize = 30;
            createdHud.logStatusText = CreateUiText("LogStatusText", rightPanel.transform, font, 20, TextAnchor.UpperLeft, "Logging: On (features, no frames) - press L to toggle", new Color(0.78f, 0.85f, 0.92f), new Vector2(0.05f, 0.13f), new Vector2(0.95f, 0.22f));
            Image timerRing = CreateUiImage("TimerRing", rightPanel.transform, new Vector2(0.03f, 0.04f), new Vector2(0.12f, 0.13f));
            timerRing.color = accent;
            timerRing.raycastTarget = false;
            createdHud.timerRingImage = timerRing;

            createdHud.timerText = CreateUiText("TimerText", rightPanel.transform, font, 32, TextAnchor.MiddleLeft, "Time left: 0.0s", accent, new Vector2(0.14f, 0.04f), new Vector2(0.95f, 0.13f));
            createdHud.timerText.fontStyle = FontStyle.Bold;
            createdHud.timerText.fontSize = 36;
            createdHud.timerText.enabled = false;

            GameObject buttonRow = CreateUiPanel("ButtonRow", canvasGo.transform, new Color(0.05f, 0.08f, 0.12f, 0.72f), new Vector2(0.28f, 0.04f), new Vector2(0.72f, 0.16f));
            createdHud.trainingButton = CreateButton("TrainingButton", buttonRow.transform, font, "Training", new Color(0.2f, 0.46f, 0.89f), new Vector2(0.04f, 0.18f), new Vector2(0.31f, 0.82f));
            createdHud.quickSortButton = CreateButton("QuickSortButton", buttonRow.transform, font, "Quick Sort", new Color(0.18f, 0.68f, 0.46f), new Vector2(0.35f, 0.18f), new Vector2(0.62f, 0.82f));
            createdHud.restartButton = CreateButton("RestartButton", buttonRow.transform, font, "Restart", new Color(0.86f, 0.55f, 0.2f), new Vector2(0.66f, 0.18f), new Vector2(0.93f, 0.82f));

            GameObject reportPanel = CreateUiPanel("ReportPanel", canvasGo.transform, new Color(0.03f, 0.05f, 0.08f, 0.92f), new Vector2(0.26f, 0.22f), new Vector2(0.74f, 0.78f));
            Text reportTitle = CreateUiText("ReportTitle", reportPanel.transform, font, 38, TextAnchor.UpperCenter, "Session Report", Color.white, new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.95f));
            reportTitle.fontStyle = FontStyle.Bold;
            reportTitle.fontSize = 42;
            createdHud.reportText = CreateUiText("ReportText", reportPanel.transform, font, 24, TextAnchor.UpperLeft, string.Empty, new Color(0.92f, 0.94f, 0.98f), new Vector2(0.08f, 0.2f), new Vector2(0.92f, 0.78f));
            createdHud.reportText.fontSize = 28;
            createdHud.closeReportButton = CreateButton("CloseReportButton", reportPanel.transform, font, "Close", accent, new Vector2(0.35f, 0.05f), new Vector2(0.65f, 0.16f));
            createdHud.reportPanel = reportPanel;
            reportPanel.SetActive(false);

            return createdHud;
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
        }

        private RecycleVisionSortingAgent CreateAgentRig(Transform parent, BaselineAiAdvisor advisor)
        {
            GameObject agentGo = new GameObject("RecycleVisionAgent");
            agentGo.transform.SetParent(parent, false);
            agentGo.transform.localPosition = new Vector3(0f, 2.05f, -2.18f);
            agentGo.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);

            RecycleVisionSortingAgent agent = agentGo.AddComponent<RecycleVisionSortingAgent>();
            agent.baselineAi = advisor;

            GameObject sensorCameraGo = new GameObject("AgentCamera");
            sensorCameraGo.transform.SetParent(agentGo.transform, false);
            Camera sensorCamera = sensorCameraGo.AddComponent<Camera>();
            sensorCamera.fieldOfView = 42f;
            sensorCamera.nearClipPlane = 0.1f;
            sensorCamera.farClipPlane = 20f;
            sensorCamera.clearFlags = CameraClearFlags.SolidColor;
            sensorCamera.backgroundColor = new Color(0.1f, 0.13f, 0.16f);
            sensorCamera.enabled = false;

            return agent;
        }

        private RecycleVisionMlAgent CreateMlAgentRig(Transform parent)
        {
            GameObject agentGo = new GameObject("RecycleVisionMlAgent");
            agentGo.transform.SetParent(parent, false);
            agentGo.transform.localPosition = new Vector3(0.6f, 2.05f, -2.18f);
            agentGo.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);

            BehaviorParameters behaviorParameters = agentGo.AddComponent<BehaviorParameters>();
            behaviorParameters.BehaviorName = "RecycleVisionSorter";
            behaviorParameters.BehaviorType = BehaviorType.Default;
            behaviorParameters.BrainParameters.VectorObservationSize = RecycleVisionMlAgent.VectorObservationSize;
            behaviorParameters.BrainParameters.NumStackedVectorObservations = 1;
            behaviorParameters.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(RecycleVisionMlAgent.DiscreteActionCount);

            GameObject sensorCameraGo = new GameObject("AgentCamera");
            sensorCameraGo.transform.SetParent(agentGo.transform, false);
            Camera sensorCamera = sensorCameraGo.AddComponent<Camera>();
            sensorCamera.fieldOfView = 42f;
            sensorCamera.nearClipPlane = 0.1f;
            sensorCamera.farClipPlane = 20f;
            sensorCamera.clearFlags = CameraClearFlags.SolidColor;
            sensorCamera.backgroundColor = new Color(0.1f, 0.13f, 0.16f);
            sensorCamera.enabled = false;

            CameraSensorComponent sensor = agentGo.AddComponent<CameraSensorComponent>();
            sensor.Camera = sensorCamera;
            sensor.SensorName = "RecycleVisionCamera";
            sensor.Width = 84;
            sensor.Height = 84;
            sensor.Grayscale = false;
            sensor.ObservationStacks = 1;
            sensor.RuntimeCameraEnable = false;

            RecycleVisionMlAgent agent = agentGo.AddComponent<RecycleVisionMlAgent>();
            return agent;
        }

        private GameObject CreatePrimitive(
            Transform parent,
            string name,
            PrimitiveType primitiveType,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject primitive = GameObject.CreatePrimitive(primitiveType);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localRotation = Quaternion.identity;
            primitive.transform.localScale = localScale;
            primitive.GetComponent<Renderer>().sharedMaterial = material;
            return primitive;
        }

        private TextMesh CreateTextMesh(
            Transform parent,
            string name,
            string text,
            Font font,
            int fontSize,
            TextAlignment alignment)
        {
            GameObject textGo = new GameObject(name);
            textGo.transform.SetParent(parent, false);
            TextMesh textMesh = textGo.AddComponent<TextMesh>();
            textMesh.font = font;
            int effectiveFontSize = fontSize * 2;
            textMesh.fontSize = effectiveFontSize;
            textMesh.characterSize = 0.025f * (fontSize / (float)effectiveFontSize);
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = alignment;
            textMesh.text = text;
            textMesh.color = Color.white;
            textGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return textMesh;
        }

        private GameObject CreateUiPanel(
            string name,
            Transform parent,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = color;
            return panel;
        }

        private Text CreateUiText(
            string name,
            Transform parent,
            Font font,
            int fontSize,
            TextAnchor alignment,
            string text,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject textGo = new GameObject(name, typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(parent, false);
            RectTransform rect = textGo.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Text uiText = textGo.GetComponent<Text>();
            uiText.font = font;
            uiText.fontSize = fontSize;
            uiText.alignment = alignment;
            uiText.color = color;
            uiText.text = text;
            uiText.horizontalOverflow = HorizontalWrapMode.Wrap;
            uiText.verticalOverflow = VerticalWrapMode.Overflow;
            return uiText;
        }

        private Image CreateUiImage(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject imageGo = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageGo.transform.SetParent(parent, false);
            RectTransform rect = imageGo.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = imageGo.GetComponent<Image>();
            image.color = Color.white;
            return image;
        }

        private Button CreateButton(
            string name,
            Transform parent,
            Font font,
            string label,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject buttonGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(parent, false);

            RectTransform rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = buttonGo.GetComponent<Image>();
            image.color = color;

            Button button = buttonGo.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.14f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            Text labelText = CreateUiText("Label", buttonGo.transform, font, 24, TextAnchor.MiddleCenter, label, Color.white, Vector2.zero, Vector2.one);
            labelText.fontStyle = FontStyle.Bold;
            labelText.fontSize = 28;
            return button;
        }

        private Material CreateRuntimeMaterial(Color baseColor, float smoothness, float metallic, Color emissionColor)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.color = baseColor;
            material.SetColor("_BaseColor", baseColor);

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emissionColor);
            }

            return material;
        }
    }
}