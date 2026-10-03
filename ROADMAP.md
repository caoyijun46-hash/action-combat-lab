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
| 7 | AttackData（ScriptableObject）三段帧 | ⬜ | 改 Inspector 里的前摇/判定/后摇数字，手感立刻变化，无需改代码 |
| 8 | 判定 + Gizmo + 靶子 + 命中记录 | ⬜ | 红盒在判定帧出现、命中变绿；靶子后退掉血；同一招式不重复命中同一目标 |

---

## 下一个块

**M2 Step 6 —— 输入缓冲消费 + 攻击触发（已与用户讨论，尚未开工）**

攻击的管道**已经全通**，只差最后一环：

```
InputBuffer 队列                    ✓ 块 2a
InputSampler 每 tick 消费一个事件    ✓ 块 2b
InputFrame.Attack                   ✓
BattleLoop 每 tick 递给 PlayerSim    ✓
PlayerSim 完全无视 frame.Attack      ← Step 6 要补的就是这里
```

建议拆成两块（6 一共动 5 个文件，一次给太多）：

**6a —— Attack 状态 + 计时 + 自动收招 —— 代码已完成，等用户在 Editor 实测验收**

已实现（详见 `DEVLOG.md` 块 6a）：`PlayerState` 加 `Attack`；`SimConstants.AttackDurationTicks = 30`；
`PlayerSim` 加 `StateTicks` + `SetState()` + **转移改成「按状态分派」**；HUD 的 `Player` 行加 `t{StateTicks}`。

**攻击键**：`PlayerActions.inputactions` 里 `Attack` 绑的是**回车**、**鼠标左键**、手柄 X。

**验收做法**：进 Play，按回车出招，看 HUD 的 `Player` 行：
- `State` 变 `Attack`，`t` 从 **0 数到 29**（共 30 个 tick = 0.5 秒），然后自动回 `Idle`
- 出招瞬间 **`speed` 从 6.00 掉到 5.50（只降 0.50），不是掉到 0**——这是「无突变」的证据
- 按住 W 出招：`State` 变 `Attack` 后 `speed` 在 12 tick 内降到 0，之后 **`pos` 完全不动**（W 按着也不动）
- 攻击结束后如果 W 还按着，自动回 `Move` 并重新起步

**已定**：**攻击期间不能移动，按状态分派。** 用户决定「不可以移动，按照状态来做」。

这条决定激活了 `DEVLOG.md` 块 5a 残留问题第 3 条记的那个转折点：
原来 `Tick` 里 `State = direction.sqrMagnitude > 0f ? Move : Idle` 是**每 tick 无条件重算**，
加了 `Attack` 之后这么写会立刻把攻击状态冲掉（玩家在攻击中输入方向 → 下一 tick 变回 `Move`）。
所以转移必须从「算出一个状态」改成「**当前状态决定能去哪**」：

**M2 Step 7 —— AttackData（ScriptableObject）三段帧 —— 尚未开工，有两个设计问题要先定**

验收标准：改 Inspector 里的前摇/判定/后摇数字，手感立刻变化，无需改代码。

**问题一：`ScriptableObject` 放哪？这和铁律 1 冲突了。**

目录约定里写了 `Sim/Data/  # ScriptableObject 定义（AttackData 等）`，
但 `ScriptableObject` 是 `UnityEngine.Object`，**持有引擎状态**——
按铁律 1 自己的判准（「是否持有引擎状态或触发引擎行为」）它应该被禁。

三个选项：
- **(a)** `Sim/Data/AttackData.cs` 直接是 `ScriptableObject`。最省事，但让 `Sim/` 破了铁律 1。
- **(b)** 拆两个类型：`Sim/AttackData.cs` 是**纯 C# 数据**（只有数字），
  另有一个 `ScriptableObject` 包装器负责在 Inspector 里编辑，启动时把数值交给 Sim。
  干净，但要决定包装器住哪个目录。
- **(c)** 同 (b)，但用 `[System.Serializable]` 让 Unity 直接序列化纯 C# 那个类，
  避免字段重复两遍。

**问题二：三段帧是三个状态，还是一个状态加一个「第几段」？**

前摇 / 判定 / 后摇：
- **(A)** 加三个状态 `AttackStartup` / `AttackActive` / `AttackRecovery`，状态数 3 → 5。
- **(B)** 保留一个 `Attack` 状态，用 `StateTicks` + `AttackData` **派生出** `AttackPhase`。

倾向 (B)：三段不是三种独立状态，是同一段动作的内部时间轴。
取消窗口天然属于 recovery 段，判定框属于 active 段。
但要说清 (A) 什么时候才对——三段里如果「玩家能做的事」差别大到需要各自的转移规则，
那就该是三个状态。

**建议拆块**：7a = 数据外置 + Boot 注入（验收「改数字手感变」）；
7b = 三段帧 + 取消窗口挪进 recovery 段。

**Step 7 会回头动的东西**：`SimConstants` 里的 `AttackDurationTicks` / `AttackCancelWindowTicks` /
`AttackInputBufferTicks` 三个常量，以及 `PlayerSim` 里那次 `StateTicks >= 29` 的判断。

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
