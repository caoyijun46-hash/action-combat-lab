using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// 玩家的模拟状态与每 tick 的运动学更新。
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
        /// 和 <see cref="CameraRelativeDirection"/> 用的是同一套角度约定：
        /// 偏航 θ 的方向向量是 <c>(sinθ, 0, cosθ)</c>。两处必须是同一个约定，
        /// 否则「朝移动方向」和「朝相机前方」会对不上。
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

        /// <summary>
        /// 本次挥砍已经打中过的目标（存的是**在 targets 数组里的下标**）。
        ///
        /// 用下标当身份，而不是给 TargetSim 加个 Id 字段：靶子数组在场景里一次建好、顺序不变，
        /// 下标就是稳定身份。**代价是这条约定必须守住**——哪天靶子会动态增删，
        /// 下标就会变，得改成真正的 Id。
        ///
        /// 固定容量、零分配。上限 8 是「一次挥砍最多同时打中几个」——
        /// 超出的直接不记（那一刀仍然会造成伤害，只是不会被去重，属于极端情况）。
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

        public PlayerSim(Vector3 spawnPosition)
        {
            Position = spawnPosition;
            PreviousPosition = spawnPosition;
            Velocity = Vector3.zero;
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
        /// 推进一个 tick。<paramref name="dt"/> 只从参数来，绝不读 <c>Time.xxx</c>。
        /// </summary>
        /// <param name="frame">这一 tick 的输入。</param>
        /// <param name="cameraYaw">相机绕 Y 轴的偏航角（度）。由 Boot 层从相机取来传进来，Sim 不接触 Camera。</param>
        /// <param name="dt">固定步长，秒。</param>
        /// <param name="attackData">
        /// 攻击的三段时长。**每 tick 传进来而不是构造时存起来**，有两个原因：
        /// 一是播放中改 Inspector 的数字要立刻生效；
        /// 二是 `Tick` 保持「只依赖参数」，重放同一串参数必然得到同一结果。
        /// 它是 struct，所以这里传的是值拷贝，没有堆分配。
        /// </param>
        /// <param name="targets">
        /// 场景里所有可被攻击的目标。**只读**——8b 只判定，不改它们（掉血和击退是 8c）。
        /// 传数组而不是「PlayerSim 持有一份」：玩家不该知道世界上有哪些敌人，
        /// 而且这样 `Tick` 仍然只是「传入参数的函数」，重放同一串参数必然同一结果。
        ///
        /// 用数组长度当数量，不另传 count——省一个参数，代价是数组里不能有空位。
        /// </param>
        public void Tick(InputFrame frame, float cameraYaw, float dt, AttackData attackData, TargetSim[] targets)
        {
            PreviousPosition = Position;   // 先记下旧位置，供 View 插值
            _attackData = attackData;      // 缓存给判定体属性用，见字段注释
            HitThisTick = false;           // 事件信号只在 Tick 内有效：开头清零，命中时置真

            Vector3 direction = CameraRelativeDirection(frame.MoveX, frame.MoveY, cameraYaw);
            bool hasMoveInput = direction.sqrMagnitude > 0f;

            // ---- 有没有「待发的攻击请求」----
            // 两个来源合起来算：这一 tick 之前缓冲里还剩的，加上这一 tick 刚按下的。
            // 这里读的是**还没有更新**的缓冲值，正是我们要的语义。
            bool hasAttackRequest = _attackBufferTicksLeft > 0 || frame.Attack;

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

            SetState(next);

            // ---- 朝向 ----
            // 视角改造（C1）之后：**朝向 = 相机朝向**。
            //
            // 块 8a 时这里是「有移动输入就跟移动方向」。改成跟相机之后，
            // **按 S 是倒退走，不是转身朝后走** —— 这是第一人称/过肩第三人称该有的行为，
            // 也是「一条规则三个后果」那个方案的最后一环：移动时跟输入那条已经不需要了。
            //
            // 「跟一定距离内的敌人、没敌人跟鼠标」里的前半段还没做，等有了敌人再说。
            //
            // 攻击中仍然锁定朝向。这条是**可以调的**：不锁的话挥到一半转鼠标，
            // 判定胶囊会在判定帧中途换方向。第一人称下玩家可能更希望「我转视角就打那边」，
            // 验收时觉得别扭就把这个判断去掉。
            //
            // 判断条件是**前后两边都看**：只要这一 tick 的前后至少有一边不在攻击中，就跟随相机。
            //   Idle/Move → 攻击     ：跟（**起手那一 tick 就对齐到按下时看的方向**，然后锁）
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
                FacingYaw = cameraYaw;
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
            else if (frame.Attack)
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

                // 四个状态现在行为相同（都停下），但仍然一条条列出来：
                // 哪天攻击要带前冲（位移突进）或者受击要带击退，就是从这里拆出独立分支。
                // 合并成 `case Idle: case AttackStartup: ...` 一行更短，但那样改动点就藏起来了。
                case PlayerState.Idle:
                case PlayerState.AttackStartup:
                case PlayerState.AttackActive:
                case PlayerState.AttackRecovery:
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

            // ---- 判定 ----
            // 放在**最后**：上面刚更新完 Position，判定胶囊要用这一 tick 的最新位置。
            // 放在前面的话判定会永远慢一 tick，角色移动快时会「穿过去没打到」。
            //
            // 只在判定帧跑。前摇和收招期间这一段整个跳过，不花任何代价。
            ResolveHits(targets);
        }

        /// <summary>
        /// 判定帧里，用判定胶囊去查所有目标，命中的记进本次挥砍的名单（不重复记）
        /// 并**当场结算**：扣血 + 给一个击退初速度。
        ///
        /// 判定帧通常有 6 个 tick，而角色在这几 tick 里可能还在滑行、胶囊在动，
        /// 所以**每个 tick 都要查一遍**——只在第一 tick 查的话，靶子晚一点进入范围就打不到了。
        /// 正因为要查 6 次，去重名单才是必需的：不做的话同一个靶子会被记 6 次、
        /// **被扣 6 次血**。
        /// </summary>
        private void ResolveHits(TargetSim[] targets)
        {
            if (!IsHitFrame || targets == null) { return; }

            Vector3 from = HitCapsuleFrom;
            Vector3 to = HitCapsuleTo;
            float radius = HitCapsuleRadius;

            for (int i = 0; i < targets.Length; i++)
            {
                if (AlreadyHitThisSwing(i)) { continue; }

                if (!HitTest.CapsuleOverlapsSphere(from, to, radius, targets[i].Position, targets[i].Radius))
                {
                    continue;
                }

                RecordHit(i);
                HitThisTick = true;

                // 结算要**当场**做，不能等判定帧结束再统一结算：
                // 这一 tick 就把击退速度给出去，targets 的 Tick 会在同一 tick 让它开始移动，
                // 打上去立刻有反应。攒到后面结算会让打击感慢半拍。
                targets[i].ApplyHit(_attackData.HitDamage, KnockbackVelocityTowards(targets[i].Position));
            }
        }

        /// <summary>
        /// 算出把目标推开所需的**初速度**。
        ///
        /// 参数配的是「推开多少米」（人按距离想），而物理要的是初速度，两者之间差一个公式。
        /// 匀减速直线运动：位移 <c>d = v² / (2a)</c>，所以反解出 <c>v = sqrt(2ad)</c>。
        ///
        /// 这样做的好处是**参数即结果**：Inspector 里填 1 米，靶子就后退约 1 米，
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

        /// <summary>
        /// 把「相机空间的输入方向」转成世界方向。
        ///
        /// 相机偏航 θ 时，它的前方向量是 (sinθ, 0, cosθ)，右方向量是 (cosθ, 0, -sinθ)。
        /// 输入 (moveX, moveY) 是「右多少、前多少」，所以直接按这两个基向量合成。
        ///
        /// 结果长度会被限制在 1 以内：键盘斜按 W+D 会得到长度 √2，(1,1) 不能比单按 W 跑得快；
        /// 而摇杆推一半（长度 0.5）要保留——那是「慢走」的语义。
        /// </summary>
        public static Vector3 CameraRelativeDirection(float moveX, float moveY, float cameraYaw)
        {
            float rad = cameraYaw * Mathf.Deg2Rad;
            float sin = Mathf.Sin(rad);
            float cos = Mathf.Cos(rad);

            Vector3 forward = new Vector3(sin, 0f, cos);
            Vector3 right = new Vector3(cos, 0f, -sin);

            Vector3 direction = forward * moveY + right * moveX;

            // 只压长度、不改方向：saturate 而不是 normalize。
            float sqr = direction.sqrMagnitude;
            if (sqr > 1f)
            {
                direction /= Mathf.Sqrt(sqr);
            }

            return direction;
        }
    }
}
