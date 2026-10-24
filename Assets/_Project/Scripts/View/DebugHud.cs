using ActionGame.Boot;
using ActionGame.Inputs;
using ActionGame.Sim;
using UnityEngine;

namespace ActionGame.View
{
    /// <summary>
    /// 开发用 HUD。纯 View 层：只读状态、只写屏幕，不碰任何逻辑。
    ///
    /// 它直接引用了 <see cref="InputSampler"/>（View → Inputs），严格说不是理想的分层——
    /// 但它是调试工具而非游戏表现，图省事直接读。以后真正的角色表现层不许这么干。
    ///
    /// ---- 为什么分左右两栏 ----
    /// 模拟跑 60 tick/秒，屏幕跑 100+ 帧/秒。左栏那些瞬时值每秒变一百多次，
    /// 「切换瞬间速度有没有突变」这类问题盯着屏幕是看不出来的，必须留下记录。
    /// 所以右栏放的是**历史**：速度连续性统计 + 状态切换日志。
    ///
    /// ---- 采样时机 ----
    /// 在 <see cref="LateUpdate"/> 里采样（每渲染帧一次），靠比对 tick 号保证「一个样本 = 一个 tick」。
    /// 一帧里跨了多个 tick（掉帧）时，那段 delta-v 没法归到某一个 tick，**作废并计数**，
    /// 不静默算错。代价是帧率低于 60 时有效样本会明显变少——这时右栏会写出来，不假装数据是好的。
    /// 若以后需要与帧率完全无关的精确统计，正确做法是把采样挪进 <c>BattleLoop.FixedUpdate</c>。
    ///
    /// ---- 布局 ----
    /// 尺寸全部是 <c>CalcSize</c> 量出来的，并且会按屏幕大小自动缩字号，
    /// 缩到最小还放不下就硬画。**任何情况下都不会再出现「内容被裁掉」**——
    /// 之前矩形写死 380 高而文字涨到 510px，底部整段（Player 区）被裁，就是这么来的。
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        [SerializeField] private BattleLoop _loop;
        [SerializeField] private InputSampler _sampler;

        [Tooltip("最大字号。屏幕放不下时会自动往小调（最小 9），直到放得下为止。")]
        [SerializeField] private int _fontSize = 16;

        [Tooltip("速度连续性统计的滑动窗口长度，单位 tick。600 = 10 秒。")]
        [SerializeField] private int _windowTicks = 600;

        [Tooltip("状态切换日志保留的条数。")]
        [SerializeField] private int _transitionLogSize = 6;

        private const int MinFontSize = 9;
        private const float Margin = 10f;
        private const float ColumnGap = 24f;

        /// <summary>
        /// 相邻 tick 速度变化的上限 = **这个角色**的 max(加速度, 减速度) × dt
        ///（MV3a 起从角色数据现算——每个角色的加减速度可以不同）。验收标准「无突变」就是拿它比的。
        /// **只盯水平通道**：竖直速度由重力管，落地（以及以后的起跳）是设计内的突变。
        /// （读它之前调用处已判过 `_loop.Player` 非空。）
        /// </summary>
        private float DeltaLimit => _loop.Player.MaxSpeedDeltaPerTick;

        /// <summary>
        /// 判定容差。`Vector3.MoveTowards` 内部要做一次向量除法归一化，
        /// 浮点误差让实测 |Δv| 最多比理论上限大出约 3.6e-7——实测 72000 组（速度 x 转向角）样本里
        /// 有 9.18% 会超过零容差的上限，所以**必须带容差**，否则转向时每 tick 都在报假警。
        /// 真正的突变是 0.5~6.0 量级，比这个容差大几千倍，漏不掉。
        /// </summary>
        private const float DeltaTolerance = 1e-4f;

        private float DeltaLimitTolerant => DeltaLimit + DeltaTolerance;

        private GUIStyle _style;
        private GUIStyle _shadow;
        private int _styleFontSize = -1;

        private string _leftText;
        private string _rightText;
        private bool _sizesDirty = true;

        // ---- 布局（每帧由 ResolveLayout 定）----
        private Vector2 _leftSize;
        private Vector2 _rightSize;
        private int _renderFontSize;
        private bool _twoColumns;
        private bool _autoShrunk;
        private int _layoutForWidth = -1;
        private int _layoutForHeight = -1;

        // ---- 速度连续性采样 ----
        private float[] _window;          // 滑动窗口：最近若干个「单 tick 的 |Δv|」
        private int _windowHead;
        private int _windowCount;
        private float _windowMaxDelta;
        private int _windowViolations;
        private int _samplesTaken;
        private int _samplesDiscarded;

        private int _lastTick = -1;
        private Vector3 _lastVelocity;
        private PlayerState _lastState;

        // ---- 命中事件（H3）----
        // 事件缓冲每世界 tick 清空；这里抄一份「最近一条」，避免事件行在没命中的 tick 上闪烁。
        private HitEvent _lastHitEvent;
        private int _lastHitEventTick = -1;
        private bool _hasLastHitEvent;

        // ---- 状态切换日志 ----
        private struct Transition
        {
            public int Tick;
            public PlayerState From;
            public PlayerState To;
            public float SpeedBefore;
            public float SpeedAfter;
            public float DeltaV;
        }

        private Transition[] _transitions;
        private int _transitionHead;
        private int _transitionCount;

        private void LateUpdate()
        {
            EnsureBuffers();
            Sample();

            BuildText(out _leftText, out _rightText);
            _sizesDirty = true;
        }

        private void OnGUI()
        {
            EnsureBuffers();

            // LateUpdate 还没跑过（第一帧）时兜底，免得画一片空白
            if (_leftText == null)
            {
                BuildText(out _leftText, out _rightText);
                _sizesDirty = true;
            }

            if (_sizesDirty)
            {
                ResolveLayout();
                _sizesDirty = false;
            }

            if (_twoColumns)
            {
                DrawColumn(Margin, Margin, _leftText, _leftSize);
                DrawColumn(Margin + _leftSize.x + ColumnGap, Margin, _rightText, _rightSize);
            }
            else
            {
                DrawColumn(Margin, Margin, _leftText, _leftSize);
                DrawColumn(Margin, Margin + _leftSize.y, _rightText, _rightSize);
            }

            DrawDebugButtons();
        }

        /// <summary>
        /// 调试按钮。固定在左下角，**不掺进上面那套「按屏幕自适应字号」的排版**——
        /// 它不参与 CalcSize 测量，屏幕再小也照画（大不了盖住 HUD 的文字）。
        ///
        /// 点击只是置一个 flag（渲染侧），真正的模拟写入发生在 BattleLoop 的下一 tick
        /// （铁律 2：不准在 Update/OnGUI 里改模拟状态）。这算调试通道，不算游戏输入。
        /// </summary>
        private void DrawDebugButtons()
        {
            if (_loop == null || _loop.Player == null) { return; }

            if (GUI.Button(new Rect(Margin, Screen.height - 40f, 210f, 30f), "受击测试（正面挨一刀）"))
            {
                _loop.RequestDebugHit();
            }
        }

        private void DrawColumn(float x, float y, string text, Vector2 size)
        {
            GUI.Label(new Rect(x + 1f, y + 1f, size.x, size.y), text, _shadow);
            GUI.Label(new Rect(x, y, size.x, size.y), text, _style);
        }

        // ------------------------------------------------------------ 布局

        /// <summary>
        /// 挑版式：两栏（矮而宽）和单栏（高而窄）各找一个放得下的最大字号，谁的字号大用谁。
        ///
        /// 这条规则不需要「屏幕够不够宽」这类阈值判断——屏幕宽就自然选两栏，
        /// 屏幕窄就自然选单栏，因为另一种版式会被迫缩得更小。
        ///
        /// 屏幕尺寸没变时先试上一帧的结论。这不是省那几个 CalcSize，而是省 GUIStyle：
        /// 往下搜字号的过程中每个字号都会 new 两个 style，每帧重搜就等于每帧制造一堆垃圾。
        /// </summary>
        private void ResolveLayout()
        {
            bool screenChanged = Screen.width != _layoutForWidth || Screen.height != _layoutForHeight;
            _layoutForWidth = Screen.width;
            _layoutForHeight = Screen.height;

            if (!screenChanged && _renderFontSize > 0 && TryUse(_renderFontSize, _twoColumns))
            {
                return;
            }

            int twoColumnSize = LargestFittingFontSize(true);
            int oneColumnSize = LargestFittingFontSize(false);
            bool useTwoColumns = twoColumnSize >= oneColumnSize;
            int size = useTwoColumns ? twoColumnSize : oneColumnSize;

            if (!TryUse(size, useTwoColumns))
            {
                // 连最小字号都放不下：照样用它硬画。宁可超出屏幕，也不像以前那样把内容裁掉。
                Measure(MinFontSize, out Vector2 left, out Vector2 right);
                Apply(MinFontSize, twoColumns: false, left, right);
            }
        }

        private int LargestFittingFontSize(bool twoColumns)
        {
            for (int size = Mathf.Max(MinFontSize, _fontSize); size > MinFontSize; size--)
            {
                if (Fits(size, twoColumns))
                {
                    return size;
                }
            }

            return MinFontSize;
        }

        /// <summary>量一遍、检查放不放得下，放得下就采用。返回是否采用。</summary>
        private bool TryUse(int fontSize, bool twoColumns)
        {
            Measure(fontSize, out Vector2 left, out Vector2 right);

            if (!FitsMeasured(left, right, twoColumns))
            {
                return false;
            }

            Apply(fontSize, twoColumns, left, right);
            return true;
        }

        private void Apply(int fontSize, bool twoColumns, Vector2 left, Vector2 right)
        {
            _renderFontSize = fontSize;
            _twoColumns = twoColumns;
            _autoShrunk = fontSize < _fontSize;
            _leftSize = left;
            _rightSize = right;
        }

        private bool Fits(int fontSize, bool twoColumns)
        {
            Measure(fontSize, out Vector2 left, out Vector2 right);
            return FitsMeasured(left, right, twoColumns);
        }

        private static bool FitsMeasured(Vector2 left, Vector2 right, bool twoColumns)
        {
            float width = Margin * 2f + (twoColumns
                ? left.x + ColumnGap + right.x
                : Mathf.Max(left.x, right.x));

            float height = Margin * 2f + (twoColumns
                ? Mathf.Max(left.y, right.y)
                : left.y + right.y);

            return width <= Screen.width && height <= Screen.height;
        }

        private void Measure(int fontSize, out Vector2 left, out Vector2 right)
        {
            EnsureStyles(fontSize);
            left = _style.CalcSize(new GUIContent(_leftText));
            right = _style.CalcSize(new GUIContent(_rightText));
        }

        // ------------------------------------------------------------ 采样

        private void Sample()
        {
            if (_loop == null || _loop.Player == null)
            {
                return;
            }

            int tick = _loop.LastFrame.Tick;

            // 命中事件（H3）：每个渲染帧看一次缓冲。缓冲在顿帧期间不会清空，
            // 所以刚命中的事件会一直显示到世界再次推进。
            if (_loop.HitEvents != null && _loop.HitEvents.Count > 0)
            {
                _lastHitEvent = _loop.HitEvents[_loop.HitEvents.Count - 1];
                _lastHitEventTick = tick;
                _hasLastHitEvent = true;
            }

            // 这一帧一个 tick 都没跑（帧率高于 60 时很常见）。没有新样本，什么都不做——
            // 注意是「跳过」而不是「作废」：没有 tick 就没有信息损失。
            if (tick == _lastTick)
            {
                return;
            }

            Vector3 velocity = _loop.Player.MoveVelocity;   // 自驱水平（y 恒 0）；竖直不参与连续性判据
            PlayerState state = _loop.Player.State;

            bool hasPrevious = _lastTick >= 0;
            bool exactlyOneTick = hasPrevious && tick - _lastTick == 1;

            if (exactlyOneTick)
            {
                // 只盯**自驱水平通道**（MoveVelocity）：竖直速度由重力管，落地那一下 vy
                // 从 -12 直接清零，那是设计内的"突变"，不该报「速度不连续」。
                // （通道分离后水平速度的 y 恒为 0，这里不再需要手动抹 y。）
                Vector3 delta = velocity - _lastVelocity;
                float deltaV = delta.magnitude;
                PushSample(deltaV);

                if (deltaV > DeltaLimitTolerant)
                {
                    // 罕见，所以不会刷屏；真出现了，这一行可以直接复制出来定位
                    Debug.LogError(
                        $"[DebugHud] 速度不连续：tick {tick}，|Δv| = {deltaV:F6} m/s，" +
                        $"上限 {DeltaLimit:F4}（含容差 {DeltaLimitTolerant:F4}）。" +
                        $"状态 {_lastState}，速度 {_lastVelocity.magnitude:F3} -> {velocity.magnitude:F3} m/s");
                }

                if (state != _lastState)
                {
                    PushTransition(new Transition
                    {
                        Tick = tick,
                        From = _lastState,
                        To = state,
                        SpeedBefore = _lastVelocity.magnitude,
                        SpeedAfter = velocity.magnitude,
                        DeltaV = deltaV,
                    });
                }
            }
            else if (hasPrevious)
            {
                // 一帧里跨了多个 tick（掉帧）。这段差值摊不到某一个 tick 上，作废并计数。
                _samplesDiscarded++;
            }

            _lastTick = tick;
            _lastVelocity = velocity;
            _lastState = state;
        }

        private void PushSample(float deltaV)
        {
            _samplesTaken++;
            _window[_windowHead] = deltaV;
            _windowHead = (_windowHead + 1) % _window.Length;
            if (_windowCount < _window.Length)
            {
                _windowCount++;
            }

            // 窗口没满时有效项就是 0..count-1；满了之后 count == Length，全表都是有效项。
            // 所以这一趟扫描在两种情况下都对，不需要区分 head 绕没绕过圈。
            float max = 0f;
            int violations = 0;
            for (int i = 0; i < _windowCount; i++)
            {
                float v = _window[i];
                if (v > max) { max = v; }
                if (v > DeltaLimitTolerant) { violations++; }
            }
            _windowMaxDelta = max;
            _windowViolations = violations;
        }

        private void PushTransition(Transition transition)
        {
            _transitions[_transitionHead] = transition;
            _transitionHead = (_transitionHead + 1) % _transitions.Length;
            if (_transitionCount < _transitions.Length)
            {
                _transitionCount++;
            }
        }

        /// <summary>
        /// 缓冲区按 Inspector 里的长度惰性重建，这样 Play 中改窗口长度/日志条数也能生效，
        /// 而不是要重启一次才认。
        /// </summary>
        private void EnsureBuffers()
        {
            int windowLength = Mathf.Max(1, _windowTicks);
            if (_window == null || _window.Length != windowLength)
            {
                _window = new float[windowLength];
                _windowHead = 0;
                _windowCount = 0;
                _windowMaxDelta = 0f;
                _windowViolations = 0;
            }

            int logLength = Mathf.Max(1, _transitionLogSize);
            if (_transitions == null || _transitions.Length != logLength)
            {
                _transitions = new Transition[logLength];
                _transitionHead = 0;
                _transitionCount = 0;
            }
        }

        // ------------------------------------------------------------ 画

        private void BuildText(out string left, out string right)
        {
            // 字号自适应是上一帧 OnGUI 里算出来的，这里显示会滞后一帧——调试标记，无所谓
            string shrinkNote = _autoShrunk ? $"   [字号自适应 {_renderFontSize}]" : string.Empty;

            // ---- 版式：一组一行，组名放行首当标签 ----
            // 原来每组都占一行标题 + 一行空行（Input / Frame / Player 三组 = 6 行纯装饰），
            // 加上字段名冒号对齐，25 行里只有 15 行是真的在报数。
            // 现在改成组名当行首标签，信息一个没删，行数少了四分之三。
            left = $"tick {SimClock.Tick} | {Time.time:F2}s | frame {Time.frameCount} | " +
                   $"{(Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f):F0}fps{shrinkNote}\n";

            if (_loop == null || _sampler == null || _loop.Player == null || _loop.Camera == null)
            {
                left += "(Inspector 引用没指定全，或 Boot 装配失败)";
                right = string.Empty;
                return;
            }

            InputFrame f = _loop.LastFrame;

            // 每次按下，要么被消费、要么还在队列里、要么被丢弃——三者互斥且穷尽。
            // 正常时只显示 [OK]；真不相等了才把算式摊开（那是要排查的时候，不占平时的宽度）。
            int accounted = _sampler.ConsumedCount + _sampler.PendingCount + _sampler.DroppedCount;
            bool balanced = accounted == _sampler.PressedCount;
            string balanceNote = balanced ? "[OK]" : $"[MISMATCH {accounted} != {_sampler.PressedCount}]";

            Vector3 pos = _loop.Player.Position;
            Vector3 vel = _loop.Player.MoveVelocity;   // 自驱水平（y 恒 0）；竖直单独读
            float vy = _loop.Player.VerticalSpeed;

            left +=
                $"Input   pressed {_sampler.PressedCount}  used {_sampler.ConsumedCount}  " +
                $"queued {_sampler.PendingCount}  dropped {_sampler.DroppedCount}   {balanceNote}   " +
                $"last@{_sampler.LastAttackTick}\n" +
                $"Frame   #{f.Tick}  mv({f.MoveX,5:F2},{f.MoveY,5:F2})  " +
                $"lk({f.LookX,6:F2},{f.LookY,6:F2})  atk {(f.Attack ? "Y" : "-")}  " +
                $"cam yaw {_loop.Camera.Yaw,6:F1}  pitch {_loop.Camera.Pitch,5:F1}\n" +
                // speed 是**自驱水平**速度大小（MoveVelocity）；竖直离地时单独显示 vy。
                // 冲量不进 speed（它有自己的「推」）——通道分离后三者互不混。
                // t 是 StateTicks：本状态已持续几个 tick，进入那 tick 记 0。
                // 攻击默认 30 tick，所以出招时它会从 0 数到 29。
                // buf 是预输入剩余有效期，只在非 0 时出现——它就是「按下的攻击键还记着」的信号。
                // State 最长的名字是 Hitstun（7 字符），按它对齐。
                // hit 只在非 0 时出现——它就是「这一刀打中了」的信号，
                // 也是验证「同一招式不重复命中同一目标」的唯一手段
                //（同一个对手被打 6 次和被打 1 次，颜色上分不出来，得看数字）。
                // stop 只在冻结期间出现，从 6 数到 1。顿帧是在命中那 tick 的**下一 tick**
                // 才开始冻，所以它比 hit 晚一 tick 出现。
                // hp / inv / 推 是受击那块的账本：inv 是无敌倒数（30 → 1）；
                // 推 只在冲量速度非零时出现——**speed 不反映冲量**（它显示的是自驱水平，
                // 冲量是另一条通道），所以「被打飞」的过程只能靠这个「推」的数字看见。
                $"Player  {_loop.Player.State,-7} t{_loop.Player.StateTicks,-4}" +
                $"  hp {_loop.Player.Health,3}/{_loop.Player.MaxHealth}" +
                (_loop.Player.BufferedAttackTicksLeft > 0 ? $"  buf{_loop.Player.BufferedAttackTicksLeft}" : "     ") +
                (_loop.Player.HitTargetCount > 0 ? $"  hit{_loop.Player.HitTargetCount}" : "     ") +
                (_loop.HitStopTicksLeft > 0 ? $"  stop{_loop.HitStopTicksLeft}" : string.Empty) +
                (_loop.Player.InvincibleTicksLeft > 0 ? $"  inv{_loop.Player.InvincibleTicksLeft}" : string.Empty) +
                (_loop.Player.ImpulseVelocity.sqrMagnitude > 0f
                    ? $"  推{_loop.Player.ImpulseVelocity.magnitude:F1}"
                    : string.Empty) +
                // 离地时显示竖直速度和「离地」标记——落地判定和跳跃的观测窗口
                (_loop.Player.IsGrounded ? string.Empty : $"  vy{vy,6:F2} 离地") +
                // 慢放倒计时（完美闪避触发，按真实 tick 数）
                (_loop.SlowMotionTicksLeft > 0 ? $"  slow{_loop.SlowMotionTicksLeft}" : string.Empty) +
                // 锁定目标下标（-1 不显示）——中键锁定 / 解除的可视信号
                (_loop.LockedTargetIndex >= 0 ? $"  lock#{_loop.LockedTargetIndex}" : string.Empty) +
                $"  speed {vel.magnitude,5:F2} m/s  pos ({pos.x,6:F2},{pos.y,6:F2},{pos.z,6:F2})";

            left += BuildActorsLine();
            left += HitEventLine();

            right = BuildHistory();
        }

        /// <summary>
        /// 命中事件（H3）：本 tick 事件数 + 最近一条非空事件。
        /// 「最近一条」是 Sample() 在渲染帧里抄下来的（缓冲每世界 tick 清空）。
        /// </summary>
        private string HitEventLine()
        {
            int current = _loop.HitEvents != null ? _loop.HitEvents.Count : 0;

            if (!_hasLastHitEvent)
            {
                return $"hit     this-tick {current}  (none yet)\n";
            }

            HitEvent e = _lastHitEvent;
            string attacker = e.Attacker == null
                ? "debug"
                : ReferenceEquals(e.Attacker, _loop.Player) ? "P" : "D";
            string target = ReferenceEquals(e.Target, _loop.Player) ? "P" : "D";

            return $"hit     this-tick {current}  last t{_lastHitEventTick} " +
                   $"{attacker}->{target} dmg {e.Damage} {e.Result} " +
                   $"@({e.Point.x:F2},{e.Point.y:F2},{e.Point.z:F2})\n";
        }

        /// <summary>
        /// 其他角色那一行：血量 + 状态 + 是否还在被推。
        ///
        /// 为什么非要有这一行：验收里的「沙包掉血、进硬直」只有数字能验（方块被推能看出来，
        /// 但血从 100 变 90 肉眼看不出）。而且**沙包那栏出现 `Hitstun`** 是
        /// 「受害者现在是一个真角色」的直接证据。
        ///
        /// 玩家自己不在这一行——它在上面那行，字段更全。
        /// </summary>
        private string BuildActorsLine()
        {
            PlayerSim[] actors = _loop.Actors;
            if (actors == null || actors.Length <= 1)
            {
                return "\nActors (没配其他角色——BattleLoop 的 Other Actors 是空的)";
            }

            string text = "\nActors";
            for (int i = 1; i < actors.Length; i++)
            {
                PlayerSim a = actors[i];
                text += $"  #{i} hp {a.Health}/{a.MaxHealth}";

                // 状态只在不是 Idle 时标出来（站着不动的沙包一直是 Idle，显示它没信息量）
                if (a.State != PlayerState.Idle) { text += $" {a.State}"; }

                // 无敌倒数：不显示它的话，「砍了不掉血」只能靠猜（沙包挨打后 0.5 秒免伤）
                if (a.InvincibleTicksLeft > 0) { text += $" inv{a.InvincibleTicksLeft}"; }

                // 推：只在冲量速度非零时出现，能看出「哪几帧在滑」
                if (a.ImpulseVelocity.sqrMagnitude > 0f)
                {
                    text += $" 推{a.ImpulseVelocity.magnitude:F1}";
                }
            }
            return text;
        }

        private string BuildHistory()
        {
            float windowSeconds = _windowCount * SimConstants.FixedDeltaTime;

            string text =
                $"连续  窗口 {_windowCount} 样本 / {windowSeconds:F1}s   " +
                $"采样 {_samplesTaken} 有效 {_samplesDiscarded} 作废(掉帧跨 tick)\n" +
                $"      水平 max|dv| {_windowMaxDelta,6:F4}  " +
                $"上限 {DeltaLimit:F4}(+{DeltaTolerance:G})   " +
                $"[{(_windowViolations == 0 ? "OK" : "超限")}]  超限 {_windowViolations} 次\n";

            text += $"\n切换  最近 {_transitionCount} 次 (新 -> 旧)\n";

            if (_transitionCount == 0)
            {
                text += "(暂无记录——按一下 WASD 或方向键)\n";
                return text;
            }

            int length = _transitions.Length;
            for (int i = 0; i < _transitionCount; i++)
            {
                int index = ((_transitionHead - 1 - i) % length + length) % length;
                Transition t = _transitions[index];
                text += $"@{t.Tick,-6} {t.From,-7} -> {t.To,-7}  " +
                        $"|v| {t.SpeedBefore,5:F2} -> {t.SpeedAfter,5:F2}   |dv| {t.DeltaV:F3}\n";
            }

            return text;
        }

        // ------------------------------------------------------------ 样式

        /// <summary>GUIStyle 建一次就够了——它是个有状态的引用类型，没必要每帧 new。</summary>
        private void EnsureStyles(int fontSize)
        {
            if (_style != null && _styleFontSize == fontSize)
            {
                return;
            }

            _styleFontSize = fontSize;
            _style = MakeStyle(Color.white, fontSize);
            _shadow = MakeStyle(Color.black, fontSize);
        }

        /// <summary>
        /// GUIStyle 是个状态机，有 normal / hover / active / focused 四组状态，各自带独立的 textColor。
        /// 只设 normal 是不够的：鼠标一进到 Label 的矩形里，Unity 就改用 hover 那一组，
        /// 没设过的会回退到 GUI.skin.label 的默认值（运行时是白色），于是文字和阴影一起变白。
        /// 这个 HUD 不需要悬停反馈，所以四组全设成同一个颜色。
        ///
        /// （另有 onNormal/onHover/... 一组，是 toggle 类控件用的；GUI.Label 永远传 on=false，不必管。）
        /// </summary>
        private static GUIStyle MakeStyle(Color textColor, int fontSize)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
            };

            style.normal.textColor = textColor;
            style.hover.textColor = textColor;
            style.active.textColor = textColor;
            style.focused.textColor = textColor;

            return style;
        }
    }
}
