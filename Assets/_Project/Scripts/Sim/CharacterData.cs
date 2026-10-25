using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// **一个角色的基础性能**：移动 / 跳跃 / 身材 / 挨打 / 生命 / 韧性 / 闪避 / 眼睛高度——
    /// 「这个角色是谁」的那一半（另一半是「他在挥哪一招」= <see cref="AttackData"/>）。
    ///
    /// 拆家结果（MV3a，2026-10-15）：这些数原来住在 `SimConstants` 里被所有角色共用；
    /// 现在每个角色一份（`Data/CharacterDataAsset` 提供资产，Boot 每 tick 值拷贝传进来），
    /// M4 的敌人要「跑得慢、血更厚、闪得短」改的就是它。
    ///
    /// **纯数据**（同 <see cref="AttackData"/>）：struct 值拷贝，Sim 物理上改不到磁盘上的资产；
    /// 数学类型属铁律 1 放行范围。
    /// </summary>
    [System.Serializable]
    public struct CharacterData
    {
        // ---- 移动 ----

        /// <summary>水平最大速度，米/秒。</summary>
        public float MaxMoveSpeed;

        /// <summary>加速度，米/秒²。60 表示 0.1 秒从静止加到满速。</summary>
        public float MoveAcceleration;

        /// <summary>减速度，米/秒²。30 表示松键后 0.2 秒停下——要能看出「有减速」而不是瞬停。</summary>
        public float MoveDeceleration;

        // ---- 跳跃 ----

        /// <summary>
        /// 起跳初速度，米/秒。跳跃高度 = v²/(2g)：8 配全局重力 30 → 约 1 米、滞空约半秒。
        /// 手感两个旋钮：改高度调它，改「利落程度」调全局数据的重力。
        /// </summary>
        public float JumpSpeed;

        // ---- 身材 ----

        /// <summary>
        /// 把角色近似成一个球的半径，米。**这是「被别人打中」用的几何体**——
        /// 方块被近似成球，角落打不到（边长 1 的方块内切球 0.5）。
        /// 攻击方用「胶囊 vs 球」判定（`HitTest`），两边都是球，算式不用动。
        /// </summary>
        public float Radius;

        /// <summary>
        /// 从角色位置（`PlayerSim.Position`，方块**中心**）到**脚底**的距离，米。
        /// 落地判定用它：脚底碰到地面时，中心停在 `GlobalData.GroundY + HalfHeight`。
        /// </summary>
        public float HalfHeight;

        // ---- 挨打 ----

        /// <summary>
        /// 受击硬直：被打中后失去控制这么多 tick。12 = 0.2 秒。
        ///
        /// 它是**受击者**的属性，不是攻击者的：攻击方给的是伤害和击退，硬直多长归防守方。
        /// 前提：**必须 ≤ <see cref="InvincibilityTicks"/>**——否则无敌还没结束就又被打中，
        /// 会在硬直中途重入同一个状态（状态机同状态只 +1、不归零），连击会把角色锁死。
        /// </summary>
        public int HitstunTicks;

        /// <summary>
        /// 无敌帧：从受击那一刻起算，这么多 tick 内的命中**完全无效**（不掉血、不推、也不进硬直）。
        /// 30 = 0.5 秒，比 <see cref="HitstunTicks"/> 长，所以硬直结束后还有一段安全期。
        /// </summary>
        public int InvincibilityTicks;

        // ---- 生命 ----

        /// <summary>
        /// 血量上限。**出生时定型**（构造那一刻取值）——之后改资产不影响已出生的角色，
        /// 影响的是下一个出生的。这样「血不是负的」这类账不用每 tick 重新结算。
        /// </summary>
        public int MaxHealth;

        // ---- 韧性（块 P1）----

        /// <summary>
        /// 韧性上限。**每 tick 热读**（和角色其他数值一样）：调低时当前韧性会被夹回，
        /// 调高不会自动补满，只靠回复慢慢涨。0 是合法配置——初始韧性 0，只要有一招
        /// 削韧 &gt; 0，第一下就直接破韧；要「关闭韧性互动」请把招式的
        /// <see cref="AttackData.PoiseDamage"/> 填 0，而不是把上限填 0。
        /// </summary>
        public float MaxPoise;

        /// <summary>
        /// 韧性回复速度，点/秒。只在回复延迟结束、且不在破韧状态时生效；负值按 0。
        /// </summary>
        public float PoiseRegenPerSecond;

        /// <summary>
        /// 削韧 / 闪避消耗后的**回复延迟**，tick。触发的那一 tick 永远不回复；
        /// `Delay=N (N≥1)` 的首次回复在触发后第 N tick，`Delay=0` 和 `Delay=1`
        /// 都是第 1 tick 开始回复（最小停顿就是触发 tick 本身）。
        /// 延迟中再次削韧/消耗会重新计时，不累加；破韧期间延迟冻结。
        /// </summary>
        public int PoiseRegenDelayTicks;

        /// <summary>
        /// **霸体等级**。v1 只参与削韧系数
        /// （`clamp(1 + k × (打断等级 − 霸体等级), min, max)`），**不做「不硬直」**。
        /// 建议 0..3 的小整数；武器系统实装后由武器类型决定，现在先写死在角色数据里。
        /// </summary>
        public int ArmorLevel;

        /// <summary>
        /// 一次闪避消耗的韧性。**当前韧性不足它就不能闪**（按键无效）。
        /// 0 = 闪避不花韧性；`== MaxPoise` 时可以闪、扣到 0（不破韧）；
        /// `> MaxPoise` 则永远闪不出来（资产的 OnValidate 会警告）。
        /// 完美闪避返还**本次实际支付**的这份消耗。
        /// </summary>
        public float DodgePoiseCost;

        /// <summary>
        /// 破韧状态的固定时长，tick（60 = 1 秒）。破韧**故意不给无敌**，
        /// 所以它不受「HitstunTicks ≤ InvincibilityTicks」那条约束；
        /// 0 / 负数按最短 1 tick 处理（进入 tick 不评估，下一次 Tick 立即退出）。
        /// </summary>
        public int PoiseBreakTicks;

        // ---- 闪避 ----

        /// <summary>
        /// 闪避的时长，tick。20 = 0.33 秒；这段时间**全程无敌**。
        /// 和 <see cref="InvincibilityTicks"/> 是两个独立的数——闪避的无敌时长就取这个。
        /// </summary>
        public int DodgeTicks;

        /// <summary>
        /// 闪避冲出去的距离，米。和击退一样**按距离参数化**（v = sqrt(2ad) 反推初速度，
        /// 走冲量通道），所以参数即结果：填 3 米，实测滑约 **3.09** 米（离散偏差与距离有关，
        /// 1 米时是 ×1.0528，3 米时是 ×1.0305——别拿一个系数外推）。
        /// </summary>
        public float DodgeDistance;

        // ---- 眼睛高度 ----

        /// <summary>
        /// 角色「眼睛」离中心的高度，米（MV3a 起属于角色——原 `SimConstants.CameraHeightOffset`，
        /// 现在矮怪、高怪各配各的）。
        ///
        /// 一个数、两个用途、必须是同一个点：
        ///   ① 玩家的相机挂在它上面（`CameraRig` 摆相机）；
        ///   ② 视角锁定的瞄准点 / 选目标都量到「各自的眼睛」（`PlayerSim.EyePosition`）。
        ///
        /// 角色位置是方块**中心**；0.5 是当前手感取值——**不是**解剖学上的眼高，
        /// 是「相机挂在身上哪个高度」，按看着舒服来。
        ///
        /// ⚠️ **判定体与这个高度无关**。块 T1 起 hitbox 的 y 由它自己的本地坐标决定
        /// （可以配在任意高度）；这里只是相机挂点 / 锁定瞄准点，两者互不影响。
        /// </summary>
        public float CameraHeightOffset;
    }
}
