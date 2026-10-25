namespace ActionGame.Sim
{
    /// <summary>
    /// <see cref="PlayerState.Dodge"/>：闪避，<c>CharacterData.DodgeTicks</c> 个 tick，全程无敌。
    ///
    /// **入口动作在这里**（这是状态实例「有状态」之外的另一半价值）：
    /// <see cref="Enter"/> 调 <c>PlayerSim.StartDodge</c>，给冲量通道一个冲量并设置无敌。
    /// 调度层只负责判断「能不能打断进入」（非硬直、非闪避），进来之后做什么归本状态。
    /// Enter 每次进入只跑一次；同状态再按闪避进不来，所以闪避时长是承诺，狂按不能延长。
    ///
    /// 正常转移：计时走完 → <see cref="Move"/> 或 <see cref="Idle"/>。
    /// </summary>
    public sealed class DodgeState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.Dodge;

        public override void Enter(PlayerSim actor, in PlayerTickContext ctx)
        {
            base.Enter(actor, ctx);
            actor.StartDodge(ctx.Command);
        }

        public override StateTransition Tick(in PlayerTickContext ctx)
        {
            if (ElapsedTicks >= ctx.CharacterData.DodgeTicks - 1)
            {
                return new StateTransition(
                    ctx.HasMoveInput ? PlayerState.Move : PlayerState.Idle);
            }

            return StateTransition.Stay(PlayerState.Dodge);
        }
    }
}
