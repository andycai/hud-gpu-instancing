// ============================================================================
// HUDInstanceData.cs
// GPU HUD Instance 数据结构，通过 StructuredBuffer 传递给 GPU
// ============================================================================

using System.Runtime.InteropServices;
using UnityEngine;

namespace GPUHud
{
    /// <summary>
    /// HUD 元素类型枚举
    /// 存储在 flags 的低 3 位
    /// </summary>
    public enum HUDElementType : uint
    {
        /// <summary>头像 → 采样 Texture2DArray</summary>
        Avatar = 0,

        /// <summary>图标 → 采样主 Atlas</summary>
        Icon = 1,

        /// <summary>血条 → 采样主 Atlas + 宽度裁剪</summary>
        HealthBar = 2,

        /// <summary>文字 → 采样 SDF Atlas</summary>
        Text = 3,

        /// <summary>飘血 → 采样 SDF Atlas + GPU 动画</summary>
        FloatText = 4,
    }

    /// <summary>
    /// 每个 HUD Instance 的 GPU 数据
    /// 64 字节对齐，cache-friendly
    /// 通过 StructuredBuffer 传递给 Vertex/Fragment Shader
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HUDInstanceData
    {
        /// <summary>所属单位的世界坐标（12 bytes）</summary>
        public Vector3 worldPosition;

        /// <summary>该元素相对 HUD 锚点的 Y 偏移，像素（4 bytes）</summary>
        public float screenOffsetY;

        /// <summary>该元素相对 HUD 锚点的 X 偏移，像素（4 bytes）</summary>
        public float screenOffsetX;

        /// <summary>该元素的屏幕尺寸，像素（8 bytes）</summary>
        public Vector2 size;

        /// <summary>在 Atlas 中的 UV 区域 (x, y, width, height)（16 bytes）</summary>
        public Vector4 uvRect;

        /// <summary>颜色/透明度（16 bytes）</summary>
        public Color color;

        /// <summary>
        /// 位标记（4 bytes）
        /// [2..0]  elementType (3bit)
        /// [3]     visible (1bit)
        /// [11..4] avatarSliceIndex (8bit)
        /// [31..12] 预留
        /// </summary>
        public uint flags;

        // 总计: 12 + 4 + 4 + 8 + 16 + 16 + 4 = 64 bytes ✓

        /// <summary>
        /// Stride 大小（用于 ComputeBuffer 创建）
        /// </summary>
        public const int Stride = 64;

        /// <summary>
        /// 创建一个不可见的空 Instance（用于初始化）
        /// </summary>
        public static HUDInstanceData Empty => new HUDInstanceData
        {
            worldPosition = Vector3.zero,
            screenOffsetX = 0f,
            screenOffsetY = 0f,
            size = Vector2.zero,
            uvRect = Vector4.zero,
            color = Color.clear,
            flags = 0u // visible=0, type=Avatar
        };

        /// <summary>
        /// 设置元素类型
        /// </summary>
        public void SetType(HUDElementType type)
        {
            flags = (flags & ~HUDConstants.FlagsTypeMask) | (uint)type;
        }

        /// <summary>
        /// 获取元素类型
        /// </summary>
        public HUDElementType GetType()
        {
            return (HUDElementType)(flags & HUDConstants.FlagsTypeMask);
        }

        /// <summary>
        /// 设置可见性
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (visible)
                flags |= (1u << HUDConstants.FlagsVisibleBit);
            else
                flags &= ~(1u << HUDConstants.FlagsVisibleBit);
        }

        /// <summary>
        /// 设置头像 Slice 索引
        /// </summary>
        public void SetAvatarSlice(int sliceIndex)
        {
            flags = (flags & ~(HUDConstants.FlagsAvatarSliceMask << HUDConstants.FlagsAvatarSliceShift))
                  | ((uint)(sliceIndex & 0xFF) << HUDConstants.FlagsAvatarSliceShift);
        }
    }
}
