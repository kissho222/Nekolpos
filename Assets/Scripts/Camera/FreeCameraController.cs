using UnityEngine;

public class FreeCameraController : MonoBehaviour
{
    public float moveSpeed = 3f;
    public float mouseSensitivity = 3f;

    float rotX;
    float rotY;

    void Update()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        rotX -= mouseY;
        rotY += mouseX;

        transform.rotation = Quaternion.Euler(rotX, rotY, 0);

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 move =
            transform.forward * v +
            transform.right * h;

        transform.position += move * moveSpeed * Time.deltaTime;
    }
}
