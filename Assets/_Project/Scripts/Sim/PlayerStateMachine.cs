namespace ActionGame.Sim
{
    /// <summary>
    /// **每个角色一套**的状态机：持有该角色的全部状态实例，负责切换与计时维护。
    ///
    /// 分层（块 S1）：
    ///   - 转移规则住在状态类里（Idle/Move/Attack/Hitstun/Dodge/PoiseBreak 各自的 <c>Tick</c>）；
    ///   - 本类只做「应用」：Exit → 换实例 → Enter，或留在原地让计时 +1；
    ///   - **全局打断不在这里**——破韧优先、受击次之、闪避再次的调度留在 <c>PlayerSim.Tick</c>，
    ///     它直接调 <see cref="ChangeState"/> 强制换状态，不评估原状态的意见。
    ///
    /// 实例化：<see cref="PlayerSim"/> 构造时 new 一个，玩家 / 沙包 / 以后的敌人各自独立。
    /// 状态对象是**有状态**的（<see cref="PlayerStateBase.ElapsedTicks"/>），绝不能跨角色共享。
    /// </summary>
    public sealed class PlayerStateMachine
    {
        /// <summary>
        /// 全部状态实例。**数组下标必须等于 <see cref="PlayerState"/> 的整数值**——
        /// 切换时直接按枚举值索引，顺序错了会静默切到别的状态（黄金轨迹会抓到）。
        /// </summary>
        private readonly PlayerStateBase[] _states;

        /// <summary>当前状态实例（不是枚举——计时在实例里）。</summary>
        public PlayerStateBase Current { get; private set; }

        /// <summary>当前状态已持续的 tick 数，和旧 <c>PlayerSim.StateTicks</c> 同义。</summary>
        public int StateTicks => Current.ElapsedTicks;

        public PlayerStateMachine()
        {
            _states = new PlayerStateBase[]
            {
                new IdleState(),     // PlayerState.Idle    = 0
                new MoveState(),     // PlayerState.Move    = 1
                new AttackState(),   // PlayerState.Attack  = 2
                new HitstunState(),  // PlayerState.Hitstun = 3
                new DodgeState(),    // PlayerState.Dodge   = 4
                new PoiseBreakState(), // PlayerState.PoiseBreak = 5
            };

            // 结构性自检：数组下标必须和枚举值一一对应。
            // 以后加状态时放错次序，在第一个角色构造时就炸——
            // 而不是静默表现成「按闪避结果进了 Idle」这种查半天的错。
            for (int i = 0; i < _states.Length; i++)
            {
                if ((int)_states[i].Id != i)
                {
                    throw new System.InvalidOperationException(
                        $"PlayerStateMachine 的状态实例顺序必须与 PlayerState 枚举一致：" +
                        $"下标 {i} 放的是 {_states[i].Id}");
                }
            }

            // 构造时角色处于默认状态 Idle（和旧实现「枚举默认值 0」一致）。
            // ElapsedTicks 初值就是 0，所以这里不需要模拟一次 Enter。
            Current = _states[(int)PlayerState.Idle];
        }

        /// <summary>
        /// 正常转移：问当前状态「下一步去哪」，然后应用结果。
        /// </summary>
        public StateTransition Tick(PlayerSim actor, in PlayerTickContext ctx)
        {
            StateTransition transition = Current.Tick(ctx);
            return Apply(actor, ctx, transition);
        }

        /// <summary>
        /// 强制切换（全局打断用：破韧 / 受击 / 闪避）。
        /// <paramref name="restart"/> 语义与 <see cref="StateTransition.Restart"/> 相同。
        /// 这条路径不评估原状态的转移，所以返回的 <see cref="StateTransition.StartsAttack"/> 恒为 false。
        /// </summary>
        public StateTransition ChangeState(PlayerState next, bool restart,
            PlayerSim actor, in PlayerTickContext ctx)
        {
            return Apply(actor, ctx, new StateTransition(next, restart));
        }

        private StateTransition Apply(PlayerSim actor, in PlayerTickContext ctx,
            StateTransition transition)
        {
            if (transition.Next == Current.Id && !transition.Restart)
            {
                // 留在原状态：只推进计时。等价于旧 SetState 的 next == State 分支。
                Current.AdvanceTick();
                return transition;
            }

            // 切换（含同状态 Restart）：离开旧实例、进入目标实例，计时由 Enter 清零。
            Current.Exit(actor);
            Current = _states[(int)transition.Next];
            Current.Enter(actor, ctx);
            return transition;
        }
    }
}
