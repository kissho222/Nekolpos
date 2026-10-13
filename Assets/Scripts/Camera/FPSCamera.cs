using UnityEngine;

public class FPSCamera : MonoBehaviour
{
    public Transform player;

    public float mouseSensitivity = 3f;

    float rotX;
    float rotY;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (Nekolpos.System.DebugConsoleInput.IsBlocked || Time.timeScale <= 0f || Nekolpos.System.SettingsPanel.IsOpen) return;
        Vector2 look = Nekolpos.System.SettingsManager.LookInput(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"), mouseSensitivity);
        float mouseX = look.x;
        float mouseY = look.y;

        rotX -= mouseY;
        rotY += mouseX;

        rotX = Mathf.Clamp(rotX, -80f, 80f);

        transform.rotation = Quaternion.Euler(rotX, rotY, 0);

        player.rotation = Quaternion.Euler(0, rotY, 0);
    }
}
