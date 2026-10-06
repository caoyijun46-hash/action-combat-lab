using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// 一个可被攻击的目标。
    ///
    /// **8b 阶段它只有位置和半径**——不能动、不会掉血。血量和击退是 8c。
    ///
    /// 用 <c>struct</c>：靶子在数组里按值存，判定时直接读，没有间接寻址。
    /// 8c 改它的时候改的是**数组里的那个元素**（数组本身在堆上），所以改动对所有人可见。
    ///
    /// <see cref="Radius"/> 是**把靶子近似成一个球**用的。真正的形状（方块）被丢掉了——
    /// 这是 8a 讨论时定下的取舍：胶囊 vs 球只要 8 行，胶囊 vs 方块要几十行（线段到线段 SAT）。
    /// 代价是立方体的**角落**会被近似掉：边长 1 的方块，内切球半径 0.5、外接球 0.707，
    /// 取哪个都会有偏差。第一版无所谓，记着就行。
    ///
    /// 本类用了 <c>Vector3</c>，属铁律 1 明确放行的无状态数学类型。
    /// </summary>
    public struct TargetSim
    {
        /// <summary>世界坐标下的中心位置，米。</summary>
        public Vector3 Position;

        /// <summary>近似球的半径，米。</summary>
        public float Radius;
    }
}
