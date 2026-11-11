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
        private const string MenuScenePath = ScenesDir + "/L_Main.unity";

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
            BuildMenuScene();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true)
            };
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
            Material ink = mats[0], bloodBrick = mats[1], candle = mats[2], iron = mats[3], mist = mats[4];

            // ---------- 环境：雾、环境光 ----------
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = C_Mist;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.15f, 0.16f, 0.20f);
            RenderSettings.skybox = null;

            var envRoot = new GameObject("ENV_TownBlock").transform;
            envRoot.gameObject.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            envRoot.gameObject.AddComponent<Nightborn.Level.NavMeshRuntimeBake>();

            // ---------- 40 x 40 箱庭 ----------
            MakeBox("GEO_Ground", envRoot, new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), ink);

            // 四面围墙，北墙留 4 m 门洞
            MakeBox("GEO_Wall_S", envRoot, new Vector3(0f, 3f, -20f), new Vector3(40f, 6f, 1f), ink);
            MakeBox("GEO_Wall_W", envRoot, new Vector3(-20f, 3f, 0f), new Vector3(1f, 6f, 40f), ink);
            MakeBox("GEO_Wall_E", envRoot, new Vector3(20f, 3f, 0f), new Vector3(1f, 6f, 40f), ink);
            MakeBox("GEO_Wall_N_L", envRoot, new Vector3(-11f, 3f, 20f), new Vector3(18f, 6f, 1f), ink);
            MakeBox("GEO_Wall_N_R", envRoot, new Vector3(11f, 3f, 20f), new Vector3(18f, 6f, 1f), ink);

            // 两栋楼 + 一道矮墙：给出三条路线（左绕 / 中穿幕 / 右贴墙）
            MakeBox("BLD_A", envRoot, new Vector3(-10f, 5f, 6f), new Vector3(10f, 10f, 12f), bloodBrick);
            MakeBox("BLD_B", envRoot, new Vector3(9f, 4f, -6f), new Vector3(10f, 8f, 10f), bloodBrick);
            MakeBox("GEO_LowWall", envRoot, new Vector3(0f, 1.2f, -4f), new Vector3(16f, 2.4f, 1f), iron);

            // 掩体
            Vector3[] cratePos =
            {
                new Vector3(-3f, 0.5f, 3f), new Vector3(-1.8f, 0.5f, 3.4f),
                new Vector3(4f, 0.5f, 8f), new Vector3(-6.5f, 0.5f, -9f),
                new Vector3(11.5f, 0.5f, 3f)
            };
            for (int i = 0; i < cratePos.Length; i++)
            {
                var crate = MakeBox("PROP_Crate_" + (i + 1), envRoot, cratePos[i], new Vector3(1.1f, 1f, 1.1f), iron);
                // 可推：Rigidbody 冻结 Y/旋转，玩家顶住即可推动（遮光用）
                crate.AddComponent<Rigidbody>();
                crate.AddComponent<Nightborn.Interaction.Pushable>();
            }

            // ---------- 薄幕（Veil 层）：影身可穿，光身被挡 ----------
            int veilLayer = LayerMask.NameToLayer("Veil");
            var veilA = MakeBox("VEIL_Curtain_A", envRoot, new Vector3(12f, 2f, 8f), new Vector3(0.2f, 4f, 9f), mist);
            veilA.layer = veilLayer;
            var veilB = MakeBox("VEIL_Curtain_B", envRoot, new Vector3(0f, 2f, 14f), new Vector3(6f, 4f, 0.2f), mist);
            veilB.layer = veilLayer;

            // ---------- 街灯（可吹灭） ----------
            var lampRoot = new GameObject("LAMP_Street_1");
            lampRoot.transform.SetParent(envRoot, false);
            lampRoot.transform.position = new Vector3(3f, 0f, 4f);
            MakeCylinder("LAMP_Post", lampRoot.transform, new Vector3(0f, 2.2f, 0f), new Vector3(0.12f, 2.2f, 0.12f), iron);

            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "LAMP_Bulb";
            bulb.transform.SetParent(lampRoot.transform, false);
            bulb.transform.localPosition = new Vector3(0f, 4.5f, 0f);
            bulb.transform.localScale = Vector3.one * 0.3f;
            bulb.GetComponent<Renderer>().sharedMaterial = candle;
            Object.DestroyImmediate(bulb.GetComponent<SphereCollider>());

            var lampLightGo = new GameObject("LAMP_Light");
            lampLightGo.transform.SetParent(lampRoot.transform, false);
            lampLightGo.transform.localPosition = new Vector3(0f, 4.5f, 0f);
            var lampLight = lampLightGo.AddComponent<Light>();
            lampLight.type = LightType.Point;
            lampLight.color = C_Candle;
            lampLight.intensity = 2.2f;
            lampLight.range = 9f;
            lampLight.shadows = LightShadows.None;

            var douse = lampRoot.AddComponent<Nightborn.Level.DouseableLamp>();
            var soDouse = new SerializedObject(douse);
            soDouse.FindProperty("lights").arraySize = 1;
            soDouse.FindProperty("lights").GetArrayElementAtIndex(0).objectReferenceValue = lampLight;
            soDouse.FindProperty("bulbs").arraySize = 1;
            soDouse.FindProperty("bulbs").GetArrayElementAtIndex(0).objectReferenceValue = bulb.GetComponent<Renderer>();
            soDouse.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 敌人 ----------
            // 开阔带：避开 BLD_A(z>=0)、BLD_B(x>=4) 和矮墙(z=-4)
            MakeGuard(envRoot, "NPC_Guard_1", new Vector3(-2f, 0f, -14f),
                new[]
                {
                    new Vector3(-2f, 0f, -14f), new Vector3(2f, 0f, -14f),
                    new Vector3(2f, 0f, -8f), new Vector3(-2f, 0f, -8f)
                },
                mist, 1.6f, 65f, 11f, false);

            // 执灯修士：北侧走廊，灯照范围大、吹不灭
            MakeGuard(envRoot, "NPC_Monk_1", new Vector3(2f, 0f, 2f),
                new[]
                {
                    new Vector3(2f, 0f, 2f), new Vector3(-2f, 0f, 2f),
                    new Vector3(-2f, 0f, 10f), new Vector3(2f, 0f, 10f)
                },
                iron, 1.3f, 60f, 9f, true);
            // ---------- 检查点 ----------
            MakeCheckpoint(envRoot, 0, new Vector3(0f, 0f, -16f));
            MakeCheckpoint(envRoot, 1, new Vector3(0f, 0f, 2f));

            // ---------- 出口 ----------
            var exitGo = new GameObject("LEVEL_Exit");
            exitGo.transform.SetParent(envRoot, false);
            exitGo.transform.position = new Vector3(0f, 1.5f, 19f);
            var exitCol = exitGo.AddComponent<BoxCollider>();
            exitCol.size = new Vector3(6f, 3f, 2f);
            exitCol.isTrigger = true;
            exitGo.AddComponent<Nightborn.Level.ExitGoal>();
            MakeBox("GEO_DoorFrame_L", envRoot, new Vector3(-2.6f, 2f, 19.5f), new Vector3(1f, 4f, 1f), iron);
            MakeBox("GEO_DoorFrame_R", envRoot, new Vector3(2.6f, 2f, 19.5f), new Vector3(1f, 4f, 1f), iron);
            MakeBox("GEO_DoorFrame_Top", envRoot, new Vector3(0f, 4.2f, 19.5f), new Vector3(6.2f, 1f, 1f), iron);

            // ---------- 血之遗物 ----------
            var relicGo = new GameObject("PROP_Relic_1");
            relicGo.transform.SetParent(envRoot, false);
            relicGo.transform.position = new Vector3(-13f, 1f, 16f);
            var relicCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            relicCube.name = "RELIC_Body";
            relicCube.transform.SetParent(relicGo.transform, false);
            relicCube.transform.localScale = Vector3.one * 0.35f;
            relicCube.GetComponent<Renderer>().sharedMaterial = candle;

            var relicLightGo = new GameObject("RELIC_Glow");
            relicLightGo.transform.SetParent(relicGo.transform, false);
            var relicLight = relicLightGo.AddComponent<Light>();
            relicLight.type = LightType.Point;
            relicLight.color = C_Candle;
            relicLight.intensity = 0.8f;
            relicLight.range = 3f;
            relicLight.shadows = LightShadows.None;

            var relic = relicGo.AddComponent<Nightborn.Level.Relic>();
            var soRelic = new SerializedObject(relic);
            soRelic.FindProperty("relicId").stringValue = "relic_ch01_s01";
            soRelic.FindProperty("relicRenderer").objectReferenceValue = relicCube.GetComponent<Renderer>();
            soRelic.FindProperty("glow").objectReferenceValue = relicLight;
            soRelic.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 月光 ----------
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
            int playerLayer = LayerMask.NameToLayer("Player");
            var player = new GameObject("PLAYER_Ada");
            player.layer = playerLayer;
            player.transform.position = new Vector3(0f, 0.2f, -17f);
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

            // 相机
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

            // 自体光
            var glowGo = new GameObject("PLAYER_Glow");
            glowGo.transform.SetParent(player.transform, false);
            glowGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            var glowLight = glowGo.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = C_Candle;
            glowLight.intensity = 0.9f;
            glowLight.range = 3.5f;
            glowLight.shadows = LightShadows.Soft;
            glowLight.shadowStrength = 0.8f;
            glowLight.shadowCustomResolution = 512;

            var glow = player.AddComponent<Nightborn.Player.SelfGlow>();
            var soGlow = new SerializedObject(glow);
            soGlow.FindProperty("glowLight").objectReferenceValue = glowLight;
            soGlow.ApplyModifiedPropertiesWithoutUndo();

            // 光照采样（自体光排除）
            var probe = player.AddComponent<Nightborn.Player.LightExposureProbe>();
            var soProbe = new SerializedObject(probe);
            soProbe.FindProperty("selfGlow").objectReferenceValue = glow;
            soProbe.ApplyModifiedPropertiesWithoutUndo();

            // 血储 / 检测度 / 化形 / 交互
            var bloodSystem = player.AddComponent<Nightborn.Player.BloodSystem>();
            var soBlood = new SerializedObject(bloodSystem);
            soBlood.FindProperty("probe").objectReferenceValue = probe;
            soBlood.FindProperty("motor").objectReferenceValue = motor;
            soBlood.ApplyModifiedPropertiesWithoutUndo();

            var detection = player.AddComponent<Nightborn.Player.DetectionMeter>();

            var shift = player.AddComponent<Nightborn.Player.ShiftForm>();
            var soShift = new SerializedObject(shift);
            soShift.FindProperty("glow").objectReferenceValue = glow;
            soShift.FindProperty("probe").objectReferenceValue = probe;
            soShift.FindProperty("motor").objectReferenceValue = motor;
            soShift.ApplyModifiedPropertiesWithoutUndo();

            player.AddComponent<Nightborn.Interaction.PlayerInteractor>();
            // M1：吸血与血视
            var feeder = player.AddComponent<Nightborn.Player.PlayerFeeder>();
            var soFeeder = new SerializedObject(feeder);
            soFeeder.FindProperty("blood").objectReferenceValue = bloodSystem;
            soFeeder.FindProperty("motor").objectReferenceValue = motor;
            soFeeder.ApplyModifiedPropertiesWithoutUndo();

            player.AddComponent<Nightborn.Player.BloodSight>();
            var potion = player.AddComponent<Nightborn.Player.DimPotion>();
            var soPotion = new SerializedObject(potion);
            soPotion.FindProperty("glow").objectReferenceValue = glow;
            soPotion.ApplyModifiedPropertiesWithoutUndo();

            // 关卡流程
            var flowGo = new GameObject("LEVEL_Flow");
            var flow = flowGo.AddComponent<Nightborn.Level.LevelFlow>();
            var soFlow = new SerializedObject(flow);
            soFlow.FindProperty("levelId").stringValue = "Ch01_S01";
            soFlow.FindProperty("levelIndex").intValue = 0;
            soFlow.ApplyModifiedPropertiesWithoutUndo();

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

        private static void MakeCheckpoint(Transform parent, int index, Vector3 position)
        {
            // 立式触发区：贴地薄板太难被 CharacterController 碰到
            var go = MakeBox("LEVEL_Checkpoint_" + index, parent, position + Vector3.up * 1f, new Vector3(3f, 2f, 3f), null);
            var col = go.GetComponent<BoxCollider>();
            col.isTrigger = true;
            var cp = go.AddComponent<Nightborn.Level.Checkpoint>();
            var so = new SerializedObject(cp);
            so.FindProperty("index").intValue = index;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        /// <summary>生成一个敌人：机身 + 视线锥 + GuardAI + NavMeshAgent + 可吸目标（可选提灯）。</summary>
        private static GameObject MakeGuard(Transform parent, string name, Vector3 position, Vector3[] waypointPositions,
            Material bodyMaterial, float patrolSpeed, float viewAngle, float viewDistance, bool withLantern)
        {
            int guardLayer = LayerMask.NameToLayer("Guard");

            var ways = new Transform[waypointPositions.Length];
            for (int i = 0; i < waypointPositions.Length; i++)
            {
                var wp = new GameObject(name + "_WP_" + (i + 1));
                wp.transform.SetParent(parent, false);
                wp.transform.position = waypointPositions[i];
                ways[i] = wp.transform;
            }

            var guard = new GameObject(name);
            guard.layer = guardLayer;
            guard.transform.position = position;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "GUARD_Body";
            body.transform.SetParent(guard.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.62f, 0.9f, 0.62f);
            body.GetComponent<Renderer>().sharedMaterial = bodyMaterial;
            body.layer = guardLayer;

            var coneGo = new GameObject("GUARD_Cone");
            coneGo.transform.SetParent(guard.transform, false);
            coneGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var cone = coneGo.AddComponent<Light>();
            cone.type = LightType.Spot;
            cone.range = viewDistance + 1f;
            cone.spotAngle = viewAngle;
            cone.intensity = 1.2f;
            cone.color = C_Candle;
            cone.shadows = LightShadows.None;

            var ai = guard.AddComponent<Nightborn.Detection.GuardAI>();
            var so = new SerializedObject(ai);
            var wpProp = so.FindProperty("waypoints");
            wpProp.arraySize = ways.Length;
            for (int i = 0; i < ways.Length; i++)
                wpProp.GetArrayElementAtIndex(i).objectReferenceValue = ways[i];
            so.FindProperty("coneLight").objectReferenceValue = cone;
            so.FindProperty("patrolSpeed").floatValue = patrolSpeed;
            so.FindProperty("viewAngle").floatValue = viewAngle;
            so.FindProperty("viewDistance").floatValue = viewDistance;
            so.ApplyModifiedPropertiesWithoutUndo();

            var agent = guard.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.speed = patrolSpeed;
            agent.angularSpeed = 150f;
            agent.acceleration = patrolSpeed * 4f;
            agent.radius = 0.3f;
            agent.height = 1.8f;
            agent.stoppingDistance = 0.2f;
            agent.autoBraking = true;

            var feedTarget = guard.AddComponent<Nightborn.Level.FeedTarget>();
            var soFeed = new SerializedObject(feedTarget);
            soFeed.FindProperty("guard").objectReferenceValue = ai;
            soFeed.FindProperty("bodyVisual").objectReferenceValue = body.transform;
            soFeed.ApplyModifiedPropertiesWithoutUndo();

            if (withLantern)
            {
                var lanternGo = new GameObject("MONK_Lantern");
                lanternGo.transform.SetParent(guard.transform, false);
                lanternGo.transform.localPosition = new Vector3(0.4f, 1.25f, 0.25f);
                var lantern = lanternGo.AddComponent<Light>();
                lantern.type = LightType.Point;
                lantern.color = C_Candle;
                lantern.intensity = 1.4f;
                lantern.range = 5f;
                lantern.shadows = LightShadows.Soft;
                lantern.shadowStrength = 0.75f;
                lantern.shadowCustomResolution = 512;

                var monk = guard.AddComponent<Nightborn.Detection.LanternMonk>();
                var soMonk = new SerializedObject(monk);
                soMonk.FindProperty("lantern").objectReferenceValue = lantern;
                soMonk.FindProperty("guard").objectReferenceValue = ai;
                soMonk.ApplyModifiedPropertiesWithoutUndo();
            }

            return guard;
        }
        /// <summary>生成主菜单 / 选关场景（代码建 UI，无 prefab 依赖）。</summary>
        private static void BuildMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("CAM_Menu");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.055f, 0.059f, 0.071f);
            cam.orthographic = true;
            camGo.AddComponent<UniversalAdditionalCameraData>();

            var root = new GameObject("MENU_Root");
            root.AddComponent<Nightborn.Menu.NightbornMenu>();

            EditorSceneManager.SaveScene(scene, MenuScenePath);
            Debug.Log("[Nightborn] 菜单场景已保存：" + MenuScenePath);
        }
        private static void RenderPreview(UnityEngine.Camera ignored)
        {
            try
            {
                string outDir = Path.Combine(Directory.GetCurrentDirectory(), "_Preview");
                Directory.CreateDirectory(outDir);

                // 单独拉一台预览机位，避免依赖运行时相机逻辑（batchmode 下 Update 不跑）。
                var go = new GameObject("PREVIEW_Camera");
                go.transform.position = new Vector3(0f, 9f, -25f);
                go.transform.rotation = Quaternion.Euler(26f, 0f, 0f);
                var cam = go.AddComponent<UnityEngine.Camera>();
                cam.fieldOfView = 60f;
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
