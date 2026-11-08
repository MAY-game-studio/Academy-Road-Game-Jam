using UnityEngine;

namespace Sunbearer
{
    public sealed class SunProjectile : MonoBehaviour
    {
        private const int FullBallReflections = 3;

        private readonly RaycastHit2D[] hits = new RaycastHit2D[12];
        private ContactFilter2D filter;
        private PlayerController owner;
        private SolarTrails trails;
        private StardustSpawner stardust;
        private SolarTrails.Trace trace;
        private Collider2D lastSurface;
        private Vector2 escapeNormal;
        private Vector2 direction;
        private float speed;
        private float burstSpeed;
        private float deceleration;
        private float radius;
        private float damage;
        private float knockback;
        private float remainingDistance;
        private int reflectionsLeft;
        private int stardustToScatter;
        private bool full;
        private bool finished;

        internal static SunProjectile Launch(PlayerController owner, SolarTrails trails, StardustSpawner stardust,
            Vector2 position, Vector2 velocity, float radius, float damage, float knockback, float range,
            bool full, float burstSpeed, int stardustToScatter)
        {
            var view = ArenaSprites.Create("Flying sun", trails.transform, position, Vector2.one * radius * 2f,
                ArenaSprites.Disc, full ? new Color(1f, 0.84f, 0.27f) : new Color(1f, 0.54f, 0.21f), 5);
            var shot = view.gameObject.AddComponent<SunProjectile>();
            shot.owner = owner;
            shot.trails = trails;
            shot.stardust = stardust;
            shot.speed = velocity.magnitude;
            shot.direction = velocity.normalized;
            shot.radius = radius;
            shot.damage = damage;
            shot.knockback = knockback;
            shot.remainingDistance = range;
            shot.full = full;
            shot.filter = new ContactFilter2D { useTriggers = false };
            shot.filter.SetLayerMask(ArenaPhysics.ShotObstacles);
            if (full)
            {
                shot.reflectionsLeft = FullBallReflections;
                shot.trace = trails.Begin(position, radius * 2f);
            }
            else
            {
                shot.burstSpeed = burstSpeed;
                // Reach the burst speed at the configured range when the path is clear.
                shot.deceleration = (shot.speed - burstSpeed) * (shot.speed + burstSpeed) / (2f * range);
                shot.stardustToScatter = stardustToScatter;
            }

            return shot;
        }

        private void FixedUpdate()
        {
            if (finished)
                return;

            float deltaTime = Time.fixedDeltaTime;
            float step;
            bool willBurst = false;
            if (full)
            {
                step = Mathf.Min(speed * deltaTime, remainingDistance);
            }
            else
            {
                float timeToBurst = speed <= burstSpeed ? 0f : (speed - burstSpeed) / deceleration;
                willBurst = timeToBurst <= deltaTime;
                float duration = Mathf.Min(timeToBurst, deltaTime);
                float endSpeed = willBurst ? burstSpeed : speed - deceleration * duration;
                step = (speed + endSpeed) * 0.5f * duration;
                speed = endSpeed;
            }

            while (step > 0.0001f)
            {
                Vector2 position = transform.position;
                if (!FindImpact(position, step, out RaycastHit2D hit))
                {
                    Advance(position + direction * step, step);
                    break;
                }

                float distance = Mathf.Clamp(hit.distance, 0f, step);
                position += direction * distance;
                Advance(position, distance);
                step -= distance;

                if (hit.collider.TryGetComponent(out PracticeTarget target))
                    target.Hit(damage, direction * knockback, false);

                if (reflectionsLeft == 0 || hit.collider.gameObject.layer == ArenaPhysics.BoundaryLayer)
                {
                    Finish();
                    return;
                }

                reflectionsLeft--;
                escapeNormal = hit.normal;
                lastSurface = hit.collider;
                direction = Vector2.Reflect(direction, escapeNormal).normalized;
                transform.position = position + escapeNormal * ArenaPhysics.Skin;
                trace.Turn(position);
                owner.ReportReflection(this, FullBallReflections - reflectionsLeft);
            }

            if (willBurst)
                Burst();
            else if (full && remainingDistance <= 0.0001f)
                Finish();
        }

        private void Burst()
        {
            Vector2 position = transform.position;
            if (stardust && stardustToScatter > 0)
                stardust.Scatter(position, radius, stardustToScatter);

            float flashSize = Mathf.Max(0.8f, radius * 3.6f);
            var flash = ArenaSprites.Create("Sun burst", trails.transform, position, Vector2.one * flashSize,
                ArenaSprites.Disc, new Color(1f, 0.7f, 0.3f, 0.65f), 1);
            Destroy(flash.gameObject, 0.12f);
            Finish();
        }

        private bool FindImpact(Vector2 position, float distance, out RaycastHit2D nearest)
        {
            nearest = default;
            int count = Physics2D.CircleCast(position, radius, direction, filter, hits, distance);
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = hits[i];
                // A sweep can start on the contact surface that the previous step is leaving.
                if (hit.collider == lastSurface && hit.distance <= 2f * ArenaPhysics.Skin &&
                    Vector2.Dot(direction, escapeNormal) > 0f)
                    continue;

                if (!nearest.collider || hit.distance < nearest.distance)
                    nearest = hit;
            }

            return nearest.collider;
        }

        private void Advance(Vector2 position, float distance)
        {
            transform.position = position;
            remainingDistance -= distance;
            trace?.Extend(position);
        }

        private void Finish()
        {
            finished = true;
            if (trace != null)
                trails.End(trace);
            Destroy(gameObject);
        }
    }
}
