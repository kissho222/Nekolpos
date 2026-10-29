using Nekolpos.System;
using UnityEngine;

namespace Nekolpos.Audio
{
    public enum FootstepPlaybackMode
    {
        [InspectorName("移動状態から自動再生")] AutomaticInterval,
        [InspectorName("Animation Eventから再生")] AnimationEvent
    }

    [DisallowMultipleComponent]
    public sealed class CharacterMovementAudioController : MonoBehaviour
    {
        [Header("音源と床材")]
        [InspectorName("効果音の音源")]
        [Tooltip("足音・ジャンプ音・着地音を再生するAudioSourceです。未指定なら実行時に子オブジェクトへ作成し、既存のSE Mixer経路へ接続します。")]
        [SerializeField] private AudioSource audioSource;
        [InspectorName("既定の床材")]
        [Tooltip("検出したColliderに床材指定が無い場合だけ使用します。未指定なら安全に無音です。明示的な無音床には使用しません。")]
        [SerializeField] private FootstepSurfaceProfile defaultSurface;

        [Header("足音の設定")]
        [InspectorName("足音の基本音量")]
        [Tooltip("床材ごとの音量倍率を掛ける前の音量です。")]
        [SerializeField, Min(0f)] private float footstepVolume = 0.5f;
        [InspectorName("歩行間隔（秒）")]
        [Tooltip("移動中に足音を鳴らす基本間隔です。停止・空中・Pause中は鳴りません。")]
        [SerializeField, Min(0.05f)] private float footstepInterval = 0.42f;
        [InspectorName("足音の発火方法")]
        [Tooltip("自動再生かAnimation Eventのどちらか一方を選びます。両方から重複して鳴ることはありません。")]
        [SerializeField] private FootstepPlaybackMode footstepPlaybackMode = FootstepPlaybackMode.AutomaticInterval;
        [InspectorName("最小再生間隔（秒）")]
        [Tooltip("Animation Event等から連続要求されても、この時間内は足音を重ねません。")]
        [SerializeField, Min(0f)] private float minimumFootstepInterval = 0.08f;
        [InspectorName("音程の揺らぎ")]
        [Tooltip("足音ごとの音程をこの範囲でランダム化します。")]
        [SerializeField] private Vector2 pitchRange = new Vector2(0.95f, 1.05f);

        [Header("ジャンプと着地")]
        [InspectorName("ジャンプ音")]
        [Tooltip("ジャンプ開始時に1回だけ鳴らします。未登録なら無音です。")]
        [SerializeField] private AudioClip jumpClip;
        [InspectorName("ジャンプ音の音量")]
        [Tooltip("ジャンプ音だけに適用する音量です。0なら無音です。")]
        [SerializeField, Min(0f)] private float jumpVolume = 0.7f;
        [InspectorName("着地音")]
        [Tooltip("空中から接地へ移行した時に1回だけ鳴らします。未登録なら無音です。")]
        [SerializeField] private AudioClip landingClip;
        [InspectorName("着地音の音量")]
        [Tooltip("着地音だけに適用する音量です。0なら無音です。")]
        [SerializeField, Min(0f)] private float landingVolume = 0.7f;
        [InspectorName("着地判定の最短滞空時間")]
        [Tooltip("段差や接地判定の揺れを着地音と誤認しないための秒数です。")]
        [SerializeField, Min(0f)] private float minimumAirborneSeconds = 0.08f;

        private AudioClip previousFootstepClip;
        private FootstepSurfaceProfile currentSurface;
        private float nextAutomaticFootstepTime;
        private float lastFootstepTime = float.NegativeInfinity;
        private float airborneSeconds;
        private bool wasGrounded = true;
        private bool isGrounded;
        private bool isMoving;
        private bool pausedByGamePause;

        public static FootstepSurfaceProfile ResolveSurface(Collider groundCollider, FootstepSurfaceProfile fallback)
        {
            if (groundCollider == null) return fallback;
            FootstepSurface surface = groundCollider.GetComponent<FootstepSurface>() ??
                                      groundCollider.GetComponentInParent<FootstepSurface>();
            return surface != null && surface.Profile != null ? surface.Profile : fallback;
        }

        public void ObserveMovement(float horizontalSpeed, bool grounded, Collider groundCollider, float deltaTime)
        {
            if (!isActiveAndEnabled) return;
            currentSurface = ResolveSurface(groundCollider, defaultSurface);
            isGrounded = grounded;
            isMoving = horizontalSpeed > 0.01f;
            SyncPauseState();
            if (IsGamePaused()) return;

            if (!grounded)
            {
                airborneSeconds += Mathf.Max(0f, deltaTime);
                wasGrounded = false;
                return;
            }

            if (!wasGrounded && airborneSeconds >= minimumAirborneSeconds)
            {
                PlayOneShot(landingClip, landingVolume);
            }

            wasGrounded = true;
            airborneSeconds = 0f;
            if (!isMoving)
            {
                nextAutomaticFootstepTime = Time.time;
                return;
            }

            if (footstepPlaybackMode == FootstepPlaybackMode.AutomaticInterval && Time.time >= nextAutomaticFootstepTime)
            {
                PlayFootstepInternal();
                nextAutomaticFootstepTime = Time.time + footstepInterval;
            }
        }

        public void NotifyJumpStarted()
        {
            if (!isActiveAndEnabled) return;
            SyncPauseState();
            if (IsGamePaused()) return;
            isGrounded = false;
            wasGrounded = false;
            airborneSeconds = 0f;
            PlayOneShot(jumpClip, jumpVolume);
        }

        public void PlayFootstep()
        {
            if (!isActiveAndEnabled) return;
            SyncPauseState();
            if (footstepPlaybackMode != FootstepPlaybackMode.AnimationEvent ||
                IsGamePaused() ||
                !isGrounded ||
                !isMoving ||
                Time.time - lastFootstepTime < minimumFootstepInterval)
            {
                return;
            }

            PlayFootstepInternal();
        }

        private void PlayFootstepInternal()
        {
            if (Time.time - lastFootstepTime < minimumFootstepInterval) return;
            FootstepSurfaceProfile surface = currentSurface;
            if (surface == null) return;
            AudioClip clip = surface.SelectClip(previousFootstepClip);
            if (clip == null) return;

            EnsureAudioSource();
            audioSource.pitch = Random.Range(Mathf.Min(pitchRange.x, pitchRange.y), Mathf.Max(pitchRange.x, pitchRange.y));
            audioSource.PlayOneShot(clip, footstepVolume * surface.VolumeMultiplier);
            previousFootstepClip = clip;
            lastFootstepTime = Time.time;
        }

        private void PlayOneShot(AudioClip clip, float volume)
        {
            if (clip == null || IsGamePaused()) return;
            EnsureAudioSource();
            audioSource.pitch = 1f;
            audioSource.PlayOneShot(clip, Mathf.Max(0f, volume));
        }

        private void Awake() => EnsureAudioSource();
        private void Update() => SyncPauseState();

        private void OnDisable()
        {
            if (audioSource != null) audioSource.Stop();
            pausedByGamePause = false;
            ResetMovementState();
        }

        // Pause/input ownership can interrupt observation without ending a jump.
        public void SuspendMovementObservation()
        {
            isMoving = false;
            nextAutomaticFootstepTime = Time.time;
        }

        // Movement can be disabled or teleported independently of this audio component.
        public void ResetMovementState()
        {
            currentSurface = null;
            isGrounded = false;
            isMoving = false;
            wasGrounded = true;
            airborneSeconds = 0f;
            nextAutomaticFootstepTime = Time.time;
            lastFootstepTime = float.NegativeInfinity;
            previousFootstepClip = null;
        }

        private void OnValidate()
        {
            footstepVolume = Mathf.Max(0f, footstepVolume);
            footstepInterval = Mathf.Max(0.05f, footstepInterval);
            minimumFootstepInterval = Mathf.Max(0f, minimumFootstepInterval);
            jumpVolume = Mathf.Max(0f, jumpVolume);
            landingVolume = Mathf.Max(0f, landingVolume);
            minimumAirborneSeconds = Mathf.Max(0f, minimumAirborneSeconds);
        }

        private void EnsureAudioSource()
        {
            if (audioSource == null)
            {
                Transform child = transform.Find("MovementAudioSource");
                if (child == null)
                {
                    child = new GameObject("MovementAudioSource").transform;
                    child.SetParent(transform, false);
                }
                audioSource = child.GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = child.gameObject.AddComponent<AudioSource>();
                }
            }

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            SettingsManager.RouteAudio(audioSource, SettingsAudioCategory.Se);
        }

        private void SyncPauseState()
        {
            if (audioSource == null) return;
            bool shouldPause = IsGamePaused();
            if (shouldPause && !pausedByGamePause)
            {
                audioSource.Pause();
                pausedByGamePause = true;
            }
            else if (!shouldPause && pausedByGamePause)
            {
                audioSource.UnPause();
                pausedByGamePause = false;
            }
        }

        private static bool IsGamePaused() => Time.timeScale <= 0f;
    }
}
