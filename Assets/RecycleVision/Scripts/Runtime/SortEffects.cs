using System.Collections;
using UnityEngine;

namespace RecycleVision
{
    /// <summary>
    /// Handles particle effects and sound for sorting feedback.
    /// Attached at runtime by SortingStationManager.
    /// </summary>
    public class SortEffects : MonoBehaviour
    {
        public AudioSource audioSource;

        // Procedural audio clips
        private AudioClip correctClip;
        private AudioClip wrongClip;

        // Particle system references
        private ParticleSystem correctParticles;
        private ParticleSystem wrongParticles;

        // Streak tracking
        public TextMesh streakText;
        private int streakCount;
        private Coroutine streakFadeRoutine;

        private void Awake()
        {
            // Generate procedural beep sounds
            correctClip = CreateBeepClip(880f, 0.15f, 0.3f);  // A5
            wrongClip = CreateBeepClip(220f, 0.2f, 0.25f);   // A3

            // Ensure AudioSource
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.volume = 0.5f;
            audioSource.spatialBlend = 0f; // 2D

            // Create particle systems
            correctParticles = CreateParticleSystem("CorrectBurst", new Color(0.3f, 0.95f, 0.5f), 20);
            wrongParticles = CreateParticleSystem("WrongBurst", new Color(1f, 0.4f, 0.3f), 15);

            // Create streak text (floating 3D text)
            CreateStreakText();
        }

        private AudioClip CreateBeepClip(float frequency, float duration, float volume)
        {
            int sampleRate = 44100;
            int samples = (int)(sampleRate * duration);
            float[] waveform = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                // Sine wave with fade-in/out envelope
                float envelope = Mathf.Min(1f, t * 20f) * Mathf.Min(1f, (duration - t) * 20f);
                waveform[i] = Mathf.Sin(t * frequency * Mathf.PI * 2f) * volume * envelope;
            }

            AudioClip clip = AudioClip.Create("Beep_" + frequency, samples, 1, sampleRate, false);
            clip.SetData(waveform, 0);
            return clip;
        }

        private ParticleSystem CreateParticleSystem(string name, Color color, int count)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startColor = color;
            main.startSize = 0.15f;
            main.startLifetime = 0.6f;
            main.maxParticles = count;
            main.duration = 0.3f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.burstCount = 1;
            emission.SetBurst(0, new ParticleSystem.Burst(0, count));

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            renderer.material.color = color;
            renderer.material.EnableKeyword("_EMISSION");
            renderer.material.SetColor("_EmissionColor", color * 1.5f);

            return ps;
        }

        private void CreateStreakText()
        {
            GameObject go = new GameObject("StreakText");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.5f, 0f);

            streakText = go.AddComponent<TextMesh>();
            streakText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            streakText.fontSize = 128;
            streakText.characterSize = 0.04f;
            streakText.anchor = TextAnchor.MiddleCenter;
            streakText.alignment = TextAlignment.Center;
            streakText.color = new Color(0.94f, 0.78f, 0.26f);
            streakText.text = "";
        }

        public void PlayCorrectEffect(Vector3 worldPosition)
        {
            // Sound
            if (audioSource != null && correctClip != null)
                audioSource.PlayOneShot(correctClip);

            // Particles at position
            if (correctParticles != null)
            {
                correctParticles.transform.position = worldPosition;
                correctParticles.Play();
            }

            // Streak
            streakCount++;
            ShowStreak($"🔥 {streakCount} in a row!");
        }

        public void PlayWrongEffect(Vector3 worldPosition)
        {
            if (audioSource != null && wrongClip != null)
                audioSource.PlayOneShot(wrongClip);

            if (wrongParticles != null)
            {
                wrongParticles.transform.position = worldPosition;
                wrongParticles.Play();
            }

            // Reset streak
            streakCount = 0;
            if (streakText != null)
            {
                streakText.text = "";
                streakText.gameObject.SetActive(false);
            }
        }

        public void ResetStreak()
        {
            streakCount = 0;
            if (streakFadeRoutine != null)
            {
                StopCoroutine(streakFadeRoutine);
                streakFadeRoutine = null;
            }

            if (streakText != null)
            {
                streakText.text = "";
                streakText.gameObject.SetActive(false);
            }
        }

        private void ShowStreak(string message)
        {
            if (streakText == null) return;
            streakText.gameObject.SetActive(true);
            streakText.text = message;

            if (streakFadeRoutine != null)
                StopCoroutine(streakFadeRoutine);
            streakFadeRoutine = StartCoroutine(FadeStreak());
        }

        private IEnumerator FadeStreak()
        {
            float duration = 1.5f;
            float elapsed = 0f;
            Color c = streakText.color;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                streakText.color = new Color(c.r, c.g, c.b, 1f - t * 0.7f);
                streakText.transform.localPosition = new Vector3(0f, 1.5f + t * 0.3f, 0f);
                yield return null;
            }

            streakText.text = "";
            streakText.gameObject.SetActive(false);
            streakText.color = c;
        }
    }
}