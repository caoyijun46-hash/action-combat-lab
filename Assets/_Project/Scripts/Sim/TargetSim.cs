using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// 一个可被攻击的目标：有位置、有血量、会被击退。
    ///
    /// 用 <c>struct</c>：靶子在数组里按值存，判定和推进都直接读，没有间接寻址。
    /// 改它的时候改的是**数组里的那个元素**（数组本身在堆上），所以改动对所有人可见。
    /// 注意这一点对 struct 很关键——如果它是某个方法的局部副本，改了没人看得见。
    ///
    /// <see cref="Radius"/> 是**把靶子近似成一个球**用的。真正的形状（方块）被丢掉了——
    /// 这是 8a 讨论时定下的取舍：胶囊 vs 球只要 8 行，胶囊 vs 方块要几十行。
    /// 代价是立方体的**角落**会被近似掉：边长 1 的方块，内切球半径 0.5、外接球 0.707。
    ///
    /// 本类用了 <c>Vector3</c> / <c>Mathf</c>，属铁律 1 明确放行的无状态数学类型。
    /// </summary>
    public struct TargetSim
    {
        /// <summary>世界坐标下的中心位置，米。</summary>
        public Vector3 Position;

        /// <summary>
        /// 上一个 tick 的位置，供 View 做**渲染插值**用。
        ///
        /// 和 <see cref="PlayerSim.PreviousPosition"/> 是同一件事、同一个理由：
        /// 击退是 60 tick/秒推进的，而屏幕可以是 100+ 帧/秒，直接显示最新位置会一顿一顿的。
        /// Step 4 在玩家身上踩过一次，这里是同一个坑的第二次。
        /// </summary>
        public Vector3 PreviousPosition;

        /// <summary>击退速度，米/秒。命中时被赋值，之后每 tick 按减速度衰减到零。</summary>
        public Vector3 KnockbackVelocity;

        /// <summary>近似球的半径，米。</summary>
        public float Radius;

        /// <summary>血量上限。留着是为了让外部能显示「90/100」这种进度。</summary>
        public int MaxHealth;

        /// <summary>当前血量。**会被夹在 0 以上**——这一块不做死亡，但血量不该是负的。</summary>
        public int Health;

        /// <summary>
        /// 挨一下：扣血 + 给一个击退初速度。
        ///
        /// 扣血夹在 0：这一块**不处理死亡**（没有销毁、没有死亡动画），但显示负血量很怪，
        /// 所以夹住。真正处理死亡是后面的事。
        /// </summary>
        public void ApplyHit(int damage, Vector3 knockbackVelocity)
        {
            Health -= damage;
            if (Health < 0) { Health = 0; }

            KnockbackVelocity = knockbackVelocity;
        }

        /// <summary>
        /// 推进一个 tick：先记旧位置，再按击退速度移动，然后把速度往零衰减。
        ///
        /// 和 <see cref="PlayerSim"/> 用的是同一套运动学写法（<c>MoveTowards</c> 而不是 <c>Lerp</c>），
        /// 不是为了统一而统一——<c>Lerp</c> 是指数衰减，永远到不了零，
        /// 靶子会以极小的速度永远飘下去。
        /// </summary>
        public void Tick(float dt)
        {
            PreviousPosition = Position;

            // 速度已经是零的话整个跳过。绝大多数 tick 都走这一条——
            // 靶子只在被击退的那十几个 tick 里动，其余时间完全静止。
            if (KnockbackVelocity.sqrMagnitude <= 0f) { return; }

            Position += KnockbackVelocity * dt;
            KnockbackVelocity = Vector3.MoveTowards(
                KnockbackVelocity, Vector3.zero, SimConstants.KnockbackDeceleration * dt);
        }

        /// <summary>
        /// 推进一整组靶子。
        ///
        /// 做成 <c>Sim/</c> 里的静态方法而不是让 Boot 自己写 for 循环：
        /// 「怎么推进」是模拟逻辑，归 Sim；Boot 只该负责「调用它」。
        /// 以后 M4 加 AI 敌人时，这段会搬进一个统一推进所有实体的地方（`WorldSim` 之类）。
        /// </summary>
        public static void TickAll(TargetSim[] targets, float dt)
        {
            if (targets == null) { return; }

            for (int i = 0; i < targets.Length; i++)
            {
                // 对**数组元素**调用实例方法：改的是数组里那个元素本身。
                // 换成 `TargetSim t = targets[i]; t.Tick(dt);` 就只改副本了，白写。
                targets[i].Tick(dt);
            }
        }
    }
}
