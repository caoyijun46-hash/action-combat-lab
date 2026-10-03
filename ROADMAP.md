# ROADMAP — M1 / M2

约定见 `AGENTS.md`。**每一步验收通过才进下一步。**

状态：`⬜ 未开始` / `🔵 进行中` / `✅ 已验收`

---

## M1 — 骨架流水线

| # | 步骤 | 状态 | 验收标准（可测） |
|---|---|---|---|
| 1 | 时间轴：固定 60Hz tick，单一驱动点 | ✅ | 10 秒内 tick ≈ 600；改 Fixed Timestep 为 0.0333 后 tick ≈ 300；`Time.timeScale = 0.5` 时 tick 变慢 |
| 2 | 输入采集 + 输入缓冲 | ✅ | HUD 上的按键计数与实际按键次数完全一致（狂敲不漏不重） |
| 3 | 相机相对运动学移动 | ✅ | WASD 跟手；转身后前进方向仍与相机一致；松键有减速不是瞬停 |
| 4 | Sim / View 分层 + 渲染插值 | ✅ | `Sim/` 下检索不到任何 UnityEngine 引擎对象（`Vector3`/`Quaternion`/`Mathf` 除外，见铁律 1）；60Hz 模拟下移动无抖动 |

## M2 — 战斗核心

| # | 步骤 | 状态 | 验收标准（可测） |
|---|---|---|---|
| 5 | 状态机（先 switch，后重构） | ✅ | Idle ↔ Move 正确切换，切换瞬间速度连续无突变 |
| 6 | 输入缓冲消费 + 攻击触发 | ✅ | 收招最后 3 tick 内按攻击能接上；快速连按不会一击触发两次 |
| 7 | AttackData（ScriptableObject）三段帧 | ✅ | 改 Inspector 里的前摇/判定/后摇数字，手感立刻变化，无需改代码 |
| 8 | 判定 + Gizmo + 靶子 + 命中记录 | ⬜ | 红盒在判定帧出现、命中变绿；靶子后退掉血；同一招式不重复命中同一目标 |

---

## 下一个块

**M2 Step 8 —— 判定 + Gizmo + 靶子 + 命中记录 —— ⬜ 下一步**

验收标准：红盒在判定帧出现、命中变绿；靶子后退掉血；同一招式不重复命中同一目标。

**已经就位的东西**（Step 7 铺好的路）：
- `PlayerState.AttackActive` 就是「判定帧」——HUD 的 `Player` 行能看到它，
  Step 8 只要在 `State == AttackActive` 时开判定框即可，不用再算时间轴。
- 判定用的**方向**是现成的：`PlayerSim.CameraRelativeDirection` 已经有了；
  但**角色朝向**还没有（块 3a 决定不做）——判定框往哪边开，Step 8 要先定这件事。
- `AttackData` 里已经有 `ActiveTicks`，但**判定框的尺寸/偏移**还没有归属，
  按 7a 的分法应该进 `AttackData`（纯数据），于是又要给已建好的 `.asset` 加字段——
  这次躲不掉了，得连带处理「旧资产新字段拿不到初始值」的问题（见 7b 设计选择 4）。

**几个要定的问题**（开工前讨论）：
1. **角色朝向**要不要现在加？加的话 `PlayerSim` 多一个 `Facing`（yaw 或方向向量），
   由移动输入更新（移动时朝移动方向，站定时保持）。判定框沿朝向前方开。
2. **判定框的检测方式**：铁律 5 要求「主动查询（`Physics.OverlapBox` 等），不用 Trigger 回调」，
   而铁律 6 说物理只做查询、且 `Sim/` 不能碰 `Physics`——查询要经 Boot 层转交。
   这是**第一次真正用到铁律 6 那条转交通道**，接口怎么设计要专门想。
3. **命中记录放哪**：要能判断「同一招式不重复命中同一目标」，所以得记住「这一招打过了谁」。
   这属于模拟状态（要参与回放/联机），放 `Sim/`，和 `PlayerSim` 并列。
4. **靶子**是不是也要是个 Sim 对象（有血量、会被击退），还是先做个最简单的一次性目标。

**7b 已完成（等 Editor 实测验收）**：`Attack` 拆成 `AttackStartup` / `AttackActive` / `AttackRecovery`。
见 `DEVLOG.md` 块 7b。**攻击键是回车 / 鼠标左键 / 手柄 X。**

**7b —— 三段帧：攻击拆成三个状态 —— 代码已完成，等用户在 Editor 实测验收**

`Attack` 拆成 `AttackStartup` / `AttackActive` / `AttackRecovery`，各段用 `AttackData` 里各自的时长。
**顺带把块 6b 加的 `SetState(restart)` 参数删掉了**——接招现在是
`AttackRecovery → AttackStartup` 的普通转移，清零是本来就有的行为。

**验收做法**：进 Play，按回车出招，盯 HUD 的 `Player` 行：
- 状态依次是 `AttackStartup` → `AttackActive` → `AttackRecovery` → `Idle`
- 每个状态的 `t` 各自从 0 数起：前摇 0→7、判定 0→5、后摇 0→**15**
- 在**后摇**的 `t=13/14/15` 那三 tick 按回车 → **当场**跳回 `AttackStartup` 且 `t` 归 0
- 在**判定**段按回车 → **不该**接上（离窗口太远，0.2 秒有效期过了），招式正常走完
- 在 Inspector 里改后摇 16 → 40：三个数字变，后摇的 `t` 跟着数到 39

**两块没做的（有意）**：
- `AttackCancelWindowTicks` 没进 `AttackData`。给已建好的 `.asset` 加字段有「新字段静默变 0」的风险，
  0 的取消窗口 = 每 tick 都能接招。等以后有第二个招、真需要「重招窗口更小」时再搬。
- 没加「后摇时长 ≥ 取消窗口」的校验，同上。

---

**7c —— 三段帧的原始设计记录（已由 7b 实现，留档）**

**用户已定的方案（问题二选 (A)）**：加**三个状态** `AttackStartup` / `AttackActive` / `AttackRecovery`，
状态数 3 → 5。理由：后面要做受击打断，三段里「玩家能做的事」差别会大到需要各自的转移规则。

**这会让转移表明显变复杂**，要注意两件事：
- 现在 `switch (State)` 里 `default` 等价于 Idle 的那个坑，状态从 3 个变 5 个之后更容易踩。
  `DEVLOG.md` 块 6a 残留问题 1 记着它，离线测试里那条「每个枚举成员都被驱动到过」要继续保留。
- 段与段之间的转移是**单向、按时长**的（前摇→判定→后摇→收招），
  但取消窗口和受击打断是**横向**的（后摇中途跳到新状态）。两种边混在一个 `switch` 里会开始难读——
  这是 M6「换成转移表」的第一个真实信号。

**7b 顺带要搬的东西**：`SimConstants.AttackCancelWindowTicks` 移进 `AttackData`
（窗口天然属于后摇段），并在资产上加校验「后摇时长 ≥ 取消窗口」。

**恢复上下文顺序**：本文件 → `AGENTS.md` → `DEVLOG.md`（最近几节最相关）。

---

## 之后（暂不展开）

- M3 受击反应、顿帧、击退、无敌帧
- M4 追击型 AI
- M5 锁定与相机
- M6 连招 / 取消窗口 / 耐力
- M7+ 联机（方向待定：先做 2 人合作 PvE）

---

## 进度日志

- M0：项目约定与环境勘察完成（Unity 6000.5.8f1 / URP / Input System 1.20 / 无 netcode 包）
- Step 1：验收通过。代码 review 修掉 3 个必修项（恒真校验条件、`Tick` 用 float、命名空间大小写）。残留 2 个非阻塞小问题**记录已丢失**（原因见 `DEVLOG.md` 块 0）。
- Step 2 块 2a：`InputBuffer` 完成。离线行为测试通过（5 种帧率 × 10 秒，不漏不重）。
- Step 2 块 2b-1：输入资产 `PlayerActions.inputactions` 落地（从模板裁剪，只含 Move/Look/Attack）。用户已确认可打开。
- Step 2 块 2b-2：`InputSampler` 完成。`dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误。
- Step 2 块 2b-2 收尾：用户已把 `InputSampler` 挂到 `BattleLoop` 那个 GameObject 上，`_actions` 已指向 `PlayerActions.inputactions`（已核对场景文件）。
- Step 2 块 2c：`BattleLoop` 接线 + `DebugHud` 显示计数完成。编译 0 警告 0 错误。
- Step 2 块 2c 补丁：修掉 HUD 鼠标悬停时文字与阴影一起变白的 bug（`GUIStyle` 只设了 `normal` 状态）。
- **Step 2 块 2d：验收全部通过 → Step 2 ✅**。7 项测试（边沿检测 / 数清 20 下 / 停手归零 / 狂敲 10 秒 / 不变式 / Move 通道 / Look 通道）结果全部符合预期。
- Step 3 块 3a：`Sim/PlayerSim.cs` 移动逻辑完成。离线行为测试（引用真 UnityEngine.CoreModule.dll）5 组 16 项全过。
- Step 3 块 3b：`View/PlayerView.cs` + `BattleLoop` 装配完成。依赖图逐文件核对与约定表一致。
- Step 3 块 3c：用户搭好场景（Plane + Player 方块 + 三个引用），接线已读场景文件核对。
- **Step 3 块 3d：验收全部通过 → Step 3 ✅**。四项测试（跟手 / 相机 yaw 0·90·180·270 的方向映射 / 松键滑行 0.55 米 / 斜向不超速）全部符合预期。**观察到轻微抖动——这是预期，正是 Step 4 的主题。**
- Step 4 块 4a：`BattleLoop` 加运行时帧率覆盖字段（不动 `ProjectSettings`）。拉到 100+ 帧后抖动放大到肉眼无法忽视。
- Step 4 块 4b：渲染插值完成（`PlayerSim.PreviousPosition` + `PlayerView` 用 `Time.fixedTime` 算 alpha 做 Lerp）。
- **Step 4 块 4c：验收全部通过 → Step 4 ✅**。四项测试（100+ 帧下平滑 / 60 帧平滑 / 改回 0 正常 / Step 3 相机相对方向无回归）全部符合预期。
- **→ M1 全部完成**（Step 1~4）。下一步进 M2 战斗核心。
- Step 5 块 5a：`Sim/PlayerState.cs` + `PlayerSim` 状态机完成。离线测试 19 项全过，
  其中「与 Step 4 旧逻辑 570 tick 逐位一致」证明状态机没改变任何运动行为。
- Step 5 块 5b/5c：HUD 分两栏 + 历史记录。**发现并修掉一个真 bug**：标签矩形写死 380 高，
  而文字已有 22 行（约 510px），底部 `-- Player --` 整段被裁掉——这才是「HUD 少东西」的原因。
- Step 5 块 5c：修掉两个 bug。① HUD 的速度连续性判据漏了浮点容差，
  实测 72000 组样本里 9.18% 会误报（`MoveTowards` 内部归一化的误差，最大 3.6e-7）；
  ② 版式改成按屏幕自适应（两栏/单栏各找最大可读字号）。
- Step 5 块 5d：HUD 压缩版式，左栏 25 行 → 4 行（组名挪到行首当标签），信息一个没删。
- **Step 5 块 5e：验收通过 → Step 5 ✅**。用户实测确认切换 `|dv|` 为 1.000 / 0.500、超限 0 次、
  状态跟手切换。**注意：期间「HUD 超出屏幕」的真因是用户误拉了 Game 视图的 Scale 滑块**——
  拉离 `Fit` 后 `Screen.width` 报的仍是游戏分辨率，与面板实际可见区域对不上。
  以后遇到「HUD 超出屏幕」，先确认 Scale 是 `Fit`。
- Step 6 块 6a：`Attack` 状态 + `StateTicks` + 自动收招完成。**转移从「每 tick 重算」改成「按状态分派」**
  ——这是用户那句「按状态来做」的落点，也是块 5a 残留问题第 3 条预告的转折点。离线测试 30 项全过。
- Step 6 块 6b：取消窗口 + 预输入完成。两个窗口（收招 3 tick / 有效期 12 tick），
  `SetState` 加 `restart` 参数解决「状态没变但要重新计时」。离线测试 29 项全过。
- **Step 6 块 6c：验收通过 → Step 6 ✅**。用户确认能连招、连按不会一次触发两次、
  按早了会过期不接。
- Step 7 块 7a：攻击数据外置。**先改了 `AGENTS.md` 再改代码**——这一步和铁律 1 直接冲突
  （`ScriptableObject` 持有引擎状态，禁止清单漏列了它）。新增 `Scripts/Data/` 与依赖表的 `Data` 列。
  `AttackData` 是纯 C# struct（用户要求），`Data` 属性交值拷贝，Sim 改不到资产。
  **测试抓到一个真 bug**：初始按键既出招又留在预输入缓冲里，攻击时长一短就凭空多接一招
  （30 tick 时被掩盖）。修法是把预输入的记账挪到转移之后。离线测试 30 项全过。
- Step 7 块 7b：攻击拆成 `AttackStartup` / `AttackActive` / `AttackRecovery` 三个状态。
  **顺带删掉了块 6b 的 `SetState(restart)` 参数**——接招变成普通状态转移，特例自己消失了。
  `TotalTicks` 改成 `Σ max(1, 段)`（填 0 的段实际占 1 tick，如实报）。离线测试 41 项全过。
  测试本身出过「假通过」：断言恒真 + 不变量空跑，两条都补了反空跑断言。
- **Step 7 块 7c：验收通过 → Step 7 ✅**（7a 数据外置 + 7b 三段帧）。
  用户确认三段 `t` 各自从 0 数起、后摇窗口能当场接回前摇、改 Inspector 不用重启 Play。
