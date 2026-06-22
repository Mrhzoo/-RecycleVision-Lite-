using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RecycleVision.Editor
{
    public static class FactoryPrefabMaterialFixer
    {
        private const string SourceRoot = "Assets/UnityFactorySceneHDRP/Scene_Factory";
        private const string FactoryReadyRoot = "Assets/RecycleVision/Prefabs/FactoryReady";
        private const string GeneratedFolder = "Assets/RecycleVision/Generated";
        private const string MaterialFolder = "Assets/RecycleVision/Generated/FactoryMaterials";

        [MenuItem("Tools/RecycleVision/Fix Factory Prefab Materials")]
        public static void FixFactoryPrefabMaterials()
        {
            FixFactoryPrefabMaterialsInFolders(SourceRoot, FactoryReadyRoot);
        }

        public static void FixFactoryPrefabMaterialsInFolders(params string[] roots)
        {
            EnsureFolders();

            List<string> validRoots = new List<string>();
            foreach (string root in roots)
            {
                if (!string.IsNullOrWhiteSpace(root) && AssetDatabase.IsValidFolder(root))
                {
                    validRoots.Add(root);
                }
            }

            if (validRoots.Count == 0)
            {
                Debug.Log("RecycleVision: No valid prefab folders found for material fix.");
                return;
            }

            HashSet<string> prefabGuids = new HashSet<string>();
            foreach (string root in validRoots)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
                {
                    prefabGuids.Add(guid);
                }
            }

            Dictionary<string, Material> materialCache = new Dictionary<string, Material>();
            int updatedPrefabs = 0;

            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                bool changed = false;

                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null)
                    {
                        continue;
                    }

                    Material[] materials = renderer.sharedMaterials;
                    if (materials == null || materials.Length == 0)
                    {
                        continue;
                    }

                    for (int i = 0; i < materials.Length; i++)
                    {
                        Material source = materials[i];
                        if (source == null)
                        {
                            continue;
                        }

                        if (IsUrpCompatible(source))
                        {
                            if (NeedsUrpRepair(source))
                            {
                                RepairUrpMaterial(source);
                                changed = true;
                            }

                            continue;
                        }

                        materials[i] = ConvertMaterial(source, materialCache);
                        changed = true;
                    }

                    if (changed)
                    {
                        renderer.sharedMaterials = materials;
                    }
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    updatedPrefabs++;
                }

                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"RecycleVision: Updated {updatedPrefabs} prefabs with URP-compatible materials.");
        }

        private static bool IsUrpCompatible(Material material)
        {
            if (material.shader == null)
            {
                return false;
            }

            string shaderName = material.shader.name;
            return shaderName.StartsWith("Universal Render Pipeline/");
        }

        private static Material ConvertMaterial(Material source, Dictionary<string, Material> cache)
        {
            string key = GetMaterialKey(source);
            if (cache.TryGetValue(key, out Material cached) && cached != null)
            {
                return cached;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = LoadExistingUrpMaterial(source);
            if (material == null)
            {
                material = new Material(shader);
            }
            else
            {
                material.shader = shader;
            }

            Color baseColor = GetBaseColor(source);
            material.color = baseColor;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", baseColor);
            }

            Texture baseMap = GetBaseMap(source);
            material.mainTexture = baseMap;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", baseMap);
            }

            float metallic = GetFloat(source, "_Metallic", 0f);
            float smoothness = GetFloat(source, "_Smoothness", GetFloat(source, "_Glossiness", 0.5f));
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            Color emission = GetEmissionColor(source);
            if (material.HasProperty("_EmissionColor"))
            {
                if (emission.maxColorComponent > 0.001f)
                {
                    material.EnableKeyword("_EMISSION");
                }
                else
                {
                    material.DisableKeyword("_EMISSION");
                }

                material.SetColor("_EmissionColor", emission);
            }

            if (ShouldBeTransparent(source, baseColor))
            {
                SetTransparent(material);
            }
            else
            {
                SetOpaque(material);
            }

            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
            {
                string materialPath = GetMaterialAssetPath(source, false);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            cache[key] = material;
            return material;
        }

        private static string GetMaterialKey(Material source)
        {
            string assetPath = AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrEmpty(assetPath))
            {
                return assetPath;
            }

            return source.name;
        }

        private static string GetMaterialAssetPath(Material source)
        {
            return GetMaterialAssetPath(source, false);
        }

        private static string GetMaterialAssetPath(Material source, bool allowExisting)
        {
            string assetPath = AssetDatabase.GetAssetPath(source);
            string guid = !string.IsNullOrEmpty(assetPath)
                ? AssetDatabase.AssetPathToGUID(assetPath)
                : System.Guid.NewGuid().ToString("N");

            string safeName = Sanitize(source.name);
            string fileName = $"{safeName}_{guid.Substring(0, 8)}_URP.mat";
            string fullPath = $"{MaterialFolder}/{fileName}";
            if (allowExisting && File.Exists(fullPath))
            {
                return fullPath;
            }

            return AssetDatabase.GenerateUniqueAssetPath(fullPath);
        }

        private static Material LoadExistingUrpMaterial(Material source)
        {
            string materialPath = GetMaterialAssetPath(source, true);
            if (string.IsNullOrWhiteSpace(materialPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        }

        private static Color GetBaseColor(Material source)
        {
            if (source.HasProperty("_BaseColor"))
            {
                return source.GetColor("_BaseColor");
            }

            if (source.HasProperty("_Color"))
            {
                return source.GetColor("_Color");
            }

            return source.color;
        }

        private static Texture GetBaseMap(Material source)
        {
            if (source.HasProperty("_BaseColorMap"))
            {
                return source.GetTexture("_BaseColorMap");
            }

            if (source.HasProperty("_BaseMap"))
            {
                return source.GetTexture("_BaseMap");
            }

            if (source.HasProperty("_MainTex"))
            {
                return source.GetTexture("_MainTex");
            }

            return source.mainTexture;
        }

        private static bool NeedsUrpRepair(Material material)
        {
            string path = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(MaterialFolder))
            {
                return false;
            }

            Color baseColor = GetBaseColor(material);
            Texture baseMap = GetBaseMap(material);
            bool hasBaseMap = baseMap != null;
            bool colorIsZero = baseColor.maxColorComponent <= 0.01f;
            bool alphaIsZero = baseColor.a <= 0.01f;

            return hasBaseMap && (colorIsZero || alphaIsZero);
        }

        private static void RepairUrpMaterial(Material material)
        {
            Color baseColor = GetBaseColor(material);
            if (baseColor.maxColorComponent <= 0.01f)
            {
                baseColor = Color.white;
            }

            baseColor.a = 1f;
            material.color = baseColor;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", baseColor);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", baseColor);
            }

            SetOpaque(material);
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            EditorUtility.SetDirty(material);
        }

        private static float GetFloat(Material source, string property, float fallback)
        {
            return source.HasProperty(property) ? source.GetFloat(property) : fallback;
        }

        private static Color GetEmissionColor(Material source)
        {
            if (source.HasProperty("_EmissiveColor"))
            {
                return source.GetColor("_EmissiveColor");
            }

            if (source.HasProperty("_EmissionColor"))
            {
                return source.GetColor("_EmissionColor");
            }

            return Color.black;
        }

        private static bool ShouldBeTransparent(Material source, Color baseColor)
        {
            if (baseColor.a < 0.98f)
            {
                return true;
            }

            if (source.shader != null)
            {
                string shaderName = source.shader.name;
                if (!string.IsNullOrEmpty(shaderName)
                    && (shaderName.Contains("Transparent") || shaderName.Contains("Glass")))
                {
                    return true;
                }
            }

            if (source.HasProperty("_SurfaceType") && source.GetFloat("_SurfaceType") > 0.5f)
            {
                return true;
            }

            return false;
        }

        private static void SetTransparent(Material material)
        {
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        private static void SetOpaque(Material material)
        {
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/RecycleVision")) AssetDatabase.CreateFolder("Assets", "RecycleVision");
            if (!AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.CreateFolder("Assets/RecycleVision", "Generated");
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder(GeneratedFolder, "FactoryMaterials");
        }

        private static string Sanitize(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Replace(" ", "_");
        }
    }
}
