using UnityEngine;

namespace RecycleVision
{
    public class LocalPositionOscillator : MonoBehaviour
    {
        [SerializeField] private Vector3 amplitude = new Vector3(0f, 0.25f, 0f);
        [SerializeField] private Vector3 frequency = new Vector3(0f, 1f, 0f);
        [SerializeField] private Vector3 phase;
        [SerializeField] private bool useUnscaledTime;

        private Vector3 startLocalPosition;

        private void OnEnable()
        {
            startLocalPosition = transform.localPosition;
        }

        private void Update()
        {
            float time = useUnscaledTime ? Time.unscaledTime : Time.time;
            Vector3 offset = new Vector3(
                amplitude.x * Mathf.Sin((time + phase.x) * frequency.x),
                amplitude.y * Mathf.Sin((time + phase.y) * frequency.y),
                amplitude.z * Mathf.Sin((time + phase.z) * frequency.z));

            transform.localPosition = startLocalPosition + offset;
        }

        public void Configure(Vector3 newAmplitude, Vector3 newFrequency, Vector3 newPhase, bool newUseUnscaledTime)
        {
            amplitude = newAmplitude;
            frequency = newFrequency;
            phase = newPhase;
            useUnscaledTime = newUseUnscaledTime;
        }
    }
}
