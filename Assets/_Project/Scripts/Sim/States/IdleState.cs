namespace ActionGame.Sim
{
    /// <summary>
    /// <see cref="PlayerState.Idle"/>：没有移动输入。角色可能还在按减速度滑行——
    /// Idle/Move 只看**输入**，不看速度（玩家松手那一刻就回 Idle，不等速度归零）。
    ///
    /// 正常转移只有两条：起手攻击、按方向进入 <see cref="Move"/>。
    /// 受击 / 闪避的打断由 <c>PlayerSim</c> 在调度层先处理，不经过这里。
    /// </summary>
    public sealed class IdleState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.Idle;

        public override StateTransition Tick(in PlayerTickContext ctx)
        {
            if (ctx.HasAttackRequest)
            {
                return new StateTransition(PlayerState.Attack, startsAttack: true);
            }

            if (ctx.HasMoveInput)
            {
                return new StateTransition(PlayerState.Move);
            }

            return StateTransition.Stay(PlayerState.Idle);
        }
    }
}
