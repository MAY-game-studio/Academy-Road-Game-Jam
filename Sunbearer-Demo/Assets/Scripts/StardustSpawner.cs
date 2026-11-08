using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Sunbearer
{
    public sealed class StardustSpawner : MonoBehaviour
    {
        [Header("Population")]
        [SerializeField, Min(0.1f)] private float spawnInterval = 0.8f;
        [SerializeField, Min(1)] private int refillBatchSize = 2;
        [SerializeField, Min(1f)] private float regionSize = 3.5f;
        [SerializeField, Min(0.001f)] private float targetPerSquareUnit = 0.25f;
        [SerializeField, Range(0f, 1f)] private float initialFill = 0.88f;
        [SerializeField, Min(1f)] private float screenWeight = 1.45f;
        [SerializeField, Min(1f)] private float depletedWeight = 2.35f;

        [SerializeField, Range(0f, 1f)] private float powerUpSpawnChance = 0.04f;

        [Header("Appearance")]
        [SerializeField] private Sprite stardustSprite;

        [Header("Placement and pickup")]
        [SerializeField, Min(0.05f)] private float terrainClearance = 0.16f;
        [SerializeField, Min(0.1f)] private float minimumSpacing = 0.8f;
        [SerializeField, Min(0.1f)] private float pickupRadius = 1f;
        [SerializeField, Min(0.1f)] private float blackHoleRadius = 2.5f;
        [SerializeField, Range(0.01f, 1f)] private float chargePerStardust = 0.08f;

        private const int SamplesPerAxis = 6;
        private const int PlacementAttempts = 10;
        private const int PlacementObstacles = ArenaPhysics.GroundObstacles | ArenaPhysics.TargetMask;
        private const float StardustHeight = 0.22f;
        private const float StardustContactRadius = 0.106066f;

        private sealed class Region
        {
            public Rect Bounds;
            public float LandArea;
            public Vector2 LandAnchor;
            public int Target;
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
        private readonly List<(PowerUpPickup Pickup, Vector2 Position, Region Region)> powerUps =
            new List<(PowerUpPickup Pickup, Vector2 Position, Region Region)>();
        private PowerUpPickup[] powerUpTemplates;
        private Camera viewCamera;
        private PlayerController player;
        private float nextSpawn;

        public int Count => active.Count;
        public int Capacity { get; private set; }
        internal float ChargePerStardust => chargePerStardust;
        internal int PowerUpCount => powerUps.Count;
        internal string LastPickup { get; private set; } = "-";
        private int Population => Count + PowerUpCount;

        internal void Initialize(Rect map, Camera camera, PlayerController controller, Vector2[] startingPoints,
            PowerUpPickup[] templates)
        {
            viewCamera = camera;
            player = controller;
            powerUpTemplates = templates.Where(template => template != null).ToArray();
            foreach (PowerUpPickup template in powerUpTemplates)
            {
                if (template.gameObject.scene.IsValid())
                    template.gameObject.SetActive(false);
            }

            float land = BuildRegions(map);
            Capacity = land > 0f ? Mathf.Max(1, Mathf.RoundToInt(land * targetPerSquareUnit)) : 0;
            AllocateRegionTargets();

            foreach (Vector2 point in startingPoints)
            {
                if (active.Count >= Capacity)
                    break;

                Region region = regions.Find(candidate => candidate.Bounds.Contains(point));
                if (region != null && !Physics2D.OverlapCircle(point, terrainClearance, PlacementObstacles))
                    AddStardust(point, region);
            }

            int startCount = Mathf.RoundToInt(Capacity * initialFill);
            foreach (Region region in regions)
            {
                int initialTarget = Mathf.FloorToInt(region.Target * initialFill);
                while (Population < startCount && region.Count < initialTarget &&
                       TryPlacement(region.Bounds, false, out Vector2 point))
                    AddRandomDrop(point, region);
            }

            for (int i = Population; i < startCount; i++)
            {
                if (!TrySpawn(false))
                    break;
            }

            nextSpawn = Time.time + spawnInterval;
        }

        private void Update()
        {
            if (!player || !player.isActiveAndEnabled)
                return;

            CollectPowerUps();
            CollectNearby();
            if (Time.time < nextSpawn)
                return;

            nextSpawn = Time.time + spawnInterval;
            int spawned = TrySpawn(true) ? 1 : 0;
            for (; spawned < refillBatchSize; spawned++)
            {
                if (!TrySpawn(false))
                    break;
            }
        }

        private void AllocateRegionTargets()
        {
            int remaining = Capacity;
            foreach (Region region in regions)
            {
                region.Target = Mathf.FloorToInt(region.LandArea * targetPerSquareUnit);
                remaining -= region.Target;
            }

            // Allocate rounding remainders without letting distant cells exhaust another cell's quota.
            foreach (Region region in regions.OrderByDescending(cell => cell.LandArea * targetPerSquareUnit - cell.Target).Take(remaining))
                region.Target++;
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
                    Vector2 landAnchor = default;
                    for (int ix = 0; ix < SamplesPerAxis; ix++)
                    {
                        for (int iy = 0; iy < SamplesPerAxis; iy++)
                        {
                            var sample = new Vector2(
                                bounds.xMin + (ix + 0.5f) * bounds.width / SamplesPerAxis,
                                bounds.yMin + (iy + 0.5f) * bounds.height / SamplesPerAxis);
                            if (!Physics2D.OverlapCircle(sample, terrainClearance, ArenaPhysics.GroundObstacles))
                            {
                                if (clear == 0)
                                    landAnchor = sample;
                                clear++;
                            }
                        }
                    }

                    if (clear == 0)
                        continue;

                    float landArea = bounds.width * bounds.height * clear / (SamplesPerAxis * SamplesPerAxis);
                    regions.Add(new Region { Bounds = bounds, LandArea = landArea, LandAnchor = landAnchor });
                    totalLand += landArea;
                }
            }

            return totalLand;
        }

        private void CollectPowerUps()
        {
            for (int i = powerUps.Count - 1; i >= 0; i--)
            {
                var (pickup, _, region) = powerUps[i];
                if (!pickup.TryCollect(player))
                    continue;

                LastPickup = pickup.DisplayName;
                region.Count--;
                powerUps.RemoveAt(i);
                Destroy(pickup.gameObject);
            }
        }

        private void CollectNearby()
        {
            bool hasRollingBall = player.TryGetRollingBall(out Vector2 ballCenter, out float ballRadius);
            if (!player.CanStoreStardust && !(hasRollingBall && player.CanFeedBallWithStardust))
                return;

            Vector2 position = player.transform.position;
            float bodyReach = player.BodyRadius + StardustContactRadius;
            float bodyReachSquared = bodyReach * bodyReach;
            bool hasBlackHole = player.BlackHoleRemaining > 0f;
            float holeAttraction = Mathf.Max(bodyReach, blackHoleRadius);
            float holeAttractionSquared = holeAttraction * holeAttraction;
            float ballReach = ballRadius + StardustContactRadius;
            float ballAttraction = Mathf.Max(ballReach, player.StardustAttractionRadius);
            float ballAttractionSquared = ballAttraction * ballAttraction;
            float pullStep = player.StardustPullSpeed * Time.deltaTime;

            for (int i = active.Count - 1; i >= 0; i--)
            {
                bool canStore = player.CanStoreStardust;
                bool canFeedBall = hasRollingBall && player.CanFeedBallWithStardust;
                if (!canStore && !canFeedBall)
                    break;

                Stardust dust = active[i];
                float bodyDistanceSquared = (dust.Position - position).sqrMagnitude;
                bool touchingPlayer = canStore && bodyDistanceSquared <= bodyReachSquared;
                bool drawnToPlayer = canStore && hasBlackHole && bodyDistanceSquared <= holeAttractionSquared;
                bool drawnToBall = canFeedBall && !touchingPlayer && !drawnToPlayer &&
                    (dust.Position - ballCenter).sqrMagnitude <= ballAttractionSquared;
                Vector2 collector = drawnToBall ? ballCenter : position;
                float reach = drawnToBall ? ballReach : bodyReach;
                if (drawnToPlayer || drawnToBall)
                {
                    float attraction = drawnToPlayer ? holeAttraction : ballAttraction;
                    PullTowardCollector(ref dust, collector, reach, attraction, pullStep);
                    active[i] = dust;
                    reach += 0.001f;
                }

                if ((dust.Position - collector).sqrMagnitude > reach * reach)
                    continue;
                bool collected = drawnToBall ? player.TryFeedBallWithStardust(chargePerStardust) : player.TryStoreStardust();
                if (!collected)
                    continue;

                dust.Region.Count--;
                active.RemoveAt(i);
                Destroy(dust.View);
            }
        }

        private void PullTowardCollector(ref Stardust dust, Vector2 center, float reach, float radius, float pullStep)
        {
            Vector2 offset = center - dust.Position;
            float distance = offset.magnitude;
            if (distance <= reach)
                return;

            float proximity = Mathf.InverseLerp(radius, reach, distance);
            float travel = Mathf.Min(distance - reach, pullStep * (1f + 2f * proximity * proximity));
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

        private bool TrySpawn(bool onScreenOnly)
        {
            if (Population >= Capacity)
                return false;

            Region selected = null;
            Vector2 point = Vector2.zero;
            float totalWeight = 0f;
            foreach (Region region in regions)
            {
                if (region.Count >= region.Target || !TryPlacement(region.Bounds, onScreenOnly, out Vector2 candidate))
                    continue;

                bool visible = onScreenOnly || IsOnScreen(candidate);
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

            AddRandomDrop(point, selected);
            return true;
        }

        private void AddRandomDrop(Vector2 point, Region region)
        {
            if (powerUpTemplates.Length > 0 && Random.value < powerUpSpawnChance)
            {
                PowerUpPickup template = powerUpTemplates[Random.Range(0, powerUpTemplates.Length)];
                float clearance = Mathf.Max(terrainClearance, template.Radius);
                if (!Physics2D.OverlapCircle(point, clearance, PlacementObstacles))
                {
                    PowerUpPickup pickup = Instantiate(template, point, template.transform.rotation, transform);
                    pickup.gameObject.SetActive(true);
                    powerUps.Add((pickup, point, region));
                    region.Count++;
                    return;
                }
            }

            AddStardust(point, region);
        }

        internal void Scatter(Vector2 origin, float sourceRadius, int count, bool fallbackToLand = false)
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
                float distance = sourceRadius + (1 + i / positionsPerRing) * spread;
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

            if (fallbackToLand && count > 0 && nearestClearRegion == null)
                nearestClearRegion = FindNearestLand(origin, out nearestClearPoint);

            // Keep the full drop on usable ground if nearby spaced positions are occupied.
            for (int i = 0; nearestClearRegion != null && i < count; i++)
                AddStardust(nearestClearPoint, nearestClearRegion);
        }

        private Region FindNearestLand(Vector2 origin, out Vector2 point)
        {
            Region nearest = null;
            point = default;
            float nearestDistance = float.PositiveInfinity;
            foreach (Region region in regions)
            {
                Vector2 candidate = region.LandAnchor;
                float distance = (candidate - origin).sqrMagnitude;
                if (distance >= nearestDistance ||
                    Physics2D.OverlapCircle(candidate, terrainClearance, ArenaPhysics.GroundObstacles))
                    continue;

                nearest = region;
                point = candidate;
                nearestDistance = distance;
            }

            return nearest;
        }

        private void AddStardust(Vector2 point, Region region)
        {
            var sprite = ArenaSprites.Create("Stardust", transform, point, Vector2.one * StardustHeight,
                stardustSprite ? stardustSprite : ArenaSprites.Disc,
                stardustSprite ? Color.white : new Color(1f, 0.82f, 0.4f), 2);
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

        private bool TryPlacement(Rect bounds, bool onScreenOnly, out Vector2 position)
        {
            Vector2 playerPosition = player.transform.position;
            float playerClearance = pickupRadius + 0.25f;
            bool hasRollingBall = player.TryGetRollingBall(out Vector2 ballCenter, out float ballRadius);
            float ballClearance = ballRadius + StardustContactRadius + 0.25f;
            for (int i = 0; i < PlacementAttempts; i++)
            {
                position = new Vector2(Random.Range(bounds.xMin, bounds.xMax), Random.Range(bounds.yMin, bounds.yMax));
                if ((onScreenOnly && !IsOnScreen(position)) ||
                    (position - playerPosition).sqrMagnitude < playerClearance * playerClearance ||
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

            foreach (var pickup in powerUps)
            {
                if ((pickup.Position - position).sqrMagnitude < spacingSquared)
                    return false;
            }

            return true;
        }
    }
}
