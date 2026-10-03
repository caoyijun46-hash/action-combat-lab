namespace ActionGame.Sim
{
    public static class SimConstants
    {
        public const int TicksPerSecond = 60;
        public const float FixedDeltaTime = 1f / TicksPerSecond;

        // ---- 玩家移动 ----
        /// <summary>水平最大速度，米/秒。</summary>
        public const float MaxMoveSpeed = 6f;

        /// <summary>加速度，米/秒²。60 表示 0.1 秒从静止加到满速。</summary>
        public const float MoveAcceleration = 60f;

        /// <summary>减速度，米/秒²。30 表示松键后 0.2 秒停下——要能看出「有减速」而不是瞬停。</summary>
        public const float MoveDeceleration = 30f;

        // ---- 攻击 ----
        // 攻击的**时长**已经不在这里了，搬去了 AttackData（前摇/判定/后摇三段），
        // 由 BattleLoop 每 tick 从数据资产（.asset）读出来传给 PlayerSim.Tick。
        // 这样改手感是改 Inspector 里的数字，不用动代码。

        /// <summary>
        /// 收招窗口：攻击的**最后**这么多 tick 内，按下的攻击键能接出下一招。
        /// 3 tick = 0.05 秒。
        ///
        /// 长度来自 <c>AttackData.TotalTicks</c>（不再是常量），所以判断写成
        /// <c>StateTicks &gt;= attackData.TotalTicks - AttackCancelWindowTicks</c>。
        /// 注意这里**没有** `- 1`——`- 1` 那条在「攻击什么时候结束」的判断里（见 PlayerSim）。
        /// 两个判断长得像但差一，写的时候回到这句对一遍。
        ///
        /// 还留在 SimConstants 而不是进 AttackData，是因为 7b 会把「后摇段」做成真正的阶段，
        /// 到时候这个窗口天然属于后摇段，那时再搬更顺。
        /// </summary>
        public const int AttackCancelWindowTicks = 3;

        /// <summary>
        /// 预输入有效期：攻击中按下的攻击键保留这么多 tick，过期作废。
        /// 12 tick = 0.2 秒。
        ///
        /// 没有这个时限的话，在攻击第 1 tick 按一下，会在第 30 tick 突然又出一招——
        /// 玩家早忘了自己按过。有时限才是「预输入」而不是「粘键」。
        /// 它比 <see cref="AttackCancelWindowTicks"/> 长，所以正常时机按的键不会因为过期而丢。
        /// </summary>
        public const int AttackInputBufferTicks = 12;
    }
}
