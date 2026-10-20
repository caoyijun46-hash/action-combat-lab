using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// **一个角色**的模拟状态与每 tick 的运动学更新。
    ///
    /// 场上每个角色一个实例，玩家只是其中一个——区别只在于「谁给它喂命令」：
    /// 玩家那个喂 <c>ActorCommand.FromPlayerInput(...)</c>，沙包（还没有脑子的角色）
    /// 喂一个什么都不做的命令。从这个块起它也是**受害者**：别人的判定体会打到它身上
    /// （<see cref="ReceiveHit"/>），它带着和玩家一模一样的受击反应（硬直 / 击退 / 无敌帧）。
    ///
    /// 运动学 = 直接积分位置，不用 <c>Rigidbody</c> 的力或速度驱动（铁律 6）。
    /// 也不碰 <c>Transform</c>——位置只存在这里，由 View 层读出去写表现。
    ///
    /// 本类用了 <c>Vector3</c> / <c>Mathf</c>，属铁律 1 明确放行的无状态数学类型。
    /// </summary>
    public class PlayerSim
    {
        /// <summary>世界坐标下的位置，米。</summary>
        public Vector3 Position;

        /// <summary>
        /// 上一个 tick 的位置，米。构造时与 <see cref="Position"/> 相同。
        ///
        /// 这是**模拟历史**的一部分，不是表现数据——Sim 并没有为渲染做任何事，只是记住了自己刚才在哪。
        /// View 层靠它和 <see cref="Position"/> 做渲染插值：显示两者之间的某个比例，而不是最新值。
        /// </summary>
        public Vector3 PreviousPosition;

        /// <summary>
        /// **自驱水平速度**，米/秒，<c>y 恒为 0</c>（2026-10-13 通道分离）。
        ///
        /// 由状态机决定目标速度，唯一改写点是那条 <c>MoveTowards</c>，
        /// 「相邻 tick 无突变」的不变量盯的就是它。
        /// </summary>
        public Vector3 MoveVelocity { get; private set; }

        /// <summary>
        /// **竖直速度**，米/秒——弹道通道（2026-10-13 通道分离）。
        ///
        /// 重力每 tick 积分、跳跃直接赋值、落地归零。**允许突变是设计**（跳跃 / 落地就是突变），
        /// 所以它是一个独立的 float、不混进 <see cref="MoveVelocity"/> 的 y——
        /// 「重力绝不能经过那条 MoveTowards」由此从注释里的纪律变成结构上的不可能。
        ///
        /// 将来的竖直击退（挑空）也写这里（像跳跃），水平分量才走 <see cref="ImpulseVelocity"/>。
        /// </summary>
        public float VerticalSpeed { get; private set; }

        /// <summary>
        /// **外力水平冲量**，米/秒，<c>y 恒为 0</c>。原「击退通道」，2026-10-13 随通道分离改名
        /// ——它现在同时承载受击击退和闪避位移（将来还有攻击前冲），「击退」这个名字已经名不副实。
        ///
        /// 受击时被赋值，之后每 tick 按 <c>GlobalData.ImpulseDeceleration</c> 衰减到零。
        /// 它和 <see cref="MoveVelocity"/> 是**两条独立的通道**，互不干扰：
        /// MoveVelocity 是「自己走出来的速度」，由状态机决定目标值，受「相邻 tick 无突变」
        /// 那条不变量约束（HUD 一直在盯它）；冲量是**外力**，直接加在位置上，不受状态限制。
        /// 硬直 12 tick 比滑完（约 19 tick）短，所以玩家会「在滑行中恢复控制」。
        ///
        /// **只收水平**：受击入口在消费时把 y 拆给 <see cref="VerticalSpeed"/>（y = 0 表示不碰），
        /// 闪避入口本来就只产生水平。
        /// </summary>
        public Vector3 ImpulseVelocity { get; private set; }

        /// <summary>血量上限。出生时从角色数据的 <see cref="CharacterData.MaxHealth"/> 取（改资产影响下一个出生的角色）。</summary>
        public int MaxHealth { get; private set; }

        /// <summary>当前血量。**会被夹在 0 以上**——这一块不做死亡，但血量不该是负的。</summary>
        public int Health { get; private set; }

        /// <summary>
        /// 把角色近似成一个球的半径，米（「被别人打中」用的几何体，定义在
        /// <see cref="CharacterData.Radius"/>）。**从角色数据现读**（MV3a）——每个角色的身材可以不同。
        /// </summary>
        public float Radius => _characterData.Radius;

        /// <summary>
        /// 从角色位置（方块中心）到**脚底**的距离，米（落地判定用，见
        /// <see cref="CharacterData.HalfHeight"/>）。从角色数据现读（MV3a）。
        /// </summary>
        public float HalfHeight => _characterData.HalfHeight;

        // ---- 从角色数据现读的只读属性（MV3a）----
        // 都是给「Tick 外面」的消费者用的：View（倾斜 / 摆相机）、调试 HUD、其他角色的判定。
        // 数据在 Tick 开头刷新，读到的是最近一次 Tick 传进来的那份。

        /// <summary>水平最大速度，米/秒（View 的倾斜幅值拿它归一）。</summary>
        public float MaxMoveSpeed => _characterData.MaxMoveSpeed;

        /// <summary>
        /// 「眼睛」离中心的高度，米（<see cref="CharacterData.CameraHeightOffset"/>）。
        /// 玩家拿它摆相机（`CameraRig`）；锁定瞄准量的是 <see cref="EyePosition"/>。
        /// </summary>
        public float CameraHeightOffset => _characterData.CameraHeightOffset;

        /// <summary>
        /// 眼睛的世界坐标（位置上方 <see cref="CameraHeightOffset"/> 米）。
        /// 相机与视角锁定都量到这个点（MV3a 起每个角色有各自的眼睛高度）。
        /// </summary>
        public Vector3 EyePosition => Position + Vector3.up * CameraHeightOffset;

        /// <summary>
        /// 「相邻 tick 速度变化」的理论上限 = max(加速度, 减速度) × dt，米/秒。
        /// 供调试 HUD 的速度连续性判据用（判据属于这个角色——每人的加减速度可以不同）。
        /// </summary>
        public float MaxSpeedDeltaPerTick =>
            Mathf.Max(_characterData.MoveAcceleration, _characterData.MoveDeceleration)
            * SimConstants.FixedDeltaTime;

        /// <summary>
        /// 脚底是不是贴着地面。落地那一 tick 起为真，离地那一 tick 起为假。
        /// **跳跃的基础**：以后「按跳跃键且 IsGrounded」就是起跳条件。
        /// </summary>
        public bool IsGrounded { get; private set; }

        /// <summary>
        /// 当前动作状态。每 tick 在 <see cref="Tick"/> 里更新，外部只读。
        ///
        /// 用 <c>private set</c> 而不是公开字段：状态是模拟的**结果**，不该被 View 或 Boot 从外面改写。
        /// 这样「状态在哪被改」是可以在代码里搜出来的，而不是散落各处。
        /// </summary>
        public PlayerState State { get; private set; }

        /// <summary>
        /// 当前状态已经持续了几个 tick。**进入状态的那一 tick 记 0**，
        /// 所以「持续了 N 个 tick」的最后一 tick 读数是 N-1。
        ///
        /// 这个 0-based 约定会让「还差几个 tick」的判断写成 <c>StateTicks &gt;= N - 1</c> 而不是
        /// <c>&gt;= N</c>，容易差一。写判断时先回到这句话对一遍。
        /// </summary>
        public int StateTicks { get; private set; }

        /// <summary>
        /// 预输入的攻击键还剩几个 tick 有效期。0 表示当前没有待发的攻击。
        /// 只用于调试显示与验收；模拟逻辑内部读 <see cref="_attackBufferTicksLeft"/>。
        /// </summary>
        public int BufferedAttackTicksLeft => _attackBufferTicksLeft;

        /// <summary>攻击键预输入的剩余有效期。见 <see cref="GlobalData.AttackInputBufferTicks"/>。</summary>
        private int _attackBufferTicksLeft;

        /// <summary>
        /// 角色朝向，绕 Y 轴的偏航角（度）。<c>0</c> = 朝世界 +Z 方向，<c>90</c> = 朝 +X。
        ///
        /// 值来自命令里的 <c>FacingYaw</c>（玩家路径填的是相机 yaw），更新分两种
        /// （块 F1，2026-10-15）：
        ///   非攻击 / 进出攻击那一 tick —— **瞬跟**；
        ///   纯攻击状态中 —— **限速跟**（<c>AttackData.TurnMaxSpeedDegreesPerSecond</c>，0 = 锁死）。
        ///
        /// 判定体的方向用的就是它——所以它怎么变，直接决定攻击判定怎么转。
        /// </summary>
        public float FacingYaw { get; private set; }

        /// <summary>
        /// 朝向的单位方向向量，由 <see cref="FacingYaw"/> 现算，不单独存。
        ///
        /// 和 <see cref="ActorCommand.CameraRelativeDirection"/> 用的是同一套角度约定：
        /// 偏航 θ 的方向向量是 <c>(sinθ, 0, cosθ)</c>。两处必须是同一个约定，
        /// 否则「朝移动方向」和「朝参考前方」会对不上。
        /// </summary>
        public Vector3 FacingDirection
        {
            get
            {
                float rad = FacingYaw * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            }
        }

        /// <summary>
        /// 这一 tick 是否至少有一个 hitbox 在生效。判定窗口由招式数据说了算（块 T1），
        /// 不再绑在某个状态段上。给 Gizmo 用的便捷属性；模拟逻辑自己遍历 hitbox。
        /// </summary>
        public bool HasActiveHitbox => ActiveHitboxCount > 0;

        /// <summary>
        /// 当前 tick 活跃（窗口覆盖 <see cref="StateTicks"/>）的 hitbox 数量。
        /// 只在 <see cref="PlayerState.Attack"/> 里有意义，其余状态恒为 0。
        /// </summary>
        public int ActiveHitboxCount
        {
            get
            {
                if (State != PlayerState.Attack) { return 0; }

                int count = 0;
                for (int i = 0; i < _attackData.HitboxCount; i++)
                {
                    if (_attackData.GetHitbox(i).IsActiveAt(StateTicks)) { count++; }
                }
                return count;
            }
        }

        /// <summary>
        /// 取第 <paramref name="activeIndex"/> 个**活跃** hitbox 的世界端点。
        /// 顺序 = 招式数组里的出现顺序；越界 / 非攻击状态返回 false。
        ///
        /// 换算和 <see cref="ResolveHits"/> 用的是同一个函数（<see cref="HitboxDefinition.LocalPointToWorld"/>），
        /// 所以 Gizmo 画出来的就是模拟真正判定的位置。
        /// </summary>
        public bool TryGetActiveHitboxWorld(
            int activeIndex, out Vector3 from, out Vector3 to, out float radius)
        {
            from = Vector3.zero;
            to = Vector3.zero;
            radius = 0f;

            if (State != PlayerState.Attack) { return false; }

            int found = 0;
            for (int i = 0; i < _attackData.HitboxCount; i++)
            {
                HitboxDefinition box = _attackData.GetHitbox(i);
                if (!box.IsActiveAt(StateTicks)) { continue; }

                if (found == activeIndex)
                {
                    from = HitboxDefinition.LocalPointToWorld(
                        box.LocalFrom, Position, HalfHeight, FacingYaw);
                    to = HitboxDefinition.LocalPointToWorld(
                        box.LocalTo, Position, HalfHeight, FacingYaw);
                    radius = box.Radius;
                    return true;
                }

                found++;
            }

            return false;
        }

        /// <summary>
        /// 上一 tick 收到的攻击数据。
        ///
        /// 存一份是为了让 <see cref="ActiveHitboxCount"/> / <see cref="TryGetActiveHitboxWorld"/>
        /// 这些属性有数可算——它们是从 Tick **外面**读的（View、Gizmo），拿不到 Tick 的参数。
        /// 这份缓存只是参数的值拷贝（数组引用共享，但只读），不影响「Tick 只依赖传入参数」
        /// 这条性质，重放同样的参数仍然得到同样的结果。
        /// </summary>
        private AttackData _attackData;

        /// <summary>
        /// 最近一 tick 收到的**角色数据**（MV3a）。和 <see cref="_attackData"/> 一个套路：
        /// 缓存一份让 <see cref="Radius"/> / <see cref="EyePosition"/> 这些属性有数可算——
        /// 它们从 Tick **外面**读（View、其他角色的判定、相机），拿不到 Tick 的参数。
        /// 构造时就先存一份（出生那一瞬属性就该是有效的）。
        /// </summary>
        private CharacterData _characterData;

        /// <summary>
        /// 最近一 tick 收到的**全局数据**（MV3a）。缓存原因同 <see cref="_characterData"/>。
        /// </summary>
        private GlobalData _globalData;

        /// <summary>
        /// 当前攻击数据的总 tick 数。供 View 校验动画长度（clip 该 ≈ 它 / 60 秒）。
        /// 读的是上一 tick 缓存的 <see cref="_attackData"/>。
        /// </summary>
        public int CurrentAttackTotalTicks => _attackData.TotalTicks;

        /// <summary>是否处于攻击状态（整招）。块 T1 起攻击只有一个状态。</summary>
        public static bool IsAttacking(PlayerState state)
        {
            return state == PlayerState.Attack;
        }

        /// <summary>
        /// 本次挥砍到目前为止打中过**几个不同的**目标。起手时清零，所以它不会跨招累积。
        ///
        /// 「同一招式不重复命中同一目标」靠的就是它背后的名单——判定窗口通常跨几个 tick，
        /// 对手一直站在 hitbox 里的话，不做记录每个 tick 都会命中一次。
        /// </summary>
        public int HitTargetCount => _hitTargetCount;

        /// <summary>
        /// 这一 tick 有没有**真的挨了一下**（受害者一侧的记账）。
        /// 每 tick 在 <see cref="Tick"/> 开头重置，<see cref="ConsumePendingHit"/> **收下时**才置真。
        ///
        /// 注意是「收下才算」：被无敌（还有闪避）挡掉的命中不记账——「无事发生」就是无事发生。
        /// 和它同收同放的是 <see cref="HitStopTicksThisTick"/>（这一下要求的顿帧时长）——
        /// 顿帧的判据在那边（H2 起），它只回答「挨没挨」。
        ///
        /// 和攻击者的分工（用户 2026-10-10 定）：
        ///   攻击者的「本次挥砍打过谁」名单 —— **几何重叠就记**（碰到就算用掉，不重试）；
        ///   受害者的「这一 tick 挨没挨」   —— **收下才记**（收不收由自己判）。两本账分开记。
        ///
        /// 为什么不让 Boot 去 diff 血量之类的：那是把事件当状态轮询（见 ROADMAP「命中事件流」）。
        /// 这是事件流的最小版本；等真需要音效/飘字时再扩成带伤害和方向的 HitEvent。
        ///
        /// 注意：顿帧期间 <see cref="Tick"/> 整个不跑，这个值会停在「上一次执行」的结果——
        /// 但读它的消费者只在 Tick 跑过的 tick 上读，所以不会重复触发。
        /// </summary>
        public bool WasHitThisTick { get; private set; }

        /// <summary>
        /// 这一 tick **收下**的命中带来的**顿帧请求**（tick 数）——攻击方招式的
        /// <c>AttackData.HitStopTicks</c> **随命中记录走**（H2，2026-10-15）。
        /// 每 tick 在 <see cref="Tick"/> 开头重置；收下命中时写入（被无敌 / 完美闪避
        /// 挡掉的不写——和 <see cref="WasHitThisTick"/> 同一条「收下才算」的纪律）。
        ///
        /// 消费者是 Boot 层的顿帧（经 <c>WorldSim</c> 汇总取最大）：它决定冻多久，
        /// **不再**由 Boot 伸手读攻击方的资产——那个读法只在「玩家是唯一攻击者、
        /// 大家共用一份数据」时才对，M4 敌人出招就会读错人（这就是 H2 修的隐患）。
        /// 0 = 没挨打（或这一下不顿）；负数会被当成不顿（汇总以 0 为底）。
        /// </summary>
        public int HitStopTicksThisTick { get; private set; }

        // ---- 受击 ----

        /// <summary>无敌还剩几个 tick。> 0 时命中完全无效。供调试 HUD 显示。</summary>
        public int InvincibleTicksLeft => _invincibleTicksLeft;

        private int _invincibleTicksLeft;

        /// <summary>
        /// 待处理的受击：由 <see cref="ReceiveHit"/> 装填、由 <see cref="Tick"/> 开头消费
        /// （收不收在消费时判）。为什么要隔一拍——见 <see cref="ReceiveHit"/> 的注释。
        /// </summary>
        private bool _hasPendingHit;
        private int _pendingHitDamage;
        private Vector3 _pendingHitKnockback;
        private int _pendingHitHitStopTicks;

        /// <summary>
        /// 这一 tick 挨了一下、但**正在闪避**把它躲掉了（「完美闪避」）。
        /// 每 tick 在 <see cref="Tick"/> 开头重置，<see cref="ConsumePendingHit"/> 里
        /// 「无敌 + 状态是 Dodge」时置真。消费者是 Boot：它据此触发慢放。
        ///
        /// 注意它和 <see cref="WasHitThisTick"/> 互斥：躲掉的一下不算「挨了」——
        /// 完美闪避不触发顿帧，触发的是慢放。
        /// </summary>
        public bool DodgedHitThisTick { get; private set; }

        /// <summary>
        /// 本次挥砍已经打中过的对手（存的是**在 actors 数组里的下标**）。
        ///
        /// 用下标当身份，而不是给角色加个 Id 字段：角色数组由 Boot 一次建好、顺序不变，
        /// 下标就是稳定身份。**代价是这条约定必须守住**——哪天角色会动态增删，
        /// 下标就会变，得改成真正的 Id。
        ///
        /// 固定容量、零分配。上限 8 是「一次挥砍最多同时打中几个」——
        /// 超出的直接不记（那一刀仍然会造成伤害，只是不会被去重，属于极端情况）。
        ///
        /// **记录发生在「几何重叠」时**（不是「伤害真的落下」时）——用户 2026-10-10 定：
        /// 名单是「我的刀碰到过你」的记录；对手在无敌期间被砍中，名额照样用掉、之后不重试。
        /// 收不收是受害者的事，两本账分开记。
        /// </summary>
        private const int MaxHitsPerSwing = 8;
        private readonly int[] _hitTargetIndices = new int[MaxHitsPerSwing];
        private int _hitTargetCount;

        private bool AlreadyHitThisSwing(int targetIndex)
        {
            for (int i = 0; i < _hitTargetCount; i++)
            {
                if (_hitTargetIndices[i] == targetIndex) { return true; }
            }
            return false;
        }

        private void RecordHit(int targetIndex)
        {
            if (_hitTargetCount >= MaxHitsPerSwing) { return; }
            _hitTargetIndices[_hitTargetCount] = targetIndex;
            _hitTargetCount++;
        }

        /// <param name="spawnPosition">出生位置（场景里摆放的坐标，由 Boot 翻译进来）。</param>
        /// <param name="character">
        /// 这个角色的基础性能（MV3a）。**构造时的那一份只定两件出生时的事**：
        /// 血量上限 + 一份初始缓存（让 <see cref="Radius"/> 这些属性在第一个 Tick 之前就有效）。
        /// 之后由每 tick 传进来的那份说话——播放中改资产下一 tick 生效。
        /// </param>
        public PlayerSim(Vector3 spawnPosition, CharacterData character)
        {
            Position = spawnPosition;
            PreviousPosition = spawnPosition;
            MoveVelocity = Vector3.zero;
            VerticalSpeed = 0f;
            ImpulseVelocity = Vector3.zero;
            _characterData = character;
            MaxHealth = character.MaxHealth;
            Health = character.MaxHealth;
        }

        /// <summary>
        /// 切状态，并维护 <see cref="StateTicks"/>：换状态清零，留在原状态则 +1。
        ///
        /// 所有状态的改变都必须走这一个口子——这样 <see cref="StateTicks"/> 不可能忘记维护，
        /// 出问题时也只有一个地方要看。
        ///
        /// <paramref name="restart"/> = **同状态也清零**。块 T1（2026-10-20）把攻击三段合并成
        /// 一个 <see cref="PlayerState.Attack"/> 之后，接招成了 Attack → Attack——状态没变，
        /// 但那是新的一招，计时必须归零。这是块 7b 删掉的那个参数的回归：当时接招还是
        /// Recovery → Startup，换状态自动清零，所以不需要它。
        /// 只有接招分支传 true；Idle/Move 起手是换状态，用不上。
        /// </summary>
        private void SetState(PlayerState next, bool restart = false)
        {
            if (next == State && !restart)
            {
                StateTicks++;
                return;
            }

            State = next;
            StateTicks = 0;
        }

        /// <summary>
        /// **接下一次命中**（攻击者的判定体调它）：扣血、进 <see cref="PlayerState.Hitstun"/>、
        /// 被击退——但**这里什么也不判，只装填**。收不收是自己在下一个 <see cref="Tick"/> 开头
        /// 消费时说了算（<see cref="ConsumePendingHit"/> 查无敌 / 以后的闪避）。
        ///
        /// 这是「命中事件由受害者消费」的入口（用户 2026-10-10 定）：攻击者那边只管
        /// 「几何上碰到了」（并立刻记进它自己的名单），碰到的**结果**归这一边。
        /// 一个直接后果：命中被无敌挡掉时，攻击者那边看起来和"命中"无异——
        /// 这是接受的：名单是「我的刀碰到过你」的记录，不是「你挨了」的记录。
        ///
        /// 为什么只装填不立刻生效：调用方（调试触发、敌人的判定体）随便什么时候调用，
        /// 世界在下一 tick 才变——「状态转移只发生在 Tick 里」这条性质不破。
        ///
        /// 参数是「伤害 + 击退速度向量 + 顿帧请求」，都由攻击方按**招式配置**带来：
        /// 击退方向是攻击者本地坐标，旋转归一化后乘速度（见 <see cref="KnockbackImpulse"/>），
        /// **y 分量有意义**——消费时水平进冲量通道、y 进竖直弹道通道（y = 0 表示不碰竖直）；
        /// 顿帧请求（tick 数）由受害者收下时转记进 <see cref="HitStopTicksThisTick"/>
        ///（H2，2026-10-15：时长随命中记录走，Boot 不再伸手读攻击方的资产）。
        ///
        /// 已知限制：同一 tick 内多次调用只有**最后一次**生效（四个字段一起）。现在是单攻击源，
        /// 够用；多敌人时改成「结算列表」（两次伤害都该扣，击退取合力）。
        /// </summary>
        public void ReceiveHit(int damage, Vector3 knockbackVelocity, int hitStopTicks)
        {
            _hasPendingHit = true;
            _pendingHitDamage = damage;
            _pendingHitKnockback = knockbackVelocity;
            _pendingHitHitStopTicks = hitStopTicks;
        }

        /// <summary>
        /// 起一次闪避：给**冲量通道**一个冲量（距离参数化，参数即结果）+ 全程无敌。
        ///
        /// 方向取移动输入的方向，**半推也按满距离冲**（闪避是承诺，不是走位微调）；
        /// 没有移动输入就**向后**退（后撤步，2026-10-15 用户定；原来是朝前）。
        ///
        /// 无敌取 <c>max(现有的, DodgeTicks)</c>——**不能直接赋值覆盖**：
        /// 无敌时长可能来自"刚受击"（30 tick），这时闪避不该把它缩回 20。
        /// </summary>
        private void StartDodge(ActorCommand cmd)
        {
            Vector3 direction = cmd.MoveDirection;
            direction.y = 0f;

            float length = direction.magnitude;
            direction = length > 1e-4f ? direction / length : -FacingDirection;

            float speed = Mathf.Sqrt(2f * _globalData.ImpulseDeceleration * _characterData.DodgeDistance);
            ImpulseVelocity = direction * speed;

            _invincibleTicksLeft = Mathf.Max(_invincibleTicksLeft, _characterData.DodgeTicks);
        }

        /// <summary>
        /// 结算待处理的受击，返回是否**因此进入**硬直（= 该打断当前状态）。
        /// 无敌期间的命中在这里被**整体作废**——不掉血、不推、不进硬直，这才是无敌帧的意义
        /// （闪避的全程无敌也是从这个门拦下的，一份检查管两件事）。
        /// </summary>
        private bool ConsumePendingHit()
        {
            if (!_hasPendingHit) { return false; }
            _hasPendingHit = false;

            if (_invincibleTicksLeft > 0)
            {
                // 无敌：这一次命中完全作废。但如果**正在闪避**——这是「完美闪避」，
                // 记一笔给 Boot（慢放的触发条件）。受击后的无敌不算，两者要分得清。
                if (State == PlayerState.Dodge) { DodgedHitThisTick = true; }
                return false;
            }

            Health -= _pendingHitDamage;
            if (Health < 0) { Health = 0; }

            // 冲量拆分（2026-10-13 起 _pendingHitKnockback 的 y 有意义）：
            //   水平分量 → 冲量通道（滑行，衰减 20）；
            //   y 分量    → 竖直弹道通道（像跳跃，重力接管弧线）；**y = 0 时不碰**——
            //   空中挨一刀不会把下坠清零。被挑空的会在这一 tick 就往上飞第一步。
            Vector3 push = _pendingHitKnockback;
            if (push.y != 0f) { VerticalSpeed = push.y; }
            ImpulseVelocity = new Vector3(push.x, 0f, push.z);
            _invincibleTicksLeft = _characterData.InvincibilityTicks;
            WasHitThisTick = true;   // 记账：这一 tick 真的挨了一下
            HitStopTicksThisTick = _pendingHitHitStopTicks;   // 顿帧时长随这一笔命中走（H2）
            return true;
        }

        /// <summary>
        /// 推进一个 tick。<paramref name="dt"/> 只从参数来，绝不读 <c>Time.xxx</c>。
        /// </summary>
        /// <param name="cmd">
        /// 这一 tick 的操作意图。**谁产出的它不管**——玩家输入（<c>ActorCommand.FromPlayerInput</c>）、
        /// AI、以后的回放，都只是把这个结构填好交进来。
        /// </param>
        /// <param name="dt">固定步长，秒。</param>
        /// <param name="attackData">
        /// 攻击数据（整招时长 + hitbox 列表 + 命中结算）。**每 tick 传进来而不是构造时存起来**，有两个原因：
        /// 一是播放中改 Inspector 的数字要立刻生效；
        /// 二是 `Tick` 保持「只依赖参数」，重放同一串参数必然得到同一结果。
        /// 它是 struct，所以这里传的是值拷贝，没有堆分配。
        /// </param>
        /// <param name="characterData">
        /// 这个角色的基础性能（移动 / 跳跃 / 身材 / 挨打 / 闪避 / 眼睛高度，MV3a）。
        /// 和攻击数据同一条路：每 tick 值拷贝传进来，播放中改角色资产立刻生效。
        /// </param>
        /// <param name="globalData">
        /// 全局数据（重力 / 地面 / 冲量衰减 / 系统规则，MV3a）。同样每 tick 传。
        /// </param>
        /// <param name="actors">
        /// 场上**所有**角色（含自己——判定时会跳过自己）。
        /// 传数组而不是「自己持有一份」：角色不该知道世界上有哪些人，
        /// 而且这样 `Tick` 仍然只是「传入参数的函数」，重放同一串参数必然同一结果。
        /// **顺序必须稳定**——判定去重名单存的是这个数组里的下标，顺序一变下标就不再是身份。
        ///
        /// 用数组长度当数量，不另传 count——省一个参数，代价是数组里不能有空位。
        /// </param>
        public void Tick(ActorCommand cmd, float dt, AttackData attackData,
            CharacterData characterData, GlobalData globalData, PlayerSim[] actors)
        {
            PreviousPosition = Position;   // 先记下旧位置，供 View 插值
            _attackData = attackData;      // 缓存给判定体属性用，见字段注释
            _characterData = characterData;   // 同上（MV3a）：每 tick 刷新 = 热调
            _globalData = globalData;         // 同上
            WasHitThisTick = false;        // 事件信号只在 Tick 内有效：开头清零，收下命中时置真
            HitStopTicksThisTick = 0;      // 同上：收下命中时写入它要求的顿帧时长（H2）
            DodgedHitThisTick = false;     // 同上：闪避躲掉命中时置真

            // 无敌倒计时。**先递减、再消费待处理的受击**，于是「受击那一 tick + 之后 29 tick」
            // 恰好免疫 30 tick，第 31 tick 起可以再被打。反过来（先消费再递减）会多免疫一 tick。
            if (_invincibleTicksLeft > 0) { _invincibleTicksLeft--; }

            // 移动意图已经是**世界空间**的：摇杆 → 方向的换算在生产者那一侧
            // （ActorCommand.FromPlayerInput）。这里只把长度兜底压到 1 以内——
            // 生产者若给了长度 2 的方向，目标速度不能跟着翻倍。
            //（玩家路径已经压过一次；重复压在浮点误差内是幂等的。）
            Vector3 direction = cmd.MoveDirection;
            float moveSqr = direction.sqrMagnitude;
            if (moveSqr > 1f)
            {
                direction /= Mathf.Sqrt(moveSqr);
            }

            bool hasMoveInput = direction.sqrMagnitude > 0f;

            // ---- 有没有「待发的攻击请求」----
            // 两个来源合起来算：这一 tick 之前缓冲里还剩的，加上这一 tick 刚按下的。
            // 这里读的是**还没有更新**的缓冲值，正是我们要的语义。
            bool hasAttackRequest = _attackBufferTicksLeft > 0 || cmd.Attack;

            // ---- 转移：由**当前状态**分派，不是每 tick 重算 ----
            //
            // Step 5 时这里是一行 `State = 有输入 ? Move : Idle` 就完事，因为只有两个状态、
            // 判据只看输入。加了 Attack 之后那行就错了：它每 tick 无条件重算，
            // 玩家在攻击中碰一下方向键，下一 tick 就会被它改回 Move，攻击根本挂不住。
            //
            // 所以改成「当前状态决定能去哪」。代价是转移规则散在了 case 里，
            // 状态再多就该换成转移表了——M6 的事。
            // 记下这一 tick **开始时**的状态。朝向那一段要用它对前后两边做判断。
            bool wasAttacking = IsAttacking(State);

            PlayerState next;
            bool startsAttack = false;   // 这一 tick 是否起了一招，记账要用
            bool restartAttack = false;  // 接招：同状态也要把 StateTicks 清零（T1）

            // ---- 受击**优先**：被打中就打断一切 ----
            // 注意是「**不评估**原状态的转移」，不是「评估完再覆盖 next」——
            // switch 会在中途写 startsAttack 这类副作用，覆盖时得逐个清掉，迟早漏一个。
            if (ConsumePendingHit())
            {
                next = PlayerState.Hitstun;
            }
            // ---- 闪避：第二个「万能打断」（输入驱动）----
            // 能从任何非硬直、非闪避的状态**打断**进入——包括攻击状态（招式被取消）。
            // 这是它和跳跃的份量差别：跳跃不打断招式，闪避打断。
            // 优先级排在受击之后：同一 tick 又被打又按闪避，挨打赢。
            else if (cmd.Dodge && State != PlayerState.Hitstun && State != PlayerState.Dodge)
            {
                StartDodge(cmd);
                next = PlayerState.Dodge;
            }
            else
            {
                // 计时状态的判断都长这样：`StateTicks >= 时长 - 1`。
                // `- 1` 是因为 StateTicks 从 0 起（进入那一 tick 记 0）：
                // 「占满 30 个 tick」的最后一 tick 读数是 29，退出发生在下一次评估
                // （所以攻击会占 T0..T29，T30 那一 tick 交还给 Idle/Move）。
                switch (State)
                {
                    case PlayerState.Attack:
                        // 取消窗口开在整招的**最后** AttackCancelWindowTicks 个 tick。
                        // 求值时机是 tick 开头、SetState 自增**之前**——旧实现按后摇段自己的
                        // StateTicks 判断，翻译成整招绝对 tick 后正好是这条式子，逐位等价
                        // （30 tick + 窗口 3 → 首次生效在 T28；黄金轨迹钉死了这条）。
                        bool cancelWindowOpen = StateTicks
                            >= attackData.TotalTicks - _globalData.AttackCancelWindowTicks;

                        if (cancelWindowOpen && hasAttackRequest)
                        {
                            // 接招。块 T1 合并之后它是 Attack → Attack：状态没变，
                            // 靠 SetState 的 restart 把计时清零——不 restart 的话
                            // 第二招会从第一招的进度继续走。
                            next = PlayerState.Attack;
                            startsAttack = true;
                            restartAttack = true;
                            break;
                        }

                        next = StateTicks >= attackData.TotalTicks - 1
                            ? (hasMoveInput ? PlayerState.Move : PlayerState.Idle)
                            : PlayerState.Attack;
                        break;

                    case PlayerState.Hitstun:
                        // 硬直是**计时退出**：走完就交还给输入（和攻击收招的出口同一条规则）。
                        // 没有取消窗口、没有预输入——硬直期间按下的攻击由**记账**存进缓冲，
                        // 下一条 if 链会管，不在这里特殊处理。
                        next = StateTicks >= _characterData.HitstunTicks - 1
                            ? (hasMoveInput ? PlayerState.Move : PlayerState.Idle)
                            : PlayerState.Hitstun;
                        break;

                    case PlayerState.Dodge:
                        // 和硬直一样是**计时退出**：走完交还给输入。
                        // 闪避中再按闪避进不来（上面那条分支把 State == Dodge 拦下了），
                        // 所以这里的 StateTicks 不会被重置——闪避时长是承诺，狂按不能延长。
                        next = StateTicks >= _characterData.DodgeTicks - 1
                            ? (hasMoveInput ? PlayerState.Move : PlayerState.Idle)
                            : PlayerState.Dodge;
                        break;

                    default:   // PlayerState.Idle / PlayerState.Move
                        // 这两个状态之间由**输入**决定，不是速度：玩家松手的那一刻就回 Idle，
                        // 哪怕角色还在按减速度滑行。用速度判的话，松手后要等 0.2 秒速度精确归零
                        // 才肯离开 Move，那 0.2 秒里状态说的是「他在移动」，而玩家早松手了。
                        if (hasAttackRequest)
                        {
                            next = PlayerState.Attack;
                            startsAttack = true;
                        }
                        else
                        {
                            next = hasMoveInput ? PlayerState.Move : PlayerState.Idle;
                        }
                        break;
                }
            }

            SetState(next, restartAttack);

            // ---- 朝向 ----
            // 朝向 = **命令里的 `FacingYaw`**。解耦之后角色不知道这个值是怎么来的：
            // 玩家路径填的是相机 yaw（所以行为还是「朝向跟相机」），AI 填它盯着谁。
            //
            // 块 8a 时这里是「有移动输入就跟移动方向」。改成跟相机之后，
            // **按 S 是倒退走，不是转身朝后走** —— 这是第一人称/过肩第三人称该有的行为，
            // 也是「一条规则三个后果」那个方案的最后一环：移动时跟输入那条已经不需要了。
            //
            // 「跟一定距离内的敌人、没敌人跟鼠标」里的前半段现在**属于生产者**——
            // 那是「往 `FacingYaw` 里填什么」的规则，将来加在 ActorCommand.FromPlayerInput 里，
            // 角色这边一个字不用动。
            //
            // 攻击中的朝向 = **限速跟**（块 F1，2026-10-15 用户拍板）：原来是「完全锁死」
            // （理由是挥到一半转视角、判定体会在判定中途换方向）；现在换成每 tick
            // 以最多 `attackData.TurnMaxSpeedDegreesPerSecond × dt` 朝命令 yaw 转——
            // 判定体照转，但每 tick 转多少有上限。0 = 退回锁死（口子留着）。
            //
            // 判断条件是**前后两边都看**：只要这一 tick 的前后至少有一边不在攻击中，就瞬跟命令。
            //   Idle/Move → 攻击     ：瞬跟（**起手那一 tick 就对齐到按下时看到的方向**，之后限速）
            //   攻击中（含接招）：**限速转**
            //   攻击 → Idle/Move     ：瞬跟（收招立刻跟上，不留滞后）
            //
            // 为什么不能只看新状态：起手那一 tick 新状态已经是 Attack，
            //   那样朝向会保持「上一次更新时的值」而不是「按下时看到的方向」。
            //   正常游玩两者只差一个 tick，但那是碰巧 —— 相机一开始就和角色朝向不同就会露馅。
            // 为什么不能只看旧状态：收招那一 tick 旧状态还是 Attack，
            //   那样收招后要等一 tick 才跟上，快速转头时能感觉到黏。
            if (!wasAttacking || !IsAttacking(State))
            {
                FacingYaw = cmd.FacingYaw;
            }
            else
            {
                // 纯攻击中：限速转。maxStep = 0（含配置填 0）时这个方法原地不动——
                // 等价于旧的「不更新」。
                FacingYaw = StepTowardsAngle(
                    FacingYaw, cmd.FacingYaw,
                    attackData.TurnMaxSpeedDegreesPerSecond * dt);
            }

            // ---- 预输入的记账：必须在转移**之后**做 ----
            //
            // 「这一 tick 的按键有没有被用掉」只有转移才知道，所以顺序不能反。
            // 反过来的话，起手那一 tick 的按键会**既出了招、又留在缓冲里**：
            // 缓冲带着 12 tick 的有效期活到取消窗口打开，凭空多接一招。
            // 攻击时长 30 tick 时不会发作（窗口在 tick 28 才开，缓冲早在 tick 12 就过期了），
            // 时长一短就露馅——这是把时长做成可配置之后才暴露出来的。
            //
            // 用「剩余有效期倒计时」而不是「记下按下的 tick 号、用的时候相减」：
            // 后者要么读 frame.Tick（把「帧号对不对」变成 Sim 的隐式依赖），
            // 要么自己维护一个全局 tick 计数器。倒计时只需要一个 int，
            // 而且 Tick 仍然只依赖传进来的参数。
            if (startsAttack)
            {
                _attackBufferTicksLeft = 0;   // 用掉了。按住不放也只会接出这一招。
                _hitTargetCount = 0;          // 新的一招，命中名单清零
            }
            else if (cmd.Attack)
            {
                _attackBufferTicksLeft = _globalData.AttackInputBufferTicks;   // 没用掉，存着
            }
            else if (_attackBufferTicksLeft > 0)
            {
                _attackBufferTicksLeft--;
            }

            // ---- 行为 ----
            // 状态只决定「**水平**朝哪个目标速度逼近、多快逼近」，**不直接赋值 MoveVelocity**。
            // 水平速度的唯一改写点是下面那条 MoveTowards，所以任何切换都不可能造成水平突变：
            //   Idle → Move：上一 tick 速度已经是 6，这一 tick 目标还是 6，MoveTowards 原地不动；
            //   Move → Attack：目标从 6 变成 6×scale（默认 0），按减速度往下拉，连续；
            //   Attack → Move：从当前值按加速度往 6 拉，连续。
            // 用 MoveTowards 而不是 Lerp：Lerp 是指数衰减，永远到不了目标值。
            //
            // rate 的口径（块 MV2）：判据是「**这一 tick 有没有实际目标速度**」——
            // 非零 = 在开车 = 加速度 60；为零 = 在刹车 = 减速度 30。
            // 别写成「有没有输入」：攻击中系数 = 0 时输入会被 ×0 乘掉、目标其实是零，
            // 只看输入会错用加速度去刹（刹停时间砍半），把「0 = 现状」破掉。
            Vector3 targetVelocity;
            float rate;
            switch (State)
            {
                case PlayerState.Move:
                    targetVelocity = direction * _characterData.MaxMoveSpeed;
                    rate = _characterData.MoveAcceleration;
                    break;

                // 攻击中移动：目标速度按招式的系数打折（块 MV2，2026-10-15 用户定）。
                // 0 = 站桩 = 旧行为——目标被 ×0 乘成零，rate 自动走「刹车」一侧，逐位不变。
                // rate 用「目标是不是零」判（不是「有没有输入」——见上面的口径注释）。
                case PlayerState.Attack:
                    targetVelocity = direction * (_characterData.MaxMoveSpeed * attackData.MoveSpeedScale);
                    rate = targetVelocity.sqrMagnitude > 0f
                        ? _characterData.MoveAcceleration
                        : _characterData.MoveDeceleration;
                    break;

                // 三个状态把水平速度拉向零。冲量不走这里——它是独立通道（见下面的注释），
                // 加在速度积分之外；受击 / 闪避的位移都走那个通道（自己不出力，只把速度归零）。
                // 合并成 `case Idle: ...` 一行更短，但那样改动点就藏起来了。
                case PlayerState.Idle:
                case PlayerState.Hitstun:
                case PlayerState.Dodge:
                    targetVelocity = Vector3.zero;
                    rate = _characterData.MoveDeceleration;
                    break;

                default:
                    // 只为满足「局部变量必须赋值」而存在（C# 不对枚举做穷尽性检查）。
                    // ⚠️ 新增状态忘了在上面加 case 会静默落到这里、表现成 Idle 的样子——
                    // 不出招、不报错。离线测试里有一条专门盯这个。
                    targetVelocity = Vector3.zero;
                    rate = _characterData.MoveDeceleration;
                    break;
            }

            // 三条通道各走各的（2026-10-13 分离后，这里不再需要「拆开 y、再装回去」的杂技）：
            //   自驱水平 → MoveTowards（唯一改写点，无突变）；
            //   竖直     → 重力积分（允许突变，所以绝不经过 MoveTowards——
            //              目标速度的 y 恒为 0，经过它等于给重力装了个阻尼）；
            //   冲量     → 在下面单独积分（不受状态机管）。
            MoveVelocity = Vector3.MoveTowards(MoveVelocity, targetVelocity, rate * dt);
            VerticalSpeed -= _globalData.Gravity * dt;

            Position += new Vector3(MoveVelocity.x, VerticalSpeed, MoveVelocity.z) * dt;

            // ---- 落地 ----
            // 「脚底」在中心下方 HalfHeight 处；脚底碰到地面就停在那儿。
            // 站着的每一 tick 都是「重力往下压一点 → 被这里夹回来」——
            // 所以脚一沾地 vy 每 tick 都会被清零，IsGrounded 恒为真。
            float groundLevel = _globalData.GroundY + HalfHeight;
            if (Position.y <= groundLevel)
            {
                Position.y = groundLevel;
                if (VerticalSpeed < 0f) { VerticalSpeed = 0f; }
                IsGrounded = true;
            }
            else
            {
                IsGrounded = false;
            }

            // ---- 起跳 ----
            // 放在落地检查**之后**：IsGrounded 是这一 tick 刚刷新的值（"现在还贴着地"）。
            // 只有硬直里不能跳（正挨打呢）；空中的请求直接丢——没有二段跳。
            //
            // **攻击中可以跳**（2026-10-09 用户定）：跳跃只推竖直通道、**不打断**招式——
            // 招式会在空中继续挥完（判定照常，这是「空中攻击」的雏形）。
            // 两个如实说明的后果：
            //   ① 还没有跳跃动画 → 起跳那一下看不出"跳"的姿态，攻守动作本身照常；
            //   ② 攻击状态把**水平**速度拉向 `方向 × 6 × scale`（块 MV2 起的规则；默认
            //      scale = 0 即拉向 0）——「跑着跳、中途出招」会在空中把水平速度刹掉，
            //      抛物线变陡（scale > 0 时不那么陡）。以后做空中战斗时再决定要不要放开。
            if (cmd.Jump && IsGrounded && State != PlayerState.Hitstun && State != PlayerState.Dodge)
            {
                VerticalSpeed = _characterData.JumpSpeed;
                IsGrounded = false;   // 立刻离地：从这一 tick 起就不再站在地上了
            }

            // ---- 冲量（外力通道）----
            // 它**不受状态限制**、也绝不写 MoveVelocity：速度和位置各有自己的改写点，
            // 「相邻 tick 无突变」那条不变量靠它保住（HUD 一直在盯，现在只盯自驱水平通道）。
            // 冲量直接加在位置上——位置**允许突变**，被打飞就是要突变。
            // 先移动、再衰减——和水平速度那条 MoveTowards 是同一套逼近写法。
            Position += ImpulseVelocity * dt;
            ImpulseVelocity = Vector3.MoveTowards(
                ImpulseVelocity, Vector3.zero, _globalData.ImpulseDeceleration * dt);

            // ---- 判定 ----
            // 放在**最后**：上面刚更新完 Position，判定体要用这一 tick 的最新位置。
            // 放在前面的话判定会永远慢一 tick，角色移动快时会「穿过去没打到」。
            //
            // 只在攻击状态里跑；没有活跃 hitbox 的 tick 会立刻返回，不花任何代价。
            ResolveHits(actors);
        }

        /// <summary>
        /// 攻击状态里，用**当前 tick 活跃的全部 hitbox** 查所有角色，
        /// 命中的记进本次挥砍的名单（不重复记）并当场结算：扣血 + 给一个击退初速度。
        ///
        /// 去重名单是**按挥砍**的（用户 2026-10-20 定）：同一目标这一刀最多挨一下，
        /// 不管有几个 hitbox 碰到它。多 hitbox 只扩大覆盖，不做多段伤害。
        ///
        /// 目标数组里**包含自己**（Boot 只有一份「全体角色」的数组），开头顶掉自己。
        ///
        /// 一个目标 tick 内可能被多个 hitbox 同时覆盖：按数组顺序取第一个命中的那个，
        /// 记一次、结算一次、break——保证结果确定（不依赖「哪一个先遍历到」之外的运气）。
        /// </summary>
        private void ResolveHits(PlayerSim[] actors)
        {
            if (State != PlayerState.Attack || actors == null) { return; }
            if (_attackData.HitboxCount == 0) { return; }

            int tick = StateTicks;   // SetState 之后的值 = 整招第几 tick

            for (int i = 0; i < actors.Length; i++)
            {
                // 判定目标数组包含全体角色，先把自己摘出去
                if (ReferenceEquals(actors[i], this)) { continue; }

                if (AlreadyHitThisSwing(i)) { continue; }

                for (int h = 0; h < _attackData.HitboxCount; h++)
                {
                    HitboxDefinition box = _attackData.GetHitbox(h);
                    if (!box.IsActiveAt(tick)) { continue; }

                    Vector3 from = HitboxDefinition.LocalPointToWorld(
                        box.LocalFrom, Position, HalfHeight, FacingYaw);
                    Vector3 to = HitboxDefinition.LocalPointToWorld(
                        box.LocalTo, Position, HalfHeight, FacingYaw);

                    if (!HitTest.CapsuleOverlapsSphere(
                        from, to, box.Radius, actors[i].Position, actors[i].Radius))
                    {
                        continue;
                    }

                    // 名单在**几何重叠**这一刻就记（碰到就算用掉，这一刀对它不再重试）——
                    // 判据是几何，不是「它收没收到」：收不收是受害者的事，见 ReceiveHit 的注释。
                    RecordHit(i);

                    // 事件递出去：受害者在自己的下一 tick 开头消费（无敌就丢掉，收下才结算）。
                    // Boot 的推进顺序是「玩家先、其他角色后」，所以沙包在**同一 tick** 就吃到反应。
                    // （击退冲量和顿帧请求都由攻击方按招式配置带出：方向旋到世界系、乘速度；
                    // 顿帧时长随命中记录走——H2。）
                    actors[i].ReceiveHit(
                        _attackData.HitDamage, KnockbackImpulse(), _attackData.HitStopTicks);
                    break;   // 同一目标同一 tick 只结算一次；后续 hitbox 不再重复
                }
            }
        }

        /// <summary>
        /// 算出这一刀给目标的**冲量速度向量**（世界空间）。
        ///
        /// 方向来自招式配置（**攻击者本地坐标**，2026-10-13 起）：按朝向旋到世界系、归一化；
        /// 大小 = <c>AttackData.KnockbackSpeed</c>（米/秒）。旧的「从攻击者指向目标的径向推」
        /// 已成历史——斜着命中不再侧推，方向完全由配置说了算。
        ///
        /// 本地坐标约定：x = 右 / y = 上 / z = 前（Unity 里角色朝 +Z 时的本地向量）。
        /// 旋转用 **sin/cos 手算**（和 <see cref="FacingDirection"/> /
        /// <c>ActorCommand.CameraRelativeDirection</c> 同一套约定：yaw θ 的前方 = (sinθ, 0, cosθ)、
        /// 右方 = (cosθ, 0, -sinθ)）——**不能用 <c>Quaternion.Euler</c>**：它是 ECall（打进 native），
        /// 离线 harness 里会抛 SecurityException（实测 2026-10-13）；手算只有托管数学，两边都能跑。
        /// 绕 Y 旋转不动 y 分量（竖直分量原样带出去）。
        ///
        /// 返回值的 **y 分量是有意义的**：受害者在消费时拆分——水平进冲量通道、
        /// y 进竖直弹道通道。方向全零 = 这一招不推（返回零向量，两条通道都不碰）。
        /// </summary>
        private Vector3 KnockbackImpulse()
        {
            Vector3 local = _attackData.KnockbackDirection;
            float lenSqr = local.sqrMagnitude;

            // 零方向 = 不推（正当配置：轻攻击不击退）。拖到归一化那一步会除零，这里先拦住。
            if (lenSqr < 1e-8f) { return Vector3.zero; }

            local /= Mathf.Sqrt(lenSqr);   // 先归一化（旋转不改长度，顺序无所谓，这样更直观）

            float rad = FacingYaw * Mathf.Deg2Rad;
            float sin = Mathf.Sin(rad);
            float cos = Mathf.Cos(rad);
            Vector3 world = new Vector3(
                local.x * cos + local.z * sin,
                local.y,
                -local.x * sin + local.z * cos);

            return world * _attackData.KnockbackSpeed;
        }

        /// <summary>
        /// 把角度 <paramref name="current"/> 朝 <paramref name="target"/> 转最多
        /// <paramref name="maxStepDegrees"/> 度（**最短方向**，一步不超剩余误差 → 落地即吸附、
        /// 不过冲、不来回抖）。和相机锁定的 TurnStep 是同一套写法。
        ///
        /// <paramref name="maxStepDegrees"/> ≤ 0 时返回原值——块 F1 的「转向速度 0 = 锁死」
        /// 就是靠它，不额外写分支。
        ///
        /// 最短角差用 <c>Mathf.Repeat</c> 折（托管实现，离线 harness 可用；和
        /// <c>MoveDirectionResolver</c> 同一招）。差恰好 180°（正对面）时取法固定落向
        /// **负方向**——行为确定、可测。
        /// </summary>
        private static float StepTowardsAngle(float current, float target, float maxStepDegrees)
        {
            if (maxStepDegrees <= 0f) { return current; }

            // 折进 [-180, 180)：先 Repeat 到 [0, 360)，再减 180。
            float delta = Mathf.Repeat(target - current + 180f, 360f) - 180f;

            float step = Mathf.Min(Mathf.Abs(delta), maxStepDegrees);
            return delta < 0f ? current - step : current + step;
        }
    }
}
