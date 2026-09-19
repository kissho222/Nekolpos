using UnityEngine;

public class ControlModeSwitcher : MonoBehaviour
{
    public MonoBehaviour playerController;
    public MonoBehaviour cameraController;

    bool cameraMode = false;

    void Start()
    {
        cameraMode = false;
        ApplyMode();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Y))
        {
            cameraMode = !cameraMode;
            ApplyMode();
        }
    }

    void ApplyMode()
    {
        if (cameraMode)
        {
            if (playerController != null)
            {
                playerController.enabled = false;
            }

            if (cameraController != null)
            {
                cameraController.enabled = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            if (playerController != null)
            {
                playerController.enabled = true;
            }

            if (cameraController != null)
            {
                cameraController.enabled = false;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
