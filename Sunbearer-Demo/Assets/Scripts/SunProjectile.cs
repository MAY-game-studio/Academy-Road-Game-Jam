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
            SunBallVisual view, Vector2 velocity, float radius, float damage, float knockback, float range,
            bool full, float burstSpeed, int stardustToScatter)
        {
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
                shot.trace = trails.Begin(view.transform.position, radius * 2f);
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
                if (!FindImpact(position, step, out RaycastHit2D hit, out Vector2 normal))
                {
                    Advance(position + direction * step, step);
                    break;
                }

                float distance = Mathf.Clamp(hit.distance, 0f, step);
                position += direction * distance;
                Advance(position, distance);
                step -= distance;

                if (full)
                    RitualTower.ReportFullBallHit(hit.collider);

                if (hit.collider.TryGetComponent(out ISolarBallTarget target))
                    target.Hit(damage, direction * knockback, false);

                if (reflectionsLeft == 0 || hit.collider.gameObject.layer == ArenaPhysics.BoundaryLayer)
                {
                    Finish();
                    return;
                }

                reflectionsLeft--;
                escapeNormal = normal;
                lastSurface = hit.collider;
                direction = Vector2.Reflect(direction, normal).normalized;
                trace.Turn();
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

            ShowBurst(trails.transform, position, radius);
            Finish();
        }

        internal static void ShowBurst(Transform parent, Vector2 position, float radius)
        {
            float flashSize = Mathf.Max(0.8f, radius * 3.6f);
            var flash = ArenaSprites.Create("Sun burst", parent, position, Vector2.one * flashSize,
                ArenaSprites.Disc, new Color(1f, 0.7f, 0.3f, 0.65f), 1);
            Destroy(flash.gameObject, 0.12f);
        }

        private bool FindImpact(Vector2 position, float distance, out RaycastHit2D nearest, out Vector2 normal)
        {
            nearest = default;
            normal = Vector2.zero;
            int count = Physics2D.CircleCast(position, radius, direction, filter, hits, distance);
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = hits[i];
                if (nearest.collider && hit.distance >= nearest.distance)
                    continue;

                Vector2 candidateNormal = hit.normal;
                if (full && hit.distance <= 0f)
                {
                    // Recover the surface normal when the sweep starts in contact.
                    Vector2 separation = position - hit.collider.ClosestPoint(position);
                    if (separation.sqrMagnitude > 0.000001f)
                        candidateNormal = separation.normalized;
                    else if (hit.collider == lastSurface)
                        candidateNormal = escapeNormal;

                    if (Vector2.Dot(direction, candidateNormal) >= -0.0001f)
                        continue;
                }

                nearest = hit;
                normal = candidateNormal;
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
