using UnityEngine;

/// <summary>
/// 写真撮影など、衝突を必要としない既存の自由カメラ用コントローラー。
/// 通常プレイの安全歩行には PlayerSafetyMovementController を使用する。
/// </summary>
public sealed class FreeCameraController : MonoBehaviour
{
    public float moveSpeed = 3f;
    public float mouseSensitivity = 3f;

    private float rotationX;
    private float rotationY;

    private void Update()
    {
        rotationX -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        rotationY += Input.GetAxis("Mouse X") * mouseSensitivity;
        transform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);

        Vector3 move = transform.forward * Input.GetAxis("Vertical") + transform.right * Input.GetAxis("Horizontal");
        transform.position += move * moveSpeed * Time.deltaTime;
    }
}
