namespace ActionGame.Sim
{
    /// <summary>
    /// 只放**结构常量**：模拟的节拍。其余参数全部搬去了两个数据文件（MV3a，2026-10-15 拆家）：
    ///   - 「这个角色是谁」 → <see cref="CharacterData"/>（每个角色一份）；
    ///   - 世界 / 系统 / 相机 → <see cref="GlobalData"/>（全局一份）。
    /// 两者都由 Boot 每 tick 从资产读出、值拷贝传进 Sim（热调 + 确定性，和 AttackData 一条路）。
    ///
    /// 这两个数**不动**：改 <see cref="TicksPerSecond"/> 等于全项目手感重调——所有「N tick」
    /// 的手感描述都要重新想一遍；而且 BattleLoop 有一条对 `Time.fixedDeltaTime` 的校验盯着它。
    /// </summary>
    public static class SimConstants
    {
        /// <summary>模拟频率：60 tick/秒。</summary>
        public const int TicksPerSecond = 60;

        /// <summary>固定步长，秒（= 1/60）。</summary>
        public const float FixedDeltaTime = 1f / TicksPerSecond;
    }
}
