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
        private const string ScenePath2 = ScenesDir + "/L_Ch01_S02_Street.unity";
        private const string ScenePath3 = ScenesDir + "/L_Ch01_S03_Street.unity";
        private const string MenuScenePath = ScenesDir + "/L_Main.unity";
        private const string ChurchScenePath = ScenesDir + "/L_Ch02_S01_Church.unity";
        private const string ChurchPath2 = ScenesDir + "/L_Ch02_S02_Church.unity";
        private const string ChurchPath3 = ScenesDir + "/L_Ch02_S03_Church.unity";
        private const string MansionPath1 = ScenesDir + "/L_Ch03_S01_Mansion.unity";
        private const string MansionPath2 = ScenesDir + "/L_Ch03_S02_Mansion.unity";
        private const string MansionPath3 = ScenesDir + "/L_Ch03_S03_Mansion.unity";
        private const string CathedralPath = ScenesDir + "/L_Ch04_S01_Cathedral.unity";

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
            BuildStreetScene(pipeline, materials, 0, ScenePath);
            BuildStreetScene(pipeline, materials, 1, ScenePath2);
            BuildStreetScene(pipeline, materials, 2, ScenePath3);
            BuildChurchScene(materials, 0, ChurchScenePath);
            BuildChurchScene(materials, 1, ChurchPath2);
            BuildChurchScene(materials, 2, ChurchPath3);
            BuildMansionScene(materials, 0, MansionPath1);
            BuildMansionScene(materials, 1, MansionPath2);
            BuildMansionScene(materials, 2, MansionPath3);
            BuildCathedralScene(materials);
            BuildMenuScene();

            // 2026-11-11（P1）：顺序反过来。DecorateAll() 是遍历 EditorBuildSettings.scenes 干活的，
            // 原来它跑在「写 Build Settings」之前，看到的是上一次构建残留的列表；而 BuildStreetScene 里
            // 又有一句把列表重置成单场景的代码 —— 结果本次构建中途列表只剩 L_Ch01_S01，
            // 9 个关卡（教堂 / 宅邸 / 终章）一个目标点、一条教程提示都装饰不上。
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(ScenePath2, true),
                new EditorBuildSettingsScene(ScenePath3, true),
                new EditorBuildSettingsScene(ChurchScenePath, true),
                new EditorBuildSettingsScene(ChurchPath2, true),
                new EditorBuildSettingsScene(ChurchPath3, true),
                new EditorBuildSettingsScene(MansionPath1, true),
                new EditorBuildSettingsScene(MansionPath2, true),
                new EditorBuildSettingsScene(MansionPath3, true),
                new EditorBuildSettingsScene(CathedralPath, true)
            };

            // 场景是几何，目标文案 / 教程提示是内容：生成完再装饰一层（见 NightbornObjectiveSetup）
            NightbornObjectiveSetup.DecorateAll();
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

        private static void BuildStreetScene(UniversalRenderPipelineAsset pipeline, Material[] mats, int variant, string path)
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
            // 2026-11-11 主 agent 改：原来是 16 m 通长（x −8..8），而 BLD_A(−15..−5) 与 BLD_B(4..14)
            // 之间只有 9 m 走廊 —— 等于把中路整段封死，Bootstrap 注释写的「左绕 / 中穿幕 / 右贴墙」
            // 实际只剩左右两条。收窄到 x −8..2，在 x 2..4 留 2 m 缺口，中路才真的存在。
            MakeBox("GEO_LowWall", envRoot, new Vector3(-3f, 1.2f, -4f), new Vector3(10f, 2.4f, 1f), iron);

            // 掩体：前两箱放在执灯修士的巡逻方框（x±2 / z 2..10）里当遮蔽。
            // 2026-11-11：原来在 (-3,3) 与 (-1.8,3.4)，两箱并在 x∈[-3.55,-1.25] 上正好横在修士
            // (−2,2)↔(−2,10) 这条腿上，NavMesh 被挖断，修士每趟都得绕箱走；挪进圈内后巡逻线是干净的。
            Vector3[] cratePos =
            {
                new Vector3(-0.8f, 0.5f, 5.2f), new Vector3(0.6f, 0.5f, 6.2f),
                new Vector3(11.5f, 0.5f, 3f), new Vector3(-6.5f, 0.5f, -9f),
                new Vector3(4f, 0.5f, 8f)
            };
            int crateCount = variant >= 2 ? 3 : cratePos.Length;
            for (int i = 0; i < crateCount; i++)
            {
                var crate = MakeBox("PROP_Crate_" + (i + 1), envRoot, cratePos[i], new Vector3(1.1f, 1f, 1.1f), iron);
                // 可推：Rigidbody 冻结 Y/旋转，玩家顶住即可推动（遮光用）
                crate.AddComponent<Rigidbody>();
                crate.AddComponent<Nightborn.Interaction.Pushable>();
            }

            // 东廊街灯。2026-11-11：原来在 (-8,0,10)，而 BLD_A 的体块是 x∈[-15,-5] / z∈[0,12] / y∈[0,10]
            // —— 整盏灯埋在楼里，照不到人、也按不到 E。挪到东廊后「东廊有光、西巷全黑」才成为真实的路线差异。
            if (variant >= 1)
                MakeCandle(envRoot, "LAMP_Street_2", new Vector3(16.5f, 0f, 6f), true, candle, iron);

            // ---------- 薄幕（Veil 层）：影身可穿，光身被挡 ----------
            int veilLayer = LayerMask.NameToLayer("Veil");
            // A 帘只挡半边东廊（可以绕过去）——保留「多条路」；B 帘封北门口（绕不过去）——化形教学点。
            var veilA = MakeBox("VEIL_Curtain_A", envRoot, new Vector3(12f, 2f, 8f), new Vector3(0.2f, 4f, 9f), mist);
            veilA.layer = veilLayer;

            // ---------- 北门（2026-11-11，P0 设计修正） ----------
            // 原来 z=14 的薄幕孤零零立在空场上：幕宽 6 m，但北院（z 14..20）是 40 m 宽的开放场地，
            // 玩家从 x=±4 绕过去就摸到出口了 ——「必须化形」是假的，S01 的教程也就一个字没提 Tab。
            // 现在 z=16 立一道只留 x∈[-3,3] 口子的横墙，口子被 B 帘封死：
            //   西巷（灯黑、有遗物）/ 中庭（修士巡逻圈）/ 东廊（有街灯）三条路都能到门口，
            //   但进北院只有「Tab 影身穿幕」一条。6 s 影身走完 z 16→18 绰绰有余。
            MakeBox("GEO_Gate_W", envRoot, new Vector3(-11.25f, 2f, 16f), new Vector3(16.5f, 4f, 0.6f), iron);
            MakeBox("GEO_Gate_E", envRoot, new Vector3(11.25f, 2f, 16f), new Vector3(16.5f, 4f, 0.6f), iron);
            var veilB = MakeBox("VEIL_Curtain_B", envRoot, new Vector3(0f, 2f, 16f), new Vector3(6f, 4f, 0.2f), mist);
            veilB.layer = veilLayer;

            // ---------- 街灯（可吹灭） ----------
            var lampRoot = new GameObject("LAMP_Street_1");
            lampRoot.transform.SetParent(envRoot, false);
            lampRoot.transform.position = variant >= 2 ? new Vector3(0f, 0f, 8f) : new Vector3(3f, 0f, 4f);
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

            if (variant >= 1) // 第一夜只有守卫；第二/三夜加执灯修士
            {
            // 执灯修士：北侧走廊，灯照范围大、吹不灭
            MakeGuard(envRoot, "NPC_Monk_1", new Vector3(2f, 0f, 2f),
                new[]
                {
                    new Vector3(2f, 0f, 2f), new Vector3(-2f, 0f, 2f),
                    new Vector3(-2f, 0f, 10f), new Vector3(2f, 0f, 10f)
                },
                iron, 1.3f, 60f, 9f, true);
            }
            // ---------- 检查点 ----------
            MakeCheckpoint(envRoot, 0, new Vector3(0f, 0f, -16f), iron);
            // 2026-11-11：原来 (0,0,2) 正好压在执灯修士 (2,0,2)↔(−2,0,2) 这一段巡逻线上，
            // 在 S02/S03 里复活就是「睁眼面对一盏吹不灭的灯」。挪到 BLD_A 北侧的暗角（北门前），
            // 既是安全复活点，也是「最后一段」的存档提示。
            MakeCheckpoint(envRoot, 1, new Vector3(-8.5f, 0f, 13.5f), iron);

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
            // 2026-11-11：原来在 (-13,1,16)，正好落在新北门墙 z=16 的墙身里。挪到西巷深处（BLD_A 背后），
            // 成为「绕远路换收集」的选择：西巷全黑无灯，东廊有灯、中庭有修士。
            relicGo.transform.position = new Vector3(-17f, 1f, 10f);
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
            // 2026-11-11（P0）：三关原来都写死 "relic_ch01_s01"，SaveSystem.relicIds 会把三关的遗物
            // 记成同一个 id —— 第二、三关进关就显示「已收集」。按关卡编号生成，与宅邸那处一致。
            soRelic.FindProperty("relicId").stringValue = "relic_ch01_s0" + (variant + 1);
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

            var levelId = "Ch01_S0" + (variant + 1);
            var playerCam = CreatePlayer(new Vector3(0f, 0.2f, -17f), mist, levelId, variant);




            // ---------- 全局 Volume ----------
            var volumeGo = new GameObject("POSTFX_GlobalVolume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(SettingsDir + "/VP_Global.asset");

            // ---------- 存档 ----------
            EditorSceneManager.SaveScene(scene, path);
            // 2026-11-11：删掉这里原来那句 `EditorBuildSettings.scenes = new[]{ ScenePath }`。
            // 它每建完一个街区场景就把 Build Settings 重置成单场景，靠 BuildAll 末尾再覆盖回来；
            // 单独调用 BuildStreetScene（或中途抛异常）就会留下只有第一关的 Build Settings。列表统一由 BuildAll 负责。
            Debug.Log("[Nightborn] 街区场景已保存：" + path);

            RenderPreview(playerCam);
        }

        /// <summary>第二章：教堂（室内、纵深）+ 夜巡犬。</summary>
        private static void BuildChurchScene(Material[] mats, int variant, string path)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Material ink = mats[0], bloodBrick = mats[1], candle = mats[2], iron = mats[3], mist = mats[4];

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.014f;
            RenderSettings.fogColor = C_Mist;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.13f, 0.14f, 0.18f);
            RenderSettings.skybox = null;

            var envRoot = new GameObject("ENV_Church").transform;
            envRoot.gameObject.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            envRoot.gameObject.AddComponent<Nightborn.Level.NavMeshRuntimeBake>();

            MakeBox("GEO_Ground", envRoot, new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), ink);
            MakeBox("GEO_Wall_S_L", envRoot, new Vector3(-11f, 6f, -20f), new Vector3(18f, 12f, 1f), ink);
            MakeBox("GEO_Wall_S_R", envRoot, new Vector3(11f, 6f, -20f), new Vector3(18f, 12f, 1f), ink);
            MakeBox("GEO_Wall_N_L", envRoot, new Vector3(-11f, 6f, 20f), new Vector3(18f, 12f, 1f), ink);
            MakeBox("GEO_Wall_N_R", envRoot, new Vector3(11f, 6f, 20f), new Vector3(18f, 12f, 1f), ink);
            MakeBox("GEO_Wall_W", envRoot, new Vector3(-20f, 6f, 0f), new Vector3(1f, 12f, 40f), ink);
            MakeBox("GEO_Wall_E", envRoot, new Vector3(20f, 6f, 0f), new Vector3(1f, 12f, 40f), ink);

            for (int i = 0; i < 5; i++)
            {
                float z = -14f + i * 7f;
                MakeCylinder("GEO_Column_L_" + i, envRoot, new Vector3(-6f, 5f, z), new Vector3(1.1f, 5f, 1.1f), iron);
                MakeCylinder("GEO_Column_R_" + i, envRoot, new Vector3(6f, 5f, z), new Vector3(1.1f, 5f, 1.1f), iron);
            }

            for (int i = 0; i < 3; i++)
            {
                float z = -9f + i * 5f;
                MakeBox("PROP_Pew_L_" + i, envRoot, new Vector3(-11f, 0.45f, z), new Vector3(3f, 0.9f, 1.2f), bloodBrick);
                MakeBox("PROP_Pew_R_" + i, envRoot, new Vector3(11f, 0.45f, z), new Vector3(3f, 0.9f, 1.2f), bloodBrick);
            }

            MakeBox("GEO_Altar", envRoot, new Vector3(0f, 0.5f, 14f), new Vector3(12f, 1f, 8f), iron);
            MakeBox("GEO_AltarBlock", envRoot, new Vector3(0f, 1.4f, 16f), new Vector3(3f, 0.8f, 1.4f), bloodBrick);

            MakeCandle(envRoot, "LAMP_Church_L", new Vector3(-8f, 0f, 4f), true, candle, iron);
            if (variant < 2)
                MakeCandle(envRoot, "LAMP_Church_R", new Vector3(8f, 0f, -4f), true, candle, iron);

            int veilLayer = LayerMask.NameToLayer("Veil");
            // ---------- 圣所隔屏（2026-11-11，P0 设计修正） ----------
            // 原来两条薄幕立在 x=±7（z 9..15），可它们外侧（|x|>7.1）是一整条空过道 ——
            // 从南边绕到 x=−8 再往北走，一路无阻地摸到出口，第二段的「穿幕」根本不存在。
            // 现在 z=9 立一道通到两侧外墙的隔屏，只留两座侧廊拱门（x∈[-14,-11] / [11,14]），
            // 拱门各挂一道薄幕：中殿与圣所之间只有「Tab 影身穿幕」这一条门，且西/东两条路线可选。
            MakeBox("GEO_Screen_W", envRoot, new Vector3(-16.75f, 3f, 9f), new Vector3(5.5f, 6f, 0.6f), ink);
            MakeBox("GEO_Screen_M", envRoot, new Vector3(0f, 3f, 9f), new Vector3(22f, 6f, 0.6f), ink);
            MakeBox("GEO_Screen_E", envRoot, new Vector3(16.75f, 3f, 9f), new Vector3(5.5f, 6f, 0.6f), ink);
            var veilL = MakeBox("VEIL_Altar_L", envRoot, new Vector3(-12.5f, 2f, 9f), new Vector3(3f, 4f, 0.2f), mist);
            veilL.layer = veilLayer;
            var veilR = MakeBox("VEIL_Altar_R", envRoot, new Vector3(12.5f, 2f, 9f), new Vector3(3f, 4f, 0.2f), mist);
            veilR.layer = veilLayer;

            MakeCheckpoint(envRoot, 0, new Vector3(0f, 0f, -16f), iron);
            // 2026-11-11：原来 (0,0,4) 正好在猎犬 (0,0,−12)↔(0,0,6) 的巡逻直线上，复活就撞狗鼻子。
            // 挪到中殿西北角、隔屏之前：这是「过拱门前」的最后一个存档点。
            MakeCheckpoint(envRoot, 1, new Vector3(-4.5f, 0f, 7.5f), iron);

            var exitGo = new GameObject("LEVEL_Exit");
            exitGo.transform.SetParent(envRoot, false);
            exitGo.transform.position = new Vector3(0f, 1.5f, 19f);
            var exitCol = exitGo.AddComponent<BoxCollider>();
            exitCol.size = new Vector3(6f, 3f, 2f);
            exitCol.isTrigger = true;
            exitGo.AddComponent<Nightborn.Level.ExitGoal>();

            var relicGo = new GameObject("PROP_Relic_1");
            relicGo.transform.SetParent(envRoot, false);
            relicGo.transform.position = new Vector3(-14f, 1f, -16f);
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
            // 2026-11-11（P0）：三关原来都写死 "relic_ch02_s01"，遗物在 SaveSystem 里互相算成已收集。
            soRelic.FindProperty("relicId").stringValue = "relic_ch02_s0" + (variant + 1);
            soRelic.FindProperty("relicRenderer").objectReferenceValue = relicCube.GetComponent<Renderer>();
            soRelic.FindProperty("glow").objectReferenceValue = relicLight;
            soRelic.ApplyModifiedPropertiesWithoutUndo();

            var moon = new GameObject("LIGHT_Moonlight");
            var moonLight = moon.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.color = new Color(0.55f, 0.68f, 0.85f);
            moonLight.intensity = 0.7f;
            moonLight.shadows = LightShadows.Soft;
            moonLight.shadowStrength = 0.85f;
            moonLight.shadowCustomResolution = 2048;
            moon.transform.rotation = Quaternion.Euler(62f, -25f, 0f);

            // 主猎犬：中殿方框巡逻。2026-11-11：原来第三、四个航点是 (−9,0,0)/(9,0,0)，
            // 两点连线正穿过 x=±6、z=0 那两根柱子（半径 0.55），NavMesh 每次都得绕柱，狗像在打摆子。
            var hound = MakeGuard(envRoot, "NPC_Hound_1", new Vector3(0f, 0f, -8f),
                new[]
                {
                    new Vector3(3f, 0f, -12f), new Vector3(3f, 0f, 6f),
                    new Vector3(-3f, 0f, 6f), new Vector3(-3f, 0f, -12f)
                },
                ink, 2.4f, 35f, 7f, false);

            var houndAi = hound.GetComponent<Nightborn.Detection.GuardAI>();
            var soHound = new SerializedObject(houndAi);
            soHound.FindProperty("chaseSpeed").floatValue = 4.6f;
            soHound.FindProperty("catchDistance").floatValue = 1.7f;
            soHound.ApplyModifiedPropertiesWithoutUndo();
            hound.AddComponent<Nightborn.Detection.Bloodhound>();

            if (variant >= 1)
            {
                // 第二只猎犬巡外圈侧廊。2026-11-11：原来在 x=±12 往返，而长椅（x −12.5..−9.5）在 z=−9/−4/1
                // 三排把这条线拦腰截断，狗每趟都要绕进内侧过道再绕回来。改到 x=±13.5、横穿 z=±5。
                var hound2 = MakeGuard(envRoot, "NPC_Hound_2", new Vector3(-13.5f, 0f, 5f),
                    new[]
                    {
                        new Vector3(-13.5f, 0f, 5f), new Vector3(-13.5f, 0f, -5f),
                        new Vector3(13.5f, 0f, -5f), new Vector3(13.5f, 0f, 5f)
                    },
                    ink, 2.2f, 35f, 7f, false);
                hound2.AddComponent<Nightborn.Detection.Bloodhound>();
            }

            if (variant >= 2)
            {
                // 第三夜：执灯修士守西拱门。2026-11-11：原来站在 (0,0,10) —— 那正是 GEO_Altar
                // （x −6..6 / z 10..18 / y 0..1）的体块内部，NavMesh 上没有这个点，代理会退化成
                // 「直线穿墙」模式一路穿过祭坛。现在改成西侧廊的方框巡逻（拱门与长椅之间）。
                MakeGuard(envRoot, "NPC_Monk_Church", new Vector3(-13f, 0f, 3f),
                    new[]
                    {
                        new Vector3(-13f, 0f, 3f), new Vector3(-13f, 0f, 8f),
                        new Vector3(-8.5f, 0f, 8f), new Vector3(-8.5f, 0f, 3f)
                    },
                    iron, 1.2f, 55f, 9f, true);
            }

            var levelId = "Ch02_S0" + (variant + 1);
            var playerCam = CreatePlayer(new Vector3(0f, 0.2f, -17f), mist, levelId, 3 + variant);

            var volumeGo = new GameObject("POSTFX_GlobalVolume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(SettingsDir + "/VP_Global.asset");

            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("[Nightborn] 教堂场景已保存：" + path);
            RenderPreview(playerCam);
        }

        /// <summary>第三章：宅邸（两层：地面 + 回廊）+ 钟楼看守。</summary>
        private static void BuildMansionScene(Material[] mats, int variant, string path)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Material ink = mats[0], bloodBrick = mats[1], candle = mats[2], iron = mats[3], mist = mats[4];

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.016f;
            RenderSettings.fogColor = C_Mist;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.11f, 0.12f, 0.16f);
            RenderSettings.skybox = null;

            var envRoot = new GameObject("ENV_Mansion").transform;
            envRoot.gameObject.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            envRoot.gameObject.AddComponent<Nightborn.Level.NavMeshRuntimeBake>();

            // 地面与外墙
            MakeBox("GEO_Ground", envRoot, new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), ink);
            MakeBox("GEO_Wall_S", envRoot, new Vector3(0f, 7f, -20f), new Vector3(40f, 14f, 1f), ink);
            MakeBox("GEO_Wall_W", envRoot, new Vector3(-20f, 7f, 0f), new Vector3(1f, 14f, 40f), ink);
            MakeBox("GEO_Wall_E", envRoot, new Vector3(20f, 7f, 0f), new Vector3(1f, 14f, 40f), ink);
            MakeBox("GEO_Wall_N_L", envRoot, new Vector3(-11f, 7f, 20f), new Vector3(18f, 14f, 1f), ink);
            MakeBox("GEO_Wall_N_R", envRoot, new Vector3(11f, 7f, 20f), new Vector3(18f, 14f, 1f), ink);

            // 二层回廊（东侧）+ 栏杆
            MakeBox("GEO_Mezzanine", envRoot, new Vector3(6f, 5f, 0f), new Vector3(28f, 0.6f, 40f), ink);
            // 2026-11-11（P0，真机实测）：原来一条 40 m 通长栏杆 (x=−8, y 5.1..6.7) 正好压在坡道顶端接缝上，
            // 玩家爬到 (−8.6, 5.1, 0) 就顶死，三个宅邸关都走不到二层。现在按坡道落点（z≈−0.7）
            // 开一个 5.4 m 的缺口，栏杆分南北两段，底边贴回廊顶面 5.3。
            MakeBox("GEO_Rail_S", envRoot, new Vector3(-8f, 6.1f, -11.35f), new Vector3(0.35f, 1.6f, 17.3f), iron);
            MakeBox("GEO_Rail_N", envRoot, new Vector3(-8f, 6.1f, 11.35f), new Vector3(0.35f, 1.6f, 17.3f), iron);

            // 坡道（地面 -> 二层），南北走向，东缘与回廊西缘 x=−8 重叠 0.5 m。
            // 2026-11-11（P0，真机实测）：旧坡道绕 Z 转 23.5°、从 x≈−19.6 爬到 x≈−8.07，
            //   · 低端整块板浮在地面上方 0.29 m，侧面靠近要抬 0.404 m，而 stepOffset 只有 0.4 —— 差 4 mm 上不去；
            //   · 高端顶面 5.294 对上回廊 5.3 本来只差 6 mm，却被栏杆挡死。
            // 新参数（解算）：长 13.55 沿 +z 爬升，顶面从 (y −0.084, z −13.13) 到 (y 5.32, z −0.70)。
            //   · 低端埋进地面负 0.08 m：地面 y=0 与坡面在 z≈−12.89 处相交，走过去是**零台阶**；
            //   · 高端 5.32 比回廊顶面高 0.02 m 并与它重叠 0.5 m：无缝隙、无同面 Z-fighting，0.02 一步随便上。
            var ramp = MakeBox("GEO_Ramp", envRoot, new Vector3(-10f, 2.389f, -6.813f), new Vector3(5f, 0.5f, 13.55f), iron);
            ramp.transform.rotation = Quaternion.Euler(-23.5f, 0f, 0f);
            // 坡脚踏板：6 cm 高的薄板，与坡面在 z≈−12.75 处相交。NavMesh 在「地面与斜面浅角度相交」
            // 那条线上偶发烘出缝隙，这块板给烘焙一个确定的重叠面（地面 → 板 6 cm → 坡面），
            // 坡脚才不会在 CalculatePath 里断掉（真机验收要求 PathComplete）。
            MakeBox("GEO_RampApron", envRoot, new Vector3(-10f, 0.03f, -12.9f), new Vector3(5f, 0.06f, 1.6f), iron);

            // 一层隔断。RoomA 是西侧一道南北墙（把地面切成东西两半），RoomB 是出生区与中厅之间的一道东西墙；
            // 2026-11-11：RoomB 改成两段、门洞对准出生轴（x −1.5..1.5）—— 原来 14 m 通长，
            // 从出生点沿 +z 走 7 m 就是一面 5 m 高的墙，中厅只能靠东西两头绕，读不出「那里有门」。
            MakeBox("GEO_RoomA", envRoot, new Vector3(-14f, 2.5f, -8f), new Vector3(0.6f, 5f, 10f), bloodBrick);
            MakeBox("GEO_RoomB_W", envRoot, new Vector3(-4.25f, 2.5f, -10f), new Vector3(5.5f, 5f, 0.6f), bloodBrick);
            MakeBox("GEO_RoomB_E", envRoot, new Vector3(4.25f, 2.5f, -10f), new Vector3(5.5f, 5f, 0.6f), bloodBrick);

            // 家具/掩体。2026-11-11（P0，真机实测）：原来 x = −4 + i*5（−4 / 1 / 6 / 11），
            // 第二个柜子占 x∈[−0.2,2.2]，正好横在出生点 (0,0.2,−17) 沿 +z 的正前方，
            // 玩家 25 s 走不动。整排挪到东半侧（x 2.8..14.2），出生轴与去坡道的西侧通道都清空。
            for (int i = 0; i < 4; i++)
                MakeBox("PROP_Cabinet_" + i, envRoot, new Vector3(4f + i * 3f, 0.8f, -14f), new Vector3(2.4f, 1.6f, 1.2f), iron);

            // 吊灯（可吹灭）
            MakeCandle(envRoot, "LAMP_Hall_L", new Vector3(-4f, 0f, 6f), true, candle, iron);
            MakeCandle(envRoot, "LAMP_Hall_R", new Vector3(10f, 5f, -6f), true, candle, iron);

            // 薄幕：钟楼塔门（二层回廊北端 z=12）。2026-11-11：
            // 原来这地方只是楼梯口一道 0.2 m 的帘子（x=−6, z∈[−3,3]），两侧都能绕过去，纯装饰；
            // 第三章三关的「化形穿空档」等于没地方练。现在把出口所在的后半层（z>12）封成钟楼间，
            // 只留 x∈[4,8] 一道挂着薄幕的门：上到二层之后必须 Tab 影身穿过去才能拿到出口。
            int veilLayer = LayerMask.NameToLayer("Veil");
            MakeBox("GEO_TowerWall_W", envRoot, new Vector3(-2f, 6.6f, 12f), new Vector3(12f, 2.6f, 0.6f), ink);
            MakeBox("GEO_TowerWall_E", envRoot, new Vector3(14f, 6.6f, 12f), new Vector3(12f, 2.6f, 0.6f), ink);
            var veil = MakeBox("VEIL_TowerGate", envRoot, new Vector3(6f, 6.6f, 12f), new Vector3(4f, 2.6f, 0.2f), mist);
            veil.layer = veilLayer;

            MakeCheckpoint(envRoot, 0, new Vector3(0f, 0f, -16f), iron);
            // 2026-11-11：检查点 1 放在回廊上、塔门之前（原 (0,5.2,10) 离看守的横向巡逻腿只有 2 m）。
            // y=5.35 而不是 5.2：触发体是 position+1 起的 3×2×3，地面标记在 position−0.1 处，
            // 5.2 会让那块金色薄板埋进回廊顶面（5.3）看不见。
            MakeCheckpoint(envRoot, 1, new Vector3(-4.5f, 5.35f, 10.5f), iron);

            // 出口在二层
            var exitGo = new GameObject("LEVEL_Exit");
            exitGo.transform.SetParent(envRoot, false);
            exitGo.transform.position = new Vector3(13f, 6.5f, 17.5f);
            var exitCol = exitGo.AddComponent<BoxCollider>();
            exitCol.size = new Vector3(6f, 3f, 2f);
            exitCol.isTrigger = true;
            exitGo.AddComponent<Nightborn.Level.ExitGoal>();

            // 遗物（二层）
            var relicGo = new GameObject("PROP_Relic_1");
            relicGo.transform.SetParent(envRoot, false);
            relicGo.transform.position = new Vector3(16f, 6f, -14f);
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
            soRelic.FindProperty("relicId").stringValue = "relic_ch03_s0" + (variant + 1);
            soRelic.FindProperty("relicRenderer").objectReferenceValue = relicCube.GetComponent<Renderer>();
            soRelic.FindProperty("glow").objectReferenceValue = relicLight;
            soRelic.ApplyModifiedPropertiesWithoutUndo();

            var moon = new GameObject("LIGHT_Moonlight");
            var moonLight = moon.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.color = new Color(0.5f, 0.62f, 0.8f);
            moonLight.intensity = 0.6f;
            moonLight.shadows = LightShadows.Soft;
            moonLight.shadowStrength = 0.85f;
            moonLight.shadowCustomResolution = 2048;
            moon.transform.rotation = Quaternion.Euler(58f, -20f, 0f);

            // 钟楼看守：二层回廊。2026-11-11：z=14 那两个航点现在在塔门墙后面（墙上只有一道挂幕的门），
            // NavMesh 上到不了，看守会永远顶在墙前。巡逻范围收回到 z≤8。
            MakeWarden(envRoot, "NPC_Warden_1", new Vector3(8f, 5.6f, -10f),
                new[]
                {
                    new Vector3(16f, 5.6f, -14f), new Vector3(16f, 5.6f, 8f),
                    new Vector3(-2f, 5.6f, 8f), new Vector3(-2f, 5.6f, -14f)
                },
                iron);

            if (variant >= 1)
            {
                // 第二夜：地面再加一名普通守卫
                // 2026-11-11 重设：守「西巷」——去坡道必经的那条 5.2 m 宽通道。
                // 原巡逻方框 (−10,−4)…(−2,8) 有两个问题：航点 (−10,0,−4) 现在落在新坡道的楔形体下面
                // （bake 会在那儿挖洞，代理要么钻坡底要么原地打摆子），而且整条路线绕开了玩家真正的通道。
                // 现在是一条 14 m 的直线（西墙与 GEO_RoomA 之间），玩家可以从他两侧摸过去。
                MakeGuard(envRoot, "NPC_Guard_1", new Vector3(-17f, 0f, -14f),
                    new[]
                    {
                        new Vector3(-17f, 0f, -16f), new Vector3(-17f, 0f, -2f)
                    },
                    mist, 1.6f, 70f, 11f, false);
            }

            if (variant >= 2)
            {
                // 第三夜：猎犬守坡道口 + 修士在二层入口。
                // 2026-11-11：原巡逻方框跨 z=−12，被 RoomB（z∈[−10.3,−9.7]）拦腰截断，每趟都要绕墙；
                // 并且起点 (−6,0,0) 正好落在旧薄幕 VEIL_Stair 体块内部（NavMesh 无点 → 直线穿墙）。
                // 薄幕已挪走，方框收到 z∈[−8,0]，四条腿都在开阔地面上。
                var hound = MakeGuard(envRoot, "NPC_Hound_1", new Vector3(-6f, 0f, 0f),
                    new[]
                    {
                        new Vector3(-6f, 0f, 0f), new Vector3(-6f, 0f, -8f),
                        new Vector3(0f, 0f, -8f), new Vector3(0f, 0f, 0f)
                    },
                    ink, 2.4f, 35f, 7f, false);
                hound.AddComponent<Nightborn.Detection.Bloodhound>();

                MakeGuard(envRoot, "NPC_Monk_Mansion", new Vector3(2f, 5.6f, 6f),
                    new[]
                    {
                        new Vector3(2f, 5.6f, 6f), new Vector3(12f, 5.6f, 6f),
                        new Vector3(12f, 5.6f, -6f), new Vector3(2f, 5.6f, -6f)
                    },
                    mist, 1.2f, 55f, 9f, true);
            }

            var levelId = "Ch03_S0" + (variant + 1);
            var playerCam = CreatePlayer(new Vector3(0f, 0.2f, -17f), mist, levelId, 6 + variant);

            var volumeGo = new GameObject("POSTFX_GlobalVolume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(SettingsDir + "/VP_Global.asset");

            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("[Nightborn] 宅邸场景已保存：" + path);
            RenderPreview(playerCam);
        }

        /// <summary>终章：主教座堂（Boss 场）。</summary>
        private static void BuildCathedralScene(Material[] mats)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Material ink = mats[0], bloodBrick = mats[1], candle = mats[2], iron = mats[3], mist = mats[4];

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.02f;
            RenderSettings.fogColor = C_Mist;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.09f, 0.10f, 0.13f);
            RenderSettings.skybox = null;

            var envRoot = new GameObject("ENV_Cathedral").transform;
            envRoot.gameObject.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            envRoot.gameObject.AddComponent<Nightborn.Level.NavMeshRuntimeBake>();

            MakeBox("GEO_Ground", envRoot, new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), ink);
            MakeBox("GEO_Wall_S", envRoot, new Vector3(0f, 8f, -20f), new Vector3(40f, 16f, 1f), ink);
            MakeBox("GEO_Wall_W", envRoot, new Vector3(-20f, 8f, 0f), new Vector3(1f, 16f, 40f), ink);
            MakeBox("GEO_Wall_E", envRoot, new Vector3(20f, 8f, 0f), new Vector3(1f, 16f, 40f), ink);
            MakeBox("GEO_Wall_N", envRoot, new Vector3(0f, 8f, 20f), new Vector3(40f, 16f, 1f), ink);

            // 环廊柱
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                MakeCylinder("GEO_Pillar_" + i, envRoot,
                    new Vector3(Mathf.Cos(a) * 12f, 5f, Mathf.Sin(a) * 12f),
                    new Vector3(1.2f, 5f, 1.2f), iron);
            }

            // 祭坛 + 南面两级台阶。2026-11-11（P0）：台面高 1.0 m，而 CharacterController 的
            // stepOffset 只有 0.4、玩家也不能跳 —— 第三段仪式要求「离祭坛 3 m 以内」，而祭坛体块
            // (|x|≤7, z 10..18) 让玩家最近只能站到 z=9.66，到 BOSS_Altar(0,1,15) 的距离恒 >5.4 m，
            // 终章永远不可能完成。加 0.34 / 0.67 两级踏板后可以走上台面，站到 z≈13.5 就进 3 m 圈了。
            MakeBox("GEO_Altar", envRoot, new Vector3(0f, 0.5f, 14f), new Vector3(14f, 1f, 8f), iron);
            MakeBox("GEO_AltarStep_1", envRoot, new Vector3(0f, 0.17f, 8.7f), new Vector3(14f, 0.34f, 1f), iron);
            MakeBox("GEO_AltarStep_2", envRoot, new Vector3(0f, 0.335f, 9.7f), new Vector3(14f, 0.67f, 1f), iron);
            MakeBox("GEO_AltarBlock", envRoot, new Vector3(0f, 1.5f, 16f), new Vector3(3f, 1f, 1.6f), bloodBrick);
            var altarAnchor = new GameObject("BOSS_Altar");
            altarAnchor.transform.SetParent(envRoot, false);
            altarAnchor.transform.position = new Vector3(0f, 1f, 15f);

            // 三盏圣灯（第一段）
            var lampA = MakeCandle(envRoot, "LAMP_Holy_A", new Vector3(-10f, 0f, -4f), true, candle, iron);
            var lampB = MakeCandle(envRoot, "LAMP_Holy_B", new Vector3(10f, 0f, -4f), true, candle, iron);
            var lampC = MakeCandle(envRoot, "LAMP_Holy_C", new Vector3(0f, 0f, 6f), true, candle, iron);

            // 侧堂（第二段的仪式门）+ 堂口薄幕。2026-11-11（P0 设计修正）：
            // 原来两道仪式门摆在 (±9,·,10) 的空地上、薄幕在 x=±5，两边互不相干 ——
            // 玩家从幕外侧走过去就触发了门，「用影身穿两道薄幕」这句目标文案是空的。
            // 现在门在贴着东西外墙的小侧堂里，堂口（|x|=12.9）挂幕：只有 Tab 影身能进去，
            // 而且必须两间都进（东西各一条路线）。室内的门触发体在 3 m 见方，一穿幕就报数。
            int veilLayer = LayerMask.NameToLayer("Veil");
            MakeBox("GEO_Chapel_W_S", envRoot, new Vector3(-16.2f, 2.5f, 3f), new Vector3(6.6f, 5f, 0.6f), ink);
            MakeBox("GEO_Chapel_W_N", envRoot, new Vector3(-16.2f, 2.5f, 9f), new Vector3(6.6f, 5f, 0.6f), ink);
            MakeBox("GEO_Chapel_E_S", envRoot, new Vector3(16.2f, 2.5f, 3f), new Vector3(6.6f, 5f, 0.6f), ink);
            MakeBox("GEO_Chapel_E_N", envRoot, new Vector3(16.2f, 2.5f, 9f), new Vector3(6.6f, 5f, 0.6f), ink);
            var veilL = MakeBox("VEIL_Side_L", envRoot, new Vector3(-12.9f, 2.5f, 6f), new Vector3(0.2f, 5f, 5.4f), mist);
            veilL.layer = veilLayer;
            var veilR = MakeBox("VEIL_Side_R", envRoot, new Vector3(12.9f, 2.5f, 6f), new Vector3(0.2f, 5f, 5.4f), mist);
            veilR.layer = veilLayer;

            // 主教（自带强光、视野极宽）
            // 2026-11-11：巡逻收成中殿里的小五边形（|x|≤6, z 2..5）。两处原因：
            //  · 原来的 (9,·,10)/(−9,·,10) 压在祭坛南沿上，代理只能绕行；
            //  · 更要紧的是巡照半径 11 m，玩家在 6.3 m 内会被灼伤、按 Tab 直接硬直 —— 侧堂门口在 |x|=12.9，
            //    主教走到 (9,10) 时离堂口只有 4 m，等于在「必须化形的地方」被烧到没法化形。
            //    收到 |x|≤6 后堂口离他最近 6.9 m，永远是安全的化形点。
            var boss = MakeGuard(envRoot, "BOSS_Bishop", new Vector3(0f, 0f, 2f),
                new[]
                {
                    new Vector3(0f, 0f, 2f), new Vector3(6f, 0f, 2f),
                    new Vector3(6f, 0f, 5f), new Vector3(-6f, 0f, 5f),
                    new Vector3(-6f, 0f, 2f)
                },
                iron, 0.8f, 100f, 16f, true);

            var bossAi = boss.GetComponent<Nightborn.Detection.GuardAI>();
            var soBoss = new SerializedObject(bossAi);
            soBoss.FindProperty("chaseSpeed").floatValue = 1.2f;
            soBoss.FindProperty("searchDuration").floatValue = 90f;
            soBoss.FindProperty("catchDistance").floatValue = 2.2f;
            soBoss.FindProperty("turnSpeed").floatValue = 60f;
            soBoss.ApplyModifiedPropertiesWithoutUndo();

            // 2026-11-11 主 agent 补：主教不能吸血。
            // MakeGuard 默认 feedable=true，不关掉的话第三段「不压光就立刻锁定」可以被一次吸血
            // 换成 60 s 空场，整个仪式白给（Warden 就是这么处理的，但主教漏了）。
            var bossFeed = boss.GetComponent<Nightborn.Level.FeedTarget>();
            if (bossFeed != null) bossFeed.SetFeedable(false);

            var bossMonk = boss.GetComponent<Nightborn.Detection.LanternMonk>();
            if (bossMonk != null)
            {
                var soMonk = new SerializedObject(bossMonk);
                soMonk.FindProperty("baseRadius").floatValue = 7f;
                soMonk.FindProperty("baseIntensity").floatValue = 1.7f;
                soMonk.FindProperty("sweepRadius").floatValue = 11f;
                soMonk.FindProperty("sweepIntensity").floatValue = 2.6f;
                soMonk.FindProperty("sweepInterval").floatValue = 9f;
                soMonk.ApplyModifiedPropertiesWithoutUndo();
            }

            // 仪式门（第二段）：挪进两间侧堂（堂口 −12.9 / 12.9 挂幕）
            var gateGoL = new GameObject("BOSS_Gate_L");
            gateGoL.transform.SetParent(envRoot, false);
            gateGoL.transform.position = new Vector3(-16f, 1.5f, 6f);
            var gateColL = gateGoL.AddComponent<BoxCollider>();
            gateColL.size = new Vector3(3f, 3f, 3f);
            gateGoL.AddComponent<Nightborn.Boss.RitualGate>();

            var gateGoR = new GameObject("BOSS_Gate_R");
            gateGoR.transform.SetParent(envRoot, false);
            gateGoR.transform.position = new Vector3(16f, 1.5f, 6f);
            var gateColR = gateGoR.AddComponent<BoxCollider>();
            gateColR.size = new Vector3(3f, 3f, 3f);
            gateGoR.AddComponent<Nightborn.Boss.RitualGate>();

            // 仪式总控
            var ritualGo = new GameObject("BOSS_Ritual");
            var ritual = ritualGo.AddComponent<Nightborn.Boss.BossRitual>();
            var soRitual = new SerializedObject(ritual);
            var lampsProp = soRitual.FindProperty("holyLamps");
            lampsProp.arraySize = 3;
            lampsProp.GetArrayElementAtIndex(0).objectReferenceValue = lampA.GetComponent<Nightborn.Level.DouseableLamp>();
            lampsProp.GetArrayElementAtIndex(1).objectReferenceValue = lampB.GetComponent<Nightborn.Level.DouseableLamp>();
            lampsProp.GetArrayElementAtIndex(2).objectReferenceValue = lampC.GetComponent<Nightborn.Level.DouseableLamp>();
            soRitual.FindProperty("altar").objectReferenceValue = altarAnchor.transform;
            soRitual.FindProperty("bossGuard").objectReferenceValue = bossAi;
            soRitual.ApplyModifiedPropertiesWithoutUndo();

            MakeCheckpoint(envRoot, 0, new Vector3(0f, 0f, -16f), iron);
            // 2026-11-11：原来 (0,0,0) 离主教的 (0,0,2) 只有 2 m，复活就直面对 100°/16 m 的视野。
            // 挪到南厅中央（z=−6），离最近巡逻腿 8 m，等主教转身再起身。
            MakeCheckpoint(envRoot, 1, new Vector3(0f, 0f, -6f), iron);

            var playerCam = CreatePlayer(new Vector3(0f, 0.2f, -17f), mist, "Ch04_S01", 9);
            // 终章给 2 瓶压光药剂：第三段仪式必须能压光，1 瓶会在前面被用掉导致卡死
            var bossPlayer = GameObject.Find("PLAYER_Ada");
            if (bossPlayer != null)
            {
                var bossPotion = bossPlayer.GetComponent<Nightborn.Player.DimPotion>();
                if (bossPotion != null)
                {
                    var soPotion = new SerializedObject(bossPotion);
                    soPotion.FindProperty("count").intValue = 2;
                    soPotion.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            var volumeGo = new GameObject("POSTFX_GlobalVolume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(SettingsDir + "/VP_Global.asset");

            EditorSceneManager.SaveScene(scene, CathedralPath);
            Debug.Log("[Nightborn] 座堂场景已保存：" + CathedralPath);
            RenderPreview(playerCam);
        }
        /// <summary>钟楼看守：宽视野 + 慢速 + 长搜索 + 吸不动。</summary>
        private static GameObject MakeWarden(Transform parent, string name, Vector3 position, Vector3[] wps, Material mat)
        {
            var go = MakeGuard(parent, name, position, wps, mat, 1.0f, 90f, 14f, false);

            var ai = go.GetComponent<Nightborn.Detection.GuardAI>();
            var so = new SerializedObject(ai);
            so.FindProperty("chaseSpeed").floatValue = 1.6f;
            so.FindProperty("searchDuration").floatValue = 60f;
            so.FindProperty("turnSpeed").floatValue = 70f;
            so.FindProperty("catchDistance").floatValue = 2.0f;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.AddComponent<Nightborn.Detection.Warden>();
            return go;
        }
        /// <summary>烛台：柱子 + 灯球 + 点光（可选可吹灭）。</summary>
        private static GameObject MakeCandle(Transform parent, string name, Vector3 position, bool douseable, Material candleMaterial, Material postMaterial)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            MakeCylinder("POST", root.transform, new Vector3(0f, 1.2f, 0f), new Vector3(0.14f, 1.2f, 0.14f), postMaterial);

            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "BULB";
            bulb.transform.SetParent(root.transform, false);
            bulb.transform.localPosition = new Vector3(0f, 2.6f, 0f);
            bulb.transform.localScale = Vector3.one * 0.32f;
            bulb.GetComponent<Renderer>().sharedMaterial = candleMaterial;
            Object.DestroyImmediate(bulb.GetComponent<SphereCollider>());

            var lightGo = new GameObject("LIGHT");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.6f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = C_Candle;
            light.intensity = 2.0f;
            light.range = 8f;
            light.shadows = LightShadows.None;

            if (!douseable) return root;

            // 2026-11-11 主 agent 补：给可吹灭的灯加一个 0.6 m 的交互触发球。
            // 原来唯一能命中的是烛台柱子的 CapsuleCollider（缩放 0.14 → 半径约 0.07 m），
            // 而 PlayerInteractor 是半径 0.25 m 的 SphereCast —— 终章要点三盏圣灯，极易点不中。
            var lampHit = root.GetComponent<SphereCollider>();
            if (lampHit == null) lampHit = root.AddComponent<SphereCollider>();
            lampHit.isTrigger = true;
            lampHit.radius = 0.6f;
            lampHit.center = new Vector3(0f, 1.2f, 0f);

            var douse = root.AddComponent<Nightborn.Level.DouseableLamp>();
            var so = new SerializedObject(douse);
            so.FindProperty("lights").arraySize = 1;
            so.FindProperty("lights").GetArrayElementAtIndex(0).objectReferenceValue = light;
            so.FindProperty("bulbs").arraySize = 1;
            so.FindProperty("bulbs").GetArrayElementAtIndex(0).objectReferenceValue = bulb.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }
        /// <summary>
        /// 标记「这个物体不参与 NavMesh 烘焙」。
        /// 触发体（检查点 / 教程提示）是看不见的体积，如果被当成实心几何烘进 NavMesh，
        /// 敌人的巡逻路径上就会出现一个洞（GuardAI 的 `isOnNavMesh` 为 false 时会退化成直线穿墙）。
        /// AI Navigation 1.1 的 NavMeshSurface 会收集 NavMeshModifier 的 markup，忽略带 ignoreFromBuild 的源。
        /// </summary>
        internal static void IgnoreNavMesh(GameObject go)
        {
            if (go == null) return;
            var mod = go.GetComponent<Unity.AI.Navigation.NavMeshModifier>();
            if (mod == null) mod = go.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
            mod.ignoreFromBuild = true;
        }

        private static void MakeCheckpoint(Transform parent, int index, Vector3 position, Material mat)
        {
            // 立式触发区：贴地薄板太难被 CharacterController 碰到
            var go = MakeBox("LEVEL_Checkpoint_" + index, parent, position + Vector3.up * 1f, new Vector3(3f, 2f, 3f), null);
            IgnoreNavMesh(go);
            // 2026-11-11: 触发体隐藏。3x2x3 实心方块的 Renderer 走默认 URP Lit（纯白），
            // 而出生点正好落在检查点 0 里，开局镜头直接被糊满白块。
            var rootRenderer = go.GetComponent<MeshRenderer>();
            if (rootRenderer != null) rootRenderer.enabled = false;
            var col = go.GetComponent<BoxCollider>();
            col.isTrigger = true;
            // 地面标记：薄板；Checkpoint 激活时会把所有子 Renderer 点亮成金色。
            // 父级缩放是 (3,2,3)，所以本地尺寸要除掉父级。
            MakeBox("GEO_Checkpoint_Mark_" + index, go.transform, new Vector3(0f, -0.5f, 0f),
                new Vector3(1.0f / 3f, 0.06f / 2f, 1.0f / 3f), mat);
            var cp = go.AddComponent<Nightborn.Level.Checkpoint>();
            var so = new SerializedObject(cp);
            so.FindProperty("index").intValue = index;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        /// <summary>玩家 + 相机（多张场景共用）。返回主相机，供预览渲染使用。</summary>
        private static UnityEngine.Camera CreatePlayer(Vector3 spawn, Material bodyMaterial, string levelId, int levelIndex)
        {
            // ---------- 玩家 ----------
            int playerLayer = LayerMask.NameToLayer("Player");
            var player = new GameObject("PLAYER_Ada");
            player.layer = playerLayer;
            player.transform.position = spawn;
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
            body.GetComponent<Renderer>().sharedMaterial = bodyMaterial;

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
            player.AddComponent<Nightborn.Audio.NightbornAudio>();
            var potion = player.AddComponent<Nightborn.Player.DimPotion>();
            var soPotion = new SerializedObject(potion);
            soPotion.FindProperty("glow").objectReferenceValue = glow;
            soPotion.ApplyModifiedPropertiesWithoutUndo();

            // 关卡流程
            var flowGo = new GameObject("LEVEL_Flow");
            var flow = flowGo.AddComponent<Nightborn.Level.LevelFlow>();
            var soFlow = new SerializedObject(flow);
            soFlow.FindProperty("levelId").stringValue = levelId;
            soFlow.FindProperty("levelIndex").intValue = levelIndex;
            soFlow.ApplyModifiedPropertiesWithoutUndo();

            return cam;
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
            var menu = root.AddComponent<Nightborn.Menu.NightbornMenu>();
            var soMenu = new SerializedObject(menu);

            string[] menuScenes =
            {
                "L_Ch01_S01_Street", "L_Ch01_S02_Street", "L_Ch01_S03_Street",
                "L_Ch02_S01_Church", "L_Ch02_S02_Church", "L_Ch02_S03_Church",
                "L_Ch03_S01_Mansion", "L_Ch03_S02_Mansion", "L_Ch03_S03_Mansion",
                "L_Ch04_S01_Cathedral"
            };
            string[] menuLabels =
            {
                "第一章 · 第一夜　街区", "第一章 · 第二夜　街区", "第一章 · 第三夜　街区",
                "第二章 · 第一夜　教堂", "第二章 · 第二夜　教堂", "第二章 · 第三夜　教堂",
                "第三章 · 第一夜　宅邸", "第三章 · 第二夜　宅邸", "第三章 · 第三夜　宅邸",
                "终章 · 主教座堂"
            };

            var scenesProp = soMenu.FindProperty("levelScenes");
            scenesProp.arraySize = menuScenes.Length;
            for (int i = 0; i < menuScenes.Length; i++)
                scenesProp.GetArrayElementAtIndex(i).stringValue = menuScenes[i];

            string[] menuIds =
            {
                "Ch01_S01", "Ch01_S02", "Ch01_S03",
                "Ch02_S01", "Ch02_S02", "Ch02_S03",
                "Ch03_S01", "Ch03_S02", "Ch03_S03",
                "Ch04_S01"
            };
            var idsProp = soMenu.FindProperty("levelIds");
            idsProp.arraySize = menuIds.Length;
            for (int i = 0; i < menuIds.Length; i++)
                idsProp.GetArrayElementAtIndex(i).stringValue = menuIds[i];

            var labelsProp = soMenu.FindProperty("levelLabels");
            labelsProp.arraySize = menuLabels.Length;
            for (int i = 0; i < menuLabels.Length; i++)
                labelsProp.GetArrayElementAtIndex(i).stringValue = menuLabels[i];

            soMenu.ApplyModifiedPropertiesWithoutUndo();

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
