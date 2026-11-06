using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Nightborn.EditorTools
{
    /// <summary>
    /// 《夜裔·血月临城》基础场景生成器（一次性 Editor 脚本，可重复执行覆盖）。
    /// 生成内容：URP 管线资产 / 五色板材质 / 全局 Volume / 开场街区灰盒 / 玩家与提灯 / 存档并加入 Build Settings。
    /// 用法：Unity.exe -batchmode -quit -projectPath D:\Unity\Nightborn -executeMethod Nightborn.EditorTools.NightbornBootstrap.BuildAll
    /// </summary>
    public static class NightbornBootstrap
    {
        private const string Root = "Assets/Nightborn";
        private const string SettingsDir = Root + "/Settings";
        private const string MaterialsDir = Root + "/Materials";
        private const string ScenesDir = Root + "/Scenes";
        private const string ScenePath = ScenesDir + "/L_Ch01_S01_Street.unity";

        private static readonly Color C_Ink = new Color32(0x0E, 0x0F, 0x12, 0xFF);
        private static readonly Color C_Blood = new Color32(0x8B, 0x1E, 0x28, 0xFF);
        private static readonly Color C_Candle = new Color32(0xE0, 0xA4, 0x4B, 0xFF);
        private static readonly Color C_Iron = new Color32(0x4A, 0x4F, 0x57, 0xFF);
        private static readonly Color C_Mist = new Color32(0x7A, 0x8A, 0x8F, 0xFF);

        [MenuItem("Nightborn/Build Base Scene")]
        public static void BuildAll()
        {
            EnsureFolders();
            var pipeline = CreateRenderPipeline();
            var materials = CreateMaterials();
            CreateGlobalVolume();
            BuildStreetScene(pipeline, materials);
            AssetDatabase.SaveAssets();
            Debug.Log("[Nightborn] 基础场景生成完成 -> " + ScenePath);
        }

        private static void EnsureFolders()
        {
            foreach (var d in new[] { SettingsDir, MaterialsDir, ScenesDir })
                Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
        }

        private static UniversalRenderPipelineAsset CreateRenderPipeline()
        {
            string rendererPath = SettingsDir + "/Nightborn_Renderer.asset";
            string pipelinePath = SettingsDir + "/Nightborn_URP.asset";

            AssetDatabase.DeleteAsset(rendererPath);
            AssetDatabase.DeleteAsset(pipelinePath);

            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, rendererPath);

            // Forward+：URP 14 里是渲染器上的 m_RenderingMode，用 SerializedObject 稳妥设置。
            var so = new SerializedObject(rendererData);
            var mode = so.FindProperty("m_RenderingMode");
            if (mode != null)
            {
                mode.intValue = 2; // 0=Forward 1=Deferred 2=Forward+
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[Nightborn] 未找到 m_RenderingMode，保持默认 Forward。");
            }

            // 后处理的 PostProcessData 必须挂在渲染器上，否则 Volume 里的效果整份失效。
            var ppProp = so.FindProperty("postProcessData");
            if (ppProp != null && ppProp.objectReferenceValue == null)
            {
                var ppData = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                if (ppData != null)
                {
                    ppProp.objectReferenceValue = ppData;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("[Nightborn] 已为渲染器挂上 PostProcessData。");
                }
                else
                {
                    Debug.LogWarning("[Nightborn] 找不到 URP 的 PostProcessData.asset，后处理不会生效。");
                }
            }

            var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            pipeline.name = "Nightborn_URP";
            pipeline.shadowDistance = 35f;
            pipeline.shadowCascadeCount = 4;
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1f;
            AssetDatabase.CreateAsset(pipeline, pipelinePath);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            return pipeline;
        }

        private static Material MakeLit(string name, Color color, float smoothness, float metallic, Color? emission)
        {
            string path = MaterialsDir + "/" + name + ".mat";
            AssetDatabase.DeleteAsset(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader);
            mat.name = name;
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value * 2f);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Material[] CreateMaterials()
        {
            return new[]
            {
                MakeLit("M_InkStone", C_Ink, 0.12f, 0f, null),
                MakeLit("M_BloodBrick", C_Blood, 0.18f, 0f, null),
                MakeLit("M_CandleGold", C_Candle, 0.45f, 0.2f, C_Candle),
                MakeLit("M_IronGrey", C_Iron, 0.35f, 0.65f, null),
                MakeLit("M_MistTeal", C_Mist, 0.22f, 0.1f, null),
            };
        }

        private static void CreateGlobalVolume()
        {
            string profilePath = SettingsDir + "/VP_Global.asset";
            AssetDatabase.DeleteAsset(profilePath);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);

            var tonemapping = profile.Add<Tonemapping>();
            tonemapping.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>();
            bloom.intensity.Override(0.6f);
            bloom.threshold.Override(0.9f);
            bloom.scatter.Override(0.7f);

            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.35f);
            vignette.smoothness.Override(0.4f);

            var color = profile.Add<ColorAdjustments>();
            color.postExposure.Override(-0.2f);
            color.saturation.Override(-15f);
            color.contrast.Override(8f);

            // VolumeComponent 必须作为 profile 的子资源写入磁盘，否则序列化成 fileID: 0（等于没有效果）。
            foreach (var c in new VolumeComponent[] { tonemapping, bloom, vignette, color })
            {
                c.name = c.GetType().Name;
                AssetDatabase.AddObjectToAsset(c, profile);
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("[Nightborn] VP_Global 已写入 4 个后处理组件。");
        }

        private static GameObject MakeBox(string name, Transform parent, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static GameObject MakeCylinder(string name, Transform parent, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void BuildStreetScene(UniversalRenderPipelineAsset pipeline, Material[] mats)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Material ink = mats[0], blood = mats[1], candle = mats[2], iron = mats[3], mist = mats[4];

            // ---------- 环境：雾、环境光、天空 ----------
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = C_Mist;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.15f, 0.16f, 0.20f);
            RenderSettings.skybox = null;

            var envRoot = new GameObject("ENV_StreetBlock").transform;

            // 路面 90 x 60
            MakeBox("GEO_Ground", envRoot, new Vector3(0f, -0.5f, 0f), new Vector3(90f, 1f, 60f), ink);

            // 人行道两条（抬高 0.15）
            MakeBox("GEO_Sidewalk_L", envRoot, new Vector3(-7.6f, 0.075f, 0f), new Vector3(3.2f, 0.15f, 60f), iron);
            MakeBox("GEO_Sidewalk_R", envRoot, new Vector3(7.6f, 0.075f, 0f), new Vector3(3.2f, 0.15f, 60f), iron);

            // 街两侧建筑：每侧 5 栋，中间留 2m 巷道
            float[] zCenters = { -20f, -11f, -1f, 9f, 19f };
            int[] heights = { 12, 9, 15, 10, 13 };
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                for (int i = 0; i < zCenters.Length; i++)
                {
                    float h = heights[i];
                    float depth = i % 2 == 0 ? 8.4f : 7.2f;
                    float x = sign * (9.2f + 7f);
                    var b = MakeBox("BLD_" + (side == 0 ? "L" : "R") + "_" + (i + 1), envRoot,
                        new Vector3(x, h * 0.5f, zCenters[i]), new Vector3(14f, h, depth), blood);
                    // 每栋正面贴一条铁灰腰线，纯灰盒用来分辨楼体
                    MakeBox("BLD_TRIM_" + i, b.transform, new Vector3(0f, 0.32f, 0f), new Vector3(1.02f, 0.08f, 0.9f), iron);
                }
            }

            // 街口两端的封口墙 + 门洞（北端是目标出口）
            MakeBox("GEO_EndWall_S", envRoot, new Vector3(0f, 5f, -26f), new Vector3(30f, 10f, 1.2f), ink);
            MakeBox("GEO_EndWall_N_L", envRoot, new Vector3(-9f, 5f, 26f), new Vector3(12f, 10f, 1.2f), ink);
            MakeBox("GEO_EndWall_N_R", envRoot, new Vector3(9f, 5f, 26f), new Vector3(12f, 10f, 1.2f), ink);
            MakeBox("GEO_EndWall_N_Top", envRoot, new Vector3(0f, 8.5f, 26f), new Vector3(6f, 3f, 1.2f), ink);

            // 横跨街道的拱桥（灰盒里的视觉地标）
            MakeBox("GEO_Arch_L", envRoot, new Vector3(-8.5f, 6f, 0f), new Vector3(2f, 12f, 2.2f), ink);
            MakeBox("GEO_Arch_R", envRoot, new Vector3(8.5f, 6f, 0f), new Vector3(2f, 12f, 2.2f), ink);
            MakeBox("GEO_Arch_Beam", envRoot, new Vector3(0f, 11f, 0f), new Vector3(19f, 2f, 2.6f), ink);

            // 街灯 6 盏（点光，无阴影）
            float[] lampZ = { -22f, -13f, -4f, 5f, 14f, 22f };
            for (int i = 0; i < lampZ.Length; i++)
            {
                float x = (i % 2 == 0) ? -5.6f : 5.6f;
                var post = MakeCylinder("LAMP_Post_" + (i + 1), envRoot, new Vector3(x, 2.2f, lampZ[i]), new Vector3(0.12f, 2.2f, 0.12f), iron);
                var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bulb.name = "LAMP_Bulb_" + (i + 1);
                bulb.transform.SetParent(envRoot, false);
                bulb.transform.localPosition = new Vector3(x, 4.5f, lampZ[i]);
                bulb.transform.localScale = Vector3.one * 0.28f;
                bulb.GetComponent<Renderer>().sharedMaterial = candle;
                var lightGo = new GameObject("LAMP_Light_" + (i + 1));
                lightGo.transform.SetParent(envRoot, false);
                lightGo.transform.localPosition = new Vector3(x, 4.5f, lampZ[i]);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = C_Candle;
                l.intensity = 2.2f;
                l.range = 9f;
                l.shadows = LightShadows.None;
            }

            // 货箱、木桶等可躲阴影的体块
            Vector3[] cratePos =
            {
                new Vector3(-6.2f, 0.5f, -16f), new Vector3(-6.2f, 0.5f, -14.8f),
                new Vector3(6.4f, 0.5f, -6f), new Vector3(6.4f, 0.5f, 8.5f),
                new Vector3(-5.4f, 0.5f, 11f), new Vector3(4.9f, 0.5f, 17.5f)
            };
            for (int i = 0; i < cratePos.Length; i++)
                MakeBox("PROP_Crate_" + (i + 1), envRoot, cratePos[i], new Vector3(1.1f, 1f, 1.1f), iron);

            // ---------- 灯光：月光 ----------
            var moon = new GameObject("LIGHT_Moonlight");
            var moonLight = moon.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.color = new Color(0.55f, 0.68f, 0.85f);
            moonLight.intensity = 0.8f;
            moonLight.shadows = LightShadows.Soft;
            moonLight.shadowStrength = 0.85f;
            moonLight.shadowCustomResolution = 2048;
            moon.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            // ---------- 玩家 ----------
            var player = new GameObject("PLAYER_Ada");
            player.transform.position = new Vector3(0f, 0.2f, -22f);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.34f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.4f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "PLAYER_Body";
            body.transform.SetParent(player.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.68f, 0.9f, 0.68f);
            Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
            body.GetComponent<Renderer>().sharedMaterial = mist;

            // 相机支架 + 主相机（第三人称）
            var rig = new GameObject("CAM_Rig");
            var camGo = new GameObject("CAM_Main");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(rig.transform, false);
            var cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = C_Ink;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>();

            rig.transform.position = player.transform.position + Vector3.up * 1.45f;
            camGo.transform.localPosition = new Vector3(0f, 0f, -3.4f);

            var tpc = rig.AddComponent<Nightborn.Cameras.ThirdPersonCamera>();
            var soTpc = new SerializedObject(tpc);
            soTpc.FindProperty("target").objectReferenceValue = player.transform;
            soTpc.ApplyModifiedPropertiesWithoutUndo();

            var motor = player.AddComponent<Nightborn.Player.PlayerMotor>();
            var soMotor = new SerializedObject(motor);
            soMotor.FindProperty("cameraRig").objectReferenceValue = rig.transform;
            soMotor.ApplyModifiedPropertiesWithoutUndo();

            // 提灯：挂在玩家身上、阻尼跟随
            var lantern = new GameObject("PLAYER_Lantern");
            lantern.transform.SetParent(null);
            var lightGo2 = new GameObject("LANTERN_Light");
            lightGo2.transform.SetParent(lantern.transform, false);
            var lanternLight = lightGo2.AddComponent<Light>();
            lanternLight.type = LightType.Point;
            lanternLight.color = C_Candle;
            lanternLight.intensity = 1.6f;
            lanternLight.range = 4f;
            lanternLight.shadows = LightShadows.None; // 灰盒阶段不投影，后续「光照即地形」再开会
            var glass = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glass.name = "LANTERN_Glass";
            glass.transform.SetParent(lantern.transform, false);
            glass.transform.localScale = Vector3.one * 0.16f;
            Object.DestroyImmediate(glass.GetComponent<SphereCollider>());
            glass.GetComponent<Renderer>().sharedMaterial = candle;
            var lf = lantern.AddComponent<Nightborn.Player.LanternFollow>();
            var soLf = new SerializedObject(lf);
            soLf.FindProperty("target").objectReferenceValue = player.transform;
            soLf.FindProperty("lanternLight").objectReferenceValue = lanternLight;
            soLf.ApplyModifiedPropertiesWithoutUndo();
            lantern.transform.position = player.transform.position + new Vector3(0.35f, 1.25f, 0.28f);

            // ---------- 全局 Volume ----------
            var volumeGo = new GameObject("POSTFX_GlobalVolume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(SettingsDir + "/VP_Global.asset");

            // ---------- 存档 ----------
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[Nightborn] 场景已保存：" + ScenePath);

            RenderPreview(cam);
        }

        private static void RenderPreview(UnityEngine.Camera ignored)
        {
            try
            {
                string outDir = Path.Combine(Directory.GetCurrentDirectory(), "_Preview");
                Directory.CreateDirectory(outDir);

                // 单独拉一台预览机位，避免依赖运行时相机逻辑（batchmode 下 Update 不跑）。
                var go = new GameObject("PREVIEW_Camera");
                go.transform.position = new Vector3(0f, 2.9f, -25.2f);
                go.transform.rotation = Quaternion.Euler(4f, 0f, 0f);
                var cam = go.AddComponent<UnityEngine.Camera>();
                cam.fieldOfView = 65f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = C_Ink;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 200f;
                go.AddComponent<UniversalAdditionalCameraData>();

                var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, "preview_street_01.png"), tex.EncodeToPNG());
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(rt);
                Debug.Log("[Nightborn] 预览图已输出：" + outDir);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Nightborn] 预览渲染失败（不影响场景）：" + e.Message);
            }
        }
    }
}
