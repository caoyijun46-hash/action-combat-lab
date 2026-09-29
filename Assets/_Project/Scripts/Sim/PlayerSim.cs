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

        public PlayerSim(Vector3 spawnPosition)
        {
            Position = spawnPosition;
            PreviousPosition = spawnPosition;
            Velocity = Vector3.zero;
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
            Vector3 targetVelocity = direction * SimConstants.MaxMoveSpeed;

            // 有方向输入就按加速度逼近目标速度，松手就按减速度逼近零。
            // 用 MoveTowards 而不是 Lerp：Lerp 是指数衰减，永远到不了目标值。
            float rate = direction.sqrMagnitude > 0f
                ? SimConstants.MoveAcceleration
                : SimConstants.MoveDeceleration;

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
