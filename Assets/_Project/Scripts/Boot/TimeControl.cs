using ActionGame.Sim;

namespace ActionGame.Boot
{
    /// <summary>
    /// 时间调速器：**顿帧和慢放的计数器 + 「这个真实 tick 要不要跳」的判断**。
    ///
    /// **为什么住在驱动层而不是 Sim**：时钟快慢不是被模拟的世界状态——它不参与确定性计算，
    /// 联机时由服务器连同命中结果同步给各客户端。机制是**跳 tick**（铁律 3 禁止 `Time.timeScale`：
    /// 它会连带改变固定步长与物理，还会污染别人的世界）。Sim 那边只管产出"发生了什么"
    /// （谁挨打了、谁完美闪避了），要不要停、停多久由这里决定。
    ///
    /// **为什么是纯 C# 类**（不是 MonoBehaviour、只 `using Sim`）：离线测试能直接实例化它，
    /// 不用再手工复刻这段骨架——复刻漏一条语义就是一轮假通过 / 假失败（已经踩过两次）。
    ///
    /// 用法：每个**真实 tick** 调一次 <see cref="Advance"/>，按返回的种类决定这一 tick 干什么。
    /// 两种"跳"的输入处理**不一样**（见 <see cref="AdvanceKind"/>），调用方别混。
    /// </summary>
    public sealed class TimeControl
    {
        /// <summary><see cref="Advance"/> 的结论：这个真实 tick 该怎么走。</summary>
        public enum AdvanceKind
        {
            /// <summary>正常推进：调用方照常取帧、推进世界。</summary>
            Run,

            /// <summary>
            /// 顿帧：整个世界停住（相机也停）。调用方要**主动丢掉鼠标位移**（DiscardLook）——
            /// 留着的话解冻后第一帧会把这几 tick 的位移一次性转过去，视角猛跳。
            /// 按键**不能**丢：调用方别取帧，队列会留住它们，解冻后照常投递。
            /// </summary>
            Freeze,

            /// <summary>
            /// 慢放跳过的 tick。和顿帧的区别在**不丢输入**：调用方同样不取帧，
            /// 但鼠标位移也不丢——继续攒着，下一个执行 tick 一起交付（所以相机的转向速度不掉，
            /// 只是步子变大）。铁律：不许用 `Time.timeScale`。
            /// </summary>
            SlowSkip,
        }

        private int _hitStopTicksLeft;      // 真实 tick
        private int _slowMotionTicksLeft;   // 真实 tick

        /// <summary>顿帧还剩几个真实 tick，0 = 没在冻。供调试 HUD 显示。</summary>
        public int HitStopTicksLeft => _hitStopTicksLeft;

        /// <summary>慢放还剩几个真实 tick，0 = 没在慢放。供调试 HUD 显示。</summary>
        public int SlowMotionTicksLeft => _slowMotionTicksLeft;

        /// <summary>
        /// 请求顿帧：接下来这么多真实 tick 全冻。重复请求 = **覆盖**（用新的时长）。
        /// 时长从哪来由调用方决定（现在是**命中记录**里随刀带来的一笔——攻击方招式配置
        /// → 受害者记账 → WorldSim 汇总，H2）。
        /// </summary>
        public void FreezeFor(int ticks)
        {
            _hitStopTicksLeft = ticks;
        }

        /// <summary>
        /// 请求慢放：接下来这么多真实 tick 里按 <c>GlobalData.SlowMotionSkipEvery</c> 跳。
        /// 重复请求 = **刷新**（慢放中再出一次完美闪避就重新数）。
        /// </summary>
        public void SlowFor(int ticks)
        {
            _slowMotionTicksLeft = ticks;
        }

        /// <param name="globalData">
        /// 全局数据（MV3a）：慢放倍率（<c>SlowMotionSkipEvery</c>）从它读，每 tick 现读——
        /// 播放中改全局资产立刻生效。
        /// </param>
        public AdvanceKind Advance(GlobalData globalData)
        {
            // 顿帧优先：它更"硬"（全冻）。而且顿帧期间**慢放的倒计时不递减**——
            // 慢放是"世界慢下来"，冻住的时候连慢放的秒表也停，解冻后接着慢。
            if (_hitStopTicksLeft > 0)
            {
                _hitStopTicksLeft--;
                return AdvanceKind.Freeze;
            }

            if (_slowMotionTicksLeft > 0)
            {
                _slowMotionTicksLeft--;
                if (_slowMotionTicksLeft % globalData.SlowMotionSkipEvery == 0)
                {
                    return AdvanceKind.SlowSkip;
                }
            }

            return AdvanceKind.Run;
        }
    }
}
