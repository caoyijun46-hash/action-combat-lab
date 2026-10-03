namespace ActionGame.Sim
{
    /// <summary>
    /// 一次攻击的时序数据：前摇 / 判定 / 后摇各占多少 tick。
    ///
    /// **纯数据**：只有数字和一个派生只读属性，没有任何行为。
    ///
    /// ---- 两处刻意的地方，都是铁律 1 要求的 ----
    ///
    /// 1. 它是 <c>struct</c> 而不是 <c>class</c>。
    ///    数据资产那边交出来的是**值拷贝**，所以 Sim 物理上不可能反向改到磁盘上的 .asset。
    ///    用 class 的话两边共享同一个对象，哪天 Sim 手滑写了字段，改的是资产本身。
    ///
    /// 2. 特性写成 <c>[System.Serializable]</c> 而不是 <c>[Serializable]</c>（前面不加 using）。
    ///    它是 mscorlib 的东西，不是 UnityEngine 的，加它只是为了让 Unity 能在 Inspector 里
    ///    展开编辑。这样本文件可以保持**零 using**，和 `SimConstants` / `PlayerState` 一样——
    ///    「Sim 不认识 Unity」这件事看一眼文件头就能确认，不用去查 using 列表。
    /// </summary>
    [System.Serializable]
    public struct AttackData
    {
        /// <summary>前摇，tick。从出招到判定出现之间的时间。</summary>
        public int StartupTicks;

        /// <summary>判定，tick。攻击判定存在的时长。</summary>
        public int ActiveTicks;

        /// <summary>后摇，tick。判定消失到能再次行动之间的时间。</summary>
        public int RecoveryTicks;

        /// <summary>
        /// 一次攻击的总时长，tick。**最小返回 1**。
        ///
        /// 下限放在这里而不是指望资产配置正确，是因为 Sim 不该能被错误数据搞坏：
        /// 总时长为 0 的话攻击占不满一个 tick，取消窗口那一堆 <c>StateTicks &gt;= 总时长 - N</c>
        /// 会立刻为真，变成「每 tick 都接招」的退化状态。
        /// 但这里只是不让它崩，**配置得不合理仍然要报警**——那个由 <c>AttackDataAsset.OnValidate</c> 做。
        /// </summary>
        public int TotalTicks
        {
            get
            {
                int total = StartupTicks + ActiveTicks + RecoveryTicks;
                return total > 0 ? total : 1;
            }
        }
    }
}
