using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System
{
    // Vertex colours use the standard UI shader and its RectMask2D/WebGL support.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TransitionSkyGraphic : MaskableGraphic
    {
        [SerializeField] private Color topColor = new Color(.58f, .8f, .91f);
        [SerializeField] private Color bottomColor = new Color(1f, .94f, .8f);
        public Color TopColor => topColor;
        public Color BottomColor => bottomColor;

        public void SetColors(Color top, Color bottom)
        {
            top.a = bottom.a = 1f;
            topColor = top;
            bottomColor = bottom;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            Rect r = GetPixelAdjustedRect();
            helper.AddVert(new Vector3(r.xMin, r.yMin), bottomColor, new Vector2(0, 0));
            helper.AddVert(new Vector3(r.xMin, r.yMax), topColor, new Vector2(0, 1));
            helper.AddVert(new Vector3(r.xMax, r.yMax), topColor, new Vector2(1, 1));
            helper.AddVert(new Vector3(r.xMax, r.yMin), bottomColor, new Vector2(1, 0));
            helper.AddTriangle(0, 1, 2);
            helper.AddTriangle(2, 3, 0);
        }
    }
}
