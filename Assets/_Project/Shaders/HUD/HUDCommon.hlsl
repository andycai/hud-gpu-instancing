// ============================================================================
// HUDCommon.hlsl
// GPU HUD 系统 Shader 公共定义
// 包含数据结构、工具函数、采样宏
// ============================================================================

#ifndef HUD_COMMON_INCLUDED
#define HUD_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// ============================================================================
// GPU 端数据结构（必须与 C# 端 struct 完全对齐）
// ============================================================================

/// HUD Instance 数据（64 bytes）
struct HUDInstanceData
{
    float3 worldPosition;   // 12B
    float  screenOffsetY;   //  4B
    float  screenOffsetX;   //  4B
    float2 size;            //  8B
    float4 uvRect;          // 16B
    float4 color;           // 16B
    uint   flags;           //  4B
};

/// 飘血数据（16 bytes）
struct FloatingTextData
{
    float  startTime;       //  4B
    float  duration;        //  4B
    float  value;           //  4B
    uint   styleFlags;      //  4B
};

// ============================================================================
// Buffer 声明
// ============================================================================

StructuredBuffer<HUDInstanceData>  _HUDBuffer;
StructuredBuffer<FloatingTextData> _FloatBuffer;

// ============================================================================
// Shader 参数
// ============================================================================

TEXTURE2D(_MainAtlas);
SAMPLER(sampler_MainAtlas);

TEXTURE2D_ARRAY(_AvatarArray);
SAMPLER(sampler_AvatarArray);

CBUFFER_START(HUDParams)
    float _SDFThreshold;       // SDF 阈值（默认 0.5）
    float _SDFSoftness;        // SDF 柔化宽度（默认 0.05）
    float _FloatRiseHeight;    // 飘血上浮高度（NDC 空间）
    float _FloatBounceScale;   // 飘血弹跳幅度
    uint  _FloatBufferOffset;  // 飘血 Instance 在 _HUDBuffer 中的起始偏移
    uint  _ActiveInstanceCount;// 活跃 Instance 数量
CBUFFER_END

// ============================================================================
// 常量（与 C# HUDConstants 对应）
// ============================================================================

#define ELEM_TYPE_AVATAR    0u
#define ELEM_TYPE_ICON      1u
#define ELEM_TYPE_HEALTHBAR 2u
#define ELEM_TYPE_TEXT      3u
#define ELEM_TYPE_FLOATTEXT 4u

#define FLAGS_TYPE_MASK      0x7u
#define FLAGS_VISIBLE_BIT    3
#define FLAGS_AVATAR_SHIFT   4
#define FLAGS_AVATAR_MASK    0xFFu

// ============================================================================
// 工具函数
// ============================================================================

/// 从 flags 中提取元素类型
uint GetElementType(uint flags)
{
    return flags & FLAGS_TYPE_MASK;
}

/// 从 flags 中提取可见性
uint GetVisible(uint flags)
{
    return (flags >> FLAGS_VISIBLE_BIT) & 1u;
}

/// 从 flags 中提取头像 Slice 索引
uint GetAvatarSlice(uint flags)
{
    return (flags >> FLAGS_AVATAR_SHIFT) & FLAGS_AVATAR_MASK;
}

/// GPU 视锥剔除检测
/// 返回 1.0 可见，0.0 不可见（用于退化 Quad 为零面积）
float FrustumCull(float4 clipPos)
{
    float cull = step(-clipPos.w, clipPos.x) * step(clipPos.x, clipPos.w)
               * step(-clipPos.w, clipPos.y) * step(clipPos.y, clipPos.w)
               * step(0.0, clipPos.w);
    return cull;
}

/// 飘血弹跳缩放曲线：sin 波形
float BounceScale(float t, float amplitude)
{
    return 1.0 + amplitude * sin(t * 3.14159265);
}

/// 飘血淡出曲线：最后 30% 线性淡出
float FadeOut(float t)
{
    return 1.0 - saturate((t - 0.7) / 0.3);
}

// ============================================================================
// Vertex/Fragment 数据传递结构
// ============================================================================

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : TEXCOORD1;
    nointerpolation uint flags : TEXCOORD2;
    float  localU : TEXCOORD3;   // 原始 UV.x，用于血条裁剪
    nointerpolation float animTime : TEXCOORD4; // 飘血动画进度
};

#endif // HUD_COMMON_INCLUDED
