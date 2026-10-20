using ActionGame.Boot;
using ActionGame.Sim;
using UnityEngine;

namespace ActionGame.View
{
    /// <summary>
    /// 把当前 tick **所有活跃的 hitbox** 画成线框。
    ///
    /// 为什么必须画：判定体是看不见的东西。不画出来就没法验证它开在哪个方向、多大、有没有跟着朝向转
    /// ——这几件事全都没法用数字看。块 T1 之后 hitbox 可以逐 tick 配置、数量任意，
    /// 更是只能靠它直观确认「这一帧到底在判什么」。
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

        [Tooltip("有判定但「没打中」时的线框颜色（红）。")]
        [SerializeField] private Color _missColor = new Color(1f, 0.25f, 0.2f, 1f);

        [Tooltip("有判定且「打中了」时的线框颜色（绿）。")]
        [SerializeField] private Color _hitColor = new Color(0.2f, 1f, 0.3f, 1f);

        private void OnDrawGizmos()
        {
            if (_loop == null || _loop.Player == null) return;

            PlayerSim player = _loop.Player;

            // 没有活跃 hitbox 就什么都不画。这本身就是一条信息：
            // 「这一 tick 不该有判定框」是能一眼看出来的。
            if (!player.HasActiveHitbox) return;

            // 用「本次挥砍打中过没有」而不是「此刻正重叠着没有」：
            // 判定窗口可能只有几个 tick，而且去重名单让命中只记一次——
            // 按瞬间重叠来变色的话，绿色会一闪而过，肉眼根本抓不住。
            // 按「这一刀打中过」来变色则会在整个判定窗口里保持绿色，能稳定地看到。
            Gizmos.color = player.HitTargetCount > 0 ? _hitColor : _missColor;

            // 同一 tick 可能有多个 hitbox（T1 起数据里可以配任意多个），逐个画。
            int count = player.ActiveHitboxCount;
            for (int i = 0; i < count; i++)
            {
                if (!player.TryGetActiveHitboxWorld(
                    i, out Vector3 from, out Vector3 to, out float radius))
                {
                    continue;
                }

                DrawWireCapsule(from, to, radius);
            }
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
