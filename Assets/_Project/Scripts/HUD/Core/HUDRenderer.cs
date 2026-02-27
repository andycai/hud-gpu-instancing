// ============================================================================
// HUDRenderer.cs
// HUD 渲染核心：管理 Mesh、Material、ComputeBuffer，执行 DrawCall
// ============================================================================

using UnityEngine;
using UnityEngine.Rendering;

namespace GPUHud
{
    /// <summary>
    /// HUD 渲染器
    /// 负责创建 Quad Mesh、管理 ComputeBuffer、执行唯一的 DrawMeshInstancedIndirect 调用
    /// </summary>
    public class HUDRenderer : System.IDisposable
    {
        // === 渲染资源 ===
        private Mesh _quadMesh;
        private Material _material;
        private ComputeBuffer _argsBuffer;
        private readonly uint[] _args = new uint[5]; // 预分配，避免每帧 GC
        private Bounds _bounds;

        // === Buffer 引用（由 HUDDataStore 提供） ===
        private ComputeBuffer _instanceBuffer;
        private ComputeBuffer _floatTextBuffer;

        // === Shader 属性 ID 缓存 ===
        private static readonly int PropHUDBuffer = Shader.PropertyToID("_HUDBuffer");
        private static readonly int PropFloatBuffer = Shader.PropertyToID("_FloatBuffer");
        private static readonly int PropSDFThreshold = Shader.PropertyToID("_SDFThreshold");
        private static readonly int PropSDFSoftness = Shader.PropertyToID("_SDFSoftness");
        private static readonly int PropFloatRiseHeight = Shader.PropertyToID("_FloatRiseHeight");
        private static readonly int PropFloatBounceScale = Shader.PropertyToID("_FloatBounceScale");
        private static readonly int PropMainAtlas = Shader.PropertyToID("_MainAtlas");
        private static readonly int PropAvatarArray = Shader.PropertyToID("_AvatarArray");

        /// <summary>
        /// 初始化渲染器
        /// </summary>
        /// <param name="hudShader">HUD/GPUInstanced Shader</param>
        public void Initialize(Shader hudShader)
        {
            // 创建 Quad Mesh（4 顶点，2 三角形）
            CreateQuadMesh();

            // 创建 Material
            _material = new Material(hudShader)
            {
                enableInstancing = true,
                renderQueue = (int)RenderQueue.Overlay
            };

            // 设置默认 Shader 参数
            _material.SetFloat(PropSDFThreshold, 0.5f);
            _material.SetFloat(PropSDFSoftness, 0.05f);
            _material.SetFloat(PropFloatRiseHeight, HUDConstants.FloatTextRiseHeight);
            _material.SetFloat(PropFloatBounceScale, HUDConstants.FloatTextBounceScale);

            // 创建 IndirectArgs Buffer
            _argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            _args[0] = 6; // 每个 Instance 的索引数（Quad = 2三角形 × 3索引）
            _args[1] = 0; // Instance 数量（每帧更新）
            _args[2] = 0; // 起始顶点
            _args[3] = 0; // 起始 Instance
            _args[4] = 0; // 保留

            // 包围盒（足够大以不被引擎剔除）
            _bounds = new Bounds(Vector3.zero, Vector3.one * HUDConstants.BoundsSize);
        }

        /// <summary>
        /// 绑定 ComputeBuffer（由 HUDDataStore 调用）
        /// </summary>
        public void BindBuffers(ComputeBuffer instanceBuffer, ComputeBuffer floatTextBuffer)
        {
            _instanceBuffer = instanceBuffer;
            _floatTextBuffer = floatTextBuffer;
        }

        /// <summary>
        /// 绑定纹理（由 HUDAtlasManager 调用）
        /// </summary>
        public void BindTextures(Texture2D mainAtlas, Texture2DArray avatarArray)
        {
            if (mainAtlas != null)
                _material.SetTexture(PropMainAtlas, mainAtlas);
            if (avatarArray != null)
                _material.SetTexture(PropAvatarArray, avatarArray);
        }

        /// <summary>
        /// 执行渲染（每帧调用一次）
        /// </summary>
        /// <param name="activeInstanceCount">当前活跃的 Instance 数量</param>
        public void Render(int activeInstanceCount)
        {
            if (activeInstanceCount <= 0) return;
            if (_instanceBuffer == null) return;

            // 更新 IndirectArgs（只改 Instance 数量，无 GC）
            _args[1] = (uint)activeInstanceCount;
            _argsBuffer.SetData(_args);

            // 绑定 StructuredBuffer
            _material.SetBuffer(PropHUDBuffer, _instanceBuffer);
            if (_floatTextBuffer != null)
                _material.SetBuffer(PropFloatBuffer, _floatTextBuffer);

            // 发出唯一的 DrawCall！
            Graphics.DrawMeshInstancedIndirect(
                _quadMesh,
                0,              // submeshIndex
                _material,
                _bounds,
                _argsBuffer,
                0,              // argsOffset
                null,           // properties
                ShadowCastingMode.Off,
                false,          // receiveShadows
                0,              // layer
                null            // camera（null = 所有相机）
            );
        }

        /// <summary>
        /// 创建标准 Quad Mesh（-0.5 ~ 0.5）
        /// </summary>
        private void CreateQuadMesh()
        {
            _quadMesh = new Mesh
            {
                name = "HUD_Quad"
            };

            // 4 个顶点，XY 范围 [-0.5, 0.5]
            var vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
            };

            // UV（Y 翻转：适配 Metal/macOS 等 Y-flip 平台）
            // 在这些平台上，投影矩阵翻转了 Y 轴，导致 Quad 顶点位置与 UV 方向不匹配
            // 通过翻转 UV.y 来补偿：底部顶点 v=1（采样纹理顶部），顶部顶点 v=0（采样底部）
            var uvs = new Vector2[]
            {
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
                new Vector2(0f, 0f),
            };

            // 三角形索引（使用 TriangleStrip 替代，但 DrawMeshInstancedIndirect 要求 Triangle）
            var indices = new int[] { 0, 1, 2, 0, 2, 3 };

            _quadMesh.SetVertices(vertices);
            _quadMesh.SetUVs(0, uvs);
            _quadMesh.SetIndices(indices, MeshTopology.Triangles, 0);
            _quadMesh.UploadMeshData(true); // 标记为只读，释放 CPU 端副本

            // 修正：DrawMeshInstancedIndirect 使用顶点数而非索引数
            // Quad 有 6 个索引（2 三角形 × 3）
            // args[0] 应该是索引数而非顶点数
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _argsBuffer?.Release();
            _argsBuffer = null;

            if (_material != null)
            {
                Object.Destroy(_material);
                _material = null;
            }

            if (_quadMesh != null)
            {
                Object.Destroy(_quadMesh);
                _quadMesh = null;
            }
        }
    }
}
