using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// 一个招式的数据：**整招的时间轴 + hitbox 列表 + 命中结算 + 攻击中的手感**。
    ///
    /// 块 T1（2026-10-20）之前它是「前摇 / 判定 / 后摇三段 + 一条胶囊」；合并后：
    ///   - **时间轴**：只有 <see cref="TotalTicks"/>，状态机在 <see cref="PlayerState.Attack"/>
    ///     里走完它（三段的行为本来就完全一致，分段的唯一作用——判定只在 Active 段——
    ///     由 hitbox 的 tick 窗口接管）；
    ///   - **判定**：<see cref="HitboxCount"/> 个 <see cref="HitboxDefinition"/>，各自带 tick 窗口；
    ///   - **结算**（伤害 / 击退 / 顿帧）仍是招式级——这一版多个 hitbox 只扩大覆盖，
    ///     同一目标每次挥砍最多挨一下（用户 2026-10-20 定）。
    ///
    /// 纯数据的纪律不变（同 <see cref="CharacterData"/>）：struct 值拷贝，
    /// Sim 拿到的是副本。**唯一的例外是 hitbox 数组**：数组是引用类型，值拷贝共享同一个数组——
    /// 所以它放在私有字段里，只暴露「数量 + 元素拷贝」的只读访问器，Sim 拿不到可写的数组，
    /// 资产仍然安全。这条例外记在 `DEVLOG.md` 块 T1。
    ///
    /// 特性写成 <c>[System.Serializable]</c> 而不是 <c>[Serializable]</c>（前面不加 using）：
    /// 它是 mscorlib 的东西，不是 UnityEngine 的。本文件用 <c>Vector3</c> / <c>Mathf</c>，
    /// 属铁律 1 明确放行的无状态数学类型。
    /// </summary>
    [System.Serializable]
    public struct AttackData
    {
        /// <summary>
        /// 整招时长，tick（≥ 1）。起手第一 tick = 0，最后一 tick = <c>TotalTicks - 1</c>。
        ///
        /// 不再由三段求和（T1 合并）：填多少就是多少。动画 clip 该 ≈ 它 / 60 秒
        /// （`ActorAnimation` 进攻击时对表）。
        /// </summary>
        public int TotalTicks;

        /// <summary>
        /// 判定体列表。每个 hitbox 的窗口是**整招绝对 tick**（0 = 起手第一 tick，含端点），
        /// 可以任意落在 <c>0..TotalTicks-1</c> 之内；每 tick 所有覆盖当前 tick 的一起参与判定。
        ///
        /// **私有 + 只读访问器**：数组是引用类型，public 会让 Sim 绕开值拷贝、直接写回
        /// 磁盘上的资产。现在 Sim 只能拿到元素拷贝——物理隔离保住了。
        /// </summary>
        [SerializeField]
        private HitboxDefinition[] _hitboxes;

        /// <summary>判定体数量（null 数组按 0 算——<c>default(AttackData)</c> 安全）。</summary>
        public int HitboxCount => _hitboxes == null ? 0 : _hitboxes.Length;

        /// <summary>取第 <paramref name="index"/> 个判定体。返回的是**拷贝**，改它不影响资产。</summary>
        public HitboxDefinition GetHitbox(int index) => _hitboxes[index];

        /// <summary>
        /// 给**资产初始化**用的构造口：返回带上了这组 hitbox 的副本
        /// （struct 的实例字段初始化器不能写 `new`，见 `AttackDataAsset`）。
        /// 运行时不该调它——Sim 只需要读。
        /// </summary>
        public AttackData WithHitboxes(HitboxDefinition[] hitboxes)
        {
            AttackData copy = this;
            copy._hitboxes = hitboxes;
            return copy;
        }

        // ---- 命中结算（招式级） ----

        /// <summary>打中一下扣多少血。</summary>
        public int HitDamage;

        /// <summary>
        /// 打中一下给目标多少**速度**，米/秒（沿 <see cref="KnockbackDirection"/> 的方向）。
        ///
        /// 2026-10-13 从「距离」改成「速度」（原 KnockbackDistance，单位米）。为什么改：
        /// 距离参数化的底层是 <c>v = sqrt(2aD)</c>——**把衰减率 a 预补偿进了初速度**，
        /// 于是「滑多远」与 a 无关（a 被约掉，只管「滑多久」）。单通道时代这笔交换换来
        /// 「填米得米」，很值；但现在冲量（水平，20 m/s²）和重力（竖直，30 m/s²）是两条通道，
        /// 一个「距离」补偿不了两边（跨通道会平方缩水），而且「每招不同衰减率」一落地，
        /// D 就说不清代表什么。**速度是唯一在两条通道上含义一致的量**：招式交付
        /// 「加多少速度」（运动学），世界交付「这个速度怎么衰减、落地」（动力学）。
        ///
        /// 默认 6.3246 = sqrt(2·20·1)，是旧「1 米」的精确等价值——老资产行为逐位不变。
        /// </summary>
        public float KnockbackSpeed;

        /// <summary>
        /// 击退方向，**攻击者本地坐标**（x = 右 / y = 上 / z = 前，即 Unity 里角色朝 +Z 时的本地向量）。
        /// 写懒值（如 <c>(0.3, 0.6, 1)</c>），**用时自动归一化**。
        ///
        /// 水平分量走**冲量通道**（滑行），y 分量走**竖直弹道通道**（像跳跃，重力接管弧线）；
        /// y = 0 表示**不碰竖直**（空中挨一刀不会清零下坠速度）；**方向全零 = 这一招不推**。
        ///
        /// 默认 (0, 0, 1) = 朝攻击者正前方推。注意这和旧的「径向远离攻击者」不同：
        /// 斜着命中**不再侧推**，推的方向完全由配置说了算（作者化，2026-10-13 用户定）。
        /// </summary>
        public Vector3 KnockbackDirection;

        /// <summary>
        /// 打中一下让**整个世界**冻住多少 tick（顿帧）。0 = 关掉。
        ///
        /// 它**不参与** <see cref="TotalTicks"/>：顿帧不推进招式的 tick 时间轴
        /// （`PlayerSim.Tick` 整个不跑，`StateTicks` 不动），只是把真实时间拉长。
        /// 6 tick = 0.1 秒，是「轻轻一顿」的常见量级。
        /// </summary>
        public int HitStopTicks;

        // ---- 攻击中的移动 ----

        /// <summary>
        /// 攻击中的**移动速度系数**（0~1，块 MV2，2026-10-15）。
        ///
        /// 攻击状态里，目标移动速度 = `方向 × MaxMoveSpeed × 它`：
        ///   0   = 站桩（= 旧行为，逐位复现——目标被 ×0 乘成零，自动走「刹车」一侧）；
        ///   1   = 攻击中移动和平时一样（除朝向仍归 <see cref="TurnMaxSpeedDegreesPerSecond"/>）；
        ///   0.5 = 半速（3 m/s）。半推摇杆与系数是**连乘**（半推 + 0.5 → 1.5 m/s）。
        ///
        /// 移动方向仍是常规的相机相对方向（含锁定防甩保持）；**朝向是另一根旋钮**——
        /// 所以系数 > 0 时会「面朝一边、横着滑」，那是预期。
        ///
        /// rate 的口径（用户 2026-10-15 确认）：判据是**目标速度是不是零**——非零 → 加速度 60
        /// （和平时开车同款）；为零（没输入 或 系数 = 0）→ 减速度 30。**不是**「有没有输入」：
        /// 系数 = 0 时输入会被乘掉、目标其实是零，只看输入会错用加速度去刹车（刹停时间砍半，
        /// 破「0 = 现状」）。实现上直接判 <c>targetVelocity.sqrMagnitude &gt; 0</c>，条件即语义。
        /// </summary>
        [Tooltip("攻击中的移动速度系数：0 = 站桩，1 = 和平时一样。半推摇杆 × 它（连乘）")]
        [Range(0f, 1f)]
        public float MoveSpeedScale;

        // ---- 攻击中的朝向 ----

        /// <summary>
        /// 攻击中朝向的**最大旋转速度**，度/秒（块 F1，2026-10-15）。
        ///
        /// 语义：**攻击状态**中，朝向每 tick 以最多 `它 × dt` 朝命令的 `FacingYaw`
        /// （玩家路径 = 相机 yaw）转——最短方向、一步不超剩余误差（和相机锁定的 TurnStep
        /// 同款写法，落地即吸附）。进/出攻击那一 tick 仍是**瞬跟**（起手对齐、收招跟上），
        /// 所以攻击之外不留滞后。
        ///
        /// 两个端点都有精确含义：
        ///   0  = 锁死朝向（= 旧行为，逐位复现）；
        ///   ≥ 相机鼠标上限（720）= 约等于不锁。每招一个数：重击慢、刺击快，配的就是这里。
        ///
        /// 已知后果（用户 2026-10-15 拍板接受）：判定窗口里 hitbox 会以这个速度慢慢转——
        /// 原来锁朝向的理由就是「挥到一半判定换方向」，现在换成「限速地转」。
        /// 不想让它转的招填 0。将来要「某段 tick 可瞄 / 某段锁」的分段控制，
        /// 是再加数据字段的事，不是改结构的事。
        /// </summary>
        public float TurnMaxSpeedDegreesPerSecond;
    }
}
