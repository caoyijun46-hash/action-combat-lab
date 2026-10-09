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
    }
}