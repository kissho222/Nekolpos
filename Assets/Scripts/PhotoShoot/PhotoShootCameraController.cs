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
        [HideInInspector] public bool acceptKeyboardInput = true;

        private Vector3 targetPosition;
        private Vector3 velocity = Vector3.zero;
        private float xRotation = 0f;
        private float yRotation = 0f;
        private Vector3 lastPosition;
        private Quaternion lastRotation;

        public void SetPose(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            targetPosition = position;
            velocity = Vector3.zero;
            xRotation = rotation.eulerAngles.x;
            if (xRotation > 180f) xRotation -= 360f;
            yRotation = rotation.eulerAngles.y;
            lastPosition = transform.position;
            lastRotation = transform.rotation;
        }

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

            SetPose(transform.position, transform.rotation);
        }

        private void Update()
        {
            if (transform.position != lastPosition || Quaternion.Angle(transform.rotation, lastRotation) > .001f)
                SetPose(transform.position, transform.rotation);
            HandleRotation();
            HandleMovement();
            lastPosition = transform.position;
            lastRotation = transform.rotation;
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
            if (Input.GetKey(KeyCode.LeftAlt)) speed *= .1f;

            float x = acceptKeyboardInput ? Input.GetAxisRaw("Horizontal") : 0f;
            float z = acceptKeyboardInput ? Input.GetAxisRaw("Vertical") : 0f;
            float y = 0f;

            if (acceptKeyboardInput && (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space))) y = 1f;
            if (acceptKeyboardInput && (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl))) y = -1f;

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
            SetPose(transform.position, transform.rotation);
        }
    }
}
