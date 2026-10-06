using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// **一个角色**的模拟状态与每 tick 的运动学更新。
    ///
    /// 场上每个角色一个实例，玩家只是其中一个——区别只在于「谁给它喂命令」：
    /// 玩家那个喂 <c>ActorCommand.FromPlayerInput(...)</c>，沙包（还没有脑子的角色）
    /// 喂一个什么都不做的命令。从这个块起它也是**受害者**：别人的判定帧会打到它身上
    /// （<see cref="ApplyHit"/>），它带着和玩家一模一样的受击反应（硬直 / 击退 / 无敌帧）。
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

        /// <summary>水平速度，米/秒。y 分量始终为 0（这一步骤不做跳跃/重力）。</summary>
        public Vector3 Velocity;

        /// <summary>
        /// 击退速度，米/秒。受击时被赋值，之后每 tick 按 <c>SimConstants.KnockbackDeceleration</c> 衰减到零。
        ///
        /// 它和 <see cref="Velocity"/> 是**两条独立的通道**，互不干扰：
        /// `Velocity` 是「玩家自己走出来的速度」，由状态机决定目标值，受「相邻 tick 无突变」
        /// 那条不变量约束（HUD 一直在盯它）；击退是**外力**，直接加在位置上，不受状态限制。
        /// 硬直 12 tick 比滑完（约 19 tick）短，所以玩家会「在滑行中恢复控制」。
        ///
        /// 和 <c>TargetSim.KnockbackVelocity</c> 是同一套写法、同一个减速度。
        /// </summary>
        public Vector3 KnockbackVelocity;

        /// <summary>血量上限。构造时从 <c>SimConstants.PlayerMaxHealth</c> 取。</summary>
        public int MaxHealth { get; private set; }

        /// <summary>当前血量。**会被夹在 0 以上**——这一块不做死亡，但血量不该是负的。</summary>
        public int Health { get; private set; }

        /// <summary>
        /// 把角色近似成一个球的半径，米。**这是「被别人打中」用的几何体**——
        /// 和靶子时代的取舍一样：方块被近似成球，角落打不到（边长 1 的方块内切球 0.5）。
        /// 攻击方用「胶囊 vs 球」判定（<c>HitTest</c>），两边都是球，算式不用动。
        /// 现在所有角色都是 0.5（一个方块），由 Boot 在构造时给。
        /// </summary>
        public float Radius { get; private set; }

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

        /// <summary>攻击键预输入的剩余有效期。见 <see cref="SimConstants.AttackInputBufferTicks"/>。</summary>
        private int _attackBufferTicksLeft;

        /// <summary>
        /// 角色朝向，绕 Y 轴的偏航角（度）。<c>0</c> = 朝世界 +Z 方向，<c>90</c> = 朝 +X。
        ///
        /// 更新规则**一条**：有移动输入 **且** 不在攻击中时才更新。三个后果：
        /// 移动时跟输入方向、站定时保持不动、攻击中锁定。
        ///
        /// 攻击中锁定是有意的：不锁的话，挥到一半转个身，判定胶囊会在判定帧中途换方向。
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
        /// 这一 tick 是否处于**判定帧**（攻击三段的中间那段）。
        ///
        /// Step 8 的判定就挂在这个属性上——不用再算时间轴，7b 拆状态时已经把「什么时候是判定帧」
        /// 表达成状态了。
        /// </summary>
        public bool IsHitFrame => State == PlayerState.AttackActive;

        /// <summary>判定胶囊起点（世界坐标）。只在 <see cref="IsHitFrame"/> 为真时有意义。</summary>
        public Vector3 HitCapsuleFrom => Position + FacingDirection * _attackData.HitStartOffset;

        /// <summary>判定胶囊终点（世界坐标）。只在 <see cref="IsHitFrame"/> 为真时有意义。</summary>
        public Vector3 HitCapsuleTo => Position + FacingDirection * _attackData.HitEndOffset;

        /// <summary>判定胶囊半径，米。</summary>
        public float HitCapsuleRadius => _attackData.HitRadius;

        /// <summary>
        /// 上一 tick 收到的攻击数据。
        ///
        /// 存一份是为了让 <see cref="HitCapsuleFrom"/> 这些属性有数可算——它们是从 Tick **外面**
        /// 读的（View、Gizmo），拿不到 Tick 的参数。这份缓存只是参数的值拷贝，
        /// 不影响「Tick 只依赖传入参数」这条性质，重放同样的参数仍然得到同样的结果。
        /// </summary>
        private AttackData _attackData;

        /// <summary>三个攻击状态之一。</summary>
        public static bool IsAttacking(PlayerState state)
        {
            return state == PlayerState.AttackStartup
                || state == PlayerState.AttackActive
                || state == PlayerState.AttackRecovery;
        }

        /// <summary>
        /// 本次挥砍到目前为止打中过**几个不同的**目标。起手时清零，所以它不会跨招累积。
        ///
        /// 「同一招式不重复命中同一目标」靠的就是它背后的名单——判定帧有 6 个 tick，
        /// 靶子一直站在胶囊里的话，不做记录就会命中 6 次。
        /// </summary>
        public int HitTargetCount => _hitTargetCount;

        /// <summary>
        /// 这一 tick 有没有**新命中**。每 tick 在 <see cref="Tick"/> 开头重置，
        /// <see cref="ResolveHits"/> 记下一个新目标时置真。
        ///
        /// 和 <see cref="HitTargetCount"/> 的区别：那个是本次挥砍的**累计值**，
        /// 这个是**事件信号**——只回答「刚刚打中了没有」，不回答「一共打中几个」。
        /// 消费者是 Boot 层的顿帧：它必须在命中的**那一 tick** 触发。
        ///
        /// 为什么不让 Boot 去 diff 上一 tick 的 <see cref="HitTargetCount"/>：
        /// 那是把事件当状态轮询，能用但方向错了（见 ROADMAP「命中事件流」）。
        /// 这是事件流的最小版本；等真需要音效/飘字时再扩成带伤害和方向的 HitEvent。
        ///
        /// 注意：顿帧期间 <see cref="Tick"/> 整个不跑，这个值会停在「上一次执行」的结果——
        /// 但读它的 Boot 只在 Tick 跑过的 tick 上读，所以不会重复触发。
        /// </summary>
        public bool HitThisTick { get; private set; }

        // ---- 受击 ----

        /// <summary>无敌还剩几个 tick。> 0 时命中完全无效。供调试 HUD 显示。</summary>
        public int InvincibleTicksLeft => _invincibleTicksLeft;

        private int _invincibleTicksLeft;

        /// <summary>
        /// 待处理的受击：由 <see cref="ApplyHit"/> 装填、由 <see cref="Tick"/> 开头消费。
        /// 为什么要隔一拍——见 <see cref="ApplyHit"/> 的注释。
        /// </summary>
        private bool _hasPendingHit;
        private int _pendingHitDamage;
        private Vector3 _pendingHitKnockback;

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
        /// ⚠️ 记录发生在「几何重叠」时，不是「伤害真的落下」时——对手在无敌期间被砍中，
        /// 也会占掉这次挥砍对它的名额（之后几 tick 不重试）。见 DEVLOG 块 P2a 的残留问题。
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
        /// <param name="radius">被判定打中用的球半径，米。玩家和沙包现在都是 0.5（一个方块）。</param>
        public PlayerSim(Vector3 spawnPosition, float radius)
        {
            Position = spawnPosition;
            PreviousPosition = spawnPosition;
            Velocity = Vector3.zero;
            Radius = radius;
            MaxHealth = SimConstants.PlayerMaxHealth;
            Health = SimConstants.PlayerMaxHealth;
        }

        /// <summary>
        /// 切状态，并维护 <see cref="StateTicks"/>：换状态清零，留在原状态则 +1。
        ///
        /// 所有状态的改变都必须走这一个口子——这样 <see cref="StateTicks"/> 不可能忘记维护，
        /// 出问题时也只有一个地方要看。
        ///
        /// 块 6b 时这个方法还有一个 <c>restart</c> 参数，用来处理「攻击接招时状态没变、
        /// 但那是新的一次攻击，计时必须归零」。7b 把攻击拆成三个状态之后它就不需要了：
        /// 接招是 <see cref="PlayerState.AttackRecovery"/> → <see cref="PlayerState.AttackStartup"/>，
        /// 是一次**正常的状态切换**，清零是本来就有的行为。参数随之删掉。
        /// </summary>
        private void SetState(PlayerState next)
        {
            if (next == State)
            {
                StateTicks++;
                return;
            }

            State = next;
            StateTicks = 0;
        }

        /// <summary>
        /// 挨一下：扣血、进 <see cref="PlayerState.Hitstun"/>（**打断当前一切**）、被击退，
        /// 之后 <c>SimConstants.InvincibilityTicks</c> 个 tick 内的命中全部无效。
        ///
        /// **只装填，不立刻生效**——真正的结算在下一个 <see cref="Tick"/> 的开头
        /// （<see cref="ConsumePendingHit"/>）。两个理由：
        ///  1. 「状态转移只发生在 Tick 里」这条性质不破——从外面看不到「tick 之外状态变了」；
        ///  2. 和输入同一条思路：调用方（现在是调试触发，以后是敌人的判定帧）随便什么时候调用，
        ///     世界在下一 tick 才变——像输入缓冲一样，是「上游采集 → tick 消费」的传送带。
        ///
        /// 签名和 <c>TargetSim.ApplyHit</c> 一致（伤害 + 击退初速度）：
        /// 击退速度由攻击方算好（那边才知道推力多大、朝哪推）。
        ///
        /// 已知限制：同一 tick 内多次调用只有**最后一次**生效。现在是单攻击源，够用；
        /// 多敌人时改成「结算列表」（两次伤害都该扣，击退取合力）。
        /// </summary>
        public void ApplyHit(int damage, Vector3 knockbackVelocity)
        {
            _hasPendingHit = true;
            _pendingHitDamage = damage;
            _pendingHitKnockback = knockbackVelocity;
        }

        /// <summary>
        /// 结算待处理的受击，返回是否**因此进入**硬直（= 该打断当前状态）。
        /// 无敌期间的命中在这里被**整体作废**——不掉血、不推、不进硬直，这才是无敌帧的意义。
        /// </summary>
        private bool ConsumePendingHit()
        {
            if (!_hasPendingHit) { return false; }
            _hasPendingHit = false;

            if (_invincibleTicksLeft > 0) { return false; }   // 无敌：这一次命中完全作废

            Health -= _pendingHitDamage;
            if (Health < 0) { Health = 0; }

            KnockbackVelocity = _pendingHitKnockback;
            _invincibleTicksLeft = SimConstants.InvincibilityTicks;
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
        /// 攻击的三段时长。**每 tick 传进来而不是构造时存起来**，有两个原因：
        /// 一是播放中改 Inspector 的数字要立刻生效；
        /// 二是 `Tick` 保持「只依赖参数」，重放同一串参数必然得到同一结果。
        /// 它是 struct，所以这里传的是值拷贝，没有堆分配。
        /// </param>
        /// <param name="actors">
        /// 场上**所有**角色（含自己——判定时会跳过自己）。
        /// 传数组而不是「自己持有一份」：角色不该知道世界上有哪些人，
        /// 而且这样 `Tick` 仍然只是「传入参数的函数」，重放同一串参数必然同一结果。
        /// **顺序必须稳定**——判定去重名单存的是这个数组里的下标，顺序一变下标就不再是身份。
        ///
        /// 用数组长度当数量，不另传 count——省一个参数，代价是数组里不能有空位。
        /// </param>
        public void Tick(ActorCommand cmd, float dt, AttackData attackData, PlayerSim[] actors)
        {
            PreviousPosition = Position;   // 先记下旧位置，供 View 插值
            _attackData = attackData;      // 缓存给判定体属性用，见字段注释
            HitThisTick = false;           // 事件信号只在 Tick 内有效：开头清零，命中时置真

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

            // ---- 受击**优先**：被打中就打断一切 ----
            // 注意是「**不评估**原状态的转移」，不是「评估完再覆盖 next」——
            // switch 会在中途写 startsAttack 这类副作用，覆盖时得逐个清掉，迟早漏一个。
            if (ConsumePendingHit())
            {
                next = PlayerState.Hitstun;
            }
            else
            {
                // 每段的判断都长这样：`StateTicks >= 本段时长 - 1`。
                // `- 1` 是因为 StateTicks 从 0 起（进入那一 tick 记 0），
                // 所以「占满 8 个 tick」的最后一 tick 读数是 7。
                // 填 0 的段：`StateTicks >= -1` 恒真，下一 tick 就走——实际占 1 tick，
                // 和 AttackData.TotalTicks 的算法（每段至少 1）对得上。
                switch (State)
                {
                    case PlayerState.AttackStartup:
                        next = StateTicks >= attackData.StartupTicks - 1
                            ? PlayerState.AttackActive
                            : PlayerState.AttackStartup;
                        break;

                    case PlayerState.AttackActive:
                        next = StateTicks >= attackData.ActiveTicks - 1
                            ? PlayerState.AttackRecovery
                            : PlayerState.AttackActive;
                        break;

                    case PlayerState.AttackRecovery:
                        // 取消窗口开在**后摇段自己的**最后 3 tick：
                        //   StateTicks 0 .. RecoveryTicks-1，最后 3 tick 是 RecoveryTicks-3 起
                        bool cancelWindowOpen = StateTicks
                            >= attackData.RecoveryTicks - SimConstants.AttackCancelWindowTicks;

                        if (cancelWindowOpen && hasAttackRequest)
                        {
                            // 接招。三个状态之后这就是一次普通转移：
                            // 后摇 → 前摇，StateTicks 由 SetState 清零，不需要特殊处理。
                            next = PlayerState.AttackStartup;
                            startsAttack = true;
                            break;
                        }

                        next = StateTicks >= attackData.RecoveryTicks - 1
                            ? (hasMoveInput ? PlayerState.Move : PlayerState.Idle)
                            : PlayerState.AttackRecovery;
                        break;

                    case PlayerState.Hitstun:
                        // 硬直是**计时退出**：走完就交还给输入（和攻击收招的出口同一条规则）。
                        // 没有取消窗口、没有预输入——硬直期间按下的攻击由**记账**存进缓冲，
                        // 下一条 if 链会管，不在这里特殊处理。
                        next = StateTicks >= SimConstants.HitstunTicks - 1
                            ? (hasMoveInput ? PlayerState.Move : PlayerState.Idle)
                            : PlayerState.Hitstun;
                        break;

                    default:   // PlayerState.Idle / PlayerState.Move
                        // 这两个状态之间由**输入**决定，不是速度：玩家松手的那一刻就回 Idle，
                        // 哪怕角色还在按减速度滑行。用速度判的话，松手后要等 0.2 秒速度精确归零
                        // 才肯离开 Move，那 0.2 秒里状态说的是「他在移动」，而玩家早松手了。
                        if (hasAttackRequest)
                        {
                            next = PlayerState.AttackStartup;
                            startsAttack = true;
                        }
                        else
                        {
                            next = hasMoveInput ? PlayerState.Move : PlayerState.Idle;
                        }
                        break;
                }
            }

            SetState(next);

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
            // 攻击中仍然锁定朝向。这条是**可以调的**：不锁的话挥到一半转视角，
            // 判定胶囊会在判定帧中途换方向。第一人称下玩家可能更希望「我转视角就打那边」，
            // 验收时觉得别扭就把这个判断去掉。
            //
            // 判断条件是**前后两边都看**：只要这一 tick 的前后至少有一边不在攻击中，就跟随命令。
            //   Idle/Move → 攻击     ：跟（**起手那一 tick 就对齐到按下时看到的方向**，然后锁）
            //   攻击中（含段间转移、接招）：不跟（锁定）
            //   攻击 → Idle/Move     ：跟（收招立刻跟上，不用等下一 tick）
            //
            // 为什么不能只看新状态：起手那一 tick 新状态已经是 AttackStartup，
            //   那样朝向会保持「上一次更新时的值」而不是「按下时看到的方向」。
            //   正常游玩两者只差一个 tick，但那是碰巧 —— 相机一开始就和角色朝向不同就会露馅。
            // 为什么不能只看旧状态：收招那一 tick 旧状态还是 AttackRecovery，
            //   那样收招后要等一 tick 才跟上，快速转头时能感觉到黏。
            if (!wasAttacking || !IsAttacking(State))
            {
                FacingYaw = cmd.FacingYaw;
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
                _attackBufferTicksLeft = SimConstants.AttackInputBufferTicks;   // 没用掉，存着
            }
            else if (_attackBufferTicksLeft > 0)
            {
                _attackBufferTicksLeft--;
            }

            // ---- 行为 ----
            // 状态只决定「朝哪个目标速度逼近、多快逼近」，**不直接赋值 Velocity**。
            // 速度的唯一改写点是下面那行 MoveTowards，所以任何切换都不可能造成速度突变：
            //   Idle → Move：上一 tick 速度已经是 6，这一 tick 目标还是 6，MoveTowards 原地不动；
            //   Move → Attack：上一 tick 速度是 6，这一 tick 目标 0，从 6 按减速度往下拉，连续；
            //   Attack → Move：停稳时速度是 0，从 0 按加速度往上拉，连续。
            // 用 MoveTowards 而不是 Lerp：Lerp 是指数衰减，永远到不了目标值。
            Vector3 targetVelocity;
            float rate;
            switch (State)
            {
                case PlayerState.Move:
                    targetVelocity = direction * SimConstants.MaxMoveSpeed;
                    rate = SimConstants.MoveAcceleration;
                    break;

                // 五个状态现在行为相同（都停下），但仍然一条条列出来：
                // 哪天攻击要带前冲（位移突进），就是从这里拆出独立分支。
                // 击退不走这里——它是独立通道（见下面的注释），加在速度积分之外。
                // 合并成 `case Idle: case AttackStartup: ...` 一行更短，但那样改动点就藏起来了。
                case PlayerState.Idle:
                case PlayerState.AttackStartup:
                case PlayerState.AttackActive:
                case PlayerState.AttackRecovery:
                case PlayerState.Hitstun:
                    targetVelocity = Vector3.zero;
                    rate = SimConstants.MoveDeceleration;
                    break;

                default:
                    // 只为满足「局部变量必须赋值」而存在（C# 不对枚举做穷尽性检查）。
                    // ⚠️ 新增状态忘了在上面加 case 会静默落到这里、表现成 Idle 的样子——
                    // 不出招、不报错。离线测试里有一条专门盯这个。
                    targetVelocity = Vector3.zero;
                    rate = SimConstants.MoveDeceleration;
                    break;
            }

            Velocity = Vector3.MoveTowards(Velocity, targetVelocity, rate * dt);
            Position += Velocity * dt;

            // ---- 击退（独立通道）----
            // 它**不受状态限制**、也绝不写 Velocity：上面那条 MoveTowards 是 Velocity 的唯一改写点，
            // 「相邻 tick 速度无突变」那条不变量靠它保住（HUD 一直在盯）。
            // 击退直接加在位置上——位置**允许突变**，被打飞就是要突变。
            // 先移动、再衰减，和 TargetSim.Tick 是同一套写法、同一个减速度。
            Position += KnockbackVelocity * dt;
            KnockbackVelocity = Vector3.MoveTowards(
                KnockbackVelocity, Vector3.zero, SimConstants.KnockbackDeceleration * dt);

            // ---- 判定 ----
            // 放在**最后**：上面刚更新完 Position，判定胶囊要用这一 tick 的最新位置。
            // 放在前面的话判定会永远慢一 tick，角色移动快时会「穿过去没打到」。
            //
            // 只在判定帧跑。前摇和收招期间这一段整个跳过，不花任何代价。
            ResolveHits(actors);
        }

        /// <summary>
        /// 判定帧里，用判定胶囊去查**所有角色**，命中的记进本次挥砍的名单（不重复记）
        /// 并**当场结算**：扣血 + 给一个击退初速度。
        ///
        /// 目标数组里**包含自己**（Boot 只有一份「全体角色」的数组），开头顶掉自己：
        /// 胶囊从身前 0.6 米开始、半径 0.5，不顶掉的话自己就在判定里。
        ///
        /// 判定帧通常有 6 个 tick，而角色在这几 tick 里可能还在滑行、胶囊在动，
        /// 所以**每个 tick 都要查一遍**——只在第一 tick 查的话，对手晚一点进入范围就打不到了。
        /// 正因为要查 6 次，去重名单才是必需的：不做的话同一个对手会被记 6 次、
        /// **被扣 6 次血**。
        /// </summary>
        private void ResolveHits(PlayerSim[] actors)
        {
            if (!IsHitFrame || actors == null) { return; }

            Vector3 from = HitCapsuleFrom;
            Vector3 to = HitCapsuleTo;
            float radius = HitCapsuleRadius;

            for (int i = 0; i < actors.Length; i++)
            {
                // 判定目标数组包含全体角色，先把自己摘出去
                if (ReferenceEquals(actors[i], this)) { continue; }

                if (AlreadyHitThisSwing(i)) { continue; }

                if (!HitTest.CapsuleOverlapsSphere(from, to, radius, actors[i].Position, actors[i].Radius))
                {
                    continue;
                }

                RecordHit(i);
                HitThisTick = true;

                // 结算要**当场**给出去，不能等判定帧结束再统一结算：
                // 受害者那边是 pending 槽，它的下一次 Tick 开头就吃到——而 Boot 的推进顺序是
                // 「玩家先、其他角色后」，所以沙包在**同一 tick** 就反应，打击感不慢半拍。
                // （击退速度由攻击方算：只有它知道推力多大、朝哪推。）
                actors[i].ApplyHit(_attackData.HitDamage, KnockbackVelocityTowards(actors[i].Position));
            }
        }

        /// <summary>
        /// 算出把目标推开所需的**初速度**。
        ///
        /// 参数配的是「推开多少米」（人按距离想），而物理要的是初速度，两者之间差一个公式。
        /// 匀减速直线运动：位移 <c>d = v² / (2a)</c>，所以反解出 <c>v = sqrt(2ad)</c>。
        ///
        /// 这样做的好处是**参数即结果**：Inspector 里填 1 米，对手就后退约 1 米，
        /// 验收时可以直接对数字，不用心算「速度 4 减速度 20 等于多远」。
        /// （离散积分会有一点误差，和玩家位移 5.75 vs 解析值 5.70 是同一回事。）
        /// </summary>
        private Vector3 KnockbackVelocityTowards(Vector3 targetPosition)
        {
            // 水平方向：从攻击者指向目标。y 抹掉——这一块没有竖直击退。
            Vector3 push = targetPosition - Position;
            push.y = 0f;

            // 两者水平位置重合时方向没有意义，用**朝向**兜底（总得给个方向，不能是零向量）。
            if (push.sqrMagnitude < 1e-8f)
            {
                push = FacingDirection;
            }
            else
            {
                push /= Mathf.Sqrt(push.sqrMagnitude);
            }

            float speed = Mathf.Sqrt(2f * SimConstants.KnockbackDeceleration * _attackData.KnockbackDistance);
            return push * speed;
        }
    }
}
