using ActionGame.Sim;
using UnityEngine;

namespace ActionGame.Data
{
    /// <summary>
    /// <see cref="AttackData"/> 的资产包装：让前摇 / 判定 / 后摇能在 Inspector 里改。
    ///
    /// 它**只做两件事**：序列化字段、把值交出去。不放任何逻辑——
    /// 放逻辑就等于把战斗规则搬到了 Unity 这一侧，铁律 1 就白定了。
    ///
    /// 为什么要有这一层，而不让 <see cref="AttackData"/> 直接继承 <c>ScriptableObject</c>：
    /// <c>ScriptableObject</c> 是 <c>UnityEngine.Object</c>，持有引擎状态（native 对象、实例 ID、
    /// 资源生命周期），按铁律 1 不能进 <c>Sim/</c>。所以数据拆成两半：
    /// 纯数据在 Sim，能编辑的包装在这里。
    ///
    /// 这个文件在播放中改数值会**立刻生效**——`BattleLoop` 每 tick 都重新读一次
    /// <see cref="Data"/>，不是启动时读一次存起来。
    /// </summary>
    [CreateAssetMenu(fileName = "AttackData", menuName = "ActionGame/Attack Data")]
    public class AttackDataAsset : ScriptableObject
    {
        [Tooltip("前摇 tick（60 = 1 秒）/ 判定 tick / 后摇 tick")]
        [SerializeField] private AttackData _data = new AttackData
        {
            StartupTicks = 8,
            ActiveTicks = 6,
            RecoveryTicks = 16,

            // 判定胶囊：从身前 0.6 米到 1.8 米，半径 0.5 米
            HitStartOffset = 0.6f,
            HitEndOffset = 1.8f,
            HitRadius = 0.5f,

            // 打中一下：扣 10 血、推开 1 米
            HitDamage = 10,
            KnockbackDistance = 1f,
        };

        /// <summary>
        /// 值拷贝交出去。返回的是副本，调用方改它不会影响资产上的数值。
        ///
        /// 用属性而不是公开字段，是为了让「谁读了这份数据」在代码里可搜索。
        /// </summary>
        public AttackData Data => _data;

        /// <summary>
        /// 两件事：三段之和为 0、判定胶囊的半径或长度非法。
        /// 都**只警告不修正**——默默改掉等于把配置错误藏起来。
        /// </summary>
        private void OnValidate()
        {
            if (StartupActiveRecoverySum() <= 0)
            {
                Debug.LogWarning(
                    $"{name}: 前摇 + 判定 + 后摇 = 0。攻击会退化成「每 tick 都能接招」，" +
                    "请至少给一段填正数。", this);
            }

            // 这条是「老资产加新字段」的保险。**实测结论：默认情况下不会触发。**
            //
            // 块 8a 给已经存在的 AttackData.asset 加了三个判定胶囊字段，实测发现：
            // .asset 的 YAML 里至今只有旧字段（说明 Unity 没重新序列化它），
            // 而运行时三个新字段拿到了**字段初始化器**给的值。所以 Unity 的顺序是
            // 「先跑初始化器 → 再套用存档数据」，存档里没有的字段保留初始化器的值。
            //
            // 那为什么还留着这条校验：半径 0 的胶囊**永远打不到任何东西、而且不报错**，
            // 是最难查的那种 bug，用三行代码兜一下很划算。它不响正好说明值是好的。
            // （7b 时我以「新字段会静默变 0」为由没把 AttackCancelWindowTicks 搬进来，
            //   那个理由现在看是站不住的。）
            if (_data.HitRadius <= 0f)
            {
                Debug.LogWarning(
                    $"{name}: 判定胶囊半径为 {_data.HitRadius}。这样永远打不到任何东西。" +
                    "如果是给老资产新加了这个字段，请手动填一个值（比如 0.5）。", this);
            }

            if (_data.HitEndOffset < _data.HitStartOffset)
            {
                Debug.LogWarning(
                    $"{name}: 判定胶囊的终点（{_data.HitEndOffset}）比起点（{_data.HitStartOffset}）还近，" +
                    "胶囊方向反了。", this);
            }
        }

        private int StartupActiveRecoverySum()
        {
            return _data.StartupTicks + _data.ActiveTicks + _data.RecoveryTicks;
        }
    }
}
