namespace ActionGame.Sim
{
    /// <summary>
    /// 状态类判断转移时能看到的**只读上下文**：这一 tick 的输入意图 + 三份数据。
    ///
    /// 边界（为什么不放更多东西）：
    ///   - **不放 <see cref="PlayerSim"/>**：状态类只回答「下一步去哪」，不执行行为。
    ///     Enter 需要 actor（比如闪避要起冲量）时由状态机单独传，不放进每 tick 的上下文。
    ///   - **不放 <see cref="HitEventBuffer"/>**：事件是命中结算的输出，
    ///     而命中结算发生在 <c>PlayerSim.Tick</c> 的后半段，不属于转移判断。
    ///
    /// 数据都是 struct 值拷贝（<see cref="AttackData"/> 的 hitbox 数组仍是只读共享），
    /// 和 <c>PlayerSim.Tick</c> 的参数纪律一致：状态转移只依赖传进来的这一份。
    /// </summary>
    public readonly struct PlayerTickContext
    {
        /// <summary>这一 tick 的操作意图（闪避方向 / 朝向等都从这里取）。</summary>
        public readonly ActorCommand Command;

        /// <summary>这一 tick 有没有移动输入（已做长度兜底）。</summary>
        public readonly bool HasMoveInput;

        /// <summary>这一 tick 有没有待发的攻击请求（预输入缓冲或刚按下）。</summary>
        public readonly bool HasAttackRequest;

        /// <summary>当前招式数据（整招时长 / 取消窗口等转移判断要用）。</summary>
        public readonly AttackData AttackData;

        /// <summary>当前角色数据（硬直 / 闪避时长等转移判断要用）。</summary>
        public readonly CharacterData CharacterData;

        /// <summary>全局数据（取消窗口长度等系统规则）。</summary>
        public readonly GlobalData GlobalData;

        public PlayerTickContext(
            ActorCommand command,
            bool hasMoveInput,
            bool hasAttackRequest,
            in AttackData attackData,
            in CharacterData characterData,
            in GlobalData globalData)
        {
            Command = command;
            HasMoveInput = hasMoveInput;
            HasAttackRequest = hasAttackRequest;
            AttackData = attackData;
            CharacterData = characterData;
            GlobalData = globalData;
        }
    }
}
