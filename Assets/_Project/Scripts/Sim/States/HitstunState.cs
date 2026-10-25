namespace ActionGame.Sim
{
    /// <summary>
    /// <see cref="PlayerState.Hitstun"/>：受击硬直，<c>CharacterData.HitstunTicks</c> 个 tick。
    ///
    /// 入口不在这里、也没有 Enter 动作：被 <c>PlayerSim.ConsumePendingHit</c> 判定收下后，
    /// 调度层直接 <c>ChangeState</c> 强制切进来（受击是最高优先级的全局打断）。
    ///
    /// 正常转移只有计时退出：走完交还输入 → <see cref="Move"/> 或 <see cref="Idle"/>。
    /// 没有取消窗口、没有预输入——硬直期间的攻击键由 <c>PlayerSim</c> 的记账存进缓冲。
    /// </summary>
    public sealed class HitstunState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.Hitstun;

        public override StateTransition Tick(in PlayerTickContext ctx)
        {
            if (ElapsedTicks >= ctx.CharacterData.HitstunTicks - 1)
            {
                return new StateTransition(
                    ctx.HasMoveInput ? PlayerState.Move : PlayerState.Idle);
            }

            return StateTransition.Stay(PlayerState.Hitstun);
        }
    }
}
