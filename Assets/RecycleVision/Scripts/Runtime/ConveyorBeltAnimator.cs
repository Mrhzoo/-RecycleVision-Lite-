using UnityEngine;

namespace RecycleVision
{
    public class ConveyorBeltAnimator : MonoBehaviour
    {
        public Renderer beltRenderer;
        public Vector2 scrollDirection = new Vector2(0f, -1f);
        public float scrollSpeed = 0.35f;
        public float speedMultiplier = 1f;
        public bool generateStripeTexture = true;

        private Material beltMaterial;
        private int texturePropertyId;

        private void Awake()
        {
            if (beltRenderer == null)
            {
                beltRenderer = GetComponent<Renderer>();
            }

            if (beltRenderer == null)
            {
                return;
            }

            beltMaterial = beltRenderer.material;
            if (beltMaterial == null)
            {
                return;
            }

            texturePropertyId = beltMaterial.HasProperty("_BaseMap")
                ? Shader.PropertyToID("_BaseMap")
                : Shader.PropertyToID("_MainTex");

            if (generateStripeTexture)
            {
                EnsureStripeTexture();
            }
        }

        private void Update()
        {
            if (beltMaterial == null)
            {
                return;
            }

            Vector2 direction = scrollDirection.sqrMagnitude > 0.001f
                ? scrollDirection.normalized
                : Vector2.up;

            Vector2 offset = beltMaterial.GetTextureOffset(texturePropertyId);
            offset += direction * (scrollSpeed * speedMultiplier) * Time.deltaTime;
            offset.x = Mathf.Repeat(offset.x, 1f);
            offset.y = Mathf.Repeat(offset.y, 1f);
            beltMaterial.SetTextureOffset(texturePropertyId, offset);
        }

        public void SetSpeedMultiplier(float multiplier)
        {
            speedMultiplier = Mathf.Max(0f, multiplier);
        }

        private void EnsureStripeTexture()
        {
            Texture existing = beltMaterial.GetTexture(texturePropertyId);
            if (existing != null)
            {
                return;
            }

            Texture2D stripe = BuildStripeTexture(128, 16);
            stripe.wrapMode = TextureWrapMode.Repeat;
            stripe.filterMode = FilterMode.Bilinear;
            stripe.Apply();
            stripe.hideFlags = HideFlags.HideAndDontSave;
            beltMaterial.SetTexture(texturePropertyId, stripe);
        }

        private static Texture2D BuildStripeTexture(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color dark = new Color(0.08f, 0.1f, 0.14f, 1f);
            Color light = new Color(0.12f, 0.15f, 0.2f, 1f);
            int stripeWidth = Mathf.Max(2, width / 8);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isLight = (x / stripeWidth) % 2 == 0;
                    texture.SetPixel(x, y, isLight ? light : dark);
                }
            }

            return texture;
        }
    }
}
