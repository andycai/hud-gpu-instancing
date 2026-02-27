// ============================================================================
// HUDSystem.cs
// GPU HUD 系统入口：初始化所有子模块，LateUpdate 调度更新与渲染
// ============================================================================

using UnityEngine;

namespace GPUHud
{
    /// <summary>
    /// GPU HUD 系统入口
    /// 整合 HUDDataStore、HUDRenderer、HUDAnimator、HUDAtlasManager
    /// 在 LateUpdate 中完成数据上传和渲染
    /// </summary>
    [RequireComponent(typeof(HUDAtlasManager))]
    public class HUDSystem : MonoBehaviour
    {
        [Header("Shader 引用")]
        [SerializeField] private Shader _hudShader;

        [Header("调试")]
        [SerializeField] private bool _showDebugInfo = true;

        // === 子模块 ===
        private HUDDataStore _dataStore;
        private HUDRenderer _renderer;
        private HUDAnimator _animator;
        private HUDAtlasManager _atlasManager;
        private SDFCharLookup _charLookup;

        // === 状态 ===
        private bool _initialized;
        private int _unitCount;
        private int _regularInstanceCount;  // 常规 Instance 数（不含飘血）
        private int _totalInstanceCount;    // 总 Instance 数（含飘血）

        /// <summary>当前注册的单位数量</summary>
        public int UnitCount => _unitCount;

        /// <summary>当前总 Instance 数量</summary>
        public int TotalInstanceCount => _totalInstanceCount;

        /// <summary>数据存储引用（供外部系统更新数据）</summary>
        public HUDDataStore DataStore => _dataStore;

        /// <summary>动画管理器引用（供外部系统触发飘血）</summary>
        public HUDAnimator Animator => _animator;

        /// <summary>Atlas 管理器引用（供外部系统获取头像 Slice）</summary>
        public HUDAtlasManager AtlasManager => _atlasManager;

        // ====================================================================
        // 生命周期
        // ====================================================================

        private void Awake()
        {
            Initialize();
        }

        private void LateUpdate()
        {
            if (!_initialized) return;

            // 1. 处理头像加载队列
            _atlasManager.ProcessLoadQueue();

            // 2. 上传脏数据到 GPU
            _dataStore.UploadDirtyChunks();

            // 3. 执行渲染（唯一的 DrawCall）
            _renderer.Render(_totalInstanceCount);
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        // ====================================================================
        // 初始化
        // ====================================================================

        private void Initialize()
        {
            if (_initialized) return;

            // 查找 Shader
            if (_hudShader == null)
                _hudShader = Shader.Find("HUD/GPUInstanced");

            if (_hudShader == null)
            {
                Debug.LogError("[HUDSystem] 找不到 HUD/GPUInstanced Shader！");
                return;
            }

            // 初始化 Atlas 管理器
            _atlasManager = GetComponent<HUDAtlasManager>();
            _atlasManager.Initialize();

            // 初始化 SDF 字符查表（使用默认数字字符）
            // Atlas 布局：数字 0-9 绘制在 Atlas 顶部（y=960~1024 of 1024px）
            // UV 区域：x=0, y=960/1024=0.9375, 每字符宽=64/1024=0.0625
            // 12 个字符总宽=12*0.0625=0.75, 高=64/1024=0.0625
            _charLookup = new SDFCharLookup();
            _charLookup.InitializeWithDefaults(new Vector4(0f, 0.9375f, 0.75f, 0.0625f));

            // 初始化数据存储
            _dataStore = new HUDDataStore();
            _dataStore.Initialize();

            // 初始化渲染器
            _renderer = new HUDRenderer();
            _renderer.Initialize(_hudShader);
            _renderer.BindBuffers(_dataStore.InstanceBuffer, _dataStore.FloatTextBuffer);
            _renderer.BindTextures(_atlasManager.MainAtlas, _atlasManager.AvatarArray);

            // 初始化动画管理器（飘血 Instance 位于常规 Instance 之后）
            _animator = new HUDAnimator(_dataStore, _charLookup, 0);

            _unitCount = 0;
            _regularInstanceCount = 0;
            _totalInstanceCount = 0;
            _initialized = true;

            Debug.Log("[HUDSystem] 初始化完成");
        }

        // ====================================================================
        // 单位管理 API
        // ====================================================================

        /// <summary>
        /// 注册一个单位的 HUD
        /// </summary>
        /// <param name="worldPosition">世界坐标</param>
        /// <param name="unitName">单位名字</param>
        /// <param name="healthPercent">血量百分比 (0~1)</param>
        /// <param name="avatarUID">头像 UID（用于加载头像）</param>
        /// <returns>单位索引（用于后续更新）</returns>
        public int RegisterUnit(Vector3 worldPosition, string unitName, float healthPercent, int avatarUID = 0)
        {
            if (_unitCount >= HUDConstants.MaxUnits)
            {
                Debug.LogWarning("[HUDSystem] 单位数已达上限！");
                return -1;
            }

            int unitIndex = _unitCount;
            int baseInstance = unitIndex * HUDConstants.InstancesPerUnit;
            int instanceIdx = baseInstance;

            // --- 1. 头像 ---
            int avatarSlice = _atlasManager.GetAvatarSlice(avatarUID > 0 ? avatarUID : unitIndex);
            var avatar = HUDInstanceData.Empty;
            avatar.worldPosition = worldPosition;
            avatar.screenOffsetX = -HUDConstants.HealthBarWidth * 0.5f - HUDConstants.AvatarDisplaySize * 0.5f - 4f;
            avatar.screenOffsetY = HUDConstants.AvatarOffsetY;
            avatar.size = new Vector2(HUDConstants.AvatarDisplaySize, HUDConstants.AvatarDisplaySize);
            avatar.uvRect = new Vector4(0, 0, 1, 1); // 整个 Slice
            avatar.color = Color.white;
            avatar.SetType(HUDElementType.Avatar);
            avatar.SetVisible(true);
            avatar.SetAvatarSlice(avatarSlice);
            _dataStore.SetInstanceData(instanceIdx++, avatar);

            // --- 2. 血条背景 ---
            var hpBg = HUDInstanceData.Empty;
            hpBg.worldPosition = worldPosition;
            hpBg.screenOffsetX = 0;
            hpBg.screenOffsetY = HUDConstants.HealthBarOffsetY;
            hpBg.size = new Vector2(HUDConstants.HealthBarWidth + 2, HUDConstants.HealthBarHeight + 2);
            hpBg.uvRect = new Vector4(0, 0, 0.01f, 0.01f); // Atlas 角落小像素
            hpBg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            hpBg.SetType(HUDElementType.Icon); // 用 Icon 类型采样纯色
            hpBg.SetVisible(true);
            _dataStore.SetInstanceData(instanceIdx++, hpBg);

            // --- 3. 血条前景 ---
            var hpFg = HUDInstanceData.Empty;
            hpFg.worldPosition = worldPosition;
            hpFg.screenOffsetX = 0;
            hpFg.screenOffsetY = HUDConstants.HealthBarOffsetY;
            hpFg.size = new Vector2(HUDConstants.HealthBarWidth, HUDConstants.HealthBarHeight);
            hpFg.uvRect = new Vector4(0, 0, 0.01f, 0.01f);
            // 血条颜色：根据血量百分比从绿到黄到红渐变
            hpFg.color = GetHealthColor(healthPercent);
            hpFg.SetType(HUDElementType.HealthBar);
            hpFg.SetVisible(true);
            _dataStore.SetInstanceData(instanceIdx++, hpFg);

            // --- 4. 名字文字（每字一个 Instance） ---
            int nameLen = unitName != null ? Mathf.Min(unitName.Length, HUDConstants.MaxNameLength) : 0;
            float nameWidth = nameLen * HUDConstants.NameCharSize * 0.6f;
            float nameStartX = -nameWidth * 0.5f;

            for (int i = 0; i < HUDConstants.MaxNameLength; i++)
            {
                var charInst = HUDInstanceData.Empty;
                if (i < nameLen)
                {
                    char c = unitName[i];
                    Vector4 charUV = _charLookup.GetCharUV(c);

                    charInst.worldPosition = worldPosition;
                    charInst.screenOffsetX = nameStartX + i * HUDConstants.NameCharSize * 0.6f;
                    charInst.screenOffsetY = HUDConstants.NameOffsetY;
                    charInst.size = new Vector2(HUDConstants.NameCharSize, HUDConstants.NameCharSize);
                    charInst.uvRect = charUV;
                    charInst.color = Color.white;
                    charInst.SetType(HUDElementType.Text);
                    charInst.SetVisible(true);
                }
                _dataStore.SetInstanceData(instanceIdx++, charInst);
            }

            // --- 5. 图标（兵种 + Buff，预留 3 个槽位） ---
            for (int i = 0; i < 3; i++)
            {
                var icon = HUDInstanceData.Empty;
                if (i == 0) // 第一个图标默认可见（兵种图标）
                {
                    icon.worldPosition = worldPosition;
                    icon.screenOffsetX = HUDConstants.HealthBarWidth * 0.5f + HUDConstants.IconDisplaySize * 0.5f + 4f;
                    icon.screenOffsetY = HUDConstants.IconOffsetY;
                    icon.size = new Vector2(HUDConstants.IconDisplaySize, HUDConstants.IconDisplaySize);
                    icon.uvRect = new Vector4(0, 0, 0.05f, 0.05f); // Atlas 中图标区域
                    icon.color = Color.white;
                    icon.SetType(HUDElementType.Icon);
                    icon.SetVisible(true);
                }
                _dataStore.SetInstanceData(instanceIdx++, icon);
            }

            _unitCount++;
            _regularInstanceCount = _unitCount * HUDConstants.InstancesPerUnit;

            // 飘血 Instance 紧接在常规 Instance 之后
            int floatTextStart = _regularInstanceCount;
            _animator.SetFloatTextInstanceStart(floatTextStart);

            // 总 Instance 数 = 常规 + 飘血池
            _totalInstanceCount = _regularInstanceCount
                + HUDConstants.FloatTextPoolSize * HUDConstants.FloatTextMaxDigits;
            _dataStore.SetActiveInstanceCount(_totalInstanceCount);

            // 标记所在 Chunk 为脏
            _dataStore.MarkUnitDirty(unitIndex);

            return unitIndex;
        }

        /// <summary>
        /// 批量注册单位（适用于初始化场景）
        /// </summary>
        public void RegisterUnits(Vector3[] positions, int count)
        {
            for (int i = 0; i < count && _unitCount < HUDConstants.MaxUnits; i++)
            {
                // 使用简单的默认名称（避免 string 分配，实际项目从数据表读取）
                RegisterUnit(positions[i], "1234", 1f, i);
            }

            // 批量完成后全量上传一次
            _dataStore.MarkAllDirty();
        }

        // ====================================================================
        // 数据更新 API
        // ====================================================================

        /// <summary>
        /// 更新单位位置
        /// </summary>
        public void UpdateUnitPosition(int unitIndex, Vector3 newPosition)
        {
            if (unitIndex < 0 || unitIndex >= _unitCount) return;

            int baseInstance = unitIndex * HUDConstants.InstancesPerUnit;
            for (int i = 0; i < HUDConstants.InstancesPerUnit; i++)
            {
                var data = _dataStore.GetInstanceData(baseInstance + i);
                data.worldPosition = newPosition;
                _dataStore.SetInstanceData(baseInstance + i, data);
            }

            _dataStore.MarkUnitDirty(unitIndex);
        }

        /// <summary>
        /// 更新单位血量
        /// </summary>
        public void UpdateUnitHealth(int unitIndex, float healthPercent)
        {
            if (unitIndex < 0 || unitIndex >= _unitCount) return;

            int baseInstance = unitIndex * HUDConstants.InstancesPerUnit;
            // 血条前景是第 3 个 Instance（index=2）
            int hpIndex = baseInstance + 2;
            var data = _dataStore.GetInstanceData(hpIndex);
            data.color = GetHealthColor(healthPercent);
            _dataStore.SetInstanceData(hpIndex, data);

            _dataStore.MarkUnitDirty(unitIndex);
        }

        /// <summary>
        /// 触发飘血
        /// </summary>
        public void SpawnFloatingText(Vector3 worldPos, float value, FloatTextStyle style)
        {
            _animator.SpawnFloatingText(worldPos, value, style);
        }

        // ====================================================================
        // 工具函数
        // ====================================================================

        /// <summary>
        /// 根据血量百分比计算颜色（绿 → 黄 → 红）
        /// 血量百分比编码在 color.a 中，供 Shader 做宽度裁剪
        /// </summary>
        private static Color GetHealthColor(float percent)
        {
            percent = Mathf.Clamp01(percent);

            Color c;
            if (percent > 0.5f)
            {
                // 绿 → 黄
                float t = (percent - 0.5f) * 2f;
                c = Color.Lerp(new Color(1f, 0.9f, 0.1f), new Color(0.2f, 0.9f, 0.2f), t);
            }
            else
            {
                // 黄 → 红
                float t = percent * 2f;
                c = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(1f, 0.9f, 0.1f), t);
            }

            // 将血量百分比编码到 alpha 通道，供 Shader 做裁剪
            c.a = percent;
            return c;
        }

        // ====================================================================
        // 清理
        // ====================================================================

        private void Cleanup()
        {
            _renderer?.Dispose();
            _dataStore?.Dispose();
            _charLookup?.Dispose();

            _renderer = null;
            _dataStore = null;
            _animator = null;
            _charLookup = null;
            _initialized = false;

            Debug.Log("[HUDSystem] 资源已释放");
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器下显示调试信息
        /// </summary>
        private void OnGUI()
        {
            if (!_showDebugInfo || !_initialized) return;

            GUILayout.BeginArea(new Rect(10, 10, 300, 120));
            GUILayout.BeginVertical("box");
            GUILayout.Label($"GPU HUD System");
            GUILayout.Label($"单位数: {_unitCount}");
            GUILayout.Label($"Instance 数: {_totalInstanceCount}");
            GUILayout.Label($"DrawCall: 1");
            GUILayout.Label($"FPS: {(1f / Time.smoothDeltaTime):F0}");
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
#endif
    }
}
