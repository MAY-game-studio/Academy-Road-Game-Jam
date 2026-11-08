using System.Collections.Generic;
using UnityEngine;

namespace Sunbearer
{
    public sealed class StardustSpawner : MonoBehaviour
    {
        [Header("Population")]
        [SerializeField, Min(0.1f)] private float spawnInterval = 2f;
        [SerializeField, Min(1f)] private float regionSize = 5f;
        [SerializeField, Min(0.001f)] private float targetPerSquareUnit = 1f / 12f;
        [SerializeField, Range(0f, 1f)] private float initialFill = 0.65f;
        [SerializeField, Min(1f)] private float screenWeight = 1.25f;
        [SerializeField, Min(1f)] private float depletedWeight = 1.5f;

        [Header("Placement and pickup")]
        [SerializeField, Min(0.05f)] private float terrainClearance = 0.16f;
        [SerializeField, Min(0.1f)] private float minimumSpacing = 0.8f;
        [SerializeField, Min(0.1f)] private float pickupRadius = 1f;
        [SerializeField, Range(0.01f, 1f)] private float chargePerStardust = 0.08f;

        private const int SamplesPerAxis = 6;
        private const int PlacementAttempts = 10;
        private const int PlacementObstacles = ArenaPhysics.GroundObstacles | ArenaPhysics.TargetMask;
        private const float StardustSize = 0.15f;
        private const float StardustHalfDiagonal = StardustSize * 0.707107f;

        private sealed class Region
        {
            public Rect Bounds;
            public float LandArea;
            public float Target;
            public int Count;
        }

        private struct Stardust
        {
            public Vector2 Position;
            public Region Region;
            public readonly GameObject View;

            public Stardust(Vector2 position, Region region, GameObject view)
            {
                Position = position;
                Region = region;
                View = view;
            }
        }

        private readonly List<Region> regions = new List<Region>();
        private readonly List<Stardust> active = new List<Stardust>();
        private Camera viewCamera;
        private PlayerController player;
        private float nextSpawn;

        public int Count => active.Count;
        public int Capacity { get; private set; }

        internal void Initialize(Rect map, Camera camera, PlayerController controller)
        {
            viewCamera = camera;
            player = controller;
            float land = BuildRegions(map);
            Capacity = land > 0f ? Mathf.Max(1, Mathf.RoundToInt(land * targetPerSquareUnit)) : 0;

            int startCount = Mathf.RoundToInt(Capacity * initialFill);
            for (int i = 0; i < startCount; i++)
            {
                if (!TrySpawn(false))
                    break;
            }

            nextSpawn = Time.time + spawnInterval;
        }

        private void Update()
        {
            if (!player)
                return;

            CollectNearby();
            if (Time.time >= nextSpawn)
            {
                TrySpawn(true);
                nextSpawn = Time.time + spawnInterval;
            }
        }

        private float BuildRegions(Rect map)
        {
            float totalLand = 0f;
            for (float x = map.xMin; x < map.xMax; x += regionSize)
            {
                for (float y = map.yMin; y < map.yMax; y += regionSize)
                {
                    var bounds = new Rect(x, y, Mathf.Min(regionSize, map.xMax - x), Mathf.Min(regionSize, map.yMax - y));
                    int clear = 0;
                    for (int ix = 0; ix < SamplesPerAxis; ix++)
                    {
                        for (int iy = 0; iy < SamplesPerAxis; iy++)
                        {
                            var sample = new Vector2(
                                bounds.xMin + (ix + 0.5f) * bounds.width / SamplesPerAxis,
                                bounds.yMin + (iy + 0.5f) * bounds.height / SamplesPerAxis);
                            if (!Physics2D.OverlapCircle(sample, terrainClearance, ArenaPhysics.GroundObstacles))
                                clear++;
                        }
                    }

                    if (clear == 0)
                        continue;

                    float landArea = bounds.width * bounds.height * clear / (SamplesPerAxis * SamplesPerAxis);
                    regions.Add(new Region { Bounds = bounds, LandArea = landArea, Target = landArea * targetPerSquareUnit });
                    totalLand += landArea;
                }
            }

            return totalLand;
        }

        private void CollectNearby()
        {
            if (!player.CanCollectStardust)
                return;

            Vector2 position = player.transform.position;
            float radiusSquared = pickupRadius * pickupRadius;
            bool hasRollingBall = player.TryGetRollingBall(out Vector2 ballCenter, out float ballRadius);
            float ballReach = ballRadius + StardustHalfDiagonal;
            float attractionRadius = Mathf.Max(ballReach, player.StardustAttractionRadius);
            float attractionRadiusSquared = attractionRadius * attractionRadius;
            float ballContact = ballReach + 0.001f;
            float pullStep = player.StardustPullSpeed * Time.deltaTime;
            for (int i = active.Count - 1; i >= 0 && player.CanCollectStardust; i--)
            {
                Stardust dust = active[i];
                bool attracted = hasRollingBall && (dust.Position - ballCenter).sqrMagnitude <= attractionRadiusSquared;
                if (attracted)
                {
                    PullTowardBall(ref dust, ballCenter, ballReach, attractionRadius, pullStep);
                    active[i] = dust;
                }

                Vector2 collector = attracted ? ballCenter : position;
                float reachSquared = attracted ? ballContact * ballContact : radiusSquared;
                if ((dust.Position - collector).sqrMagnitude > reachSquared || !player.TryCollect(chargePerStardust))
                    continue;

                dust.Region.Count--;
                active.RemoveAt(i);
                Destroy(dust.View);
            }
        }

        private void PullTowardBall(ref Stardust dust, Vector2 ballCenter, float ballReach, float radius, float pullStep)
        {
            Vector2 offset = ballCenter - dust.Position;
            float distance = offset.magnitude;
            if (distance <= ballReach)
                return;

            float proximity = Mathf.InverseLerp(radius, ballReach, distance);
            float travel = Mathf.Min(distance - ballReach, pullStep * (1f + 2f * proximity * proximity));
            Vector2 motion = offset * (travel / distance);
            Vector2 next = dust.Position + ArenaPhysics.ClipMotion(dust.Position, terrainClearance, motion, ArenaPhysics.GroundObstacles);
            if (!dust.Region.Bounds.Contains(next))
            {
                Region region = regions.Find(candidate => candidate.Bounds.Contains(next));
                if (region == null)
                    return;

                dust.Region.Count--;
                region.Count++;
                dust.Region = region;
            }

            dust.Position = next;
            dust.View.transform.position = next;
        }

        private bool TrySpawn(bool favorScreen)
        {
            if (active.Count >= Capacity)
                return false;

            Region selected = null;
            Vector2 point = Vector2.zero;
            float totalWeight = 0f;
            foreach (Region region in regions)
            {
                if (region.Count >= Mathf.CeilToInt(region.Target) || !TryPlacement(region.Bounds, out Vector2 candidate))
                    continue;

                bool visible = favorScreen && IsOnScreen(candidate);
                float weight = SpawnWeight(region.LandArea, region.Target, region.Count, depletedWeight, screenWeight, visible);
                totalWeight += weight;
                // Choose from the weighted candidates in one pass.
                if (selected == null || Random.value * totalWeight < weight)
                {
                    selected = region;
                    point = candidate;
                }
            }

            if (selected == null)
                return false;

            AddStardust(point, selected);
            return true;
        }

        internal void Scatter(Vector2 origin, float ballRadius, int count)
        {
            const int positionsPerRing = 16;
            const float goldenAngle = 2.399963f;
            float angle = Random.Range(0f, 2f * Mathf.PI);
            float spread = Mathf.Clamp(minimumSpacing, 0.45f, 1f);
            int attempts = positionsPerRing * 6 + count * 4;
            Region nearestClearRegion = null;
            Vector2 nearestClearPoint = default;
            for (int i = 0; i < attempts && count > 0; i++)
            {
                float distance = ballRadius + (1 + i / positionsPerRing) * spread;
                Vector2 point = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                angle += goldenAngle;
                Region region = regions.Find(candidate => candidate.Bounds.Contains(point));
                if (region == null || Physics2D.OverlapCircle(point, terrainClearance, PlacementObstacles))
                    continue;

                if (nearestClearRegion == null)
                {
                    nearestClearRegion = region;
                    nearestClearPoint = point;
                }

                if (!HasSpacing(point))
                    continue;

                AddStardust(point, region);
                count--;
            }

            // Keep the full refund on usable ground if nearby spaced positions are occupied.
            for (int i = 0; nearestClearRegion != null && i < count; i++)
                AddStardust(nearestClearPoint, nearestClearRegion);
        }

        private void AddStardust(Vector2 point, Region region)
        {
            var sprite = ArenaSprites.Create("Stardust", transform, point, Vector2.one * StardustSize,
                ArenaSprites.Square, new Color(1f, 0.82f, 0.4f), 2);
            sprite.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            region.Count++;
            active.Add(new Stardust(point, region, sprite.gameObject));
        }

        internal static float SpawnWeight(float landArea, float target, int count, float refill, float screen, bool visible)
        {
            float deficit = Mathf.Clamp01(1f - count / target);
            return landArea * Mathf.Lerp(1f, refill, deficit) * (visible ? screen : 1f);
        }

        private bool IsOnScreen(Vector2 position)
        {
            Vector3 point = viewCamera.WorldToViewportPoint(position);
            return point.z > 0f && point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f;
        }

        private bool TryPlacement(Rect bounds, out Vector2 position)
        {
            Vector2 playerPosition = player.transform.position;
            float playerClearance = pickupRadius + 0.25f;
            bool hasRollingBall = player.TryGetRollingBall(out Vector2 ballCenter, out float ballRadius);
            float ballClearance = ballRadius + StardustHalfDiagonal + 0.25f;
            for (int i = 0; i < PlacementAttempts; i++)
            {
                position = new Vector2(Random.Range(bounds.xMin, bounds.xMax), Random.Range(bounds.yMin, bounds.yMax));
                if ((position - playerPosition).sqrMagnitude < playerClearance * playerClearance ||
                    (hasRollingBall && (position - ballCenter).sqrMagnitude < ballClearance * ballClearance) ||
                    Physics2D.OverlapCircle(position, terrainClearance, PlacementObstacles))
                    continue;

                if (HasSpacing(position))
                    return true;
            }

            position = Vector2.zero;
            return false;
        }

        private bool HasSpacing(Vector2 position)
        {
            float spacingSquared = minimumSpacing * minimumSpacing;
            foreach (Stardust dust in active)
            {
                if ((dust.Position - position).sqrMagnitude < spacingSquared)
                    return false;
            }

            return true;
        }
    }
}
