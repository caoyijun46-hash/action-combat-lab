namespace ActionGame.Sim
{
    /// <summary>
    /// 世界：**实体表 + 「一 tick 里谁先谁后」**。
    ///
    /// 这一块原本住在 Boot（`BattleLoop.FixedUpdate` 里）——但「有哪些角色、按什么顺序推进」
    /// 是**世界自己的知识**，不是驱动层的。搬下来的直接好处：离线测试可以直接实例化它，
    /// 不用再手工复刻那段循环（复刻漏一条语义就是一轮假通过 / 假失败——已经踩过）。
    ///
    /// **它不拥有**：时钟的快慢（顿帧 / 慢放的计数在 Boot——那是驱动层的时钟决策，
    /// 联机时由服务器同步）、输入、场景、任何表现。它只回答一件事：
    /// 给定玩家这一 tick 的操作意图，世界怎么往前迈一步。
    ///
    /// 判定名单存的是 <see cref="Actors"/> 的**下标**——顺序必须稳定（下标 = 身份）。
    ///
    /// 本文件零 `using`：它只做转发，用不到 UnityEngine 的任何东西。
    /// </summary>
    public class WorldSim
    {
        private readonly PlayerSim[] _actors;   // [0] = 玩家，其余是其他角色

        /// <param name="actors">
        /// 全体角色，<c>[0]</c> 必须是玩家。**数组由这里保管**（建一次、顺序不变），
        /// 内容会随模拟变化。Boot 负责从场景翻译出来、装配好 View，再交进来。
        /// </param>
        public WorldSim(PlayerSim[] actors)
        {
            _actors = actors;
        }

        public PlayerSim Player => _actors[0];

        public PlayerSim[] Actors => _actors;

        /// <summary>
        /// 世界往前迈一步。顺序是**玩家先、其他角色后**——不能反：
        /// 玩家那一 tick 里会做判定、给受害者装填受击，紧接着推进其他角色，
        /// 它们就在**同一 tick** 吃到反应（进硬直、开始被推），打击感不会慢半拍。
        /// 反过来（先推进别人再判定）的话，受击要等下一 tick 才生效。
        /// </summary>
        /// <param name="playerCommand">玩家这一 tick 的操作意图（由 Boot 从输入产出）。</param>
        /// <param name="dt">固定步长，秒。</param>
        /// <param name="attackData">
        /// 攻击数据。**每 tick 现读传进来**（不是构造时存一份）——播放中改 Inspector 立刻生效。
        /// 现在所有角色共用这一份；M4 给敌人自己的资产时再改成每个角色一份。
        /// </param>
        public void Tick(ActorCommand playerCommand, float dt, AttackData attackData)
        {
            _actors[0].Tick(playerCommand, dt, attackData, _actors);

            for (int i = 1; i < _actors.Length; i++)
            {
                ActorCommand command = default;   // 现在：什么都不做（沙包没有脑子）
                // M4：这里换成脑子产出的命令 —— _brains[i].Think(_actors[i], _actors, dt)
                _actors[i].Tick(command, dt, attackData, _actors);
            }
        }

        /// <summary>
        /// 玩家以外的角色里，这一 tick 有没有人**真的**挨了打（= 玩家的刀真落下了）。
        /// 顿帧的触发条件——「真的挨了」的记账在受害者那边（<see cref="PlayerSim.WasHitThisTick"/>），
        /// 这里只是替 Boot 遍历一遍。
        /// </summary>
        public bool AnyOtherActorWasHit()
        {
            for (int i = 1; i < _actors.Length; i++)
            {
                if (_actors[i].WasHitThisTick) { return true; }
            }
            return false;
        }
    }
}
