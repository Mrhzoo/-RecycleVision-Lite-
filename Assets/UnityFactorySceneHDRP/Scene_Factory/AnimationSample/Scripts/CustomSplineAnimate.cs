using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;

namespace UnityFactorySceneHDRP
{
    [ExecuteAlways]
    public class CustomSplineAnimate : MonoBehaviour
    {
        [System.Serializable]
        private struct StopPoint
        {
            public float time;
            public float duration;
            public Animation robotArmAnimation;
        }

        [SerializeField, FormerlySerializedAs("_spline")] private Component splineSource;
        [SerializeField, FormerlySerializedAs("_duration")] private float duration = 8f;
        [SerializeField, FormerlySerializedAs("_startOffset")] private float startOffset;
        [SerializeField, FormerlySerializedAs("_stopPoints")] private StopPoint[] stopPoints;

        [Header("Preview")]
        [SerializeField, FormerlySerializedAs("_previewTime"), Range(0f, 1f)] private float previewTime;

        private Transform cachedTransform;
        private float currentTime;
        private MethodInfo evaluatePositionMethod;
        private MethodInfo evaluateTangentMethod;
        private bool methodsResolved;

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            cachedTransform = transform;
            currentTime += startOffset;
        }

        private void Start()
        {
            if (!Application.isPlaying || splineSource == null || duration <= 0f)
            {
                return;
            }

            StartCoroutine(Animate());
        }

        private IEnumerator Animate()
        {
            bool[] isPassed = new bool[stopPoints.Length];
            float stopOverrun = 0f;

            for (int index = 0; index < stopPoints.Length; index++)
            {
                if (currentTime > stopPoints[index].time && !isPassed[index])
                {
                    isPassed[index] = true;
                }
            }

            while (true)
            {
                for (int index = 0; index < stopPoints.Length; index++)
                {
                    if (currentTime <= stopPoints[index].time || isPassed[index])
                    {
                        continue;
                    }

                    isPassed[index] = true;
                    stopOverrun = currentTime - stopPoints[index].time;

                    if (stopPoints[index].robotArmAnimation != null)
                    {
                        stopPoints[index].robotArmAnimation.Play();
                    }

                    currentTime = stopPoints[index].time;
                    SetPositionAndRotation(currentTime);
                    yield return new WaitForSeconds(stopPoints[index].duration);
                }

                currentTime = currentTime + stopOverrun + (Time.deltaTime / duration);
                stopOverrun = 0f;

                if (currentTime > 1f)
                {
                    currentTime %= 1f;

                    for (int index = 0; index < isPassed.Length; index++)
                    {
                        isPassed[index] = false;
                    }
                }

                SetPositionAndRotation(currentTime);
                yield return null;
            }
        }

        private void SetPositionAndRotation(float time)
        {
            if (!TryEvaluateSpline(time, out Vector3 position, out Vector3 tangent))
            {
                return;
            }

            if (cachedTransform == null)
            {
                cachedTransform = transform;
            }

            cachedTransform.position = position;
            cachedTransform.rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);
        }

        private bool TryEvaluateSpline(float time, out Vector3 position, out Vector3 tangent)
        {
            position = Vector3.zero;
            tangent = Vector3.forward;

            if (splineSource == null)
            {
                return false;
            }

            if (!methodsResolved)
            {
                System.Type splineType = splineSource.GetType();
                evaluatePositionMethod = splineType.GetMethod("EvaluatePosition", new[] { typeof(float) });
                evaluateTangentMethod = splineType.GetMethod("EvaluateTangent", new[] { typeof(float) });
                methodsResolved = true;
            }

            if (evaluatePositionMethod == null || evaluateTangentMethod == null)
            {
                return false;
            }

            object positionValue = evaluatePositionMethod.Invoke(splineSource, new object[] { time });
            object tangentValue = evaluateTangentMethod.Invoke(splineSource, new object[] { time });

            position = ConvertToVector3(positionValue);
            tangent = ConvertToVector3(tangentValue);
            return tangent.sqrMagnitude > 0.0001f;
        }

        private static Vector3 ConvertToVector3(object value)
        {
            if (value is Vector3 vector)
            {
                return vector;
            }

            if (value == null)
            {
                return Vector3.forward;
            }

            System.Type valueType = value.GetType();
            FieldInfo xField = valueType.GetField("x");
            FieldInfo yField = valueType.GetField("y");
            FieldInfo zField = valueType.GetField("z");

            if (xField == null || yField == null || zField == null)
            {
                return Vector3.forward;
            }

            return new Vector3(
                (float)xField.GetValue(value),
                (float)yField.GetValue(value),
                (float)zField.GetValue(value));
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (Application.isPlaying || splineSource == null)
            {
                return;
            }

            if (cachedTransform == null)
            {
                cachedTransform = transform;
            }

            SetPositionAndRotation(previewTime);
        }
#endif
    }
}
