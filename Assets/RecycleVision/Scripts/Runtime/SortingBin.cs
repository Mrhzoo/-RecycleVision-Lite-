using System.Collections;
using UnityEngine;

namespace RecycleVision
{
    public class SortingBin : MonoBehaviour
    {
        public RecycleCategory binType;
        public SortingStationManager manager;
        public Transform dropAnchor;
        public Renderer[] renderers;
        public TextMesh label;

        private Material[] runtimeMaterials;
        private Color[] baseColors;
        private bool suggested;

        private GameObject ghostOutline;
        private Material ghostMaterial;
        private TextMesh ghostText;
        private Color ghostBaseColor = new Color(0.2f, 0.9f, 1f, 0.22f);
        private Color ghostEmissionColor = new Color(0.2f, 0.9f, 1f, 1f);
        private Color ghostTextBaseColor = new Color(0.85f, 0.95f, 1f);
        private Coroutine ghostRoutine;

        // Cache the trigger collider for direct overlap checks
        private BoxCollider triggerZone;
        private WasteItem lastTrackedItem;
        private bool lastItemWasTracked;

        public RecycleCategory BinType => binType;
        public Transform DropAnchor => dropAnchor;

        private void Awake()
        {
            RefreshPresentation();
            CreateGhostEffects();
            // Find the DropZone trigger collider
            Transform dropZone = transform.Find("DropZone");
            if (dropZone != null)
                triggerZone = dropZone.GetComponent<BoxCollider>();
        }

        private void Update()
        {
            if (runtimeMaterials == null) return;

            for (int i = 0; i < runtimeMaterials.Length; i++)
            {
                Color target = suggested
                    ? Color.Lerp(baseColors[i], Color.white, 0.22f + Mathf.PingPong(Time.time * 0.8f, 0.12f))
                    : baseColors[i];
                runtimeMaterials[i].SetColor("_BaseColor", target);
                runtimeMaterials[i].color = target;
            }

            if (manager == null) return;

            WasteItem readyItem = null;

            // 1. Check previously tracked item (caught by OnTriggerEnter/Stay)
            if (lastTrackedItem != null && !lastTrackedItem.IsHeld && !lastTrackedItem.IsResolved)
            {
                readyItem = lastTrackedItem;
            }

            // 2. BULLETPROOF: Direct physics overlap check inside the trigger zone.
            //    This catches ANY item that's inside the bin, even if trigger events
            //    were missed due to physics timing.
            if (readyItem == null && triggerZone != null)
            {
                // Calculate the world-space bounds of the trigger volume
                Matrix4x4 m = triggerZone.transform.localToWorldMatrix;
                Vector3 worldCenter = triggerZone.transform.TransformPoint(triggerZone.center);
                Vector3 worldSize = Vector3.Scale(triggerZone.size, triggerZone.transform.lossyScale);

                Collider[] hits = Physics.OverlapBox(worldCenter, worldSize * 0.5f, triggerZone.transform.rotation, ~0, QueryTriggerInteraction.Collide);

                foreach (Collider hit in hits)
                {
                    WasteItem item = hit.GetComponentInParent<WasteItem>();
                    if (item != null && !item.IsHeld && !item.IsResolved)
                    {
                        // Verify the item is actually inside (not just touching) 
                        // by checking its center point
                        Vector3 localPos = triggerZone.transform.InverseTransformPoint(item.transform.position);
                        if (Mathf.Abs(localPos.x) <= triggerZone.size.x * 0.5f &&
                            Mathf.Abs(localPos.y) <= triggerZone.size.y * 0.5f &&
                            Mathf.Abs(localPos.z) <= triggerZone.size.z * 0.5f)
                        {
                            readyItem = item;
                            break;
                        }
                    }
                }
            }

            // 3. Register the drop
            if (readyItem != null)
            {
                manager.HandleItemDroppedInBin(this, readyItem);
                lastTrackedItem = null;
            }
        }

        public void SetSuggested(bool isSuggested)
        {
            suggested = isSuggested;
        }

        public void RefreshPresentation()
        {
            CacheRuntimeMaterials();
            if (label != null)
                label.text = binType.ToDisplayName();
        }

        public void FlashResult(bool wasCorrect)
        {
            StartCoroutine(FlashRoutine(wasCorrect ? new Color(0.39f, 0.95f, 0.56f) : new Color(1f, 0.43f, 0.35f)));
        }

        public void ShowGhostHint(string message, float duration)
        {
            if (ghostOutline == null || ghostText == null)
            {
                return;
            }

            ghostText.text = message;
            ghostText.color = ghostTextBaseColor;
            if (ghostMaterial != null)
            {
                ghostMaterial.color = ghostBaseColor;
                if (ghostMaterial.HasProperty("_BaseColor"))
                {
                    ghostMaterial.SetColor("_BaseColor", ghostBaseColor);
                }
                if (ghostMaterial.HasProperty("_EmissionColor"))
                {
                    ghostMaterial.SetColor("_EmissionColor", ghostEmissionColor);
                }
            }
            ghostOutline.SetActive(true);
            ghostText.gameObject.SetActive(true);

            if (ghostRoutine != null)
            {
                StopCoroutine(ghostRoutine);
            }

            ghostRoutine = StartCoroutine(GhostRoutine(duration));
        }

        private void OnTriggerEnter(Collider other)
        {
            WasteItem item = other.GetComponentInParent<WasteItem>();
            if (item == null || item.IsResolved || manager == null) return;

            if (!item.IsHeld)
            {
                manager.HandleItemDroppedInBin(this, item);
                return;
            }

            lastTrackedItem = item;
        }

        private void OnTriggerStay(Collider other)
        {
            WasteItem item = other.GetComponentInParent<WasteItem>();
            if (item == null || item.IsResolved || manager == null) return;

            if (lastTrackedItem == null || lastTrackedItem != item)
                lastTrackedItem = item;
        }

        // DELIBERATELY NO OnTriggerExit - we use direct overlap checks in Update
        // which is more reliable. OnTriggerExit can clear the tracking prematurely
        // when physics nudges the item slightly out of the zone.

        private void CacheRuntimeMaterials()
        {
            if (renderers == null || renderers.Length == 0) return;

            runtimeMaterials = new Material[renderers.Length];
            baseColors = new Color[renderers.Length];

            for (int i = 0; i < renderers.Length; i++)
            {
                runtimeMaterials[i] = renderers[i].material;
                baseColors[i] = runtimeMaterials[i].HasProperty("_BaseColor")
                    ? runtimeMaterials[i].GetColor("_BaseColor")
                    : runtimeMaterials[i].color;
            }
        }

        private void CreateGhostEffects()
        {
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            Vector3 localCenter = transform.InverseTransformPoint(bounds.center);
            Vector3 localSize = transform.InverseTransformVector(bounds.size);
            localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
            localSize *= 1.08f;

            ghostOutline = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ghostOutline.name = "GhostOutline";
            ghostOutline.transform.SetParent(transform, false);
            ghostOutline.transform.localPosition = localCenter;
            ghostOutline.transform.localRotation = Quaternion.identity;
            ghostOutline.transform.localScale = localSize;
            ghostOutline.layer = 2;

            Collider outlineCollider = ghostOutline.GetComponent<Collider>();
            if (outlineCollider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(outlineCollider);
                }
                else
                {
                    DestroyImmediate(outlineCollider);
                }
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            ghostMaterial = new Material(shader);
            ConfigureGhostMaterial(ghostMaterial, ghostBaseColor, ghostEmissionColor);
            ghostOutline.GetComponent<Renderer>().material = ghostMaterial;
            ghostOutline.SetActive(false);

            GameObject ghostTextGo = new GameObject("GhostHint");
            ghostTextGo.transform.SetParent(transform, false);
            ghostTextGo.transform.localPosition = localCenter + Vector3.up * (localSize.y * 0.65f + 0.05f);
            ghostTextGo.transform.localRotation = Quaternion.identity;

            ghostText = ghostTextGo.AddComponent<TextMesh>();
            ghostText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ghostText.fontSize = 64;
            ghostText.characterSize = 0.02f;
            ghostText.anchor = TextAnchor.LowerCenter;
            ghostText.alignment = TextAlignment.Center;
            ghostText.color = ghostTextBaseColor;
            ghostText.text = string.Empty;
            MeshRenderer textRenderer = ghostText.GetComponent<MeshRenderer>();
            if (textRenderer != null && ghostText.font != null)
            {
                textRenderer.sharedMaterial = ghostText.font.material;
            }

            ghostTextGo.SetActive(false);
        }

        private static void ConfigureGhostMaterial(Material material, Color baseColor, Color emissionColor)
        {
            if (material == null)
            {
                return;
            }

            material.color = baseColor;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", baseColor);
            }

            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emissionColor);
            }
        }

        private IEnumerator GhostRoutine(float duration)
        {
            float safeDuration = Mathf.Max(0.4f, duration);
            float fadeStart = safeDuration * 0.6f;
            float elapsed = 0f;

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;

                if (ghostMaterial != null && elapsed >= fadeStart)
                {
                    float t = Mathf.Clamp01((elapsed - fadeStart) / (safeDuration - fadeStart));
                    Color baseColor = ghostBaseColor;
                    baseColor.a = Mathf.Lerp(ghostBaseColor.a, 0f, t);
                    ghostMaterial.color = baseColor;
                    if (ghostMaterial.HasProperty("_BaseColor"))
                    {
                        ghostMaterial.SetColor("_BaseColor", baseColor);
                    }

                    if (ghostMaterial.HasProperty("_EmissionColor"))
                    {
                        ghostMaterial.SetColor("_EmissionColor", ghostEmissionColor * (1f - t * 0.7f));
                    }

                    if (ghostText != null)
                    {
                        ghostText.color = Color.Lerp(ghostTextBaseColor, new Color(ghostTextBaseColor.r, ghostTextBaseColor.g, ghostTextBaseColor.b, 0f), t);
                    }
                }

                yield return null;
            }

            if (ghostOutline != null)
            {
                ghostOutline.SetActive(false);
            }

            if (ghostText != null)
            {
                ghostText.gameObject.SetActive(false);
            }

            ghostRoutine = null;
        }

        private IEnumerator FlashRoutine(Color flashColor)
        {
            if (runtimeMaterials == null) yield break;

            for (int i = 0; i < runtimeMaterials.Length; i++)
            {
                runtimeMaterials[i].SetColor("_BaseColor", flashColor);
                runtimeMaterials[i].color = flashColor;
            }

            yield return new WaitForSeconds(0.28f);
            suggested = false;
        }

        private void OnDestroy()
        {
            // Clean up
        }
    }
}