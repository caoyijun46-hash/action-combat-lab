using ActionGame.Sim;
using UnityEngine;

namespace ActionGame.Data
{
    /// <summary>
    /// **一个招式的配置文件**：手感数值（<see cref="AttackData"/>）+ 动画 clip 住在一个文件里。
    ///
    /// 文件里的东西分两半、走两条路（见 `AGENTS.md` 依赖表）：
    ///   - **数值**（时序 / 判定 / 伤害 / 击退 / 顿帧）→ <see cref="Data"/>，值拷贝进 Sim；
    ///   - **资源引用**（<see cref="AnimationClip"/>）→ 表现层直接读——**永不进 Sim**。
    ///
    /// 它**只做两件事**：序列化字段、把东西交出去。不放任何逻辑——
    /// 放逻辑就等于把战斗规则搬到了 Unity 这一侧，铁律 1 就白定了。
    ///
    /// 为什么要有这一层，而不让 <see cref="AttackData"/> 直接继承 <c>ScriptableObject</c>：
    /// <c>ScriptableObject</c> 是 <c>UnityEngine.Object</c>，持有引擎状态（native 对象、实例 ID、
    /// 资源生命周期），按铁律 1 不能进 <c>Sim/</c>。所以数据拆成两半：
    /// 纯数据在 Sim，能编辑的包装在这里。
    ///
    /// 这个文件在播放中改数值会**立刻生效**——`BattleLoop` 每 tick 都重新读一次
    /// <see cref="Data"/>，不是启动时读一次存起来；换 clip 同理（表现层每帧现读
    /// <see cref="AnimationClip"/>）。
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

            // 打中一下：扣 10 血、按**正前方**推 6.3246 米/秒（= sqrt(2·20·1)，旧「1 米」的精确等价值）、顿 6 tick
            HitDamage = 10,
            KnockbackSpeed = 6.3246f,
            KnockbackDirection = new Vector3(0f, 0f, 1f),
            HitStopTicks = 6,

            // 攻击中移动：0 = 站桩（= 现状）。Play 里把滑块推起来试手感——每招可不同。
            MoveSpeedScale = 0f,

            // 攻击中朝向：以最多 360°/秒 朝相机转（= 锁定追敌同档；0 = 锁死，≥720 ≈ 不锁）
            TurnMaxSpeedDegreesPerSecond = 360f,
        };

        [Tooltip("这一招的动画 clip。留空 = 这一招不播动画（模型停在上一姿势）。")]
        [SerializeField] private AnimationClip _animationClip;

        /// <summary>
        /// 值拷贝交出去。返回的是副本，调用方改它不会影响资产上的数值。
        ///
        /// 用属性而不是公开字段，是为了让「谁读了这份数据」在代码里可搜索。
        /// </summary>
        public AttackData Data => _data;

        /// <summary>
        /// 这一招的动画。交给**表现层**（<c>ActorAnimation</c> 直接读——依赖表里 `View → Data`
        /// 那条限定边，只读表现资源）。
        ///
        /// 它是引擎资源引用（native 对象），和 <see cref="Data"/> 走的不是一条路：
        /// 那半是纯数值、值拷贝进 Sim；这一半永远不进 Sim。
        ///
        /// 表现层每帧现读，所以 Play 里换掉它会**立刻**换动作（和数值一个待遇）。
        /// </summary>
        public AnimationClip AnimationClip => _animationClip;

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

            // 负数和 0 一样都是「不顿」（运行时判的是 `> 0`），填负数多半是手滑。
            // 和上面那条半径校验同一类：**静默失效的配置**要出声。
            if (_data.HitStopTicks < 0)
            {
                Debug.LogWarning(
                    $"{name}: 顿帧时长是负数（{_data.HitStopTicks}），会被当成 0（不顿）。", this);
            }

            // 速度是**大小**，方向有单独的字段（KnockbackDirection）。负数速度 = 反向推，
            // 那是方向字段能干的事，填负数多半是手滑。
            if (_data.KnockbackSpeed < 0f)
            {
                Debug.LogWarning(
                    $"{name}: 击退速度是负数（{_data.KnockbackSpeed}），会被当成反向推。" +
                    "要反向请改 Knockback Direction 字段的符号。", this);
            }

            // 转向速度是**大小**（度/秒），没有「反向转」的语义——运行时会当成 0（= 锁死），
            // 和填 0 一样；填负多半是手滑。和上面几条同类：静默失效的配置要出声。
            if (_data.TurnMaxSpeedDegreesPerSecond < 0f)
            {
                Debug.LogWarning(
                    $"{name}: 攻击中的转向速度是负数（{_data.TurnMaxSpeedDegreesPerSecond}），" +
                    "会被当成 0（锁死朝向）。要锁死请填 0。", this);
            }

            // 移动系数是**打折因子**，只在 0~1 有意义。运行期按原样用（不裁）——
            // 负值 = 倒着走、>1 = 比平时快，多半是手滑。
            if (_data.MoveSpeedScale < 0f || _data.MoveSpeedScale > 1f)
            {
                Debug.LogWarning(
                    $"{name}: 攻击中的移动速度系数是 {_data.MoveSpeedScale}（应为 0~1），" +
                    "会被原样使用（负值 = 倒着走、>1 = 比平时快）。", this);
            }
        }

        private int StartupActiveRecoverySum()
        {
            return _data.StartupTicks + _data.ActiveTicks + _data.RecoveryTicks;
        }
    }
}
