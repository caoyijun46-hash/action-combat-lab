namespace ActionGame.Sim
{
    /// <summary>
    /// 每个世界 tick 的命中事件缓冲（块 H3）。
    ///
    /// 和 `InputBuffer` 同一类东西：**固定容量、零分配、消费时机明确**。
    /// <see cref="WorldSim"/> 在每个世界 tick 开头 <see cref="Clear"/>，
    /// 角色在结算命中时 <see cref="TryAdd"/>，Boot 在世界 tick 之后读取。
    ///
    /// 注意「之后」的边界：顿帧 / 慢放跳过的真实 tick 不跑世界 tick，
    /// 所以缓冲里的事件会一直保留到下一次真正推进——HUD 在顿帧期间仍能看到刚发生的命中。
    ///
    /// 溢出策略：满了**拒绝新事件**并计数（<see cref="DroppedCount"/>），不静默、
    /// 不覆盖旧事件——同一 tick 的命中按确定性顺序保留前 N 条，回放也一致。
    /// </summary>
    public sealed class HitEventBuffer
    {
        /// <summary>默认容量。单 tick 最多「每个受害者一条」，32 对当前世界绰绰有余。</summary>
        public const int DefaultCapacity = 32;

        private readonly HitEvent[] _events;

        public HitEventBuffer(int capacity = DefaultCapacity)
        {
            _events = new HitEvent[capacity > 0 ? capacity : 1];
        }

        /// <summary>本 tick 已记录的事件数。</summary>
        public int Count { get; private set; }

        /// <summary>本 tick 因容量满被丢掉的事件数（溢出信号）。</summary>
        public int DroppedCount { get; private set; }

        public HitEvent this[int index] => _events[index];

        /// <summary>追加一条；满了返回 false 并计数。</summary>
        public bool TryAdd(HitEvent hitEvent)
        {
            if (Count >= _events.Length)
            {
                DroppedCount++;
                return false;
            }

            _events[Count] = hitEvent;
            Count++;
            return true;
        }

        /// <summary>每个世界 tick 开头调用（清零计数，不分配）。</summary>
        public void Clear()
        {
            Count = 0;
            DroppedCount = 0;
        }
    }
}
