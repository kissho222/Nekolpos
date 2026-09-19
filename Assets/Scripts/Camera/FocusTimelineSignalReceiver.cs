using UnityEngine;

namespace Nekolpos.CameraSystem
{
    public sealed class FocusTimelineSignalReceiver : MonoBehaviour
    {
        [SerializeField] private FocusCameraController focusController;

        private FocusCameraController Controller
        {
            get
            {
                if (focusController == null)
                {
                    focusController = FocusCameraController.Instance != null
                        ? FocusCameraController.Instance
                        : Object.FindFirstObjectByType<FocusCameraController>();
                }

                return focusController;
            }
        }

        public void FocusCatEyes()
        {
            Controller?.FocusCatEyes();
        }

        public void FocusCatPaw()
        {
            Controller?.FocusCatPaw();
        }

        public void FocusCatNose()
        {
            Controller?.FocusCatNose();
        }

        public void FocusCatTail()
        {
            Controller?.FocusCatTail();
        }

        public void ClearFocus()
        {
            Controller?.ClearFocus();
        }

        public void SetFocusEnabled(bool enabled)
        {
            Controller?.SetFocusEnabled(enabled);
        }
    }
}
