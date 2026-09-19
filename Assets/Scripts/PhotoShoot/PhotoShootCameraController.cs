using UnityEngine;

namespace Nekolpos.PhotoShoot
{
    public class PhotoShootCameraController : MonoBehaviour
    {
        [Header("Movement Settings")]
        public float moveSpeed = 10f;
        public float fastMoveSpeed = 30f;
        public float smoothTime = 0.1f;

        [Header("Rotation Settings")]
        public float mouseSensitivity = 2f;

        private Vector3 targetPosition;
        private Vector3 velocity = Vector3.zero;
        private float xRotation = 0f;
        private float yRotation = 0f;

        private void Start()
        {
            Camera cam = GetComponent<Camera>();
            if (cam != null)
            {
                // To make sure Depth of Field is rendered in Play Mode
                cam.depthTextureMode |= DepthTextureMode.Depth;
                // Force Top Priority
                cam.depth = 100f;
                gameObject.tag = "MainCamera";
            }

            targetPosition = transform.position;
            Vector3 angles = transform.eulerAngles;
            xRotation = angles.x;
            yRotation = angles.y;
        }

        private void Update()
        {
            HandleRotation();
            HandleMovement();
        }

        private void HandleRotation()
        {
            // Only rotate when holding right mouse button
            if (Input.GetMouseButton(1))
            {
                Cursor.lockState = CursorLockMode.Locked;

                float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
                float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

                yRotation += mouseX;
                xRotation -= mouseY;
                xRotation = Mathf.Clamp(xRotation, -90f, 90f);

                transform.localRotation = Quaternion.Euler(xRotation, yRotation, 0f);
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void HandleMovement()
        {
            float speed = Input.GetKey(KeyCode.LeftShift) ? fastMoveSpeed : moveSpeed;

            float x = Input.GetAxisRaw("Horizontal");
            float z = Input.GetAxisRaw("Vertical");
            float y = 0f;

            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) y = 1f;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl)) y = -1f;

            Vector3 move = transform.right * x + transform.up * y + transform.forward * z;
            
            if (move.magnitude > 0.1f)
            {
                targetPosition += move.normalized * speed * Time.deltaTime;
            }

            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, smoothTime);
        }

        private void OnEnable()
        {
            // Reset target position when enabled so it doesn't fly back to an old target
            targetPosition = transform.position;
        }
    }
}
