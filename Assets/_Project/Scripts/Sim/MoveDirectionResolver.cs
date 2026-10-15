using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// 「move 输入 + 相机朝向」→ 世界方向的解析器，带**锁定中的防甩保持**（用户 2026-10-15 定）。
    ///
    /// 问题：移动方向是相机相对的、每 tick 现解——锁定中大回摆（冲过敌人、镜头绕回来）
    /// 会把按住的方向键一起拖转，角色画弧「绕敌人旋转」。
    ///
    /// 规则（用户 2026-10-15 定的最终口径）：
    ///   进入：**锁定中**且「敌人方向与视线的水平角误差」超过 `LockOnFreezeAngleDegrees`
    ///        → 方向的**参照系 yaw 冻结**（= 进入那一刻的相机 yaw）；
    ///   解除：**只有 move 输入变化**（方向角 / 幅度超 ε，或过零）或退出锁定——
    ///        误差回到阈值以内**不**解除（保持会一直挂着，直到动一下输入）。
    /// 其余时候参照系每 tick 跟着相机——和原来的相机相对解析**逐位一致**。
    ///
    /// 它是**有状态的**（上次输入 / 参照系 / 迟滞闩）——像 <c>TimeControl</c> 一样做成纯类，
    /// 离线测试直接实例化。状态只在 <see cref="Resolve"/> 里更新，调用方每 tick 喂一次。
    ///
    /// 本文件用了 <c>Vector3</c> / <c>Mathf</c>，属铁律 1 明确放行的无状态数学类型。
    /// </summary>
    public class MoveDirectionResolver
    {
        private float _lastMoveX;
        private float _lastMoveY;
        private bool _hasLastInput;

        /// <summary>方向的参照系（相机 yaw，度）。冻结时不动，跟踪时每 tick 跟。</summary>
        private float _referenceYaw;

        /// <summary>
        /// 上一 tick 是否处于「保持」状态。单阈值之后它就是每 tick 现算的值——
        /// 留着这个字段只为了「参照系更新的前后两边判断」（见 <see cref="Resolve"/>）。
        /// </summary>
        private bool _frozen;

        /// <param name="moveX">move 输入（相机空间，长度 ≤ 1）。</param>
        /// <param name="moveY">同上。</param>
        /// <param name="cameraYaw">这一 tick 的相机 yaw，度。</param>
        /// <param name="locked">这一 tick 是否在视角锁定中（不在就不可能有保持）。</param>
        /// <param name="toEnemy">
        /// 玩家指向锁定目标的眼睛高度的向量（世界空间）；只有水平分量参与门控。
        /// 未锁定时随便传（不参与）。
        /// </param>
        /// <param name="globalData">
        /// 全局数据（MV3a）：冻结角门槛 + 两个输入 ε 都从它读。每 tick 现读——热调。
        /// </param>
        public Vector3 Resolve(float moveX, float moveY, float cameraYaw, bool locked, Vector3 toEnemy,
            GlobalData globalData)
        {
            bool wasFrozen = _frozen;
            bool inputChanged = InputChanged(moveX, moveY, globalData);

            // ---- 保持闩（用户 2026-10-15 口径）----
            //   进入：锁定中且误差超过阈值（只在「没保持」时评估）；
            //   解除：**只有 move 输入变化**——误差回到阈值以内也不解除。
            if (!locked)
            {
                _frozen = false;
            }
            else if (_frozen)
            {
                if (inputChanged) { _frozen = false; }
            }
            else
            {
                _frozen = HorizontalAngleErrorDegrees(toEnemy, cameraYaw)
                    > globalData.LockOnFreezeAngleDegrees;
            }

            // ---- 参照系：跟踪时每 tick 跟相机；保持时冻住 ----
            // 更新条件是「前后两边至少有一边没在保持」（和 PlayerSim 的朝向锁定同一个惯用法）：
            // 进保持的那一 tick 用**当前** yaw 当参照系（避免差一拍），解除的那一 tick 立刻恢复。
            if (inputChanged || !wasFrozen || !_frozen)
            {
                _referenceYaw = cameraYaw;
            }

            _lastMoveX = moveX;
            _lastMoveY = moveY;
            _hasLastInput = true;

            return ActorCommand.CameraRelativeDirection(moveX, moveY, _referenceYaw);
        }

        /// <summary>
        /// 「敌人方向」和「视线方向」的**水平**夹角（度，0~180）。
        /// 只看 yaw——pitch 摆动不会带动移动方向，不该参与门控。
        /// 敌人几乎在自己正上方 / 正下方（水平距离为 0）时方向没有意义，当作 0（不触发保持）。
        /// </summary>
        private static float HorizontalAngleErrorDegrees(Vector3 toEnemy, float cameraYaw)
        {
            float horizontal = Mathf.Sqrt(toEnemy.x * toEnemy.x + toEnemy.z * toEnemy.z);
            if (horizontal < 1e-4f) { return 0f; }

            float enemyYaw = Mathf.Atan2(toEnemy.x, toEnemy.z) * Mathf.Rad2Deg;

            // 最短角差，手算（Mathf.Repeat 是托管实现，离线 harness 验证过）
            return Mathf.Abs(Mathf.Repeat(enemyYaw - cameraYaw + 180f, 360f) - 180f);
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
