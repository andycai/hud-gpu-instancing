// ============================================================================
// HUDSetupWizard.cs
// 一键生成 HUD 测试所需的所有资源（场景、纹理、材质）
// 菜单路径: HUD Tools / 一键搭建测试环境
// ============================================================================

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;

namespace GPUHud.Editor
{
    /// <summary>
    /// HUD 系统一键搭建向导
    /// 自动创建：测试场景、占位 Atlas、占位头像、Material、挂载脚本
    /// </summary>
    public static class HUDSetupWizard
    {
        // === 路径常量 ===
        private const string ArtPath = "Assets/_Project/Art/HUD";
        private const string MaterialPath = "Assets/_Project/Art/HUD/Materials";
        private const string ScenePath = "Assets/_Project/Scenes";
        private const string ShaderName = "HUD/GPUInstanced";

        // ====================================================================
        // 主入口：一键搭建
        // ====================================================================

        [MenuItem("HUD Tools/一键搭建测试环境", false, 1)]
        public static void SetupTestEnvironment()
        {
            Debug.Log("[HUD Setup] 开始搭建测试环境...");

            // 1. 创建目录
            EnsureDirectories();

            // 2. 生成 Atlas 纹理
            var mainAtlas = GenerateMainAtlas();

            // 3. 生成测试头像纹理
            GenerateTestAvatars();

            // 4. 创建 Material
            var hudMaterial = CreateHUDMaterial(mainAtlas);

            // 5. 创建测试场景
            CreateTestScene(mainAtlas);

            Debug.Log("[HUD Setup] ✅ 测试环境搭建完成！点击 Play 按钮运行测试。");
        }

        // ====================================================================
        // 单独的菜单项
        // ====================================================================

        [MenuItem("HUD Tools/生成 Atlas 纹理", false, 20)]
        public static void MenuGenerateAtlas()
        {
            EnsureDirectories();
            GenerateMainAtlas();
            Debug.Log("[HUD Setup] Atlas 纹理已生成");
        }

        [MenuItem("HUD Tools/创建测试场景", false, 21)]
        public static void MenuCreateScene()
        {
            EnsureDirectories();
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtPath}/HUDAtlas.png");
            if (atlas == null)
            {
                atlas = GenerateMainAtlas();
            }
            CreateTestScene(atlas);
        }

        // ====================================================================
        // 目录创建
        // ====================================================================

        private static void EnsureDirectories()
        {
            string[] dirs = { ArtPath, MaterialPath, ScenePath };
            foreach (var dir in dirs)
            {
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    // 逐级创建
                    string[] parts = dir.Split('/');
                    string current = parts[0];
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string next = current + "/" + parts[i];
                        if (!AssetDatabase.IsValidFolder(next))
                        {
                            AssetDatabase.CreateFolder(current, parts[i]);
                        }
                        current = next;
                    }
                }
            }
        }

        // ====================================================================
        // 生成主 Atlas（包含数字 SDF 占位、图标占位、血条渐变）
        // ====================================================================

        private static Texture2D GenerateMainAtlas()
        {
            int size = 1024; // 使用 1024 做测试，生产环境换 4096
            var atlas = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "HUDAtlas",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // 清空为透明
            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, 0);

            // ----- 区域 1: 数字字符 0~9（顶部，用于 SDF 文字占位） -----
            // 每个字符 64×64，排列在第一行
            DrawDigitCharacters(pixels, size, 0, size - 64, 64);

            // ----- 区域 2: 图标占位（中部 256×256 区域，带彩色方块） -----
            DrawIconPlaceholders(pixels, size, 0, size - 64 - 256, 64, 8);

            // ----- 区域 3: 血条渐变纹理（底部 256×16） -----
            DrawHealthBarGradient(pixels, size, 0, 0, 256, 16);

            // ----- 区域 4: 左上角 2×2 白色像素（用于纯色采样） -----
            for (int y = size - 4; y < size; y++)
                for (int x = 0; x < 4; x++)
                    pixels[y * size + x] = new Color32(255, 255, 255, 255);

            atlas.SetPixels32(pixels);
            atlas.Apply(false, false); // 保持可读（编辑器下需要）

            // 保存为 PNG
            string path = $"{ArtPath}/HUDAtlas.png";
            File.WriteAllBytes(Path.Combine(Application.dataPath, "../", path),
                               atlas.EncodeToPNG());

            AssetDatabase.Refresh();

            // 设置导入参数
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }

            var loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Debug.Log($"[HUD Setup] 主 Atlas 已生成: {path}");
            return loaded;
        }

        /// <summary>
        /// 绘制数字字符 0~9（简易位图字体，用于 SDF 占位）
        /// </summary>
        private static void DrawDigitCharacters(Color32[] pixels, int texSize,
                                                 int startX, int startY, int charSize)
        {
            // 简易 5×7 点阵数字模板
            string[,] digitPatterns = new string[10, 7]
            {
                { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },  // 0
                { "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },  // 1
                { " ### ", "#   #", "    #", "  ## ", " #   ", "#    ", "#####" },  // 2
                { " ### ", "#   #", "    #", "  ## ", "    #", "#   #", " ### " },  // 3
                { "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # " },  // 4
                { "#####", "#    ", "#### ", "    #", "    #", "#   #", " ### " },  // 5
                { " ### ", "#    ", "#### ", "#   #", "#   #", "#   #", " ### " },  // 6
                { "#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   " },  // 7
                { " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### " },  // 8
                { " ### ", "#   #", "#   #", " ####", "    #", "    #", " ### " },  // 9
            };

            int scale = charSize / 8; // 每个点阵像素的放大倍数
            Color32 white = new Color32(255, 255, 255, 255);
            Color32 transparent = new Color32(0, 0, 0, 0);

            for (int digit = 0; digit < 10; digit++)
            {
                int baseX = startX + digit * charSize;

                for (int rowIdx = 0; rowIdx < 7; rowIdx++)
                {
                    string row = digitPatterns[digit, rowIdx];
                    for (int colIdx = 0; colIdx < 5; colIdx++)
                    {
                        bool filled = colIdx < row.Length && row[colIdx] == '#';
                        Color32 c = filled ? white : transparent;

                        // 放大绘制
                        for (int sy = 0; sy < scale; sy++)
                        {
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px = baseX + (colIdx + 1) * scale + sx; // +1 留边距
                                int py = startY + (6 - rowIdx + 1) * scale + sy; // 翻转 Y，+1 留边距

                                if (px >= 0 && px < texSize && py >= 0 && py < texSize)
                                {
                                    // 对于 SDF 效果，给填充区域设置 alpha=1，非填充 alpha=0
                                    pixels[py * texSize + px] = c;
                                }
                            }
                        }
                    }
                }
            }

            Debug.Log($"[HUD Setup] 数字字符 0~9 已绘制（起始位置 {startX},{startY}，字符大小 {charSize}）");
        }

        /// <summary>
        /// 绘制图标占位方块（不同颜色表示不同图标）
        /// </summary>
        private static void DrawIconPlaceholders(Color32[] pixels, int texSize,
                                                  int startX, int startY,
                                                  int iconSize, int count)
        {
            Color32[] iconColors = new Color32[]
            {
                new Color32(220, 50, 50, 255),    // 红-战士
                new Color32(50, 120, 220, 255),   // 蓝-法师
                new Color32(50, 200, 80, 255),    // 绿-弓手
                new Color32(200, 180, 50, 255),   // 黄-骑兵
                new Color32(150, 50, 200, 255),   // 紫-刺客
                new Color32(200, 100, 50, 255),   // 橙-buff攻击
                new Color32(50, 200, 200, 255),   // 青-buff防御
                new Color32(200, 50, 150, 255),   // 粉-buff速度
            };

            for (int i = 0; i < count; i++)
            {
                int bx = startX + (i % 4) * iconSize;
                int by = startY + (i / 4) * iconSize;
                Color32 col = iconColors[i % iconColors.Length];

                for (int y = 0; y < iconSize; y++)
                {
                    for (int x = 0; x < iconSize; x++)
                    {
                        int px = bx + x;
                        int py = by + y;
                        if (px >= 0 && px < texSize && py >= 0 && py < texSize)
                        {
                            // 留 2px 边框
                            bool isBorder = x < 2 || x >= iconSize - 2 || y < 2 || y >= iconSize - 2;
                            pixels[py * texSize + px] = isBorder
                                ? new Color32(255, 255, 255, 255)
                                : col;
                        }
                    }
                }
            }

            Debug.Log($"[HUD Setup] {count} 个图标占位已绘制");
        }

        /// <summary>
        /// 绘制血条渐变纹理（红 → 黄 → 绿）
        /// </summary>
        private static void DrawHealthBarGradient(Color32[] pixels, int texSize,
                                                   int startX, int startY,
                                                   int width, int height)
        {
            for (int x = 0; x < width; x++)
            {
                float t = (float)x / width;
                Color c;
                if (t < 0.5f)
                {
                    c = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(1f, 0.9f, 0.1f), t * 2f);
                }
                else
                {
                    c = Color.Lerp(new Color(1f, 0.9f, 0.1f), new Color(0.2f, 0.9f, 0.2f), (t - 0.5f) * 2f);
                }
                Color32 c32 = c;

                for (int y = 0; y < height; y++)
                {
                    int px = startX + x;
                    int py = startY + y;
                    if (px >= 0 && px < texSize && py >= 0 && py < texSize)
                    {
                        pixels[py * texSize + px] = c32;
                    }
                }
            }

            Debug.Log($"[HUD Setup] 血条渐变纹理已绘制");
        }

        // ====================================================================
        // 生成测试头像
        // ====================================================================

        private static void GenerateTestAvatars()
        {
            // 生成 8 张不同颜色的测试头像
            Color32[] avatarColors = new Color32[]
            {
                new Color32(200, 80, 80, 255),
                new Color32(80, 160, 200, 255),
                new Color32(80, 200, 100, 255),
                new Color32(200, 200, 80, 255),
                new Color32(160, 80, 200, 255),
                new Color32(200, 120, 80, 255),
                new Color32(80, 200, 200, 255),
                new Color32(200, 80, 160, 255),
            };

            int size = 128;
            for (int i = 0; i < avatarColors.Length; i++)
            {
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];
                var bgColor = avatarColors[i];
                var faceColor = new Color32(240, 220, 190, 255); // 肤色

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x - size * 0.5f) / (size * 0.5f);
                        float dy = (y - size * 0.5f) / (size * 0.5f);
                        float dist = dx * dx + dy * dy;

                        if (dist < 0.6f)
                        {
                            // 脸部（椭圆）
                            float faceDx = dx;
                            float faceDy = (dy + 0.1f) * 1.2f;
                            float faceDist = faceDx * faceDx + faceDy * faceDy;
                            pixels[y * size + x] = faceDist < 0.35f ? faceColor : bgColor;
                        }
                        else
                        {
                            pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        }
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply();

                string path = $"{ArtPath}/avatar_test_{i}.png";
                File.WriteAllBytes(
                    Path.Combine(Application.dataPath, "../", path),
                    tex.EncodeToPNG());

                Object.DestroyImmediate(tex);
            }

            AssetDatabase.Refresh();
            Debug.Log($"[HUD Setup] 8 张测试头像已生成");
        }

        // ====================================================================
        // 创建 Material
        // ====================================================================

        private static Material CreateHUDMaterial(Texture2D atlas)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[HUD Setup] 找不到 Shader: {ShaderName}");
                return null;
            }

            var mat = new Material(shader)
            {
                name = "HUD_Material"
            };
            mat.SetTexture("_MainAtlas", atlas);
            mat.SetFloat("_SDFThreshold", 0.5f);
            mat.SetFloat("_SDFSoftness", 0.05f);

            string matPath = $"{MaterialPath}/HUD_Material.mat";
            AssetDatabase.CreateAsset(mat, matPath);
            Debug.Log($"[HUD Setup] Material 已创建: {matPath}");
            return mat;
        }

        // ====================================================================
        // 创建测试场景
        // ====================================================================

        private static void CreateTestScene(Texture2D mainAtlas)
        {
            // 创建新场景
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. 主相机
            var cameraGO = new GameObject("Main Camera");
            var camera = cameraGO.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);
            camera.orthographic = false;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 5000f;
            cameraGO.transform.position = new Vector3(0, 80, -80);
            cameraGO.transform.rotation = Quaternion.Euler(45, 0, 0);
            cameraGO.tag = "MainCamera";

            // 添加飞行相机控制器
            cameraGO.AddComponent<Demo.FreeCameraController>();

            // 添加 URP 相机数据组件（如果 URP 可用）
            var urpCamDataType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (urpCamDataType != null)
            {
                cameraGO.AddComponent(urpCamDataType);
            }

            // 2. 平行光
            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.intensity = 1f;
            lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);

            // 3. 地面（可选，帮助辨识方向）
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(50, 1, 50);
            ground.transform.position = Vector3.zero;
            var groundRenderer = ground.GetComponent<Renderer>();
            if (groundRenderer != null)
            {
                var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                groundMat.color = new Color(0.2f, 0.25f, 0.2f);
                groundRenderer.sharedMaterial = groundMat;
            }

            // 4. HUD System 对象
            var hudGO = new GameObject("HUDSystem");

            // 添加 HUDAtlasManager（HUDSystem 的 RequireComponent 会自动添加，但我们先添加以设置 Atlas 引用）
            var atlasManager = hudGO.AddComponent<HUDAtlasManager>();

            // 设置 Atlas 引用
            var atlasField = typeof(HUDAtlasManager).GetField("_mainAtlas",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (atlasField != null && mainAtlas != null)
            {
                atlasField.SetValue(atlasManager, mainAtlas);
            }

            // 添加 HUDSystem
            var hudSystem = hudGO.AddComponent<HUDSystem>();

            // 设置 Shader 引用
            var shaderField = typeof(HUDSystem).GetField("_hudShader",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var shader = Shader.Find(ShaderName);
            if (shaderField != null && shader != null)
            {
                shaderField.SetValue(hudSystem, shader);
            }

            // 添加测试场景脚本
            var testScene = hudGO.AddComponent<Demo.HUDTestScene>();

            // 设置较少的测试单位数（先从 1000 开始确认功能，然后可以调到 10000）
            var unitCountField = typeof(Demo.HUDTestScene).GetField("_unitCount",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (unitCountField != null)
            {
                unitCountField.SetValue(testScene, 1000);
            }

            // 5. 简单的相机控制提示
            var infoGO = new GameObject("_Info");
            infoGO.transform.SetParent(hudGO.transform);

            // 6. 保存场景
            string scenePath = $"{ScenePath}/HUDTestScene.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"[HUD Setup] 测试场景已创建: {scenePath}");

            // 标记场景为已修改
            EditorSceneManager.MarkSceneDirty(scene);

            // 选中 HUD 对象
            Selection.activeGameObject = hudGO;

            EditorUtility.DisplayDialog(
                "HUD 测试环境已就绪",
                "✅ 所有资源已创建完成！\n\n" +
                "已创建：\n" +
                "• HUDAtlas.png (1024×1024 占位 Atlas)\n" +
                "• 8 张测试头像\n" +
                "• HUD_Material.mat\n" +
                "• HUDTestScene.unity\n\n" +
                "点击 Unity Play 按钮开始测试！\n" +
                "默认 1000 个单位，确认无误后在 Inspector 中改为 10000",
                "OK");
        }
    }
}
#endif
