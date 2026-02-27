// ============================================================================
// HUDDataStore.cs
// HUD 数据管理：NativeArray + ComputeBuffer + 分块脏更新
// ============================================================================

using Unity.Collections;
using UnityEngine;

namespace GPUHud
{
    /// <summary>
    /// HUD 数据存储与管理
    /// 维护 CPU 端 NativeArray，按 Chunk 脏标记管理 ComputeBuffer 上传
    /// 所有操作保证 0 GC
    /// </summary>
    public class HUDDataStore : System.IDisposable
    {
        // === CPU 端数据 ===
        private NativeArray<HUDInstanceData> _instanceData;
        private NativeArray<FloatingTextData> _floatTextData;

        // === GPU 端 Buffer ===
        private ComputeBuffer _instanceBuffer;
        private ComputeBuffer _floatTextBuffer;

        // === 脏标记 ===
        private readonly bool[] _chunkDirty;
        private bool _floatTextDirty;

        // === 状态 ===
        private int _activeInstanceCount;
        private int _floatTextNextIndex;

        /// <summary>当前活跃 Instance 数量</summary>
        public int ActiveInstanceCount => _activeInstanceCount;

        /// <summary>Instance ComputeBuffer（供 HUDRenderer 绑定）</summary>
        public ComputeBuffer InstanceBuffer => _instanceBuffer;

        /// <summary>飘血 ComputeBuffer（供 HUDRenderer 绑定）</summary>
        public ComputeBuffer FloatTextBuffer => _floatTextBuffer;

        /// <summary>
        /// 初始化数据存储
        /// </summary>
        public void Initialize()
        {
            // 一次性分配所有 NativeArray（Persistent 生命周期）
            _instanceData = new NativeArray<HUDInstanceData>(
                HUDConstants.MaxTotalInstances, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            _floatTextData = new NativeArray<FloatingTextData>(
                HUDConstants.FloatTextPoolSize, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            // 初始化飘血池为过期状态
            for (int i = 0; i < HUDConstants.FloatTextPoolSize; i++)
            {
                _floatTextData[i] = FloatingTextData.Expired;
            }

            // 创建 GPU ComputeBuffer
            _instanceBuffer = new ComputeBuffer(
                HUDConstants.MaxTotalInstances, HUDInstanceData.Stride);

            _floatTextBuffer = new ComputeBuffer(
                HUDConstants.FloatTextPoolSize, FloatingTextData.Stride);

            // 上传初始飘血数据
            _floatTextBuffer.SetData(_floatTextData);

            _activeInstanceCount = 0;
            _floatTextNextIndex = 0;
            _floatTextDirty = false;
        }

        /// <summary>
        /// 脏标记数组（在构造函数中初始化）
        /// </summary>
        public HUDDataStore()
        {
            // Chunk 数量覆盖全部 Instance（含飘血区域）
            _chunkDirty = new bool[HUDConstants.MaxTotalInstances / HUDConstants.ChunkSize + 1];
        }

        // ====================================================================
        // Instance 数据操作
        // ====================================================================

        /// <summary>
        /// 设置指定索引的 Instance 数据（不会触发上传，需调用 MarkChunkDirty）
        /// </summary>
        public void SetInstanceData(int index, in HUDInstanceData data)
        {
            _instanceData[index] = data;
        }

        /// <summary>
        /// 获取指定索引的 Instance 数据
        /// </summary>
        public HUDInstanceData GetInstanceData(int index)
        {
            return _instanceData[index];
        }

        /// <summary>
        /// 设置活跃 Instance 数量
        /// </summary>
        public void SetActiveInstanceCount(int count)
        {
            _activeInstanceCount = Mathf.Min(count, HUDConstants.MaxTotalInstances);
        }

        /// <summary>
        /// 标记指定 Chunk 为脏
        /// </summary>
        public void MarkChunkDirty(int chunkIndex)
        {
            if (chunkIndex >= 0 && chunkIndex < _chunkDirty.Length)
                _chunkDirty[chunkIndex] = true;
        }

        /// <summary>
        /// 标记指定单位的所有 Instance 所在 Chunk 为脏
        /// </summary>
        public void MarkUnitDirty(int unitIndex)
        {
            int instanceStart = unitIndex * HUDConstants.InstancesPerUnit;
            int chunkIndex = instanceStart / HUDConstants.ChunkSize;
            MarkChunkDirty(chunkIndex);

            // 如果跨 Chunk 边界
            int instanceEnd = instanceStart + HUDConstants.InstancesPerUnit - 1;
            int endChunkIndex = instanceEnd / HUDConstants.ChunkSize;
            if (endChunkIndex != chunkIndex)
                MarkChunkDirty(endChunkIndex);
        }

        /// <summary>
        /// 标记指定索引范围的 Chunk 为脏
        /// </summary>
        public void MarkRangeDirty(int startIndex, int count)
        {
            int startChunk = startIndex / HUDConstants.ChunkSize;
            int endChunk = (startIndex + count - 1) / HUDConstants.ChunkSize;
            for (int i = startChunk; i <= endChunk && i < _chunkDirty.Length; i++)
                _chunkDirty[i] = true;
        }

        /// <summary>
        /// 标记所有 Chunk 为脏（强制全量上传）
        /// </summary>
        public void MarkAllDirty()
        {
            for (int i = 0; i < _chunkDirty.Length; i++)
                _chunkDirty[i] = true;
            _floatTextDirty = true;
        }

        // ====================================================================
        // 飘血数据操作
        // ====================================================================

        /// <summary>
        /// 写入飘血数据（环形缓冲区）
        /// </summary>
        /// <returns>飘血槽位索引</returns>
        public int WriteFloatText(in FloatingTextData data)
        {
            int idx = _floatTextNextIndex;
            _floatTextNextIndex = (idx + 1) % HUDConstants.FloatTextPoolSize;

            _floatTextData[idx] = data;
            _floatTextDirty = true;

            return idx;
        }

        // ====================================================================
        // 上传逻辑（每帧调用）
        // ====================================================================

        /// <summary>
        /// 上传脏 Chunk 到 GPU（0 GC）
        /// </summary>
        public void UploadDirtyChunks()
        {
            for (int i = 0; i < _chunkDirty.Length; i++)
            {
                if (!_chunkDirty[i]) continue;

                int start = i * HUDConstants.ChunkSize;
                int count = Mathf.Min(HUDConstants.ChunkSize, _activeInstanceCount - start);
                if (count <= 0)
                {
                    _chunkDirty[i] = false;
                    continue;
                }

                // NativeArray → ComputeBuffer 部分上传，零 GC
                _instanceBuffer.SetData(_instanceData, start, start, count);
                _chunkDirty[i] = false;
            }

            // 飘血 Buffer 更新
            if (_floatTextDirty)
            {
                _floatTextBuffer.SetData(_floatTextData);
                _floatTextDirty = false;
            }
        }

        // ====================================================================
        // 释放资源
        // ====================================================================

        /// <summary>
        /// 释放所有 Native 和 GPU 资源
        /// </summary>
        public void Dispose()
        {
            if (_instanceData.IsCreated)
                _instanceData.Dispose();
            if (_floatTextData.IsCreated)
                _floatTextData.Dispose();

            _instanceBuffer?.Release();
            _instanceBuffer = null;

            _floatTextBuffer?.Release();
            _floatTextBuffer = null;
        }
    }
}
