using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sunbearer
{
    internal sealed class MonsterNavigation
    {
        private const float CellSize = 0.48f;
        private readonly Vector2 origin;
        private readonly int columns;
        private readonly int rows;
        private readonly int obstacles;
        private readonly int[] neighborOffsets;
        private readonly Vector2[] centers;
        private readonly bool[] open;
        private readonly byte[] links;
        private readonly int[] next;
        private readonly Queue<int> frontier = new Queue<int>();
        private int goalCell = -1;

        internal MonsterNavigation(Rect bounds, float clearance, int obstacles, Func<Vector2, bool> inside)
        {
            origin = bounds.min;
            columns = Mathf.CeilToInt(bounds.width / CellSize);
            rows = Mathf.CeilToInt(bounds.height / CellSize);
            this.obstacles = obstacles;
            neighborOffsets = new[] { 1, -1, columns, -columns };

            int cellCount = columns * rows;
            centers = new Vector2[cellCount];
            open = new bool[cellCount];
            links = new byte[cellCount];
            next = new int[cellCount];
            Array.Fill(next, -1);

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int cell = y * columns + x;
                    Vector2 center = origin + new Vector2(x + 0.5f, y + 0.5f) * CellSize;
                    centers[cell] = center;
                    open[cell] = (inside == null || inside(center)) &&
                        !Physics2D.OverlapCircle(center, clearance, obstacles);
                }
            }

            for (int cell = 0; cell < cellCount; cell++)
            {
                if (!open[cell])
                    continue;

                if (cell % columns + 1 < columns)
                    Connect(cell, cell + 1, 0, clearance);
                if (cell / columns + 1 < rows)
                    Connect(cell, cell + columns, 2, clearance);
            }
        }

        internal void SetGoal(Vector2 position)
        {
            int cell = ClosestCell(position, 4, false, 0f);
            if (cell == goalCell)
                return;

            goalCell = cell;
            Array.Fill(next, -1);
            if (goalCell < 0)
                return;

            // All monsters using this movement mask share the routes back to the player.
            next[goalCell] = goalCell;
            frontier.Enqueue(goalCell);
            while (frontier.Count > 0)
            {
                int parent = frontier.Dequeue();
                for (int direction = 0; direction < neighborOffsets.Length; direction++)
                {
                    if ((links[parent] & (1 << direction)) == 0)
                        continue;

                    int neighbor = parent + neighborOffsets[direction];
                    if (next[neighbor] >= 0)
                        continue;

                    next[neighbor] = parent;
                    frontier.Enqueue(neighbor);
                }
            }
        }

        internal Vector2 NextPoint(Vector2 position, Vector2 goal, float radius)
        {
            if (CanTravel(position, goal, radius))
                return goal;

            int cell = ClosestCell(position, 2, true, radius);
            if (cell < 0)
                return goal;

            Vector2 waypoint = centers[next[cell]];
            return CanTravel(position, waypoint, radius) ? waypoint : centers[cell];
        }

        private void Connect(int from, int to, int direction, float clearance)
        {
            if (!open[to] || !CanTravel(centers[from], centers[to], clearance))
                return;

            links[from] |= (byte)(1 << direction);
            links[to] |= (byte)(1 << (direction + 1));
        }

        private int ClosestCell(Vector2 position, int searchRadius, bool needsRoute, float radius)
        {
            Vector2 grid = (position - origin) / CellSize;
            int cx = Mathf.FloorToInt(grid.x);
            int cy = Mathf.FloorToInt(grid.y);
            int closest = -1;
            float bestDistance = float.PositiveInfinity;
            for (int y = Mathf.Max(0, cy - searchRadius); y <= Mathf.Min(rows - 1, cy + searchRadius); y++)
            {
                for (int x = Mathf.Max(0, cx - searchRadius); x <= Mathf.Min(columns - 1, cx + searchRadius); x++)
                {
                    int cell = y * columns + x;
                    if (!open[cell] || (needsRoute && next[cell] < 0))
                        continue;

                    float distance = (centers[cell] - position).sqrMagnitude;
                    if (distance >= bestDistance || (needsRoute && !CanTravel(position, centers[cell], radius)))
                        continue;

                    closest = cell;
                    bestDistance = distance;
                }
            }

            return closest;
        }

        private bool CanTravel(Vector2 from, Vector2 to, float radius)
        {
            Vector2 motion = to - from;
            float distance = motion.magnitude;
            return distance <= 0.000001f ||
                !Physics2D.CircleCast(from, radius, motion / distance, distance, obstacles).collider;
        }
    }
}
