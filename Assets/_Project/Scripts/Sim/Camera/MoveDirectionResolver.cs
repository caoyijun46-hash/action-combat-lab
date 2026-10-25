using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// 「move 输入 + 相机朝向」→ 世界方向的解析器，带**锁定中的方向保持**（2026-10-24 改版）。
    ///
    /// 问题：移动方向是相机相对的、每 tick 现解——锁定中镜头一转
    /// 会把按住的方向键一起拖转，角色画弧「绕敌人旋转」。
    ///
    /// 规则（用户 2026-10-24 定的最终口径）：
    ///   **锁定中，只要 move 输入不变，世界方向就锁死**——不再看敌人/视角的夹角，
    ///   也没有「超过 60° 才冻结」的门槛；
    ///   唯一的重新解析时机：**move 输入变化**（方向角 / 幅度超 ε，或过零）、
    ///   刚进入锁定的那一 tick、或退出锁定。
    /// 未锁定时参照系每 tick 跟着相机——和原来的相机相对解析**逐位一致**。
    ///
    /// 它是**有状态的**（上次输入 / 参照系 / 上一 tick 的锁定状态）——像 <c>TimeControl</c>
    /// 一样做成纯类，离线测试直接实例化。状态只在 <see cref="Resolve"/> 里更新，调用方每 tick 喂一次。
    ///
    /// 本文件用了 <c>Vector3</c> / <c>Mathf</c>，属铁律 1 明确放行的无状态数学类型。
    /// </summary>
    public class MoveDirectionResolver
    {
        private float _lastMoveX;
        private float _lastMoveY;
        private bool _hasLastInput;

        /// <summary>方向的参照系（相机 yaw，度）。锁定中输入不变时冻住，其余时候每 tick 跟相机。</summary>
        private float _referenceYaw;

        /// <summary>上一 tick 是否锁定：用来识别「刚进入锁定」那一 tick，给它一次重新解析。</summary>
        private bool _wasLocked;

        /// <param name="moveX">move 输入（相机空间，长度 ≤ 1）。</param>
        /// <param name="moveY">同上。</param>
        /// <param name="cameraYaw">这一 tick 的相机 yaw，度。</param>
        /// <param name="locked">这一 tick 是否在视角锁定中。</param>
        /// <param name="globalData">
        /// 全局数据（MV3a）：两个输入 ε 从它读（判断 move 有没有变）。每 tick 现读——热调。
        /// </param>
        public Vector3 Resolve(float moveX, float moveY, float cameraYaw, bool locked,
            GlobalData globalData)
        {
            bool inputChanged = InputChanged(moveX, moveY, globalData);

            // 参照系更新时机：
            //   未锁定            → 每 tick 跟相机（原行为）；
            //   刚进入锁定        → 用当前 yaw 当参照系（避免差一拍）；
            //   锁定中 + 输入变了 → 用当前 yaw 重新解析；
            //   锁定中 + 输入没变 → **保持**参照系（世界方向锁死）。
            if (!locked || !_wasLocked || inputChanged)
            {
                _referenceYaw = cameraYaw;
            }

            _wasLocked = locked;
            _lastMoveX = moveX;
            _lastMoveY = moveY;
            _hasLastInput = true;

            return ActorCommand.CameraRelativeDirection(moveX, moveY, _referenceYaw);
        }

        /// <summary>
        /// move 输入是否变化——**方向**角变化超 ε、**幅度**变化超 ε、或过零，都算。
        /// （2026-10-15 用户口径：幅度变化也算「变化」——它是解除保持的扳机。）
        /// 两个 ε 从全局数据读（MV3a）。
        /// </summary>
        private bool InputChanged(float moveX, float moveY, GlobalData globalData)
        {
            if (!_hasLastInput) { return true; }

            float oldSqr = _lastMoveX * _lastMoveX + _lastMoveY * _lastMoveY;
            float newSqr = moveX * moveX + moveY * moveY;

            bool oldZero = oldSqr < 1e-8f;
            bool newZero = newSqr < 1e-8f;
            if (oldZero != newZero) { return true; }   // 从零起 / 回零
            if (newZero) { return false; }             // 都是零

            float oldMag = Mathf.Sqrt(oldSqr);
            float newMag = Mathf.Sqrt(newSqr);

            // 幅度变化（绝对，输入单位 0~1）
            if (Mathf.Abs(newMag - oldMag) > globalData.MoveInputMagnitudeEpsilon) { return true; }

            // 方向角变化（点积 vs cos ε）
            float dot = (_lastMoveX * moveX + _lastMoveY * moveY) / (oldMag * newMag);
            return dot < Mathf.Cos(globalData.MoveInputDirectionEpsilonDegrees * Mathf.Deg2Rad);
        }
    }
}
