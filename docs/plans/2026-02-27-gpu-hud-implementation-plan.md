# GPU HUD 系统实现计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 实现 Unity 2022.3 下 10,000 单位同屏的 GPU 驱动 HUD 系统，1 DrawCall，0 GC，60 FPS

**Architecture:** 使用 `Graphics.DrawMeshInstancedIndirect` 将所有 HUD 元素作为 GPU Instance 一次绘制。CPU 端用 NativeArray + ComputeBuffer 管理数据，GPU 端 Shader 统一处理头像/血条/图标/SDF文字/飘血5种元素类型。

**Tech Stack:** Unity 2022.3 LTS, URP, C#, HLSL, ComputeBuffer, NativeArray, Texture2DArray

---

## Task 1: 数据结构与常量定义

**Files:**
- Create: `Assets/_Project/Scripts/HUD/Data/HUDConstants.cs`
- Create: `Assets/_Project/Scripts/HUD/Data/HUDInstanceData.cs`
- Create: `Assets/_Project/Scripts/HUD/Data/FloatingTextData.cs`
- Create: `Assets/_Project/Scripts/HUD/Data/CharGlyphInfo.cs`

**Step 1:** 创建目录结构和所有数据结构文件

**Step 2:** 验证编译通过（Unity Console 无错误）

**Step 3:** Commit: `feat(hud): 添加核心数据结构定义`

---

## Task 2: HUD Shader

**Files:**
- Create: `Assets/_Project/Shaders/HUD/HUDCommon.hlsl`
- Create: `Assets/_Project/Shaders/HUD/HUDInstanced.shader`

**Step 1:** 创建 HUDCommon.hlsl 包含数据结构定义和工具函数

**Step 2:** 创建 HUDInstanced.shader 包含完整的 Vertex/Fragment Shader

**Step 3:** 验证 Shader 编译通过（Unity Console 无错误）

**Step 4:** Commit: `feat(hud): 添加 GPU Instanced HUD Shader`

---

## Task 3: HUDRenderer 渲染核心

**Files:**
- Create: `Assets/_Project/Scripts/HUD/Core/HUDRenderer.cs`

**Step 1:** 实现 Mesh 创建、ComputeBuffer 管理、DrawMeshInstancedIndirect 调用

**Step 2:** 验证可以在场景中挂载并运行

**Step 3:** Commit: `feat(hud): 添加 HUDRenderer 渲染核心`

---

## Task 4: HUDDataStore 数据管理

**Files:**
- Create: `Assets/_Project/Scripts/HUD/Core/HUDDataStore.cs`

**Step 1:** 实现 NativeArray 管理、Chunk 脏标记、分块上传

**Step 2:** Commit: `feat(hud): 添加 HUDDataStore 数据管理`

---

## Task 5: HUDAnimator 飘血动画

**Files:**
- Create: `Assets/_Project/Scripts/HUD/Core/HUDAnimator.cs`

**Step 1:** 实现环形缓冲区、飘血触发、字符拆分

**Step 2:** Commit: `feat(hud): 添加 HUDAnimator 飘血动画管理`

---

## Task 6: HUDAtlasManager 纹理管理

**Files:**
- Create: `Assets/_Project/Scripts/HUD/Core/HUDAtlasManager.cs`

**Step 1:** 实现 Texture2DArray 头像管理、LRU 缓存

**Step 2:** Commit: `feat(hud): 添加 HUDAtlasManager 纹理管理`

---

## Task 7: HUDSystem 系统入口

**Files:**
- Create: `Assets/_Project/Scripts/HUD/Core/HUDSystem.cs`

**Step 1:** 实现系统入口，整合所有子模块，LateUpdate 调度

**Step 2:** Commit: `feat(hud): 添加 HUDSystem 系统入口`

---

## Task 8: 测试场景与性能验证

**Files:**
- Create: `Assets/_Project/Scripts/HUD/Demo/HUDTestScene.cs`
- Create: `Assets/_Project/Scenes/HUDTest.unity`（在 Unity Editor 中创建）

**Step 1:** 创建测试脚本，生成 10,000 个单位的 HUD 数据

**Step 2:** 在 Unity Editor 中验证：1 DC、0 GC、60 FPS

**Step 3:** Commit: `feat(hud): 添加测试场景与性能验证`
