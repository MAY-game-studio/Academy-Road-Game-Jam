using System;
using UnityEngine;

namespace Sunbearer
{
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class IslandSoftLight : MonoBehaviour
    {
        [Serializable]
        private struct IslandProfile
        {
            [Min(0)] public int ShorePath;
            public Color Tint;
            [Range(0f, 1f)] public float Strength;
            [Range(0f, 1f)] public float Threshold;
            [Range(0.25f, 3f)] public float Spread;
        }

        [SerializeField] private Shader shader;
        [SerializeField, Min(0.01f)] private float transitionSeconds = 1.5f;
        [SerializeField] private IslandProfile[] islandProfiles = new IslandProfile[0];

        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
        private static readonly int BlurDirectionId = Shader.PropertyToID("_BlurDirection");
        private static readonly int GlowTextureId = Shader.PropertyToID("_GlowTex");

        private Material material;
        private Transform player;
        private PolygonCollider2D coastline;
        private Vector2[][] shores;
        private IslandProfile current;
        private IslandProfile transitionFrom;
        private int targetIsland;
        private float transitionProgress;
        private int previewIsland = -1;
        private bool boostPreview;
        private bool previewBypassed;

        private IslandProfile RenderProfile
        {
            get
            {
                IslandProfile profile = previewIsland >= 0 ? islandProfiles[previewIsland] : current;
                if (boostPreview)
                {
                    profile.Strength = Mathf.Clamp01(profile.Strength * 3f + 0.20f);
                    profile.Threshold *= 0.25f;
                    profile.Spread = Mathf.Min(3f, profile.Spread + 0.90f);
                }

                return profile;
            }
        }

        internal string PreviewStatus
        {
            get
            {
                if (!isActiveAndEnabled || !material)
                    return "Soft light unavailable";

                IslandProfile profile = RenderProfile;
                string mode = previewIsland >= 0 ? $"Island {profile.ShorePath}" : $"Auto {profile.ShorePath}";
                if (boostPreview)
                    mode += " Boost";
                if (previewBypassed)
                    mode += " (Off)";

                return $"Light {mode}  |  Strength {profile.Strength:0.00}  Cutoff {profile.Threshold:0.00}  Spread {profile.Spread:0.00}";
            }
        }

        internal void Initialize(Transform target, PolygonCollider2D sea)
        {
            if (!shader || !shader.isSupported || islandProfiles.Length == 0)
                return;

            player = target;
            coastline = sea;
            shores = new Vector2[islandProfiles.Length][];
            for (int i = 0; i < shores.Length; i++)
            {
                int path = islandProfiles[i].ShorePath;
                if (path >= 0 && path < coastline.pathCount)
                    shores[i] = coastline.GetPath(path);
            }

            targetIsland = Mathf.Max(0, FindIsland());
            current = transitionFrom = islandProfiles[targetIsland];
            transitionProgress = 1f;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private int FindIsland()
        {
            Vector2 local = (Vector2)coastline.transform.InverseTransformPoint(player.position) - coastline.offset;
            for (int i = 0; i < shores.Length; i++)
            {
                if (ArenaPhysics.ContainsPoint(shores[i], local))
                    return i;
            }

            return -1;
        }

        private void Update()
        {
            if (!material)
                return;

            if (Input.GetKeyDown(KeyCode.L))
                previewBypassed = !previewBypassed;

            if (NumberPressed(0))
            {
                previewIsland = -1;
                boostPreview = false;
                previewBypassed = false;
                return;
            }

            if (NumberPressed(4))
            {
                boostPreview = !boostPreview;
                previewBypassed = false;
                return;
            }

            int presetCount = Mathf.Min(3, islandProfiles.Length);
            for (int i = 0; i < presetCount; i++)
            {
                if (!NumberPressed(i + 1))
                    continue;

                previewIsland = i;
                previewBypassed = false;
                break;
            }
        }

        private static bool NumberPressed(int number)
        {
            return Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + number)) ||
                Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad0 + number));
        }

        private void LateUpdate()
        {
            if (!material || !player || !coastline)
                return;

            int island = FindIsland();
            if (island >= 0 && island != targetIsland)
            {
                transitionFrom = current;
                transitionProgress = 0f;
                targetIsland = island;
            }

            transitionProgress = Mathf.MoveTowards(transitionProgress, 1f,
                Time.deltaTime / Mathf.Max(0.01f, transitionSeconds));
            float blend = Mathf.SmoothStep(0f, 1f, transitionProgress);
            IslandProfile target = islandProfiles[targetIsland];
            current.ShorePath = target.ShorePath;
            current.Tint = Color.Lerp(transitionFrom.Tint, target.Tint, blend);
            current.Strength = Mathf.Lerp(transitionFrom.Strength, target.Strength, blend);
            current.Threshold = Mathf.Lerp(transitionFrom.Threshold, target.Threshold, blend);
            current.Spread = Mathf.Lerp(transitionFrom.Spread, target.Spread, blend);
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            IslandProfile profile = RenderProfile;
            if (!material || previewBypassed || profile.Strength <= 0f)
            {
                Graphics.Blit(source, destination);
                return;
            }

            RenderTextureDescriptor descriptor = source.descriptor;
            descriptor.width = Mathf.Max(1, source.width / 4);
            descriptor.height = Mathf.Max(1, source.height / 4);
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            RenderTexture glow = RenderTexture.GetTemporary(descriptor);
            RenderTexture scratch = null;
            try
            {
                scratch = RenderTexture.GetTemporary(descriptor);
                glow.filterMode = scratch.filterMode = FilterMode.Bilinear;
                glow.wrapMode = scratch.wrapMode = TextureWrapMode.Clamp;

                material.SetFloat(ThresholdId, profile.Threshold);
                Graphics.Blit(source, glow, material, 0);
                material.SetVector(BlurDirectionId, new Vector4(profile.Spread, 0f, 0f, 0f));
                Graphics.Blit(glow, scratch, material, 1);
                material.SetVector(BlurDirectionId, new Vector4(0f, profile.Spread, 0f, 0f));
                Graphics.Blit(scratch, glow, material, 1);

                material.SetColor(TintId, profile.Tint);
                material.SetFloat(StrengthId, profile.Strength);
                material.SetTexture(GlowTextureId, glow);
                Graphics.Blit(source, destination, material, 2);
            }
            finally
            {
                material.SetTexture(GlowTextureId, null);
                if (scratch)
                    RenderTexture.ReleaseTemporary(scratch);
                RenderTexture.ReleaseTemporary(glow);
            }
        }

        private void OnDestroy()
        {
            if (material)
                Destroy(material);
        }
    }
}
