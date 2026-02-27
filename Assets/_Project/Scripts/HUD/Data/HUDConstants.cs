// ============================================================================
// HUDConstants.cs
// GPU HUD 系统全局常量定义
// ============================================================================

namespace GPUHud
{
    /// <summary>
    /// HUD 系统全局常量
    /// </summary>
    public static class HUDConstants
    {
        // === 容量限制 ===

        /// <summary>最大单位数量</summary>
        public const int MaxUnits = 10000;

        /// <summary>每个单位的平均 Instance 数量</summary>
        public const int InstancesPerUnit = 16;

        /// <summary>最大 Instance 总数</summary>
        public const int MaxInstances = MaxUnits * InstancesPerUnit; // 160,000

        /// <summary>每个 Chunk 包含的 Instance 数量</summary>
        public const int ChunkSize = 256;

        /// <summary>Chunk 总数</summary>
        public const int ChunkCount = MaxInstances / ChunkSize + 1; // 626

        /// <summary>飘血池大小</summary>
        public const int FloatTextPoolSize = 512;

        /// <summary>飘血每个数字最大字符数（如 "99999"）</summary>
        public const int FloatTextMaxDigits = 6;

        // === 头像系统 ===

        /// <summary>头像纹理尺寸</summary>
        public const int AvatarSize = 128;

        /// <summary>头像 Texture2DArray 最大 Slice 数</summary>
        public const int AvatarMaxSlices = 256;

        /// <summary>每帧最大头像加载数</summary>
        public const int AvatarLoadPerFrame = 2;

        // === HUD 布局（像素偏移，以锚点为中心） ===

        /// <summary>头像 Y 偏移</summary>
        public const float AvatarOffsetY = 60f;

        /// <summary>头像大小</summary>
        public const float AvatarDisplaySize = 48f;

        /// <summary>血条 Y 偏移</summary>
        public const float HealthBarOffsetY = 30f;

        /// <summary>血条宽度</summary>
        public const float HealthBarWidth = 64f;

        /// <summary>血条高度</summary>
        public const float HealthBarHeight = 8f;

        /// <summary>名字 Y 偏移</summary>
        public const float NameOffsetY = 12f;

        /// <summary>名字字符大小</summary>
        public const float NameCharSize = 20f;

        /// <summary>图标 Y 偏移</summary>
        public const float IconOffsetY = 80f;

        /// <summary>图标大小</summary>
        public const float IconDisplaySize = 24f;

        // === 名字系统 ===

        /// <summary>最大名字长度（字符数）</summary>
        public const int MaxNameLength = 10;

        // === 飘血动画 ===

        /// <summary>飘血默认持续时间</summary>
        public const float FloatTextDuration = 0.8f;

        /// <summary>飘血上浮高度（NDC 空间）</summary>
        public const float FloatTextRiseHeight = 0.05f;

        /// <summary>飘血弹跳幅度</summary>
        public const float FloatTextBounceScale = 0.3f;

        /// <summary>飘血字符大小</summary>
        public const float FloatTextCharSize = 28f;

        /// <summary>飘血暴击字符大小倍率</summary>
        public const float FloatTextCritScale = 1.5f;

        // === 渲染 ===

        /// <summary>包围盒大小（足够大以覆盖整个世界）</summary>
        public const float BoundsSize = 10000f;

        // === flags 位域偏移 ===

        /// <summary>元素类型位掩码（低 3 位）</summary>
        public const uint FlagsTypeMask = 0x7u;

        /// <summary>可见性位偏移（第 3 位）</summary>
        public const int FlagsVisibleBit = 3;

        /// <summary>头像 Slice 索引位偏移（第 4~11 位）</summary>
        public const int FlagsAvatarSliceShift = 4;

        /// <summary>头像 Slice 索引位掩码</summary>
        public const uint FlagsAvatarSliceMask = 0xFFu;
    }
}
