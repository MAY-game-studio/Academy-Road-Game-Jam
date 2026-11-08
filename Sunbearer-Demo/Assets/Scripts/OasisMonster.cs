using System.Collections;
using UnityEngine;

namespace Sunbearer
{
    public sealed class OasisMonster : MonoBehaviour, ISolarBallTarget
    {
        private const float DeathFadeDuration = 0.35f;
        private const float YieldDuration = 0.45f;
        private static readonly float[] SidestepAngles = { 35f, 70f, 105f };
        private static readonly Color HitColor = new Color(1f, 0.72f, 0.65f);

        private OasisMonsterSpawner spawner;
        private CircleCollider2D hitbox;
        private SpriteRenderer visual;
        private Vector2 knockback;
        private Vector2 chaseDirection;
        private Vector2 yieldDirection;
        private float yieldUntil;
        private int sidestepSide = 1;
        private float health;
        private float moveSpeed;
        private float attackDamage;
        private float sporeInterval;
        private float nextSporeAt;
        private Vector2 sporeMuzzle;
        private float nextPushHit;
        private float flashUntil;
        private int obstacles;

        internal int Level { get; private set; }
        internal bool Flies { get; private set; }
        internal float BodyRadius => hitbox.radius;

        internal void Initialize(OasisMonsterSpawner owner, CircleCollider2D collider, SpriteRenderer sprite,
            OasisMonsterSpawner.MonsterKind kind, float speed, float maxHealth, float damage)
        {
            spawner = owner;
            hitbox = collider;
            visual = sprite;
            Level = Mathf.Max(1, kind.Level);
            Flies = kind.Flies;
            moveSpeed = speed;
            health = maxHealth;
            attackDamage = damage;
            sporeInterval = kind.SporeInterval;
            sporeMuzzle = kind.SporeMuzzle;
            nextSporeAt = Time.fixedTime + sporeInterval;
            obstacles = Flies ? ArenaPhysics.SolidMask : ArenaPhysics.GroundObstacles;
        }

        internal bool Step(PlayerController player, MonsterNavigation navigation, bool pursuing)
        {
            Vector2 position = transform.position;
            Vector2 goal = player.transform.position;
            float radius = BodyRadius;
            float reach = player.BodyRadius + radius;
            float contactReach = reach + 2f * ArenaPhysics.Skin;
            bool touching = TouchesPlayer(position, goal, contactReach);
            float deltaTime = Time.fixedDeltaTime;
            Vector2 allowed = Vector2.zero;
            chaseDirection = Vector2.zero;

            if (knockback.sqrMagnitude > 0f)
            {
                yieldUntil = 0f;
                allowed = ClipMovement(position, TakeKnockbackStep(deltaTime), goal, reach, out _);
            }
            else if (pursuing && !touching)
            {
                allowed = Pursue(position, goal, reach, navigation, moveSpeed * deltaTime);
                if (Mathf.Abs(allowed.x) > 0.001f)
                    visual.flipX = allowed.x > 0f;
            }

            bool moved = allowed.sqrMagnitude > 0f;
            if (moved)
            {
                position += allowed;
                transform.position = position;
                touching = TouchesPlayer(position, goal, contactReach);
            }

            visual.color = Time.time < flashUntil ? HitColor : Color.white;
            if (pursuing && sporeInterval > 0f && Time.fixedTime >= nextSporeAt)
                TryFireSpore(player, position, goal);
            if (touching)
                player.TakeDamage(attackDamage, position);

            return moved;
        }

        private void TryFireSpore(PlayerController player, Vector2 position, Vector2 goal)
        {
            float horizontal = goal.x - position.x;
            bool flip = Mathf.Abs(horizontal) > 0.001f ? horizontal > 0f : visual.flipX;
            Vector2 muzzle = sporeMuzzle;
            if (flip)
                muzzle.x = -muzzle.x;

            if (SporeProjectile.TryLaunch(spawner.transform, player, transform.TransformPoint(muzzle), attackDamage))
            {
                visual.flipX = flip;
                nextSporeAt = Time.fixedTime + sporeInterval;
            }
        }

        private Vector2 Pursue(Vector2 position, Vector2 goal, float reach, MonsterNavigation navigation, float stepDistance)
        {
            if (Time.fixedTime < yieldUntil)
            {
                Vector2 retreat = ClipMovement(position, yieldDirection * stepDistance, goal, reach, out _);
                if (retreat.sqrMagnitude > 0.000001f)
                {
                    chaseDirection = yieldDirection;
                    return retreat;
                }

                yieldUntil = 0f;
            }

            Vector2 waypoint = navigation.NextPoint(position, goal, BodyRadius);
            Vector2 motion = Vector2.ClampMagnitude(waypoint - position, stepDistance);
            chaseDirection = motion.normalized;
            Vector2 allowed = ClipMovement(position, motion, goal, reach, out OasisMonster blocker);
            if (!blocker || allowed.sqrMagnitude >= motion.sqrMagnitude * 0.64f)
                return allowed;

            float minimumSidestep = Mathf.Max(allowed.sqrMagnitude, motion.sqrMagnitude * 0.36f);
            foreach (float angle in SidestepAngles)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    int side = attempt == 0 ? sidestepSide : -sidestepSide;
                    Vector2 direction = Quaternion.Euler(0f, 0f, side * angle) * motion;
                    Vector2 sidestep = ClipMovement(position, direction, goal, reach, out _);
                    if (sidestep.sqrMagnitude <= minimumSidestep)
                        continue;

                    sidestepSide = side;
                    return sidestep;
                }
            }

            // Pick one monster to back out when a passage is too narrow to sidestep.
            if (GetInstanceID() > blocker.GetInstanceID() && Vector2.Dot(chaseDirection, blocker.chaseDirection) < -0.45f)
            {
                Vector2 retreat = ClipMovement(position, -chaseDirection * stepDistance, goal, reach, out _);
                if (retreat.sqrMagnitude > 0.000001f)
                {
                    yieldDirection = -chaseDirection;
                    chaseDirection = yieldDirection;
                    yieldUntil = Time.fixedTime + YieldDuration;
                    return retreat;
                }
            }

            return allowed;
        }

        private Vector2 ClipMovement(Vector2 position, Vector2 motion, Vector2 goal, float reach, out OasisMonster blocker)
        {
            float radius = BodyRadius;
            Vector2 allowed = ArenaPhysics.ClipMotion(position, radius, motion, obstacles);
            allowed = ArenaPhysics.ClipAgainstBody(position, allowed, goal, reach);
            return spawner.ClipAgainstMonsters(position, radius, allowed, this, out blocker);
        }

        internal void Repel(Vector2 impulse)
        {
            knockback = impulse;
        }

        private bool TouchesPlayer(Vector2 position, Vector2 goal, float reach)
        {
            return (goal - position).sqrMagnitude <= reach * reach &&
                !Physics2D.Linecast(position, goal, obstacles).collider;
        }

        bool ISolarBallTarget.Hit(float damage, Vector2 impulse, bool fromPush)
        {
            if (health <= 0f || (fromPush && Time.fixedTime < nextPushHit))
                return false;

            if (fromPush)
                nextPushHit = Time.fixedTime + 0.40f;

            health = Mathf.Max(0f, health - damage);
            flashUntil = Time.time + 0.12f;
            if (health <= 0f)
            {
                knockback = impulse;
                hitbox.enabled = false;
                spawner.ReportDefeat(this);
                StartCoroutine(PlayDeath());
            }
            else
                knockback += impulse;

            return true;
        }

        private Vector2 TakeKnockbackStep(float deltaTime)
        {
            Vector2 motion = knockback * deltaTime;
            knockback = Vector2.MoveTowards(knockback, Vector2.zero, 9f * deltaTime);
            return motion;
        }

        private IEnumerator PlayDeath()
        {
            visual.color = HitColor;
            var physicsStep = new WaitForFixedUpdate();
            while (knockback.sqrMagnitude > 0f)
            {
                yield return physicsStep;
                Vector2 position = transform.position;
                Vector2 motion = TakeKnockbackStep(Time.fixedDeltaTime);
                Vector2 allowed = ArenaPhysics.ClipMotion(position, hitbox.radius, motion, obstacles);
                if (allowed.sqrMagnitude <= 0f)
                    break;

                transform.position = position + allowed;
                visual.color = Time.time < flashUntil ? HitColor : Color.white;
            }

            Color color = Color.white;
            for (float elapsed = 0f; elapsed < DeathFadeDuration; elapsed += Time.deltaTime)
            {
                color.a = 1f - elapsed / DeathFadeDuration;
                visual.color = color;
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
