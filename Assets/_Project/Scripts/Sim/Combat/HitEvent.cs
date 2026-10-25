using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// 一次**已经结算完**的命中（块 H3）。
    ///
    /// 模拟每 tick 产出、Boot 取走交给表现层（或调试工具）。它是**输出不是输入**——
    /// 不回灌进模拟，不参与确定性计算，谁消费、消费多少都不影响模拟结果。
    ///
    /// **由受害者发**（<see cref="PlayerSim.ConsumePendingHit"/>）：只有它同时知道
    /// 「谁打的」（攻击方随命中递过来）、「打在哪」和「收没收」。攻击者侧只能发
    /// 「几何接触」——那是另一件事（刀碰到过你 ≠ 你挨了）。
    ///
    /// 引用 <see cref="PlayerSim"/> 对象：Sim 内部消费没问题；将来要序列化 / 联机落盘时
    /// 再换成 actor 下标（顺序 = 身份的既有约定）。
    /// </summary>
    public struct HitEvent
    {
        /// <summary>攻击者。<c>null</c> = 调试触发（Boot 的受击测试按钮，没有真的攻击者）。</summary>
        public PlayerSim Attacker;

        /// <summary>受害者。</summary>
        public PlayerSim Target;

        /// <summary>
        /// 世界坐标：受害者**表面**朝向攻击者的接触点。
        /// 将来的火花 / 音效 / 飘字用它定位。
        /// </summary>
        public Vector3 Point;

        /// <summary>
        /// 世界坐标，已归一化：这一刀把目标往哪推。
        /// 招式没有击退（方向全零）时退化为接触法线（从受害者中心朝外）。
        /// </summary>
        public Vector3 Direction;

        /// <summary>
        /// 招式配置的伤害。**Blocked / Dodged 时实际扣 0**——要不要显示、
        /// 显示多少由消费者结合 <see cref="Result"/> 决定。
        /// </summary>
        public int Damage;

        /// <summary>结算结果（收下 / 无敌挡掉 / 完美闪避）。</summary>
        public HitResult Result;

        // ---- 韧性结算（块 P1）----

        /// <summary>这条命中**结算前**的韧性（Dodged 时是返还前的值）。</summary>
        public float PoiseBefore;

        /// <summary>
        /// 这条命中**结算后**的韧性。Dodged 时**包含完美闪避返还**；
        /// Blocked 与结算前相同；破韧那一击是 0；破韧中受击前后都是 0。
        /// </summary>
        public float PoiseAfter;

        /// <summary>
        /// 这条命中**实际扣减**的韧性（削韧值 × 系数后被 0 下限夹过的量，不是配置原值）。
        /// Blocked / Dodged / 破韧中受击都是 0。
        /// </summary>
        public float PoiseDamage;

        /// <summary>这条命中是否把韧性削到 0、触发了破韧。只有破韧那一击为真。</summary>
        public bool PoiseBroken;
    }
}
