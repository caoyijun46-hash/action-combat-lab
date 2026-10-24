using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// 命中判定的纯数学。全是静态函数，没有状态、没有引擎调用。
    ///
    /// 为什么不用 <c>Physics.OverlapBox</c>：见 `AGENTS.md` 铁律 5。
    /// 一句话——**确定性**。PhysX 的浮点结果跨平台不保证一致，而 `Sim/` 要能回放和联机。
    ///
    /// 为什么用胶囊而不是盒子：球的侧向太胖（能打到不该打的东西）；
    /// 盒子有「角落比面中心远 73%」的方向不均匀问题，而且判定算法要处理旋转（SAT 15 条轴）。
    /// 胶囊的朝向**编码在两个端点的坐标里**，所以下面这些算式完全不碰旋转矩阵。
    ///
    /// 本类用了 <c>Vector3</c> / <c>Mathf</c>，属铁律 1 明确放行的无状态数学类型。
    /// </summary>
    public static class HitTest
    {
        /// <summary>
        /// 胶囊（线段 <paramref name="from"/>→<paramref name="to"/> + 半径）与球是否重叠。
        ///
        /// 判据：**球心到线段的最短距离 ≤ 两个半径之和**。
        /// 两边都取平方再比，省一次开方——判定只需要知道大小关系，不需要真实距离。
        /// </summary>
        public static bool CapsuleOverlapsSphere(
            Vector3 from, Vector3 to, float capsuleRadius,
            Vector3 sphereCenter, float sphereRadius)
        {
            float sum = capsuleRadius + sphereRadius;
            return SqrDistanceToSegment(sphereCenter, from, to) <= sum * sum;
        }

        /// <summary>
        /// 点到线段的**平方**距离。
        ///
        /// 三步：
        /// 1. 把点投影到线段所在的直线上，算出参数 <c>t</c>（0 = 在 from，1 = 在 to）；
        /// 2. **把 t 夹到 [0,1]** —— 这一步是「线段」和「无限长直线」的**唯一**区别。
        ///    不夹的话，点在延长线上 10 米远也会被算成垂足距离 0；
        /// 3. 用夹紧后的 t 求出线段上离点最近的那个点，量平方距离。
        /// </summary>
        public static float SqrDistanceToSegment(Vector3 point, Vector3 from, Vector3 to)
        {
            Vector3 closest = ClosestPointOnSegment(point, from, to);
            return (point - closest).sqrMagnitude;
        }

        /// <summary>
        /// 线段上离点最近的点（参数 p 夹在 [0,1]）。
        ///
        /// 块 H3 起把它单独拆出来：命中事件要用它算**接触点**
        /// （受害者表面朝向攻击者的那一点）。数学和上面原来的内联写法完全相同——
        /// 同样的运算顺序，逐位不变（现有离线锚点可复验）。
        /// </summary>
        public static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 from, Vector3 to)
        {
            Vector3 axis = to - from;
            float axisLengthSqr = axis.sqrMagnitude;

            // 线段退化成一个点（from == to）：直接返回那个点。
            // 不特殊处理的话下面除以 0，得到 NaN，判定会**静默变成永远不命中**。
            if (axisLengthSqr < 1e-12f)
            {
                return from;
            }

            float t = Vector3.Dot(point - from, axis) / axisLengthSqr;
            t = Mathf.Clamp01(t);

            return from + axis * t;
        }
    }
}
