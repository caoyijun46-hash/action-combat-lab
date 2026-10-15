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
        /// <summary>
        /// **世界**：全体角色 + 「一 tick 里谁先谁后」。启动时建一次，之后每 tick 调
        /// <see cref="WorldSim.Tick"/>。判定去重名单存的就是它里面 <see cref="Actors"/> 的下标，
        /// 「顺序稳定」这条约定要守住（内容会变，数组和顺序不变）。
        /// </summary>
        private WorldSim _world;

        [Tooltip("调试用：「受击测试」按钮（HUD 左下角）造成的伤害。")]
        [SerializeField] private int _debugHitDamage = 10;

        [Tooltip("调试用：「受击测试」把玩家推开的速度，米/秒。默认 6.3246 = sqrt(2·20·1)，等价于原来「推开 1 米」。")]
        [SerializeField] private float _debugHitSpeed = 6.3246f;

        [Tooltip("开发用：>0 时关掉 VSync 并把渲染帧率锁到这个值；0 = 完全不动（恢复原设置）。\n" +
                 "用途是把渲染帧率拉离 60Hz，好观察模拟 60Hz 与渲染帧率不同相位时的表现差异。Play 中改这个值会立即生效。")]
        [SerializeField] private int _targetFrameRate = 0;

        private int _savedVSync = -1;          // -1 表示还没存过
        private int _savedTargetFrameRate;

        /// <summary>
        /// 时间调速器：顿帧 / 慢放的计数和「这个真实 tick 要不要跳」的判断。
        /// 纯 C# 类——离线测试能直接实例化它，不用复刻这段骨架。机制和取舍见它的注释。
        /// </summary>
        private readonly TimeControl _time = new TimeControl();

        /// <summary>
        /// 视角锁定的目标下标（-1 = 没锁）。**选目标在 Boot**（只有它认识全体角色），
        /// 旋转数学在 <see cref="CameraSim.TickLocked"/>。下标就是 <see cref="WorldSim.Actors"/>
        /// 的下标——「顺序稳定」的约定照旧。
        /// </summary>
        private int _lockedTargetIndex = -1;

        /// <summary>
        /// 玩家移动方向的解析器（C5）：锁定中大回摆时「保持原方向」的防甩逻辑住在这里。
        /// 纯类、有状态——和 <see cref="TimeControl"/> 同一个模式（离线可实例化）。
        /// </summary>
        private readonly MoveDirectionResolver _moveDirection = new MoveDirectionResolver();

        /// <summary>慢放还剩几个真实 tick，0 = 没在慢放。供调试 HUD 显示。</summary>
        public int SlowMotionTicksLeft => _time.SlowMotionTicksLeft;

        /// <summary>视角锁定了谁（<see cref="WorldSim.Actors"/> 的下标；-1 = 没锁）。供调试 HUD 显示。</summary>
        public int LockedTargetIndex => _lockedTargetIndex;

        /// <summary>最近一个 tick 采到的输入。现在只供 HUD 显示，以后交给 Sim 的战斗逻辑。</summary>
        public InputFrame LastFrame { get; private set; }

        /// <summary>顿帧还剩几个 tick，0 = 没在冻。供调试 HUD 显示（验收「冻了几 tick」要看得见）。</summary>
        public int HitStopTicksLeft => _time.HitStopTicksLeft;

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

        /// <summary>模拟层的玩家对象（世界里的第 0 个角色）。View 通过 <see cref="PlayerView.Bind"/> 拿到它。</summary>
        public PlayerSim Player => _world?.Player;

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
        public PlayerSim[] Actors => _world?.Actors;

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
            PlayerSim player = new PlayerSim(_playerView.transform.position, _actorRadius, _actorHalfHeight);
            _playerView.Bind(player);
            BindAnimation(_playerView.gameObject, player);

            // 相机的初始朝向取角色的初始朝向——出生时视角正对着角色面朝的方向。
            Camera = new CameraSim(_playerView.transform.eulerAngles.y);

            BuildWorld(player);
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
        /// 把场景里摆好的其他角色 Transform 翻译成 Sim 的角色，装配它们的 View，
        /// 然后连同玩家一起交给 <see cref="WorldSim"/>。
        ///
        /// 这就是「Boot 是场景和 Sim 之间的翻译层」那条分工：场景里的东西（Transform）
        /// 是引擎对象，Sim 不能碰；Boot 读一次，翻译成 <see cref="PlayerSim"/> 交出去。
        /// 之后位置就由 Sim 说了算，Transform 不再具有权威性。
        ///
        /// 下标约定：<c>[0]</c> 是玩家（外面已经建好），其他角色从 1 开始。
        /// 判定去重名单存的就是这个下标，顺序必须稳定。
        /// </summary>
        private void BuildWorld(PlayerSim player)
        {
            int count = _otherActors != null ? _otherActors.Length : 0;
            PlayerSim[] actors = new PlayerSim[count + 1];
            actors[0] = player;

            for (int i = 0; i < count; i++)
            {
                Transform t = _otherActors[i];
                if (t == null)
                {
                    Debug.LogError($"{nameof(BattleLoop)}: Other Actors 第 {i} 个槽是空的，已按原点处理。", this);
                    actors[i + 1] = new PlayerSim(Vector3.zero, _actorRadius, _actorHalfHeight);
                    continue;
                }

                PlayerSim actor = new PlayerSim(t.position, _actorRadius, _actorHalfHeight);
                actors[i + 1] = actor;

                // 把 View 接上。用 GetComponent 而不是让用户在 Inspector 里填引用——
                // 漏了会表现为「这个角色不动」，比漏拖一个引用更容易发现。
                PlayerView view = t.GetComponent<PlayerView>();
                if (view != null) { view.Bind(actor); }
                else { Debug.LogError($"{nameof(BattleLoop)}: {t.name} 上没有 PlayerView 组件，它不会动。", t); }

                BindAnimation(t.gameObject, actor);
            }

            _world = new WorldSim(actors);
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

        /// <summary>
        /// 中键：切换视角锁定。未锁定时在「范围内的其他角色」里挑与视线夹角最小的
        /// （<see cref="CameraSim.PickLockTarget"/>）；挑不到（没人 / 全在范围外）就保持未锁定。
        /// 已锁定时 = 解除。
        /// </summary>
        private void ToggleLockOn()
        {
            if (_lockedTargetIndex >= 0)
            {
                _lockedTargetIndex = -1;
                return;
            }

            _lockedTargetIndex = Camera.PickLockTarget(
                _world.Actors, _world.Player, SimConstants.LockOnRangeMeters);
        }

        /// <summary>
        /// 锁定目标的指向向量（玩家位置 → 目标位置）。防甩门控只看它的水平分量
        ///（两边「眼睛」的垂直偏移在作差时相消，不用再加）。
        /// </summary>
        private Vector3 LockedTargetVector()
        {
            return Actors[_lockedTargetIndex].Position - Player.Position;
        }

        private void FixedUpdate()
        {
            // 先推进时钟再取帧，这样 frame.Tick 就是「正在模拟的这一 tick」，
            // 且采集端打的时间戳与消费时的 tick 之差最小为 1，便于观察延迟。
            // （顿帧期间这一行照跑：时钟一直在数，冻的只有模拟推进。）
            SimClock.Advance();

            // ---- 时钟调速：这个真实 tick 要不要跳（顿帧 / 慢放）----
            // 机制和取舍都在 TimeControl 里；这里只做它要求的动作：
            //   顿帧 → **丢掉鼠标位移**（不丢的话解冻后视角猛跳），按键留在队列里（不取帧），整个 tick 跳过；
            //   慢放跳过的 tick → **什么都不丢**（按键留队列、位移继续攒），下一个执行 tick 一起交付。
            switch (_time.Advance())
            {
                case TimeControl.AdvanceKind.Freeze:
                    _inputSampler.DiscardLook();
                    return;

                case TimeControl.AdvanceKind.SlowSkip:
                    return;
            }

            LastFrame = _inputSampler.BuildFrame(SimClock.Tick);

            // ---- 锁定键（鼠标中键）：切换锁定 ----
            // 和输入同一条传送带（渲染侧采集、tick 侧消费）；放在相机推进**之前**，
            // 这样「按下的这一 tick」就开始按（可能的）锁定转了。
            if (LastFrame.LockOn)
            {
                ToggleLockOn();
            }

            // 相机朝向**先**推进，玩家**后**用。
            // 这样这一 tick 的鼠标位移会立刻反映在本 tick 的移动方向和角色朝向上，
            // 不会慢一帧。反过来（玩家先 tick）会多出 16.7ms 的输入延迟，转头时会觉得黏。
            // 锁定中改成「自动朝目标转」——鼠标位移在 BuildFrame 里已经被取走，等于被忽略。
            if (_lockedTargetIndex >= 0)
            {
                Camera.TickLocked(Player.Position, Actors[_lockedTargetIndex].Position);
            }
            else
            {
                Camera.Tick(LastFrame);
            }

            // ---- 调试受击：从「玩家正前方」来的一刀 ----
            // 和输入同一条传送带：按钮在渲染侧置位，这里在 tick 侧消费。
            // ReceiveHit 只装填，Player.Tick 开头才消费（所以它在 Tick 之前调，效果落在这一 tick）。
            // 放在冻结判断**之后**，所以顿帧期间点的按钮会等到解冻才生效——不丢。
            // 顿帧请求传 0：玩家挨打本来就不触发顿帧（M3b 定的；M4 给敌人真正的招式值时再填）。
            if (_debugHitRequested)
            {
                _debugHitRequested = false;
                Player.ReceiveHit(_debugHitDamage, -Player.FacingDirection * _debugHitSpeed, 0);
            }

            // ---- 世界往前迈一步 ----
            // 「谁先谁后」现在住在 WorldSim 里（那是世界的知识，不是驱动层的）。
            // 玩家输入 → 操作意图：这就是「谁在操作」和「角色做什么」之间的那道缝——
            // 将来 AI 驱动玩家角色，改的就是这一行（换成 AI 填好的 ActorCommand）。
            // 攻击数据也每 tick 现读（拿到的是 struct 值拷贝），播放中改 Inspector 立刻生效。
            // 移动方向走「防甩」解析器（C5）：锁定中敌人偏出画面时（迟滞 60°/25°），
            // 方向的参照系冻住——镜头大回摆不把角色拖成弧；输入一变（或敌人回到画面里）即恢复。
            // 未锁定 / 误差小时它和原来的相机相对解析逐位一致（回归）。
            Vector3 moveDirection = _moveDirection.Resolve(
                LastFrame.MoveX, LastFrame.MoveY, Camera.Yaw,
                _lockedTargetIndex >= 0,
                _lockedTargetIndex >= 0 ? LockedTargetVector() : Vector3.zero);
            ActorCommand command = ActorCommand.FromPlayerInput(LastFrame, Camera.Yaw, moveDirection);
            _world.Tick(command, SimConstants.FixedDeltaTime, _attackData.Data);

            // ---- 命中就顿 ----
            // 放在最后：所有角色都 tick 过之后才看——顿帧的两件事（有没有人真挨了 + 顿多久）
            // 合成一个数（WorldSim.MaxOtherActorHitStopTicks）：受害者收下才算；时长**随命中
            // 记录走**（攻击方把招式值塞进了命中——H2，2026-10-15；Boot 不再伸手读玩家的资产，
            // 那个读法只在「玩家是唯一攻击者、大家共用一份数据」时才对）。被无敌挡掉的不记账、
            // 不顿帧。只看玩家以外：顿帧的语义仍是「玩家的刀砍中了谁」（M3b 定）；玩家自己
            // 挨打（调试按钮 / 以后的敌人）不触发——「敌人打人顿不顿」是 M4 的悬案。
            // 0 = 不顿（没人挨打 / 招式的值就是 0 / 负数被汇总的 0 底吃掉）；多笔取最大。
            int hitStopTicks = _world.MaxOtherActorHitStopTicks();
            if (hitStopTicks > 0)
            {
                _time.FreezeFor(hitStopTicks);
            }

            // ---- 完美闪避 → 慢放 ----
            // 触发条件在**受害者**那边：闪避的无敌帧丢弃掉一次命中时记了账（DodgedHitThisTick）——
            // 和 H1 的分工一致：结果由受害者判，世界读它的记账。
            // 只看**玩家**的：敌人躲开你的刀不该给你慢放；反过来玩家躲开敌人的刀才该有奖励。
            // 重复触发就是刷新时长（慢放中再出一次完美闪避 → 重新数 120）。
            if (Player.DodgedHitThisTick)
            {
                _time.SlowFor(SimConstants.SlowMotionTicks);
            }
        }
    }
}
