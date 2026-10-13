using UnityEngine;

/// <summary>非Activeな骨も含め、侵入防止Colliderへ最終的な骨姿勢をコピーする。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10500)]
public sealed class CatBodyColliderFollower : MonoBehaviour
{
    [System.Serializable]
    public struct Binding
    {
        public Transform bone;
        public Transform colliderTransform;
    }

    [Tooltip("Colliderは猫Rootの有効な専用子へ置きます。骨のActive状態は変更しません。")]
    [SerializeField] private Binding[] bindings = System.Array.Empty<Binding>();

    private void OnEnable() => SyncToBones();
    private void LateUpdate() => SyncToBones();

    public void SyncToBones()
    {
        foreach (Binding binding in bindings)
        {
            if (binding.bone == null || binding.colliderTransform == null) continue;
            Transform target = binding.colliderTransform;
            target.SetPositionAndRotation(binding.bone.position, binding.bone.rotation);
            Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
            Vector3 boneScale = binding.bone.lossyScale;
            target.localScale = new Vector3(
                SafeScaleRatio(boneScale.x, parentScale.x),
                SafeScaleRatio(boneScale.y, parentScale.y),
                SafeScaleRatio(boneScale.z, parentScale.z));
        }
    }

    private static float SafeScaleRatio(float value, float parent) => Mathf.Abs(parent) > 0.00001f ? value / parent : 1f;
}
