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
        };

        /// <summary>
        /// 值拷贝交出去。返回的是副本，调用方改它不会影响资产上的数值。
        ///
        /// 用属性而不是公开字段，是为了让「谁读了这份数据」在代码里可搜索。
        /// </summary>
        public AttackData Data => _data;

        /// <summary>
        /// 三段之和为 0 时不阻止运行（Sim 那边会把总时长兜到 1），但必须吵一声——
        /// 那种配置下攻击占不满一个 tick，取消窗口会每 tick 都成立，变成一直连招。
        /// 这里**只警告不修正**：默默改掉等于把配置错误藏起来。
        /// </summary>
        private void OnValidate()
        {
            if (StartupActiveRecoverySum() <= 0)
            {
                Debug.LogWarning(
                    $"{name}: 前摇 + 判定 + 后摇 = 0。攻击会退化成「每 tick 都能接招」，" +
                    "请至少给一段填正数。", this);
            }
        }

        private int StartupActiveRecoverySum()
        {
            return _data.StartupTicks + _data.ActiveTicks + _data.RecoveryTicks;
        }
    }
}
