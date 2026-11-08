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

        public static Vector2 ClipAgainstBody(Vector2 position, Vector2 motion, Vector2 center, float reach)
        {
            Vector2 offset = position - center;
            float approach = -Vector2.Dot(offset, motion);
            if (approach <= 0f)
                return motion;

            float clearance = reach + Skin;
            float separation = offset.sqrMagnitude - clearance * clearance;
            if (separation <= 0f)
                return Vector2.zero;

            float discriminant = approach * approach - motion.sqrMagnitude * separation;
            if (discriminant <= 0f)
                return motion;

            // The first contact along the move, computed without subtracting nearly equal values.
            float fraction = separation / (approach + Mathf.Sqrt(discriminant));
            return motion * Mathf.Min(1f, fraction);
        }

        public static bool ContainsPoint(Vector2[] polygon, Vector2 point)
        {
            if (polygon == null || polygon.Length < 3)
                return false;

            bool inside = false;
            Vector2 previous = polygon[polygon.Length - 1];
            foreach (Vector2 current in polygon)
            {
                if ((previous.y > point.y) != (current.y > point.y))
                {
                    float edgeX = previous.x + (point.y - previous.y) * (current.x - previous.x) / (current.y - previous.y);
                    if (point.x < edgeX)
                        inside = !inside;
                }

                previous = current;
            }

            return inside;
        }
    }

    internal interface ISolarBallTarget
    {
        bool Hit(float damage, Vector2 knockback, bool fromPush);
    }

    [System.Serializable]
    internal struct IslandCrossing
    {
        private const float HalfWidth = 0.85f;

        public Vector2 FirstBank;
        public Vector2 SecondBank;

        public bool Contains(Vector2 point)
        {
            Vector2 route = SecondBank - FirstBank;
            float lengthSquared = route.sqrMagnitude;
            if (lengthSquared <= 0.000001f)
                return false;

            float along = Mathf.Clamp01(Vector2.Dot(point - FirstBank, route) / lengthSquared);
            return (point - (FirstBank + route * along)).sqrMagnitude <= HalfWidth * HalfWidth;
        }
    }
}
