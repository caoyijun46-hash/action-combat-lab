using ActionGame.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ActionGame.Inputs
{
    /// <summary>
    /// 输入采集：把 Input System 的状态翻译成模拟层认识的 <see cref="InputFrame"/>。
    ///
    /// 三种输入用三种传递方式，别混（见 AGENTS.md「架构铁律 → 4. 输入」）：
    ///   Move   —— 状态。Update 里**覆盖**字段，tick 直接读。读晚了拿到的是更新的真相，不是过期的旧值。
    ///   Look   —— 增量。Update 里**累加**到余额，tick 一次性用掉后清零。可加，所以用加法不用队列。
    ///   Attack —— 离散事件。Update 里**入队**，tick 每 tick 出队一个。不可加，按 2 次 ≠ 1 次的双倍。
    ///   Jump   —— 离散事件，但「同一 tick 内合并无损失」（16ms 里按两下本来也只该跳一次），
    ///             所以**锁存一个 bool**就够了，不占队列（见 <see cref="InputFrame.Jump"/> 的注释）。
    ///
    /// 采样只在渲染帧做（Update）；FixedUpdate 里一个字都不写，全项目只有 BattleLoop 有 FixedUpdate。
    /// </summary>
    public sealed class InputSampler : MonoBehaviour
    {
        private const string MapName = "Player";
        private const string MoveActionName = "Move";
        private const string LookActionName = "Look";
        private const string AttackActionName = "Attack";
        private const string JumpActionName = "Jump";

        [SerializeField] private InputActionAsset _actions;

        private InputActionMap _map;
        private InputAction _move;
        private InputAction _look;
        private InputAction _attack;
        private InputAction _jump;

        // Jump —— 事件，锁存。Update 置位、BuildFrame 取走清零。
        private bool _jumpPressed;

        private readonly InputBuffer _buffer = new InputBuffer();

        // Move —— 状态，覆盖。存的是「最近一次 Update 时的摇杆/按键方向」。
        private float _moveX;
        private float _moveY;

        // Look —— 增量，累加。存的是「自上次被 tick 取走以来，鼠标一共移了多少」。
        private float _lookAccumX;
        private float _lookAccumY;

        /// <summary>累计采样到的攻击按下次数。恒有 PressedCount == ConsumedCount + PendingCount + DroppedCount。</summary>
        public int PressedCount { get; private set; }

        /// <summary>累计被 tick 取走的攻击次数。</summary>
        public int ConsumedCount { get; private set; }

        /// <summary>队列里还积压着几个攻击。松手后这个数会继续被 tick 逐个消化到 0。</summary>
        public int PendingCount => _buffer.PendingCount;

        /// <summary>因队列满被丢弃的次数。正常游玩应恒为 0，不为 0 就是有 bug。</summary>
        public int DroppedCount => _buffer.DroppedCount;

        /// <summary>最近一次被消费的攻击是在哪个 tick 采集的。用「当前 tick - 它」观察输入延迟。</summary>
        public int LastAttackTick { get; private set; }

        private void Awake()
        {
            if (_actions == null)
            {
                Debug.LogError($"{nameof(InputSampler)}: Inspector 上的 Actions 没指定。" +
                               "请把 Assets/_Project/Settings/PlayerActions.inputactions 拖上去。", this);
                return;
            }

            _map = _actions.FindActionMap(MapName, false);
            if (_map == null)
            {
                Debug.LogError($"{nameof(InputSampler)}: 资产里找不到名为 \"{MapName}\" 的 Action Map。", this);
                return;
            }

            // throwIfNotFound = true：名字写错了要立刻炸，别留到运行时才 NRE。
            _move = _map.FindAction(MoveActionName, true);
            _look = _map.FindAction(LookActionName, true);
            _attack = _map.FindAction(AttackActionName, true);
            _jump = _map.FindAction(JumpActionName, true);
        }

        private void OnEnable()
        {
            // 只启用 Player map。以后有菜单时，Player.Disable() + UI.Enable() 避免两套输入同时生效。
            _map?.Enable();
        }

        private void OnDisable()
        {
            _map?.Disable();
        }

        private void Update()
        {
            if (_map == null) return;   // Awake 里没装配成功，错误已经报过了

            // ---- Move：状态，覆盖。旧值直接扔掉，下一帧重新读 ----
            Vector2 move = _move.ReadValue<Vector2>();
            _moveX = move.x;
            _moveY = move.y;

            // ---- Look：增量，累加。旧值必须留着，攒够了一次性交给 tick ----
            Vector2 look = _look.ReadValue<Vector2>();
            _lookAccumX += look.x;
            _lookAccumY += look.y;

            // ---- Attack：离散事件，入队。每次按下都单独记一条，绝不合并 ----
            // 边沿检测只在这里做：WasPressedThisFrame 比较的是「这一渲染帧」和「上一渲染帧」。
            if (_attack.WasPressedThisFrame())
            {
                _buffer.PushAttack(SimClock.Tick);
                PressedCount++;
            }

            // ---- Jump：事件，锁存。同 tick 内多次按下合并成一次（无损失）----
            if (_jump.WasPressedThisFrame())
            {
                _jumpPressed = true;
            }
        }

        /// <summary>
        /// BattleLoop 每 tick 调一次，把当前输入打包成一个 <see cref="InputFrame"/>。
        /// </summary>
        public InputFrame BuildFrame(int tick)
        {
            InputFrame frame;
            frame.Tick = tick;
            frame.MoveX = _moveX;
            frame.MoveY = _moveY;

            // Look 是「用掉」，不是「读一下」：取走之后必须清零，否则下个 tick 会重复转动同样的角度。
            frame.LookX = _lookAccumX;
            frame.LookY = _lookAccumY;
            _lookAccumX = 0f;
            _lookAccumY = 0f;

            // 每 tick 最多取一个。狂敲积压的事件会分摊到后续若干 tick 逐个触发。
            if (_buffer.TryConsumeAttack(out int attackTick))
            {
                frame.Attack = true;
                LastAttackTick = attackTick;
                ConsumedCount++;
            }
            else
            {
                frame.Attack = false;
            }

            // Jump：取走即清零（和 Look 的"用掉"一个意思，只是它是事件不是增量）
            frame.Jump = _jumpPressed;
            _jumpPressed = false;

            return frame;
        }

        /// <summary>
        /// 丢掉当前攒下的鼠标位移，不交给任何人。
        ///
        /// 供 <c>BattleLoop</c> 在**顿帧**期间调用：冻结期间相机也停（设计决定），
        /// 这几 tick 攒下的位移如果留着，会在解冻后第一帧一次性转过去——视角猛跳。
        /// 所以必须主动清掉；而按键**不能**这样处理，它留在队列里等解冻（见 ROADMAP「顿帧」一节）。
        ///
        /// 和 <see cref="BuildFrame"/> 里那句清零是同一件事，只是这种情况下没人要这个值。
        /// </summary>
        public void DiscardLook()
        {
            _lookAccumX = 0f;
            _lookAccumY = 0f;
        }

        /// <summary>
        /// 清空队列、统计和 Look 余额。开局时与 <see cref="SimClock.Reset"/> 配对调用。
        /// Move 不用清：它每个 Update 都被覆盖，没有残留状态。
        /// </summary>
        public void ResetState()
        {
            _buffer.Clear();
            PressedCount = 0;
            ConsumedCount = 0;
            LastAttackTick = 0;
            _lookAccumX = 0f;
            _lookAccumY = 0f;
            _jumpPressed = false;
        }
    }
}
