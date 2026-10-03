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
        /// </summary>
        /// <param name="restart">
        /// true = 「还是这个状态，但重新开始计时」。攻击接招就属于这种：
        /// 状态没变（还是 <see cref="PlayerState.Attack"/>），但那是**新的一次**攻击，
        /// 计时必须归零。不加这个开关的话它会被当成「留在原状态」而把计时 +1，
        /// 接出来的第二招就短了。
        /// </param>
        private void SetState(PlayerState next, bool restart = false)
        {
            if (!restart && next == State)
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
        public void Tick(InputFrame frame, float cameraYaw, float dt)
        {
            PreviousPosition = Position;   // 先记下旧位置，供 View 插值

            Vector3 direction = CameraRelativeDirection(frame.MoveX, frame.MoveY, cameraYaw);
            bool hasMoveInput = direction.sqrMagnitude > 0f;

            // ---- 预输入：先把按下的键记下来，能不能用交给状态决定 ----
            //
            // 用「剩余有效期倒计时」而不是「记下按下的 tick 号、用的时候相减」：
            // 后者要么读 frame.Tick（把「帧号对不对」变成 Sim 的隐式依赖），
            // 要么自己维护一个全局 tick 计数器。倒计时只需要一个 int，
            // 而且 Tick 仍然只依赖传进来的参数。
            if (frame.Attack)
            {
                _attackBufferTicksLeft = SimConstants.AttackInputBufferTicks;
            }
            else if (_attackBufferTicksLeft > 0)
            {
                _attackBufferTicksLeft--;
            }

            bool hasBufferedAttack = _attackBufferTicksLeft > 0;

            // ---- 转移：由**当前状态**分派，不是每 tick 重算 ----
            //
            // Step 5 时这里是一行 `State = 有输入 ? Move : Idle` 就完事，因为只有两个状态、
            // 判据只看输入。加了 Attack 之后那行就错了：它每 tick 无条件重算，
            // 玩家在攻击中碰一下方向键，下一 tick 就会被它改回 Move，攻击根本挂不住。
            //
            // 所以改成「当前状态决定能去哪」。代价是转移规则散在了 case 里，
            // 状态再多就该换成转移表了——M6 的事。
            PlayerState next;
            bool restart = false;

            switch (State)
            {
                case PlayerState.Attack:
                    // 攻击占 StateTicks 0..29。判断有两处，长得像但差一：
                    //   收招窗口开：最后 3 tick（27/28/29）→ >= 30 - 3 = 27
                    //   攻击结束  ：再下一 tick（30）      → >= 30 - 1 = 29
                    bool cancelWindowOpen = StateTicks
                        >= SimConstants.AttackDurationTicks - SimConstants.AttackCancelWindowTicks;

                    if (cancelWindowOpen && hasBufferedAttack)
                    {
                        // 接招：状态还是 Attack，但这是**新的一次**，所以 restart = true 让计时归零。
                        // 预输入在这里被消耗掉，所以按住不放也只会接出这一招，
                        // 要再接必须再按一次——这就是「快速连按不会一击触发两次」。
                        _attackBufferTicksLeft = 0;
                        next = PlayerState.Attack;
                        restart = true;
                        break;
                    }

                    next = StateTicks >= SimConstants.AttackDurationTicks - 1
                        ? (hasMoveInput ? PlayerState.Move : PlayerState.Idle)
                        : PlayerState.Attack;
                    break;

                default:   // PlayerState.Idle / PlayerState.Move
                    // 这两个状态之间由**输入**决定，不是速度：玩家松手的那一刻就回 Idle，
                    // 哪怕角色还在按减速度滑行。用速度判的话，松手后要等 0.2 秒速度精确归零
                    // 才肯离开 Move，那 0.2 秒里状态说的是「他在移动」，而玩家早松手了。
                    next = frame.Attack
                        ? PlayerState.Attack
                        : (hasMoveInput ? PlayerState.Move : PlayerState.Idle);
                    break;
            }

            SetState(next, restart);

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

                // Idle 和 Attack 现在行为相同（都停下），但仍然分开写：
                // 哪天攻击要带前冲（位移突进）或者受击要带击退，就是从这里拆出独立分支。
                // 合并成一个 case 的话，那天的改动会变成「先把它拆开」。
                case PlayerState.Idle:
                case PlayerState.Attack:
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
