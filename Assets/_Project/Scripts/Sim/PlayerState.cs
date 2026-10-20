namespace ActionGame.Sim
{
    /// <summary>
    /// 动作状态。
    ///
    /// 这是**模拟层**的概念：它描述「现在处于哪种动作」，由输入决定、供行为分支使用。
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
        /// **出招中（整招）**。长度 = <c>AttackData.TotalTicks</c>；
        /// 判定窗口由招式里的 hitbox 数据定义——块 T1（2026-10-20）把原来的
        /// 前摇 / 判定 / 后摇三个状态合并成了这一个（三段的行为本来就完全一致，
        /// 唯一的段差异「判定只在 Active」由 hitbox 的 tick 窗口接管）。
        ///
        /// 最后 <c>GlobalData.AttackCancelWindowTicks</c> 个 tick 是取消窗口，
        /// 有预输入时**重新起这一招**（同状态重置计时）；走完则回到
        /// <see cref="Move"/>（方向键还按着）或 <see cref="Idle"/>。
        ///
        /// 行为细节（移动打几折、转身多快）由招式数据说了算——
        /// AttackData 的移动系数（MV2）和转向上限（F1）。
        /// </summary>
        Attack,

        /// <summary>
        /// **受击硬直**：挨了一下，失去控制 <c>CharacterData.HitstunTicks</c> 个 tick。
        ///
        /// 唯一一个**不由输入进入**的状态——入口是 <c>PlayerSim.ReceiveHit</c>（挨打），
        /// 而且优先级最高：不管当前在哪个状态（包括 <see cref="Attack"/>）都会被它打断。
        ///
        /// 出口：计时走完 → <see cref="Move"/>（方向键还按着）或 <see cref="Idle"/>。
        /// 它**只管「不能动、不能出招」**：被推的那一段走的是独立的冲量通道
        /// （<c>PlayerSim.ImpulseVelocity</c>），硬直结束了可能还在滑。
        /// </summary>
        Hitstun,

        /// <summary>
        /// **闪避**：冲一段距离，全程无敌（<c>CharacterData.DodgeTicks</c> 个 tick）。
        ///
        /// 和跳跃不同——它是**新动词**，所以是一个状态（跳跃只是竖直通道上的一次冲量，不进状态机）：
        ///   - 入口由输入驱动（<c>ActorCommand.Dodge</c>），能从任何非硬直状态**打断**进入
        ///     （连招式都能取消——这是它作为「新动词」的份量所在）；
        ///   - 位移走**冲量通道**（距离参数化），自己的 <see cref="Move"/> 速度照常归零；
        ///   - 出口：计时走完 → <see cref="Move"/>（方向键还按着）或 <see cref="Idle"/>。
        /// </summary>
        Dodge,
    }
}
