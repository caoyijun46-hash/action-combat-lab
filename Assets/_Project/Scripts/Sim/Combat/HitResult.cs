namespace ActionGame.Sim
{
    /// <summary>
    /// 一次命中对受害者的**结算结果**（块 H3 事件流）。
    ///
    /// 判定层（<see cref="PlayerSim.ConsumePendingHit"/>）的三种出口：
    /// 收下、被受击无敌挡掉、被闪避无敌挡掉。事件流把它显式化——
    /// 不再让消费者从三个 flag 里猜发生了什么。
    /// </summary>
    public enum HitResult
    {
        /// <summary>收下：扣血、击退、无敌计时生效，并进入硬直。</summary>
        Accepted,

        /// <summary>被**受击后的无敌帧**挡掉（不是闪避）：完全无事发生。</summary>
        Blocked,

        /// <summary>被**闪避的无敌帧**挡掉 = 完美闪避（会触发慢放）。</summary>
        Dodged,
    }
}
