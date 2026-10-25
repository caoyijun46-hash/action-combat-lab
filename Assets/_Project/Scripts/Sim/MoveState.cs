namespace ActionGame.Sim
{
    /// <summary>
    /// <see cref="PlayerState.Move"/>：有移动输入。
    ///
    /// 正常转移：起手攻击、无输入回 <see cref="Idle"/>。和 Idle 同一条判据——
    /// 输入决定，不拿速度判（否则松手后要等 0.2 秒滑停才肯离开 Move）。
    ///
    /// 行为（目标速度 = 输入方向 × 最大速度）不在这里：状态类只决定去哪，
    /// 速度由 <c>PlayerSim</c> 的行为 switch 统一执行。
    /// </summary>
    public sealed class MoveState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.Move;

        public override StateTransition Tick(in PlayerTickContext ctx)
        {
            if (ctx.HasAttackRequest)
            {
                return new StateTransition(PlayerState.Attack, startsAttack: true);
            }

            if (!ctx.HasMoveInput)
            {
                return new StateTransition(PlayerState.Idle);
            }

            return StateTransition.Stay(PlayerState.Move);
        }
    }
}
