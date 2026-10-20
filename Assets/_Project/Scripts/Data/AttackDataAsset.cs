using ActionGame.Sim;
using UnityEngine;

namespace ActionGame.Data
{
    /// <summary>
    /// **一个招式的配置文件**：手感数值（<see cref="AttackData"/>）+ 动画 clip 住在一个文件里。
    ///
    /// 文件里的东西分两半、走两条路（见 `AGENTS.md` 依赖表）：
    ///   - **数值**（整招时长 / hitbox 列表 / 伤害 / 击退 / 顿帧）→ <see cref="Data"/>，值拷贝进 Sim；
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
        [Tooltip("整招时长（tick）、判定体列表与命中结算。播放中改这里每 tick 生效。")]
        [SerializeField] private AttackData _data = new AttackData
        {
            // 整招 30 tick = 0.5 秒
            TotalTicks = 30,

            // 打中一下：扣 10 血、按**正前方**推 6.3246 米/秒（= sqrt(2·20·1)，旧「1 米」的精确等价值）、顿 6 tick
            HitDamage = 10,
            KnockbackSpeed = 6.3246f,
            KnockbackDirection = new Vector3(0f, 0f, 1f),
            HitStopTicks = 6,

            // 攻击中移动：0 = 站桩（= 现状）。Play 里把滑块推起来试手感——每招可不同。
            MoveSpeedScale = 0f,

            // 攻击中朝向：以最多 360°/秒 朝相机转（= 锁定追敌同档；0 = 锁死，≥720 ≈ 不锁）
            TurnMaxSpeedDegreesPerSecond = 360f,
        }
        // 默认判定：一个 hitbox，窗口 = 整招第 8~13 tick（含）。
        // 本地坐标以角色根（脚底）为原点：身前 0.6 米到 1.8 米、高度 0.5 米（= 方块中心）、半径 0.5 米。
        // （struct 的字段初始化器不能写 new 数组，所以用 WithHitboxes 带上。）
        .WithHitboxes(new[]
        {
            new HitboxDefinition
            {
                StartTick = 8,
                EndTick = 13,
                LocalFrom = new Vector3(0f, 0.5f, 0.6f),
                LocalTo = new Vector3(0f, 0.5f, 1.8f),
                Radius = 0.5f,
            },
        });

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
        /// 配置校验：整招时长、hitbox 窗口 / 半径、顿帧 / 击退 / 转向 / 移动系数。
        /// 都**只警告不修正**——默默改掉等于把配置错误藏起来。
        /// </summary>
        private void OnValidate()
        {
            if (_data.TotalTicks <= 0)
            {
                Debug.LogWarning(
                    $"{name}: 整招时长是 {_data.TotalTicks} tick。攻击会立刻收招，" +
                    "请至少填 1（60 = 1 秒）。", this);
            }

            if (_data.HitboxCount == 0)
            {
                Debug.LogWarning(
                    $"{name}: 这一招没有任何 hitbox，永远打不到东西。" +
                    "如果是有意做成纯位移 / 纯表现的动作，忽略这条。", this);
            }

            for (int i = 0; i < _data.HitboxCount; i++)
            {
                HitboxDefinition box = _data.GetHitbox(i);

                if (box.StartTick < 0 || box.EndTick < box.StartTick)
                {
                    Debug.LogWarning(
                        $"{name}: 第 {i} 个 hitbox 的窗口 [{box.StartTick}, {box.EndTick}] 非法——" +
                        "起点不能为负、终点不能早于起点（两端都含）。运行时它不会生效。", this);
                }

                if (box.EndTick >= _data.TotalTicks)
                {
                    Debug.LogWarning(
                        $"{name}: 第 {i} 个 hitbox 的结束 tick（{box.EndTick}）超出整招长度" +
                        $"（{_data.TotalTicks}）——超出部分永远不会生效。", this);
                }

                if (box.Radius <= 0f)
                {
                    Debug.LogWarning(
                        $"{name}: 第 {i} 个 hitbox 半径是 {box.Radius}，这样永远打不到任何东西。", this);
                }
            }

            // 负数和 0 一样都是「不顿」（运行时判的是 `> 0`），填负数多半是手滑。
            // **静默失效的配置**要出声。
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
    }
}
