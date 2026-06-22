using UnityEditor;
using UnityEngine;

namespace RecycleVision
{
    public static class FactoryMovablePreparer
    {
        private const string OutputDir = "Assets/RecycleVision/Prefabs/FactoryReady";
        private const string WorkerPrefabPath = "Assets/UnityFactorySceneHDRP/Scene_Factory/Movable/Prefabs/Worker.prefab";
        private const string WorkerModelPath = "Assets/UnityFactorySceneHDRP/Scene_Factory/Movable/Models/Worker.fbx";
        private const string ArmPrefabPath = "Assets/UnityFactorySceneHDRP/Scene_Factory/Movable/Prefabs/Arm.prefab";
        private const string ProductPrefabPath = "Assets/UnityFactorySceneHDRP/Scene_Factory/Movable/Prefabs/Product.prefab";

        [MenuItem("Tools/RecycleVision/Prepare Factory Movables")]
        public static void PrepareFactoryMovables()
        {
            EnsureFolder(OutputDir);

            PrepareWorker("Standing1", "Assets/UnityFactorySceneHDRP/Scene_Factory/AnimationSample/AnimatorController/Standing1.controller");
            PrepareWorker("Standing2", "Assets/UnityFactorySceneHDRP/Scene_Factory/AnimationSample/AnimatorController/Standing2.controller");
            PrepareWorker("Walk", "Assets/UnityFactorySceneHDRP/Scene_Factory/AnimationSample/AnimatorController/Walk.controller");
            PrepareWorker("Listening", "Assets/UnityFactorySceneHDRP/Scene_Factory/AnimationSample/AnimatorController/Listening.controller");
            PrepareWorker("UsingTablet", "Assets/UnityFactorySceneHDRP/Scene_Factory/AnimationSample/AnimatorController/UsingTablet.controller");

            PrepareArm();
            PrepareProduct();

            RecycleVision.Editor.FactoryPrefabMaterialFixer.FixFactoryPrefabMaterialsInFolders(OutputDir);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Factory movables prepared in " + OutputDir);
        }

        private static void PrepareWorker(string suffix, string controllerPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("Worker prefab not found at " + WorkerPrefabPath);
                return;
            }

            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            if (controller == null)
            {
                Debug.LogWarning("Animator controller not found at " + controllerPath);
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(WorkerPrefabPath);
            try
            {
                Transform animatorHost = FindAnimatorHost(root.transform);
                GameObject hostObject = animatorHost != null ? animatorHost.gameObject : root;

                Animator rootAnimator = root.GetComponent<Animator>();
                if (rootAnimator != null && rootAnimator.gameObject != hostObject)
                {
                    Object.DestroyImmediate(rootAnimator, true);
                }

                Animator animator = hostObject.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = hostObject.AddComponent<Animator>();
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                if (animator.avatar == null)
                {
                    animator.avatar = LoadAvatar(WorkerModelPath);
                }

                PrefabUtility.SaveAsPrefabAsset(root, OutputDir + "/Worker_" + suffix + ".prefab");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void PrepareArm()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArmPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("Arm prefab not found at " + ArmPrefabPath);
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(ArmPrefabPath);
            try
            {
                Transform target = FindChildByName(root.transform, "IK Target");
                if (target == null)
                {
                    Debug.LogWarning("IK Target not found in Arm prefab.");
                }
                else
                {
                    LocalPositionOscillator oscillator = target.GetComponent<LocalPositionOscillator>();
                    if (oscillator == null)
                    {
                        oscillator = target.gameObject.AddComponent<LocalPositionOscillator>();
                    }

                    oscillator.Configure(
                        new Vector3(0.15f, 0.35f, 0.15f),
                        new Vector3(0.9f, 1.1f, 0.8f),
                        new Vector3(0f, 0.4f, 1.1f),
                        false);
                }

                PrefabUtility.SaveAsPrefabAsset(root, OutputDir + "/Arm_Moving.prefab");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void PrepareProduct()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProductPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("Product prefab not found at " + ProductPrefabPath);
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(ProductPrefabPath);
            try
            {
                LocalPositionOscillator oscillator = root.GetComponent<LocalPositionOscillator>();
                if (oscillator == null)
                {
                    oscillator = root.AddComponent<LocalPositionOscillator>();
                }

                oscillator.Configure(
                    new Vector3(0f, 0f, 2f),
                    new Vector3(0f, 0f, 0.35f),
                    Vector3.zero,
                    false);

                PrefabUtility.SaveAsPrefabAsset(root, OutputDir + "/Product_Moving.prefab");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string[] parts = path.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static Transform FindAnimatorHost(Transform root)
        {
            if (root.childCount > 0)
            {
                return root.GetChild(0);
            }

            return root;
        }

        private static Avatar LoadAvatar(string modelPath)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
            foreach (Object asset in assets)
            {
                if (asset is Avatar avatar)
                {
                    return avatar;
                }
            }

            return null;
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
