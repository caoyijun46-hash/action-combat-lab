namespace ActionGame.Sim
{
    /// <summary>
    /// **全局数据**：既不属于某一招、也不属于某一个角色的那些参数（MV3a，2026-10-15 拆家）。
    /// 四组，共用一份（`Data/GlobalDataAsset`）：
    ///   世界动力学 —— 重力和地面管所有角色的弹道 / 落地；冲量衰减管「速度怎么被世界磨掉」；
    ///   系统规则   —— 取消窗口 / 预输入 / 慢放，是玩法规则，不是角色属性；
    ///   削韧系数   —— 打断等级与霸体等级的差值怎么换算成削韧倍率；
    ///   相机与操作 —— 鼠标、俯仰、锁定、输入判定 ε，跟的是「玩家这个**操作者**」而不是「角色」。
    ///
    /// **纯数据**（同 <see cref="CharacterData"/>）：struct 值拷贝，每 tick 由 Boot
    /// 从资产读出来传进 Sim（热调 + 确定性）。
    ///
    /// 本文件**零 `using`**：全是 float / int，连 UnityEngine 都不用。
    /// </summary>
    [System.Serializable]
    public struct GlobalData
    {
        // ---- 世界动力学 ----

        /// <summary>
        /// 重力加速度，米/秒²。**30 不是 9.8**——游戏里的重力普遍调快，跳跃才利落
        /// （9.8 的话跳 1 米要滞空 0.9 秒，像月球）。
        /// 它同时管竖直击退（挑空）的弧线——调它所有人的弹道一起变。
        /// </summary>
        public float Gravity;

        /// <summary>
        /// **冲量通道**的衰减率，米/秒²——受击击退和闪避位移共用这条通道。
        ///
        /// 和自驱移动用的是同一套运动学（`MoveTowards` 逼近零），拿它和角色的
        /// `MoveDeceleration` 比一比就有手感概念：这里是 20，比玩家刹车的 30 慢一点，
        /// 所以被推的物体滑得比玩家自己刹车远一点。
        ///
        /// 它是「世界怎么衰减速度」，不是「这一招多用力」（那是招式文件里的事）。
        /// 哪天要「重角色抗推、轻角色飘」再拆进角色数据。
        /// </summary>
        public float ImpulseDeceleration;

        /// <summary>
        /// 唯一的地面高度，米。现在世界是一块平地，所以是一个数——
        /// 以后有平台 / 台阶时，它变成「世界数据」（由 Boot 查好传进来）。
        /// </summary>
        public float GroundY;

        // ---- 系统规则 ----

        /// <summary>
        /// 收招窗口：攻击的**最后**这么多 tick 内，按下的攻击键能接出下一招。
        /// 3 tick = 0.05 秒。放在全局而不是招式文件，是因为它是「连招规则」——
        /// 将来要「某招收招快 / 慢」再拨给招式。
        /// </summary>
        public int AttackCancelWindowTicks;

        /// <summary>
        /// 预输入有效期：攻击中按下的攻击键保留这么多 tick，过期作废。12 tick = 0.2 秒。
        ///
        /// 没有这个时限的话，在攻击第 1 tick 按一下，会在第 30 tick 突然又出一招——
        /// 玩家早忘了自己按过。有时限才是「预输入」而不是「粘键」。
        /// 它比 <see cref="AttackCancelWindowTicks"/> 长，所以正常时机按的键不会因为过期而丢。
        /// </summary>
        public int AttackInputBufferTicks;

        /// <summary>
        /// 完美闪避的慢放时长，**真实 tick**（不是模拟 tick）。120 = 2 秒——
        /// 这段时间里世界只推进 1 秒（半速）。
        /// </summary>
        public int SlowMotionTicks;

        /// <summary>
        /// 慢放倍率：每 N 个真实 tick 里跳掉 (N-1) 个。2 = 半速。
        /// 机制和顿帧一样是「跳 tick」——**不许用 `Time.timeScale`**（铁律 3）。
        /// 必须是 ≥ 1 的整数（0 会在取模上炸——资产的 OnValidate 盯着）。
        /// </summary>
        public int SlowMotionSkipEvery;

        // ---- 削韧系数（块 P1）----

        /// <summary>
        /// 削韧系数里**等级差每 1 级的倍率变化**：
        /// `系数 = clamp(1 + 它 × (打断等级 − 霸体等级), Min, Max)`。
        /// 0 = 等级差完全不影响削韧；负值无意义（资产的 OnValidate 会警告）。
        /// </summary>
        public float PoiseCoefficientPerLevel;

        /// <summary>削韧系数下限。防止高霸体对低打断完全免疫；0 = 允许免疫。</summary>
        public float PoiseCoefficientMin;

        /// <summary>削韧系数上限。防止低霸体对高打断被瞬间破韧。</summary>
        public float PoiseCoefficientMax;

        // ---- 相机与操作 ----

        /// <summary>
        /// 鼠标灵敏度，**度/像素**。
        ///
        /// 单位是「度每像素」而不是「度每秒」，因为鼠标给的是**位移**不是速度。
        /// 0.15 的意思是横跨 1000 像素转 150°。
        /// </summary>
        public float MouseSensitivityDegreesPerPixel;

        /// <summary>
        /// 俯仰角上限，度。
        ///
        /// 逼近 90 但不到 90 —— 正好 90° 时视线和世界上方向平行，`Quaternion.Euler`
        /// 会退化成万向节死锁，画面会抖。89 是常见取值。
        /// </summary>
        public float MaxPitchDegrees;

        /// <summary>
        /// 视角的**最大旋转速度**，度/秒——yaw 和 pitch **各自**的上限。
        ///
        /// 鼠标甩得再快，视角每 tick 最多转 这个值 × dt，**超出的部分直接丢掉**
        /// （用户拍板：不做「欠账追赶」）。代价如实说：超过 720°/s（每 tick 80 像素）的
        /// 甩视角，鼠标会相对视角「打滑」——要多甩一点才转到位；阈值以内完全无感。
        ///
        /// 它只管鼠标转向；**锁定自动转向的上限另有一条**（<see cref="LockOnTurnMaxSpeedDegreesPerSecond"/>）。
        /// </summary>
        public float CameraMaxTurnSpeedDegreesPerSecond;

        /// <summary>
        /// 视角锁定的**范围**，米：中键按下时只会锁「离眼睛这么近」的敌人；
        /// 锁定之后目标跑出范围**不会自动解除**（再按中键才解）。
        /// </summary>
        public float LockOnRangeMeters;

        /// <summary>
        /// 锁定转向的**速度增益**，1/秒：追赶速度 = |视线夹角误差| × 它，
        /// 再夹在 [<see cref="LockOnTurnMinSpeedDegreesPerSecond"/>,
        /// <see cref="LockOnTurnMaxSpeedDegreesPerSecond"/>] 之间。
        /// 误差大（目标在屏幕边上 / 身后）→ 顶到硬上限追得快；快对齐 → 按比例慢下来。
        /// </summary>
        public float LockOnTurnGainPerSecond;

        /// <summary>
        /// 锁定转向的**速度下限**，度/秒：误差很小时按增益算出的速度太小、
        /// 会在最后几度上「蹭」——给个下限收尾（配合「一步不超过剩余误差」的吸附，
        /// 最后一步精确落位、不抖）。
        /// </summary>
        public float LockOnTurnMinSpeedDegreesPerSecond;

        /// <summary>
        /// 锁定转向的**速度上限**，度/秒：锁定自动转向的封顶速度——
        /// 独立于鼠标那条 <see cref="CameraMaxTurnSpeedDegreesPerSecond"/>。
        /// 「鼠标能甩多快」管响应，「锁定该追多快」管观感，两种手感分开调。
        /// </summary>
        public float LockOnTurnMaxSpeedDegreesPerSecond;

        /// <summary>
        /// 「move 输入方向变了」的判定阈值，度：小于它算没变（吸摇杆抖动）。
        /// 方向或幅度任一超阈值都算「变化」——它是锁定中重新解析移动方向的扳机。
        /// </summary>
        public float MoveInputDirectionEpsilonDegrees;

        /// <summary>
        /// 「move 输入幅度变了」的判定阈值（输入单位，0~1）：
        /// |Δ幅度| 超过它算变化——**幅度变化也会触发重新解析**；半推 → 满推
        /// 这种 0.5 量级的变化会重解，摇杆的细微抖动（0.01 量级）不会。
        /// </summary>
        public float MoveInputMagnitudeEpsilon;
    }
}
