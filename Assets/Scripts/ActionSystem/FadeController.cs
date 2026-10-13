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
        [SerializeField] private TransitionBackgroundView transitionBackground;
        [SerializeField] private RectMask2D transitionMask;
        private Vector4 originalMaskPadding;
        private bool backgroundPrepared;
        public float WipeBoundary { get; private set; }

        private bool isInitialized;
        private int fadeRequestVersion;
        private bool wipeInProgress;
        private RectTransform wipeRect;
        private Vector2 originalAnchorMin, originalAnchorMax, originalOffsetMin, originalOffsetMax;

        /// <summary>
        /// Raised once an active fade has reached a fully opaque black overlay.
        /// Consumers that must update scene state while hidden should synchronize on this
        /// event instead of duplicating the fade duration.
        /// </summary>
        public event Action BecameOpaque;

        /// <summary>
        /// Raised once an active fade has reached a fully transparent overlay.
        /// </summary>
        public event Action BecameTransparent;

        public bool IsOpaque => !wipeInProgress && fadeGroup != null && fadeGroup.alpha >= 0.999f;
        public Transform OverlayRoot => fadeGroup != null ? fadeGroup.transform : null;
        public bool IsTransparent => fadeGroup == null || fadeGroup.alpha <= 0.001f;

        public void ResetForTitleReload()
        {
            CancelFade();
            BecameOpaque = null;
            BecameTransparent = null;
        }

        private void OnDisable() => CancelFade();

        private void CancelFade()
        {
            fadeRequestVersion++;
            RestoreWipeLayout();
            transitionBackground?.EndPresentation();
            backgroundPrepared = false;
            if (fadeGroup == null) return;
            fadeGroup.alpha = 0f;
            fadeGroup.blocksRaycasts = false;
            fadeGroup.interactable = false;
            fadeGroup.gameObject.SetActive(false);
        }

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
            RestoreWipeLayout();
            ApplyAlpha(alpha);
        }

        public void WipeToBlack(float duration) => WipeAsync(true, duration).Forget();
        public void WipeFromBlack(float duration) => WipeAsync(false, duration).Forget();

        public void BeginTimeTransition()
        {
            if (PrepareBackground()) transitionBackground.BeginTime();
        }

        public void BeginLocationTransition()
        {
            if (PrepareBackground()) transitionBackground.BeginLocation();
        }

        public void BeginRevivalTransition()
        {
            if (transitionBackground == null) return;
            if (!transitionBackground.ValidateRevivalConfiguration(out string error))
            {
                Debug.LogWarning($"[TransitionBackground] {error} Black fade remains available.", this);
                transitionBackground.gameObject.SetActive(false);
                backgroundPrepared = false;
                return;
            }
            if (PrepareBackground()) transitionBackground.BeginRevivalDark();
        }

        public UniTask PlayRevivalSunriseAsync()
        {
            if (!backgroundPrepared || transitionBackground == null) return UniTask.CompletedTask;
            transitionBackground.StartRevivalSunrise();
            return transitionBackground.WaitForCompletionAsync();
        }

        public UniTask WaitForTransitionBackgroundAsync() => transitionBackground != null
            ? transitionBackground.WaitForCompletionAsync() : UniTask.CompletedTask;

        private bool PrepareBackground()
        {
            if (transitionBackground == null) return false;
            if (!transitionBackground.ValidateConfiguration(out string error))
            {
                Debug.LogWarning($"[TransitionBackground] {error} Black fade remains available.", this);
                transitionBackground.gameObject.SetActive(false);
                return false;
            }
            transitionBackground.gameObject.SetActive(true);
            backgroundPrepared = true;
            return true;
        }

        private async UniTask WipeAsync(bool cover, float duration)
        {
            EnsureUi();
            BringToFront();
            if (fadeGroup == null) return;
            int version = ++fadeRequestVersion;
            RestoreWipeLayout();
            if (cover)
            {
                BeginLocationTransition();
            }
            wipeRect = fadeGroup.transform as RectTransform;
            if (wipeRect == null) { ApplyAlpha(cover ? 1f : 0f); return; }
            originalAnchorMin = wipeRect.anchorMin;
            originalAnchorMax = wipeRect.anchorMax;
            originalOffsetMin = wipeRect.offsetMin;
            originalOffsetMax = wipeRect.offsetMax;
            if (transitionMask != null) originalMaskPadding = transitionMask.padding;
            wipeInProgress = true;
            fadeGroup.gameObject.SetActive(true);
            fadeGroup.blocksRaycasts = true;
            fadeGroup.interactable = true;
            SetAlpha(1f);
            float elapsed = 0f;
            float seconds = float.IsNaN(duration) || float.IsInfinity(duration) ? fadeDuration : Mathf.Max(.05f, duration);
            ApplyWipeProgress(cover, 0f);
            while (elapsed < seconds)
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
                if (version != fadeRequestVersion || fadeGroup == null) return;
                elapsed += Time.unscaledDeltaTime;
                ApplyWipeProgress(cover, Mathf.Clamp01(elapsed / seconds));
            }
            RestoreWipeLayout();
            ApplyAlpha(cover ? 1f : 0f);
        }

        private void ApplyWipeProgress(bool cover, float progress)
        {
            WipeBoundary = progress;
            if (transitionMask != null)
            {
                float width = wipeRect.rect.width;
                transitionMask.padding = new Vector4(cover ? 0f : progress * width, 0f,
                    cover ? (1f - progress) * width : 0f, 0f);
                return;
            }
            // Both boundaries advance left to right; opening exposes the left side first.
            wipeRect.anchorMin = new Vector2(cover ? 0f : progress, 0f);
            wipeRect.anchorMax = new Vector2(cover ? progress : 1f, 1f);
            wipeRect.offsetMin = Vector2.zero;
            wipeRect.offsetMax = Vector2.zero;
        }

        private void RestoreWipeLayout()
        {
            if (wipeInProgress && wipeRect != null)
            {
                if (transitionMask != null) transitionMask.padding = originalMaskPadding;
                wipeRect.anchorMin = originalAnchorMin;
                wipeRect.anchorMax = originalAnchorMax;
                wipeRect.offsetMin = originalOffsetMin;
                wipeRect.offsetMax = originalOffsetMax;
            }
            wipeInProgress = false;
            wipeRect = null;
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
            RestoreWipeLayout();
            if (targetAlpha > 0f && !backgroundPrepared && transitionBackground != null)
                transitionBackground.gameObject.SetActive(false);

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
                transitionBackground?.EndPresentation();
                backgroundPrepared = false;
                fadeGroup.blocksRaycasts = false;
                fadeGroup.interactable = false;
                fadeGroup.gameObject.SetActive(false);
                BecameTransparent?.Invoke();
            }
            else if (targetAlpha >= 1f)
            {
                BecameOpaque?.Invoke();
            }
        }

        private void EnsureUi()
        {
            if (rootCanvas == null)
            {
                ChatUIController chatUi = GetComponent<ChatUIController>();
                chatUi ??= FindFirstObjectByType<ChatUIController>(FindObjectsInactive.Include);
                if (chatUi != null && chatUi.messageText != null)
                {
                    rootCanvas = chatUi.messageText.canvas != null
                        ? chatUi.messageText.canvas.rootCanvas
                        : null;
                }
            }

            if (rootCanvas == null)
            {
                rootCanvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
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
