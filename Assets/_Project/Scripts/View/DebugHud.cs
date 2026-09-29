using ActionGame.Boot;
using ActionGame.Inputs;
using ActionGame.Sim;
using UnityEngine;

namespace ActionGame.View
{
    /// <summary>
    /// 开发用 HUD。纯 View 层：只读状态、只写屏幕，不碰任何逻辑。
    ///
    /// 它直接引用了 <see cref="InputSampler"/>（View → Inputs），严格说不是理想的分层——
    /// 但它是调试工具而非游戏表现，图省事直接读。以后真正的角色表现层不许这么干。
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        [SerializeField] private BattleLoop _loop;
        [SerializeField] private InputSampler _sampler;

        private GUIStyle _style;
        private GUIStyle _shadow;

        private void OnGUI()
        {
            EnsureStyles();
            string text = BuildText();
            GUI.Label(new Rect(11, 11, 560, 380), text, _shadow);
            GUI.Label(new Rect(10, 10, 560, 380), text, _style);
        }

        /// <summary>
        /// GUIStyle 建一次就够了。原实现在 OnGUI 里 new 两个 style，
        /// 而 OnGUI 每帧要跑多次，等于持续产生垃圾。
        /// </summary>
        private void EnsureStyles()
        {
            if (_style != null) return;

            _style = MakeStyle(Color.white);
            _shadow = MakeStyle(Color.black);
        }

        /// <summary>
        /// GUIStyle 是个状态机，有 normal / hover / active / focused 四组状态，各自带独立的 textColor。
        /// 只设 normal 是不够的：鼠标一进到 Label 的矩形里，Unity 就改用 hover 那一组，
        /// 没设过的会回退到 GUI.skin.label 的默认值（运行时是白色），于是文字和阴影一起变白。
        /// 这个 HUD 不需要悬停反馈，所以四组全设成同一个颜色。
        ///
        /// （另有 onNormal/onHover/... 一组，是 toggle 类控件用的；GUI.Label 永远传 on=false，不必管。）
        /// </summary>
        private static GUIStyle MakeStyle(Color textColor)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
            };

            style.normal.textColor = textColor;
            style.hover.textColor = textColor;
            style.active.textColor = textColor;
            style.focused.textColor = textColor;

            return style;
        }

        private string BuildText()
        {
            string text = $"Tick : {SimClock.Tick}\n" +
                          $"Time : {Time.time:F2}\n" +
                          $"Frame: {Time.frameCount}\n" +
                          $"FPS  : {1f / Time.unscaledDeltaTime:F0}\n";

            if (_loop == null || _sampler == null || _loop.Player == null)
            {
                return text + "\n(Inspector 引用没指定全，或 Boot 装配失败)";
            }

            InputFrame f = _loop.LastFrame;

            // 每次按下，要么被消费、要么还在队列里、要么被丢弃——三者互斥且穷尽。
            int accounted = _sampler.ConsumedCount + _sampler.PendingCount + _sampler.DroppedCount;
            bool balanced = accounted == _sampler.PressedCount;

            Vector3 pos = _loop.Player.Position;
            Vector3 vel = _loop.Player.Velocity;

            return text +
                   "\n-- Input --\n" +
                   $"Pressed : {_sampler.PressedCount}\n" +
                   $"Consumed: {_sampler.ConsumedCount}\n" +
                   $"Pending : {_sampler.PendingCount}\n" +
                   $"Dropped : {_sampler.DroppedCount}\n" +
                   $"[{(balanced ? "OK" : "MISMATCH")}] {_sampler.ConsumedCount} + {_sampler.PendingCount} + {_sampler.DroppedCount} = {accounted} / {_sampler.PressedCount}\n" +
                   $"LastAtk : tick {_sampler.LastAttackTick}\n" +
                   $"\n-- Frame #{f.Tick} --\n" +
                   $"Move  : ({f.MoveX,5:F2}, {f.MoveY,5:F2})\n" +
                   $"Look  : ({f.LookX,7:F2}, {f.LookY,7:F2})\n" +
                   $"Attack: {f.Attack}\n" +
                   "\n-- Player --\n" +
                   $"Pos   : ({pos.x,7:F2}, {pos.y,6:F2}, {pos.z,7:F2})\n" +
                   $"Speed : {vel.magnitude,5:F2} m/s   (水平 {new Vector2(vel.x, vel.z).magnitude:F2})\n";
        }
    }
}
