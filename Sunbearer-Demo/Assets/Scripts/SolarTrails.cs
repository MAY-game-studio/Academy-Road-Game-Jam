using System.Collections.Generic;
using UnityEngine;

namespace Sunbearer
{
    public sealed class SolarTrails : MonoBehaviour
    {
        [SerializeField, Min(0.2f)] private float lifetimeAfterFlight = 6f;
        [SerializeField, Min(1f)] private float speedMultiplier = 1.55f;

        private readonly List<Trace> traces = new List<Trace>();
        private Material material;
        private PlayerController player;

        public float SpeedMultiplier => speedMultiplier;

        internal void Initialize(PlayerController target)
        {
            player = target;
        }

        internal Trace Begin(Vector2 position, float width)
        {
            if (!material)
                material = new Material(Shader.Find("Sprites/Default"));

            var trail = new GameObject("Solar trail");
            trail.transform.SetParent(transform, false);
            var trace = new Trace(trail, material, position, width);
            traces.Add(trace);
            return trace;
        }

        internal void End(Trace trace)
        {
            trace.RemainingLifetime = lifetimeAfterFlight;
        }

        public bool Covers(Vector2 point)
        {
            foreach (Trace trace in traces)
            {
                if (trace.Covers(point))
                    return true;
            }

            return false;
        }

        private void LateUpdate()
        {
            bool hasPlayer = player && player.isActiveAndEnabled;
            Vector2 playerPosition = hasPlayer ? (Vector2)player.transform.position : Vector2.zero;
            float fadeDuration = Mathf.Min(1f, lifetimeAfterFlight);
            for (int i = traces.Count - 1; i >= 0; i--)
            {
                Trace trace = traces[i];
                float remaining = trace.RemainingLifetime;
                if (float.IsPositiveInfinity(remaining))
                    continue;

                if (hasPlayer && trace.Covers(playerPosition))
                {
                    if (remaining < fadeDuration)
                    {
                        trace.RemainingLifetime = fadeDuration;
                        trace.SetOpacity(1f);
                    }
                    continue;
                }

                remaining -= Time.deltaTime;
                if (remaining <= 0f)
                {
                    Destroy(trace.Root);
                    traces.RemoveAt(i);
                    continue;
                }

                trace.RemainingLifetime = remaining;
                if (remaining < fadeDuration)
                    trace.SetOpacity(remaining / fadeDuration);
            }
        }

        private void OnDestroy()
        {
            if (material)
                Destroy(material);
        }

        internal sealed class Trace
        {
            private const float FillWidthRatio = 0.92f;
            private static readonly Color GoldFill = new Color(1f, 0.67f, 0.12f, 0.76f);
            private static readonly Color PurpleFill = new Color(0.56f, 0.23f, 0.94f, 0.76f);
            private static readonly Color GoldEdge = new Color(1f, 0.93f, 0.50f, 0.88f);
            private static readonly Color PurpleEdge = new Color(0.83f, 0.64f, 1f, 0.88f);

            private readonly List<Vector2> points = new List<Vector2>(5);
            private readonly List<(LineRenderer Fill, LineRenderer Edge)> segments =
                new List<(LineRenderer Fill, LineRenderer Edge)>(4);
            private readonly Material material;
            private readonly float width;
            private readonly float radiusSquared;
            private bool extendingSegment;

            public readonly GameObject Root;
            public float RemainingLifetime = float.PositiveInfinity;

            public Trace(GameObject root, Material material, Vector2 start, float width)
            {
                Root = root;
                this.material = material;
                this.width = width;
                float radius = width * 0.5f;
                radiusSquared = radius * radius;
                points.Add(start);
            }

            public void Extend(Vector2 position)
            {
                int end = points.Count - 1;
                if (extendingSegment)
                {
                    points[end] = position;
                    var (fill, edge) = segments[end - 1];
                    fill.SetPosition(1, position);
                    edge.SetPosition(1, position);
                }
                else
                {
                    Vector2 start = points[end];
                    if ((position - start).sqrMagnitude <= 0.000001f)
                        return;

                    // Draw the fill above the wider edge and keep each turn in separate straight legs.
                    LineRenderer edge = CreateLine("Solar trail edge", Root.transform, start, position, width, -2);
                    LineRenderer fill = CreateLine("Solar trail fill", edge.transform, start, position, width * FillWidthRatio, -1);
                    points.Add(position);
                    segments.Add((fill, edge));
                    extendingSegment = true;
                }

                SetOpacity(1f);
            }

            public void Turn()
            {
                extendingSegment = false;
            }

            public void SetOpacity(float opacity)
            {
                float totalLength = 0f;
                for (int i = 1; i < points.Count; i++)
                    totalLength += Vector2.Distance(points[i - 1], points[i]);

                if (totalLength <= 0f)
                    return;

                float distance = 0f;
                for (int i = 0; i < segments.Count; i++)
                {
                    float from = distance / totalLength;
                    distance += Vector2.Distance(points[i], points[i + 1]);
                    float to = distance / totalLength;
                    var (fill, edge) = segments[i];
                    SetGradient(fill, GoldFill, PurpleFill, from, to, opacity);
                    SetGradient(edge, GoldEdge, PurpleEdge, from, to, opacity);
                }
            }

            private LineRenderer CreateLine(string name, Transform parent, Vector2 start, Vector2 end,
                float lineWidth, int sortingOrder)
            {
                var segment = new GameObject(name);
                segment.transform.SetParent(parent, false);
                var line = segment.AddComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.useWorldSpace = true;
                line.sortingOrder = sortingOrder;
                line.numCapVertices = 8;
                line.startWidth = line.endWidth = lineWidth;
                line.positionCount = 2;
                line.SetPosition(0, start);
                line.SetPosition(1, end);
                return line;
            }

            private static void SetGradient(LineRenderer line, Color start, Color end, float from, float to, float opacity)
            {
                Color startColor = Color.Lerp(start, end, from);
                Color endColor = Color.Lerp(start, end, to);
                startColor.a *= opacity;
                endColor.a *= opacity;
                line.startColor = startColor;
                line.endColor = endColor;
            }

            public bool Covers(Vector2 point)
            {
                for (int i = 1; i < points.Count; i++)
                {
                    Vector2 start = points[i - 1];
                    Vector2 segment = points[i] - start;
                    float lengthSquared = segment.sqrMagnitude;
                    if (lengthSquared <= 0.000001f)
                        continue;

                    float along = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
                    if ((point - (start + segment * along)).sqrMagnitude <= radiusSquared)
                        return true;
                }

                return false;
            }
        }
    }
}
