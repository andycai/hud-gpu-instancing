# SLG 大世界高性能 GPU HUD 系统设计方案

> **项目**: Unity 2022.3 LTS + URP  
> **目标**: 10,000 单位同屏 HUD，1 DrawCall，0 GC，稳定 60 FPS  
> **日期**: 2026-02-27  
> **作者**: Andy  

---

## 1. 需求概述

### 1.1 HUD 元素

| # | 元素 | 描述 | 数据来源 |
|---|------|------|---------|
| 1 | **Avatar（头像）** | 玩家自定义头像，128×128 | CDN 动态加载 |
| 2 | **Icons（图标）** | 技能、Buff、兵种图标 | 系统预设（静态 Atlas） |
| 3 | **血条** | 单条血条，颜色渐变表示血量 | 实时计算 |
| 4 | **名字** | 玩家名字文本 | 字符串数据 |
| 5 | **飘血** | 伤害/治疗跳字，颜色+缩放+位移动画 | 战斗事件触发 |

### 1.2 性能目标

| 指标 | 目标值 |
|------|--------|
| 单位数 | 10,000 |
| DrawCall | 1 |
| GC 分配 | 0 B/帧（运行时） |
| 帧率 | 稳定 60 FPS |

---

## 2. 技术决策汇总

| # | 决策点 | 选择 | 理由 |
|---|--------|------|------|
| 1 | 文本渲染 | **SDF Font Atlas** | 缩放无损、可合并到统一 Atlas、0 GC |
| 2 | 渲染架构 | **`Graphics.DrawMeshInstancedIndirect`** | Unity 2022.3 原生支持、真正 1 DC、StructuredBuffer 灵活 |
| 3 | 纹理组织 | **混合方案（Atlas + Texture2DArray）** | 静态资源用 Atlas，动态头像用 Texture2DArray 支持 LRU 替换 |
| 4 | 数据更新 | **脏标记 + 分块 ComputeBuffer 更新** | SLG 大部分单位静止，减少无效传输 |
| 5 | 元素布局 | **每个元素一个 Instance** | 灵活度高、GPU 150K Instance 无压力 |
| 6 | 飘血管理 | **预分配全局池 + 环形缓冲区** | 0 GC、固定内存、逻辑简单 |

---

## 3. 系统总体架构

```
┌──────────────────────────────────────────────────────────┐
│                    HUDSystem (MonoBehaviour)              │
│  系统入口，负责初始化、每帧调度、资源生命周期管理           │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────┐  │
│  │ HUDDataStore │  │ HUDRenderer  │  │ HUDAnimator    │  │
│  │              │  │              │  │                │  │
│  │ ·NativeArray │  │ ·Material    │  │ ·飘血环形缓冲  │  │
│  │ ·脏标记管理  │  │ ·ComputeBuffer│ │ ·GPU时间驱动   │  │
│  │ ·分块更新    │  │ ·DrawCall    │  │ ·动画参数      │  │
│  └──────┬───────┘  └──────┬───────┘  └───────┬────────┘  │
│         │                 │                  │           │
│  ┌──────┴─────────────────┴──────────────────┴────────┐  │
│  │              HUDAtlasManager                       │  │
│  │  ·静态 Atlas（图标/SDF字体/血条）                    │  │
│  │  ·动态 Texture2DArray（头像，LRU 缓存）              │  │
│  │  ·CDN 头像异步加载 → 写入 Array Slice               │  │
│  └────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────┘
```

### 3.1 模块职责

- **HUDSystem**（入口）：初始化所有子模块，在 `LateUpdate` 中调度更新与渲染
- **HUDDataStore**：CPU 端数据管理，维护 `NativeArray<HUDInstanceData>`，按 Chunk 管理脏标记
- **HUDRenderer**：持有 Material 和 ComputeBuffer，执行唯一的 `DrawMeshInstancedIndirect` 调用
- **HUDAnimator**：管理飘血环形缓冲区，将动画参数传入 Shader
- **HUDAtlasManager**：管理静态 Atlas 和动态头像 Texture2DArray，实现 LRU 缓存

---

## 4. 核心数据结构

### 4.1 HUD Instance 数据（GPU 端，64 字节对齐）

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct HUDInstanceData  // 64 bytes
{
    public Vector3 worldPosition;   // 12B  所属单位的世界坐标
    public float   screenOffsetY;   //  4B  该元素相对 HUD 锚点的 Y 偏移（像素）
    public float   screenOffsetX;   //  4B  该元素相对 HUD 锚点的 X 偏移（像素）
    public Vector2 size;            //  8B  该元素的屏幕尺寸（像素）
    public Vector4 uvRect;          // 16B  在 Atlas 中的 UV 区域 (x,y,w,h)
    public Color   color;           // 16B  颜色/透明度
    public uint    flags;           //  4B  位标记（见下方）
}
```

### 4.2 元素类型标记（flags 位域）

```csharp
[Flags]
public enum HUDElementType : uint
{
    Avatar    = 0,  // 头像 → 采样 Texture2DArray
    Icon      = 1,  // 图标 → 采样 Atlas
    HealthBar = 2,  // 血条 → 采样 Atlas + 宽度裁剪
    Text      = 3,  // 文字 → 采样 SDF Atlas
    FloatText = 4,  // 飘血 → 采样 SDF Atlas + 动画
}

// flags 位域布局:
// [31..12] 预留
// [11..4]  avatarSliceIndex (8bit, 0~255)
// [3]      visible (1bit)
// [2..0]   elementType (3bit)
```

### 4.3 飘血专用数据

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct FloatingTextData  // 16 bytes
{
    public float  startTime;      // 触发时间（_Time.y）
    public float  duration;       // 持续时间（默认 0.8s）
    public float  value;          // 数值（用于选字符 UV）
    public uint   styleFlags;     // 颜色类型(2bit) + 暴击(1bit) + 预留
}
```

### 4.4 每单位 Instance 分布（约 15 个）

```
Unit HUD Instance 布局：
├── 头像 ×1           → 采样 AvatarArray，圆形裁剪
├── 血条背景 ×1       → 纯色/Atlas UV
├── 血条前景 ×1       → 颜色渐变 + 宽度裁剪（按血量百分比）
├── 名字 ×8~10        → 每字一个 Instance，采样 SDF Atlas
├── 图标 ×2~3         → 采样 Atlas（兵种 + Buff）
└── 飘血 ×1（动态）   → 环形缓冲区管理，GPU 端动画
```

### 4.5 Chunk 管理

```csharp
// 10,000 单位 × 16 Instance/单位 = 160,000 实例
// 160,000 / 256 = 625 个 Chunk
// 每帧只上传标脏的 Chunk
public struct ChunkDirtyState
{
    public bool isDirty;
    public int  startIndex;
    public int  count;
}
```

---

## 5. Shader 设计

### 5.1 Shader 属性与渲染状态

```hlsl
Shader "HUD/GPUInstanced"
{
    Properties
    {
        _MainAtlas ("Main Atlas (Icons/SDF/HealthBar)", 2D) = "white" {}
        _AvatarArray ("Avatar Array", 2DArray) = "" {}
        _SDFThreshold ("SDF Threshold", Range(0, 1)) = 0.5
        _SDFSoftness ("SDF Softness", Range(0, 0.2)) = 0.05
        _FloatRiseHeight ("Float Text Rise Height", Float) = 0.05
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always      // HUD 始终在最前
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5
            ENDHLSL
        }
    }
}
```

### 5.2 Vertex Shader：世界坐标 → 屏幕空间 Billboard

```hlsl
StructuredBuffer<HUDInstanceData> _HUDBuffer;
StructuredBuffer<FloatingTextData> _FloatBuffer;
uint _FloatBufferOffset;  // 飘血 Instance 在 _HUDBuffer 中的起始偏移

v2f vert(appdata v, uint instanceID : SV_InstanceID)
{
    HUDInstanceData data = _HUDBuffer[instanceID];

    // 1. 世界坐标 → 裁剪空间
    float4 clipPos = mul(UNITY_MATRIX_VP, float4(data.worldPosition, 1.0));

    // 2. GPU 视锥剔除：超出裁剪空间 → 退化为零面积
    float cullMask = step(-clipPos.w, clipPos.x) * step(clipPos.x, clipPos.w)
                   * step(-clipPos.w, clipPos.y) * step(clipPos.y, clipPos.w)
                   * step(0, clipPos.w);
    uint visible = (data.flags >> 3) & 1u;
    cullMask *= visible;

    // 3. 透视除法 + 像素偏移
    float2 ndcPos = clipPos.xy / clipPos.w;
    float2 pixelOffset = float2(data.screenOffsetX, data.screenOffsetY)
                       * float2(2.0 / _ScreenParams.x, 2.0 / _ScreenParams.y);
    ndcPos += pixelOffset;

    // 4. Quad 顶点展开
    float2 quadSize = data.size
                    * float2(2.0 / _ScreenParams.x, 2.0 / _ScreenParams.y);
    float2 centerNDC = ndcPos;
    ndcPos += v.vertex.xy * quadSize * cullMask;

    // 5. 飘血动画（仅 FloatText 类型）
    uint elemType = data.flags & 7u;
    if (elemType == 4u)
    {
        FloatingTextData ft = _FloatBuffer[instanceID - _FloatBufferOffset];
        float t = saturate((_Time.y - ft.startTime) / ft.duration);
        ndcPos.y += t * _FloatRiseHeight;
        float bounceScale = 1.0 + 0.3 * sin(t * 3.14159);
        ndcPos = lerp(centerNDC, ndcPos, bounceScale);
    }

    v2f o;
    o.clipPos = float4(ndcPos * clipPos.w, clipPos.z, clipPos.w);
    o.uv = v.uv * data.uvRect.zw + data.uvRect.xy;
    o.color = data.color;
    o.flags = data.flags;
    o.localU = v.uv.x;  // 用于血条裁剪
    return o;
}
```

### 5.3 Fragment Shader：按类型分支采样

```hlsl
float4 frag(v2f i) : SV_Target
{
    uint elemType = i.flags & 7u;
    float4 col;

    if (elemType == 0u) // === Avatar ===
    {
        uint sliceIndex = (i.flags >> 4) & 0xFFu;
        col = SAMPLE_TEXTURE2D_ARRAY(_AvatarArray, sampler_AvatarArray,
                                     i.uv, sliceIndex);
        // 圆形裁剪
        float2 center = i.uv - 0.5;
        col.a *= step(dot(center, center), 0.25);
    }
    else if (elemType == 3u || elemType == 4u) // === Text / FloatText ===
    {
        // SDF 采样
        float dist = SAMPLE_TEXTURE2D(_MainAtlas, sampler_MainAtlas, i.uv).a;
        float alpha = smoothstep(_SDFThreshold - _SDFSoftness,
                                 _SDFThreshold + _SDFSoftness, dist);
        col = float4(i.color.rgb, alpha * i.color.a);

        // 飘血淡出（最后 30% 时间）
        if (elemType == 4u)
        {
            FloatingTextData ft = _FloatBuffer[instanceID - _FloatBufferOffset];
            float t = saturate((_Time.y - ft.startTime) / ft.duration);
            float fadeOut = 1.0 - saturate((t - 0.7) / 0.3);
            col.a *= fadeOut;
        }
    }
    else // === Icon (1) / HealthBar (2) ===
    {
        col = SAMPLE_TEXTURE2D(_MainAtlas, sampler_MainAtlas, i.uv) * i.color;

        // 血条裁剪：localU > healthPercent 的部分透明
        if (elemType == 2u)
        {
            // healthPercent 编码在 color.a 的高位，或单独通过 uvRect.w 传递
            // 这里简化为用 color.a 作为血量百分比
            col.a *= step(i.localU, i.color.a);
            // 实际颜色通过 Atlas 渐变纹理获取（红→黄→绿）
        }
    }

    return col;
}
```

### 5.4 Shader 关键设计点

- **GPU 视锥剔除**：不可见 Instance 退化零面积，Fragment Shader 零开销
- **分支策略**：使用 `if` 条件分支而非 `#ifdef`（同一 DC 内类型混合）；按类型排序 Instance 减少 warp 发散
- **SDF 抗锯齿**：`smoothstep` 实现平滑边缘，质量接近 TextMeshPro
- **所有动画 GPU 端**：飘血的位移、缩放、淡出全部用 `_Time.y` 驱动

---

## 6. CPU 端管理逻辑

### 6.1 HUDSystem 主循环

```csharp
public class HUDSystem : MonoBehaviour
{
    // === 预分配资源（整个生命周期不释放） ===
    private NativeArray<HUDInstanceData> _instanceData;
    private NativeArray<FloatingTextData> _floatTextData;
    private ComputeBuffer _instanceBuffer;
    private ComputeBuffer _floatTextBuffer;
    private ComputeBuffer _argsBuffer;
    private bool[] _chunkDirty;

    // === 常量 ===
    private const int MaxUnits          = 10000;
    private const int InstancesPerUnit  = 16;
    private const int MaxInstances      = MaxUnits * InstancesPerUnit; // 160,000
    private const int ChunkSize         = 256;
    private const int ChunkCount        = MaxInstances / ChunkSize + 1; // 626
    private const int FloatTextPoolSize = 512;

    private Mesh _quadMesh;
    private Material _material;
    private Bounds _bounds;
    private int _activeInstanceCount;

    void Awake()
    {
        // 一次性分配所有内存
        _instanceData  = new NativeArray<HUDInstanceData>(MaxInstances, Allocator.Persistent);
        _floatTextData = new NativeArray<FloatingTextData>(FloatTextPoolSize, Allocator.Persistent);

        _instanceBuffer  = new ComputeBuffer(MaxInstances, Marshal.SizeOf<HUDInstanceData>());
        _floatTextBuffer = new ComputeBuffer(FloatTextPoolSize, Marshal.SizeOf<FloatingTextData>());
        _argsBuffer      = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);

        _chunkDirty = new bool[ChunkCount];
        _bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);

        InitQuadMesh();
        InitMaterial();
    }

    void LateUpdate()
    {
        // 步骤 1：上传脏 Chunk
        UploadDirtyChunks();

        // 步骤 2：更新 IndirectArgs（顶点数, 实例数, ...）
        uint[] args = { 4u, (uint)_activeInstanceCount, 0u, 0u, 0u };
        _argsBuffer.SetData(args);

        // 步骤 3：绑定并绘制
        _material.SetBuffer("_HUDBuffer", _instanceBuffer);
        _material.SetBuffer("_FloatBuffer", _floatTextBuffer);
        Graphics.DrawMeshInstancedIndirect(_quadMesh, 0, _material, _bounds, _argsBuffer);
    }

    void OnDestroy()
    {
        _instanceData.Dispose();
        _floatTextData.Dispose();
        _instanceBuffer?.Release();
        _floatTextBuffer?.Release();
        _argsBuffer?.Release();
    }
}
```

### 6.2 分块脏更新

```csharp
private void UploadDirtyChunks()
{
    for (int i = 0; i < _chunkDirty.Length; i++)
    {
        if (!_chunkDirty[i]) continue;

        int start = i * ChunkSize;
        int count = Mathf.Min(ChunkSize, _activeInstanceCount - start);
        if (count <= 0) { _chunkDirty[i] = false; continue; }

        // NativeArray → ComputeBuffer，零 GC
        _instanceBuffer.SetData(_instanceData, start, start, count);
        _chunkDirty[i] = false;
    }
}

/// <summary>
/// 外部调用：标记某个单位的 HUD 数据已变化
/// </summary>
public void MarkUnitDirty(int unitIndex)
{
    int instanceStart = unitIndex * InstancesPerUnit;
    int chunkIndex    = instanceStart / ChunkSize;
    _chunkDirty[chunkIndex] = true;

    // 如果该单位跨 Chunk 边界，也标脏下一个
    int instanceEnd   = instanceStart + InstancesPerUnit - 1;
    int endChunkIndex = instanceEnd / ChunkSize;
    if (endChunkIndex != chunkIndex && endChunkIndex < _chunkDirty.Length)
        _chunkDirty[endChunkIndex] = true;
}
```

### 6.3 飘血触发（环形缓冲区）

```csharp
private int _floatTextNextIndex = 0;

/// <summary>
/// 触发飘血效果，0 GC
/// </summary>
public void SpawnFloatingText(Vector3 worldPos, float value, FloatTextStyle style)
{
    int idx = _floatTextNextIndex;
    _floatTextNextIndex = (idx + 1) % FloatTextPoolSize;

    _floatTextData[idx] = new FloatingTextData
    {
        startTime  = Time.time,
        duration   = 0.8f,
        value      = value,
        styleFlags = (uint)style
    };

    // 创建对应的字符 Instance（数字拆分为单个字符）
    WriteFloatTextInstances(idx, worldPos, value, style);

    // 精确上传这一条
    _floatTextBuffer.SetData(_floatTextData, idx, idx, 1);
}

public enum FloatTextStyle : uint
{
    Damage  = 0,  // 红色
    Heal    = 1,  // 绿色
    Crit    = 2,  // 橙色 + 大字号
}
```

### 6.4 零 GC 保证清单

| 操作 | GC 分析 |
|------|---------|
| `NativeArray<T>` | Unmanaged 内存，无 GC |
| `ComputeBuffer.SetData(NativeArray, ...)` | 直接内存拷贝，无装箱 |
| `bool[] _chunkDirty` | 初始化一次，不扩容 |
| `Graphics.DrawMeshInstancedIndirect` | Unity 内部验证，无 GC |
| `SpawnFloatingText` | 全 struct 赋值，无堆分配 |
| `uint[] args` | **注意**：这里每帧分配 20 字节，需优化为预分配 |

> **优化点**：`args` 数组应在 `Awake` 中预分配并缓存，`LateUpdate` 中只修改元素值。

---

## 7. 纹理资源管理

### 7.1 静态 Atlas 布局（4096×4096）

```
┌───────────────────────────────────────────┐
│                4096 × 4096                │
│                                           │
│  ┌─────────────────────────────────────┐  │
│  │     SDF Font Atlas (2048×2048)      │  │
│  │  ASCII + 常用中文 3500字 + 数字符号  │  │
│  │  UV 区域: (0, 0.5, 0.5, 0.5)       │  │
│  └─────────────────────────────────────┘  │
│                                           │
│  ┌──────────────┐  ┌──────────────────┐   │
│  │  Icons 区域  │  │  血条渐变纹理    │   │
│  │  512×512     │  │  256×32          │   │
│  │  兵种×20     │  │  红→黄→绿渐变   │   │
│  │  Buff×30     │  │                  │   │
│  │  技能×50     │  │  UV: 采样 X 轴   │   │
│  └──────────────┘  └──────────────────┘   │
│                                           │
│  ┌──────────────────────────────────────┐  │
│  │         预留扩展区域                 │  │
│  └──────────────────────────────────────┘  │
└───────────────────────────────────────────┘
```

### 7.2 头像 Texture2DArray（动态 LRU 缓存）

```csharp
public class HUDAtlasManager : MonoBehaviour
{
    private Texture2DArray _avatarArray;       // 128×128, RGBA32, 256 slices
    private int[] _avatarSlotToUID;            // slot → 玩家 UID
    private Dictionary<int, int> _uidToSlot;   // UID → slot 反查
    private int _lruCursor = 0;

    void Awake()
    {
        _avatarArray = new Texture2DArray(128, 128, 256,
                                          TextureFormat.RGBA32, false);
        _avatarArray.filterMode = FilterMode.Bilinear;
        _avatarArray.wrapMode   = TextureWrapMode.Clamp;

        _avatarSlotToUID = new int[256];
        _uidToSlot       = new Dictionary<int, int>(256);
    }

    /// <summary>
    /// 获取或分配头像 slice，0 GC（Dictionary 预分配容量）
    /// </summary>
    public int GetAvatarSlice(int uid)
    {
        if (_uidToSlot.TryGetValue(uid, out int slot))
            return slot;

        // LRU 淘汰
        slot = _lruCursor;
        _lruCursor = (_lruCursor + 1) % 256;

        int oldUID = _avatarSlotToUID[slot];
        if (oldUID != 0) _uidToSlot.Remove(oldUID);

        _avatarSlotToUID[slot] = uid;
        _uidToSlot[uid] = slot;

        StartCoroutine(LoadAvatarAsync(uid, slot));
        return slot;
    }

    /// <summary>
    /// 异步加载头像，低 GC
    /// </summary>
    private IEnumerator LoadAvatarAsync(int uid, int sliceIndex)
    {
        string url = $"{CDN_BASE_URL}/avatars/{uid}.png";

        using var request = UnityWebRequestTexture.GetTexture(url);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var tex = DownloadHandlerTexture.GetContent(request);
            // GPU 端拷贝，零 GC
            Graphics.CopyTexture(tex, 0, 0, _avatarArray, sliceIndex, 0);
            Destroy(tex);
        }
    }
}
```

### 7.3 SDF 字符映射查表

```csharp
/// <summary>
/// 字符 UV 数据，构建时序列化为排序数组
/// </summary>
public struct CharGlyphInfo
{
    public ushort charCode;
    public float4 uvRect;     // Atlas 中的 UV 区域
    public float  advance;    // 字符宽度（像素）
    public float  offsetY;    // 基线偏移
}

// 运行时二分查找，O(logN)，0 GC
public float4 GetCharUV(char c)
{
    int idx = BinarySearch(_charTable, (ushort)c);
    return idx >= 0 ? _charTable[idx].uvRect : _fallbackUV;
}
```

---

## 8. 飘血动画参数

| 参数 | 值 | 说明 |
|------|-----|------|
| 持续时间 | 0.8s | 从触发到完全消失 |
| Y 轴位移 | +50px | 上浮效果 |
| 缩放曲线 | `1.0 + 0.3 * sin(t * PI)` | 弹跳效果 |
| 淡出 | 后 30% 时间线性淡出 | `t ∈ [0.7, 1.0] → alpha: 1.0 → 0.0` |
| 伤害颜色 | 红色 `(1, 0.2, 0.1)` | — |
| 治疗颜色 | 绿色 `(0.2, 1, 0.3)` | — |
| 暴击颜色 | 橙色 `(1, 0.6, 0.1)` | 字号放大 1.5× |

---

## 9. 性能预算

### 9.1 帧时间预算（16.67ms @ 60 FPS）

| 模块 | 预算 | 预估 | 说明 |
|------|------|------|------|
| CPU 数据更新 | < 2ms | ~0.5ms | 脏 Chunk 更新，最差全量 ~1.2ms |
| CPU 调度 | < 0.5ms | ~0.1ms | 单次 DrawCall + Buffer 绑定 |
| GPU 顶点 | < 3ms | ~1.5ms | 150K Instance × 4 顶点 = 600K 顶点 |
| GPU 片元 | < 3ms | ~2ms | 小面积 Quad，低 overdraw |
| **合计** | < 8.5ms | ~4.1ms | 留足裕量给其他系统 |

### 9.2 内存预算

| 资源 | 大小 | 说明 |
|------|------|------|
| `_instanceData` NativeArray | ~10 MB | 160K × 64B |
| `_instanceBuffer` ComputeBuffer | ~10 MB | GPU 端镜像 |
| `_floatTextData` | ~8 KB | 512 × 16B |
| 静态 Atlas 4096² | ~16 MB | RGBA32（可压缩为 ASTC） |
| 头像 Texture2DArray | ~8 MB | 128² × 256 × RGBA32（可压缩） |
| **合计** | **~44 MB** | 可通过纹理压缩降至 ~25MB |

---

## 10. GPU 端优化措施

| 优化项 | 实现方式 |
|--------|---------|
| **视锥剔除** | Vertex Shader 中检测 clipPos，退化为零面积三角形 |
| **距离 LOD** | 远距离隐藏文字/图标，只保留血条（设置 visible flag） |
| **Instance 排序** | 按类型排序减少 GPU warp 分支发散 |
| **SDF 抗锯齿** | `smoothstep` 平滑边缘 |
| **动画全 GPU** | `_Time.y` 驱动飘血动画，CPU 零开销 |

---

## 11. CPU 端优化措施

| 优化项 | 实现方式 |
|--------|---------|
| **NativeArray** | 替代 managed array，无 GC |
| **脏 Chunk 上传** | 静止单位 0 传输开销 |
| **SDF 查表** | 排序数组 + 二分查找，cache-friendly |
| **名字缓存** | 名字 Instance 变化时才重建字符 UV |
| **头像分帧加载** | 每帧最多加载 2 张，防卡帧 |
| **禁用项** | 无 string 拼接、无 LINQ、无 delegate 捕获、无 List 扩容 |

---

## 12. 可扩展性预留

| 方向 | 扩展方式 |
|------|---------|
| **ComputeShader 剔除** | GPU 端遮挡剔除 → AppendBuffer |
| **LOD 层级** | 远/中/近三级显示内容 |
| **多 DrawCall 分层** | 不同混合模式/RenderQueue 拆分 |
| **名字描边/阴影** | SDF Shader 扩展 outline + shadow |
| **Buff 倒计时** | `_Time.y` 驱动扇形裁剪 |

---

## 13. 开发里程碑

| 阶段 | 里程碑 | 验证指标 | 预估工期 |
|------|--------|---------|---------|
| **M1** | 基础渲染框架 | 1 DC 绘制 10K 彩色 Quad，60 FPS | 2 天 |
| **M2** | 血条 + 图标 | Atlas 采样正确，血条宽度裁剪 | 2 天 |
| **M3** | SDF 文字渲染 | 名字显示清晰，中英文支持 | 3 天 |
| **M4** | 头像系统 | Texture2DArray + LRU 缓存 | 2 天 |
| **M5** | 飘血动画 | 环形缓冲区 + GPU 动画 | 2 天 |
| **M6** | 性能调优 | Profiler 验证 0 GC + 60 FPS | 2 天 |

**总预估工期：约 13 个工作日**

---

## 14. 文件结构规划

```
Assets/_Project/
├── Scripts/
│   └── HUD/
│       ├── Core/
│       │   ├── HUDSystem.cs              # 系统入口
│       │   ├── HUDDataStore.cs           # 数据管理
│       │   ├── HUDRenderer.cs            # 渲染执行
│       │   ├── HUDAnimator.cs            # 动画驱动
│       │   └── HUDAtlasManager.cs        # 纹理管理
│       ├── Data/
│       │   ├── HUDInstanceData.cs        # Instance 数据结构
│       │   ├── FloatingTextData.cs       # 飘血数据结构
│       │   ├── CharGlyphInfo.cs          # SDF 字符映射
│       │   └── HUDConstants.cs           # 常量定义
│       └── Utils/
│           ├── RingBuffer.cs             # 环形缓冲区
│           └── SDFCharLookup.cs          # SDF 字符查表
├── Shaders/
│   └── HUD/
│       ├── HUDInstanced.shader           # 主 Shader
│       └── HUDInstanced.hlsl             # Shader 函数库
├── Art/
│   └── HUD/
│       ├── HUDAtlas.png                  # 静态 Atlas（4096×4096）
│       ├── SDFFont.asset                 # SDF 字体资产
│       └── HealthBarGradient.png         # 血条渐变
└── Editor/
    └── HUD/
        ├── SDFFontAtlasGenerator.cs      # SDF Atlas 生成工具
        └── HUDAtlasPacker.cs             # Atlas 打包工具
```
