using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace Nekolpos.PhotoShoot
{
    public class PhotoShootManager : MonoBehaviour
    {
        public enum ControlMode { Camera, Cat, Human }

        [global::System.Serializable]
        public class CharacterData
        {
            public ControlMode modeType;
            public string characterName;
            public GameObject rootObject;
            public Animator animator;
            public float moveSpeed = 2f;
            public float rotationSpeed = 180f;

            [HideInInspector]
            public List<AnimationClip> availableClips = new List<AnimationClip>();
            [HideInInspector]
            public int currentClipIndex = -1;
            [HideInInspector]
            public PlayableGraph playableGraph;
            [HideInInspector]
            public Transform headBone; // Used for generic rig lookAt
        }

        public ControlMode currentMode = ControlMode.Camera;
        public PhotoShootCameraController cameraController;
        public List<CharacterData> characters = new List<CharacterData>();
        public Transform cameraTransform;

        [Header("Head Tracking")]
        public bool isHeadTrackingEnabled = false;
        private float headTrackingWeight = 0f;
        private bool hasInitializedLookTarget = false;
        private Vector3 currentLookTarget;

        private CharacterData CurrentCharacter 
        {
            get {
                foreach(var c in characters) {
                    if (c.modeType == currentMode) return c;
                }
                return null;
            }
        }

        private void Start()
        {
            // Lock mode to Camera initially
            if (cameraController != null) cameraController.enabled = true;
            InitializeAnimators();
            Debug.Log($"[PhotoShoot] Mode Switched to: {currentMode}");
        }

        private void Update()
        {
            HandleModeSwitching();
            
            if (Input.GetKeyDown(KeyCode.R))
            {
                isHeadTrackingEnabled = !isHeadTrackingEnabled;
                Debug.Log($"[PhotoShoot] Mouse Head Tracking: {(isHeadTrackingEnabled ? "ON" : "OFF")}");
            }

            if (currentMode != ControlMode.Camera && CurrentCharacter != null)
            {
                HandleCharacterMovement();
                HandleAnimationSwitching();
            }

            // Always keep weight at 1 once we start tracking, so the head stays put when paused
            if (isHeadTrackingEnabled || hasInitializedLookTarget)
            {
                headTrackingWeight = Mathf.Lerp(headTrackingWeight, 1f, Time.deltaTime * 5f);
            }
        }

        private void OnDestroy()
        {
            // Clean up PlayableGraphs to prevent memory leaks
            foreach (var character in characters)
            {
                if (character.playableGraph.IsValid())
                {
                    character.playableGraph.Destroy();
                }
            }
        }

        private void InitializeAnimators()
        {
            foreach (var character in characters)
            {
                if (character.animator != null)
                {
                    // Completely decouple from Animator Controller to prevent Editor Graph crashes
                    character.animator.runtimeAnimatorController = null;

                    // Automatically find the head/neck bone for LookAt if it's the cat
                    if (character.modeType == ControlMode.Cat)
                    {
                        character.headBone = FindBoneRecursive(character.rootObject.transform, "neck");
                        if (character.headBone == null)
                        {
                            character.headBone = FindBoneRecursive(character.rootObject.transform, "head");
                        }
                    }

                    if (character.availableClips.Count > 0)
                    {
                        character.currentClipIndex = 0;
                        PlayCurrentClip(character);
                    }
                    else
                    {
                        Debug.LogWarning($"[PhotoShoot] Character '{character.characterName}' has no animations assigned via setup.");
                    }
                }
            }
        }

        private Transform FindBoneRecursive(Transform parent, string boneName)
        {
            if (parent.name.ToLower().Contains(boneName.ToLower())) return parent;
            foreach (Transform child in parent)
            {
                Transform found = FindBoneRecursive(child, boneName);
                if (found != null) return found;
            }
            return null;
        }

        private void HandleModeSwitching()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                int nextMode = ((int)currentMode + 1) % 3;
                currentMode = (ControlMode)nextMode;

                if (cameraController != null)
                {
                    cameraController.enabled = (currentMode == ControlMode.Camera);
                }

                Debug.Log($"[PhotoShoot] Mode Switched to: {currentMode}");
            }
        }

        private void HandleCharacterMovement()
        {
            var character = CurrentCharacter;
            if (character == null || character.rootObject == null) return;

            float x = Input.GetAxis("Horizontal");
            float z = Input.GetAxis("Vertical");
            float y = 0f;

            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) y = 1f;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl)) y = -1f;

            if (Mathf.Abs(x) > 0.1f || Mathf.Abs(z) > 0.1f || Mathf.Abs(y) > 0.1f)
            {
                // Calculate move direction relative to camera
                Vector3 forward = cameraTransform.forward;
                Vector3 right = cameraTransform.right;
                forward.y = 0f;
                right.y = 0f;
                forward.Normalize();
                right.Normalize();

                Vector3 moveDir = (forward * z + right * x + Vector3.up * y).normalized;

                // Move
                character.rootObject.transform.position += moveDir * character.moveSpeed * Time.deltaTime;

                // Rotate towards movement (only horizontal plane so they don't tilt into the sky)
                Vector3 lookDir = new Vector3(moveDir.x, 0, moveDir.z);
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(lookDir);
                    character.rootObject.transform.rotation = Quaternion.RotateTowards(
                        character.rootObject.transform.rotation, 
                        targetRotation, 
                        character.rotationSpeed * Time.deltaTime
                    );
                }
            }
        }

        private void HandleAnimationSwitching()
        {
            var character = CurrentCharacter;
            if (character == null || character.availableClips.Count == 0) return;

            bool changed = false;

            if (Input.GetKeyDown(KeyCode.Z)) // Previous animation
            {
                character.currentClipIndex--;
                if (character.currentClipIndex < 0) character.currentClipIndex = character.availableClips.Count - 1;
                changed = true;
            }
            else if (Input.GetKeyDown(KeyCode.X)) // Next animation
            {
                character.currentClipIndex = (character.currentClipIndex + 1) % character.availableClips.Count;
                changed = true;
            }

            if (changed)
            {
                PlayCurrentClip(character);
            }
        }

        private void PlayCurrentClip(CharacterData character)
        {
            if (character.currentClipIndex >= 0 && character.currentClipIndex < character.availableClips.Count)
            {
                var clip = character.availableClips[character.currentClipIndex];
                
                // Destroy old graph if it exists
                if (character.playableGraph.IsValid())
                {
                    character.playableGraph.Destroy();
                }

                // Create a new PlayableGraph for this clip and play it directly.
                // This 100% bypasses the Animator Window and prevents the GUI GetFocusRect crashes.
                AnimationPlayableUtilities.PlayClip(character.animator, clip, out character.playableGraph);
                
                Debug.Log($"[PhotoShoot] {character.characterName} is now playing: {clip.name}");
            }
        }

        private void LateUpdate()
        {
            CharacterData cat = characters.Find(c => c.modeType == ControlMode.Cat);
            if (cat == null || cat.headBone == null) return;

            // Update Look Target based on Mouse Position if tracking is enabled
            if (isHeadTrackingEnabled && Camera.main != null)
            {
                hasInitializedLookTarget = true;
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                
                // Create a virtual plane facing the camera AT the cat's head to allow up/down look
                Plane lookPlane = new Plane(-Camera.main.transform.forward, cat.headBone.position);
                
                if (lookPlane.Raycast(ray, out float distance))
                {
                    Vector3 hitPoint = ray.GetPoint(distance);
                    // Smoothly move the target point
                    if (currentLookTarget == Vector3.zero) currentLookTarget = hitPoint;
                    currentLookTarget = Vector3.Lerp(currentLookTarget, hitPoint, Time.deltaTime * 10f);
                }
            }

            if (headTrackingWeight <= 0.01f) return;

            // Calculate direction to target
            Vector3 directionToTarget = currentLookTarget - cat.headBone.position;
            
            // Keep the cat's head generally upright by setting up to World Up
            if (directionToTarget.sqrMagnitude > 0.01f)
            {
                Vector3 bodyForward = cat.rootObject.transform.forward;
                Vector3 lookDir = directionToTarget.normalized;
                
                // Prevent looking backwards (limit angle)
                float angle = Vector3.Angle(bodyForward, lookDir);
                if (angle > 120f)
                {
                    lookDir = Vector3.Slerp(bodyForward, lookDir, 120f / angle);
                }

                Quaternion deltaRot = Quaternion.FromToRotation(bodyForward, lookDir);

                // Apply this delta to the head's current animated world rotation
                Quaternion desiredRotation = deltaRot * cat.headBone.rotation;
                
                cat.headBone.rotation = Quaternion.Slerp(cat.headBone.rotation, desiredRotation, headTrackingWeight * 0.8f);
            }
        }
    }
}
