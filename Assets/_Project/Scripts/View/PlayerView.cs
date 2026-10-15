using ActionGame.Sim;
using UnityEngine;

namespace ActionGame.View
{
    /// <summary>
    /// 把 Sim 里的位置搬到 Transform 上，并做**渲染插值**。
    ///
    /// View 层的全部职责就是这个：读 Sim 状态 → 写表现。它不算任何东西，
    /// 一个字的移动/战斗逻辑都不许有。PlayerSim 由 Boot 层通过 <see cref="Bind"/> 交进来，
    /// 所以本类只依赖 Sim，不依赖 Boot。
    ///
    /// 为什么需要插值：模拟跑 60Hz，渲染帧率可以任意，两者不是一一对应。
    /// 直接显示「最新位置」会出现——没有 tick 的帧原地不动、跑了 2 个 tick 的帧一下跳两格，
    /// 看起来就是一顿一顿的。帧率越高越明显。
    ///
    /// 插值改成显示「最近两个 tick 位置之间的某个比例」，画面就连续了。
    /// 代价是画面比模拟**落后一个 tick**（约 16.7ms）——这是插值的固有代价：
    /// 只能在两个已知状态之间插值，不能预测还没算出来的状态。
    /// </summary>
    public class PlayerView : MonoBehaviour
    {
        [Tooltip("倾斜和摆放偏移写在这个子物体上（通常是一层空物体，下面挂 FBX 实例）。\n" +
                 "写在这里而不是根上：根只管位置和朝向；动画采样动的是 FBX 自己那棵子树，互不打架。")]
        [SerializeField] private Transform _modelPivot;

        [Tooltip("移动时的最大倾斜角（度）。0 = 关掉。方向按移动方向：前倾 / 后仰 / 侧倾。")]
        [SerializeField] private float _maxLeanDegrees = 12f;

        [Tooltip("模型相对 Sim 位置的竖直偏移（米）。脚底对齐是**自动**的（模型原点在脚底时，0 = 脚踩地）；" +
                 "要「半离地」这类效果就填正数。")]
        [SerializeField] private float _visualOffsetY = 0f;

        private PlayerSim _player;

        /// <summary>由 Boot 层在装配时调用。</summary>
        public void Bind(PlayerSim player)
        {
            _player = player;

            // 先摆到位，免得第一帧从场景里摆放的位置跳一下。
            transform.position = player.Position;
            transform.rotation = Quaternion.Euler(0f, player.FacingYaw, 0f);
        }

        private void LateUpdate()
        {
            if (_player == null) return;   // 还没被 Bind（或 Boot 装配失败）

            // Time.fixedTime = 最近一次 FixedUpdate 开始的时刻；Time.time = 现在。
            // 两者之差 ÷ 固定步长 = 距离上次 tick 过去了这个周期的百分之几。
            // Clamp01 防的是「tick 迟到」——那时算出来会大于 1，不夹住会插过头。
            float alpha = (Time.time - Time.fixedTime) / SimConstants.FixedDeltaTime;

            transform.position = Vector3.Lerp(
                _player.PreviousPosition,
                _player.Position,
                Mathf.Clamp01(alpha));

            // 朝向**不做插值，直接设**。位置插值是为了消抖动，朝向插值却是害的：
            // 判定胶囊用的是 Sim 里那个 FacingYaw，方块必须显示同一个值，
            // 否则你看到方块朝一边、判定框朝另一边，验收时根本判断不了对错。
            // 代价是转向会「啪」一下（输入方向一变就跳），动作游戏里通常要加转身速度——
            // 那是后面的事，现在**看见真相**比好看重要。
            transform.rotation = Quaternion.Euler(0f, _player.FacingYaw, 0f);

            // ---- 倾斜：Move 的观感 ----
            // Move 不单独做动画（和 Idle 同一个 clip），"在走"靠这层小倾角表现：
            //   驱动源是 MoveVelocity（自驱水平）而不是输入方向——输入是突变的（按下瞬间就是 90°，倾斜会跳），
            //   MoveVelocity 自带加速 / 减速 → 倾斜角连续长出来，松手自动回正；
            //   方向**相对朝向**：前倾 = 往前走，后仰 = 按 S 倒退，侧倾 = 横移（往哪走往哪倒）。
            if (_modelPivot == null) { return; }

            Vector3 localVelocity = Quaternion.Euler(0f, -_player.FacingYaw, 0f) * _player.MoveVelocity;
            float pitch = localVelocity.z / _player.MaxMoveSpeed * _maxLeanDegrees;   // +X = 前倾
            float roll = -localVelocity.x / _player.MaxMoveSpeed * _maxLeanDegrees;   // -Z = 向右倒

            _modelPivot.localRotation = Quaternion.Euler(pitch, 0f, roll);

            // 竖直摆放：模型原点在**脚底**（建模约定），而 Sim 的 Position 是**中心**——
            // 自动下移一个半高，脚就落在 Sim 认的地面上；「半离地」这类艺术调整加在 _visualOffsetY。
            _modelPivot.localPosition = new Vector3(0f, -_player.HalfHeight + _visualOffsetY, 0f);
        }
    }
}
