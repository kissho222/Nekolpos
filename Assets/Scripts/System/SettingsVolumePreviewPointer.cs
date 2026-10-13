using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Nekolpos.System
{
    /// <summary>Mouse/touch lifetime for a settings audio preview; the Slider still owns value changes.</summary>
    public sealed class SettingsVolumePreviewPointer : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IEndDragHandler
    {
        private SettingsPanel panel;
        private Slider slider;
        private bool catVoice;
        private int? activePointer;

        internal void Bind(SettingsPanel target, Slider control, bool voice)
        {
            panel = target;
            slider = control;
            catVoice = voice;
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || activePointer.HasValue ||
                slider == null || !slider.IsActive() || !slider.IsInteractable()) return;
            activePointer = data.pointerId;
            panel.BeginAudioPreview(catVoice);
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (activePointer != data.pointerId) return;
            StopPreview();
        }

        public void OnEndDrag(PointerEventData data) => OnPointerUp(data);
        private void OnDisable() => StopPreview();
        private void OnApplicationFocus(bool focused) { if (!focused) StopPreview(); }
        private void OnApplicationPause(bool paused) { if (paused) StopPreview(); }
        private void StopPreview()
        {
            activePointer = null;
            if (panel != null) panel.EndAudioPreview(catVoice);
        }
    }
}
