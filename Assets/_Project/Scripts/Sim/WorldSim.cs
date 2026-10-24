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

        /// <summary>
        /// 本世界 tick 的命中事件（块 H3）。tick 开头清空，受害者在结算命中时写入；
        /// Boot 在世界 tick 之后读取。事件是模拟的**输出**，不回灌进模拟。
        /// </summary>
        private readonly HitEventBuffer _hitEvents = new HitEventBuffer();

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
        /// 本世界 tick 的命中事件（块 H3）。只读给消费者；缓冲每 tick 开头清空，
        /// 顿帧 / 慢放跳过的真实 tick 不跑世界 tick，事件会保留到下一次推进。
        /// </summary>
        public HitEventBuffer HitEvents => _hitEvents;

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
        /// 现在所有角色共用这一份；M4 给敌人自己的招式时再改成每个角色一份。
        /// </param>
        /// <param name="characterData">
        /// 每个角色的基础性能（MV3a），**与 <see cref="Actors"/> 同下标对齐**
        /// （沿用「顺序 = 身份」的约定）。Boot 每 tick 从各角色资产刷新这个数组——
        /// 长度必须和 <see cref="Actors"/> 一致，顺序一致。
        /// </param>
        /// <param name="globalData">全局数据（重力 / 地面 / 系统规则，MV3a）。每个角色都收同一份。</param>
        public void Tick(ActorCommand playerCommand, float dt, AttackData attackData,
            CharacterData[] characterData, GlobalData globalData)
        {
            _hitEvents.Clear();   // 事件是「这一 tick 发生的事」，每 tick 重新攒

            _actors[0].Tick(playerCommand, dt, attackData, characterData[0], globalData,
                _actors, _hitEvents);

            for (int i = 1; i < _actors.Length; i++)
            {
                ActorCommand command = default;   // 现在：什么都不做（沙包没有脑子）
                // M4：这里换成脑子产出的命令 —— _brains[i].Think(_actors[i], _actors, dt)
                _actors[i].Tick(command, dt, attackData, characterData[i], globalData,
                    _actors, _hitEvents);
            }
        }

        /// <summary>
        /// 玩家以外的角色这一 tick **收下**的命中里，最大的一笔**顿帧请求**（tick 数）；
        /// 0 = 没人挨打（或挨打的招填的就是 0 / 负数）。
        ///
        /// 顿帧的两件事在这里合成了一个数：有没有人真的挨了（受害者的记账，
        /// <see cref="PlayerSim.HitStopTicksThisTick"/>——收下才算）+ 该顿多久
        /// （**随命中记录走**：攻击方把自己的招式值塞进了命中，H2，2026-10-15）。
        /// 多笔取最大：同一 tick 两刀砍中两个目标时按顿得最久的那笔。
        ///
        /// 仍然只看玩家以外：顿帧的语义是「玩家的刀砍中了谁」（M3b 定）；玩家自己挨打
        /// （调试按钮 / 以后的敌人）不触发它——「敌人打人顿不顿」是 M4 再议的悬案，
        /// 到时候改这里是删一行过滤的事。
        /// </summary>
        public int MaxOtherActorHitStopTicks()
        {
            int max = 0;
            for (int i = 1; i < _actors.Length; i++)
            {
                int ticks = _actors[i].HitStopTicksThisTick;
                if (ticks > max) { max = ticks; }
            }
            return max;
        }
    }
}
