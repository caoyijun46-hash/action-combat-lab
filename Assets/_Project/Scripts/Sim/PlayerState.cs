namespace ActionGame.Sim
{
    /// <summary>
    /// 玩家的动作状态。
    ///
    /// 这是**模拟层**的概念：它描述「玩家现在处于哪种动作」，由输入决定、供行为分支使用。
    /// 表现层只能读它（比如 HUD 显示），不能写它——状态切换属于战斗逻辑，不属于表现（铁律 1）。
    ///
    /// <see cref="Idle"/> 必须是第一个成员：枚举底层是整数，第一个成员值为 0，
    /// 而字段的默认值就是 0。这样新建出来的 <see cref="PlayerSim"/> 天然处于 Idle，不用额外赋值。
    /// 代价是它同时也是「无法识别的值」的落点——所以 <c>switch</c> 里的 <c>default</c> 要按 Idle 处理。
    /// </summary>
    public enum PlayerState
    {
        /// <summary>没有移动输入。角色可能仍在滑行（减速中），但玩家没在操作方向。</summary>
        Idle,

        /// <summary>有移动输入。目标速度是输入方向乘最大速度。</summary>
        Move,

        /// <summary>
        /// 出招中·**前摇**。从出招到判定出现之间的时间，长度 = <c>AttackData.StartupTicks</c>。
        ///
        /// 三个攻击状态行为相同（都无视移动输入、目标速度恒为 0、按减速度停下），
        /// 分成三个是为了**转移**不同：它们各自的时长独立，后摇段才有取消窗口。
        /// （7b 当时设想「受击打断按段区分，判定段不可打断」——M3b 实现的是**无条件全打断**，
        ///   这个按段区分的规则没有做，是以后可以调的方向。）
        /// </summary>
        AttackStartup,

        /// <summary>
        /// 出招中·**判定**。攻击判定存在的时长，长度 = <c>AttackData.ActiveTicks</c>。
        /// Step 8 的判定框就挂在这一段上。
        /// </summary>
        AttackActive,

        /// <summary>
        /// 出招中·**后摇**。判定消失到能再次行动之间的时间，长度 = <c>AttackData.RecoveryTicks</c>。
        /// 最后 <c>SimConstants.AttackCancelWindowTicks</c> 个 tick 是取消窗口，
        /// 有预输入时会接回 <see cref="AttackStartup"/>；走完则回到
        /// <see cref="Move"/>（方向键还按着）或 <see cref="Idle"/>。
        /// </summary>
        AttackRecovery,

        /// <summary>
        /// **受击硬直**：挨了一下，失去控制 <c>SimConstants.HitstunTicks</c> 个 tick。
        ///
        /// 唯一一个**不由输入进入**的状态——入口是 <c>PlayerSim.ReceiveHit</c>（挨打），
        /// 而且优先级最高：不管当前在哪个状态（包括三个攻击段）都会被它打断。
        ///
        /// 出口：计时走完 → <see cref="Move"/>（方向键还按着）或 <see cref="Idle"/>。
        /// 它**只管「不能动、不能出招」**：被推的那一段走的是独立的击退通道
        /// （<c>PlayerSim.KnockbackVelocity</c>），硬直结束了可能还在滑。
        /// </summary>
        Hitstun,
    }
}
