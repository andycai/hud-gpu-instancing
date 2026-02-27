// ============================================================================
// HUDAtlasManager.cs
// 纹理资源管理：静态 Atlas + 动态头像 Texture2DArray（LRU 缓存）
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace GPUHud
{
    /// <summary>
    /// HUD 纹理资源管理器
    /// 管理静态主 Atlas 和动态头像 Texture2DArray
    /// 头像使用 LRU 淘汰策略，支持 CDN 异步加载
    /// </summary>
    public class HUDAtlasManager : MonoBehaviour
    {
        [Header("Atlas 配置")]
        [SerializeField] private Texture2D _mainAtlas;
        [SerializeField] private string _cdnBaseUrl = "https://cdn.example.com";

        // === 头像 Texture2DArray ===
        private Texture2DArray _avatarArray;
        private int[] _avatarSlotToUID;
        private Dictionary<int, int> _uidToSlot;
        private int _lruCursor;

        // === 加载队列 ===
        private readonly Queue<System.ValueTuple<int, int>> _loadQueue
            = new Queue<System.ValueTuple<int, int>>();
        private int _loadingCount;

        // === 默认占位头像 ===
        private Texture2D _placeholderAvatar;

        /// <summary>主 Atlas 纹理</summary>
        public Texture2D MainAtlas => _mainAtlas;

        /// <summary>头像 Texture2DArray</summary>
        public Texture2DArray AvatarArray => _avatarArray;

        /// <summary>
        /// 初始化
        /// </summary>
        public void Initialize()
        {
            // 创建头像 Texture2DArray
            _avatarArray = new Texture2DArray(
                HUDConstants.AvatarSize,
                HUDConstants.AvatarSize,
                HUDConstants.AvatarMaxSlices,
                TextureFormat.RGBA32,
                false) // 不生成 mipmap
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "HUD_AvatarArray"
            };

            _avatarSlotToUID = new int[HUDConstants.AvatarMaxSlices];
            _uidToSlot = new Dictionary<int, int>(HUDConstants.AvatarMaxSlices);
            _lruCursor = 0;
            _loadingCount = 0;

            // 创建占位头像（灰色）
            CreatePlaceholderAvatar();

            // 用占位头像填充所有 Slice
            for (int i = 0; i < HUDConstants.AvatarMaxSlices; i++)
            {
                Graphics.CopyTexture(_placeholderAvatar, 0, 0, _avatarArray, i, 0);
            }

            // 如果没有设置 MainAtlas，创建一个默认的
            if (_mainAtlas == null)
            {
                CreateDefaultAtlas();
            }
        }

        /// <summary>
        /// 获取或分配头像 Slice 索引
        /// 如果头像未缓存，触发异步加载，先返回占位 slot
        /// </summary>
        /// <param name="uid">玩家唯一 ID</param>
        /// <returns>Texture2DArray 的 slice 索引</returns>
        public int GetAvatarSlice(int uid)
        {
            // 已缓存
            if (_uidToSlot.TryGetValue(uid, out int slot))
                return slot;

            // LRU 淘汰最老的 Slot
            slot = _lruCursor;
            _lruCursor = (_lruCursor + 1) % HUDConstants.AvatarMaxSlices;

            // 清除旧映射
            int oldUID = _avatarSlotToUID[slot];
            if (oldUID != 0)
                _uidToSlot.Remove(oldUID);

            // 建立新映射
            _avatarSlotToUID[slot] = uid;
            _uidToSlot[uid] = slot;

            // 加入加载队列
            _loadQueue.Enqueue((uid, slot));

            return slot;
        }

        /// <summary>
        /// 每帧处理加载队列（分帧加载，避免卡帧）
        /// </summary>
        public void ProcessLoadQueue()
        {
            while (_loadingCount < HUDConstants.AvatarLoadPerFrame && _loadQueue.Count > 0)
            {
                var (uid, slot) = _loadQueue.Dequeue();

                // 验证 slot 仍然属于这个 uid（可能已被淘汰）
                if (_avatarSlotToUID[slot] == uid)
                {
                    StartCoroutine(LoadAvatarCoroutine(uid, slot));
                    _loadingCount++;
                }
            }
        }

        /// <summary>
        /// 异步加载头像
        /// </summary>
        private IEnumerator LoadAvatarCoroutine(int uid, int sliceIndex)
        {
            string url = $"{_cdnBaseUrl}/avatars/{uid}.png";

            using var request = UnityWebRequestTexture.GetTexture(url);
            yield return request.SendWebRequest();

            _loadingCount--;

            if (request.result == UnityWebRequest.Result.Success)
            {
                var tex = DownloadHandlerTexture.GetContent(request);

                // 验证 slot 仍有效
                if (_avatarSlotToUID[sliceIndex] == uid)
                {
                    // GPU 端拷贝，零 GC
                    Graphics.CopyTexture(tex, 0, 0, _avatarArray, sliceIndex, 0);
                }

                // 销毁临时纹理
                Destroy(tex);
            }
        }

        /// <summary>
        /// 直接设置头像纹理（用于本地测试）
        /// </summary>
        public void SetAvatarDirect(int sliceIndex, Texture2D texture)
        {
            if (sliceIndex >= 0 && sliceIndex < HUDConstants.AvatarMaxSlices)
            {
                Graphics.CopyTexture(texture, 0, 0, _avatarArray, sliceIndex, 0);
            }
        }

        /// <summary>
        /// 创建灰色占位头像
        /// </summary>
        private void CreatePlaceholderAvatar()
        {
            _placeholderAvatar = new Texture2D(
                HUDConstants.AvatarSize, HUDConstants.AvatarSize,
                TextureFormat.RGBA32, false);

            var pixels = _placeholderAvatar.GetPixels32();
            var gray = new Color32(128, 128, 128, 255);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = gray;

            _placeholderAvatar.SetPixels32(pixels);
            _placeholderAvatar.Apply(false, true); // makeNoLongerReadable = true
        }

        /// <summary>
        /// 创建默认测试 Atlas（纯白，用于无真实资源时的占位）
        /// </summary>
        private void CreateDefaultAtlas()
        {
            _mainAtlas = new Texture2D(256, 256, TextureFormat.RGBA32, false)
            {
                name = "HUD_DefaultAtlas",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = _mainAtlas.GetPixels32();
            var white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = white;

            _mainAtlas.SetPixels32(pixels);
            _mainAtlas.Apply(false, true);
        }

        private void OnDestroy()
        {
            if (_avatarArray != null)
            {
                Destroy(_avatarArray);
                _avatarArray = null;
            }

            if (_placeholderAvatar != null)
            {
                Destroy(_placeholderAvatar);
                _placeholderAvatar = null;
            }
        }
    }
}
