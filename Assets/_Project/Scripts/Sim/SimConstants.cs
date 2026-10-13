namespace ActionGame.Sim
{
    public static class SimConstants
    {
        public const int TicksPerSecond = 60;
        public const float FixedDeltaTime = 1f / TicksPerSecond;

        // ---- 玩家移动 ----
        /// <summary>水平最大速度，米/秒。</summary>
        public const float MaxMoveSpeed = 6f;

        /// <summary>加速度，米/秒²。60 表示 0.1 秒从静止加到满速。</summary>
        public const float MoveAcceleration = 60f;

        /// <summary>减速度，米/秒²。30 表示松键后 0.2 秒停下——要能看出「有减速」而不是瞬停。</summary>
        public const float MoveDeceleration = 30f;

        // ---- 攻击 ----
        // 攻击的**时长**已经不在这里了，搬去了 AttackData（前摇/判定/后摇三段），
        // 由 BattleLoop 每 tick 从数据资产（.asset）读出来传给 PlayerSim.Tick。
        // 这样改手感是改 Inspector 里的数字，不用动代码。

        /// <summary>
        /// 收招窗口：攻击的**最后**这么多 tick 内，按下的攻击键能接出下一招。
        /// 3 tick = 0.05 秒。
        ///
        /// 长度来自 <c>AttackData.TotalTicks</c>（不再是常量），所以判断写成
        /// <c>StateTicks &gt;= attackData.TotalTicks - AttackCancelWindowTicks</c>。
        /// 注意这里**没有** `- 1`——`- 1` 那条在「攻击什么时候结束」的判断里（见 PlayerSim）。
        /// 两个判断长得像但差一，写的时候回到这句对一遍。
        ///
        /// 还留在 SimConstants 而不是进 AttackData，是因为 7b 会把「后摇段」做成真正的阶段，
        /// 到时候这个窗口天然属于后摇段，那时再搬更顺。
        /// </summary>
        public const int AttackCancelWindowTicks = 3;

        /// <summary>
        /// 预输入有效期：攻击中按下的攻击键保留这么多 tick，过期作废。
        /// 12 tick = 0.2 秒。
        ///
        /// 没有这个时限的话，在攻击第 1 tick 按一下，会在第 30 tick 突然又出一招——
        /// 玩家早忘了自己按过。有时限才是「预输入」而不是「粘键」。
        /// 它比 <see cref="AttackCancelWindowTicks"/> 长，所以正常时机按的键不会因为过期而丢。
        /// </summary>
        public const int AttackInputBufferTicks = 12;

        // ---- 击退 ----

        /// <summary>
        /// 被击退的物体每秒减速多少，米/秒²。
        ///
        /// 和玩家移动用的是同一套运动学（<c>MoveTowards</c> 逼近零），所以拿它和
        /// <see cref="MoveDeceleration"/> 比一比就有手感概念：这里是 20，比玩家的 30 慢一点，
        /// 所以被推的物体滑得比玩家自己刹车远一点。
        ///
        /// 放在 SimConstants 而不是 AttackData，是因为它描述的是「世界怎么衰减速度」，
        /// 不是「这一招多用力」。哪天要区分「重击减速慢、轻击减速快」再搬过去。
        /// </summary>
        public const float KnockbackDeceleration = 20f;

        // ---- 世界 / 重力 ----

        /// <summary>
        /// 重力加速度，米/秒²。**30 不是 9.8**——游戏里的重力普遍调快，跳跃才利落
        /// （9.8 的话跳 1 米要滞空 0.9 秒，像月球）。这个数是跳跃手感的第一旋钮。
        /// </summary>
        public const float Gravity = 30f;

        /// <summary>
        /// 起跳初速度，米/秒。跳跃高度 = v²/(2g)：8 配 <see cref="Gravity"/> 30 → 约 1 米、滞空约半秒。
        /// 手感两个旋钮：改高度调它，改「利落程度」调重力。
        /// </summary>
        public const float JumpSpeed = 8f;

        /// <summary>
        /// 唯一的地面高度，米。现在世界是一块平地，所以是一个常量——
        /// 以后有平台 / 台阶时，它变成「世界数据」（由 Boot 查好传进来，`Tick` 多收一个参数）。
        /// </summary>
        public const float GroundY = 0f;

        // ---- 受击 ----

        /// <summary>
        /// 受击硬直：被打中后失去控制这么多 tick。12 = 0.2 秒。
        ///
        /// 它是**受击者**的属性，不是攻击者的：攻击方给的是伤害和击退，硬直多长归防守方。
        /// 前提：**必须 ≤ <see cref="InvincibilityTicks"/>**——否则无敌还没结束就又被打中，
        /// 会在硬直中途重入同一个状态（`SetState` 只 +1 不归零），连击会把玩家锁死。
        /// </summary>
        public const int HitstunTicks = 12;

        /// <summary>
        /// 无敌帧：从受击那一刻起算，这么多 tick 内的命中**完全无效**（不掉血、不推、也不进硬直）。
        /// 30 = 0.5 秒，比 <see cref="HitstunTicks"/> 长，所以硬直结束后还有一段安全期。
        /// </summary>
        public const int InvincibilityTicks = 30;

        /// <summary>玩家血量上限。血夹在 0，不做死亡。</summary>
        public const int PlayerMaxHealth = 100;

        // ---- 闪避 ----

        /// <summary>
        /// 闪避的时长，tick。20 = 0.33 秒；这段时间**全程无敌**。
        /// 和 <see cref="InvincibilityTicks"/> 是两个独立的数——闪避的无敌时长就取这个。
        /// </summary>
        public const int DodgeTicks = 20;

        /// <summary>
        /// 闪避冲出去的距离，米。和击退一样**按距离参数化**（v = sqrt(2ad) 反推初速度，
        /// 走击退通道），所以参数即结果：填 3 米，实测滑约 **3.09** 米（离散偏差与距离有关，
        /// 1 米时是 ×1.0528，3 米时是 ×1.0305——别拿一个系数外推）。
        /// </summary>
        public const float DodgeDistance = 3f;

        // ---- 相机 ----
        // 这几个最后应该进数据资产（像 AttackData 那样可以热调），
        // 但第一版先放常量——还没到需要频繁调参的时候，进资产是没被验证过的代码。

        /// <summary>
        /// 鼠标灵敏度，**度/像素**。
        ///
        /// 单位是「度每像素」而不是「度每秒」，因为鼠标给的是**位移**不是速度。
        /// 0.15 的意思是横跨 1000 像素转 150°。
        /// </summary>
        public const float MouseSensitivityDegreesPerPixel = 0.15f;

        /// <summary>
        /// 俯仰角上限，度。
        ///
        /// 逼近 90 但不到 90 —— 正好 90° 时视线和世界上方向平行，<c>Quaternion.Euler</c>
        /// 会退化成万向节死锁，画面会抖。89 是常见取值。
        /// </summary>
        public const float MaxPitchDegrees = 89f;

        /// <summary>
        /// 相机比角色位置高多少，米。
        ///
        /// 角色位置在 `PlayerSim.Position`，是方块**中心**（也就是场景里摆的那个点）。
        /// 0.75 是当前的手感取值——**不是**解剖学上的眼高，是「相机挂在身上哪个高度」，
        /// 调它就是调视角高低，按看着舒服来。
        ///
        /// ⚠️ **判定胶囊不在这个高度上**。胶囊是从 `Position` 出发的水平线段，
        /// 和相机差这 0.75 米。打地平面附近的目标没问题，但以后目标浮空时要回来处理。
        /// </summary>
        public const float CameraHeightOffset = 0.75f;
    }
}
