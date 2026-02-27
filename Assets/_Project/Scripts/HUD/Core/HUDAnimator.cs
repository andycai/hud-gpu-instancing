// ============================================================================
// HUDAnimator.cs
// 飘血动画管理：环形缓冲区 + GPU 时间驱动
// ============================================================================

using UnityEngine;

namespace GPUHud
{
    /// <summary>
    /// 飘血动画管理器
    /// 管理飘血的触发、数字拆分为字符 Instance、生命周期
    /// 动画本身在 GPU 端由 _Time.y 驱动
    /// </summary>
    public class HUDAnimator
    {
        private readonly HUDDataStore _dataStore;
        private readonly SDFCharLookup _charLookup;

        // 飘血 Instance 的起始索引（紧接在常规 Instance 之后）
        private int _floatTextInstanceStart;

        // 每条飘血消息占用的 Instance 数（最多 6 个数字字符）
        private const int InstancesPerFloatText = HUDConstants.FloatTextMaxDigits;

        // 飘血颜色表
        private static readonly Color DamageColor = new Color(1f, 0.2f, 0.1f, 1f);
        private static readonly Color HealColor = new Color(0.2f, 1f, 0.3f, 1f);
        private static readonly Color CritColor = new Color(1f, 0.6f, 0.1f, 1f);

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dataStore">数据存储引用</param>
        /// <param name="charLookup">SDF 字符查表引用</param>
        /// <param name="floatTextInstanceStart">飘血 Instance 在总 Buffer 中的起始索引</param>
        public HUDAnimator(HUDDataStore dataStore, SDFCharLookup charLookup, int floatTextInstanceStart)
        {
            _dataStore = dataStore;
            _charLookup = charLookup;
            _floatTextInstanceStart = floatTextInstanceStart;
        }

        /// <summary>
        /// 触发一次飘血效果
        /// </summary>
        /// <param name="worldPos">世界坐标</param>
        /// <param name="value">数值（伤害/治疗量）</param>
        /// <param name="style">样式</param>
        public void SpawnFloatingText(Vector3 worldPos, float value, FloatTextStyle style)
        {
            float currentTime = Time.time;

            // 1. 写入飘血数据（环形缓冲区）
            var floatData = new FloatingTextData
            {
                startTime = currentTime,
                duration = HUDConstants.FloatTextDuration,
                value = value,
                styleFlags = (uint)style
            };
            int slotIndex = _dataStore.WriteFloatText(floatData);

            // 2. 选择颜色和大小
            Color textColor;
            float charSize;
            switch (style)
            {
                case FloatTextStyle.Heal:
                    textColor = HealColor;
                    charSize = HUDConstants.FloatTextCharSize;
                    break;
                case FloatTextStyle.Crit:
                    textColor = CritColor;
                    charSize = HUDConstants.FloatTextCharSize * HUDConstants.FloatTextCritScale;
                    break;
                default: // Damage
                    textColor = DamageColor;
                    charSize = HUDConstants.FloatTextCharSize;
                    break;
            }

            // 3. 数字拆分为字符
            int intValue = Mathf.Abs(Mathf.RoundToInt(value));
            // 临时数字缓冲区（栈上，0 GC）
            int digitCount = 0;
            // 使用 stackalloc 风格的固定数组
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0, d4 = 0, d5 = 0;

            if (intValue == 0)
            {
                d0 = 0;
                digitCount = 1;
            }
            else
            {
                int tmp = intValue;
                // 先计算位数
                while (tmp > 0 && digitCount < InstancesPerFloatText)
                {
                    int digit = tmp % 10;
                    switch (digitCount)
                    {
                        case 0: d0 = digit; break;
                        case 1: d1 = digit; break;
                        case 2: d2 = digit; break;
                        case 3: d3 = digit; break;
                        case 4: d4 = digit; break;
                        case 5: d5 = digit; break;
                    }
                    tmp /= 10;
                    digitCount++;
                }
            }

            // 4. 为每个数字字符创建 HUD Instance
            int baseInstanceIndex = _floatTextInstanceStart + slotIndex * InstancesPerFloatText;
            float totalWidth = digitCount * charSize * 0.6f; // 字符宽度约为 0.6 倍大小
            float startX = -totalWidth * 0.5f;

            for (int i = 0; i < InstancesPerFloatText; i++)
            {
                var instance = HUDInstanceData.Empty;

                if (i < digitCount)
                {
                    // 获取第 (digitCount - 1 - i) 个数字（反转顺序）
                    int digit;
                    int ri = digitCount - 1 - i;
                    switch (ri)
                    {
                        case 0: digit = d0; break;
                        case 1: digit = d1; break;
                        case 2: digit = d2; break;
                        case 3: digit = d3; break;
                        case 4: digit = d4; break;
                        case 5: digit = d5; break;
                        default: digit = 0; break;
                    }

                    char c = (char)('0' + digit);
                    Vector4 charUV = _charLookup.GetCharUV(c);

                    instance.worldPosition = worldPos;
                    instance.screenOffsetX = startX + i * charSize * 0.6f;
                    instance.screenOffsetY = HUDConstants.HealthBarOffsetY + 20f; // 血条上方
                    instance.size = new Vector2(charSize, charSize);
                    instance.uvRect = charUV;
                    instance.color = textColor;
                    // 将 startTime 编码到 color.a 中，供 Shader 读取
                    instance.color = new Color(textColor.r, textColor.g, textColor.b, currentTime);
                    instance.SetType(HUDElementType.FloatText);
                    instance.SetVisible(true);
                }
                // else: 保持 Empty（不可见）

                _dataStore.SetInstanceData(baseInstanceIndex + i, instance);
            }

            // 5. 标记所在 Chunk 为脏
            _dataStore.MarkRangeDirty(baseInstanceIndex, InstancesPerFloatText);
        }

        /// <summary>
        /// 更新飘血 Instance 起始索引
        /// </summary>
        public void SetFloatTextInstanceStart(int start)
        {
            _floatTextInstanceStart = start;
        }
    }
}
