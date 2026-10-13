using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Nekolpos.ActionSystem
{
    public class ResultTextUI : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private CanvasGroup rootGroup;
        [SerializeField] private TextMeshProUGUI resultText;
        private int presentationVersion;

        public void Initialize(Canvas canvas, TMP_FontAsset font)
        {
            rootCanvas = canvas;
            if (resultText != null && resultText.font == null)
            {
                resultText.font = font != null ? font : TMP_Settings.defaultFontAsset;
            }
            HideInstant();
        }

        public async UniTask ShowResultAsync(string message, float durationSeconds,
            global::System.Threading.CancellationToken cancellationToken = default)
        {
            if (rootGroup == null || resultText == null)
            {
                Debug.LogError("[ResultTextUI] Assign the authored result panel and text in the Scene before displaying a timed event.", this);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            int version = ++presentationVersion;
            resultText.text = message;
            BringToFront();
            rootGroup.gameObject.SetActive(true);
            rootGroup.alpha = 1f;
            await UniTask.Delay(global::System.TimeSpan.FromSeconds(Mathf.Max(0.1f, durationSeconds)), cancellationToken: cancellationToken);
            if (version != presentationVersion || rootGroup == null) return;
            rootGroup.alpha = 0f;
            rootGroup.gameObject.SetActive(false);
        }

        public void HideInstant()
        {
            presentationVersion++;
            if (rootGroup == null)
            {
                return;
            }

            rootGroup.alpha = 0f;
            rootGroup.gameObject.SetActive(false);
        }

        public void BringToFront()
        {
            if (rootGroup != null)
            {
                rootGroup.transform.SetAsLastSibling();
            }
        }
    }
}
