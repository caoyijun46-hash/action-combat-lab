namespace ActionGame.Sim
{
    /// <summary>
    /// <see cref="PlayerState.PoiseBreak"/>：韧性被削到 0 后的固定惩罚窗口，
    /// 时长 <c>CharacterData.PoiseBreakTicks</c>（60 = 1 秒）。
    ///
    /// 入口动作不在本类：破韧那一击由 <c>PlayerSim.ConsumePendingHit</c> 把韧性夹到 0、
    /// 清空无敌，然后调度层强制切换进来。本类只负责计时。
    ///
    /// 出口动作在 <see cref="Exit"/>：韧性回满、回复延迟清零。破韧目前只会因计时结束
    /// 而退出（受击不打断、不延长），所以 Exit 等价于「破韧结束」。
    ///
    /// 计时口径与 <see cref="HitstunState"/> / <see cref="DodgeState"/> 一致：
    /// 进入 tick 记 0、不评估转移；<c>PoiseBreakTicks=60</c> 时在进入后的第 60 tick 退出。
    /// 0 / 负数按最短 1 tick 处理（进入后的下一次 Tick 立即退出）。
    /// </summary>
    public sealed class PoiseBreakState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.PoiseBreak;

        public override void Exit(PlayerSim actor)
        {
            actor.RecoverPoiseAfterBreak();
        }

        public override StateTransition Tick(in PlayerTickContext ctx)
        {
            if (ElapsedTicks >= ctx.CharacterData.PoiseBreakTicks - 1)
            {
                return new StateTransition(
                    ctx.HasMoveInput ? PlayerState.Move : PlayerState.Idle);
            }

            return StateTransition.Stay(PlayerState.PoiseBreak);
        }
    }
}
