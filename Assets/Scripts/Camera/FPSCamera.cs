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
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        rotX -= mouseY;
        rotY += mouseX;

        rotX = Mathf.Clamp(rotX, -80f, 80f);

        transform.rotation = Quaternion.Euler(rotX, rotY, 0);

        player.rotation = Quaternion.Euler(0, rotY, 0);
    }
}
