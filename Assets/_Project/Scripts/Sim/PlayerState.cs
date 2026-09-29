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
    }
}
