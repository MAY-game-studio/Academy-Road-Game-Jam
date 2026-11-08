using System.Collections.Generic;
using UnityEngine;

namespace Sunbearer
{
    public sealed class SolarTrails : MonoBehaviour
    {
        [SerializeField, Min(0.2f)] private float lifetimeAfterFlight = 6f;
        [SerializeField, Min(1f)] private float speedMultiplier = 1.55f;

        private static readonly Color TrailColor = new Color(0.31f, 0.87f, 1f, 0.72f);
        private readonly List<Trace> traces = new List<Trace>();
        private Material material;

        public float SpeedMultiplier => speedMultiplier;

        internal Trace Begin(Vector2 position, float width)
        {
            if (!material)
                material = new Material(Shader.Find("Sprites/Default"));

            var trail = new GameObject("Solar trail");
            trail.transform.SetParent(transform, false);
            var line = trail.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.sortingOrder = -2;
            line.numCornerVertices = 5;
            line.numCapVertices = 5;
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = TrailColor;

            var trace = new Trace(line, position, width * 0.5f);
            traces.Add(trace);
            return trace;
        }

        internal void End(Trace trace)
        {
            trace.ExpiresAt = Time.time + lifetimeAfterFlight;
        }

        public bool Covers(Vector2 point)
        {
            foreach (Trace trace in traces)
            {
                if (Time.time < trace.ExpiresAt && trace.Covers(point))
                    return true;
            }

            return false;
        }

        private void Update()
        {
            for (int i = traces.Count - 1; i >= 0; i--)
            {
                Trace trace = traces[i];
                float remaining = trace.ExpiresAt - Time.time;
                if (remaining <= 0f)
                {
                    Destroy(trace.Line.gameObject);
                    traces.RemoveAt(i);
                    continue;
                }

                if (remaining < 1f)
                {
                    Color faded = TrailColor;
                    faded.a *= remaining;
                    trace.Line.startColor = trace.Line.endColor = faded;
                }
            }
        }

        private void OnDestroy()
        {
            if (material)
                Destroy(material);
        }

        internal sealed class Trace
        {
            private readonly List<Vector2> points = new List<Vector2>(5);
            private readonly float radiusSquared;

            public readonly LineRenderer Line;
            public float ExpiresAt = float.PositiveInfinity;

            public Trace(LineRenderer line, Vector2 start, float radius)
            {
                Line = line;
                radiusSquared = radius * radius;
                points.Add(start);
                points.Add(start);
                Line.positionCount = 2;
                Line.SetPosition(0, start);
                Line.SetPosition(1, start);
            }

            public void Extend(Vector2 position)
            {
                int end = points.Count - 1;
                points[end] = position;
                Line.SetPosition(end, position);
            }

            public void Turn(Vector2 position)
            {
                points.Add(position);
                Line.positionCount = points.Count;
                Line.SetPosition(points.Count - 1, position);
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
