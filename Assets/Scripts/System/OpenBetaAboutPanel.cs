using System;
using System.Collections;
using System.Collections.Generic;
using Backgammon.Conversation;
using Nekolpos.Audio;
using Nekolpos.Data;
using Nekolpos.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Yarn.Unity;

namespace Nekolpos.System
{
    public sealed class OpenBetaAboutPanel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private TMP_Text steamButtonLabel;
        [SerializeField] private TMP_Text backButtonLabel;
        [SerializeField] private Button steamButton;
        [SerializeField] private Button backButton;
        [SerializeField] private TMP_Text logButtonLabel;
        [SerializeField] private Button logButton;
        [SerializeField] private GameObject normalImage;
        [SerializeField] private GameObject mouseOnImage;

        private bool isHoveringImage;
        private Coroutine clickPulseCoroutine;

        public void Configure(string body, string steamLabel, string backLabel, Action onSteam, Action onBack)
        {
            ResolveReferences();
            SetHoverState(false);
            if (bodyText != null) bodyText.text = body;
            if (steamButtonLabel != null) steamButtonLabel.text = steamLabel;
            if (backButtonLabel != null) backButtonLabel.text = backLabel;
            Bind(steamButton, onSteam);
            Bind(backButton, onBack);
        }

        public void ConfigureLogButton(string label, Action onLog)
        {
            ResolveReferences();
            if (logButtonLabel != null) logButtonLabel.text = label;
            Bind(logButton, onLog);
        }

        private void OnEnable()
        {
            ResolveReferences();
            SetHoverState(false);
        }

        private void OnDisable()
        {
            if (clickPulseCoroutine != null)
            {
                StopCoroutine(clickPulseCoroutine);
                clickPulseCoroutine = null;
            }
        }

        public void OnPointerEnter(PointerEventData eventData) => UpdateHoverStateFromPointer();

        public void OnPointerExit(PointerEventData eventData) => UpdateHoverStateFromPointer();

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!IsPointerOverImage(eventData)) return;

            if (clickPulseCoroutine != null)
            {
                StopCoroutine(clickPulseCoroutine);
            }

            clickPulseCoroutine = StartCoroutine(PlayClickPulse());
        }

        private void Update()
        {
            UpdateHoverStateFromPointer();
        }

        private void UpdateHoverStateFromPointer()
        {
            if (clickPulseCoroutine != null) return;
            if (normalImage == null || mouseOnImage == null) return;
            SetHoverState(IsScreenPointOverImage(Input.mousePosition));
        }

        private void ResolveReferences()
        {
            Transform body = FindDescendant(transform, "BodyText");
            Transform steam = FindDescendant(transform, "SteamButton");
            Transform back = FindDescendant(transform, "BackButton");
            Transform log = FindDescendant(transform, "LogButton");
            Transform image = FindDescendant(transform, "PunchImage") ?? FindDescendant(transform, "Image");
            Transform hoverImage = FindDescendant(transform, "PunchImage2") ?? FindDescendant(transform, "MouseONImage");

            bodyText ??= body != null ? body.GetComponent<TMP_Text>() : null;
            steamButton ??= steam != null ? steam.GetComponent<Button>() : null;
            steamButtonLabel ??= steam != null ? steam.GetComponentInChildren<TMP_Text>(true) : null;
            backButton ??= back != null ? back.GetComponent<Button>() : null;
            backButtonLabel ??= back != null ? back.GetComponentInChildren<TMP_Text>(true) : null;
            logButton ??= log != null ? log.GetComponent<Button>() : null;
            logButtonLabel ??= log != null ? log.GetComponentInChildren<TMP_Text>(true) : null;
            if (image != null) normalImage = image.gameObject;
            if (hoverImage != null) mouseOnImage = hoverImage.gameObject;
        }

        private IEnumerator PlayClickPulse()
        {
            SetHoverState(false);
            yield return new WaitForSeconds(0.1f);
            clickPulseCoroutine = null;
            UpdateHoverStateFromPointer();
        }

        private bool IsPointerOverImage(PointerEventData eventData)
        {
            Vector2 screenPosition = eventData != null ? eventData.position : (Vector2)Input.mousePosition;
            return IsScreenPointOverImage(screenPosition);
        }

        private bool IsScreenPointOverImage(Vector2 screenPosition)
        {
            if (normalImage == null) return false;
            RectTransform imageRect = normalImage.transform as RectTransform;
            if (imageRect == null) return false;

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(imageRect, screenPosition, eventCamera);
        }

        private void SetHoverState(bool isHovered)
        {
            if (mouseOnImage == null) return;

            isHoveringImage = isHovered;
            if (normalImage != null)
            {
                normalImage.SetActive(!isHovered);
            }

            mouseOnImage.SetActive(isHovered);
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (string.Equals(child.name, objectName, StringComparison.Ordinal)) return child;
                Transform nested = FindDescendant(child, objectName);
                if (nested != null) return nested;
            }

            return null;
        }

        private static void Bind(Button button, Action action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action?.Invoke());
        }
    }
}
