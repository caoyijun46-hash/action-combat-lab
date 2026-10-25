namespace ActionGame.Sim
{
    /// <summary>
    /// 一个角色的**状态实例**基类。
    ///
    /// 每个 <see cref="PlayerSim"/> 在构造时创建自己的一整套状态对象——**不能用静态单例**：
    /// <see cref="ElapsedTicks"/> 是每个角色自己的计时，场上两个角色同时攻击时各走各的。
    /// 状态对象本身只持有「我在这个状态里待了几个 tick」，不持有角色数据、不持有世界引用。
    ///
    /// 计时语义和原来的 <c>PlayerSim.StateTicks</c> 逐位一致：
    ///   进入状态那一 tick 记 0；之后每停留一 tick 在 **tick 末尾** +1。
    /// 所以状态类在 <see cref="Tick"/> 里读到的永远是「自增前」的值——
    /// 判断退出写成 <c>ElapsedTicks &gt;= 时长 - 1</c>（进入 tick 不评估转移）。
    ///
    /// 职责边界：状态类只决定**正常转移**。受击 / 闪避这种全局打断由
    /// <c>PlayerSim.Tick</c> 在本类之外先行处理，不进入这里的 Tick。
    /// </summary>
    public abstract class PlayerStateBase
    {
        /// <summary>这个实例代表哪个状态。状态机用它做数组下标。</summary>
        public abstract PlayerState Id { get; }

        /// <summary>本状态已持续的 tick 数（进入那一 tick 记 0）。</summary>
        public int ElapsedTicks { get; private set; }

        /// <summary>
        /// 进入状态（状态机切换时调一次，同状态 Restart 也算进入）。
        /// 基类只负责清零计时；子类要加进入动作就 override 并先调 <c>base.Enter</c>。
        /// </summary>
        public virtual void Enter(PlayerSim actor, in PlayerTickContext ctx)
        {
            ElapsedTicks = 0;
        }

        /// <summary>离开状态。当前没有状态需要清理，留给以后。</summary>
        public virtual void Exit(PlayerSim actor)
        {
        }

        /// <summary>
        /// 判断这一 tick 的转移。**只读 context，不改 actor**——
        /// 行为（移动 / 物理 / 判定）仍由 <see cref="PlayerSim"/> 统一执行。
        /// </summary>
        public abstract StateTransition Tick(in PlayerTickContext ctx);

        /// <summary>状态机在「留在原状态」时调用，让计时 +1。</summary>
        internal void AdvanceTick()
        {
            ElapsedTicks++;
        }
    }
}
