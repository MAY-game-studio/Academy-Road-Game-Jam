using UnityEngine;

namespace Sunbearer
{
    public sealed class SporeProjectile : MonoBehaviour
    {
        private const float Speed = 2.8f;
        private const float Range = 6.5f;
        private const float Radius = 0.10f;
        private static readonly Color SporeColor = new Color(0.61f, 0.98f, 0.89f);

        private PlayerController player;
        private Vector2 direction;
        private float damage;
        private float remainingRange = Range;

        internal static bool TryLaunch(Transform parent, PlayerController player, Vector2 origin, float damage)
        {
            Vector2 aim = (Vector2)player.transform.position - origin;
            float distance = aim.magnitude;
            if (distance <= 0.001f || distance > Range)
                return false;

            Vector2 direction = aim / distance;
            if (Physics2D.CircleCast(origin, Radius, direction, distance, ArenaPhysics.SolidMask).collider)
                return false;

            SpriteRenderer view = ArenaSprites.Create("Spore", parent, origin, Vector2.one * (Radius * 2f),
                ArenaSprites.Disc, SporeColor, 6);
            var spore = view.gameObject.AddComponent<SporeProjectile>();
            spore.player = player;
            spore.direction = direction;
            spore.damage = damage;
            return true;
        }

        private void FixedUpdate()
        {
            if (!player || !player.enabled)
            {
                Finish();
                return;
            }

            Vector2 position = transform.position;
            float distance = Mathf.Min(Speed * Time.fixedDeltaTime, remainingRange);
            RaycastHit2D wall = Physics2D.CircleCast(position, Radius, direction, distance, ArenaPhysics.SolidMask);
            Vector2 motion = direction * (wall.collider ? wall.distance : distance);
            Vector2 target = player.transform.position;
            float reach = player.BodyRadius + Radius;
            bool overlapping = (position - target).sqrMagnitude <= reach * reach;
            Vector2 toContact = overlapping ? Vector2.zero : ArenaPhysics.ClipAgainstBody(position, motion, target, reach);

            if (toContact.sqrMagnitude < motion.sqrMagnitude)
            {
                Vector2 source = position + toContact;
                if ((source - target).sqrMagnitude <= 0.000001f)
                    source = target - direction * reach;

                player.TakeDamage(damage, source);
                Finish();
                return;
            }

            if (wall.collider)
            {
                Finish();
                return;
            }

            transform.position = position + motion;
            remainingRange -= distance;
            if (remainingRange <= 0f)
                Finish();
        }

        private void Finish()
        {
            enabled = false;
            Destroy(gameObject);
        }
    }
}
