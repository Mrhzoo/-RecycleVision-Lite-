using UnityEngine;

namespace RecycleVision
{
    public class WasteItem : MonoBehaviour
    {
        public WasteItemDefinition Definition { get; private set; }
        public Rigidbody Body { get; private set; }
        public bool IsHeld { get; private set; }
        public bool IsResolved { get; private set; }

        private Vector3 defaultScale = Vector3.one;
        private Renderer[] cachedRenderers;
        private TextMesh aiOverlayText;
        private Transform aiOverlayRoot;
        private Camera cachedCamera;
        private int fallThroughFrameCount;
        private Material[] glowMaterials;
        private Color[] glowBaseEmission;
        private bool glowEnabled;
        private Color glowColor = new Color(0.22f, 0.84f, 0.96f);
        private float glowTimer;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private const float OverlayHeightOffset = 0.08f;
        private const float OverlayCharacterSize = 0.02f;
        private const float FallThresholdY = -0.8f; // Higher threshold for earlier detection
        private const int FallDetectionFrames = 2;  // Faster detection

        public void Initialize(WasteItemDefinition definition, Rigidbody body)
        {
            Definition = definition;
            Body = body;
            defaultScale = transform.localScale;
            name = $"WasteItem_{definition.DisplayName.Replace(" ", string.Empty)}";
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
            fallThroughFrameCount = 0;
        }

        private void LateUpdate()
        {
            if (aiOverlayText == null || !aiOverlayText.gameObject.activeSelf) return;
            UpdateOverlayTransform();
        }

        private void Update()
        {
            if (!glowEnabled || glowMaterials == null) return;

            glowTimer += Time.deltaTime * 3f;
            float pulse = 0.6f + Mathf.Sin(glowTimer) * 0.4f;

            for (int i = 0; i < glowMaterials.Length; i++)
            {
                Material mat = glowMaterials[i];
                if (mat == null || !mat.HasProperty(EmissionColorId))
                    continue;

                Color baseEmission = glowBaseEmission[i];
                Color target = baseEmission + glowColor * pulse;
                mat.EnableKeyword("_EMISSION");
                mat.SetColor(EmissionColorId, target);
            }
        }

        private void FixedUpdate()
        {
            if (IsHeld || IsResolved || Body == null || !Body.useGravity)
            {
                fallThroughFrameCount = 0;
                return;
            }

            if (transform.position.y < FallThresholdY)
            {
                fallThroughFrameCount++;
                if (fallThroughFrameCount >= FallDetectionFrames)
                {
                    fallThroughFrameCount = 0;
                    NotifyFallThrough();
                }
            }
            else
            {
                fallThroughFrameCount = 0;
            }
        }

        private void NotifyFallThrough()
        {
            SortingStationManager manager = FindFirstObjectByType<SortingStationManager>();
            if (manager != null)
            {
                Body.isKinematic = true;
                Body.useGravity = false;
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                manager.ResetCurrentItemPosition();
            }
        }

        public void PlaceAt(Vector3 position, Quaternion rotation)
        {
            IsHeld = false;
            IsResolved = false;
            fallThroughFrameCount = 0;

            // Detach completely so no parent affects physics
            transform.SetParent(null, true);

            // Hard-set position and rotation
            transform.SetPositionAndRotation(position, rotation);

            // Re-attach to a parent container after positioning
            SortingStationManager mgr = FindFirstObjectByType<SortingStationManager>();
            if (mgr != null && mgr.itemContainer != null)
                transform.SetParent(mgr.itemContainer, true);

            if (Body == null)
                Body = GetComponent<Rigidbody>();

            if (Body != null)
            {
                Body.isKinematic = false;
                Body.useGravity = true;
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.WakeUp();
                Body.ResetInertiaTensor();
            }

            transform.localScale = defaultScale;
        }

        public void SetHeld(bool held)
        {
            if (IsResolved) return;

            IsHeld = held;
            transform.localScale = held ? defaultScale * 1.04f : defaultScale;

            if (Body == null) return;

            if (held)
            {
                if (!Body.isKinematic)
                {
                    Body.linearVelocity = Vector3.zero;
                    Body.angularVelocity = Vector3.zero;
                }
                Body.isKinematic = true;
                Body.useGravity = false;
            }
            else
            {
                Body.isKinematic = false;
                Body.useGravity = true;
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.WakeUp();
            }
        }

        public void RotateBy(float angle)
        {
            transform.Rotate(Vector3.up, angle, Space.World);
        }

        public void SetGlow(bool enabled, Color color)
        {
            glowEnabled = enabled;
            glowColor = color;
            glowTimer = 0f;

            if (enabled)
            {
                EnsureGlowMaterials();
                return;
            }

            ResetGlowMaterials();
        }

        public void SnapTo(Transform anchor)
        {
            IsHeld = false;
            IsResolved = true;
            fallThroughFrameCount = 0;

            if (Body != null)
            {
                if (!Body.isKinematic)
                {
                    Body.linearVelocity = Vector3.zero;
                    Body.angularVelocity = Vector3.zero;
                }
                Body.isKinematic = true;
                Body.useGravity = false;
            }

            transform.SetParent(null, true);
            transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            transform.SetParent(anchor, true);
        }

        public void SetAiOverlay(AiSuggestion suggestion)
        {
            EnsureOverlay();
            string confidenceLabel = suggestion.IsEstimatedConfidence ? "est. " : string.Empty;
            aiOverlayText.text = $"AI: {suggestion.PredictedBin.ToDisplayName()}\n({confidenceLabel}{suggestion.Confidence * 100f:0}%)";
            aiOverlayText.color = Color.white;
            aiOverlayRoot.gameObject.SetActive(true);
            UpdateOverlayTransform();
        }

        public void ClearAiOverlay()
        {
            if (aiOverlayRoot == null) return;
            aiOverlayText.text = string.Empty;
            aiOverlayRoot.gameObject.SetActive(false);
        }

        public void ResetForPool(Transform poolRoot)
        {
            IsHeld = false;
            IsResolved = false;
            fallThroughFrameCount = 0;

            ClearAiOverlay();

            if (Body != null)
            {
                bool wasKinematic = Body.isKinematic;
                if (wasKinematic)
                {
                    Body.isKinematic = false;
                }

                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.useGravity = false;
                Body.isKinematic = true;
            }

            transform.localScale = defaultScale;
            transform.SetParent(poolRoot, false);
            gameObject.SetActive(false);
        }

        private void EnsureGlowMaterials()
        {
            if (cachedRenderers == null || cachedRenderers.Length == 0) return;
            if (glowMaterials != null) return;

            glowMaterials = new Material[cachedRenderers.Length];
            glowBaseEmission = new Color[cachedRenderers.Length];

            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                Material mat = cachedRenderers[i].material;
                glowMaterials[i] = mat;
                glowBaseEmission[i] = mat != null && mat.HasProperty(EmissionColorId)
                    ? mat.GetColor(EmissionColorId)
                    : Color.black;
            }
        }

        private void ResetGlowMaterials()
        {
            if (glowMaterials == null) return;

            for (int i = 0; i < glowMaterials.Length; i++)
            {
                Material mat = glowMaterials[i];
                if (mat == null || !mat.HasProperty(EmissionColorId))
                    continue;

                mat.SetColor(EmissionColorId, glowBaseEmission[i]);
            }
        }

        private void EnsureOverlay()
        {
            if (aiOverlayRoot != null) return;
            aiOverlayRoot = new GameObject("AiSuggestionOverlay").transform;
            aiOverlayRoot.SetParent(transform, true);

            aiOverlayText = aiOverlayRoot.gameObject.AddComponent<TextMesh>();
            aiOverlayText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            aiOverlayText.fontSize = 64;
            aiOverlayText.characterSize = OverlayCharacterSize;
            aiOverlayText.anchor = TextAnchor.MiddleCenter;
            aiOverlayText.alignment = TextAlignment.Center;
            aiOverlayText.color = Color.white;

            MeshRenderer renderer = aiOverlayText.GetComponent<MeshRenderer>();
            if (renderer != null && aiOverlayText.font != null)
                renderer.sharedMaterial = aiOverlayText.font.material;
        }

        private void UpdateOverlayTransform()
        {
            if (aiOverlayRoot == null) return;
            Bounds bounds = GetVisualBounds();
            aiOverlayRoot.position = bounds.center + Vector3.up * (bounds.extents.y + OverlayHeightOffset);

            Camera camera = GetOverlayCamera();
            if (camera == null) return;

            Vector3 lookDirection = camera.transform.position - aiOverlayRoot.position;
            if (lookDirection.sqrMagnitude > 0.0001f)
                aiOverlayRoot.rotation = Quaternion.LookRotation(lookDirection, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
        }

        private Bounds GetVisualBounds()
        {
            if (cachedRenderers == null || cachedRenderers.Length == 0)
                return new Bounds(transform.position, Vector3.one * 0.3f);

            Bounds bounds = cachedRenderers[0].bounds;
            for (int i = 1; i < cachedRenderers.Length; i++)
                bounds.Encapsulate(cachedRenderers[i].bounds);
            return bounds;
        }

        private Camera GetOverlayCamera()
        {
            if (cachedCamera != null && cachedCamera.isActiveAndEnabled)
                return cachedCamera;
            cachedCamera = Camera.main;
            if (cachedCamera == null)
                cachedCamera = FindFirstObjectByType<Camera>();
            return cachedCamera;
        }
    }
}