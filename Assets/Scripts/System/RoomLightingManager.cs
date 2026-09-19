using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Nekolpos.TimeSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Nekolpos.System
{
    public enum LightTimeOfDay
    {
        Morning,
        Day,
        Evening,
        Night
    }

    public class RoomLightingManager : MonoBehaviour
    {
        public static RoomLightingManager Instance { get; private set; }

        [SerializeField] private Light directionalLight;
        [SerializeField] private Transform ceilingLightAnchor;
        [SerializeField] private Light tableLampLight;
        [SerializeField] private Light nightCeilingLight;
        [SerializeField] private Transform windowLightAnchor;
        [SerializeField] private Transform windowInteriorTarget;
        [SerializeField] private Volume globalVolume;
        [SerializeField] private WeatherType fallbackWeather = WeatherType.Sunny;
        [Header("Skybox Presets")]
        [SerializeField] private Material morningSkybox;
        [SerializeField] private Material daySkybox;
        [SerializeField] private Material eveningSkybox;
        [SerializeField] private Material nightSkybox;
        [SerializeField] private Material cloudySkybox;
        [SerializeField] private Material rainySkybox;
        [SerializeField] private bool useWindowAreaLights = true;
        [SerializeField] private float windowSourceHalfSize = 0.45f;
        [SerializeField] private float areaLightIntensityMultiplier = 0.22f;
        [SerializeField] private float windowLightOutsideOffset = 0.9f;
        [SerializeField] private float windowLightAimDistance = 8f;
        [Header("Open Beta Conversation Lighting")]
        [SerializeField] private bool useOpenBetaConversationLighting = true;
        [SerializeField] private string openBetaRigRootName = "Lighting_TimeOfDay";
        [SerializeField] private float openBetaVolumePriority = 20f;
        [Header("Night Living Light")]
        [SerializeField] private bool useNightLivingCeilingLight = true;
        [SerializeField] [Min(0f)] private float nightLivingCeilingIntensity = 0.5f;
        [Header("Overall Brightness")]
        [SerializeField] [Min(0f)] private float globalLightIntensityMultiplier = 1.12f;
        [SerializeField] private float globalPostExposureOffset = 0.12f;
        [SerializeField] [Range(0f, 1f)] private float globalAmbientBrightenAmount = 0.08f;
        [Header("Cat Night Eye Reflection")]
        [SerializeField] private bool useCatNightEyeReflection = true;

        private Light ceilingLight;
        private Light windowLight;
        private Light[] windowAreaLights;
        private Vector3[] windowAreaOffsets;
        private ColorAdjustments colorAdjustments;
        private TimeManager timeManager;
        private TimeOfDayRig morningRig;
        private TimeOfDayRig noonRig;
        private TimeOfDayRig eveningRig;
        private Light nightMoonLight;
        private GameObject openBetaRigRoot;

        private const string TableLampLightName = "ライト テーブルランプ";
        private const string NightCeilingLightName = "照明 シーリングライト";

        private static readonly string[] RoomLightsDisabledForNight =
        {
            "ライト お風呂",
            "ライト 台所",
            "ライト 玄関",
            "ライト 玄関外",
        };

        private sealed class TimeOfDayRig
        {
            public GameObject Root;
            public Light Sun;
            public Light Fill;
            public Volume Volume;
            public VolumeProfile Profile;
        }

#if UNITY_EDITOR
        private const string OpenBetaVolumeProfileFolder = "Assets/Settings/OpenBeta/Lighting";
#endif

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            if (!TryEnsureLightingReady())
            {
                Debug.LogWarning("[Lighting] Anchors are not assigned. RoomLightingManager will be disabled.");
                enabled = false;
                return;
            }

            ApplyInitialLighting();
        }

        private void ApplyInitialLighting()
        {
            LightTimeOfDay initialTime = LightTimeOfDay.Morning;
            if (timeManager != null)
            {
                initialTime = ConvertDayPeriodToLightTime(timeManager.CurrentPeriod);
            }

            Debug.Log($"[Lighting][TimeChange] Initial apply: time={initialTime}, weather={ResolveWeather()}");
            SetLighting(initialTime, ResolveWeather());
        }

        private static LightTimeOfDay ConvertDayPeriodToLightTime(DayPeriod period)
        {
            switch (period)
            {
                case DayPeriod.Morning:
                    return LightTimeOfDay.Morning;
                case DayPeriod.Afternoon:
                    return LightTimeOfDay.Day;
                case DayPeriod.Evening:
                    return LightTimeOfDay.Evening;
                case DayPeriod.Night:
                    return LightTimeOfDay.Night;
                default:
                    return LightTimeOfDay.Morning;
            }
        }

        private void ResolveSceneReferences()
        {
            if (directionalLight == null)
            {
                GameObject directionalLightObject = GameObject.Find("Directional Light");
                if (directionalLightObject != null)
                {
                    directionalLight = directionalLightObject.GetComponent<Light>();
                }
            }

            if (ceilingLightAnchor == null)
            {
                GameObject livingLightObject = GameObject.Find("ライト リビング");
                if (livingLightObject != null)
                {
                    ceilingLightAnchor = livingLightObject.transform;
                }
            }

            if (tableLampLight == null)
            {
                tableLampLight = FindSceneLightByNameIncludingInactive(TableLampLightName);
            }

            if (nightCeilingLight == null)
            {
                nightCeilingLight = FindSceneLightByNameIncludingInactive(NightCeilingLightName);
            }

            if (globalVolume == null)
            {
                globalVolume = Object.FindFirstObjectByType<Volume>();
            }

            if (timeManager == null)
            {
                timeManager = Object.FindFirstObjectByType<TimeManager>();
            }
        }

        private void CacheVolumeOverrides()
        {
            if (globalVolume == null)
            {
                globalVolume = Object.FindFirstObjectByType<Volume>();
            }

            if (globalVolume == null || globalVolume.profile == null)
            {
                return;
            }

            globalVolume.profile.TryGet(out colorAdjustments);
        }

        private bool TryEnsureLightingReady()
        {
            ResolveSceneReferences();

            if (ceilingLightAnchor == null || windowLightAnchor == null)
            {
                return false;
            }

            if (ceilingLight == null)
            {
                ceilingLight = ceilingLightAnchor.GetComponent<Light>();
                if (ceilingLight == null)
                {
                    ceilingLight = ceilingLightAnchor.gameObject.AddComponent<Light>();
                }
            }

            if (windowLight == null)
            {
                windowLight = GetOrCreateWindowMainLight();
            }

            ConfigureCeilingLight();
            ConfigureTableLampLight();
            ConfigureNightCeilingLight();
            ConfigureNightMoonLight();
            ConfigureWindowLight();

            if (windowAreaLights == null)
            {
                CreateWindowAreaLights();
            }

            CacheVolumeOverrides();
            EnsureOpenBetaRig();

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.sun = directionalLight;
            return true;
        }

        private void ConfigureCeilingLight()
        {
            ceilingLight.type = LightType.Point;
            ceilingLight.range = 14f;
            ceilingLight.shadows = LightShadows.Soft;
            ceilingLight.shadowStrength = 0.6f;
        }

        private void ConfigureTableLampLight()
        {
            if (tableLampLight == null)
            {
                return;
            }

            tableLampLight.type = LightType.Point;
            tableLampLight.color = new Color(1.0f, 0.72f, 0.42f);
            tableLampLight.intensity = 0.45f * globalLightIntensityMultiplier;
            tableLampLight.range = 2.6f;
            tableLampLight.shadows = LightShadows.Soft;
            tableLampLight.shadowStrength = 0.35f;
            tableLampLight.renderMode = LightRenderMode.ForcePixel;
            SetRealtimeBakeType(tableLampLight);
        }

        private void ConfigureNightCeilingLight()
        {
            if (nightCeilingLight == null)
            {
                return;
            }

            nightCeilingLight.type = LightType.Point;
            nightCeilingLight.shadows = LightShadows.Soft;
            nightCeilingLight.shadowStrength = 0.35f;
            nightCeilingLight.renderMode = LightRenderMode.Auto;
            SetRealtimeBakeType(nightCeilingLight);
        }

        private void ConfigureNightMoonLight()
        {
            if (nightMoonLight == null)
            {
                GameObject moonObject = GameObject.Find("Night_Moon_Fill");
                if (moonObject == null)
                {
                    moonObject = new GameObject("Night_Moon_Fill");
                    if (useOpenBetaConversationLighting)
                    {
                        EnsureOpenBetaRigRoot();
                        if (openBetaRigRoot != null)
                        {
                            moonObject.transform.SetParent(openBetaRigRoot.transform, false);
                        }
                    }
                }

                nightMoonLight = moonObject.GetComponent<Light>();
                if (nightMoonLight == null)
                {
                    nightMoonLight = moonObject.AddComponent<Light>();
                }
            }

            nightMoonLight.type = LightType.Directional;
            nightMoonLight.transform.rotation = Quaternion.Euler(22f, -42f, 0f);
            nightMoonLight.color = new Color(0.62f, 0.72f, 1.0f);
            nightMoonLight.useColorTemperature = true;
            nightMoonLight.colorTemperature = 7800f;
            nightMoonLight.intensity = 0.16f * globalLightIntensityMultiplier;
            nightMoonLight.shadows = LightShadows.None;
            nightMoonLight.renderMode = LightRenderMode.ForcePixel;
            SetRealtimeBakeType(nightMoonLight);
        }

        private void ConfigureWindowLight()
        {
            windowLight.type = LightType.Spot;
            windowLight.range = 24f;
            windowLight.spotAngle = 70f;
            windowLight.shadows = LightShadows.Soft;
            windowLight.shadowStrength = 0.6f;

            windowLight.transform.forward = windowLightAnchor.forward;
        }

        private Light GetOrCreateWindowMainLight()
        {
            Transform mainLightTransform = windowLightAnchor.Find("RuntimeWindowMainLight");
            if (mainLightTransform == null)
            {
                GameObject mainLightObj = new GameObject("RuntimeWindowMainLight");
                mainLightObj.transform.SetParent(windowLightAnchor, false);
                mainLightTransform = mainLightObj.transform;
            }

            Light mainLight = mainLightTransform.GetComponent<Light>();
            if (mainLight == null)
            {
                mainLight = mainLightTransform.gameObject.AddComponent<Light>();
            }

            return mainLight;
        }

        private void CreateWindowAreaLights()
        {
            if (!useWindowAreaLights)
            {
                windowAreaLights = null;
                return;
            }

            // 窓面を模した2点（左右）から同方向に照射して、点光源感を弱める
            windowAreaOffsets = new Vector3[]
            {
                new Vector3(-windowSourceHalfSize, 0f, 0f),
                new Vector3( windowSourceHalfSize, 0f, 0f)
            };

            windowAreaLights = new Light[windowAreaOffsets.Length];

            for (int i = 0; i < windowAreaOffsets.Length; i++)
            {
                Light areaLight = GetOrCreateWindowAreaLight(i);
                areaLight.type = LightType.Spot;
                areaLight.shadows = LightShadows.None;
                areaLight.renderMode = LightRenderMode.ForcePixel;
                areaLight.enabled = false;

                windowAreaLights[i] = areaLight;
            }
        }

        private Light GetOrCreateWindowAreaLight(int index)
        {
            Transform areaLightTransform = windowLightAnchor.Find($"WindowAreaLight_{index + 1}");
            if (areaLightTransform == null)
            {
                areaLightTransform = windowLightAnchor.Find($"RuntimeWindowFill{index + 1}");
            }

            if (areaLightTransform == null)
            {
                GameObject areaLightObj = new GameObject($"WindowAreaLight_{index + 1}");
                areaLightObj.transform.SetParent(windowLightAnchor, false);
                areaLightTransform = areaLightObj.transform;
            }

            areaLightTransform.localPosition = windowAreaOffsets[index];
            areaLightTransform.forward = windowLightAnchor.forward;

            Light areaLight = areaLightTransform.GetComponent<Light>();
            if (areaLight == null)
            {
                areaLight = areaLightTransform.gameObject.AddComponent<Light>();
            }

            return areaLight;
        }

        private void UpdateWindowLightTransforms()
        {
            Vector3 sourceCenter = windowLightAnchor.position - windowLightAnchor.forward * windowLightOutsideOffset;
            Vector3 targetPosition = windowInteriorTarget != null
                ? windowInteriorTarget.position
                : windowLightAnchor.position + windowLightAnchor.forward * windowLightAimDistance;

            Vector3 forward = (targetPosition - sourceCenter).sqrMagnitude > 0.0001f
                ? (targetPosition - sourceCenter).normalized
                : windowLightAnchor.forward;

            windowLight.transform.position = sourceCenter;
            windowLight.transform.rotation = Quaternion.LookRotation(forward, windowLightAnchor.up);

            if (windowAreaLights == null || windowAreaOffsets == null)
            {
                return;
            }

            for (int i = 0; i < windowAreaLights.Length; i++)
            {
                Light areaLight = windowAreaLights[i];
                if (areaLight == null)
                {
                    continue;
                }

                Vector3 offset = windowAreaOffsets[i];
                Vector3 worldPos = sourceCenter + (windowLightAnchor.right * offset.x) + (windowLightAnchor.up * offset.y);
                areaLight.transform.position = worldPos;
                areaLight.transform.rotation = Quaternion.LookRotation(forward, windowLightAnchor.up);
            }
        }

        private void ApplyWindowLighting(bool enabled, Color color, float intensity, float range, float spotAngle)
        {
            windowLight.enabled = enabled;
            windowLight.color = color;
            windowLight.intensity = intensity;
            windowLight.range = range;
            windowLight.spotAngle = spotAngle;
            UpdateWindowLightTransforms();

            if (windowAreaLights == null)
            {
                return;
            }

            float areaIntensity = intensity * areaLightIntensityMultiplier;
            for (int i = 0; i < windowAreaLights.Length; i++)
            {
                Light areaLight = windowAreaLights[i];
                if (areaLight == null)
                {
                    continue;
                }

                areaLight.enabled = enabled;
                areaLight.color = color;
                areaLight.intensity = areaIntensity;
                areaLight.range = range;
                areaLight.spotAngle = spotAngle;
                areaLight.transform.forward = windowLightAnchor.forward;
            }
        }

        private void ApplyPostExposure(float value)
        {
            if (colorAdjustments == null)
            {
                return;
            }

            colorAdjustments.postExposure.overrideState = true;
            colorAdjustments.postExposure.value = value;
        }

        private Color BrightenAmbient(Color color)
        {
            return Color.Lerp(color, Color.white, globalAmbientBrightenAmount);
        }

        private void ApplyColorAdjustments(float postExposure, Color colorFilter, float contrast, float saturation)
        {
            if (colorAdjustments == null)
            {
                return;
            }

            colorAdjustments.postExposure.overrideState = true;
            colorAdjustments.postExposure.value = postExposure;
            colorAdjustments.colorFilter.overrideState = true;
            colorAdjustments.colorFilter.value = colorFilter;
            colorAdjustments.contrast.overrideState = true;
            colorAdjustments.contrast.value = contrast;
            colorAdjustments.saturation.overrideState = true;
            colorAdjustments.saturation.value = saturation;
        }

        private WeatherType ResolveWeather()
        {
            if (timeManager == null)
            {
                timeManager = Object.FindFirstObjectByType<TimeManager>();
            }

            return timeManager != null ? timeManager.Weather : fallbackWeather;
        }

        private static float WeatherLightMultiplier(WeatherType weather)
        {
            switch (weather)
            {
                case WeatherType.Cloudy:
                    return 0.62f;
                case WeatherType.Rainy:
                    return 0.38f;
                default:
                    return 1.0f;
            }
        }

        private static float WeatherExposureOffset(WeatherType weather)
        {
            switch (weather)
            {
                case WeatherType.Cloudy:
                    return -0.35f;
                case WeatherType.Rainy:
                    return -0.65f;
                default:
                    return 0.0f;
            }
        }

        private static float WeatherSaturation(WeatherType weather)
        {
            switch (weather)
            {
                case WeatherType.Cloudy:
                    return -18f;
                case WeatherType.Rainy:
                    return -32f;
                default:
                    return 0f;
            }
        }

        private static Color WeatherColorFilter(WeatherType weather)
        {
            switch (weather)
            {
                case WeatherType.Cloudy:
                    return new Color(0.86f, 0.88f, 0.92f);
                case WeatherType.Rainy:
                    return new Color(0.72f, 0.78f, 0.9f);
                default:
                    return Color.white;
            }
        }

        private void ApplyDirectionalLight(bool enabled, Color color, float intensity, Vector3 eulerAngles)
        {
            if (directionalLight == null)
            {
                return;
            }

            directionalLight.enabled = enabled;
            directionalLight.color = color;
            directionalLight.intensity = intensity;
            directionalLight.shadows = LightShadows.Soft;
            directionalLight.transform.rotation = Quaternion.Euler(eulerAngles);
            RenderSettings.sun = directionalLight;
        }

        private void ApplySkybox(LightTimeOfDay time, WeatherType weather)
        {
            Material skybox = ResolveSkybox(time, weather);
            if (skybox == null)
            {
                return;
            }

            RenderSettings.skybox = skybox;
            DynamicGI.UpdateEnvironment();
        }

        private Material ResolveSkybox(LightTimeOfDay time, WeatherType weather)
        {
            if (weather == WeatherType.Rainy && rainySkybox != null)
            {
                return rainySkybox;
            }

            if (weather == WeatherType.Cloudy && cloudySkybox != null)
            {
                return cloudySkybox;
            }

            switch (time)
            {
                case LightTimeOfDay.Morning:
                    return morningSkybox;
                case LightTimeOfDay.Day:
                    return daySkybox;
                case LightTimeOfDay.Evening:
                    return eveningSkybox;
                case LightTimeOfDay.Night:
                    return nightSkybox;
                default:
                    return null;
            }
        }

        public void SetTimeOfDay(LightTimeOfDay time)
        {
            Debug.Log($"[Lighting][TimeChange] SetTimeOfDay requested: {time}, weather={ResolveWeather()}");
            SetLighting(time, ResolveWeather());
        }

        [ContextMenu("Preview OB Morning Lighting")]
        private void PreviewOpenBetaMorningLighting()
        {
            SetLighting(LightTimeOfDay.Morning, ResolveWeather());
        }

        [ContextMenu("Preview OB Noon Lighting")]
        private void PreviewOpenBetaNoonLighting()
        {
            SetLighting(LightTimeOfDay.Day, ResolveWeather());
        }

        [ContextMenu("Preview OB Evening Lighting")]
        private void PreviewOpenBetaEveningLighting()
        {
            SetLighting(LightTimeOfDay.Evening, ResolveWeather());
        }

        public void SetLighting(LightTimeOfDay time, WeatherType weather)
        {
            Debug.Log($"[Lighting][TimeChange] Apply requested: time={time}, weather={weather}, openBeta={useOpenBetaConversationLighting}");

            if (!TryEnsureLightingReady())
            {
                Debug.LogWarning("[Lighting] Light components are not initialized yet.");
                return;
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            float lightMultiplier = WeatherLightMultiplier(weather) * globalLightIntensityMultiplier;
            Color weatherFilter = WeatherColorFilter(weather);
            float exposureOffset = WeatherExposureOffset(weather);
            float saturation = WeatherSaturation(weather);
            RenderSettings.fog = weather == WeatherType.Rainy;
            RenderSettings.fogColor = weather == WeatherType.Rainy
                ? new Color(0.42f, 0.46f, 0.52f)
                : new Color(0.5f, 0.5f, 0.5f);
            RenderSettings.fogDensity = weather == WeatherType.Rainy ? 0.018f : 0.01f;

            ApplySkybox(time, weather);

            if (useOpenBetaConversationLighting && TryApplyOpenBetaLighting(time, weather, lightMultiplier, weatherFilter, exposureOffset, saturation))
            {
                ApplyCatNightEyeReflection(time);
                return;
            }

            SetOpenBetaRigActive(null);

            switch (time)
            {
                case LightTimeOfDay.Morning:
                    ceilingLight.enabled = false;
                    ApplyTableLampLighting(false);
                    ApplyNightCeilingLighting(false);
                    ApplyNightMoonLighting(false);
                    ApplyDirectionalLight(true, new Color(0.9f, 0.95f, 1.0f), 1.45f * lightMultiplier, new Vector3(20f, -35f, 0f));
                    ApplyWindowLighting(
                        true,
                        new Color(0.85f, 0.9f, 1.0f),
                        5.0f * lightMultiplier,
                        32f,
                        70f
                    );

                    RenderSettings.ambientLight = BrightenAmbient(Color.Lerp(new Color(0.48f, 0.52f, 0.58f), weatherFilter, 0.35f));
                    RenderSettings.reflectionIntensity = 1.0f;
                    ApplyColorAdjustments(0.25f + exposureOffset + globalPostExposureOffset, weatherFilter, 4f, saturation);
                    Debug.Log($"[Lighting] Morning / {weather}");
                    break;

                case LightTimeOfDay.Day:
                    ceilingLight.enabled = false;
                    ApplyTableLampLighting(false);
                    ApplyNightCeilingLighting(false);
                    ApplyNightMoonLighting(false);
                    ApplyDirectionalLight(true, new Color(1.0f, 0.98f, 0.9f), 2.0f * lightMultiplier, new Vector3(50f, -25f, 0f));
                    ApplyWindowLighting(
                        true,
                        new Color(1.0f, 0.98f, 0.92f),
                        8.0f * lightMultiplier,
                        34f,
                        75f
                    );

                    RenderSettings.ambientLight = BrightenAmbient(Color.Lerp(new Color(0.72f, 0.72f, 0.72f), weatherFilter, 0.25f));
                    RenderSettings.reflectionIntensity = 0.75f;
                    ApplyColorAdjustments(1.0f + exposureOffset + globalPostExposureOffset, weatherFilter, 2f, saturation);
                    Debug.Log($"[Lighting] Day / {weather}");
                    break;

                case LightTimeOfDay.Evening:
                    ceilingLight.enabled = true;
                    ceilingLight.intensity = 1.4f * globalLightIntensityMultiplier;
                    ApplyTableLampLighting(false);
                    ApplyNightCeilingLighting(false);
                    ApplyNightMoonLighting(false);
                    ApplyDirectionalLight(true, new Color(1.0f, 0.48f, 0.16f), 1.15f * lightMultiplier, new Vector3(12f, 45f, 0f));
                    ApplyWindowLighting(
                        true,
                        new Color(1.0f, 0.45f, 0.1f),
                        3.6f * lightMultiplier,
                        26f,
                        70f
                    );

                    RenderSettings.ambientLight = BrightenAmbient(Color.Lerp(new Color(0.32f, 0.2f, 0.14f), weatherFilter, 0.2f));
                    RenderSettings.reflectionIntensity = 1.0f;
                    ApplyColorAdjustments(0.1f + exposureOffset + globalPostExposureOffset, Color.Lerp(new Color(1.0f, 0.62f, 0.34f), weatherFilter, 0.35f), 8f, saturation - 4f);
                    Debug.Log($"[Lighting] Evening / {weather}");
                    break;

                case LightTimeOfDay.Night:
                    DisableRoomLightsForNight();
                    ApplyLivingCeilingLighting(useNightLivingCeilingLight, nightLivingCeilingIntensity * globalLightIntensityMultiplier);
                    ApplyTableLampLighting(true);
                    ApplyNightCeilingLighting(true);
                    ApplyNightMoonLighting(true);
                    ApplyDirectionalLight(false, directionalLight != null ? directionalLight.color : Color.white, 0f, Vector3.zero);
                    ApplyWindowLighting(
                        false,
                        windowLight.color,
                        windowLight.intensity,
                        windowLight.range,
                        windowLight.spotAngle
                    );

                    RenderSettings.sun = null;
                    RenderSettings.ambientLight = BrightenAmbient(Color.Lerp(new Color(0.035f, 0.032f, 0.04f), weatherFilter, 0.08f));
                    RenderSettings.reflectionIntensity = 0.28f;
                    ApplyColorAdjustments(-0.28f + exposureOffset + globalPostExposureOffset, Color.Lerp(new Color(1.0f, 0.78f, 0.52f), weatherFilter, 0.16f), 4f, saturation - 12f);
                    Debug.Log($"[Lighting] Night table lamp + living ceiling + ceiling light + moon fill / {weather}");
                    break;
            }

            ApplyCatNightEyeReflection(time);
        }

        private void ApplyCatNightEyeReflection(LightTimeOfDay time)
        {
            if (!useCatNightEyeReflection)
            {
                CatNightEyeReflectionController.SetNightReflectionActiveForAll(false);
                return;
            }

            CatNightEyeReflectionController.SetNightReflectionActiveForAll(time == LightTimeOfDay.Night);
        }

        private bool TryApplyOpenBetaLighting(
            LightTimeOfDay time,
            WeatherType weather,
            float lightMultiplier,
            Color weatherFilter,
            float exposureOffset,
            float saturation)
        {
            EnsureOpenBetaRig();
            LightTimeOfDay rigTime = ResolveOpenBetaVisualTime(time);

            if (time == LightTimeOfDay.Night)
            {
                ApplyOpenBetaNightLighting(weather, weatherFilter, exposureOffset, saturation);
                return true;
            }

            TimeOfDayRig rig = ResolveOpenBetaRig(rigTime);
            if (rig == null)
            {
                return false;
            }

            ceilingLight.enabled = false;
            ApplyTableLampLighting(false);
            ApplyNightCeilingLighting(false);
            ApplyNightMoonLighting(false);
            ApplyWindowLighting(false, windowLight.color, windowLight.intensity, windowLight.range, windowLight.spotAngle);
            if (directionalLight != null)
            {
                directionalLight.enabled = false;
            }

            ApplyOpenBetaWeatherToRig(rig, rigTime, weather, lightMultiplier);
            SetOpenBetaRigActive(rig);
            RenderSettings.sun = rig.Sun;

            switch (rigTime)
            {
                case LightTimeOfDay.Morning:
                    RenderSettings.ambientLight = BrightenAmbient(Color.Lerp(new Color(0.46f, 0.49f, 0.55f), weatherFilter, 0.32f));
                    RenderSettings.reflectionIntensity = 0.75f;
                    LogOpenBetaLightingApplied(time, rigTime, weather, rig);
                    break;
                case LightTimeOfDay.Day:
                    RenderSettings.ambientLight = BrightenAmbient(Color.Lerp(new Color(0.62f, 0.63f, 0.64f), weatherFilter, 0.22f));
                    RenderSettings.reflectionIntensity = 0.7f;
                    LogOpenBetaLightingApplied(time, rigTime, weather, rig);
                    break;
                case LightTimeOfDay.Evening:
                    RenderSettings.ambientLight = BrightenAmbient(Color.Lerp(new Color(0.34f, 0.31f, 0.34f), weatherFilter, 0.28f));
                    RenderSettings.reflectionIntensity = 0.8f;
                    LogOpenBetaLightingApplied(time, rigTime, weather, rig);
                    break;
            }

            return true;
        }

        private void ApplyOpenBetaNightLighting(
            WeatherType weather,
            Color weatherFilter,
            float exposureOffset,
            float saturation)
        {
            SetOpenBetaRigActive(null);

            DisableRoomLightsForNight();
            ApplyLivingCeilingLighting(useNightLivingCeilingLight, nightLivingCeilingIntensity * globalLightIntensityMultiplier);
            ApplyTableLampLighting(true);
            ApplyNightCeilingLighting(true);
            ApplyNightMoonLighting(true);
            ApplyWindowLighting(false, windowLight.color, windowLight.intensity, windowLight.range, windowLight.spotAngle);

            if (directionalLight != null)
            {
                directionalLight.enabled = false;
            }

            RenderSettings.sun = null;
            RenderSettings.ambientLight = BrightenAmbient(Color.Lerp(new Color(0.035f, 0.032f, 0.04f), weatherFilter, 0.08f));
            RenderSettings.reflectionIntensity = 0.28f;
            ApplyColorAdjustments(-0.28f + exposureOffset + globalPostExposureOffset, Color.Lerp(new Color(1.0f, 0.78f, 0.52f), weatherFilter, 0.16f), 4f, saturation - 12f);
            Debug.Log($"[Lighting][TimeChange] OB Night applied: living={(ceilingLight != null ? ceilingLight.name : "None")}, tableLamp={(tableLampLight != null ? tableLampLight.name : "None")}, ceiling={(nightCeilingLight != null ? nightCeilingLight.name : "None")}, moon={(nightMoonLight != null ? nightMoonLight.name : "None")}, weather={weather}");
        }

        private void ApplyTableLampLighting(bool enabled)
        {
            if (tableLampLight == null)
            {
                return;
            }

            tableLampLight.enabled = enabled;
            if (enabled)
            {
                ConfigureTableLampLight();
            }
        }

        private void ApplyNightCeilingLighting(bool enabled)
        {
            if (nightCeilingLight == null)
            {
                return;
            }

            nightCeilingLight.enabled = enabled;
            if (enabled)
            {
                ConfigureNightCeilingLight();
            }
        }

        private void ApplyLivingCeilingLighting(bool enabled, float intensity)
        {
            if (ceilingLight == null)
            {
                return;
            }

            ceilingLight.enabled = enabled;
            if (enabled)
            {
                ConfigureCeilingLight();
                ceilingLight.intensity = intensity;
            }
        }

        private void ApplyNightMoonLighting(bool enabled)
        {
            if (nightMoonLight == null)
            {
                ConfigureNightMoonLight();
            }

            if (nightMoonLight == null)
            {
                return;
            }

            nightMoonLight.enabled = enabled;
            if (enabled)
            {
                ConfigureNightMoonLight();
            }
        }

        private static void DisableRoomLightsForNight()
        {
            for (int i = 0; i < RoomLightsDisabledForNight.Length; i++)
            {
                GameObject lightObject = GameObject.Find(RoomLightsDisabledForNight[i]);
                if (lightObject == null)
                {
                    continue;
                }

                Light light = lightObject.GetComponent<Light>();
                if (light != null)
                {
                    light.enabled = false;
                }
            }
        }

        private static Light FindSceneLightByNameIncludingInactive(string lightName)
        {
            Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform current = transforms[i];
                if (current == null || current.name != lightName)
                {
                    continue;
                }

                Light light = current.GetComponent<Light>();
                if (light != null)
                {
                    return light;
                }

                light = current.GetComponentInChildren<Light>(true);
                if (light != null)
                {
                    return light;
                }
            }

            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].name == lightName)
                {
                    return lights[i];
                }
            }

            return null;
        }

        private static LightTimeOfDay ResolveOpenBetaVisualTime(LightTimeOfDay time)
        {
            return time;
        }

        private static void LogOpenBetaLightingApplied(
            LightTimeOfDay requestedTime,
            LightTimeOfDay appliedTime,
            WeatherType weather,
            TimeOfDayRig rig)
        {
            string sunName = rig?.Sun != null ? rig.Sun.name : "None";
            string volumeName = rig?.Volume != null ? rig.Volume.name : "None";
            Debug.Log($"[Lighting][TimeChange] OB applied: requested={requestedTime}, applied={appliedTime}, weather={weather}, sun={sunName}, volume={volumeName}");
        }

        private void EnsureOpenBetaRig()
        {
            if (!useOpenBetaConversationLighting || morningRig != null)
            {
                return;
            }

            EnsureOpenBetaRigRoot();

            morningRig = CreateOpenBetaRig(
                "Morning",
                "Sun_Morning",
                "Morning_Fill",
                new Vector3(18f, -28f, 0f),
                new Color(1.0f, 0.96f, 0.9f),
                5300f,
                0.9f,
                0.62f,
                0.16f,
                new Color(0.9f, 0.94f, 1.0f),
                0.02f,
                -3f,
                -2f,
                new Color(1.0f, 0.97f, 0.92f),
                4f,
                -2f);

            noonRig = CreateOpenBetaRig(
                "Noon",
                "Sun_Noon",
                "Noon_Fill",
                new Vector3(48f, -8f, 0f),
                Color.white,
                6200f,
                1.12f,
                0.48f,
                0.2f,
                new Color(0.95f, 0.97f, 1.0f),
                0.1f,
                0f,
                0f,
                Color.white,
                0f,
                0f);

            eveningRig = CreateOpenBetaRig(
                "Evening",
                "Sun_Evening",
                "Evening_Fill",
                new Vector3(9f, 38f, 0f),
                new Color(1.0f, 0.68f, 0.46f),
                3800f,
                0.78f,
                0.72f,
                0.14f,
                new Color(0.72f, 0.78f, 0.9f),
                -0.04f,
                3f,
                -4f,
                new Color(1.0f, 0.92f, 0.84f),
                8f,
                2f);

            SetOpenBetaRigActive(null);
        }

        private void EnsureOpenBetaRigRoot()
        {
            if (openBetaRigRoot != null)
            {
                return;
            }

            openBetaRigRoot = GameObject.Find(openBetaRigRootName);
            if (openBetaRigRoot == null)
            {
                openBetaRigRoot = new GameObject(openBetaRigRootName);
            }
        }

        private TimeOfDayRig CreateOpenBetaRig(
            string rootName,
            string sunName,
            string fillName,
            Vector3 sunEulerAngles,
            Color sunColor,
            float colorTemperature,
            float sunIntensity,
            float shadowStrength,
            float fillIntensity,
            Color fillColor,
            float postExposure,
            float contrast,
            float profileSaturation,
            Color colorFilter,
            float whiteBalanceTemperature,
            float whiteBalanceTint)
        {
            GameObject root = GetOrCreateChild(openBetaRigRoot.transform, rootName);
            GameObject sunObject = GetOrCreateChild(root.transform, sunName);
            Light sun = GetOrCreateLight(sunObject, LightType.Directional);
            sun.transform.rotation = Quaternion.Euler(sunEulerAngles);
            sun.color = sunColor;
            sun.useColorTemperature = true;
            sun.colorTemperature = colorTemperature;
            sun.intensity = sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = shadowStrength;
            sun.renderMode = LightRenderMode.ForcePixel;
            SetRealtimeBakeType(sun);

            GameObject fillObject = GetOrCreateChild(root.transform, fillName);
            Light fill = GetOrCreateLight(fillObject, LightType.Point);
            PositionFillLight(fill.transform);
            fill.color = fillColor;
            fill.intensity = fillIntensity;
            fill.range = 7f;
            fill.shadows = LightShadows.None;
            fill.renderMode = LightRenderMode.ForcePixel;
            SetRealtimeBakeType(fill);

            GameObject volumeObject = GetOrCreateChild(root.transform, $"PV_OB_{rootName}_Volume");
            Volume volume = volumeObject.GetComponent<Volume>();
            if (volume == null)
            {
                volume = volumeObject.AddComponent<Volume>();
            }

            volume.isGlobal = true;
            volume.priority = openBetaVolumePriority;
            volume.profile = CreateOpenBetaVolumeProfile(rootName, postExposure, contrast, profileSaturation, colorFilter, whiteBalanceTemperature, whiteBalanceTint);

            return new TimeOfDayRig
            {
                Root = root,
                Sun = sun,
                Fill = fill,
                Volume = volume,
                Profile = volume.profile,
            };
        }

        private static void SetRealtimeBakeType(Light light)
        {
#if UNITY_EDITOR
            if (light != null)
            {
                light.lightmapBakeType = LightmapBakeType.Realtime;
            }
#endif
        }

        private VolumeProfile CreateOpenBetaVolumeProfile(
            string name,
            float postExposure,
            float contrast,
            float saturation,
            Color colorFilter,
            float whiteBalanceTemperature,
            float whiteBalanceTint)
        {
#if UNITY_EDITOR
            VolumeProfile profile = LoadOrCreateOpenBetaVolumeProfileAsset(name);
#else
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = $"PV_OB_{name}_VolumeProfile_Runtime";
#endif

            WhiteBalance whiteBalance = GetOrAddVolumeComponent<WhiteBalance>(profile);
            whiteBalance.temperature.Override(whiteBalanceTemperature);
            whiteBalance.tint.Override(whiteBalanceTint);

            ColorAdjustments colorAdjustmentsOverride = GetOrAddVolumeComponent<ColorAdjustments>(profile);
            colorAdjustmentsOverride.postExposure.Override(postExposure);
            colorAdjustmentsOverride.contrast.Override(contrast);
            colorAdjustmentsOverride.saturation.Override(saturation);
            colorAdjustmentsOverride.colorFilter.Override(colorFilter);

            Tonemapping tonemapping = GetOrAddVolumeComponent<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);

#if UNITY_EDITOR
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
#endif

            return profile;
        }

        private static T GetOrAddVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component) || component == null)
            {
                component = ScriptableObject.CreateInstance<T>();
                component.name = typeof(T).Name;
                component.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                profile.components.Add(component);

#if UNITY_EDITOR
                string assetPath = AssetDatabase.GetAssetPath(profile);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    AssetDatabase.AddObjectToAsset(component, profile);
                }
#endif
            }

            component.active = true;
            return component;
        }

#if UNITY_EDITOR
        private static VolumeProfile LoadOrCreateOpenBetaVolumeProfileAsset(string name)
        {
            EnsureOpenBetaVolumeProfileFolder();
            string path = $"{OpenBetaVolumeProfileFolder}/PV_OB_{name}_VolumeProfile.asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile != null)
            {
                return profile;
            }

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = $"PV_OB_{name}_VolumeProfile";
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        private static void EnsureOpenBetaVolumeProfileFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Settings/OpenBeta"))
            {
                AssetDatabase.CreateFolder("Assets/Settings", "OpenBeta");
            }

            if (!AssetDatabase.IsValidFolder(OpenBetaVolumeProfileFolder))
            {
                AssetDatabase.CreateFolder("Assets/Settings/OpenBeta", "Lighting");
            }
        }
#endif

        private static GameObject GetOrCreateChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            GameObject child = new GameObject(childName);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static Light GetOrCreateLight(GameObject target, LightType lightType)
        {
            Light light = target.GetComponent<Light>();
            if (light == null)
            {
                light = target.AddComponent<Light>();
            }

            light.type = lightType;
            return light;
        }

        private void PositionFillLight(Transform fillTransform)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                fillTransform.position = new Vector3(-2.6f, 1.2f, -0.8f);
                return;
            }

            Transform cameraTransform = mainCamera.transform;
            fillTransform.position =
                cameraTransform.position +
                cameraTransform.right * 0.8f -
                cameraTransform.forward * 0.6f +
                Vector3.up * 0.25f;
        }

        private TimeOfDayRig ResolveOpenBetaRig(LightTimeOfDay time)
        {
            switch (time)
            {
                case LightTimeOfDay.Morning:
                    return morningRig;
                case LightTimeOfDay.Day:
                    return noonRig;
                case LightTimeOfDay.Evening:
                    return eveningRig;
                default:
                    return null;
            }
        }

        private void SetOpenBetaRigActive(TimeOfDayRig activeRig)
        {
            SetRigActive(morningRig, ReferenceEquals(activeRig, morningRig));
            SetRigActive(noonRig, ReferenceEquals(activeRig, noonRig));
            SetRigActive(eveningRig, ReferenceEquals(activeRig, eveningRig));
        }

        private static void SetRigActive(TimeOfDayRig rig, bool active)
        {
            if (rig == null || rig.Root == null)
            {
                return;
            }

            rig.Root.SetActive(active);
            if (rig.Sun != null)
            {
                rig.Sun.enabled = active;
            }

            if (rig.Fill != null)
            {
                rig.Fill.enabled = active;
            }

            if (rig.Volume != null)
            {
                rig.Volume.enabled = active;
            }
        }

        private void ApplyOpenBetaWeatherToRig(TimeOfDayRig rig, LightTimeOfDay time, WeatherType weather, float lightMultiplier)
        {
            if (rig == null)
            {
                return;
            }

            if (rig.Sun != null)
            {
                rig.Sun.intensity = ResolveOpenBetaSunIntensity(time) * lightMultiplier;
            }

            if (rig.Fill != null)
            {
                rig.Fill.intensity = ResolveOpenBetaFillIntensity(time, weather) * globalLightIntensityMultiplier;
            }

            if (rig.Profile != null && rig.Profile.TryGet(out ColorAdjustments adjustments))
            {
                adjustments.postExposure.Override(ResolveOpenBetaPostExposure(time) + globalPostExposureOffset);
            }
        }

        private static float ResolveOpenBetaPostExposure(LightTimeOfDay time)
        {
            switch (time)
            {
                case LightTimeOfDay.Morning:
                    return 0.02f;
                case LightTimeOfDay.Day:
                    return 0.1f;
                case LightTimeOfDay.Evening:
                    return -0.04f;
                default:
                    return 0f;
            }
        }

        private static float ResolveOpenBetaSunIntensity(LightTimeOfDay time)
        {
            switch (time)
            {
                case LightTimeOfDay.Morning:
                    return 0.9f;
                case LightTimeOfDay.Day:
                    return 1.12f;
                case LightTimeOfDay.Evening:
                    return 0.78f;
                default:
                    return 0f;
            }
        }

        private static float ResolveOpenBetaFillIntensity(LightTimeOfDay time, WeatherType weather)
        {
            float baseIntensity;
            switch (time)
            {
                case LightTimeOfDay.Morning:
                    baseIntensity = 0.16f;
                    break;
                case LightTimeOfDay.Day:
                    baseIntensity = 0.2f;
                    break;
                case LightTimeOfDay.Evening:
                    baseIntensity = 0.14f;
                    break;
                default:
                    baseIntensity = 0f;
                    break;
            }

            return weather == WeatherType.Rainy ? baseIntensity * 1.35f : baseIntensity;
        }
    }
}
