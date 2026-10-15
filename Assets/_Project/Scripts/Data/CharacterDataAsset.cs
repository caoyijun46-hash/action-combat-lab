using ActionGame.Sim;
using UnityEngine;

namespace ActionGame.Data
{
    /// <summary>
    /// **一个角色的基础性能文件**（MV3a）：数值半（<see cref="CharacterData"/>）进 Sim；
    /// 角色级动画（Idle / Hitstun 的 clip）从 MV3b 起也住这里，由表现层直接读——
    /// 老规矩「数值问 Sim、资源问 Data」。
    ///
    /// 和 <see cref="AttackDataAsset"/> 同一个模式：只做两件事——序列化字段、把东西交出去。
    /// 播放中改数值**立刻生效**（Boot 每 tick 重新读一次 <see cref="Data"/>）。
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterData", menuName = "ActionGame/Character Data")]
    public class CharacterDataAsset : ScriptableObject
    {
        [SerializeField] private CharacterData _data = new CharacterData
        {
            // 移动：满速 6 / 加速 60（0.1 秒到满速）/ 减速 30（0.2 秒停下）
            MaxMoveSpeed = 6f,
            MoveAcceleration = 60f,
            MoveDeceleration = 30f,

            // 跳跃：8 配重力 30 → 约 1 米高、滞空约半秒
            JumpSpeed = 8f,

            // 身材：1 米高的方块——判定球半径 0.5、中心到脚底 0.5
            Radius = 0.5f,
            HalfHeight = 0.5f,

            // 挨打：硬直 12 tick（0.2 秒）、无敌 30 tick（0.5 秒）——硬直必须 ≤ 无敌
            HitstunTicks = 12,
            InvincibilityTicks = 30,

            // 生命
            MaxHealth = 100,

            // 闪避：20 tick（0.33 秒）全程无敌、冲 3 米（实测约 3.09）
            DodgeTicks = 20,
            DodgeDistance = 3f,

            // 眼睛高度：相机挂点 = 锁定瞄准点（0.5 = 当前手感值）
            CameraHeightOffset = 0.5f,
        };

        [Tooltip("待机循环（MV3b 起住角色文件，原来挂在 ActorAnimation 上）。Move 也用它——" +
                 "移动的观感靠 PlayerView 的倾斜。")]
        [SerializeField] private AnimationClip _idleClip;

        [Tooltip("受击。还没做就留空——留空时表现层借 Idle 顶。")]
        [SerializeField] private AnimationClip _hitstunClip;

        /// <summary>
        /// 待机循环 clip。交给**表现层**（`ActorAnimation` 直接读——依赖表里 `View → Data`
        /// 那条限定边，只读表现资源）。**永不进 Sim**。
        /// </summary>
        public AnimationClip IdleClip => _idleClip;

        /// <summary>
        /// 受击 clip。同样只走表现层；留空是正当配置（表现层借 <see cref="IdleClip"/> 顶）。
        /// </summary>
        public AnimationClip HitstunClip => _hitstunClip;

        /// <summary>
        /// 值拷贝交出去。返回的是副本，调用方改它不会影响资产上的数值。
        /// （和 <see cref="AttackDataAsset.Data"/> 同款。）
        /// </summary>
        public CharacterData Data => _data;

        /// <summary>
        /// 只警告不修正——默默改掉等于把配置错误藏起来（同 AttackDataAsset 的纪律）。
        /// </summary>
        private void OnValidate()
        {
            // 这条是硬约束：硬直比无敌长，会在无敌中途的硬直里重入，连击把角色锁死。
            if (_data.HitstunTicks > _data.InvincibilityTicks)
            {
                Debug.LogWarning(
                    $"{name}: 硬直（{_data.HitstunTicks} tick）比无敌（{_data.InvincibilityTicks} tick）还长——" +
                    "无敌没结束就会被打中、在硬直中途重入，连击会把角色锁死。", this);
            }

            // 半径 ≤ 0 的球永远打不到（和 AttackData 的判定半径校验同一类）；
            // 半高 ≤ 0 落地判定会算到地面以下。
            if (_data.Radius <= 0f || _data.HalfHeight <= 0f)
            {
                Debug.LogWarning(
                    $"{name}: 身材非法（半径 {_data.Radius}、半高 {_data.HalfHeight}）——" +
                    "半径 ≤ 0 永远打不到，半高 ≤ 0 落地判定会出事。", this);
            }
        }
    }
}
