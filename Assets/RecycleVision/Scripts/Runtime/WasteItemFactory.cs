using System.Collections.Generic;
using UnityEngine;

namespace RecycleVision
{
    public static class WasteItemFactory
    {
        private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Material> CompatibleMaterialCache = new Dictionary<string, Material>();
        private const float TargetMaxDimension = 0.45f;
        private const float MinAllowedDimension = 0.24f;
        private const float MaxAllowedDimension = 0.65f;

        public static WasteItem Create(Transform parent, WasteItemDefinition definition)
        {
            GameObject root = new GameObject(definition.DisplayName);
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * definition.Scale;

            if (definition.VisualPrefab != null)
                BuildPrefabVisuals(root.transform, definition);
            else
                BuildVisuals(root.transform, definition);

            // Remove ALL existing colliders to prevent interference
            RemoveAllColliders(root);

            // Create a proper collider based on bounds
            EnsureCollider(root);

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 0.35f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = 0.5f;       // Add some drag so items settle faster
            body.angularDamping = 0.5f;

            WasteItem item = root.AddComponent<WasteItem>();
            item.Initialize(definition, body);
            return item;
        }

        private static void RemoveAllColliders(GameObject root)
        {
            Collider[] allColliders = root.GetComponentsInChildren<Collider>(true);
            foreach (Collider c in allColliders)
            {
                RemoveCollider(c);
            }
        }

        private static void BuildPrefabVisuals(Transform root, WasteItemDefinition definition)
        {
            GameObject visual = Object.Instantiate(definition.VisualPrefab, root);
            visual.name = "Visual";
            visual.transform.localPosition = definition.VisualPrefabOffset;
            visual.transform.localRotation = Quaternion.Euler(definition.VisualPrefabEuler);
            visual.transform.localScale = definition.VisualPrefabScale;
            RemoveNestedRigidbodies(visual.transform);
            RemoveNestedColliders(visual.transform);
            ApplyCompatibleMaterials(visual.transform, definition);
            NormalizeVisualScale(visual.transform);
        }

        private static void NormalizeVisualScale(Transform visualRoot)
        {
            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (maxDimension <= 0.0001f) return;

            if (maxDimension < MinAllowedDimension || maxDimension > MaxAllowedDimension)
            {
                float scaleFactor = TargetMaxDimension / maxDimension;
                visualRoot.localScale *= scaleFactor;
            }
        }

        private static void ApplyCompatibleMaterials(Transform root, WasteItemDefinition definition)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Material primaryFallback = GetMaterial(definition.PrimaryColor);
            Material accentFallback = GetMaterial(definition.AccentColor);

            for (int ri = 0; ri < renderers.Length; ri++)
            {
                Renderer renderer = renderers[ri];
                Material[] sourceMaterials = renderer.sharedMaterials;

                if (sourceMaterials == null || sourceMaterials.Length == 0)
                {
                    renderer.sharedMaterial = ri == 0 ? primaryFallback : accentFallback;
                    renderer.receiveShadows = true;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    continue;
                }

                Material[] resolvedMaterials = new Material[sourceMaterials.Length];
                bool changed = false;

                for (int mi = 0; mi < sourceMaterials.Length; mi++)
                {
                    Material src = sourceMaterials[mi];
                    if (UsesUniversalRenderPipelineShader(src))
                        resolvedMaterials[mi] = src;
                    else
                    {
                        resolvedMaterials[mi] = BuildCompatibleMaterial(src, mi == 0 ? definition.PrimaryColor : definition.AccentColor);
                        changed = true;
                    }
                }

                if (changed)
                    renderer.sharedMaterials = resolvedMaterials;

                renderer.receiveShadows = true;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        private static Material BuildCompatibleMaterial(Material sourceMaterial, Color fallbackColor)
        {
            if (sourceMaterial == null)
                return GetMaterial(fallbackColor);
            if (UsesUniversalRenderPipelineShader(sourceMaterial))
                return sourceMaterial;

            string textureName = sourceMaterial.mainTexture != null ? sourceMaterial.mainTexture.name : "NoTexture";
            string cacheKey = $"{sourceMaterial.GetInstanceID()}_{sourceMaterial.name}_{textureName}";

            if (CompatibleMaterialCache.TryGetValue(cacheKey, out Material cached))
                return cached;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader);

            Color color = sourceMaterial.HasProperty("_BaseColor") ? sourceMaterial.GetColor("_BaseColor") : sourceMaterial.color;
            if (color.a <= 0f) color = fallbackColor;

            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            if (sourceMaterial.mainTexture != null)
            {
                material.mainTexture = sourceMaterial.mainTexture;
                material.mainTextureScale = sourceMaterial.mainTextureScale;
                material.mainTextureOffset = sourceMaterial.mainTextureOffset;
                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", sourceMaterial.mainTexture);
                    material.SetTextureScale("_BaseMap", sourceMaterial.mainTextureScale);
                    material.SetTextureOffset("_BaseMap", sourceMaterial.mainTextureOffset);
                }
            }

            CopyFloatProperty(sourceMaterial, material, "_Metallic");
            CopyFloatProperty(sourceMaterial, material, "_Smoothness");
            CopyFloatProperty(sourceMaterial, material, "_Glossiness");

            if (sourceMaterial.HasProperty("_EmissionColor") && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", sourceMaterial.GetColor("_EmissionColor"));
            }

            CompatibleMaterialCache[cacheKey] = material;
            return material;
        }

        private static bool UsesUniversalRenderPipelineShader(Material material)
        {
            return material != null && material.shader != null && material.shader.name.StartsWith("Universal Render Pipeline/");
        }

        private static void CopyFloatProperty(Material src, Material dst, string name)
        {
            if (src.HasProperty(name) && dst.HasProperty(name))
                dst.SetFloat(name, src.GetFloat(name));
        }

        private static void BuildVisuals(Transform root, WasteItemDefinition definition)
        {
            switch (definition.VisualShape)
            {
                case WasteVisualShape.Bottle:
                    CreatePart(root, PrimitiveType.Capsule, new Vector3(0f, 0.32f, 0f), new Vector3(0.28f, 0.62f, 0.28f), definition.PrimaryColor);
                    CreatePart(root, PrimitiveType.Cylinder, new Vector3(0f, 0.71f, 0f), new Vector3(0.09f, 0.09f, 0.09f), definition.AccentColor);
                    break;
                case WasteVisualShape.Box:
                    CreatePart(root, PrimitiveType.Cube, new Vector3(0f, 0.22f, 0f), new Vector3(0.45f, 0.44f, 0.3f), definition.PrimaryColor);
                    CreatePart(root, PrimitiveType.Cube, new Vector3(0f, 0.48f, 0f), new Vector3(0.38f, 0.08f, 0.26f), definition.AccentColor);
                    break;
                case WasteVisualShape.Jar:
                    CreatePart(root, PrimitiveType.Cylinder, new Vector3(0f, 0.24f, 0f), new Vector3(0.34f, 0.42f, 0.34f), definition.PrimaryColor);
                    CreatePart(root, PrimitiveType.Cylinder, new Vector3(0f, 0.55f, 0f), new Vector3(0.26f, 0.09f, 0.26f), definition.AccentColor);
                    break;
                case WasteVisualShape.FlatPack:
                    CreatePart(root, PrimitiveType.Cube, new Vector3(0f, 0.08f, 0f), new Vector3(0.56f, 0.1f, 0.38f), definition.PrimaryColor);
                    CreatePart(root, PrimitiveType.Cube, new Vector3(0f, 0.14f, 0f), new Vector3(0.5f, 0.02f, 0.34f), definition.AccentColor);
                    break;
                case WasteVisualShape.Scrap:
                    CreatePart(root, PrimitiveType.Sphere, new Vector3(-0.12f, 0.12f, 0f), new Vector3(0.22f, 0.22f, 0.22f), definition.PrimaryColor);
                    CreatePart(root, PrimitiveType.Sphere, new Vector3(0.08f, 0.16f, -0.04f), new Vector3(0.18f, 0.18f, 0.18f), definition.AccentColor);
                    CreatePart(root, PrimitiveType.Sphere, new Vector3(0f, 0.05f, 0.1f), new Vector3(0.14f, 0.14f, 0.14f), definition.PrimaryColor);
                    break;
                case WasteVisualShape.Cup:
                    CreatePart(root, PrimitiveType.Cylinder, new Vector3(0f, 0.22f, 0f), new Vector3(0.28f, 0.4f, 0.28f), definition.PrimaryColor);
                    CreatePart(root, PrimitiveType.Cylinder, new Vector3(0f, 0.49f, 0f), new Vector3(0.31f, 0.03f, 0.31f), definition.AccentColor);
                    break;
            }
        }

        private static void CreatePart(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = type.ToString();
            part.transform.SetParent(parent, false);
            part.transform.localPosition = pos;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = scale;

            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetMaterial(color);

            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(collider);
                else
                    Object.DestroyImmediate(collider);
            }
        }

        private static Material GetMaterial(Color color)
        {
            string key = ColorUtility.ToHtmlStringRGBA(color);
            if (MaterialCache.TryGetValue(key, out Material cached))
                return cached;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader);
            material.color = color;
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0.1f);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.55f);
            MaterialCache[key] = material;
            return material;
        }

        private static void RemoveNestedRigidbodies(Transform root)
        {
            foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = false;
                DestroyComponent(rb);
            }
        }

        private static void RemoveNestedColliders(Transform root)
        {
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
            {
                RemoveCollider(c);
            }
        }

        private static void RemoveCollider(Collider collider)
        {
            if (collider == null) return;

            collider.enabled = false;

            if (collider is MeshCollider meshCollider && !meshCollider.convex)
            {
                meshCollider.convex = true;
            }

            DestroyComponent(collider);
        }

        private static void DestroyComponent(Object component)
        {
            if (component == null) return;

            if (Application.isPlaying)
                Object.Destroy(component);
            else
                Object.DestroyImmediate(component);
        }

        private static void EnsureCollider(GameObject root)
        {
            if (root.GetComponent<Collider>() != null) return;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                SphereCollider fc = root.AddComponent<SphereCollider>();
                fc.radius = 0.22f;
                fc.center = new Vector3(0f, 0.22f, 0f);
                return;
            }

            // Calculate bounds in WORLD space then convert to LOCAL space properly
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            // Add a BoxCollider with bounds calculated in local space
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(bounds.center);

            // Calculate size in local space using the transform matrix
            Vector3 localMin = root.transform.InverseTransformPoint(bounds.min);
            Vector3 localMax = root.transform.InverseTransformPoint(bounds.max);
            box.size = localMax - localMin;

            // Add a small margin so collider fully encloses the mesh
            box.size += Vector3.one * 0.01f;
        }
    }
}
