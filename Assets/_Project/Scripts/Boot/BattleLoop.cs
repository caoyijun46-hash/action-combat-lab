using ActionGame.Data;
using ActionGame.Inputs;
using ActionGame.Sim;
using ActionGame.View;
using UnityEngine;

namespace ActionGame.Boot
{
    /// <summary>
    /// 全项目唯一的驱动点：每 tick 推进时钟 → 取输入帧 → 推进模拟。
    /// 装配也在这一层：创建 Sim 对象、把 View 绑到 Sim 上。
    /// </summary>
    public class BattleLoop : MonoBehaviour
    {
        [SerializeField] private InputSampler _inputSampler;
        [SerializeField] private PlayerView _playerView;

        [Tooltip("相机所在的 Transform。相机朝向已经由 Sim 管了（CameraSim），" +
                 "这里只用来找挂在同一个物体上的 CameraRig。")]
        [SerializeField] private Transform _camera;

        [Tooltip("攻击的前摇/判定/后摇。播放中改这里会立刻生效——每 tick 都重新读一次。")]
        [SerializeField] private AttackDataAsset _attackData;

        [Tooltip("玩家以外的角色（沙包）。每个物体上要有 PlayerView 组件，Boot 自动绑。")]
        [SerializeField] private Transform[] _otherActors;

        [Tooltip("把每个角色近似成的球半径，米（被别人打中时用的几何体）。玩家和沙包共用一个值。")]
        [SerializeField] private float _actorRadius = 0.5f;

        [Tooltip("角色从中心到脚底的距离，米——落地判定用（脚底碰到地面就停住）。玩家和沙包共用一个值。")]
        [SerializeField] private float _actorHalfHeight = 0.5f;

        /// <summary>
        /// **全部角色**：下标 0 是玩家，其余是其他角色（沙包）。
        /// **启动时建一次，之后每 tick 复用同一个数组**——
        /// 每 tick new 一个会持续产生垃圾；而且判定去重名单存的是**这个数组里的下标**，
        /// 「顺序稳定」这条约定要守住（内容会变，数组和顺序不变）。
        /// </summary>
        private PlayerSim[] _actors;

        [Tooltip("调试用：「受击测试」按钮（HUD 左下角）造成的伤害。")]
        [SerializeField] private int _debugHitDamage = 10;

        [Tooltip("调试用：「受击测试」把玩家推开多少米。和攻击的击退一样按距离参数化（v = sqrt(2ad) 反推）。")]
        [SerializeField] private float _debugHitKnockbackDistance = 1f;

        [Tooltip("开发用：>0 时关掉 VSync 并把渲染帧率锁到这个值；0 = 完全不动（恢复原设置）。\n" +
                 "用途是把渲染帧率拉离 60Hz，好观察模拟 60Hz 与渲染帧率不同相位时的表现差异。Play 中改这个值会立即生效。")]
        [SerializeField] private int _targetFrameRate = 0;

        private int _savedVSync = -1;          // -1 表示还没存过
        private int _savedTargetFrameRate;

        /// <summary>
        /// 顿帧还剩几个 tick，0 = 没在冻。命中那一 tick 从 <see cref="AttackData.HitStopTicks"/>
        /// 装载，之后每 tick 减一（见 <see cref="FixedUpdate"/>）。
        ///
        /// 它**不是模拟状态**——不参与确定性计算，是驱动层的调度状态
        /// （联机时「冻几 tick」会由服务器连同命中结果一起告诉各客户端）。
        /// </summary>
        private int _hitStopTicksLeft;

        /// <summary>
        /// 慢放还剩几个**真实 tick**（0 = 没在慢放）。机制和顿帧一样是「跳 tick」，
        /// 倍率见 <see cref="SimConstants.SlowMotionSkipEvery"/>。
        /// **不许用 `Time.timeScale`**（铁律 3：它会连带改变固定步长与物理，联机时污染别人的世界）。
        /// </summary>
        private int _slowMotionTicksLeft;

        /// <summary>慢放还剩几个真实 tick，0 = 没在慢放。供调试 HUD 显示。</summary>
        public int SlowMotionTicksLeft => _slowMotionTicksLeft;

        /// <summary>最近一个 tick 采到的输入。现在只供 HUD 显示，以后交给 Sim 的战斗逻辑。</summary>
        public InputFrame LastFrame { get; private set; }

        /// <summary>顿帧还剩几个 tick，0 = 没在冻。供调试 HUD 显示（验收「冻了几 tick」要看得见）。</summary>
        public int HitStopTicksLeft => _hitStopTicksLeft;

        /// <summary>
        /// 调试受击请求：HUD 的按钮在**渲染侧**置位，<see cref="FixedUpdate"/> 在 **tick 侧**消费。
        /// 这是调试通道，不是游戏输入——所以不走 <c>Inputs/</c>；
        /// 但写模拟的动作仍然只发生在 tick 里（铁律 2），和输入缓冲是同一条传送带思路。
        /// </summary>
        private bool _debugHitRequested;

        /// <summary>请求一次模拟受击（从玩家正前方来的一刀：扣 <see cref="_debugHitDamage"/> 血、向后推）。</summary>
        public void RequestDebugHit()
        {
            _debugHitRequested = true;
        }

        /// <summary>模拟层的玩家对象。View 通过 <see cref="PlayerView.Bind"/> 拿到它。</summary>
        public PlayerSim Player { get; private set; }

        /// <summary>
        /// 相机的朝向。<see cref="CameraSim"/> 是**模拟状态**（它决定移动方向和判定朝向），
        /// 由 Boot 持有、每 tick 用 <c>Look</c> 输入推进。
        ///
        /// 视角改造之前，相机朝向是场景里那个 Transform 的 <c>eulerAngles.y</c>，
        /// Sim 每 tick 去读它。现在方向反过来了：View 读这个来摆相机，Sim 不再碰场景。
        /// </summary>
        public CameraSim Camera { get; private set; }

        /// <summary>
        /// 全部角色（和 Sim 用的是**同一个数组**，不是副本）。供调试 HUD 读。装配完成前是 null。
        /// </summary>
        public PlayerSim[] Actors => _actors;

        private void Awake()
        {
            ApplyFrameRateOverride();
            SimClock.Reset();

            if (!ValidateRefs())
            {
                enabled = false;   // 停下，别让 FixedUpdate 里 NRE
                return;
            }

            _inputSampler.ResetState();   // 与 SimClock.Reset() 配对

            // 出生点 = 方块在场景里摆的位置。Boot 负责把场景里的摆放翻译成 Sim 的初始状态，
            // 之后位置就由 Sim 说了算，Transform 不再具有权威性。
            Player = new PlayerSim(_playerView.transform.position, _actorRadius, _actorHalfHeight);
            _playerView.Bind(Player);
            BindAnimation(_playerView.gameObject, Player);

            // 相机的初始朝向取角色的初始朝向——出生时视角正对着角色面朝的方向。
            Camera = new CameraSim(_playerView.transform.eulerAngles.y);

            BuildActors();
            BindCameraRig();

            float delta = Time.fixedDeltaTime - SimConstants.FixedDeltaTime;
            if (Mathf.Abs(delta) > 1e-5f)
            {
                Debug.LogError($"FixedDeltaTime mismatch: Time.fixedDeltaTime={Time.fixedDeltaTime}, SimConstants={SimConstants.FixedDeltaTime}");
            }
        }

        /// <summary>
        /// 把相机朝向和一个角色交给相机夹具。
        ///
        /// 用 <c>GetComponent</c> 从 <see cref="_camera"/> 上找，而不是再开一个 Inspector 槽：
        /// 场景里的引用不用重接，而且「相机夹具挂在相机上」这个约束是自动成立的
        /// ——挂错了地方会在这里报错，比在 Inspector 里漏拖一个引用更容易发现。
        /// </summary>
        private void BindCameraRig()
        {
            CameraRig rig = _camera.GetComponent<CameraRig>();
            if (rig == null)
            {
                Debug.LogError($"{nameof(BattleLoop)}: Camera 那个物体上没有 CameraRig 组件，相机不会动。", this);
                return;
            }

            rig.Bind(Camera, Player);
        }

        /// <summary>
        /// 把场景里摆好的其他角色 Transform 翻译成 Sim 的角色，并装配它们的 View。
        /// 玩家（下标 0）在 Awake 里已经先建好了。
        ///
        /// 这就是「Boot 是场景和 Sim 之间的翻译层」那条分工：场景里的东西（Transform）
        /// 是引擎对象，Sim 不能碰；Boot 读一次，翻译成 <see cref="PlayerSim"/> 交出去。
        /// 之后位置就由 Sim 说了算，Transform 不再具有权威性。
        ///
        /// 下标约定：<c>_actors[0]</c> 是玩家，其他角色从 1 开始。
        /// 判定去重名单存的就是这个下标，顺序必须稳定。
        /// </summary>
        private void BuildActors()
        {
            int count = _otherActors != null ? _otherActors.Length : 0;
            _actors = new PlayerSim[count + 1];
            _actors[0] = Player;

            for (int i = 0; i < count; i++)
            {
                Transform t = _otherActors[i];
                if (t == null)
                {
                    Debug.LogError($"{nameof(BattleLoop)}: Other Actors 第 {i} 个槽是空的，已按原点处理。", this);
                    _actors[i + 1] = new PlayerSim(Vector3.zero, _actorRadius, _actorHalfHeight);
                    continue;
                }

                PlayerSim actor = new PlayerSim(t.position, _actorRadius, _actorHalfHeight);
                _actors[i + 1] = actor;

                // 把 View 接上。用 GetComponent 而不是让用户在 Inspector 里填引用——
                // 漏了会表现为「这个角色不动」，比漏拖一个引用更容易发现。
                PlayerView view = t.GetComponent<PlayerView>();
                if (view != null) { view.Bind(actor); }
                else { Debug.LogError($"{nameof(BattleLoop)}: {t.name} 上没有 PlayerView 组件，它不会动。", t); }

                BindAnimation(t.gameObject, actor);
            }
        }

        /// <summary>
        /// 把角色交给它身上的动画组件（**有才绑**——还没换模型的方块角色可以不挂，不影响其他系统）。
        /// 和 <see cref="PlayerView.Bind"/> 一个模式：组件在物体上，Boot 装配时 Bind 一次。
        /// </summary>
        private static void BindAnimation(GameObject go, PlayerSim actor)
        {
            ActorAnimation animation = go.GetComponent<ActorAnimation>();
            if (animation != null) { animation.Bind(actor); }
        }

        /// <summary>
        /// 推进玩家以外的角色：喂「什么都不做」的命令（不移动、不出招、朝向 0）。
        ///
        /// 它们是**还没有脑子的角色**（沙包）。以后 AI 来了，就是在这里给它们填命令
        /// （和 <c>FromPlayerInput</c> 平级的生产者），<see cref="PlayerSim.Tick"/> 的形状不用变。
        /// </summary>
        private void TickOtherActors()
        {
            ActorCommand idle = default;
            for (int i = 1; i < _actors.Length; i++)
            {
                _actors[i].Tick(idle, SimConstants.FixedDeltaTime, _attackData.Data, _actors);
            }
        }

        /// <summary>
        /// 开发用：把渲染帧率拉离 60Hz，好观察模拟与渲染不同相位时的表现。
        /// 首次调用时记下原设置，这样把 <see cref="_targetFrameRate"/> 改回 0 能真正恢复，而不是留下一个被改过的运行时状态。
        /// </summary>
        private void ApplyFrameRateOverride()
        {
            if (_savedVSync < 0)
            {
                _savedVSync = QualitySettings.vSyncCount;
                _savedTargetFrameRate = Application.targetFrameRate;
            }

            bool overrideOn = _targetFrameRate > 0;

            // 必须先把 VSync 关掉，否则各平台会忽略 targetFrameRate。
            QualitySettings.vSyncCount = overrideOn ? 0 : _savedVSync;
            Application.targetFrameRate = overrideOn ? _targetFrameRate : _savedTargetFrameRate;
        }

        /// <summary>Play 模式下在 Inspector 里改动 <see cref="_targetFrameRate"/> 时立即生效，不用重启。</summary>
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                ApplyFrameRateOverride();
            }
        }

        private bool ValidateRefs()
        {
            bool ok = true;

            if (_inputSampler == null) { Debug.LogError($"{nameof(BattleLoop)}: Input Sampler 没指定。", this); ok = false; }
            if (_playerView == null) { Debug.LogError($"{nameof(BattleLoop)}: Player View 没指定。", this); ok = false; }
            if (_camera == null) { Debug.LogError($"{nameof(BattleLoop)}: Camera 没指定。", this); ok = false; }
            if (_attackData == null) { Debug.LogError($"{nameof(BattleLoop)}: Attack Data 资产没指定（右键 Create > ActionGame > Attack Data 建一个）。", this); ok = false; }

            return ok;
        }

        private void FixedUpdate()
        {
            // 先推进时钟再取帧，这样 frame.Tick 就是「正在模拟的这一 tick」，
            // 且采集端打的时间戳与消费时的 tick 之差最小为 1，便于观察延迟。
            // （顿帧期间这一行照跑：时钟一直在数，冻的只有模拟推进。）
            SimClock.Advance();

            // ---- 顿帧：整个世界停住，相机也停 ----
            //
            // 冻结期间**不取帧**，并且要分两类处理攒着的输入：
            //   位移必须主动丢（DiscardLook）——留着的话解冻后第一帧会把这几 tick 的
            //   鼠标位移一次性转过去，视角猛跳；
            //   按键**不能**丢——留在队列里，解冻后照常投递，等于玩家在解冻那一刻按下。
            // （如果反过来「照常取帧、然后把整个 frame 扔掉」，队列里的按键会被消费掉
            //   却没人读，玩家的按键就凭空消失了。A / C'' 两个方案的取舍见 ROADMAP「顿帧」。）
            if (_hitStopTicksLeft > 0)
            {
                _hitStopTicksLeft--;
                _inputSampler.DiscardLook();
                return;
            }

            // ---- 慢放：跳掉一部分真实 tick，世界变慢 ----
            // 和顿帧同一套机制（跳 tick），区别在**不丢输入**：这里**不调 BuildFrame**——
            // 按键留在队列里、鼠标位移继续攒着，下一个执行的 tick 一起交付。
            // 所以慢放期间相机的**转向速度不掉**（所有位移都会被消费），只是步子变大（30Hz 的步子，
            // 渲染插值会把它抹平一部分）。铁律 3：不许用 Time.timeScale。
            if (_slowMotionTicksLeft > 0)
            {
                _slowMotionTicksLeft--;
                if (_slowMotionTicksLeft % SimConstants.SlowMotionSkipEvery == 0) { return; }
            }

            LastFrame = _inputSampler.BuildFrame(SimClock.Tick);

            // 相机朝向**先**推进，玩家**后**用。
            // 这样这一 tick 的鼠标位移会立刻反映在本 tick 的移动方向和角色朝向上，
            // 不会慢一帧。反过来（玩家先 tick）会多出 16.7ms 的输入延迟，转头时会觉得黏。
            Camera.Tick(LastFrame);

            // ---- 调试受击：从「玩家正前方」来的一刀 ----
            // 和输入同一条传送带：按钮在渲染侧置位，这里在 tick 侧消费。
            // ReceiveHit 只装填，Player.Tick 开头才消费（所以它在 Tick 之前调，效果落在这一 tick）。
            // 放在冻结判断**之后**，所以顿帧期间点的按钮会等到解冻才生效——不丢。
            if (_debugHitRequested)
            {
                _debugHitRequested = false;
                float speed = Mathf.Sqrt(
                    2f * SimConstants.KnockbackDeceleration * _debugHitKnockbackDistance);
                Player.ReceiveHit(_debugHitDamage, -Player.FacingDirection * speed);
            }

            // ---- 玩家输入 → 操作意图：这就是「谁在操作」和「角色做什么」之间的那道缝 ----
            // 将来 AI 驱动角色，改的就是这一行（换成 AI 填好的 ActorCommand），
            // Player.Tick 一个字不用动。
            ActorCommand command = ActorCommand.FromPlayerInput(LastFrame, Camera.Yaw);

            // 攻击数据也每 tick 现读（拿到的是 struct 值拷贝）。
            // 每次读而不是 Awake 时存一份，是为了让播放中改 Inspector 的数字立刻生效。
            Player.Tick(command, SimConstants.FixedDeltaTime, _attackData.Data, _actors);

            // 其他角色必须排在玩家**之后**：
            // 玩家那一 tick 里会做判定、给受害者装填受击，紧接着推进其他角色，
            // 它们就在**同一 tick** 吃到反应（进硬直、开始被推），打击感不会慢半拍。
            // 顺序反过来（先推进别人再判定）的话，受击要等下一 tick 才生效。
            TickOtherActors();

            // ---- 命中就顿 ----
            // 放在最后：所有角色都 tick 过之后才看——触发条件是**受害者侧的记账**
            // （WasHitThisTick），也就是「有命中真的落下了」；被无敌挡掉的命中不记账、不顿帧。
            // 只看玩家以外的角色：顿帧的语义仍是「玩家的刀砍中了谁」——
            // 玩家自己挨打（调试按钮 / 以后的敌人）不触发它（M3b 定的）。
            // 时长每 tick 现读（和上面读攻击数据同理），填 0 = 关掉顿帧。
            if (AnyOtherActorWasHit() && _attackData.Data.HitStopTicks > 0)
            {
                _hitStopTicksLeft = _attackData.Data.HitStopTicks;
            }

            // ---- 完美闪避 → 慢放 ----
            // 触发条件在**受害者**那边：闪避的无敌帧丢弃掉一次命中时记了账（DodgedHitThisTick）——
            // 和 H1 的分工一致：结果由受害者判，世界读它的记账。
            // 只看**玩家**的：敌人躲开你的刀不该给你慢放；反过来玩家躲开敌人的刀才该有奖励。
            // 重复触发就是刷新时长（慢放中再出一次完美闪避 → 重新数 120）。
            if (Player.DodgedHitThisTick)
            {
                _slowMotionTicksLeft = SimConstants.SlowMotionTicks;
            }
        }

        /// <summary>
        /// 玩家以外的角色里，这一 tick 有没有人**真的**挨了打（= 玩家的刀真落下了）。
        /// 顿帧的触发条件——为什么这么看，见 <see cref="FixedUpdate"/> 末尾的说明。
        /// </summary>
        private bool AnyOtherActorWasHit()
        {
            for (int i = 1; i < _actors.Length; i++)
            {
                if (_actors[i].WasHitThisTick) { return true; }
            }
            return false;
        }
    }
}
