using ActionGame.Sim;
using UnityEngine;

namespace ActionGame.View
{
    /// <summary>
    /// 把 Sim 的状态搬到骨架上：**每帧把 (State, StateTicks) 翻译成 (clip, 时间) 采样一次**。
    ///
    /// 这是「动画由代码驱动」的落点（见 `AGENTS.md`「动画」一节）：唯一的状态机在 Sim 里，
    /// 本类只是播放器——不用 Animator Controller 的转移。三个直接后果：
    ///   - **顿帧 / 硬直自动冻住**：StateTicks 不前进 → 采样时间不前进；
    ///   - 动画**不可能**和判定错位（两边读的是同一个 tick 数）；
    ///   - 用 `SampleAnimation` 而不是 Animator：语义就是「按时间采样这个 clip」，
    ///     不需要 Controller 资产。以后要做混合 / IK 再换成 Animator——
    ///     换的是本类内部，对外只有 Bind + 采样两件事。
    ///
    /// **采样目标是 FBX 实例根**（<see cref="_modelRoot"/>）：clip 里的骨骼路径从它开始解析。
    /// 层级约定：角色根 → ModelPivot（<see cref="PlayerView"/> 写倾斜 / 偏移）→ FBX 实例（本类写）。
    /// 采样会覆盖整棵子树，所以「我们的姿态」（倾斜、偏移）必须写在**它的父级**上，两边互不打架。
    /// </summary>
    public class ActorAnimation : MonoBehaviour
    {
        [Tooltip("FBX 实例的根物体。clip 的骨骼路径从它开始解析，采样作用在它和它的子树。")]
        [SerializeField] private Transform _modelRoot;

        [Tooltip("待机循环。Move 也用它——移动的观感靠 PlayerView 的倾斜。")]
        [SerializeField] private AnimationClip _idleClip;

        [Tooltip("攻击。先用 AttackRight.fbx 里的那个 clip；长度应约等于 AttackData.TotalTicks / 60 秒。")]
        [SerializeField] private AnimationClip _attackClip;

        [Tooltip("受击。还没做就留空——留空时借 Idle 顶。")]
        [SerializeField] private AnimationClip _hitstunClip;

        private PlayerSim _actor;

        private bool _wasAttacking;
        private bool _lengthWarned;

        /// <summary>由 Boot 在装配时调用（和 <see cref="PlayerView.Bind"/> 一个模式）。</summary>
        public void Bind(PlayerSim actor)
        {
            _actor = actor;
        }

        private void LateUpdate()
        {
            if (_actor == null || _modelRoot == null) { return; }

            // 进入攻击的那一 tick 对一次表（那时候 AttackData 才已经到位了——
            // Awake 时 Sim 还没 tick 过，读到的 TotalTicks 是默认值的，会误报）
            bool attacking = PlayerSim.IsAttacking(_actor.State);
            if (attacking && !_wasAttacking) { CheckAttackClipLength(); }
            _wasAttacking = attacking;

            AnimationClip clip = ClipFor(_actor.State);
            if (clip == null) { return; }

            clip.SampleAnimation(_modelRoot.gameObject, TimeAt(_actor, clip));
        }

        /// <summary>
        /// 状态 → clip。**Move 故意和 Idle 是同一个**——移动不切动作，
        /// 观感由 <see cref="PlayerView"/> 按移动方向做的倾斜来表现。
        /// </summary>
        private AnimationClip ClipFor(PlayerState state)
        {
            switch (state)
            {
                case PlayerState.AttackStartup:
                case PlayerState.AttackActive:
                case PlayerState.AttackRecovery:
                    return _attackClip;

                case PlayerState.Hitstun:
                    return _hitstunClip != null ? _hitstunClip : _idleClip;

                default:   // Idle / Move
                    return _idleClip;
            }
        }

        /// <summary>
        /// 这一帧该采样到第几秒。**全部用 tick 数算，不读 Game 逻辑时间**——
        /// 顿帧 / 硬直时 tick 不走，时间自然冻住。
        ///
        /// 渲染插值：和 <see cref="PlayerView"/> 用同一个 alpha，再往回退 1 tick——
        /// 因为位置显示的是「上一 tick → 这一 tick」之间的值，动画要跟同一个相位，
        /// 否则 60Hz 的姿势在 100+ 帧下会比位置更跳。
        /// </summary>
        private static float TimeAt(PlayerSim actor, AnimationClip clip)
        {
            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / SimConstants.FixedDeltaTime);

            switch (actor.State)
            {
                case PlayerState.AttackStartup:
                case PlayerState.AttackActive:
                case PlayerState.AttackRecovery:
                    // 「整招第几 tick」的连读算术在 Sim 里（AttackElapsedTicks）——这里不重推
                    return Mathf.Max(0f,
                        (actor.AttackElapsedTicks - 1f + alpha) * SimConstants.FixedDeltaTime);

                case PlayerState.Hitstun:
                    return Mathf.Max(0f, (actor.StateTicks - 1f + alpha) * SimConstants.FixedDeltaTime);

                default:   // Idle / Move：循环（相位也由 tick 数来，顿帧时一样冻住）
                    return Mathf.Repeat(
                        (actor.StateTicks - 1f + alpha) * SimConstants.FixedDeltaTime, clip.length);
            }
        }

        /// <summary>
        /// 开机对表：攻击 clip 的长度该 ≈ <c>AttackData.TotalTicks / 60</c> 秒。
        /// 差太多说明「动画没照 AttackData 烤」——这种错位肉眼几乎看不出来（差 2 tick 根本发现不了），
        /// 用一次检查兜住。容差 1.5 帧：导入时的首尾取整可能差 1 帧。
        /// </summary>
        private void CheckAttackClipLength()
        {
            if (_attackClip == null || _lengthWarned) { return; }

            float expected = _actor.CurrentAttackTotalTicks * SimConstants.FixedDeltaTime;
            float tolerance = 1.5f * SimConstants.FixedDeltaTime;

            if (Mathf.Abs(_attackClip.length - expected) > tolerance)
            {
                _lengthWarned = true;
                Debug.LogWarning(
                    $"[{nameof(ActorAnimation)}] Attack clip「{_attackClip.name}」长 {_attackClip.length:F3} 秒，" +
                    $"而 AttackData 是 {_actor.CurrentAttackTotalTicks} tick = {expected:F3} 秒——" +
                    "动画和判定会对不上。回 Blender 检查动作帧数（fps 60 时一帧 = 一 tick）。", this);
            }
        }
    }
}
