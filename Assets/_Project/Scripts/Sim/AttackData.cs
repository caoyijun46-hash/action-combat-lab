using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// 一次攻击的时序数据：前摇 / 判定 / 后摇各占多少 tick。
    ///
    /// **纯数据**：只有数字和一个派生只读属性，没有任何行为。
    ///
    /// ---- 两处刻意的地方，都是铁律 1 要求的 ----
    ///
    /// 1. 它是 <c>struct</c> 而不是 <c>class</c>。
    ///    数据资产那边交出来的是**值拷贝**，所以 Sim 物理上不可能反向改到磁盘上的 .asset。
    ///    用 class 的话两边共享同一个对象，哪天 Sim 手滑写了字段，改的是资产本身。
    ///
    /// 2. 特性写成 <c>[System.Serializable]</c> 而不是 <c>[Serializable]</c>（前面不加 using）。
    ///    它是 mscorlib 的东西，不是 UnityEngine 的；加它只是为了让 Unity 能在 Inspector 里展开编辑。
    ///
    ///    （2026-10-13 起本文件有 `using UnityEngine;`——<see cref="KnockbackDirection"/> 是
    ///    <c>Vector3</c>，属铁律 1 明确放行的无状态数学类型。原来「零 using」那个标签到此为止；
    ///    引擎**对象**（Transform / ScriptableObject 之类）仍然一个都进不来。）
    /// </summary>
    [System.Serializable]
    public struct AttackData
    {
        /// <summary>前摇，tick。从出招到判定出现之间的时间。</summary>
        public int StartupTicks;

        /// <summary>判定，tick。攻击判定存在的时长。</summary>
        public int ActiveTicks;

        /// <summary>后摇，tick。判定消失到能再次行动之间的时间。</summary>
        public int RecoveryTicks;

        // ---- 判定体：一个胶囊 ----
        // 用胶囊而不是盒子，理由见 AGENTS.md 铁律 5 和 DEVLOG 块 8a：
        // 球太胖（侧向能打到不该打的东西），盒子有「角落比面中心远 73%」的方向不均匀问题。
        // 胶囊的「朝向」编码在**两个端点的坐标**里，所以判定算式完全不碰旋转矩阵。

        /// <summary>判定胶囊起点：从角色位置沿**朝向**往前多少米。</summary>
        public float HitStartOffset;

        /// <summary>判定胶囊终点：从角色位置沿**朝向**往前多少米。一般比起点大。</summary>
        public float HitEndOffset;

        /// <summary>判定胶囊的半径，米。</summary>
        public float HitRadius;

        /// <summary>打中一下扣多少血。</summary>
        public int HitDamage;

        /// <summary>
        /// 打中一下给目标多少**速度**，米/秒（沿 <see cref="KnockbackDirection"/> 的方向）。
        ///
        /// 2026-10-13 从「距离」改成「速度」（原 KnockbackDistance，单位米）。为什么改：
        /// 距离参数化的底层是 <c>v = sqrt(2aD)</c>——**把衰减率 a 预补偿进了初速度**，
        /// 于是「滑多远」与 a 无关（a 被约掉，只管「滑多久」）。单通道时代这笔交换换来
        /// 「填米得米」，很值；但现在冲量（水平，20 m/s²）和重力（竖直，30 m/s²）是两条通道，
        /// 一个「距离」补偿不了两边（跨通道会平方缩水），而且「每招不同衰减率」一落地，
        /// D 就说不清代表什么。**速度是唯一在两条通道上含义一致的量**：招式交付
        /// 「加多少速度」（运动学），世界交付「这个速度怎么衰减、落地」（动力学）。
        ///
        /// 默认 6.3246 = sqrt(2·20·1)，是旧「1 米」的精确等价值——老资产行为逐位不变。
        /// </summary>
        public float KnockbackSpeed;

        /// <summary>
        /// 击退方向，**攻击者本地坐标**（x = 右 / y = 上 / z = 前，即 Unity 里角色朝 +Z 时的本地向量）。
        /// 写懒值（如 <c>(0.3, 0.6, 1)</c>），**用时自动归一化**。
        ///
        /// 水平分量走**冲量通道**（滑行），y 分量走**竖直弹道通道**（像跳跃，重力接管弧线）；
        /// y = 0 表示**不碰竖直**（空中挨一刀不会清零下坠速度）；**方向全零 = 这一招不推**。
        ///
        /// 默认 (0, 0, 1) = 朝攻击者正前方推。注意这和旧的「径向远离攻击者」不同：
        /// 斜着命中**不再侧推**，推的方向完全由配置说了算（作者化，2026-10-13 用户定）。
        /// </summary>
        public Vector3 KnockbackDirection;

        /// <summary>
        /// 打中一下让**整个世界**冻住多少 tick（顿帧）。0 = 关掉。
        ///
        /// 它**不参与** <see cref="TotalTicks"/>：顿帧不推进招式的 tick 时间轴
        /// （`PlayerSim.Tick` 整个不跑，`StateTicks` 不动），只是把真实时间拉长。
        /// 所以三段时长加起来仍是 `TotalTicks`，顿帧是在这之外**额外**占的真实时间。
        /// 6 tick = 0.1 秒，是「轻轻一顿」的常见量级。
        /// </summary>
        public int HitStopTicks;

        // ---- 攻击中的移动 ----

        /// <summary>
        /// 攻击中的**移动速度系数**（0~1，块 MV2，2026-10-15）。
        ///
        /// 三个攻击状态里，目标移动速度 = `方向 × MaxMoveSpeed × 它`：
        ///   0   = 站桩（= 旧行为，逐位复现——目标被 ×0 乘成零，自动走「刹车」一侧）；
        ///   1   = 攻击中移动和平时一样（除朝向仍归 <see cref="TurnMaxSpeedDegreesPerSecond"/>）；
        ///   0.5 = 半速（3 m/s）。半推摇杆与系数是**连乘**（半推 + 0.5 → 1.5 m/s）。
        ///
        /// 移动方向仍是常规的相机相对方向（含锁定防甩保持）；**朝向是另一根旋钮**——
        /// 所以系数 > 0 时会「面朝一边、横着滑」，那是预期。
        ///
        /// rate 的口径（用户 2026-10-15 确认）：判据是**目标速度是不是零**——非零 → 加速度 60
        /// （和平时开车同款）；为零（没输入 或 系数 = 0）→ 减速度 30。**不是**「有没有输入」：
        /// 系数 = 0 时输入会被乘掉、目标其实是零，只看输入会错用加速度去刹车（刹停时间砍半，
        /// 破「0 = 现状」）。实现上直接判 <c>targetVelocity.sqrMagnitude &gt; 0</c>，条件即语义。
        /// </summary>
        [Tooltip("攻击中的移动速度系数：0 = 站桩，1 = 和平时一样。半推摇杆 × 它（连乘）")]
        [Range(0f, 1f)]
        public float MoveSpeedScale;

        // ---- 攻击中的朝向 ----

        /// <summary>
        /// 攻击中朝向的**最大旋转速度**，度/秒（块 F1，2026-10-15）。
        ///
        /// 语义：**纯攻击状态**中，朝向每 tick 以最多 `它 × dt` 朝命令的 `FacingYaw`
        /// （玩家路径 = 相机 yaw）转——最短方向、一步不超剩余误差（和相机锁定的 TurnStep
        /// 同款写法，落地即吸附）。进/出攻击那一 tick 仍是**瞬跟**（起手对齐、收招跟上），
        /// 所以攻击之外不留滞后。
        ///
        /// 两个端点都有精确含义：
        ///   0  = 锁死朝向（= 旧行为，逐位复现）；
        ///   ≥ 相机鼠标上限（720）= 约等于不锁。每招一个数：重击慢、刺击快，配的就是这里。
        ///
        /// 已知后果（用户 2026-10-15 拍板接受）：判定帧里判定胶囊会以这个速度慢慢转——
        /// 原来锁朝向的理由就是「挥到一半判定换方向」，现在换成「限速地转」。
        /// 不想让它转的招填 0。将来要「前摇可瞄 / 判定锁 / 后摇自由」的分段控制，
        /// 是再加字段的事，不是改结构的事。
        /// </summary>
        public float TurnMaxSpeedDegreesPerSecond;

        /// <summary>
        /// 判定段的第一个 tick 在「整招时间轴」上的位置。
        ///
        /// 和 <see cref="TotalTicks"/> 用**同一套**「每段至少 1」的规则——填 0 的前摇实际占 1 tick，
        /// 这里必须照实算，否则 View 把「状态 + StateTicks」翻译成「整招第几 tick」时会和下一段撞号。
        /// </summary>
        public int ActiveStartTick => AtLeastOneTick(StartupTicks);

        /// <summary>后摇段的第一个 tick 在「整招时间轴」上的位置。见 <see cref="ActiveStartTick"/>。</summary>
        public int RecoveryStartTick => ActiveStartTick + AtLeastOneTick(ActiveTicks);

        /// <summary>
        /// 一次攻击**实际**会占多少个 tick。
        ///
        /// 三段各取「至少 1」再相加，而不是直接相加三段——这不是防御性编程，是描述事实：
        /// 三个攻击状态是三个独立的状态，状态机每 tick 只评估一次转移，
        /// **进入一个状态的那一 tick 必然属于那个状态**。
        /// 所以填 0 的段实际占 1 tick（1/60 秒，感觉不出来），`TotalTicks` 必须照实报，
        /// 否则拿它算进度、算判定帧都会差。
        ///
        /// 每段填 1 和填 0 的效果完全一样。
        /// </summary>
        public int TotalTicks
        {
            get
            {
                return AtLeastOneTick(StartupTicks)
                     + AtLeastOneTick(ActiveTicks)
                     + AtLeastOneTick(RecoveryTicks);
            }
        }

        private static int AtLeastOneTick(int ticks)
        {
            return ticks > 0 ? ticks : 1;
        }
    }
}
