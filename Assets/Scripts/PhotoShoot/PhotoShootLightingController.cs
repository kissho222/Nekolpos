using UnityEngine;
using Nekolpos.PhotoShoot;
using System.Linq;

namespace Nekolpos.PhotoShoot
{
    public class PhotoShootLightingController : MonoBehaviour
    {
        private Light keyLight;
        private Light fillLight;
        private Light rimLight;
        
        private PhotoShootManager manager;

        [Header("Lighting Settings")]
        public float keyIntensity = 1.0f;
        public float fillIntensity = 0.3f;
        public float rimIntensity = 2.5f;
        public Color rimColor = new Color(1f, 0.95f, 0.8f);

        private void Start()
        {
            manager = FindFirstObjectByType<PhotoShootManager>();
            SetupLights();
        }

        private void SetupLights()
        {
            // 1. Key Light (Main light, usually 45 deg offset)
            keyLight = CreateLight("PhotoShoot_KeyLight", LightType.Directional);
            keyLight.intensity = keyIntensity;
            keyLight.shadows = LightShadows.Soft;

            // 2. Fill Light (Soft light from opposite side to fill shadows)
            fillLight = CreateLight("PhotoShoot_FillLight", LightType.Directional);
            fillLight.intensity = fillIntensity;
            fillLight.shadows = LightShadows.None;

            // 3. Rim Light (Backlight to pop the subject out from background)
            rimLight = CreateLight("PhotoShoot_RimLight", LightType.Spot);
            rimLight.intensity = rimIntensity;
            rimLight.color = rimColor;
            rimLight.spotAngle = 60f;
            rimLight.innerSpotAngle = 40f;
            rimLight.range = 20f;
            rimLight.shadows = LightShadows.Soft;
            
            // Put them under a clean parent to avoid scene clutter
            GameObject container = GameObject.Find("PhotoShoot_LightingContainer");
            if (container == null) container = new GameObject("PhotoShoot_LightingContainer");

            keyLight.transform.parent = container.transform;
            fillLight.transform.parent = container.transform;
            rimLight.transform.parent = container.transform;
        }

        private Light CreateLight(string name, LightType type)
        {
            GameObject obj = GameObject.Find(name);
            if (obj == null) obj = new GameObject(name);
            
            Light l = obj.GetComponent<Light>();
            if (l == null) l = obj.AddComponent<Light>();
            
            l.type = type;
            return l;
        }

        private void LateUpdate()
        {
            if (manager == null || manager.characters.Count == 0) return;

            Transform target = null;
            
            // Try to find the active character, or fallback to the cat if in Camera mode
            var activeChar = manager.characters.FirstOrDefault(c => c.modeType == manager.currentMode);
            if (activeChar == null || activeChar.rootObject == null)
            {
                activeChar = manager.characters.FirstOrDefault(c => c.modeType == PhotoShootManager.ControlMode.Cat);
            }

            if (activeChar != null && activeChar.rootObject != null)
            {
                // Prioritize head bone for height if available, otherwise use center
                target = activeChar.headBone != null ? activeChar.headBone : activeChar.rootObject.transform;
            }

            if (target == null) return;

            Vector3 targetPos = target.position;
            // If it's a root transform, aim slightly higher (chest/head level)
            if (target == activeChar.rootObject.transform) targetPos += Vector3.up * 0.8f;

            // Update Key Light (Follows camera but offset to the upper left)
            Vector3 keyDir = Quaternion.AngleAxis(-30, Vector3.up) * Quaternion.AngleAxis(30, transform.right) * transform.forward;
            keyLight.transform.rotation = Quaternion.LookRotation(keyDir);

            // Update Fill Light (Follows camera but offset to the right, much softer)
            Vector3 fillDir = Quaternion.AngleAxis(45, Vector3.up) * transform.forward;
            fillLight.transform.rotation = Quaternion.LookRotation(fillDir);

            // Update Rim Light (Dynamic Spot Light positioned behind the subject, pointing at the camera)
            // Get direction from target to camera
            Vector3 toCamera = (transform.position - targetPos).normalized;
            
            // Place the rim light behind the target, slightly offset to one side and up
            Vector3 rimOffset = -toCamera + Vector3.up * 0.5f + transform.right * 0.3f;
            Vector3 rimPos = targetPos + rimOffset.normalized * 2.0f; // 2 meters behind
            
            rimLight.transform.position = rimPos;
            rimLight.transform.LookAt(targetPos);
        }
    }
}
