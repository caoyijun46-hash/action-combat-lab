using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// 一个判定体：一段**本地空间**的胶囊 + 它激活的 tick 窗口。
    ///
    /// 块 T1（2026-10-20）从「一个招式一条胶囊、只在判定段生效」升级而来：
    /// 一招可以有任意多个 hitbox，每个自己决定「哪几个 tick 有判定、摆在哪、多大」。
    ///
    /// ---- 坐标约定 ----
    /// 本地坐标以**角色根（脚底）**为原点：x = 右 / y = 上 / z = 前（yaw = 0 时朝 +Z）。
    /// 和 `AttackData.KnockbackDirection` 的「攻击者本地坐标」是同一套。
    /// 运行时用「身体中心 + 脚底补偿 + 只绕 Y 的 yaw 旋转」换算成世界端点——
    /// 角色不俯仰、不翻滚，所以不需要完整旋转矩阵（也避开 `Quaternion.Euler`，离线可测）。
    ///
    /// ---- tick 窗口 ----
    /// `[StartTick, EndTick]` **含两端**，0 = 起手第一 tick，上限是 `AttackData.TotalTicks - 1`。
    /// 每 tick 所有覆盖当前 tick 的 hitbox 一起参与判定。
    ///
    /// **纯数据**：struct 值拷贝；数组本身住在 <see cref="AttackData"/> 的私有字段里，
    /// Sim 只能通过只读访问器拿到**元素拷贝**，物理上改不到资产。
    /// </summary>
    [System.Serializable]
    public struct HitboxDefinition
    {
        /// <summary>第一个激活的 tick（含）。0 = 起手第一 tick。</summary>
        public int StartTick;

        /// <summary>最后一个激活的 tick（含）。</summary>
        public int EndTick;

        /// <summary>胶囊起点，角色根（脚底）本地坐标：x = 右 / y = 上 / z = 前。</summary>
        public Vector3 LocalFrom;

        /// <summary>胶囊终点，同一套本地坐标。起终点重合 = 一个球。</summary>
        public Vector3 LocalTo;

        /// <summary>胶囊半径，米。</summary>
        public float Radius;

        /// <summary>这个 hitbox 在整招第 <paramref name="tick"/> tick 是否启用（两端都含）。</summary>
        public bool IsActiveAt(int tick)
        {
            return tick >= StartTick && tick <= EndTick;
        }

        /// <summary>
        /// 本地点 → 世界坐标。脚底原点 + 只绕 Y 的 yaw 旋转。
        ///
        /// <paramref name="characterPosition"/> 是角色**身体中心**（<see cref="PlayerSim.Position"/>），
        /// 脚底 = 中心往下 <paramref name="halfHeight"/> 米。
        ///
        /// sin/cos 手算（和 <see cref="PlayerSim.FacingDirection"/> 同一套角度约定：
        /// 偏航 θ 的前方 = `(sinθ, 0, cosθ)`、右方 = `(cosθ, 0, -sinθ)`）——
        /// `Quaternion.Euler` 是 ECall，离线 harness 跑不了。
        /// </summary>
        public static Vector3 LocalPointToWorld(
            Vector3 localPoint, Vector3 characterPosition,
            float halfHeight, float facingYawDegrees)
        {
            float rad = facingYawDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(rad);
            float cos = Mathf.Cos(rad);

            return new Vector3(
                characterPosition.x + localPoint.x * cos + localPoint.z * sin,
                characterPosition.y + (localPoint.y - halfHeight),
                characterPosition.z - localPoint.x * sin + localPoint.z * cos);
        }
    }
}
