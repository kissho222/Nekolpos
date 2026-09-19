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
    public sealed class OpenBetaIntroPopupPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private TMP_Text startButtonLabel;
        [SerializeField] private TMP_Text aboutButtonLabel;
        [SerializeField] private TMP_Text quitButtonLabel;
        [SerializeField] private TMP_Text steamButtonLabel;
        [SerializeField] private TMP_Text worldviewButtonLabel;
        [SerializeField] private TMP_Text trainingDataButtonLabel;
        [SerializeField] private TMP_Text creditButtonLabel;
        [SerializeField] private TMP_Text debugButtonLabel;
        [SerializeField] private TMP_Text precautionsText;
        [SerializeField] private Button startButton;
        [SerializeField] private Button aboutButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button steamButton;
        [SerializeField] private Button worldviewButton;
        [SerializeField] private Button trainingDataButton;
        [SerializeField] private Button creditButton;
        [SerializeField] private Button debugButton;

        public void Configure(string body, string startLabel, string aboutLabel, string quitLabel, Action onStart, Action onAbout, Action onQuit)
        {
            ResolveButtonReferences();
            if (bodyText != null) bodyText.text = body;
            if (startButtonLabel != null) startButtonLabel.text = startLabel;
            if (aboutButtonLabel != null) aboutButtonLabel.text = aboutLabel;
            if (quitButtonLabel != null) quitButtonLabel.text = quitLabel;
            Bind(startButton, onStart);
            Bind(aboutButton, onAbout);
            Bind(quitButton, onQuit);
        }

        public void ConfigureAuxiliaryButtons(
            string steamLabel,
            string worldviewLabel,
            string trainingDataLabel,
            Action onSteam,
            Action onWorldview,
            Action onTrainingData)
        {
            ResolveButtonReferences();
            if (steamButtonLabel != null) steamButtonLabel.text = steamLabel;
            if (worldviewButtonLabel != null) worldviewButtonLabel.text = worldviewLabel;
            if (trainingDataButtonLabel != null) trainingDataButtonLabel.text = trainingDataLabel;
            Bind(steamButton, onSteam);
            Bind(worldviewButton, onWorldview);
            Bind(trainingDataButton, onTrainingData);
        }

        public void ConfigureCreditButton(Action onCredit)
        {
            ResolveButtonReferences();
            Bind(creditButton, onCredit);
        }

        public void ConfigureDebugButton(string label, Action onDebugStart)
        {
            ResolveButtonReferences();
            if (debugButtonLabel != null)
            {
                debugButtonLabel.text = label;
            }

            Bind(debugButton, onDebugStart);
        }

        public void SetPrecautions(string text)
        {
            ResolveButtonReferences();
            ResolveTextReferences();
            if (precautionsText != null)
            {
                precautionsText.text = text ?? string.Empty;
            }
        }

        public void SetQuitButtonActive(bool active)
        {
            ResolveButtonReferences();
            if (quitButton != null)
            {
                quitButton.gameObject.SetActive(active);
            }
        }

        private void ResolveButtonReferences()
        {
            ResolveTextReferences();
            ResolveButton("StartDemoButton", ref startButton, ref startButtonLabel);
            ResolveButton("AboutButton", ref aboutButton, ref aboutButtonLabel);
            ResolveButton("QuitButton", ref quitButton, ref quitButtonLabel);
            ResolveButton("SteamButton", ref steamButton, ref steamButtonLabel);
            ResolveButton("WorldviewButton", ref worldviewButton, ref worldviewButtonLabel);
            ResolveButton("TrainingDataButton", ref trainingDataButton, ref trainingDataButtonLabel);
            ResolveButton("CreditButton", ref creditButton, ref creditButtonLabel);
            ResolveButton("DebugButton", ref debugButton, ref debugButtonLabel);
            EnsureDebugButton();
        }

        private void EnsureDebugButton()
        {
            if (debugButton != null || startButton == null)
            {
                return;
            }

            // Older title scenes did not serialize DebugButton yet. Clone the established button style
            // so the same bootstrap can still expose the requested development entry point.
            debugButton = Instantiate(startButton, startButton.transform.parent);
            debugButton.name = "DebugButton";
            RectTransform startRect = startButton.transform as RectTransform;
            RectTransform debugRect = debugButton.transform as RectTransform;
            if (startRect != null && debugRect != null)
            {
                debugRect.anchoredPosition = startRect.anchoredPosition + new Vector2(0f, -56f);
            }

            debugButtonLabel = debugButton.GetComponentInChildren<TMP_Text>(true);
        }

        private void ResolveTextReferences()
        {
            if (precautionsText == null)
            {
                Transform target = FindDescendant(transform, "PrecautionsText");
                precautionsText = target != null ? target.GetComponent<TMP_Text>() : null;
            }
        }

        private void ResolveButton(string objectName, ref Button button, ref TMP_Text label)
        {
            if (button != null && label != null) return;
            Transform target = FindDescendant(transform, objectName);
            if (target == null) return;
            button ??= target.GetComponent<Button>();
            label ??= target.GetComponentInChildren<TMP_Text>(true);
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
