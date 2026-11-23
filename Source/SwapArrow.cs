using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DeckOrdering
{
    // Double-headed arrow between the two cards a drag would swap.
    //
    // The curve follows the battle clash arrow (BattleUnitTargetArrowUI.Update): a quadratic Bezier
    // sampled at 50 points, whose control point is the midpoint pushed out by
    // distance * 0.25 * (heightparam * 0.7), with heightparam = 1 as for clashes. The arrowheads point
    // along the curve's tangent at each end, as the clash arrow's two heads do. The clash arrow itself
    // is a world-space LineRenderer with battle-scene sprites, which the deck editor's UI canvas cannot
    // use, so the same shape is built here as a UI mesh. One difference: the clash arrow always bends
    // towards world up; this one bends perpendicular to the line, towards the top of the screen, so
    // cards stacked vertically still get a curve.
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

        private readonly List<Vector2> points = new List<Vector2>(NumPoints);
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
            // Hidden by disabling the component, not the GameObject: a nested Canvas that is configured
            // while its GameObject is inactive loses its sorting override when reactivated.
            arrow.enabled = false;
            return arrow;
        }

        // Ends are world positions; the tips land exactly on them.
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
            float distance = Vector2.Distance(from, to);
            if (distance < 1f)
                return;

            Vector2 normal = new Vector2(-(to - from).y, (to - from).x).normalized;
            if (normal.y < 0f || (normal.y == 0f && normal.x > 0f))
                normal = -normal;
            Vector2 control = (from + to) * 0.5f + normal * (distance * 0.25f * (HeightParam * 0.7f));

            points.Clear();
            for (int i = 0; i < NumPoints; i++)
                points.Add(Bezier((float)i / (NumPoints - 1), from, control, to));

            Vector2 endDir = (points[NumPoints - 1] - points[NumPoints - 2]).normalized;
            Vector2 startDir = (points[0] - points[1]).normalized;

            // Stop the shaft inside each head so it does not poke past the tip.
            List<Vector2> shaft = new List<Vector2>(NumPoints);
            float trim = HeadLength * 0.6f;
            foreach (Vector2 p in points)
            {
                if (Vector2.Distance(p, from) >= trim && Vector2.Distance(p, to) >= trim)
                    shaft.Add(p);
            }

            AddStrip(vh, shaft, LineWidth + OutlineWidth * 2f, OutlineColor);
            AddHead(vh, to + endDir * OutlineWidth, endDir, HeadLength + OutlineWidth * 2f, HeadWidth + OutlineWidth * 2.5f, OutlineColor);
            AddHead(vh, from + startDir * OutlineWidth, startDir, HeadLength + OutlineWidth * 2f, HeadWidth + OutlineWidth * 2.5f, OutlineColor);
            AddStrip(vh, shaft, LineWidth, FillColor);
            AddHead(vh, to, endDir, HeadLength, HeadWidth, FillColor);
            AddHead(vh, from, startDir, HeadLength, HeadWidth, FillColor);
        }

        private static Vector2 Bezier(float t, Vector2 p0, Vector2 p1, Vector2 p2)
        {
            float u = 1f - t;
            return u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }

        private static void AddStrip(VertexHelper vh, List<Vector2> pts, float width, Color color)
        {
            if (pts.Count < 2)
                return;
            int start = vh.currentVertCount;
            float half = width * 0.5f;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 tangent = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                Vector2 side = new Vector2(-tangent.y, tangent.x) * half;
                vh.AddVert(pts[i] + side, color, Vector2.zero);
                vh.AddVert(pts[i] - side, color, Vector2.zero);
            }
            for (int i = 0; i < pts.Count - 1; i++)
            {
                int a = start + i * 2;
                vh.AddTriangle(a, a + 1, a + 3);
                vh.AddTriangle(a, a + 3, a + 2);
            }
        }

        private static void AddHead(VertexHelper vh, Vector2 tip, Vector2 dir, float length, float width, Color color)
        {
            Vector2 baseCenter = tip - dir * length;
            Vector2 side = new Vector2(-dir.y, dir.x) * (width * 0.5f);
            int start = vh.currentVertCount;
            vh.AddVert(tip, color, Vector2.zero);
            vh.AddVert(baseCenter + side, color, Vector2.zero);
            vh.AddVert(baseCenter - side, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
        }
    }
}
