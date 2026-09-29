namespace ActionGame.Inputs
{
    /// <summary>
    /// 「渲染帧采集 → 模拟 tick 消费」的输入事件传送带。
    ///
    /// 采样端：InputSampler.Update() 里做边沿检测，按下瞬间调 PushAttack。
    /// 消费端：BattleLoop.FixedUpdate() 里每 tick 调一次 TryConsumeAttack。
    ///
    /// 不属于模拟状态——不参与 Sim 的确定性计算，联机时天然属于客户端。
    /// 队列只能有一个消费者，两个消费者会互相偷事件。
    /// </summary>
    public sealed class InputBuffer
    {
        /// <summary>
        /// 容量上限。取 2 的幂，让下面的 % 能被 JIT 优化成位与。
        /// 人手按键频率远低于 60Hz，32 在正常游玩下不可能填满；
        /// 一旦填满，说明采样端或 tick 驱动出了问题，DroppedCount 会立刻暴露。
        /// </summary>
        public const int Capacity = 32;

        private readonly int[] _ticks = new int[Capacity];
        private int _head;          // 队首下标
        private int _count;         // 当前积压数量
        private int _droppedCount;  // 因队列满而丢弃的累计次数

        /// <summary>当前积压的、还没被 tick 消费掉的按键数。</summary>
        public int PendingCount => _count;

        /// <summary>累计丢弃数。正常游玩应恒为 0，不为 0 就是有 bug。</summary>
        public int DroppedCount => _droppedCount;

        /// <summary>
        /// 压入一次「刚按下攻击」事件。只在渲染帧的 Update 里调。
        /// </summary>
        /// <param name="tick">采集时刻的 SimClock.Tick。现在只用于调试显示，以后回放/联机靠它对齐。</param>
        public void PushAttack(int tick)
        {
            if (_count >= Capacity)
            {
                // 队列满了：丢新的。先按的顺序不能被破坏，否则连招顺序会乱。
                _droppedCount++;
                return;
            }

            _ticks[(_head + _count) % Capacity] = tick;
            _count++;
        }

        /// <summary>
        /// 取走一个按键事件。每个 tick 最多取一个——这是「不漏不重」的关键：
        /// 狂敲时积压的事件分摊到后续若干 tick 逐个交给模拟层，不会被合并成一次。
        /// </summary>
        public bool TryConsumeAttack(out int tick)
        {
            if (_count == 0)
            {
                tick = 0;
                return false;
            }

            tick = _ticks[_head];
            _head = (_head + 1) % Capacity;
            _count--;
            return true;
        }

        /// <summary>
        /// 清空队列并归零统计。与 SimClock.Reset() 配对，在开局时调用。
        /// 不需要清 _ticks 数组本身：_count 归零后没有元素会被读出，而 int 不持有引用，不阻碍 GC。
        /// </summary>
        public void Clear()
        {
            _head = 0;
            _count = 0;
            _droppedCount = 0;
        }
    }
}
