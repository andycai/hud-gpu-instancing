// ============================================================================
// SDFCharLookup.cs
// SDF 字符 UV 查表：排序数组 + 二分查找，0 GC
// ============================================================================

using Unity.Collections;
using UnityEngine;

namespace GPUHud
{
    /// <summary>
    /// SDF 字符 UV 查表
    /// 使用排序的 NativeArray + 二分查找
    /// 比 Dictionary 更 cache-friendly，且无 GC
    /// </summary>
    public class SDFCharLookup : System.IDisposable
    {
        private NativeArray<CharGlyphInfo> _charTable;
        private int _charCount;

        /// <summary>
        /// 后备 UV（字符缺失时使用方块字符）
        /// </summary>
        private Vector4 _fallbackUV = new Vector4(0, 0, 0.01f, 0.01f);

        /// <summary>
        /// 从字符映射数据初始化（需已按 charCode 排序）
        /// </summary>
        public void Initialize(CharGlyphInfo[] sortedGlyphs)
        {
            _charCount = sortedGlyphs.Length;
            _charTable = new NativeArray<CharGlyphInfo>(_charCount, Allocator.Persistent);

            for (int i = 0; i < _charCount; i++)
                _charTable[i] = sortedGlyphs[i];
        }

        /// <summary>
        /// 使用默认数字字符初始化（0~9 + 基础符号）
        /// 临时方案：在没有真实 SDF Atlas 时使用
        /// 将数字均匀分布在 Atlas 的指定区域
        /// </summary>
        public void InitializeWithDefaults(Vector4 digitAtlasRegion)
        {
            // 默认支持 0-9 和 +、- 共 12 个字符
            int count = 12;
            _charCount = count;
            _charTable = new NativeArray<CharGlyphInfo>(count, Allocator.Persistent);

            float charW = digitAtlasRegion.z / count;
            float charH = digitAtlasRegion.w;

            // 数字 0~9
            for (int i = 0; i < 10; i++)
            {
                _charTable[i] = new CharGlyphInfo(
                    (char)('0' + i),
                    new Vector4(
                        digitAtlasRegion.x + i * charW,
                        digitAtlasRegion.y,
                        charW,
                        charH),
                    charW * 512f, // 假设 Atlas 512px
                    0f
                );
            }

            // '+' (charCode 43)
            _charTable[10] = new CharGlyphInfo(
                '+',
                new Vector4(digitAtlasRegion.x + 10 * charW, digitAtlasRegion.y, charW, charH),
                charW * 512f,
                0f
            );

            // '-' (charCode 45)
            _charTable[11] = new CharGlyphInfo(
                '-',
                new Vector4(digitAtlasRegion.x + 11 * charW, digitAtlasRegion.y, charW, charH),
                charW * 512f,
                0f
            );

            // 按 charCode 排序（已经近似有序，但 +/- 需要排到正确位置）
            SortByCharCode();
        }

        /// <summary>
        /// 二分查找字符 UV（O(logN)，0 GC）
        /// </summary>
        public Vector4 GetCharUV(char c)
        {
            ushort code = (ushort)c;
            int lo = 0, hi = _charCount - 1;

            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                ushort midCode = _charTable[mid].charCode;

                if (midCode == code)
                    return _charTable[mid].uvRect;
                else if (midCode < code)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            return _fallbackUV;
        }

        /// <summary>
        /// 获取字符的前进宽度
        /// </summary>
        public float GetCharAdvance(char c)
        {
            ushort code = (ushort)c;
            int lo = 0, hi = _charCount - 1;

            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                ushort midCode = _charTable[mid].charCode;

                if (midCode == code)
                    return _charTable[mid].advance;
                else if (midCode < code)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            return 8f; // 默认宽度
        }

        /// <summary>
        /// 简单插入排序（初始化时一次性调用）
        /// </summary>
        private void SortByCharCode()
        {
            for (int i = 1; i < _charCount; i++)
            {
                var key = _charTable[i];
                int j = i - 1;
                while (j >= 0 && _charTable[j].charCode > key.charCode)
                {
                    _charTable[j + 1] = _charTable[j];
                    j--;
                }
                _charTable[j + 1] = key;
            }
        }

        public void Dispose()
        {
            if (_charTable.IsCreated)
                _charTable.Dispose();
        }
    }
}
