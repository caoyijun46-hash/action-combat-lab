namespace ActionGame.Sim
{
    public struct InputFrame
    {
        public int Tick;
        public float MoveX;
        public float MoveY;
        public float LookX;
        public float LookY;
        public bool Attack;

        /// <summary>
        /// 这一 tick 有没有**新的**跳跃按下。
        ///
        /// 和 Attack 一样是**事件**，但它**没进 `InputBuffer` 队列**——
        /// 那个队列是为「连招意义上的多次按键」准备的（两下 ≠ 一下）；
        /// 跳跃在同一 tick 里被合并成一次没有损失（16ms 内按两下跳，本来也只该跳一次）。
        /// 传递方式：`Update` 置位、`BuildFrame` 取走清零（和 Look 一个套路，只是它是 bool）。
        /// </summary>
        public bool Jump;

        /// <summary>这一 tick 有没有**新的**闪避按下。传递方式和 <see cref="Jump"/> 完全一样（锁存事件）。</summary>
        public bool Dodge;

        /// <summary>
        /// 这一 tick 有没有**新的**锁定键（鼠标中键）按下。传递方式和 <see cref="Jump"/> 一样（锁存事件）。
        /// 它是个 **toggle**：Boot 收到一次就切换一次锁定状态。
        /// 同一 tick 连按两下会被锁存合并成一次（理论上是「切了两次」——16ms 内双击中键，
        /// 当成没发生，可接受）。
        /// </summary>
        public bool LockOn;
    }
}