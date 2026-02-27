// ============================================================================
// FloatingTextData.cs
// 飘血（浮动伤害/治疗数字）专用 GPU 数据结构
// ============================================================================

using System.Runtime.InteropServices;

namespace GPUHud
{
    /// <summary>
    /// 飘血样式类型
    /// </summary>
    public enum FloatTextStyle : uint
    {
        /// <summary>伤害 - 红色</summary>
        Damage = 0,

        /// <summary>治疗 - 绿色</summary>
        Heal = 1,

        /// <summary>暴击 - 橙色 + 加大字号</summary>
        Crit = 2,
    }

    /// <summary>
    /// 飘血专用数据，通过 StructuredBuffer 传递给 GPU
    /// GPU 端根据 startTime 和 _Time.y 计算动画进度
    /// 16 字节对齐
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FloatingTextData
    {
        /// <summary>触发时间（Time.time，对应 Shader 中的 _Time.y）（4 bytes）</summary>
        public float startTime;

        /// <summary>持续时间，秒（4 bytes）</summary>
        public float duration;

        /// <summary>数值（用于选择字符 UV）（4 bytes）</summary>
        public float value;

        /// <summary>
        /// 样式标记（4 bytes）
        /// [1..0] 颜色类型（FloatTextStyle）
        /// [2]    暴击标记
        /// [31..3] 预留
        /// </summary>
        public uint styleFlags;

        // 总计: 4 + 4 + 4 + 4 = 16 bytes ✓

        /// <summary>
        /// Stride 大小（用于 ComputeBuffer 创建）
        /// </summary>
        public const int Stride = 16;

        /// <summary>
        /// 创建一个已过期的空数据（startTime 为负值，GPU 端自动隐藏）
        /// </summary>
        public static FloatingTextData Expired => new FloatingTextData
        {
            startTime = -999f,
            duration = 0.01f,
            value = 0f,
            styleFlags = 0u
        };
    }
}
