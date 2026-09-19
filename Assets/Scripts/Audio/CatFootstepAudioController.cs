using UnityEngine;

namespace Nekolpos.Audio
{
    /// <summary>
    /// Receives Animation Event "PlayFootstep" from cat walk/run clips and plays a 3D footstep SE.
    /// Add this component to the same GameObject that has the Animator.
    /// Assign footstep clips in the Inspector, then add PlayFootstep events to clips such as CatSimple_Run_F_IP.
    /// </summary>
    public class CatFootstepAudioController : MonoBehaviour
    {
        private const string AudioSourceObjectName = "CatFootstepAudioSource";
        private const string DefaultFootstepResourcePath = "SE/足音　タタン";

        [Header("Clips")]
        [SerializeField] private AudioClip[] footstepClips;
        [SerializeField] private Transform footstepOrigin;

        [Header("Playback")]
        [SerializeField] [Min(0f)] private float baseVolume = 0.45f;
        [SerializeField] private Vector2 volumeRandomRange = new Vector2(0.9f, 1.1f);
        [SerializeField] private Vector2 pitchRandomRange = new Vector2(0.95f, 1.05f);
        [SerializeField] [Min(0f)] private float minInterval = 0.08f;

        [Header("3D Sound")]
        [SerializeField] [Min(0f)] private float minDistance = 0.4f;
        [SerializeField] [Min(0.01f)] private float maxDistance = 8.0f;
        [SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;

        [Header("Debug")]
        [SerializeField] private bool logFootstepEvents;

        private AudioSource footstepAudioSource;
        private float lastPlayTime = float.NegativeInfinity;

        private void Awake()
        {
            EnsureAudioSource();
            ConfigureAudioSource();
            LoadDefaultClipIfNeeded();
        }

        private void OnValidate()
        {
            baseVolume = Mathf.Max(0f, baseVolume);
            minInterval = Mathf.Max(0f, minInterval);
            minDistance = Mathf.Max(0f, minDistance);
            maxDistance = Mathf.Max(0.01f, maxDistance);
            if (maxDistance < minDistance)
            {
                maxDistance = minDistance;
            }

            NormalizeRange(ref volumeRandomRange);
            NormalizeRange(ref pitchRandomRange);

            if (footstepAudioSource != null)
            {
                ConfigureAudioSource();
            }
        }

        /// <summary>
        /// Called by Animation Events. Keep this public and parameterless.
        /// </summary>
        public void PlayFootstep()
        {
            if (Time.time - lastPlayTime < minInterval)
            {
                return;
            }

            EnsureAudioSource();
            ConfigureAudioSource();
            LoadDefaultClipIfNeeded();

            AudioClip clip = SelectRandomClip();
            if (clip == null)
            {
                if (logFootstepEvents)
                {
                    Debug.Log("[CatFootstepAudioController] PlayFootstep called, but no footstep clip is assigned.", this);
                }

                return;
            }

            Transform origin = footstepOrigin != null ? footstepOrigin : transform;
            footstepAudioSource.transform.position = origin.position;
            footstepAudioSource.pitch = Random.Range(pitchRandomRange.x, pitchRandomRange.y);

            float volumeScale = Random.Range(volumeRandomRange.x, volumeRandomRange.y);
            float volume = Mathf.Max(0f, baseVolume * volumeScale);

            lastPlayTime = Time.time;
            footstepAudioSource.PlayOneShot(clip, volume);

            if (logFootstepEvents)
            {
                Debug.Log($"[CatFootstepAudioController] PlayFootstep: {clip.name}, volume={volume:0.00}, pitch={footstepAudioSource.pitch:0.00}", this);
            }
        }

        private void EnsureAudioSource()
        {
            if (footstepAudioSource != null)
            {
                return;
            }

            Transform child = transform.Find(AudioSourceObjectName);
            if (child == null)
            {
                GameObject audioSourceObject = new GameObject(AudioSourceObjectName);
                child = audioSourceObject.transform;
                child.SetParent(transform, false);
            }

            footstepAudioSource = child.GetComponent<AudioSource>();
            if (footstepAudioSource == null)
            {
                footstepAudioSource = child.gameObject.AddComponent<AudioSource>();
            }
        }

        private void ConfigureAudioSource()
        {
            if (footstepAudioSource == null)
            {
                return;
            }

            footstepAudioSource.playOnAwake = false;
            Nekolpos.System.OpenBetaPauseMenuController.ApplySavedAudioState(footstepAudioSource, false);
            footstepAudioSource.loop = false;
            footstepAudioSource.spatialBlend = 1f;
            footstepAudioSource.minDistance = minDistance;
            footstepAudioSource.maxDistance = maxDistance;
            footstepAudioSource.rolloffMode = rolloffMode;
        }

        private AudioClip SelectRandomClip()
        {
            if (footstepClips == null || footstepClips.Length == 0)
            {
                return null;
            }

            int validCount = 0;
            for (int i = 0; i < footstepClips.Length; i++)
            {
                if (footstepClips[i] != null)
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return null;
            }

            int selectedIndex = Random.Range(0, validCount);
            for (int i = 0; i < footstepClips.Length; i++)
            {
                AudioClip clip = footstepClips[i];
                if (clip == null)
                {
                    continue;
                }

                if (selectedIndex == 0)
                {
                    return clip;
                }

                selectedIndex--;
            }

            return null;
        }

        private void LoadDefaultClipIfNeeded()
        {
            if (HasAnyClip())
            {
                return;
            }

            AudioClip defaultClip = Resources.Load<AudioClip>(DefaultFootstepResourcePath);
            if (defaultClip != null)
            {
                footstepClips = new[] { defaultClip };
            }
        }

        private bool HasAnyClip()
        {
            if (footstepClips == null)
            {
                return false;
            }

            for (int i = 0; i < footstepClips.Length; i++)
            {
                if (footstepClips[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void NormalizeRange(ref Vector2 range)
        {
            if (range.x > range.y)
            {
                (range.x, range.y) = (range.y, range.x);
            }
        }
    }
}
