using ActionGame.Boot;
using ActionGame.Sim;
using UnityEngine;

namespace ActionGame.View
{
    /// <summary>
    /// 把判定胶囊画成线框，**只在判定帧显示**。
    ///
    /// 为什么必须画：判定体是看不见的东西。不画出来就没法验证它开在哪个方向、多大、有没有跟着朝向转
    /// ——这几件事全都没法用数字看。Step 8 的验收标准第一条「红盒在判定帧出现」就是它。
    ///
    /// 它读 <see cref="BattleLoop.Player"/>，是 View → Boot 的一条边。
    /// 铁律 1 的依赖表里那条边标的是 `△`（只有调试工具能走），本类就是调试工具。
    ///
    /// 用 Gizmos 而不是 HUD：Gizmos 画在世界空间，跟着物体走，能直接看出「判定框在角色前面还是后面」。
    /// 代价是只在 Scene 视图可见——Play 的时候把 Scene 视图和 Game 视图并排看。
    /// </summary>
    public class HitboxGizmo : MonoBehaviour
    {
        [SerializeField] private BattleLoop _loop;

        [Tooltip("判定帧的线框颜色。Step 8b 会加一个「命中时」的颜色。")]
        [SerializeField] private Color _hitFrameColor = new Color(1f, 0.25f, 0.2f, 1f);

        private void OnDrawGizmos()
        {
            if (_loop == null || _loop.Player == null) return;

            PlayerSim player = _loop.Player;

            // 非判定帧什么都不画。这本身就是一条信息：
            // 「前摇和收招期间不该有判定框」是能一眼看出来的。
            if (!player.IsHitFrame) return;

            Gizmos.color = _hitFrameColor;
            DrawWireCapsule(player.HitCapsuleFrom, player.HitCapsuleTo, player.HitCapsuleRadius);
        }

        /// <summary>
        /// 线框胶囊 = 两端的球 + 四条母线。Unity 没有内置的 DrawWireCapsule，自己拼。
        ///
        /// 注意球的半径就是胶囊半径——胶囊本来就是「一段线段 + 半径」，
        /// 两端的半球和中间的圆柱共用同一个半径。
        /// </summary>
        private static void DrawWireCapsule(Vector3 from, Vector3 to, float radius)
        {
            Vector3 axis = to - from;

            // 起终点重合时胶囊退化成一个球，叉积算不出方向，单独处理
            if (axis.sqrMagnitude < 1e-8f)
            {
                Gizmos.DrawWireSphere(from, radius);
                return;
            }

            Vector3 dir = axis.normalized;

            // 找两个与 dir 垂直的基向量。dir 接近 ±Y 时不能拿 Vector3.up 做叉积——
            // 叉积结果是零向量，normalized 会得到 NaN，线框直接消失。
            Vector3 reference = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f
                ? Vector3.right
                : Vector3.up;

            Vector3 u = Vector3.Cross(dir, reference).normalized;
            Vector3 v = Vector3.Cross(dir, u).normalized;

            Gizmos.DrawWireSphere(from, radius);
            Gizmos.DrawWireSphere(to, radius);

            Gizmos.DrawLine(from + u * radius, to + u * radius);
            Gizmos.DrawLine(from - u * radius, to - u * radius);
            Gizmos.DrawLine(from + v * radius, to + v * radius);
            Gizmos.DrawLine(from - v * radius, to - v * radius);
        }
    }
}
