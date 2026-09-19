using UnityEngine;
using UnityEditor;
using TMPro;

namespace Nekolpos.EditorTools
{
    /// <summary>
    /// TMP SubMeshUI が Hierarchy を埋め尽くすのを防ぐためのユーティリティ
    /// </summary>
    public class TMPSubMeshHider : Editor
    {
        public static void HideSubMeshes()
        {
            // シーン内のすべての TMP_SubMeshUI を取得（非アクティブも含む）
            var subMeshes = Object.FindObjectsByType<TMP_SubMeshUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;

            foreach (var sm in subMeshes)
            {
                if ((sm.gameObject.hideFlags & HideFlags.HideInHierarchy) == 0)
                {
                    sm.gameObject.hideFlags |= HideFlags.HideInHierarchy;
                    EditorUtility.SetDirty(sm.gameObject);
                    count++;
                }
            }

            // Hierarchyの更新を強制
            EditorApplication.RepaintHierarchyWindow();
            Debug.Log($"[TMP] {count} 個の TMP SubMeshUI を Hierarchy から非表示にしました！");
        }

        public static void ShowSubMeshes()
        {
            var subMeshes = Object.FindObjectsByType<TMP_SubMeshUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;

            foreach (var sm in subMeshes)
            {
                if ((sm.gameObject.hideFlags & HideFlags.HideInHierarchy) != 0)
                {
                    sm.gameObject.hideFlags &= ~HideFlags.HideInHierarchy;
                    EditorUtility.SetDirty(sm.gameObject);
                    count++;
                }
            }

            EditorApplication.RepaintHierarchyWindow();
            Debug.Log($"[TMP] {count} 個の TMP SubMeshUI を Hierarchy に表示しました！");
        }
    }
}
