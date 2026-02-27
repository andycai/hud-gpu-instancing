# GPU HUD System for Unity 2022.3 SLG

> 🎮 高性能 GPU 驱动的 HUD 渲染系统，专为 SLG 大世界场景设计

[![Unity](https://img.shields.io/badge/Unity-2022.3%20LTS-black?logo=unity)](https://unity.com)
[![URP](https://img.shields.io/badge/Pipeline-URP-blue)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)

## ✨ 核心特性

| 指标 | 目标 |
|------|------|
| **同屏单位数** | 10,000 |
| **DrawCall** | 1 |
| **GC 分配** | 0 B/帧（运行时） |
| **帧率** | 稳定 60 FPS |

### HUD 元素

- 🖼️ **头像**（Avatar）：128×128 玩家自定义头像，CDN 异步加载 + LRU 缓存
- ⚔️ **图标**（Icons）：技能、Buff、兵种图标，系统预设 Atlas
- ❤️ **血条**（Health Bar）：颜色渐变（绿→黄→红），GPU 端宽度裁剪
- 📝 **名字**（Name）：SDF 字体渲染，缩放无损
- 💥 **飘血**（Floating Text）：伤害/治疗数字弹跳动画，GPU 端驱动

## 🏗️ 技术架构

```
┌──────────────────────────────────────────────┐
│              HUDSystem (入口)                 │
├──────────────────────────────────────────────┤
│  HUDDataStore    │ HUDRenderer │ HUDAnimator │
│  NativeArray     │ 1 DrawCall  │ 飘血缓冲区  │
│  脏 Chunk 更新   │ ComputeBuffer│ GPU 动画   │
├──────────────────────────────────────────────┤
│           HUDAtlasManager                    │
│  静态 Atlas + 动态 Texture2DArray（LRU）      │
└──────────────────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────────┐
│  HUDInstanced.shader (GPU)                   │
│  统一处理 5 种元素 · 视锥剔除 · SDF 渲染     │
└──────────────────────────────────────────────┘
```

### 关键技术决策

| 技术点 | 方案 | 理由 |
|--------|------|------|
| 渲染方式 | `Graphics.DrawMeshInstancedIndirect` | 真正单次 DrawCall，150K Instance |
| 文字渲染 | SDF Font Atlas | 缩放无损，可合并到统一 Atlas |
| 纹理组织 | Atlas + Texture2DArray 混合 | 静态紧凑 + 动态头像替换 |
| 数据传输 | 脏标记 + 分块 ComputeBuffer | SLG 场景大部分单位静止 |
| 飘血管理 | 环形缓冲区 | 0 GC，固定内存 |
| 视锥剔除 | Vertex Shader 零面积退化 | 屏幕外 Instance 零开销 |

## 📁 项目结构

```
Assets/_Project/
├── Scripts/HUD/
│   ├── Core/
│   │   ├── HUDSystem.cs              # 系统入口，LateUpdate 调度
│   │   ├── HUDRenderer.cs            # Quad Mesh + 唯一的 DrawCall
│   │   ├── HUDDataStore.cs           # NativeArray + 脏 Chunk 上传
│   │   ├── HUDAnimator.cs            # 飘血环形缓冲区
│   │   └── HUDAtlasManager.cs        # 头像 LRU 缓存 + CDN 加载
│   ├── Data/
│   │   ├── HUDConstants.cs           # 全局常量
│   │   ├── HUDInstanceData.cs        # 64B GPU Instance 结构体
│   │   ├── FloatingTextData.cs       # 16B 飘血数据
│   │   └── CharGlyphInfo.cs          # SDF 字符映射
│   ├── Utils/
│   │   └── SDFCharLookup.cs          # 排序数组 + 二分查找 (0 GC)
│   └── Demo/
│       ├── HUDTestScene.cs           # 10K 单位压力测试
│       └── FreeCameraController.cs   # WASD 飞行相机
├── Shaders/HUD/
│   ├── HUDCommon.hlsl                # Shader 公共定义与工具函数
│   └── HUDInstanced.shader           # 统一 GPU HUD 渲染 Shader
├── Editor/HUD/
│   └── HUDSetupWizard.cs             # 一键搭建测试环境
└── Art/HUD/
    ├── HUDAtlas.png                  # 主 Atlas（自动生成）
    └── Materials/HUD_Material.mat    # HUD 材质（自动生成）
```

## 🚀 快速开始

### 环境要求

- Unity **2022.3 LTS** 或更高版本
- 渲染管线：**URP**（Universal Render Pipeline）
- 平台：PC / macOS（需要 Shader Model 4.5+）

### 搭建步骤

1. **克隆项目**
   ```bash
   git clone <repo-url>
   ```

2. **使用 Unity Hub 打开项目**，等待编译完成

3. **一键搭建测试环境**
   ```
   菜单栏 → HUD Tools → 一键搭建测试环境
   ```
   自动生成：Atlas 纹理、测试头像、Material、测试场景

4. **运行测试**
   - 点击 **▶ Play**
   - 默认 1000 个单位，确认功能后 Inspector 中改为 10000

### 场景操作

| 操作 | 按键 |
|------|------|
| 移动 | WASD |
| 旋转视角 | 鼠标右键拖动 |
| 加速 | Shift |
| 调整速度 | 滚轮 |

## 📊 性能数据

### 帧时间预算（10,000 单位 @ 60 FPS）

| 模块 | 预算 | 预估 |
|------|------|------|
| CPU 数据更新 | < 2ms | ~0.5ms |
| CPU 调度 | < 0.5ms | ~0.1ms |
| GPU 顶点处理 | < 3ms | ~1.5ms |
| GPU 片元处理 | < 3ms | ~2ms |
| **合计** | **< 8.5ms** | **~4.1ms** |

### 内存占用

| 资源 | 大小 |
|------|------|
| Instance Buffer（CPU + GPU） | ~20 MB |
| 主 Atlas | ~16 MB |
| 头像 Texture2DArray | ~8 MB |
| **合计** | **~44 MB** |

### 性能验证方式

- **Profiler**（Window → Analysis → Profiler）：检查 GC Alloc 列
- **Frame Debugger**（Window → Analysis → Frame Debugger）：确认 DrawCall 数
- 左上角 **OnGUI 面板**：实时单位数、Instance 数、FPS

## 🔧 API 使用

```csharp
// 获取 HUDSystem 引用
var hudSystem = GetComponent<HUDSystem>();

// 注册单位
int unitIndex = hudSystem.RegisterUnit(
    worldPosition: transform.position,
    unitName: "Player01",
    healthPercent: 1.0f,
    avatarUID: 12345
);

// 更新血量
hudSystem.UpdateUnitHealth(unitIndex, 0.6f);

// 更新位置
hudSystem.UpdateUnitPosition(unitIndex, newPosition);

// 触发飘血
hudSystem.SpawnFloatingText(
    transform.position,
    value: -1234,
    style: FloatTextStyle.Crit
);
```

## 📚 设计文档

- [完整设计方案](docs/plans/2026-02-27-gpu-hud-system-design.md) — 14 章节详细设计
- [实现计划](docs/plans/2026-02-27-gpu-hud-implementation-plan.md) — 8 个 Task 分步流程
- [头脑风暴全记录](docs/conversation-log-gpu-hud-brainstorm.md) — 6 个技术决策的完整推导

## 🔮 扩展方向

| 方向 | 实现思路 |
|------|---------|
| ComputeShader 剔除 | GPU 端遮挡剔除 → AppendBuffer |
| 多级 LOD | 远/中/近三级显示不同内容 |
| 名字描边/阴影 | SDF Shader 扩展 outline + shadow |
| Buff 倒计时 | `_Time.y` 驱动扇形裁剪动画 |
| 完整 SDF 中文字体 | TMP Font Asset Creator 生成 3500+ 常用字 |

## 📄 License

MIT License
