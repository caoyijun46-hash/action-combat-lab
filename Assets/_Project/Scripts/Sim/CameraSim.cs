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
    }
}
