// ============================================================================
// HUDTestScene.cs
// 测试场景：生成 10,000 个单位验证 HUD 系统性能
// ============================================================================

using UnityEngine;

namespace GPUHud.Demo
{
    /// <summary>
    /// HUD 系统测试场景
    /// 在 100×100 网格上生成 10,000 个单位，验证性能目标
    /// </summary>
    [RequireComponent(typeof(HUDSystem))]
    public class HUDTestScene : MonoBehaviour
    {
        [Header("测试配置")]
        [SerializeField] private int _unitCount = 10000;
        [SerializeField] private float _spacing = 3f;
        [SerializeField] private float _yDistance = 10f;
        [SerializeField] private bool _animatePositions = false;
        [SerializeField] private bool _randomDamage = true;
        [SerializeField] private float _damageInterval = 0.1f;

        private HUDSystem _hudSystem;
        private Vector3[] _unitPositions;
        private float _nextDamageTime;
        private int _gridSize;

        private void Start()
        {
            _hudSystem = GetComponent<HUDSystem>();

            // 计算网格大小
            _gridSize = Mathf.CeilToInt(Mathf.Sqrt(_unitCount));
            int actualCount = Mathf.Min(_unitCount, HUDConstants.MaxUnits);

            // 生成单位位置（网格分布）
            _unitPositions = new Vector3[actualCount];
            for (int i = 0; i < actualCount; i++)
            {
                int x = i % _gridSize;
                int z = i / _gridSize;
                _unitPositions[i] = new Vector3(
                    (x - _gridSize * 0.5f) * _spacing,
                    _yDistance,
                    (z - _gridSize * 0.5f) * _spacing
                );
            }

            // 批量注册单位
            _hudSystem.RegisterUnits(_unitPositions, actualCount);

            // 设置随机血量
            for (int i = 0; i < actualCount; i++)
            {
                float hp = Random.Range(0.1f, 1f);
                _hudSystem.UpdateUnitHealth(i, hp);
            }

            Debug.Log($"[HUDTestScene] 已生成 {actualCount} 个单位 HUD，网格 {_gridSize}×{_gridSize}");
        }

        private void Update()
        {
            if (!_animatePositions && !_randomDamage) return;

            // 动态位置更新（模拟移动）
            if (_animatePositions)
            {
                float time = Time.time;
                // 每帧只更新一部分单位（模拟 SLG 场景中少量移动）
                int moveCount = Mathf.Min(100, _unitPositions.Length);
                int startIdx = (int)(time * 10) % _unitPositions.Length;

                for (int i = 0; i < moveCount; i++)
                {
                    int idx = (startIdx + i) % _unitPositions.Length;
                    var pos = _unitPositions[idx];
                    pos.y = Mathf.Sin(time + idx * 0.1f) * 0.5f;
                    _hudSystem.UpdateUnitPosition(idx, pos);
                }
            }

            // 随机伤害飘血
            if (_randomDamage && Time.time >= _nextDamageTime)
            {
                _nextDamageTime = Time.time + _damageInterval;

                // 随机选几个单位产生伤害
                int dmgCount = Random.Range(1, 5);
                for (int i = 0; i < dmgCount; i++)
                {
                    int idx = Random.Range(0, _unitPositions.Length);
                    float value = Random.Range(10f, 9999f);
                    FloatTextStyle style = (FloatTextStyle)Random.Range(0, 3);
                    _hudSystem.SpawnFloatingText(_unitPositions[idx], value, style);
                }
            }
        }
    }
}
