using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Backgammon.Conversation
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ConversationRadarChartGraphic : MaskableGraphic
    {
        [SerializeField] private Color axisColor = new(1f, 1f, 1f, 0.4f);
        [SerializeField] private Color fillColor = new(0.95f, 0.45f, 0.25f, 0.2f);
        [SerializeField] private Color outlineColor = new(0.95f, 0.45f, 0.25f, 1f);
        [SerializeField] private float maxValue = 100f;
        [SerializeField] private float axisThickness = 2f;
        [SerializeField] private float outlineThickness = 3f;
        [SerializeField] private int axisCount = 6;

        private readonly List<float> values = new();

        protected override void Awake()
        {
            EnsureCanvasRenderer();
            base.Awake();
        }

        protected override void OnEnable()
        {
            EnsureCanvasRenderer();
            base.OnEnable();
        }

        public void SetValues(IReadOnlyList<float> sourceValues)
        {
            values.Clear();
            if (sourceValues != null)
            {
                for (var i = 0; i < sourceValues.Count; i++)
                {
                    values.Add(Mathf.Clamp(sourceValues[i], 0f, maxValue));
                }
            }

            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            var count = Mathf.Max(3, axisCount);
            EnsureValueCount(count);

            var rect = rectTransform.rect;
            var center = rect.center;
            var radius = Mathf.Min(rect.width, rect.height) * 0.35f;
            if (radius <= 0f)
            {
                return;
            }

            var outerPoints = new Vector2[count];
            var polygonPoints = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                var angle = Mathf.PI * 0.5f - (Mathf.PI * 2f * i / count);
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                outerPoints[i] = center + direction * radius;
                polygonPoints[i] = center + direction * radius * (values[i] / Mathf.Max(0.0001f, maxValue));
                AddLine(vh, center, outerPoints[i], axisThickness, axisColor);
            }

            AddPolygonFill(vh, center, polygonPoints, fillColor);
            for (var i = 0; i < count; i++)
            {
                AddLine(vh, polygonPoints[i], polygonPoints[(i + 1) % count], outlineThickness, outlineColor);
            }
        }

        private void EnsureValueCount(int count)
        {
            while (values.Count < count)
            {
                values.Add(0f);
            }
        }

        private void EnsureCanvasRenderer()
        {
            if (GetComponent<CanvasRenderer>() == null)
            {
                gameObject.AddComponent<CanvasRenderer>();
            }
        }

        private static void AddPolygonFill(VertexHelper vh, Vector2 center, IReadOnlyList<Vector2> polygonPoints, Color32 color)
        {
            var startIndex = vh.currentVertCount;
            vh.AddVert(center, color, Vector2.zero);
            for (var i = 0; i < polygonPoints.Count; i++)
            {
                vh.AddVert(polygonPoints[i], color, Vector2.zero);
            }

            for (var i = 0; i < polygonPoints.Count; i++)
            {
                var next = i + 1 < polygonPoints.Count ? i + 1 : 0;
                vh.AddTriangle(startIndex, startIndex + 1 + i, startIndex + 1 + next);
            }
        }

        private static void AddLine(VertexHelper vh, Vector2 start, Vector2 end, float thickness, Color32 color)
        {
            var direction = (end - start).normalized;
            var normal = new Vector2(-direction.y, direction.x) * (thickness * 0.5f);
            var startIndex = vh.currentVertCount;

            vh.AddVert(start - normal, color, Vector2.zero);
            vh.AddVert(start + normal, color, Vector2.zero);
            vh.AddVert(end + normal, color, Vector2.zero);
            vh.AddVert(end - normal, color, Vector2.zero);

            vh.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
            vh.AddTriangle(startIndex, startIndex + 2, startIndex + 3);
        }
    }
}
