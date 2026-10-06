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

        // ---- 判定体：一个胶囊 ----
        // 用胶囊而不是盒子，理由见 AGENTS.md 铁律 5 和 DEVLOG 块 8a：
        // 球太胖（侧向能打到不该打的东西），盒子有「角落比面中心远 73%」的方向不均匀问题。
        // 胶囊的「朝向」编码在**两个端点的坐标**里，所以判定算式完全不碰旋转矩阵。

        /// <summary>判定胶囊起点：从角色位置沿**朝向**往前多少米。</summary>
        public float HitStartOffset;

        /// <summary>判定胶囊终点：从角色位置沿**朝向**往前多少米。一般比起点大。</summary>
        public float HitEndOffset;

        /// <summary>判定胶囊的半径，米。</summary>
        public float HitRadius;

        /// <summary>打中一下扣多少血。</summary>
        public int HitDamage;

        /// <summary>
        /// 打中一下把目标推开多少**米**。
        ///
        /// 用「推开的距离」而不是「初速度」当参数，因为调参的人是按「这一刀把敌人推开多远」想的，
        /// 不是按「初速度几米每秒」。代码里再反推出初速度（见 `PlayerSim.ResolveHits`）。
        /// </summary>
        public float KnockbackDistance;

        /// <summary>
        /// 一次攻击**实际**会占多少个 tick。
        ///
        /// 三段各取「至少 1」再相加，而不是直接相加三段——这不是防御性编程，是描述事实：
        /// 三个攻击状态是三个独立的状态，状态机每 tick 只评估一次转移，
        /// **进入一个状态的那一 tick 必然属于那个状态**。
        /// 所以填 0 的段实际占 1 tick（1/60 秒，感觉不出来），`TotalTicks` 必须照实报，
        /// 否则拿它算进度、算判定帧都会差。
        ///
        /// 每段填 1 和填 0 的效果完全一样。
        /// </summary>
        public int TotalTicks
        {
            get
            {
                return AtLeastOneTick(StartupTicks)
                     + AtLeastOneTick(ActiveTicks)
                     + AtLeastOneTick(RecoveryTicks);
            }
        }

        private static int AtLeastOneTick(int ticks)
        {
            return ticks > 0 ? ticks : 1;
        }
    }
}
