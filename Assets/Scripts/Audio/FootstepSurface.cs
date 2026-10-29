using UnityEngine;

namespace Nekolpos.Audio
{
    [DisallowMultipleComponent]
    public sealed class FootstepSurface : MonoBehaviour
    {
        [InspectorName("床の種類")]
        [Tooltip("このColliderの床材を指定します。プロファイル内の足音候補が空なら明示的な無音です。未指定の場合だけキャラクター側の既定床材を使用します。")]
        [SerializeField] private FootstepSurfaceProfile profile;

        public FootstepSurfaceProfile Profile => profile;
    }
}
