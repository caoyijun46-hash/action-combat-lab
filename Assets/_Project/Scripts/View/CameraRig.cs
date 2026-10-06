using ActionGame.Sim;
using UnityEngine;

namespace ActionGame.View
{
    /// <summary>
    /// 把相机摆到该在的位置和朝向上。挂在相机所在的 GameObject 上。
    ///
    /// **方向反过来了**：视角改造之前是「Sim 每 tick 读相机的 <c>eulerAngles.y</c>」，
    /// 现在是「本类读 <see cref="CameraSim"/> 来摆相机」。Sim 不再碰任何场景对象，
    /// 而 <see cref="CameraSim"/> 是纯数据、可回放。
    ///
    /// 和 <see cref="PlayerView"/> 一个模式：数据由 Boot 通过 <see cref="Bind"/> 交进来，
    /// 所以本类**只依赖 Sim、不依赖 Boot** —— 真正的表现层不许走铁律 1 那张表里那条 `△`
    /// （那是留给 <see cref="DebugHud"/> / <see cref="HitboxGizmo"/> 这类调试工具的）。
    ///
    /// **这一版是第一人称**：相机就贴在眼睛位置，朝向完全由 <see cref="CameraSim"/> 决定。
    /// （C1 时的过肩第三人称是「角色位置 + 后上方偏移」，那几行现在是历史了。）
    ///
    /// 从头做第一人称而不是一开始就做，是因为做 C1 那版**能看见角色**，
    /// 调朝向、调判定胶囊方向时肉眼就能判断对错——那一步的验收就是这么过的。
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        private CameraSim _camera;
        private PlayerSim _player;

        /// <summary>由 Boot 在装配时调用。</summary>
        public void Bind(CameraSim cameraSim, PlayerSim player)
        {
            _camera = cameraSim;
            _player = player;
            Apply();
        }

        private void LateUpdate()
        {
            if (_camera == null || _player == null) return;
            Apply();
        }

        private void Apply()
        {
            // 位置用**和 PlayerView 一样的 alpha** 插值。
            // 两边必须用同一个比例，否则相机会跟着角色的「未插值位置」走，
            // 走起来会有一顿一顿的感觉——和 Step 4 修的是同一个毛病。
            float alpha = (Time.time - Time.fixedTime) / SimConstants.FixedDeltaTime;
            Vector3 basePosition = Vector3.Lerp(
                _player.PreviousPosition, _player.Position, Mathf.Clamp01(alpha));

            // 相机挂在角色位置往上 `CameraHeightOffset` 米的地方。
            // 位置**不受 yaw 影响**——相机就在身上，转视角不会让它绕着身体转。
            transform.position = basePosition + Vector3.up * SimConstants.CameraHeightOffset;

            // 朝向也插值。**这里和 C1 时写的注释是反的，改对了。**
            //
            // C1 时我认为「朝向插值会让鼠标感觉黏」所以直接设。那是**过肩第三人称**下
            // 得出的结论——相机离角色 2.5 米，视角变化不那么刺眼，抖动不明显。
            // 换到第一人称就站不住了：**相机旋转就是画面旋转**，
            // 而 CameraSim 每 tick 才推进一次（60Hz），渲染 100+ 帧，
            // 不插值的话画面每 16.7ms 跳一格，看起来就是抖。
            //
            // 代价是鼠标多了**一个 tick（约 16.7ms）的延迟**。这是渲染插值的固有代价，
            // 和 PlayerView 那边一样——只能在两个已知状态之间插值，不能预测还没算出来的。
            //
            // （注意：插值只能抹平「台阶」，抹不平「速度摆动」。
            //   鼠标增量在渲染帧累积、在 tick 消费，一 tick 摊到 1 个还是 2 个渲染帧
            //   取决于帧率相位，所以每次消费的量本身就不均匀。真要根治得让画面朝向
            //   直接吃「还没被 tick 消费掉的增量」——那要动分层，等不够用了再说。）
            float yaw = Mathf.LerpAngle(_camera.PreviousYaw, _camera.Yaw, Mathf.Clamp01(alpha));
            float pitch = Mathf.Lerp(_camera.PreviousPitch, _camera.Pitch, Mathf.Clamp01(alpha));

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
