namespace ActionGame.Sim
{
    /// <summary>
    /// <see cref="PlayerState.Attack"/>：整招（<c>AttackData.TotalTicks</c> 个 tick）。
    ///
    /// 两种正常转移：
    ///   1. **取消窗口**（整招最后 <c>GlobalData.AttackCancelWindowTicks</c> 个 tick）
    ///      且有攻击请求 → 接招：Attack → Attack，<c>Restart</c> 让计时归零。
    ///      没有 Restart 的话，第二招会从第一招的进度继续走。
    ///   2. 时长走完 → <see cref="Move"/>（方向键还按着）或 <see cref="Idle"/>。
    ///
    /// 计时口径：本状态在进入 tick 记 0，且进入那一 tick 不评估转移；之后每 tick
    /// 看到的是「自增前」的值。所以：
    ///   第 0..TotalTicks-1 tick 在攻击里；退出判断写 <c>ElapsedTicks &gt;= TotalTicks - 1</c>，
    ///   在最后一个 tick 的**下一次评估**退出（和旧 switch 逐位一致）。
    /// 取消窗口首次生效的 ElapsedTicks = TotalTicks - AttackCancelWindowTicks。
    /// </summary>
    public sealed class AttackState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.Attack;

        public override StateTransition Tick(in PlayerTickContext ctx)
        {
            bool cancelWindowOpen = ElapsedTicks
                >= ctx.AttackData.TotalTicks - ctx.GlobalData.AttackCancelWindowTicks;

            if (cancelWindowOpen && ctx.HasAttackRequest)
            {
                return new StateTransition(
                    PlayerState.Attack, restart: true, startsAttack: true);
            }

            if (ElapsedTicks >= ctx.AttackData.TotalTicks - 1)
            {
                return new StateTransition(
                    ctx.HasMoveInput ? PlayerState.Move : PlayerState.Idle);
            }

            return StateTransition.Stay(PlayerState.Attack);
        }
    }
}
