using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.System
{
    /// <summary>
    /// 現在の時間帯と、その状態で使えるショートカットを左上に表示するランタイムHUD。
    /// </summary>
    public class TimeShortcutHUD : MonoBehaviour
    {
        private const string CanvasObjectName = "TimeShortcutHUDCanvas";
        private const string LabelObjectName = "TimeShortcutHUDLabel";

        private TextMeshProUGUI shortcutLabel;

        private void Awake()
        {
            EnsureHudExists();
            RefreshLabel();
        }

        private void OnEnable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged += HandleStateChanged;
            }
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= HandleStateChanged;
            }
        }

        private void HandleStateChanged(IGameState _)
        {
            RefreshLabel();
        }

        private void EnsureHudExists()
        {
            if (shortcutLabel != null)
            {
                return;
            }

            Transform existingCanvas = transform.Find(CanvasObjectName);
            Canvas canvas = existingCanvas != null ? existingCanvas.GetComponent<Canvas>() : null;
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject(
                    CanvasObjectName,
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster)
                );
                canvasObject.transform.SetParent(transform, false);

                canvas = canvasObject.GetComponent<Canvas>();
                RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
                canvasRect.anchorMin = Vector2.zero;
                canvasRect.anchorMax = Vector2.one;
                canvasRect.offsetMin = Vector2.zero;
                canvasRect.offsetMax = Vector2.zero;

                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            ConfigureCanvas(canvas);

            Transform labelTransform = canvas.transform.Find(LabelObjectName);
            if (labelTransform == null)
            {
                GameObject labelObject = new GameObject(LabelObjectName, typeof(RectTransform));
                labelObject.transform.SetParent(canvas.transform, false);
                labelTransform = labelObject.transform;
            }

            RectTransform rectTransform = labelTransform as RectTransform;

            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0f, 1f);
            rectTransform.anchoredPosition = new Vector2(24f, -24f);
            rectTransform.sizeDelta = new Vector2(420f, 96f);

            shortcutLabel = labelTransform.GetComponent<TextMeshProUGUI>();
            if (shortcutLabel == null)
            {
                shortcutLabel = labelTransform.gameObject.AddComponent<TextMeshProUGUI>();
            }

            shortcutLabel.font = ResolveGuideFont();
            shortcutLabel.fontSize = 28f;
            shortcutLabel.alignment = TextAlignmentOptions.TopLeft;
            shortcutLabel.textWrappingMode = TextWrappingModes.NoWrap;
            shortcutLabel.color = Color.white;
            shortcutLabel.outlineWidth = 0.18f;
            shortcutLabel.outlineColor = new Color32(0, 0, 0, 255);
        }

        private static void ConfigureCanvas(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            Camera targetCamera = Camera.main;
            if (targetCamera == null)
            {
                targetCamera = Object.FindFirstObjectByType<Camera>();
            }

            if (targetCamera != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = targetCamera;
                canvas.planeDistance = 100f;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
            }

            canvas.sortingOrder = 1000;
        }

        private TMP_FontAsset ResolveGuideFont()
        {
            ChatUIController chatUi = GetComponent<ChatUIController>();
            if (chatUi != null)
            {
                if (chatUi.speakerNameText != null && chatUi.speakerNameText.font != null)
                {
                    return chatUi.speakerNameText.font;
                }

                if (chatUi.messageText != null && chatUi.messageText.font != null)
                {
                    return chatUi.messageText.font;
                }
            }

            return TMP_Settings.defaultFontAsset;
        }

        private void RefreshLabel()
        {
            if (shortcutLabel == null)
            {
                return;
            }

            shortcutLabel.text = BuildShortcutText();
        }

        private string BuildShortcutText()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.CurrentState == null)
            {
                return "現在: 初期化中";
            }

            if (gm.CurrentState is StateMorning)
            {
                return "現在: 朝\n[Space] 昼へ";
            }

            if (gm.CurrentState is StateDay)
            {
                return "現在: 昼\n[E] 夕方へ";
            }

            if (gm.CurrentState is StateEvening)
            {
                return "現在: 夕方\n[N] 夜へ";
            }

            if (gm.CurrentState is StateNight)
            {
                return "現在: 夜\n[Space] 朝へ";
            }

            if (gm.CurrentState is StateDead)
            {
                return "現在: 死亡演出中\n3秒後に朝へ";
            }

            return $"現在: {gm.CurrentState.GetType().Name}";
        }
    }
}
