using ActionGame.Sim;
using UnityEngine;

namespace ActionGame.View
{
    /// <summary>
    /// 把 <see cref="TargetSim"/> 的位置搬到 Transform 上，并做**渲染插值**。
    ///
    /// 和 <see cref="PlayerView"/> 是同一个模式：只读 Sim、只写表现、一点都不算。
    /// 数据由 Boot 通过 <see cref="Bind"/> 交进来，所以本类**只依赖 Sim，不依赖 Boot**
    /// ——真正的表现层不许走铁律 1 依赖表里那条 `△`（那是留给调试工具的）。
    ///
    /// 为什么要插值：击退是 60 tick/秒推进的，屏幕可以 100+ 帧/秒。
    /// 直接显示最新位置的话，靶子在被击退的那十几 tick 里会一顿一顿的。
    /// **Step 4 在玩家身上踩过一次，这是同一个坑的第二次**——所以这次一开始就带上。
    /// </summary>
    public class TargetView : MonoBehaviour
    {
        private TargetSim[] _targets;
        private int _index;

        /// <summary>
        /// 由 Boot 在装配时调用，把整组靶子和「我是第几个」告诉它。
        ///
        /// 传整个数组 + 下标，而不是把 <see cref="TargetSim"/> 拷贝一份进来：
        /// struct 拷贝的话，Sim 那边改了靶子位置，这里的副本还是旧的。
        /// 拿着数组引用 + 下标读，每次读到的都是最新的值。
        /// </summary>
        public void Bind(TargetSim[] targets, int index)
        {
            _targets = targets;
            _index = index;

            // 先摆到位，免得第一帧从场景里摆放的位置跳一下
            transform.position = targets[index].Position;
        }

        private void LateUpdate()
        {
            if (_targets == null) return;
            if (_index < 0 || _index >= _targets.Length) return;

            // 和 PlayerView 完全一样的算法：距离上次 tick 过去了这个周期的百分之几。
            // Clamp01 防的是「tick 迟到」——那时算出来会大于 1。
            float alpha = (Time.time - Time.fixedTime) / SimConstants.FixedDeltaTime;

            transform.position = Vector3.Lerp(
                _targets[_index].PreviousPosition,
                _targets[_index].Position,
                Mathf.Clamp01(alpha));
        }
    }
}
