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
        [SerializeField] private Transform _camera;

        [Tooltip("开发用：>0 时关掉 VSync 并把渲染帧率锁到这个值；0 = 完全不动（恢复原设置）。\n" +
                 "用途是把渲染帧率拉离 60Hz，好观察模拟 60Hz 与渲染帧率不同相位时的表现差异。Play 中改这个值会立即生效。")]
        [SerializeField] private int _targetFrameRate = 0;

        private int _savedVSync = -1;          // -1 表示还没存过
        private int _savedTargetFrameRate;

        /// <summary>最近一个 tick 采到的输入。现在只供 HUD 显示，以后交给 Sim 的战斗逻辑。</summary>
        public InputFrame LastFrame { get; private set; }

        /// <summary>模拟层的玩家对象。View 通过 <see cref="PlayerView.Bind"/> 拿到它。</summary>
        public PlayerSim Player { get; private set; }

        /// <summary>
        /// 上一个 tick 实际传给 Sim 的相机偏航角（度）。
        ///
        /// 缓存下来而不是让 HUD 自己去读 <c>Camera.main</c>：一是 HUD 不该知道用的是哪个相机，
        /// 二是要显示的应该是 Sim **真正收到**的那个值，而不是「现在再去量一次」的值。
        /// </summary>
        public float LastCameraYaw { get; private set; }

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

            float delta = Time.fixedDeltaTime - SimConstants.FixedDeltaTime;
            if (Mathf.Abs(delta) > 1e-5f)
            {
                Debug.LogError($"FixedDeltaTime mismatch: Time.fixedDeltaTime={Time.fixedDeltaTime}, SimConstants={SimConstants.FixedDeltaTime}");
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

            return ok;
        }

        private void FixedUpdate()
        {
            // 先推进时钟再取帧，这样 frame.Tick 就是「正在模拟的这一 tick」，
            // 且采集端打的时间戳与消费时的 tick 之差最小为 1，便于观察延迟。
            SimClock.Advance();
            LastFrame = _inputSampler.BuildFrame(SimClock.Tick);

            // 相机偏航角每 tick 现取。Sim 不接触 Camera，由这里取好传进去。
            LastCameraYaw = _camera.eulerAngles.y;
            Player.Tick(LastFrame, LastCameraYaw, SimConstants.FixedDeltaTime);
        }
    }
}
