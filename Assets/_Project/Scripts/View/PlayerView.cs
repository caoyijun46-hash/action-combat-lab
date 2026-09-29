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
        private PlayerSim _player;

        /// <summary>由 Boot 层在装配时调用。</summary>
        public void Bind(PlayerSim player)
        {
            _player = player;

            // 先摆到位，免得第一帧从场景里摆放的位置跳一下。
            transform.position = player.Position;
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
        }
    }
}
