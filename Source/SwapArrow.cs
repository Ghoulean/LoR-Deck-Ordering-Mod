using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DeckOrdering
{
    internal class SwapArrow : MaskableGraphic
    {
        private const int NumPoints = 50;
        private const float HeightParam = 1f;
        private const float LineWidth = 9f;
        private const float HeadLength = 30f;
        private const float HeadWidth = 30f;
        private const float OutlineWidth = 3f;

        private static readonly Color FillColor = new Color(1f, 0.8f, 0.3f, 1f);
        private static readonly Color OutlineColor = new Color(0.08f, 0.05f, 0.02f, 0.85f);

        private sealed class Shape
        {
            internal static readonly Shape Empty = new Shape(new Vector2[0], new int[0], Color.clear);

            internal readonly IReadOnlyList<Vector2> Vertices;
            internal readonly IReadOnlyList<int> Triangles;
            internal readonly Color Color;

            internal Shape(IReadOnlyList<Vector2> vertices, IReadOnlyList<int> triangles, Color color)
            {
                Vertices = vertices;
                Triangles = triangles;
                Color = color;
            }
        }

        private Vector2 from;
        private Vector2 to;

        internal static SwapArrow Create(Transform parent)
        {
            GameObject go = new GameObject("DeckOrdering_SwapArrow", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            SwapArrow arrow = go.AddComponent<SwapArrow>();
            arrow.raycastTarget = false;
            arrow.enabled = false;
            return arrow;
        }

        internal void Show(Vector3 worldFrom, Vector3 worldTo)
        {
            from = rectTransform.InverseTransformPoint(worldFrom);
            to = rectTransform.InverseTransformPoint(worldTo);
            enabled = true;
            SetVerticesDirty();
        }

        internal void Hide()
        {
            enabled = false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            foreach (Shape shape in Shapes(from, to))
            {
                Emit(vh, shape);
            }
        }

        private static IEnumerable<Shape> Shapes(Vector2 from, Vector2 to)
        {
            float distance = Vector2.Distance(from, to);
            if (distance < 1f)
            {
                return Enumerable.Empty<Shape>();
            }

            IReadOnlyList<Vector2> points = Curve(from, to, distance);
            Vector2 endDir = (points[NumPoints - 1] - points[NumPoints - 2]).normalized;
            Vector2 startDir = (points[0] - points[1]).normalized;

            IReadOnlyList<Vector2> shaft = Shaft(points, from, to, HeadLength * 0.6f);

            return new[]
            {
                Strip(shaft, LineWidth + OutlineWidth * 2f, OutlineColor),
                Head(to + endDir * OutlineWidth, endDir, HeadLength + OutlineWidth * 2f, HeadWidth + OutlineWidth * 2.5f, OutlineColor),
                Head(from + startDir * OutlineWidth, startDir, HeadLength + OutlineWidth * 2f, HeadWidth + OutlineWidth * 2.5f, OutlineColor),
                Strip(shaft, LineWidth, FillColor),
                Head(to, endDir, HeadLength, HeadWidth, FillColor),
                Head(from, startDir, HeadLength, HeadWidth, FillColor),
            };
        }

        private static IReadOnlyList<Vector2> Curve(Vector2 from, Vector2 to, float distance)
        {
            Vector2 control = (from + to) * 0.5f + UpNormal(from, to) * (distance * 0.25f * (HeightParam * 0.7f));
            return Enumerable.Range(0, NumPoints).Select(i => Bezier((float)i / (NumPoints - 1), from, control, to)).ToList();
        }

        private static Vector2 UpNormal(Vector2 from, Vector2 to)
        {
            Vector2 normal = new Vector2(-(to - from).y, (to - from).x).normalized;
            return normal.y < 0f || (normal.y == 0f && normal.x > 0f) ? -normal : normal;
        }

        private static IReadOnlyList<Vector2> Shaft(IEnumerable<Vector2> points, Vector2 from, Vector2 to, float trim)
        {
            return points.Where(p => Vector2.Distance(p, from) >= trim && Vector2.Distance(p, to) >= trim).ToList();
        }

        private static Vector2 Bezier(float t, Vector2 p0, Vector2 p1, Vector2 p2)
        {
            float u = 1f - t;
            return u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }

        private static Vector2 Perpendicular(Vector2 v)
        {
            return new Vector2(-v.y, v.x);
        }

        private static Shape Strip(IReadOnlyList<Vector2> pts, float width, Color color)
        {
            if (pts.Count < 2)
            {
                return Shape.Empty;
            }

            float half = width * 0.5f;
            IReadOnlyList<Vector2> vertices = pts.SelectMany((p, i) =>
            {
                Vector2 side = Perpendicular((pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized) * half;
                return new[] { p + side, p - side };
            }).ToList();
            IReadOnlyList<int> triangles = Enumerable.Range(0, pts.Count - 1)
                .SelectMany(i => new[] { i * 2, i * 2 + 1, i * 2 + 3, i * 2, i * 2 + 3, i * 2 + 2 })
                .ToList();
            return new Shape(vertices, triangles, color);
        }

        private static Shape Head(Vector2 tip, Vector2 dir, float length, float width, Color color)
        {
            Vector2 baseCenter = tip - dir * length;
            Vector2 side = Perpendicular(dir) * (width * 0.5f);
            return new Shape(new[] { tip, baseCenter + side, baseCenter - side }, new[] { 0, 1, 2 }, color);
        }

        private static void Emit(VertexHelper vh, Shape shape)
        {
            int start = vh.currentVertCount;
            foreach (Vector2 vertex in shape.Vertices)
            {
                vh.AddVert(vertex, shape.Color, Vector2.zero);
            }

            for (int i = 0; i + 2 < shape.Triangles.Count; i += 3)
            {
                vh.AddTriangle(start + shape.Triangles[i], start + shape.Triangles[i + 1], start + shape.Triangles[i + 2]);
            }
        }
    }
}
