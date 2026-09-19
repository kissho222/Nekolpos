using Nekolpos.Data;
using Nekolpos.StatusSystem;
using Nekolpos.System;
using UnityEngine;

namespace Nekolpos.Audio
{
    /// <summary>
    /// Plays a looping 3D purr while the cat is emotionally eligible and the player is within range.
    /// Usage:
    /// - Add this component to the cat root GameObject.
    /// - Assign purrClip to a loopable purr AudioClip.
    /// - Assign playerTarget to the player or PlayerCameraRoot.
    /// - Optionally assign purrOrigin to a throat/chest Transform.
    /// - Runtime心理値はStatusManagerから取得する。外部のHigh判定を使う場合はSetPurrEligibleを呼ぶ。
    /// - Tune audibleDistance, stopDistance, near/far volume, and AudioSource 3D distances.
    /// </summary>
    public class CatPurrAudioController : MonoBehaviour
    {
        private const string AudioSourceObjectName = "CatPurrAudioSource";

        [Header("References")]
        [Tooltip("ループ再生するゴロゴロ音のAudioClip。自然に繰り返せる素材を指定します。")]
        [SerializeField] private AudioClip purrClip;
        [Tooltip("音を鳴らす位置。猫の喉元・胸元などを指定します。未指定ならこのGameObjectの位置を使います。")]
        [SerializeField] private Transform purrOrigin;
        [Tooltip("距離判定に使うプレイヤー側Transform。PlayerCameraRootなど、実際に聞かせたい対象を指定します。")]
        [SerializeField] private Transform playerTarget;
        [Tooltip("ランタイム心理値の正本。未指定ならシーン内から自動取得を試みます。")]
        [SerializeField] private StatusManager statusManager;

        [Header("Eligibility")]
        [Tooltip("ONにすると、ふれあい中として通知されている間だけゴロゴロ音を再生します。")]
        [SerializeField] private bool playOnlyDuringInteraction = true;
        [Tooltip("愛情がこの値以上ならゴロゴロ再生条件を満たします。")]
        [SerializeField] [Range(0, 100)] private int affectionHighThreshold = 70;
        [Tooltip("従順がこの値以上ならゴロゴロ再生条件を満たします。")]
        [SerializeField] [Range(0, 100)] private int obedienceHighThreshold = 70;
        [Tooltip("ONにするとCatDataSOではなく、SetPurrEligibleなど外部APIで渡されたHigh判定を使います。")]
        [SerializeField] private bool useInjectedEligibility;

        [Header("Distance")]
        [Tooltip("この距離以内にプレイヤーが入ると、条件成立時にゴロゴロ音をフェードイン開始します。")]
        [SerializeField] [Min(0f)] private float audibleDistance = 10.0f;
        [Tooltip("近距離として扱う距離。この範囲内ではnearVolumeに近い音量になります。")]
        [SerializeField] [Min(0f)] private float closeDistance = 2.5f;
        [Tooltip("再生中にこの距離以上へ離れるとフェードアウト停止します。audibleDistanceより少し大きくすると境界で切り替わりにくくなります。")]
        [SerializeField] [Min(0f)] private float stopDistance = 12.0f;

        [Header("Volume")]
        [Tooltip("audibleDistance付近で聞こえる遠距離用の小さい音量です。")]
        [SerializeField] [Min(0f)] private float farVolume = 0.08f;
        [Tooltip("closeDistance以内で聞こえる近距離用の音量です。")]
        [SerializeField] [Min(0f)] private float nearVolume = 0.32f;
        [Tooltip("条件成立時に音量が0から上がるまでの秒数です。")]
        [SerializeField] [Min(0f)] private float fadeInSeconds = 0.8f;
        [Tooltip("条件解除時に音量が0まで下がるまでの秒数です。完了後にAudioSourceを停止します。")]
        [SerializeField] [Min(0f)] private float fadeOutSeconds = 1.0f;
        [Tooltip("距離に応じた演出音量カーブ。横軸0が近距離、1がaudibleDistance付近です。")]
        [SerializeField] private AnimationCurve distanceVolumeCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Header("3D Sound")]
        [Tooltip("AudioSourceの3D距離減衰で、最大音量として扱われる距離です。")]
        [SerializeField] [Min(0f)] private float minDistance = 0.3f;
        [Tooltip("AudioSourceの3D距離減衰で、音が届く最大距離です。")]
        [SerializeField] [Min(0.01f)] private float maxDistance = 10.0f;
        [Tooltip("AudioSourceの3D距離減衰カーブです。通常はLogarithmicで自然に減衰します。")]
        [SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;

        [Header("Debug")]
        [Tooltip("ONにすると、ゴロゴロ音のフェードイン開始・フェードアウト開始・停止完了をConsoleに出します。")]
        [SerializeField] private bool logPurrState;

        private AudioSource purrAudioSource;
        private StatusManager subscribedStatusManager;
        private bool injectedAffectionHigh;
        private bool injectedObedienceHigh;
        private bool isInteractionActive;
        private bool wantsToPlay;
        private float fadeAmount;
        private float currentVolume;

        private void Awake()
        {
            ResolveStatusReferences();
            BindStatusManager();
            EnsureAudioSource();
            ConfigureAudioSource();
        }

        private void OnEnable()
        {
            ResolveStatusReferences();
            BindStatusManager();
        }

        private void OnDisable()
        {
            UnbindStatusManager();
        }

        private void Update()
        {
            FollowOrigin();
            ConfigureAudioSource();

            float distance = GetPlayerDistance();
            UpdatePlaybackEligibility(distance);

            float targetFade = wantsToPlay ? 1f : 0f;
            float duration = wantsToPlay ? fadeInSeconds : fadeOutSeconds;
            fadeAmount = MoveTowardsFade(fadeAmount, targetFade, duration);

            float targetVolume = CalculateDistanceVolume(distance);
            currentVolume = Mathf.MoveTowards(currentVolume, targetVolume, Time.deltaTime / Mathf.Max(0.01f, fadeInSeconds));

            if (purrAudioSource != null)
            {
                purrAudioSource.volume = currentVolume * fadeAmount;

                if (!wantsToPlay && purrAudioSource.isPlaying && fadeAmount <= 0f)
                {
                    purrAudioSource.Stop();
                    purrAudioSource.volume = 0f;

                    if (logPurrState)
                    {
                        Debug.Log("[CatPurrAudioController] Purr stopped.", this);
                    }
                }
            }
        }

        public void SetAffectionHigh(bool value)
        {
            useInjectedEligibility = true;
            injectedAffectionHigh = value;
        }

        public void SetObedienceHigh(bool value)
        {
            useInjectedEligibility = true;
            injectedObedienceHigh = value;
        }

        public void SetPurrEligible(bool affectionHigh, bool obedienceHigh)
        {
            useInjectedEligibility = true;
            injectedAffectionHigh = affectionHigh;
            injectedObedienceHigh = obedienceHigh;
        }

        public void SetInteractionActive(bool active)
        {
            isInteractionActive = active;
        }

        private void OnValidate()
        {
            audibleDistance = Mathf.Max(0f, audibleDistance);
            closeDistance = Mathf.Clamp(closeDistance, 0f, audibleDistance);
            stopDistance = Mathf.Max(audibleDistance, stopDistance);
            farVolume = Mathf.Max(0f, farVolume);
            nearVolume = Mathf.Max(0f, nearVolume);
            fadeInSeconds = Mathf.Max(0f, fadeInSeconds);
            fadeOutSeconds = Mathf.Max(0f, fadeOutSeconds);
            minDistance = Mathf.Max(0f, minDistance);
            maxDistance = Mathf.Max(0.01f, maxDistance);
            if (maxDistance < minDistance)
            {
                maxDistance = minDistance;
            }

            if (distanceVolumeCurve == null || distanceVolumeCurve.length == 0)
            {
                distanceVolumeCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            }

            if (purrAudioSource != null)
            {
                ConfigureAudioSource();
            }
        }

        private void ResolveStatusReferences()
        {
            DialogueManager dialogueManager = DialogueManager.Instance ?? FindFirstObjectByType<DialogueManager>();
            StatusManager dialogueStatusManager = dialogueManager != null
                ? dialogueManager.GetComponent<StatusManager>()
                : null;
            if (dialogueStatusManager != null)
            {
                statusManager = dialogueStatusManager;
            }
            else if (statusManager == null)
            {
                statusManager = GetComponent<StatusManager>() ?? FindFirstObjectByType<StatusManager>();
            }

            BindStatusManager();
        }

        private void BindStatusManager()
        {
            if (ReferenceEquals(subscribedStatusManager, statusManager))
            {
                return;
            }

            UnbindStatusManager();
            subscribedStatusManager = statusManager;
            if (subscribedStatusManager != null)
            {
                subscribedStatusManager.OnStatusChanged += HandleStatusChanged;
            }
        }

        private void UnbindStatusManager()
        {
            if (subscribedStatusManager == null)
            {
                return;
            }

            subscribedStatusManager.OnStatusChanged -= HandleStatusChanged;
            subscribedStatusManager = null;
        }

        private void HandleStatusChanged(StatusType statusType, int _)
        {
            if (statusType == StatusType.Affection || statusType == StatusType.Obedience)
            {
                UpdatePlaybackEligibility(GetPlayerDistance());
            }
        }

        private void UpdatePlaybackEligibility(float distance)
        {
            bool shouldPlay = ShouldPlay(distance);
            if (shouldPlay && !wantsToPlay)
            {
                BeginFadeIn();
            }
            else if (!shouldPlay && wantsToPlay)
            {
                BeginFadeOut();
            }
        }

        private bool ShouldPlay(float distance)
        {
            if (purrClip == null || playerTarget == null)
            {
                return false;
            }

            if (playOnlyDuringInteraction && !isInteractionActive)
            {
                return false;
            }

            if (!IsEmotionEligible())
            {
                return false;
            }

            if (purrAudioSource != null && purrAudioSource.isPlaying)
            {
                return distance < stopDistance;
            }

            return distance <= audibleDistance;
        }

        private bool IsEmotionEligible()
        {
            if (useInjectedEligibility)
            {
                return injectedAffectionHigh || injectedObedienceHigh;
            }

            ResolveStatusReferences();
            if (statusManager != null)
            {
                return statusManager.GetValue(StatusType.Affection) >= affectionHighThreshold ||
                       statusManager.GetValue(StatusType.Obedience) >= obedienceHighThreshold;
            }

            return injectedAffectionHigh || injectedObedienceHigh;
        }

        private void BeginFadeIn()
        {
            wantsToPlay = true;
            EnsureAudioSource();
            ConfigureAudioSource();
            FollowOrigin();

            if (!purrAudioSource.isPlaying)
            {
                purrAudioSource.volume = 0f;
                purrAudioSource.Play();
            }

            if (logPurrState)
            {
                Debug.Log("[CatPurrAudioController] Purr fade in started.", this);
            }
        }

        private void BeginFadeOut()
        {
            wantsToPlay = false;

            if (logPurrState)
            {
                Debug.Log("[CatPurrAudioController] Purr fade out started.", this);
            }
        }

        private void EnsureAudioSource()
        {
            if (purrAudioSource != null)
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

            purrAudioSource = child.GetComponent<AudioSource>();
            if (purrAudioSource == null)
            {
                purrAudioSource = child.gameObject.AddComponent<AudioSource>();
            }
        }

        private void ConfigureAudioSource()
        {
            if (purrAudioSource == null)
            {
                return;
            }

            purrAudioSource.clip = purrClip;
            purrAudioSource.loop = true;
            purrAudioSource.playOnAwake = false;
            OpenBetaPauseMenuController.ApplySavedAudioState(purrAudioSource, false);
            purrAudioSource.spatialBlend = 1f;
            purrAudioSource.minDistance = minDistance;
            purrAudioSource.maxDistance = maxDistance;
            purrAudioSource.rolloffMode = rolloffMode;
        }

        private void FollowOrigin()
        {
            if (purrAudioSource == null)
            {
                return;
            }

            Transform origin = purrOrigin != null ? purrOrigin : transform;
            purrAudioSource.transform.position = origin.position;
        }

        private float GetPlayerDistance()
        {
            if (playerTarget == null)
            {
                return float.PositiveInfinity;
            }

            Transform origin = purrOrigin != null ? purrOrigin : transform;
            return Vector3.Distance(origin.position, playerTarget.position);
        }

        private float CalculateDistanceVolume(float distance)
        {
            if (float.IsPositiveInfinity(distance))
            {
                return 0f;
            }

            float normalized = audibleDistance <= 0f ? 0f : Mathf.Clamp01(distance / audibleDistance);
            float curveValue = distanceVolumeCurve != null ? distanceVolumeCurve.Evaluate(normalized) : 1f - normalized;
            float volume = Mathf.Lerp(farVolume, nearVolume, Mathf.Clamp01(curveValue));

            if (distance <= closeDistance)
            {
                volume = nearVolume;
            }

            return Mathf.Max(0f, volume);
        }

        private static float MoveTowardsFade(float current, float target, float duration)
        {
            if (duration <= 0f)
            {
                return target;
            }

            return Mathf.MoveTowards(current, target, Time.deltaTime / duration);
        }
    }
}
