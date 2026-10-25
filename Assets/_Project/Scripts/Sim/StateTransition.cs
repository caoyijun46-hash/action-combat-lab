namespace ActionGame.Sim
{
    /// <summary>
    /// 状态机一次评估的**结果**：下一状态 + 两个后果标记。
    ///
    /// 为什么不只返回 <see cref="PlayerState"/>——转移不只是「换状态」：
    ///   <see cref="Restart"/>      同状态要不要重新计时（接招 Attack → Attack）；
    ///   <see cref="StartsAttack"/> 调用方要不要清预输入 + 命中名单（起手 / 接招）。
    ///
    /// 这两个信息只有做出判断的状态类知道，所以必须随结果一起带出来。
    /// 特别注意 <see cref="StartsAttack"/> **不能靠「Next == Attack」推断**：
    /// 攻击中原地停留也是 Next == Attack，但那是「继续这一招」，不是「起新招」。
    /// <see cref="Restart"/> 与 <see cref="StartsAttack"/> 也是两件事——
    /// 前者管计时，后者管输入记账，只是当前规则里恰好同时出现。
    /// </summary>
    public readonly struct StateTransition
    {
        /// <summary>下一状态。</summary>
        public readonly PlayerState Next;

        /// <summary>
        /// 同状态也重入：先 Exit 再 Enter，<see cref="PlayerStateBase.ElapsedTicks"/> 归零。
        /// 目前只有接招（Attack → Attack）用。
        /// </summary>
        public readonly bool Restart;

        /// <summary>
        /// 这一 tick 起了一招。调用方据此清空攻击预输入缓冲和本次挥砍的命中名单。
        /// 它在转移**应用之后**才被读取——「这一 tick 的按键有没有被用掉」只有转移知道。
        /// </summary>
        public readonly bool StartsAttack;

        public StateTransition(PlayerState next, bool restart = false, bool startsAttack = false)
        {
            Next = next;
            Restart = restart;
            StartsAttack = startsAttack;
        }

        /// <summary>留在当前状态，只让计时 +1。</summary>
        public static StateTransition Stay(PlayerState current)
        {
            return new StateTransition(current);
        }
    }
}
