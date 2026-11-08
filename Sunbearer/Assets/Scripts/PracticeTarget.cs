using UnityEngine;

namespace Sunbearer
{
    public sealed class PracticeTarget : MonoBehaviour
    {
        public const float Radius = 0.45f;
        private const float MaxHealth = 70f;

        private Vector2 start;
        private Vector2 velocity;
        private Collider2D hitbox;
        private SpriteRenderer visual;
        private Color normalColor;
        private float nextPushHit;
        private float resetAt;
        private float flashUntil;

        public float Health { get; private set; } = MaxHealth;
        public float LastDamage { get; private set; }
        public float LastKnockback { get; private set; }

        private void Awake()
        {
            start = transform.position;
            hitbox = GetComponent<Collider2D>();
            visual = GetComponent<SpriteRenderer>();
            normalColor = visual.color;
        }

        internal bool Hit(float damage, Vector2 knockback, bool fromPush)
        {
            if (Health <= 0f || (fromPush && Time.fixedTime < nextPushHit))
                return false;

            if (fromPush)
                nextPushHit = Time.fixedTime + 0.40f;

            LastDamage = damage;
            LastKnockback = knockback.magnitude;
            Health = Mathf.Max(0f, Health - damage);
            velocity += knockback;
            flashUntil = Time.time + 0.12f;
            if (Health <= 0f)
            {
                resetAt = Time.time + 1.5f;
                hitbox.enabled = false;
                visual.enabled = false;
            }

            return true;
        }

        private void FixedUpdate()
        {
            if (Health <= 0f)
            {
                if (Time.time >= resetAt)
                {
                    transform.position = start;
                    velocity = Vector2.zero;
                    Health = MaxHealth;
                    visual.enabled = true;
                    hitbox.enabled = true;
                    Physics2D.SyncTransforms();
                }

                return;
            }

            if (velocity.sqrMagnitude > 0f)
            {
                Vector2 motion = velocity * Time.fixedDeltaTime;
                Vector2 allowed = ArenaPhysics.ClipMotion(transform.position, Radius, motion, ArenaPhysics.GroundObstacles);
                transform.position += (Vector3)allowed;
                velocity = Vector2.MoveTowards(velocity, Vector2.zero, 9f * Time.fixedDeltaTime);
                Physics2D.SyncTransforms();
            }

            visual.color = Time.time < flashUntil ? Color.white : normalColor;
        }
    }
}
