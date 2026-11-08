using UnityEngine;

namespace Sunbearer
{
    internal static class ArenaPhysics
    {
        public const int WallLayer = 8;
        public const int GapLayer = 9;
        public const int TargetLayer = 10;
        public const int BoundaryLayer = 11;

        public const int SolidMask = (1 << WallLayer) | (1 << BoundaryLayer);
        public const int GapMask = 1 << GapLayer;
        public const int TargetMask = 1 << TargetLayer;
        public const int GroundObstacles = SolidMask | GapMask;
        public const int ShotObstacles = SolidMask | TargetMask;
        public const float Skin = 0.006f;

        public static Vector2 ClipMotion(Vector2 position, float radius, Vector2 motion, int obstacles)
        {
            float distance = motion.magnitude;
            if (distance <= 0.000001f)
                return motion;

            Vector2 direction = motion / distance;
            RaycastHit2D hit = Physics2D.CircleCast(position, radius, direction, distance, obstacles);
            return hit.collider ? direction * Mathf.Max(0f, hit.distance - Skin) : motion;
        }
    }
}
