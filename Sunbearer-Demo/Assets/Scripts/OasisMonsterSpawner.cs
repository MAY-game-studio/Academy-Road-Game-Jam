using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sunbearer
{
    public sealed class OasisMonsterSpawner : MonoBehaviour
    {
        [Serializable]
        internal struct MonsterKind
        {
            public string Name;
            [Min(1)] public int Level;
            public Sprite Sprite;
            [Min(0.1f)] public float SpeedMultiplier;
            [Min(0.1f)] public float Radius;
            public bool Flies;
            [Min(0f), Tooltip("Zero uses base health.")] public float Health;
            [Min(0f), Tooltip("Zero uses base damage.")] public float DamageMultiplier;
            [Min(1), Tooltip("The number of spawn rotations between appearances.")] public int SpawnEveryRounds;
            [Min(0f), Tooltip("Zero disables spore attacks.")] public float SporeInterval;
            public Vector2 SporeMuzzle;
        }

        [SerializeField] private MonsterKind[] kinds = new MonsterKind[0];
        [SerializeField, Min(0)] private int oasisShorePath = 1;

        [Header("Base stats")]
        [SerializeField, Min(0.1f)] private float baseMoveSpeed = 1.5f;
        [SerializeField, Min(1f)] private float health = 20f;
        [SerializeField, Min(0f)] private float contactDamage = 10f;

        [Header("Spawning")]
        [SerializeField, Min(0.1f)] private float calmInterval = 11f;
        [SerializeField, Min(0.1f)] private float challengeInterval = 4f;
        [SerializeField, Min(1)] private int maxAlive = 14;

        private const int LevelOneTowerEnergy = 2;
        private const int LevelTwoTowerEnergy = 5;
        private const float FirstSpawnDelay = 2.5f;
        private const int SpawnAttempts = 28;

        private readonly List<OasisMonster> monsters = new List<OasisMonster>();
        private Playground arena;
        private StardustSpawner stardust;
        private PlayerController player;
        private Vector2[] shore;
        private Rect chaseBounds;
        private MonsterNavigation groundNavigation;
        private MonsterNavigation airNavigation;
        private float nextSpawnAt;
        private int nextKind;
        private int spawnRound;
        private bool wasChallengeActive;

        internal int AliveCount => monsters.Count;
        internal float CurrentInterval => arena.HasActiveTowerChallenge ? challengeInterval : calmInterval;

        internal void Initialize(Playground owner, PlayerController target, PolygonCollider2D coastline)
        {
            arena = owner;
            stardust = owner.GetComponent<StardustSpawner>();
            if (kinds.Length == 0)
                return;

            shore = coastline.GetPath(oasisShorePath);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < shore.Length; i++)
            {
                shore[i] = coastline.transform.TransformPoint(shore[i] + coastline.offset);
                min = Vector2.Min(min, shore[i]);
                max = Vector2.Max(max, shore[i]);
            }

            chaseBounds = Rect.MinMaxRect(min.x - 1f, min.y - 1f, max.x + 1f, max.y + 1f);
            float clearance = 0f;
            foreach (MonsterKind kind in kinds)
                clearance = Mathf.Max(clearance, kind.Radius);

            groundNavigation = new MonsterNavigation(chaseBounds, clearance + 0.02f,
                ArenaPhysics.GroundObstacles, InsideOasis);
            airNavigation = new MonsterNavigation(chaseBounds, clearance + 0.02f, ArenaPhysics.SolidMask, null);
            player = target;
            nextSpawnAt = Time.time + FirstSpawnDelay;
        }

        private void Update()
        {
            if (!player || !player.enabled)
                return;

            bool challenge = arena.HasActiveTowerChallenge;
            float interval = CurrentInterval;
            if (challenge != wasChallengeActive)
            {
                float revised = Time.time + interval;
                nextSpawnAt = challenge ? Mathf.Min(nextSpawnAt, revised) : revised;
                wasChallengeActive = challenge;
            }

            if (Time.time < nextSpawnAt)
                return;

            nextSpawnAt = Time.time + interval;
            Vector2 playerPosition = player.transform.position;
            if (monsters.Count < maxAlive && InsideOasis(playerPosition))
                TrySpawn(playerPosition);
        }

        private void FixedUpdate()
        {
            if (!player || !player.enabled || monsters.Count == 0)
                return;

            Vector2 goal = player.transform.position;
            bool pursuing = chaseBounds.Contains(goal);
            if (pursuing)
            {
                groundNavigation.SetGoal(goal);
                airNavigation.SetGoal(goal);
            }

            bool moved = false;
            foreach (OasisMonster monster in monsters)
            {
                MonsterNavigation navigation = monster.Flies ? airNavigation : groundNavigation;
                moved |= monster.Step(player, navigation, pursuing);
                if (!player.enabled)
                    break;
            }

            if (moved)
                Physics2D.SyncTransforms();
        }

        internal Vector2 ClipAgainstMonsters(Vector2 position, float radius, Vector2 motion)
        {
            return ClipAgainstMonsters(position, radius, motion, null, out _);
        }

        internal Vector2 ClipAgainstMonsters(Vector2 position, float radius, Vector2 motion,
            OasisMonster movingMonster, out OasisMonster blocker)
        {
            blocker = null;
            foreach (OasisMonster monster in monsters)
            {
                if (motion.sqrMagnitude <= 0f)
                    break;
                if (monster == movingMonster)
                    continue;

                Vector2 clipped = ArenaPhysics.ClipAgainstBody(position, motion, monster.transform.position,
                    radius + monster.BodyRadius);
                if (clipped.sqrMagnitude < motion.sqrMagnitude)
                {
                    motion = clipped;
                    blocker = monster;
                }
            }

            return motion;
        }

        internal void RepelAround(Vector2 position, float radius, float speed)
        {
            if (radius <= 0f || speed <= 0f)
                return;

            float radiusSquared = radius * radius;
            foreach (OasisMonster monster in monsters)
            {
                Vector2 away = (Vector2)monster.transform.position - position;
                float distanceSquared = away.sqrMagnitude;
                if (distanceSquared > radiusSquared)
                    continue;

                Vector2 direction = distanceSquared > 0.000001f ? away.normalized : Vector2.right;
                monster.Repel(direction * speed);
            }
        }

        internal void ReportDefeat(OasisMonster monster)
        {
            if (!monsters.Remove(monster))
                return;

            arena.AddTowerChallengeEnergy(monster.Level == 2 ? LevelTwoTowerEnergy : LevelOneTowerEnergy);
            if (stardust)
                stardust.Scatter(monster.transform.position, monster.BodyRadius, monster.Level, fallbackToLand: true);
        }

        private void TrySpawn(Vector2 playerPosition)
        {
            while (spawnRound % Mathf.Max(1, kinds[nextKind].SpawnEveryRounds) != 0)
                AdvanceSpawnKind();

            MonsterKind kind = kinds[nextKind];
            int spawnObstacles = ArenaPhysics.GroundObstacles | ArenaPhysics.TargetMask;
            for (int attempt = 0; attempt < SpawnAttempts; attempt++)
            {
                float angle = Random.Range(0f, 2f * Mathf.PI);
                float distance = Random.Range(3.8f, 6.8f);
                if (distance < player.BodyRadius + kind.Radius + ArenaPhysics.Skin)
                    continue;

                Vector2 point = playerPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                if (!InsideOasis(point) || Physics2D.OverlapCircle(point, kind.Radius + 0.15f, spawnObstacles))
                    continue;

                SpriteRenderer view = ArenaSprites.Create(kind.Name, transform, point, Vector2.one,
                    kind.Sprite, Color.white, 5);
                view.gameObject.layer = ArenaPhysics.TargetLayer;
                CircleCollider2D hitbox = view.gameObject.AddComponent<CircleCollider2D>();
                hitbox.radius = kind.Radius;
                var monster = view.gameObject.AddComponent<OasisMonster>();
                float maxHealth = kind.Health > 0f ? kind.Health : health;
                float damage = contactDamage * (kind.DamageMultiplier > 0f ? kind.DamageMultiplier : 1f);
                monster.Initialize(this, hitbox, view, kind, baseMoveSpeed * kind.SpeedMultiplier, maxHealth, damage);
                monsters.Add(monster);
                AdvanceSpawnKind();
                return;
            }
        }

        private void AdvanceSpawnKind()
        {
            nextKind++;
            if (nextKind == kinds.Length)
            {
                nextKind = 0;
                spawnRound++;
            }
        }

        private bool InsideOasis(Vector2 point) => ArenaPhysics.ContainsPoint(shore, point);
    }
}
