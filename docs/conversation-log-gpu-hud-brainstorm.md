# GPU HUD 系统头脑风暴与实现全记录

> **日期**: 2026-02-27  
> **项目**: Unity 2022.3 LTS + URP  
> **目标**: SLG 大世界 10,000 单位同屏 GPU HUD 系统  
> **性能指标**: 1 DrawCall · 0 GC · 60 FPS  

---

## 目录

- [第一阶段：头脑风暴（6 个技术决策）](#第一阶段头脑风暴6-个技术决策)
  - [决策 1：文本渲染方案](#决策-1文本渲染方案)
  - [决策 2：渲染架构](#决策-2渲染架构)
  - [决策 3：纹理 Atlas 组织策略](#决策-3纹理-atlas-组织策略)
  - [决策 4：CPU → GPU 数据更新策略](#决策-4cpu--gpu-数据更新策略)
  - [决策 5：HUD 元素布局方式](#决策-5hud-元素布局方式)
  - [决策 6：飘血生命周期管理](#决策-6飘血生命周期管理)
- [第二阶段：分段设计方案（6 个段落）](#第二阶段分段设计方案6-个段落)
  - [设计 1：系统总体架构](#设计-1系统总体架构)
  - [设计 2：核心数据结构](#设计-2核心数据结构)
  - [设计 3：Shader 设计](#设计-3shader-设计)
  - [设计 4：CPU 端管理逻辑](#设计-4cpu-端管理逻辑)
  - [设计 5：纹理资源管理与头像加载](#设计-5纹理资源管理与头像加载)
  - [设计 6：性能预算与优化策略](#设计-6性能预算与优化策略)
- [第三阶段：代码实现](#第三阶段代码实现)
  - [文件清单](#文件清单)
  - [核心代码](#核心代码)
- [第四阶段：测试环境搭建](#第四阶段测试环境搭建)
- [附录：技术决策汇总表](#附录技术决策汇总表)

---

## 第一阶段：头脑风暴（6 个技术决策）

### 需求原文

> 实现一套 Unity 2022.3 下的 SLG 大世界高性能 GPU HUD 系统，需要有 HUD 元素：
> 1. **Avatar**: 玩家自定义头像（128×128，从 CDN 加载）
> 2. **Icons**: 技能、Buff、兵种图标（系统预设）
> 3. **血条**: 单条血条，颜色渐变表示血量
> 4. **名字**: 玩家名字文本
> 5. **飘血**: 伤害/治疗跳字，基础特效（颜色+缩放+位移动画）
> 
> 希望实现 1 个 DrawCall 和 0 GC 的高性能，单位数需要是 10,000，保持稳定 60 帧运行。

---

### 决策 1：文本渲染方案

**问题**：名字和飘血涉及文字显示，在"1 DC + GPU 驱动"的约束下是最大的难点。

| 方案 | 描述 | 优点 | 缺点 |
|------|------|------|------|
| **A. 预烘焙 TextureAtlas** | 将所有可能的文字预烘焙到一张大 Atlas 上，GPU 端直接采样 | 完美契合 1DC 目标；纯 GPU 驱动；0 GC | 中文字符集太大（常用 3500+ 字），Atlas 很大；不支持任意 Unicode |
| **B. SDF Font Atlas ✅** | 使用 SDF（Signed Distance Field）字体技术，预生成包含 ASCII + 常用中文字符的 SDF Atlas | 缩放无损、质量高；可合并到统一 Atlas；成熟方案 | 需要预生成字符集；极端生僻字可能缺失 |
| **C. Runtime 动态生成** | 每个名字 Runtime 渲染到小纹理，打包到动态 Atlas | 支持任意文字；视觉效果好 | 有 GC 开销（与 0GC 目标冲突） |

**结论**：选择 **B. SDF Font Atlas**，因为缩放无损、可合并到统一 Atlas、0 GC。

---

### 决策 2：渲染架构

**问题**：要实现万级单位 1 DrawCall，核心架构选择至关重要。

| 方案 | 描述 | 优点 | 缺点 |
|------|------|------|------|
| **A. DrawMeshInstancedIndirect ✅** | 使用一个 Quad Mesh + ComputeBuffer 存储所有 HUD 实例数据，GPU Instancing 一次绘制全部 | 真正 1 DC；数据全在 GPU 端；Unity 2022.3 完美支持 | 需要手写 Shader |
| **B. CommandBuffer + DrawMeshInstancedProcedural** | 通过 CommandBuffer 插入渲染管线 | 可精确控制渲染顺序；同样 1 DC | 复杂度略高 |
| **C. ComputeShader + DrawProcedural** | ComputeShader 直接生成所有顶点数据 | 完全 GPU 驱动；可做 GPU 端剔除/LOD | 实现最复杂；调试困难 |

**结论**：选择 **A. `Graphics.DrawMeshInstancedIndirect`**，理由：
1. Unity 2022.3 原生支持，稳定可靠
2. 一个 Quad Mesh 就能表示所有 HUD 元素
3. StructuredBuffer 传递每个实例的数据
4. 可配合 GPU 视锥剔除（在 Vertex Shader 中退化为零面积三角形）

---

### 决策 3：纹理 Atlas 组织策略

**问题**：要实现 1 DC，所有视觉元素必须打包到同一组纹理中。

| 方案 | 描述 | 优点 | 缺点 |
|------|------|------|------|
| **A. 单张超大 Atlas** | 将所有资源打包到一张 4096×4096 | 绝对 1 DC；Shader 逻辑简单 | 需要精心规划布局 |
| **B. Texture2DArray 分层** | 每层存不同类型 | 每层独立管理；不存在 UV 干扰 | 每层尺寸必须一致 |
| **C. 混合方案 ✅** | 系统图标/SDF字体/血条用一张静态 Atlas；头像用独立的 Texture2DArray | 头像可动态换页；静态资源 Atlas 紧凑 | Shader 需采样两张纹理 |

**结论**：选择 **C. 混合方案**，理由：
1. 头像是动态的（CDN 加载），用 Texture2DArray 可按 sliceIndex 动态替换
2. 系统资源是静态的，打成紧凑 Atlas 最高效
3. 1 DC 不受影响——Shader 绑定两个纹理，同一个 Material
4. 头像 Texture2DArray：128×128 每页，256 页，超出用 LRU 缓存替换

---

### 决策 4：CPU → GPU 数据更新策略

**问题**：10,000 个单位的 HUD 数据每帧可能变化，如何高效传递到 GPU。

| 方案 | 描述 | 优点 | 缺点 |
|------|------|------|------|
| **A. 全量更新 ComputeBuffer** | 每帧 `SetData()` 上传整个数组 | 实现简单 | 万级单位时数据量较大（~1MB/帧） |
| **B. 脏标记 + 分块更新 ✅** | 将 Buffer 分成多个 Chunk（每 256 个单位一块），只上传标记为脏的 | 减少无变化时的传输量 | 需要脏标记管理逻辑 |
| **C. 双缓冲 + 异步上传** | 两个 Buffer 交替使用 | 最低延迟 | 实现最复杂；内存翻倍 |

**结论**：选择 **B. 脏标记 + 分块更新**，理由：
1. SLG 场景大部分单位处于静止状态，全量更新浪费带宽
2. `NativeArray<T>` + `ComputeBuffer.SetData(nativeArray, srcOffset, dstOffset, count)` 按 Chunk 部分更新，完全零分配
3. Chunk 粒度：256 个单位/Chunk × 128 字节 = 32KB/Chunk
4. 方案 A 可作为保底方案

---

### 决策 5：HUD 元素布局方式

**问题**：每个单位的 HUD 包含 5 种元素，在 1 DC 约束下如何组织子元素。

| 方案 | 描述 | 优点 | 缺点 |
|------|------|------|------|
| **A. 每个元素一个 Instance ✅** | 每个单位拆分为多个 Instance，通过 unitID 关联 | 每个元素可独立控制；灵活度最高；仍然 1 DC | Instance 数量膨胀（每单位约 15~20 个） |
| **B. 每个单位一个 Instance** | 在 Fragment Shader 中用 UV 条件判断绘制所有子元素 | Instance 数量最少 | Shader 分支多、复杂度高 |
| **C. 按元素类型分 Pass** | 头像一批、血条一批、名字一批 | 每种元素 Shader 简单 | 违反 1 DC 目标 |

**结论**：选择 **A. 每个元素一个 Instance**，理由：
- 10,000 单位 × ~15 实例/单位 = 约 150,000 个 Instance，现代 GPU 轻松处理
- 每个 Instance 只是一个小 Quad，通过 instanceID 索引 StructuredBuffer
- 灵活度高：头像圆形裁剪、图标闪烁、飘血动画互不干扰

**每单位 Instance 分布（约 15 个）**：

```
├── 头像 ×1           → 采样 AvatarArray
├── 血条背景 ×1       → 纯色/Atlas UV
├── 血条前景 ×1       → 颜色渐变 + 宽度裁剪
├── 名字 ×8~10        → 每字一个 Instance，采样 SDF Atlas
├── 图标 ×2~3         → 采样 Atlas（兵种 + Buff）
└── 飘血 ×1（动态）   → 环形缓冲区管理
```

---

### 决策 6：飘血生命周期管理

**问题**：飘血是瞬态的（触发 → 动画 → 消失），在 0 GC 约束下需要特殊管理。

| 方案 | 描述 | 优点 | 缺点 |
|------|------|------|------|
| **A. 预分配全局飘血池 ✅** | 预分配 512 个飘血 Instance 槽位，环形缓冲区管理 | 0 GC；固定内存；逻辑简单 | 同时飘血数有上限 |
| **B. 每单位预留飘血槽位** | 每个单位预留 2~3 个飘血 Instance 槽位 | 独立管理；不会抢占 | 浪费内存（30000 个永久槽位） |
| **C. 动态增长缓冲区** | 动态添加到末尾 | 无上限 | 碎片化；可能触发 Buffer 扩容 = GC |

**结论**：选择 **A. 预分配全局飘血池 + 环形缓冲区**，理由：
1. 环形缓冲区实现极简：`nextIndex = (nextIndex + 1) % poolSize`
2. 动画完全在 GPU 端：每个飘血 Instance 只需 `startTime`，Shader 用 `_Time.y - startTime` 计算进度
3. 512 个槽位足够覆盖 SLG 战斗场景

**飘血动画参数**：
- 持续时间: 0.8s
- 位移: Y 轴上移 50px
- 缩放: `1.0 + 0.3 * sin(t * PI)`（弹跳效果）
- 透明度: 后 30% 时间线性淡出
- 颜色: 伤害=红色，治疗=绿色，暴击=橙色+加大字号

---

## 第二阶段：分段设计方案（6 个段落）

### 设计 1：系统总体架构

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

**四大模块职责**：
- **HUDDataStore**：CPU 端数据管理。维护 `NativeArray<HUDInstanceData>`，按 256 单位一个 Chunk 管理脏标记
- **HUDRenderer**：渲染执行。持有 Material 和 ComputeBuffer，调用 `Graphics.DrawMeshInstancedIndirect`
- **HUDAnimator**：动画驱动。管理飘血环形缓冲区，所有动画在 GPU 端完成
- **HUDAtlasManager**：纹理资源管理。维护静态 Atlas 和动态头像 Texture2DArray

---

### 设计 2：核心数据结构

#### HUDInstanceData（GPU 端，64 字节对齐）

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
    public uint    flags;           //  4B  位标记
}
```

#### 元素类型标记（flags 位域）

```csharp
public enum HUDElementType : uint
{
    Avatar    = 0,  // 头像 → 采样 Texture2DArray
    Icon      = 1,  // 图标 → 采样 Atlas
    HealthBar = 2,  // 血条 → 采样 Atlas + 宽度裁剪
    Text      = 3,  // 文字 → 采样 SDF Atlas
    FloatText = 4,  // 飘血 → 采样 SDF Atlas + 动画
}
// flags 布局: [31..12 预留] [11..4 avatarSlice] [3 visible] [2..0 type]
```

#### FloatingTextData（飘血专用，16 字节）

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct FloatingTextData  // 16 bytes
{
    public float  startTime;      // 触发时间
    public float  duration;       // 持续时间
    public float  value;          // 数值
    public uint   styleFlags;     // 颜色类型 + 暴击标记
}
```

**关键设计点**：
- 64 字节对齐，GPU 缓存行友好
- 全部值类型，零装箱、零 GC
- flags 使用位域压缩，避免多余字段

---

### 设计 3：Shader 设计

#### Vertex Shader：世界坐标 → 屏幕空间 Billboard

```hlsl
Varyings HUDVert(Attributes input, uint instanceID : SV_InstanceID)
{
    HUDInstanceData data = _HUDBuffer[instanceID];

    // 1. 世界坐标 → 裁剪空间
    float4 clipPos = mul(UNITY_MATRIX_VP, float4(data.worldPosition, 1.0));

    // 2. GPU 视锥剔除：退化为零面积
    float cullMask = step(-clipPos.w, clipPos.x) * step(clipPos.x, clipPos.w)
                   * step(-clipPos.w, clipPos.y) * step(clipPos.y, clipPos.w)
                   * step(0, clipPos.w);
    cullMask *= visible;

    // 3. 透视除法 + 像素偏移
    float2 ndcPos = clipPos.xy / clipPos.w;
    ndcPos += pixelOffset;

    // 4. Quad 顶点展开
    ndcPos += input.positionOS.xy * quadSize * cullMask;

    // 5. 飘血动画（仅 FloatText 类型）
    if (elemType == ELEM_TYPE_FLOATTEXT) { /* 上浮 + 弹跳 */ }

    return o;
}
```

#### Fragment Shader：按类型分支采样

```hlsl
float4 HUDFrag(Varyings input) : SV_Target
{
    uint elemType = GetElementType(input.flags);

    if (elemType == ELEM_TYPE_AVATAR)
    {
        // Texture2DArray 采样 + 圆形裁剪（smoothstep 抗锯齿）
    }
    else if (elemType == ELEM_TYPE_TEXT || elemType == ELEM_TYPE_FLOATTEXT)
    {
        // SDF 采样 + smoothstep 抗锯齿 + 飘血淡出
    }
    else if (elemType == ELEM_TYPE_HEALTHBAR)
    {
        // Atlas 采样 + localU 宽度裁剪
    }
    else // Icon
    {
        // Atlas 采样
    }
}
```

**关键设计点**：
- GPU 视锥剔除：不可见 Instance 零片元开销
- SDF 抗锯齿：smoothstep 实现平滑边缘
- 所有动画 GPU 端：`_Time.y` 驱动飘血动画

---

### 设计 4：CPU 端管理逻辑

#### 主循环（0 GC 关键路径）

```csharp
void LateUpdate()
{
    // 步骤 1：处理头像加载
    _atlasManager.ProcessLoadQueue();

    // 步骤 2：上传脏 Chunk
    _dataStore.UploadDirtyChunks();

    // 步骤 3：绑定并绘制（唯一的 DrawCall）
    _renderer.Render(_totalInstanceCount);
}
```

#### 分块脏更新

```csharp
private void UploadDirtyChunks()
{
    for (int i = 0; i < _chunkDirty.Length; i++)
    {
        if (!_chunkDirty[i]) continue;

        int start = i * ChunkSize;
        int count = Mathf.Min(ChunkSize, _activeInstanceCount - start);
        // NativeArray → ComputeBuffer 部分上传，零 GC
        _instanceBuffer.SetData(_instanceData, start, start, count);
        _chunkDirty[i] = false;
    }
}
```

#### 飘血触发（环形缓冲区）

```csharp
public void SpawnFloatingText(Vector3 worldPos, float value, FloatTextStyle style)
{
    int idx = _floatTextNextIndex;
    _floatTextNextIndex = (idx + 1) % FloatTextPoolSize;
    // 写入数据，创建字符 Instance，上传 Buffer —— 全 struct 赋值，0 GC
}
```

#### 零 GC 保证清单

| 操作 | GC 分析 |
|------|---------|
| `NativeArray<T>` | Unmanaged 内存，无 GC |
| `ComputeBuffer.SetData(NativeArray)` | 直接内存拷贝，无装箱 |
| `bool[] _chunkDirty` | 初始化一次，不扩容 |
| `Graphics.DrawMeshInstancedIndirect` | Unity 内部验证，无 GC |
| 飘血 `SpawnFloatingText` | 全 struct 赋值，无堆分配 |

---

### 设计 5：纹理资源管理与头像加载

#### 静态 Atlas 布局（4096×4096）

```
┌───────────────────────────────────────────┐
│  ┌─────────────────────────────────────┐  │
│  │     SDF Font Atlas (2048×2048)      │  │
│  │  ASCII + 常用中文 3500字 + 数字符号  │  │
│  └─────────────────────────────────────┘  │
│  ┌──────────────┐  ┌──────────────────┐   │
│  │  Icons 区域  │  │  血条渐变纹理    │   │
│  │  512×512     │  │  256×32          │   │
│  └──────────────┘  └──────────────────┘   │
│  ┌──────────────────────────────────────┐  │
│  │         预留扩展区域                 │  │
│  └──────────────────────────────────────┘  │
└───────────────────────────────────────────┘
```

#### 头像 LRU 缓存

- Texture2DArray：128×128，256 slices
- `GetAvatarSlice(uid)` → 已缓存直接返回，未缓存 LRU 淘汰 + 异步加载
- `Graphics.CopyTexture()` GPU 端拷贝，零 GC
- 每帧最多加载 2 张头像，防止卡帧

#### SDF 字符查表

- 排序数组 + 二分查找，O(logN)，比 Dictionary 更 cache-friendly，零 GC

---

### 设计 6：性能预算与优化策略

#### 帧时间预算（16.67ms @ 60 FPS）

| 模块 | 预算 | 预估 |
|------|------|------|
| CPU 数据更新 | < 2ms | ~0.5ms |
| CPU 调度 | < 0.5ms | ~0.1ms |
| GPU 顶点 | < 3ms | ~1.5ms |
| GPU 片元 | < 3ms | ~2ms |
| **合计** | < 8.5ms | ~4.1ms |

#### 内存预算

| 资源 | 大小 |
|------|------|
| Instance NativeArray | ~10 MB |
| Instance ComputeBuffer | ~10 MB |
| 静态 Atlas 4096² | ~16 MB |
| 头像 Texture2DArray | ~8 MB |
| **合计** | ~44 MB |

#### GPU 端优化

- 视锥剔除（零面积退化）
- 距离 LOD（远距离只保留血条）
- Instance 按类型排序（减少 warp 分支发散）
- SDF 抗锯齿（smoothstep）
- 动画全 GPU（_Time.y 驱动）

#### CPU 端优化

- NativeArray 替代 managed array
- 脏 Chunk 批量上传
- SDF 查表用二分查找
- 名字 Instance 变化时才重建
- 头像分帧加载
- **禁用**：string 拼接、LINQ、delegate 捕获、List 扩容

#### 开发里程碑

| 阶段 | 里程碑 | 预估工期 |
|------|--------|---------|
| M1 | 基础渲染框架 | 2 天 |
| M2 | 血条 + 图标 | 2 天 |
| M3 | SDF 文字渲染 | 3 天 |
| M4 | 头像系统 | 2 天 |
| M5 | 飘血动画 | 2 天 |
| M6 | 性能调优 | 2 天 |

---

## 第三阶段：代码实现

### 文件清单

```
Assets/_Project/
├── Scripts/HUD/
│   ├── Core/
│   │   ├── HUDSystem.cs              # 系统入口，LateUpdate 调度
│   │   ├── HUDRenderer.cs            # Quad Mesh + 1 DrawCall
│   │   ├── HUDDataStore.cs           # NativeArray + 脏 Chunk 上传
│   │   ├── HUDAnimator.cs            # 飘血环形缓冲区
│   │   └── HUDAtlasManager.cs        # 头像 LRU 缓存 + CDN 加载
│   ├── Data/
│   │   ├── HUDConstants.cs           # 全局常量
│   │   ├── HUDInstanceData.cs        # 64B GPU Instance 结构
│   │   ├── FloatingTextData.cs       # 16B 飘血数据
│   │   └── CharGlyphInfo.cs          # SDF 字符映射
│   ├── Utils/
│   │   └── SDFCharLookup.cs          # 二分查找字符表
│   └── Demo/
│       ├── HUDTestScene.cs           # 10K 单位测试
│       └── FreeCameraController.cs   # WASD 飞行相机
├── Shaders/HUD/
│   ├── HUDCommon.hlsl                # Shader 公共库
│   └── HUDInstanced.shader           # 统一 GPU HUD Shader
└── Editor/HUD/
    └── HUDSetupWizard.cs             # 一键搭建测试环境
```

### 核心代码

> 完整代码请参见项目中的各文件。以下摘录关键片段。

#### HUDInstanceData.cs - 64 字节 GPU 数据结构

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct HUDInstanceData  // 64 bytes
{
    public Vector3 worldPosition;   // 12B
    public float   screenOffsetY;   //  4B
    public float   screenOffsetX;   //  4B
    public Vector2 size;            //  8B
    public Vector4 uvRect;          // 16B
    public Color   color;           // 16B
    public uint    flags;           //  4B

    public void SetType(HUDElementType type) { ... }
    public void SetVisible(bool visible) { ... }
    public void SetAvatarSlice(int sliceIndex) { ... }
}
```

#### HUDInstanced.shader - 核心 Shader 逻辑

```hlsl
// Vertex: 世界坐标 → 屏幕 Billboard + GPU 剔除 + 飘血动画
Varyings HUDVert(Attributes input, uint instanceID : SV_InstanceID)
{
    HUDInstanceData data = _HUDBuffer[instanceID];
    float4 clipPos = mul(UNITY_MATRIX_VP, float4(data.worldPosition, 1.0));
    float cullMask = FrustumCull(clipPos) * GetVisible(data.flags);
    // ... Billboard + 飘血动画
}

// Fragment: 按类型分支采样
float4 HUDFrag(Varyings input) : SV_Target
{
    // Avatar → Texture2DArray + 圆形裁剪
    // Text → SDF smoothstep
    // HealthBar → 宽度裁剪
    // Icon → Atlas 采样
    // FloatText → SDF + 淡出
}
```

#### HUDRenderer.cs - 唯一的 DrawCall

```csharp
public void Render(int activeInstanceCount)
{
    _args[1] = (uint)activeInstanceCount;
    _argsBuffer.SetData(_args);  // 预分配的 uint[5]，无 GC
    _material.SetBuffer("_HUDBuffer", _instanceBuffer);
    Graphics.DrawMeshInstancedIndirect(_quadMesh, 0, _material, _bounds, _argsBuffer);
}
```

#### HUDDataStore.cs - 脏 Chunk 上传

```csharp
public void UploadDirtyChunks()
{
    for (int i = 0; i < _chunkDirty.Length; i++)
    {
        if (!_chunkDirty[i]) continue;
        int start = i * ChunkSize;
        int count = Mathf.Min(ChunkSize, _activeInstanceCount - start);
        _instanceBuffer.SetData(_instanceData, start, start, count); // NativeArray → GPU，0 GC
        _chunkDirty[i] = false;
    }
}
```

---

## 第四阶段：测试环境搭建

由于 Unity 项目需要场景和纹理资源才能运行，创建了 **Editor 自动化工具**：

### 使用方法

1. 打开 Unity Hub → 打开 `hud-perf-gemini` 项目
2. 等待编译完成
3. 点击菜单 **`HUD Tools`** → **`一键搭建测试环境`**

### 工具自动创建的资源

| 资源 | 说明 |
|------|------|
| `HUDAtlas.png` | 1024×1024 占位 Atlas（数字 0-9 位图、8 个彩色图标、血条渐变） |
| `avatar_test_0~7.png` | 8 张 128×128 不同颜色的测试头像 |
| `HUD_Material.mat` | 绑定好 Shader 和 Atlas 的 Material |
| `HUDTestScene.unity` | 完整测试场景（相机+灯光+地面+HUDSystem+测试脚本） |

### 场景操作

| 操作 | 按键 |
|------|------|
| 移动 | WASD |
| 旋转视角 | 鼠标右键拖动 |
| 加速移动 | Shift |
| 调整速度 | 滚轮 |

### 性能验证

- **Profiler** (Window → Analysis → Profiler)：检查 GC Alloc = 0
- **Frame Debugger** (Window → Analysis → Frame Debugger)：检查 DrawCall 数
- 左上角调试信息面板：单位数、Instance 数、FPS

### 调整单位数

初始设为 1000 个（先验证功能），确认后：
- 选中 HUDSystem 对象
- Inspector → HUD Test Scene → Unit Count 改为 10000
- 重新 Play

---

## 附录：技术决策汇总表

| # | 决策点 | 选择 | 核心理由 |
|---|--------|------|---------|
| 1 | 文本渲染 | SDF Font Atlas | 缩放无损、可合并 Atlas、0 GC |
| 2 | 渲染架构 | DrawMeshInstancedIndirect | Unity 原生、真正 1 DC、灵活 |
| 3 | 纹理组织 | Atlas + Texture2DArray 混合 | 静态紧凑 + 动态替换 |
| 4 | 数据更新 | 脏标记 + 分块 ComputeBuffer | SLG 特性、减少传输 |
| 5 | 元素布局 | 每元素一个 Instance | 灵活、GPU 无压力 |
| 6 | 飘血管理 | 预分配池 + 环形缓冲区 | 0 GC、固定内存、简单 |

---

> **备注**：本文档记录了从需求分析 → 技术决策 → 架构设计 → 代码实现 → 测试环境搭建的完整过程，可作为类似 GPU 驱动 UI 系统的参考方案。
