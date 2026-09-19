using System;
using Cysharp.Threading.Tasks;
using Nekolpos.System;
using UnityEngine;
using UnityEngine.UI;

namespace Nekolpos.ActionSystem
{
    public class FadeController : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private CanvasGroup fadeGroup;
        [SerializeField] [Min(0.05f)] private float fadeDuration = 0.35f;

        private bool isInitialized;
        private int fadeRequestVersion;

        public void Initialize(Canvas canvas)
        {
            if (canvas != null)
            {
                rootCanvas = canvas;
            }

            EnsureUi();
            if (isInitialized)
            {
                return;
            }

            if (fadeGroup == null)
            {
                return;
            }

            SetAlpha(0f);
            fadeGroup.blocksRaycasts = false;
            fadeGroup.interactable = false;
            fadeGroup.gameObject.SetActive(false);

            isInitialized = true;
        }

        /// <summary>
        /// Signal Receiver / Timeline 用の暗転開始入口です。
        /// </summary>
        public void FadeToBlack()
        {
            FadeOutAsync().Forget();
        }

        public void FadeToBlack(float duration)
        {
            FadeOutAsync(duration).Forget();
        }

        /// <summary>
        /// Signal Receiver / Timeline 用の明転開始入口です。
        /// </summary>
        public void FadeFromBlack()
        {
            FadeInAsync().Forget();
        }

        public void FadeFromBlack(float duration)
        {
            FadeInAsync(duration).Forget();
        }

        /// <summary>
        /// Timeline の Signal で即時に暗転状態へ切り替えるための入口です。
        /// </summary>
        public void SetOpaque()
        {
            SetFadeAlpha(1f);
        }

        /// <summary>
        /// Timeline の Signal で即時に透明状態へ切り替えるための入口です。
        /// </summary>
        public void SetTransparent()
        {
            SetFadeAlpha(0f);
        }

        /// <summary>
        /// 現在実行中のフェードを中止し、指定した不透明度を即時に反映します。
        /// </summary>
        public void SetFadeAlpha(float alpha)
        {
            EnsureUi();
            BringToFront();
            fadeRequestVersion++;
            ApplyAlpha(alpha);
        }

        public UniTask FadeOutAsync()
        {
            return FadeOutAsync(fadeDuration);
        }

        public UniTask FadeOutAsync(float duration)
        {
            EnsureUi();
            BringToFront();
            return TweenAlphaAsync(1f, duration);
        }

        public UniTask FadeInAsync()
        {
            return FadeInAsync(fadeDuration);
        }

        public UniTask FadeInAsync(float duration)
        {
            EnsureUi();
            BringToFront();
            return TweenAlphaAsync(0f, duration);
        }

        public void BringToFront()
        {
            if (fadeGroup != null)
            {
                fadeGroup.transform.SetAsLastSibling();
            }
        }

        private async UniTask TweenAlphaAsync(float targetAlpha, float duration)
        {
            if (fadeGroup == null)
            {
                return;
            }

            int requestVersion = ++fadeRequestVersion;

            fadeGroup.gameObject.SetActive(true);
            fadeGroup.blocksRaycasts = true;
            fadeGroup.interactable = true;

            float startAlpha = fadeGroup.alpha;
            float elapsed = 0f;
            float safeDuration = Mathf.Max(0.05f, duration);

            while (elapsed < safeDuration)
            {
                if (requestVersion != fadeRequestVersion)
                {
                    return;
                }

                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                SetAlpha(Mathf.Lerp(startAlpha, targetAlpha, t));
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (requestVersion != fadeRequestVersion)
            {
                return;
            }

            ApplyAlpha(targetAlpha);
        }

        private void ApplyAlpha(float targetAlpha)
        {
            if (fadeGroup == null)
            {
                return;
            }

            fadeGroup.gameObject.SetActive(true);
            fadeGroup.blocksRaycasts = targetAlpha > 0f;
            fadeGroup.interactable = targetAlpha > 0f;
            SetAlpha(targetAlpha);
            if (targetAlpha <= 0f)
            {
                fadeGroup.blocksRaycasts = false;
                fadeGroup.interactable = false;
                fadeGroup.gameObject.SetActive(false);
            }
        }

        private void EnsureUi()
        {
            if (rootCanvas == null)
            {
                ChatUIController chatUi = GetComponent<ChatUIController>();
                if (chatUi != null && chatUi.messageText != null)
                {
                    rootCanvas = chatUi.messageText.canvas;
                }
            }

            if (rootCanvas == null || fadeGroup != null)
            {
                return;
            }

            GameObject root = new GameObject("ActionFadeOverlay", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            root.transform.SetParent(rootCanvas.transform, false);
            root.transform.SetAsLastSibling();

            RectTransform rectTransform = root.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            Image image = root.GetComponent<Image>();
            image.color = Color.black;

            fadeGroup = root.GetComponent<CanvasGroup>();
        }

        private void SetAlpha(float value)
        {
            if (fadeGroup == null)
            {
                return;
            }

            fadeGroup.alpha = Mathf.Clamp01(value);
        }
    }
}
