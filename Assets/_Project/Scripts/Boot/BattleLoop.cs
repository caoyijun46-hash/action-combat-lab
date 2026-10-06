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

        [Tooltip("场景里所有可被攻击的目标。Boot 在启动时把它们的 Transform 翻译成 Sim 的数据。")]
        [SerializeField] private Transform[] _targets;

        [Tooltip("把所有靶子近似成的球半径，米。现在统一一个值，以后可以每个靶子各配。")]
        [SerializeField] private float _targetRadius = 0.5f;

        [Tooltip("靶子的血量上限。")]
        [SerializeField] private int _targetMaxHealth = 100;

        /// <summary>
        /// 交给 Sim 的靶子数组。**启动时建一次，之后每 tick 复用同一个数组**——
        /// 每 tick new 一个会持续产生垃圾，而且 Sim 那边接收的是引用，
        /// 「数组内容不变」这条约定要守住（8c 会改内容，但不会换数组）。
        /// </summary>
        private TargetSim[] _targetSims;

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

        /// <summary>最近一个 tick 采到的输入。现在只供 HUD 显示，以后交给 Sim 的战斗逻辑。</summary>
        public InputFrame LastFrame { get; private set; }

        /// <summary>顿帧还剩几个 tick，0 = 没在冻。供调试 HUD 显示（验收「冻了几 tick」要看得见）。</summary>
        public int HitStopTicksLeft => _hitStopTicksLeft;

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
        /// 交给 Sim 的那组靶子（和 Sim 用的是**同一个数组**，不是副本）。
        /// 供调试 HUD 和 <see cref="TargetView"/> 读。装配完成前是 null。
        /// </summary>
        public TargetSim[] Targets => _targetSims;

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
            Player = new PlayerSim(_playerView.transform.position);
            _playerView.Bind(Player);

            // 相机的初始朝向取角色的初始朝向——出生时视角正对着角色面朝的方向。
            Camera = new CameraSim(_playerView.transform.eulerAngles.y);

            BuildTargets();
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
        /// 把场景里摆好的靶子 Transform 翻译成 Sim 的纯数据。
        ///
        /// 这就是「Boot 是场景和 Sim 之间的翻译层」那条分工：场景里的东西（Transform）
        /// 是引擎对象，Sim 不能碰；Boot 读一次，翻译成 <see cref="TargetSim"/> 数组交出去。
        /// 之后位置就由 Sim 说了算（8c 靶子会后退），Transform 不再具有权威性。
        /// </summary>
        private void BuildTargets()
        {
            int count = _targets != null ? _targets.Length : 0;
            _targetSims = new TargetSim[count];

            for (int i = 0; i < count; i++)
            {
                if (_targets[i] == null)
                {
                    Debug.LogError($"{nameof(BattleLoop)}: Targets 第 {i} 个槽是空的，已按原点处理。", this);
                    _targetSims[i].Position = Vector3.zero;
                }
                else
                {
                    _targetSims[i].Position = _targets[i].position;
                }

                // PreviousPosition 也要初始化成同一个值，否则第一帧插值会从原点飞过来
                _targetSims[i].PreviousPosition = _targetSims[i].Position;
                _targetSims[i].Radius = _targetRadius;
                _targetSims[i].MaxHealth = _targetMaxHealth;
                _targetSims[i].Health = _targetMaxHealth;

                // 把 View 接上。用 GetComponent 而不是让用户在 Inspector 里填下标——
                // 下标填错了很难看出来（靶子显示的是别人的位置），自动绑不会错。
                TargetView view = _targets[i] != null ? _targets[i].GetComponent<TargetView>() : null;
                if (view != null) { view.Bind(_targetSims, i); }
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

            LastFrame = _inputSampler.BuildFrame(SimClock.Tick);

            // 相机朝向**先**推进，玩家**后**用。
            // 这样这一 tick 的鼠标位移会立刻反映在本 tick 的移动方向和角色朝向上，
            // 不会慢一帧。反过来（玩家先 tick）会多出 16.7ms 的输入延迟，转头时会觉得黏。
            Camera.Tick(LastFrame);

            // 攻击数据也每 tick 现读（拿到的是 struct 值拷贝）。
            // 每次读而不是 Awake 时存一份，是为了让播放中改 Inspector 的数字立刻生效。
            Player.Tick(LastFrame, Camera.Yaw, SimConstants.FixedDeltaTime, _attackData.Data, _targetSims);

            // 靶子的推进必须排在玩家之后：
            // 玩家那一 tick 里会做判定、给靶子一个击退初速度，
            // 紧接着推进靶子，它就在**同一 tick** 开始移动，打击感不会慢半拍。
            // 顺序反过来（先推进靶子再判定）的话，击退要等下一 tick 才生效。
            TargetSim.TickAll(_targetSims, SimConstants.FixedDeltaTime);

            // ---- 命中就顿 ----
            // 放在最后：判定是 Player.Tick 里做的，命中那一 tick 要完整跑完
            // （击退已经在同一 tick 生效），从**下一 tick** 开始冻。
            // 时长每 tick 现读（和上面读攻击数据同理），填 0 = 关掉顿帧。
            if (Player.HitThisTick && _attackData.Data.HitStopTicks > 0)
            {
                _hitStopTicksLeft = _attackData.Data.HitStopTicks;
            }
        }
    }
}
