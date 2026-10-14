using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// 相机的朝向。**这是模拟状态，不是表现。**
    ///
    /// 为什么相机朝向要进 <c>Sim/</c>：它决定**移动方向**（W 朝哪走）和**判定胶囊朝向**
    /// （第一人称下角色朝向 = 相机朝向）。这两件事都影响模拟结果，
    /// 所以要满足「同一串输入必然得到同一结果」——相机朝向必须可回放。
    ///
    /// 改造之前，相机朝向是场景里一个 Transform，Sim 每 tick 去读它的 <c>eulerAngles.y</c>。
    /// 那其实留了个口子：**谁能手动转一下那个相机，同一串输入就会得到不同的移动方向**。
    /// 现在相机朝向由 <c>Look</c> 输入驱动、存在这里，方向反过来了——
    /// Sim 不再读场景，改成 View 读 Sim 来摆相机。
    ///
    /// 顺便：`InputFrame.LookX/LookY` 从块 2b 起就一直在采集，
    /// 但**从来没有任何消费者**（HUD 里那句 `Look :` 一直只是显示）。这个类就是它的第一个消费者。
    ///
    /// 本类用了 <c>Mathf</c>，属铁律 1 明确放行的无状态数学类型。
    /// </summary>
    public class CameraSim
    {
        /// <summary>水平偏航角，度。归一化在 <c>[0, 360)</c>。</summary>
        public float Yaw { get; private set; }

        /// <summary>俯仰角，度。正值 = 抬头，范围 <c>[-MaxPitch, +MaxPitch]</c>。</summary>
        public float Pitch { get; private set; }

        /// <summary>上一个 tick 的 <see cref="Yaw"/>，供 View 做**渲染插值**。</summary>
        public float PreviousYaw { get; private set; }

        /// <summary>上一个 tick 的 <see cref="Pitch"/>，供 View 做**渲染插值**。</summary>
        public float PreviousPitch { get; private set; }

        /// <summary>
        /// 当前视线方向（单位向量，世界空间）。约定和 <see cref="PlayerSim.FacingDirection"/> 同源：
        /// yaw θ 的水平前方 = (sinθ, 0, cosθ)；抬头（Pitch 为正）时 y 分量为正。
        /// 供锁定选目标（夹角比较）用。
        /// </summary>
        public Vector3 ViewForward
        {
            get
            {
                float yawRad = Yaw * Mathf.Deg2Rad;
                float pitchRad = Pitch * Mathf.Deg2Rad;
                float cosPitch = Mathf.Cos(pitchRad);
                return new Vector3(
                    Mathf.Sin(yawRad) * cosPitch,
                    Mathf.Sin(pitchRad),
                    Mathf.Cos(yawRad) * cosPitch);
            }
        }

        /// <param name="initialYaw">初始偏航角，度。一般用角色出生时的朝向。</param>
        public CameraSim(float initialYaw)
        {
            Yaw = Mathf.Repeat(initialYaw, 360f);
            Pitch = 0f;
            PreviousYaw = Yaw;
            PreviousPitch = Pitch;
        }

        /// <summary>
        /// 推进一个 tick。**注意没有 dt 参数**——这是有意的。
        ///
        /// 鼠标给的是**位移**（这一 tick 累计移动了多少像素），不是速率。
        /// 所以「想转多少角度」= 位移 × 灵敏度，和时间长短无关：
        /// 一 tick 内移动 10 像素，想转的就是 10 × 0.15 = 1.5°，
        /// 哪怕这一 tick 因为卡顿花了 33ms 也一样。
        ///
        /// 但**实际转多少受速度上限约束**（2026-10-14 加）：每 tick 最多转
        /// <see cref="SimConstants.CameraMaxTurnSpeedDegreesPerSecond"/> × dt，
        /// **超出的部分直接丢掉**（用户拍板：不做「欠账追赶」——没有追账尾巴，
        /// 代价是超速甩视角时鼠标相对视角「打滑」）。限速是速率、输入是位移，
        /// 这个换算只发生在本方法内部（用 <c>FixedDeltaTime</c>，参数上依然不收 dt）。
        /// </summary>
        public void Tick(InputFrame frame)
        {
            // 先记下旧值，供 View 插值用（和 PlayerSim.PreviousPosition 同一个用途）
            PreviousYaw = Yaw;
            PreviousPitch = Pitch;

            // 限速：这一 tick 最多转 maxDelta，超出的直接丢。
            float maxDelta = SimConstants.CameraMaxTurnSpeedDegreesPerSecond * SimConstants.FixedDeltaTime;

            // 左右：愿望 = 位移 × 灵敏度，夹到 ±maxDelta 后累加。
            // Repeat 夹回 [0, 360)：转几十圈之后角度大到 float 精度开始损失
            //（Yaw 只用来算 sin/cos，但精度掉了会看出抖动）。
            float yawStep = Mathf.Clamp(
                frame.LookX * SimConstants.MouseSensitivityDegreesPerPixel, -maxDelta, maxDelta);
            Yaw = Mathf.Repeat(Yaw + yawStep, 360f);

            // 上下：愿望同样夹到 ±maxDelta，再夹到俯仰限位。
            //
            // 这里是**减号**，已经实测确认过：Input System 的 `<Pointer>/delta` 的 y 轴
            // 在屏幕坐标系下**向下为正**，所以鼠标往下推时 LookY 是正的，要抬头就得减。
            float pitchStep = Mathf.Clamp(
                -frame.LookY * SimConstants.MouseSensitivityDegreesPerPixel, -maxDelta, maxDelta);
            Pitch = Mathf.Clamp(
                Pitch + pitchStep,
                -SimConstants.MaxPitchDegrees,
                SimConstants.MaxPitchDegrees);
        }

        /// <summary>
        /// 锁定时的推进：**忽略鼠标**，自动转向 <paramref name="targetPosition"/> 的
        /// **眼睛高度**（<c>+ CameraHeightOffset</c>，和玩家自己算眼睛用同一个常量，
        /// 2026-10-15 用户定：瞄准点 = 敌人身上「摄像机该在的位置」）。
        ///
        /// 速度曲线（用户 2026-10-14 定）：追赶速度 = |视线夹角误差| ×
        /// <see cref="SimConstants.LockOnTurnGainPerSecond"/>，夹在
        /// [<see cref="SimConstants.LockOnTurnMinSpeedDegreesPerSecond"/>,
        /// <see cref="SimConstants.LockOnTurnMaxSpeedDegreesPerSecond"/>] 之间
        ///（上限 2026-10-15 从鼠标那条拆开、单独调）——
        /// 误差大（目标在屏幕边上 / 身后）→ 顶到硬上限追得快；快对齐 → 按比例慢下来。
        /// 每步再取「不超过剩余误差」（吸附），最后一步精确落位、不来回抖。
        ///
        /// 相机的「眼睛」位置 = <paramref name="ownerPosition"/> 上方
        /// <see cref="SimConstants.CameraHeightOffset"/> 米（和 CameraRig 摆相机用的是同一个常量）。
        /// </summary>
        public void TickLocked(Vector3 ownerPosition, Vector3 targetPosition)
        {
            // 先记下旧值，供 View 插值用（和 Tick 一样）
            PreviousYaw = Yaw;
            PreviousPitch = Pitch;

            // 看向目标所需的 yaw / pitch（约定：yaw θ → (sinθ, 0, cosθ)；Pitch 正 = 抬头）。
            // 瞄准点 = 敌人的**眼睛高度**（和玩家自己的眼睛公式同一个常量）——
            // 两边站在同一块地上时 Δy = 0，视线**水平**；敌人被挑空 → 视线自动抬起来。
            Vector3 eye = ownerPosition + Vector3.up * SimConstants.CameraHeightOffset;
            Vector3 aimPoint = targetPosition + Vector3.up * SimConstants.CameraHeightOffset;
            Vector3 dir = aimPoint - eye;
            float horizontal = Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z);
            float desiredYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float desiredPitch = Mathf.Atan2(dir.y, horizontal) * Mathf.Rad2Deg;

            // yaw 取**最短角差**：手算（Mathf.Repeat 是托管实现，离线 harness 里验证过；
            // DeltaAngle 没验证过，不碰——ECall 的教训还热着）。
            float yawError = Mathf.Repeat(desiredYaw - Yaw + 180f, 360f) - 180f;
            Yaw = Mathf.Repeat(Yaw + TurnStep(yawError), 360f);

            // pitch 误差没有环绕问题，直接算
            Pitch = Mathf.Clamp(
                Pitch + TurnStep(desiredPitch - Pitch),
                -SimConstants.MaxPitchDegrees,
                SimConstants.MaxPitchDegrees);
        }

        /// <summary>
        /// 锁定转向的「一步」：速度随误差增长（有下限和硬上限），且**一步不超过剩余误差**（吸附）。
        /// </summary>
        private static float TurnStep(float error)
        {
            float rate = Mathf.Clamp(
                Mathf.Abs(error) * SimConstants.LockOnTurnGainPerSecond,
                SimConstants.LockOnTurnMinSpeedDegreesPerSecond,
                SimConstants.LockOnTurnMaxSpeedDegreesPerSecond);

            float step = Mathf.Min(Mathf.Abs(error), rate * SimConstants.FixedDeltaTime);
            return error < 0f ? -step : step;
        }

        /// <summary>
        /// 视角锁定的**目标挑选**（纯函数，离线可测）：在所有角色里（**跳过 [0]——玩家自己**）
        /// 找「离眼睛 <paramref name="rangeMeters"/> 米内、与当前视线夹角最小」的那个，
        /// 返回下标；没有符合条件的返回 -1。
        ///
        /// 「与视线夹角最小」 = 与 <see cref="ViewForward"/> 的点积最大——点积单调，
        /// 不用算 Acos。这也就是「离屏幕中心最近」的代理：不用做屏幕投影
        ///（那是 View 的活，Sim 算不了）。
        /// 夹角和距离都量到敌人的**眼睛高度**——和 <see cref="TickLocked"/> 的瞄准点是同一个点
        ///（2026-10-15）。
        ///
        /// v1 没有角度门槛：目标在身后也能锁上（视角会自己转过去）——要不要加
        ///「必须大致在视线前方」的限制，等 Play 后说。
        /// </summary>
        public int PickLockTarget(PlayerSim[] actors, PlayerSim self, float rangeMeters)
        {
            Vector3 eye = self.Position + Vector3.up * SimConstants.CameraHeightOffset;
            Vector3 forward = ViewForward;
            float rangeSqr = rangeMeters * rangeMeters;

            int best = -1;
            float bestDot = -2f;   // 任何单位向量的点积都 ≥ -1，比它大

            for (int i = 1; i < actors.Length; i++)
            {
                // 量到敌人的**眼睛高度**——和 TickLocked 的瞄准点一致
                Vector3 to = (actors[i].Position + Vector3.up * SimConstants.CameraHeightOffset) - eye;
                float sqr = to.sqrMagnitude;
                if (sqr > rangeSqr || sqr < 1e-8f) { continue; }   // 范围外 / 位置重合（方向没意义）

                float dot = Vector3.Dot(forward, to / Mathf.Sqrt(sqr));
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = i;
                }
            }
            return best;
        }
    }
}
