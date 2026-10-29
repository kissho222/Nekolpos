using UnityEngine;

namespace Nekolpos.Audio
{
    [CreateAssetMenu(fileName = "FootstepSurface", menuName = "ネコルポス/音声/足音の床材")]
    public sealed class FootstepSurfaceProfile : ScriptableObject
    {
        [InspectorName("床の種類")]
        [Tooltip("Inspectorで識別する床材名です（例：木、畳、カーペット、布団、タイル、金属、土、その他）。")]
        [SerializeField] private string displayName = "その他";

        [InspectorName("足音候補")]
        [Tooltip("この床で鳴らす足音を複数登録します。空欄またはNullだけの場合は明示的な無音として扱い、既定音へ置き換えません。")]
        [SerializeField] private AudioClip[] clips = global::System.Array.Empty<AudioClip>();

        [InspectorName("床材ごとの音量")]
        [Tooltip("キャラクター側の基本音量へ掛ける倍率です。")]
        [SerializeField, Min(0f)] private float volumeMultiplier = 1f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public float VolumeMultiplier => Mathf.Max(0f, volumeMultiplier);

        public AudioClip SelectClip(AudioClip previousClip)
        {
            return AudioClipSelection.SelectAvoidingImmediateRepeat(clips, previousClip);
        }
    }

    public static class AudioClipSelection
    {
        public static AudioClip SelectAvoidingImmediateRepeat(AudioClip[] clips, AudioClip previousClip)
        {
            if (clips == null || clips.Length == 0)
            {
                return null;
            }

            int validCount = 0;
            int alternativeCount = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null) continue;
                validCount++;
                if (clips[i] != previousClip) alternativeCount++;
            }

            if (validCount == 0) return null;
            bool avoidPrevious = previousClip != null && alternativeCount > 0;
            int selected = Random.Range(0, avoidPrevious ? alternativeCount : validCount);
            for (int i = 0; i < clips.Length; i++)
            {
                AudioClip clip = clips[i];
                if (clip == null || (avoidPrevious && clip == previousClip)) continue;
                if (selected-- == 0) return clip;
            }

            return null;
        }
    }
}
