// ============================================================================
// CharGlyphInfo.cs
// SDF 字体字符 UV 映射数据结构
// ============================================================================

using System.Runtime.InteropServices;
using UnityEngine;

namespace GPUHud
{
    /// <summary>
    /// 单个字符在 SDF Atlas 中的映射信息
    /// 构建时离线生成，运行时只读
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CharGlyphInfo
    {
        /// <summary>Unicode 编码</summary>
        public ushort charCode;

        /// <summary>对齐填充</summary>
        private ushort _padding;

        /// <summary>在 Atlas 中的 UV 区域 (x, y, width, height)</summary>
        public Vector4 uvRect;

        /// <summary>字符前进宽度（像素）</summary>
        public float advance;

        /// <summary>基线 Y 偏移（像素）</summary>
        public float offsetY;

        // 总计: 2 + 2 + 16 + 4 + 4 = 28 bytes

        /// <summary>
        /// 创建字符映射信息
        /// </summary>
        public CharGlyphInfo(char c, Vector4 uv, float adv, float offY)
        {
            charCode = (ushort)c;
            _padding = 0;
            uvRect = uv;
            advance = adv;
            offsetY = offY;
        }
    }
}
