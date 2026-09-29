# DEVLOG — 实现记录

记录每一块的**过程与理由**。状态见 `ROADMAP.md`，约定见 `AGENTS.md`。
只追加，不删改历史；发现前面写错了，新增一节更正并说明。

---

## 块 0 — 上下文恢复 + 协作模式变更

日期：2026-09-27

### 背景

上一轮会话的上下文丢了，AI 对工程重新做了一次勘察。勘察结论如下，作为后续所有工作的基线。

### 工程现状（勘察结论）

- 引擎 Unity 6000.5.8f1 / URP 17.5.0 / Input System 1.20.0 / C# 语言版本 **9.0**。
- `Assets/_Project/Scripts/` 下 7 个文件：

  | 文件 | 状态 |
  |---|---|
  | `Sim/SimConstants.cs` | ✅ 60 tick/s 常量 |
  | `Sim/SimClock.cs` | ✅ 全局 tick 计数 |
  | `Sim/InputFrame.cs` | ✅ 结构体，5 个字段 |
  | `Boot/BattleLoop.cs` | ✅ Step 1 产物，唯一 `FixedUpdate` |
  | `View/DebugHud.cs` | ✅ OnGUI 显示 Tick/Time/Frame/FPS |
  | `Inputs/InputSampler.cs` | ⚠️ 只有接口骨架，无方法体 |
  | `Inputs/InputBuffer.cs` | ⚠️ 只有接口骨架，第 5 行有残缺的 `private ` |

- **工程当前编译不过**：无方法体的 `BuildFrame(int tick);` 是 CS0501，残缺的 `private ` 是语法错误。
  证据：`Library/ScriptAssemblies/Assembly-CSharp.dll` 时间戳 9-27 20:51，两个骨架文件建于 22:10 之后。
- `Dev.unity` 场景里有 Main Camera / Directional Light / BattleLoop / DebugHud 四个对象。
  `InputSampler` 还没放进场景（Step 2 尚未接线）。
- 场景里两个脚本的 GUID 与 `.meta` 对得上，绑定正常。
- `Time.fixedDeltaTime` 实际值 0.016666694（Unity 存的有理数近似），与 `SimConstants.FixedDeltaTime`
  相差约 3e-8，`BattleLoop` 里 1e-5 的校验能过。
- `Sim/` 目录违禁词扫描干净。

### 发现的问题

1. **Step 1 的 review 记录丢了且不可恢复。** `ROADMAP.md` 里写着「残留 2 个非阻塞小问题见 review 记录」，
   但工程里从来没有过 review 记录文件。根本原因是约定没规定这类内容放哪。→ 本次新增 `DEVLOG.md` 解决。
2. **`Assets/InputSystem_Actions.inputactions` 在 `Assets/` 根目录。** 这是 Unity 模板自带资产，
   与「根目录保持干净、自建内容放 `_Project/`」的约定冲突。→ 待决，见下。
3. **工程没有 git 仓库**，已验收的 Step 1 代码零版本控制保护。
4. `Dev.unity` 里 `m_EditorClassIdentifier` 仍是小写的 `ActionGame.boot.BattleLoop`（代码已是
   `ActionGame.Boot`）。Unity 靠 GUID 绑定，无害，纯显示残留。

### 本次变更

协作模式从「学习者手写 + AI 评审」改为「**AI 分块实现 + 实现后讲解**」，并新增本文件。
理由：用户要求改为 AI 实现，且要求分块推进、每块实现后讲解代码与架构。
`AGENTS.md` 的「协作模式」「实现记录」「验证方式」三节已同步更新。

`验证方式` 一节同时修正了一处**事实错误**：原文写「本机 bash 不可用」，实际 Git Bash 可用。
另外查到 Unity 装在 `C:/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe`，
理论上 Editor 关闭时 AI 可以用 `-batchmode` 自己编译校验，但**尚未实测**，已在该节标注。

### 待决项

| # | 事项 | 倾向 |
|---|---|---|
| 1 | inputactions 资产归属 | 拷一份到 `_Project/Settings/`（目录约定里 Settings/ 就是留给输入配置的），根目录那份保持模板原样不动 |
| 2 | 是否建 git 仓库 | 建议建，`Library/`、`Temp/`、`Logs/`、`obj/` 走 `.gitignore` |
| 3 | `ActionGame.boot` 小写残留 | 可在下次动场景时顺手修 |
| 4 | batchmode 编译是否可用 | Editor 关闭时实测一次 |

### Step 2 的拆块方案

| 块 | 内容 | 依赖 |
|---|---|---|
| 2a | `InputBuffer` — 纯 C# 队列，无 Unity 依赖 | 无 |
| 2b | `InputSampler` — MonoBehaviour，`Update` 里做边沿检测 | 需先定待决项 1 |
| 2c | `BattleLoop` 接线 + `DebugHud` 显示计数 | 2a、2b |
| 2d | 验收：狂敲攻击键，计数不漏不重 | 2c |

---

## 块 2a — `InputBuffer`（纯 C# 输入队列）

日期：2026-09-28
文件：`Assets/_Project/Scripts/Inputs/InputBuffer.cs`（替换掉原来的接口骨架）

### 实现了什么

固定容量 32 的**环形缓冲**，存放「刚按下攻击」的边沿事件（载荷是采集时刻的 tick）。
采样端 `PushAttack`、消费端 `TryConsumeAttack`（每 tick 最多取一个），外加 `PendingCount` /
`DroppedCount` / `Clear`。整个文件无任何 `using`，不依赖 UnityEngine。

### 关键设计选择

1. **环形缓冲，不用 `Queue<int>`。**
   `Queue<int>` 有构造参数 `capacity`，但那是**初始**容量不是**上限**，超了照样扩容——上限仍得手写。
   而接口里有 `DroppedCount`，说明设计意图就是「有上限、会丢」。环形缓冲天然零分配，
   且「满了就丢并计数」能把故障**显性化**：万一采样端出 bug 疯狂 push，无界队列会静默吃内存到崩，
   环形缓冲会明确丢并让你在 HUD 上看到 `Dropped` 不为 0。
2. **容量 32。** 取 2 的幂，让 `% Capacity` 被 JIT 优化成位与。人手按键频率远低于 60Hz，
   正常游玩不可能填满；填满即代表有 bug。
3. **溢出时丢新的，不丢旧的。** 先按的顺序不能被破坏（以后连招顺序靠它）；
   而且「丢新的」会让 `DroppedCount` 直接等于漏掉的次数，与验收标准对得上。
4. **每 tick 最多消费一个。** 这是「狂敲不漏不重」的关键机制：狂敲时积压的事件分摊到
   后续若干 tick 逐个交给模拟层，不会被合并成一次。
5. **`Clear()` 连统计一起归零。** 它的调用点与 `SimClock.Reset()` 配对，语义是「新一局开始」，
   统计残留会让 HUD 数字困惑。若将来需要「只清队列保留统计」，应另加方法，不让 `Clear` 语义含糊。
6. **`Clear()` 不清 `_ticks` 数组。** `_count` 归零后没有元素会被读出，而 `int` 不持引用、不阻碍 GC。

### 验证

`InputBuffer` 是纯 C#，所以没等 Unity —— 在本机用 dotnet 9 离线编译并跑了行为测试
（测试工程在系统临时目录 `%TEMP%\inputbuffer-check\`，不在 Unity 工程内）。
第一轮 2 项 FAIL，排查后确认**都是测试自身写错**，非实现问题（一处是断言无视了容量上限，
一处是断言 `消费 == 按键` 而正确不变式是 `消费 + 积压 == 按键`）。改严格后全绿：

- FIFO 顺序、空队列、Clear、环形回绕（10 轮 × 20 次）全部正确。
- 溢出：push 100 → 取出 32 + 丢弃 68 == 100，队首仍是最早的。
- **帧率模拟（30/60/90/144/240 fps 渲染 vs 60Hz tick，各 10 秒，固定 seed）**：
  消费 + 积压 == 按键，序号 0..N-1 连续无缺号无重复，丢弃恒为 0。
  这是对 Step 2 验收标准「狂敲不漏不重」的直接验证。

静态检查：`Inputs/` 无反向依赖 `View/`；`Sim/` 无违禁词；`FixedUpdate` 全项目仍只有 `BattleLoop` 一处。

### 残留问题

1. **结构性 1 帧输入延迟。** Unity 一帧内 `FixedUpdate` 先于 `Update`，所以 `Update` 采到的按键
   最快也要**下一帧**的 tick 才被消费。这是循环顺序决定的，不是 bug，动作游戏 1~2 帧延迟属正常。
   以后 M6 做取消窗口时会自然碰到。
2. **命名是 attack 专属的。** 现在只有 `PushAttack`/`TryConsumeAttack`。多按键时的演进方向是
   让 `InputBuffer` 只管「**一个**按键的边沿队列」，多个按键 = 多个实例——这样现有接口签名一个字都不用改。
   当前按接口骨架原样实现，不提前做。
3. 离线测试工程在临时目录，Windows 迟早会清掉。**用户决定不留**，已删除。

---

## 块 2b-1 — 输入资产落地

日期：2026-09-28
文件：`Assets/_Project/Settings/PlayerActions.inputactions`（新增，当前未创建）

### 背景：一个被问出来的设计缺口

用户问了一个关键问题：**「攻击按键进队列我理解了，但 Move 和 Look 也是吗？」**

答案是「不是，三者机制不同」，而这个区分此前**没有写进任何约定**——原来的 AGENTS.md 只说了
「边沿检测压进缓冲队列」，没说连续量怎么办。这是约定的一个洞，补掉了（见下）。

### 结论文：状态 / 增量 / 事件

判据是**「这一帧的值如果没被消费掉，下一帧读还能补回来吗？」**

| | 本质 | 能补回来吗 | 传递方式 |
|---|---|---|---|
| Move | 状态：摇杆现在推在哪 | 能，下一帧读到的是最新状态 | 不缓冲，每 tick 读当前值 |
| Look | 增量：这一帧移了多少像素 | 不能，差值会丢 | 在 Update 里**累加**，tick 一次性用掉 |
| Attack | 离散事件：发生了一次 | 不能，且不可合并 | **队列**，每 tick 消费一个 |

关键区分：
- Move 是**幂等**的——读晚了拿到的是那一刻的真实状态，不是丢失的旧状态。时长由 tick 次数体现，
  值只回答「现在朝哪」。所以状态不需要缓冲。
- 鼠标 delta **可加**——2 帧各移 5px 等于一次移 10px，先转后转只是时机差别，总量守恒。所以用**加法**。
  若用队列，帧率掉到 30fps（一帧 2 个 tick）时视角转动会一顿一顿。
- 按键**不可加**——按 2 次不等于 1 次双倍力度，是 2 个独立事件。所以必须**队列**、逐个交出。
  用「一个 bool 标志位」在狂敲时会把 3 次合并成 2 次。

已写入 `AGENTS.md` 的「架构铁律 → 4. 输入」。

### 资产处置（用户选定 A + ①）

从模板 `Assets/InputSystem_Actions.inputactions` **程序化派生**一份裁剪版到 `_Project/Settings/`，
而不是手写 JSON——复用模板自身的结构与 id，保证 schema 正确。

- 保留 `Player` map 的 `Move` / `Look` / `Attack` 三个 action。
- 丢弃 `Interact`/`Crouch`/`Jump`/`Previous`/`Next`/`Sprint` 与整个 `UI` map。
- 只保留 `Keyboard&Mouse` 和 `Gamepad` 两个 control scheme，丢弃 `Touch`/`Joystick`/`XR` 及其全部绑定。
- 顺带统一了 `groups` 字段格式（模板自身不一致：`;Gamepad` 与 `Keyboard&Mouse` 混用），
  并清掉了 `Look/<Pointer>/delta` 上残留的 `Touch` 引用。
- 41005 → 13874 字节。模板原件未改动（仍是原大小与原始时间戳），且不被任何东西引用。

**踩到的坑**：过滤绑定时必须把 **composite 头部 + 它的全部 `isPartOfComposite` 部件当成一整组**
保留或丢弃。模板里 WASD 是个 `path: "Dpad"`、`groups: ""` 的复合头，如果按 groups 过滤单独丢掉头部
而留下部件，文件就坏了。脚本里做了整组处理。

### 待验证

生成的文件 JSON 合法性已验，但**尚未在 Unity 里打开过**。需要用户确认 Input Actions 窗口能正常打开。
→ 用户已确认能打开。✅

---

## 块 2b-2 — `InputSampler`

日期：2026-09-28
文件：`Assets/_Project/Scripts/Inputs/InputSampler.cs`（替换接口骨架，18 → 137 行）

### 实现了什么

`MonoBehaviour`，`Update` 里采集输入，`BuildFrame(int tick)` 每 tick 打包成一个 `InputFrame`。
三种输入按块 2b-1 定下的规则各自实现：

| 输入 | Update 里 | BuildFrame 里 |
|---|---|---|
| Move | `_moveX = move.x` 覆盖 | 直接读 |
| Look | `_lookAccumX += look.x` 累加 | 读出后**清零** |
| Attack | `_buffer.PushAttack(...)` 入队 | `TryConsumeAttack` 出队一个 |

### 关键设计选择

1. **对接口骨架做了一处主动修改**：`PressedCount` / `ConsumedCount` / `LastAttackTick` 三个属性
   从 `{ get; }` 改成 `{ get; private set; }`。C# 的 get-only 自动属性只能在构造函数里赋值，
   而它们要在 `Update`/`BuildFrame` 里累加，必须可写。`PendingCount` / `DroppedCount` 则保持只读，
   直接透传给内部的 `InputBuffer`——它们本来就是队列自己的状态，不该在两层各存一份。
2. **`Awake` 里解析并缓存 `InputAction` 引用**，不在 `Update` 里每帧 `FindAction`。
   `FindAction(name, true)` 用 `throwIfNotFound: true`——action 名字写错要立刻炸，别留到运行时 NRE。
   同时做了 `_actions` 未指派的检查，报错信息直接写明「把 PlayerActions.inputactions 拖上去」。
3. **`OnEnable`/`OnDisable` 只启停 `Player` map**，不是整个 asset。
   为以后的暂停菜单留路：`Player.Disable()` + `UI.Enable()`，避免两套输入同时生效。
4. **`Look` 取出后必须清零**。这是容易漏的一步——如果不清零，下个 tick 会以同样的角度再转一次，
   视角会持续自转。测试时这条最值得盯。
5. **没有加 `ResetState()`**。虽然 `InputBuffer.Clear()` 已经写好、也标注了「与 `SimClock.Reset()` 配对」，
   但配对动作发生在 `BattleLoop` 里，属于块 2c。按「一次一块」的纪律留到那时再加，避免这块超出边界。
6. **一条可测的不变式**：`PressedCount == ConsumedCount + PendingCount + DroppedCount`。
   每次按下要么被消费、要么还在队列里、要么被丢弃，三者互斥且穷尽。2d 验收时可以直接验算。

### 验证

**重大发现：编译校验可以自己做，不需要关 Editor、也不需要用户当编译器。**

跑 `dotnet build Assembly-CSharp.csproj -v quiet --nologo` ——用的是 Unity 自己生成的 csproj，
程序集引用、预处理宏、`LangVersion 9.0` 都与 Unity 一致。结果：**0 警告 0 错误**，耗时 5.4 秒。
Editor 开着也能跑。已写进 `AGENTS.md` 的「验证方式」，替代了原先那条未实测的 batchmode 方案
（batchmode 留作备用）。

注意一个危险点：这个 csproj 只编译 `<Compile Include>` 里列出的文件。**新加的 .cs 如果没进这个列表，
会静默漏编译**——比编译报错更危险。新增文件后要先切回 Unity 触发 csproj 重新生成。

静态检查全过：`Sim/` 无违禁词；全项目 `FixedUpdate` 仍只有 `BattleLoop` 一处；
`Inputs/` 依赖方向正确（`using ActionGame.Sim` 允许，无 `ActionGame.View`/`Boot` 反向依赖）。

### 残留问题

1. **还没挂进场景。** `InputSampler` 需要用户在 Unity 里操作：加到 `Dev.unity` 的某个 GameObject 上，
   并把 `PlayerActions.inputactions` 拖到 `_actions` 字段。这步做完才能进 2c。
2. **`Look` 累加值没有上限。** 如果长时间没有 tick（游戏卡住几秒），`_lookAccum` 会累积成一个很大的值，
   恢复时视角猛转一下。桌面端正常游玩碰不到，但这是真实存在的边界。真要处理可以在 `BuildFrame` 里
   对 `Look` 做 clamp，或按帧数归一化。现在不做，记下来。
3. **`SimClock.Tick` 在 `Update` 里读到的是「最近一次已完成的 tick」**，所以 `LastAttackTick` 的语义是
   「这次按下发生在 tick N 之后」。用它算延迟时要知道这个偏移。
4. 运行时行为（按键计数、HUD 数字）尚未验证，属于 2d。

---

## 块 2c — `BattleLoop` 接线 + `DebugHud` 显示计数

日期：2026-09-28
文件：`Boot/BattleLoop.cs`（改）、`Inputs/InputSampler.cs`（加 `ResetState`）、`View/DebugHud.cs`（改）

### 实现了什么

- `BattleLoop` 持 `[SerializeField] InputSampler`，`FixedUpdate` 里 `Advance()` 后调 `BuildFrame(SimClock.Tick)`，
  结果存进 `LastFrame` 属性（以后交给 Sim，现在只供 HUD 显示）。
- `BattleLoop.Awake` 里补上 `_inputSampler.ResetState()`，与 `SimClock.Reset()` 配对。
- 补上 `InputSampler.ResetState()`——块 2b-2 里故意留给这块的。
- `DebugHud` 显示：Tick/Time/Frame/FPS + 输入四计数 + 不变式检查 + LastAttackTick + 当前帧的 Move/Look/Attack。

### 关键设计选择

1. **`Advance()` 放在 `BuildFrame()` 之前**，而不是之后。这样 `frame.Tick` 就是「正在模拟的这一 tick」，
   且采集端打的时间戳与消费时的 tick 之差最小为 1，`LastAttackTick` 才是个有意义的延迟指标。
   如果反过来，第一次 tick 是 0，且延迟会算成 0，反直觉。
2. **`ResetState()` 不清 `_moveX/_moveY`**：它们每个 `Update` 都被覆盖，没有残留状态可言。
   只清真正有「余额」语义的东西——队列、统计、Look 累加值。
3. **`DebugHud` 用两个序列化引用**（`BattleLoop` + `InputSampler`）而不是让 `BattleLoop` 转发。
   转发等于让 Boot 层为了 HUD 再导出一次 Inputs 的东西，多一层间接没有收益。
   代价是引入 `View/ → Boot/` 依赖，**这是一处已知的妥协**（见残留问题 1）。
4. **顺手修了 `DebugHud` 的每帧分配**：原实现在 `OnGUI` 里 `new` 两个 `GUIStyle`，而 `OnGUI`
   每帧跑多次，等于持续产生垃圾。改成 `EnsureStyles()` 懒初始化一次。不在原计划内，但在要改的文件里
   且只有几行，一并处理。
5. **新增依赖方向表**到 `AGENTS.md` 的「架构铁律 → 1」。原来只规定了 `Inputs/ → Sim/` 一条边，
   现在实际有 6 条，不写下来迟早漂移。表里对 `View → {Inputs, Boot}` 标注为「仅限调试工具」。

### 验证

`dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**，3.4 秒。

静态检查：`Sim/` 无 `using`、无违禁词；全项目 `FixedUpdate` 仍只有 `BattleLoop` 一处（第 38 行）；
依赖方向与新增的依赖表一致（`Sim/` 零 `using`，`Inputs/ → Sim`，`Boot/ → Inputs,Sim`，`View/ → Boot,Inputs,Sim`）。

### 残留问题

1. **`View/ → Boot/` 这处依赖是妥协。** `DebugHud` 读 `BattleLoop.LastFrame` 才需要它。
   更好的做法是让 `BattleLoop` 通过一个纯数据结构把「要显示的东西」交给 HUD，或者干脆让 HUD
   自己持有 `InputFrame` 的只读快照。对一个调试工具不值得现在做，但依赖表里已经标成 `△` 了，
   以后真正的表现层不能援引这个先例。
2. **场景里还没拖引用。** `BattleLoop._inputSampler` 和 `DebugHud` 的 `_loop`/`_sampler` 三个字段
   需要在 Unity 里指定。指定完才能跑。
3. **`PressedCount` 的计数与 `InputBuffer` 的 drop 计数是两处独立累加**，靠不变式
   `Pressed == Consumed + Pending + Dropped` 保证一致。HUD 上会实时显示这个等式，对不上会打 `MISMATCH`。
   这正是 2d 要盯的。
4. 运行时行为（真按键、真数字）尚未验证，属于 2d。

### 补记：HUD 悬停变白的 bug

用户实测发现：鼠标移到 HUD 矩形范围内时，文字**和阴影**一起变成白色。

**根因**：`GUIStyle` 是状态机，有 `normal` / `hover` / `active` / `focused` 四组状态，各自带独立的
`textColor`。上一版只设了 `_style.normal.textColor` 和 `_shadow.normal.textColor`，其余三组
保留了从 `GUI.skin.label` 拷来的默认值（运行时默认 skin 的 label hover 色是白色）。
`GUI.Label` 传入了 `Rect(10,10,560,380)` 的大矩形，鼠标一进去该控件就被判定为 hover，
改用 `hover.textColor`——于是阴影从黑变白，两层叠成一片白。

**修法**：加 `MakeStyle(Color)` 辅助方法，四组状态全设成同一颜色。这个 HUD 不需要悬停反馈。

**教训**：`GUIStyle` 只设 `normal` 是常见坑。凡是自己 new 出来的 `GUIStyle`，
只要控件矩形可能被鼠标覆盖，就得把四组状态一起设。

（诊断依据：`normal` 明明设过了，阴影却会变色，说明一定是某个非 normal 状态被激活；
而唯一随鼠标位置变化的状态就是 `hover`。默认 skin 的具体 hover 色值是反推的，未实测。）

用户复测通过：鼠标在 HUD 区域移动时阴影保持黑色。✅

---

## 块 2d — 验收（Step 2 收尾）

日期：2026-09-28

### 验收测试表与结果

| # | 操作 | 预期 | 结果 |
|---|---|---|---|
| A | 按住攻击键 3 秒不放 | `Pressed` 只涨 1 | ✅ |
| B | 有节奏敲 20 下 | `Pressed` = 20 | ✅ |
| C | 敲完停手等 1 秒 | `Pending` = 0，`Consumed` = 20，`Dropped` = 0 | ✅ |
| D | 最快速度狂敲 10 秒 | 停手后 `Pending` 归 0，`Dropped` = 0 | ✅ |
| E | 全程 | 不变式恒为 `[OK]` | ✅ |
| F | 按 WASD 再松开 | `Move` 跟随，松手回 (0,0) | ✅ |
| G | 快速甩鼠标再停住 | `Look` 出现非零值，停住回 0 | ✅ |

**用户报告：7 项全部符合预期。**

覆盖到的：边沿检测（非 `IsPressed`，也顺带排除 Windows 按键自动重复）、采样端不漏、
消费端不重不多、队列在压力下不丢、两处独立计数器一致、Move 状态通道、Look 增量通道。

### 已知的测试边界

B 项用手敲，最快也就每秒 ~10 下，覆盖不到「按下与松开都落在同一渲染帧之间」那种情况
（144fps 下是约 7ms 的窗口）——那是唯一会让 `WasPressedThisFrame()` 漏掉真实按键的场景。
该侧逻辑已在块 2a 的离线测试里压过（5 种帧率 × 10 秒，唯一序号载荷，验证无缺号无重复）。
想要更硬的证据可以用脚本向 Unity 窗口发送精确次数的按键，暂未做。

### Step 2 收尾状态

代码文件（`Assets/_Project/Scripts/`）：

| 文件 | 行数 | 说明 |
|---|---|---|
| `Sim/SimConstants.cs` | 8 | 60 tick/s |
| `Sim/SimClock.cs` | 16 | 全局 tick |
| `Sim/InputFrame.cs` | 11 | 输入帧结构体 |
| `Inputs/InputBuffer.cs` | 78 | 攻击边沿队列（环形缓冲） |
| `Inputs/InputSampler.cs` | 152 | 三种机制采样 + `BuildFrame` |
| `Boot/BattleLoop.cs` | 44 | 唯一驱动点 |
| `View/DebugHud.cs` | 96 | 调试显示 |

资产：`Assets/_Project/Settings/PlayerActions.inputactions`

**Step 2 ✅ 完成。下一步 Step 3（相机相对运动学移动）。**

---

## 块 3a — `Sim/` 移动逻辑

日期：2026-09-28
文件：`Sim/SimConstants.cs`（加常量）、`Sim/PlayerSim.cs`（新增）

### 先决决策（两个，用户拍板）

**1. Sim 层允许用 `Vector3` / `Quaternion` / `Mathf`。**
这是铁律 1 的一个洞——禁止列表里没有它们，但也没有任何允许列表。定的判准是：
**「是否持有引擎状态或触发引擎行为」，不是「是否住在 `UnityEngine` 命名空间里」**。
这三个是无状态的数学值类型，放行。代价已写进约定：`Sim/` 因此要引用 `UnityEngine.dll`，
**服务器只能是 Unity headless build**。真要换独立服务，`System.Numerics.Vector3` 是现成替代品。

**2. Step 3 不做角色朝向。** 只做位置移动，方块不转。
理由：Roadmap 的验收标准没提朝向，而「转向有多快」是独立的手感问题，适合单独一块。

### 实现了什么

- `SimConstants` 加三个常量：`MaxMoveSpeed = 6`、`MoveAcceleration = 60`、`MoveDeceleration = 30`
  （60 m/s² 表示 0.1 秒到满速；30 m/s² 表示松键后 0.2 秒停下）。
- `Sim/PlayerSim.cs`：持有 `Position` / `Velocity`，`Tick(InputFrame, cameraYaw, dt)` 推进一个 tick。
  另暴露静态纯函数 `CameraRelativeDirection(moveX, moveY, cameraYaw)`。

### 关键设计选择

1. **相机 yaw 作为参数传进 `Tick`，不在 Sim 里存。** Sim 不碰 `Camera`（铁律 1），
   由 Boot 层从相机取出 yaw 传进来。签名里显式写出这个依赖，比藏一个字段好。
2. **用 `Vector3.MoveTowards` 而不是 `Lerp`。** `Lerp` 是指数衰减，永远逼近但到不了目标值，
   速度曲线也说不清；`MoveTowards` 是「以固定速率靠近」，加速度才是字面意义的加速度。
3. **方向只压长度、不归一化**（`saturate` 而非 `normalize`）：
   - 键盘斜按 W+D 得到长度 √2，必须压到 1，否则斜着走比直着走快。
   - 摇杆推一半得到长度 0.5，**必须保留**——那是「慢走」的语义。
   归一化会把第二种情况也拉满，丢掉模拟量输入。
4. **显式欧拉：先更新速度，再积分位置。** 这带来一个可观测的后果，见下。

### 验证

**编译**：`dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误。

**离线行为测试**：这次 `PlayerSim` 依赖 `UnityEngine.Vector3` / `Mathf`，所以测试工程
**引用了真的 `UnityEngine.CoreModule.dll`**（不是自己写的替身），跑在临时目录里。
5 组共 16 项，全部通过：

- **相机相对方向映射**：yaw = 0 / 90 / 180 / 270 下按 W、yaw = 0 / 90 下按 D、yaw = 45 下按 S，
  七个方向全部与手算一致。这是 Roadmap 验收标准第二条「转身后前进方向仍与相机一致」的离线验证。
- **加速曲线**：第 5 tick 速度 5.0，第 6 tick 恰好 6.0，之后 30 tick 稳定限速在 6.0（不超速）。
- **减速曲线**：满速松键后第 1 tick 仍有 5.5 m/s（**证明不是瞬停**），第 12 tick 完全停下。
  这是验收标准第三条的离线验证。
- **斜向不超速**：W+D 满速仍是 6.0；摇杆半推方向长度保留 0.5。
- **一秒位移**：按住 W 一秒前进 **5.75 米**，横向与垂直方向零漂移。

**一个新发现**：我按连续数学预估位移是 `½·a·t² + v·(1-t) = 0.3 + 5.4 = 5.7` 米，
实得 **5.75 米**。原因就是设计选择 4 的显式欧拉——每 tick 先更新速度再用**新速度**积分位置，
等于是拿「加速后的速度」去乘这一整帧。离散积分必然与连续解析解有偏差。
0.05 米/秒的误差对游戏无影响，但这个偏差是真实存在的，记录下来。

静态检查：`Sim/` 无违禁类型（唯一的命中在注释里和 `CameraRelativeDirection` 这个合法方法名里）；
`FixedUpdate` 全项目仍只有 `BattleLoop` 一处。

### 顺带修的约定问题

- **静态检查命令有误报**：原来的 grep 搜 `Camera` 会命中 `CameraRelativeDirection` 这个方法名，
  搜 `Transform` 会命中注释里的说明文字。一个每次都误报的检查最后会训练人忽略它，所以改成
  带 `\b` 词边界 + 过滤 `///` 注释行的版本，写回约定。
- **补上「新增 .cs 后要手动加进 csproj」的提醒**。本块新建 `PlayerSim.cs` 时正好踩到：
  `dotnet build` 会静默漏编译它。已手动补进 `Assembly-CSharp.csproj`（该文件由 Unity 生成，
  下次 Unity 重新生成时会带上，届时手动的这行自然被覆盖，无副作用）。

### 残留问题

1. **场景里还没有地面和角色方块。** 现在 `PlayerSim` 没被任何东西实例化，`BattleLoop` 也还没接它。
   属于块 3c。
2. **`PlayerSim` 的位置来源是构造参数**，`BattleLoop` 还没决定出生点在哪。属于 3c。
3. 显式欧拉的位移偏差（见上），暂不处理。
4. 运行时手感（跟手程度、减速是否合适）尚未验证，属于 3d。数值都在 `SimConstants` 里，好调。

---

## 块 3b — `PlayerView` + `BattleLoop` 装配

日期：2026-09-28
文件：`View/PlayerView.cs`（新增）、`Boot/BattleLoop.cs`（改）、`View/DebugHud.cs`（加 Player 区）

### 关于分块的说明

原计划 3b 只写 `PlayerView`、3c 才做装配。实际把两者合并了——`PlayerView` 不被接线就是一段
无法验证的死代码，单独成块没有意义。真实的分界是「Sim 位置流到表现层」这件事本身，
而它必须包含装配才完整。3c 因此收窄为「场景搭建 + 让整条链路跑起来」。

### 实现了什么

- `View/PlayerView.cs`：`Bind(PlayerSim)` 接收 Sim 对象，`LateUpdate` 里把 `Position` 写给 `Transform`。
- `Boot/BattleLoop.cs`：新增 `_playerView` / `_camera` 两个序列化引用；`Awake` 里创建
  `PlayerSim` 并 `Bind` 给 View；`FixedUpdate` 里推进 `Player.Tick(...)`。
- `View/DebugHud.cs`：新增 `-- Player --` 区，显示 `Pos` 与 `Speed`。
  **加这个是为了让 3d 的「松键有减速不是瞬停」可测量**——没有数字就只能靠眼睛看，而 6→0 m/s
  的过渡肉眼分辨不出是"0.2 秒减速"还是"瞬停"。

### 关键设计选择

1. **View 通过 `Bind()` 拿 Sim 对象，而不是直接引用 `BattleLoop`。**
   这是本块最重要的一处。如果 `PlayerView` 写成 `[SerializeField] BattleLoop _loop` 然后读
   `_loop.Player`，就坐实了 `View → Boot` 这条依赖——而它在依赖表里只是「调试工具例外」。
   改成 Boot 主动把 Sim 交进来，`PlayerView` 就**只依赖 Sim**，依赖表里那条真实表现层的规则得以守住。
   验证：依赖图显示 `View/PlayerView.cs -> Sim`，没有 Boot。
2. **出生点 = 方块在场景里摆的位置。** `BattleLoop.Awake` 读 `_playerView.transform.position`
   作为 `PlayerSim` 的初始位置。这样"想让它从哪出生就把方块摆哪"，不需要在 Inspector 里再填一遍坐标。
   之后 Transform 就不再具有权威性——它每个 `LateUpdate` 都会被 Sim 的位置覆盖。
3. **`LateUpdate` 而不是 `Update` 写 Transform。** 表现层应用状态放在 `LateUpdate` 是惯例
   （ULate 在所有 `Update` 之后跑），以后相机跟随也会放这儿，顺序天然正确。
4. **`Player` 做成公开属性。** 本来只有 `PlayerView` 用得到，可以是 private；但 HUD 要显示
   `Pos`/`Speed` 就得读它，所以公开。不算投机性 API——有真实消费者。
5. **`ValidateRefs()` 抽成单独方法。** 三个序列化引用逐个检查并报出**具体是哪个**没指定，
   而不是笼统一个错误。避免"没反应"时还要猜。

### 本块故意不做的事

**`PlayerView` 是朴素直写——每次 `LateUpdate` 把 Sim 当前位置原样赋给 `Transform`。**

模拟跑 60Hz、渲染可能跑 144Hz，所以看起来会抖。**这个抖动是 Step 4 的主题**（渲染插值）。
本块刻意保持朴素版本，好让 Step 4 的效果有对照。

### 验证

- **编译**：`dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误。
- **静态检查**：`Sim/` 无违禁类型；`FixedUpdate` 全项目仍只有 `BattleLoop` 一处（第 59 行）；
  `Inputs/` 无反向依赖。
- **依赖图逐文件核对**，与约定里的表完全一致：
  ```
  Boot/BattleLoop.cs      -> Inputs Sim View
  Inputs/InputBuffer.cs   -> (无)
  Inputs/InputSampler.cs  -> Sim
  Sim/*.cs                -> (无)
  View/DebugHud.cs        -> Boot Inputs Sim      ← 表中标 △ 的调试工具例外
  View/PlayerView.cs      -> Sim                  ← 真实表现层，只依赖 Sim
  ```
- **csproj 完整性核对**：写了个脚本比对 csproj 的 `<Compile Include>` 列表与磁盘上的 `.cs`，
  9 个文件全部对得上、无漏无余。这一步是新增的——因为上次踩过「新文件静默漏编译」的坑，
  之后每次加文件都要核一遍。

### 残留问题

1. **场景里还没有地面和角色方块**，`BattleLoop` 的三个引用也还没拖。属于 3c。
2. **没有重力、没有碰撞。** `PlayerSim` 只积分 x/z，`Position.y` 永远等于出生点的 y。
   角色会直接走出地面之外继续走。Step 3 不需要这些，属于 M3 之后的事。
3. **没有相机跟随。** 走出视野是正常的，相机属于 Step 5。
4. **朴素直写的抖动**（见上），属于 Step 4。

---

## 块 3c / 3d — 场景搭建与验收

日期：2026-09-29

### 3c 场景（用户操作，AI 读场景文件核对）

场景对象：Main Camera、Directional Light、**Player**（Cube）、BattleLoop、DebugHud、**Plane**（Scale 5,1,5）。
`Player` 位置 (0, 0.5, 0)，Main Camera 位置 (0, 12, -12)、旋转 (40, 0, 0)。

AI 逐个 fileID 核对了 `BattleLoop` 的三个序列化引用与 `DebugHud` 的两个引用，
全部指向正确组件（含 `_camera` 确实指向 Main Camera 的 Transform）。**读场景文件核对比让用户截图可靠。**

### 3d 验收测试表与结果

| # | 操作 | 预期 | 结果 |
|---|---|---|---|
| A | 按住/松开 W | 约 0.1 秒到 6.00 m/s，松手立即开始降 | ✅ |
| B | 相机 Rotation Y 改 0 / 90 / 180 / 270，各按 W 半秒 | 位移方向依次为 +Z / +X / -Z / -X | ✅ |
| C | 满速松手，量滑行位移 | 约 0.55 米（半个多方块身位） | ✅ |
| D | 同时按 W + D | `Speed` 仍是 6.00，不是 8.49 | ✅ |
| E | 观察抖动 | 有轻微抖动，肉眼不易察觉 | 预期内 |

**C 的判据是算出来的**：减速 30 m/s² 从 6 m/s 到 0，连续解滑行 `v²/2a = 0.6` 米；
离散逐 tick 求和为 `Σ(5.5+5.0+…+0.5+0.0)/60 = 33/60 = 0.55` 米。方块宽 1 米，所以"滑了半个多身位"是可量化的。

**E 的为什么**：`PlayerView` 是朴素直写，模拟跑 60Hz。渲染帧率与固定步长不同相位，
某些帧拿到 0 个 tick、某些帧拿到 2 个，位置推进不均匀 → 轻微抖动。
用户显示器大概率是 60Hz，所以 60 帧/秒 vs 60 tick/秒 的相位差看不出多少。
**帧率越高抖动越明显。** 这正是 Step 4 渲染插值要解决的。

### 验收通过的副作用

**Step 3 ✅。场景现在是一条完整可跑的链路**：Input System → InputSampler → InputFrame →
PlayerSim.Tick → PlayerSim.Position → PlayerView → Transform。

### 残留问题

1. 抖动未修（Step 4）。
2. 无重力/碰撞/相机跟随（预期）。
3. **Step 4 的验证需要一个高帧率环境**，否则插值前后的差别很微妙。见 Step 4 的讨论。

---

## 块 4a — 运行时帧率覆盖

日期：2026-09-29
文件：`Boot/BattleLoop.cs`（改）

### 背景

Step 4 要修的是「模拟 60Hz、渲染帧率不同相位导致的抖动」。但用户显示器大概率 60Hz，
渲染 60fps 对上模拟 60Hz 相位差很小，**插值前后的差别会微妙到无法作为验收依据**。
所以先加一个开关把帧率拉离 60Hz，把问题放大到肉眼无法忽视，再做插值——这样才有干脆的 before/after。

用户选定「代码里加运行时开关」而不是改 `ProjectSettings`（后者是红线，且要退出 Play 才能改）。

### 实现了什么

`BattleLoop` 上加 `[SerializeField] int _targetFrameRate`（0 = 不干预）。

### 关键设计选择

1. **必须先关 VSync 再设 targetFrameRate。** Unity 的坑：垂直同步开着时多数平台会**直接忽略**
   `Application.targetFrameRate`。顺序反了就没效果。
2. **在 `OnValidate` 里重新应用，实现"Play 中改立即生效"。** Unity 在 Inspector 序列化字段变动时
   会调 `OnValidate`——**Play 模式下也会**。用 `Application.isPlaying` 挡掉编辑模式下的调用。
   这样可以在跑着的当中 300 → 60 → 0 来回切，立刻看差别，不用重启 Play。
3. **记住原始设置，让 `0` 是真的"关掉"。** 首次调用时存下 `vSyncCount` 与 `targetFrameRate`，
   改回 0 时恢复它们。没有这段的话，`0` 是个骗人的开关——界面显示"未启用"，实际 VSync 已被永久关掉。

### 验证

编译 0 警告 0 错误。用户实测：抖动在 100+ 帧下变得非常明显 —— **before 状态确认**。

**一个未解的小疑问**：目标设 300 只跑到 100 多帧。怀疑是 Editor + URP 开销，或 `DebugHud.OnGUI`
每帧多次拼大字符串。给出了诊断方法（禁用 DebugHud 看 FPS 是否跳升），但**没有去修**——
100+ 帧已远超 60Hz 模拟，足以暴露问题，不值得为此做猜测性优化。

---

## 块 4b — 渲染插值

日期：2026-09-29
文件：`Sim/PlayerSim.cs`（加 `PreviousPosition`）、`View/PlayerView.cs`（换插值实现）

### 实现了什么

- `PlayerSim` 加 `public Vector3 PreviousPosition`，在 `Tick` 第一行记下旧位置。
- `PlayerView.LateUpdate` 改成按 alpha 在两个位置间 Lerp。

### 关键设计选择

1. **`PreviousPosition` 放在 Sim，而不是让 View 自己缓存。**
   考虑过的替代方案：给 `PlayerView` 加一个每 tick 被通知的推送接口，自己维护两个快照。
   否掉的理由——那样 `PlayerView` 的接线要变（从 `Bind(PlayerSim)` 变成推送式），多不少活动部件；
   而放在 Sim 里，**`PlayerView` 的接线一个字都不用改**，只是从读一个字段变成读两个。

   这不算把渲染的东西塞进 Sim：「我上一个 tick 在哪」是**模拟历史**，不是表现数据。
   Sim 没有为渲染做任何事，只是记住了自己刚才在哪。这个字段对以后的击退计算、位移判定、
   乃至联机回滚的状态历史都是同一方向。

2. **用 `Time.fixedTime`，不在 `FixedUpdate` 里自己记时间戳。**
   `Time.fixedTime` 的语义就是「最近一次 FixedUpdate 开始的时刻」，正是插值需要的量。
   自己记 `_tickTime = Time.time` 有个尴尬：一帧内跑 2 个 `FixedUpdate` 时，两次可能记录同一个值。

3. **显式 `Mathf.Clamp01(alpha)`。** Unity 的 `Lerp` 内部其实也会夹，但语义不同：
   内部夹是"防止越界"，这里夹是因为"alpha 本来就该在 [0,1]"。tick 迟到时
   `Time.time - Time.fixedTime` 会大于一个步长，不夹住会**插过头**，方块冲到目标位置前面。

4. **插值只改表现，不改逻辑。** `PlayerSim.Position` 仍是每 16.7ms 更新的真值，
   HUD 显示的也是它。所以 Step 3 的验收方法（读 HUD 数字）不受插值影响——这一点也让回归测试变得简单。

### 代价

**画面比模拟落后约一个 tick（16.7ms）。** 插值只能在两个已知状态之间取点，区间右端是"刚算出来的位置"，
所以显示的是过去到现在的中间。靠预测未来消除延迟会在状态突变时算错。Unity 的 `Rigidbody` 插值同样如此。

### 验证

编译 0 警告 0 错误。

| # | 操作 | 结果 |
|---|---|---|
| A | 帧率 100+ 下按 W | 平滑连续，不再一跳一跳 ✅ |
| B | 帧率改成 60 | 平滑 ✅ |
| C | 改回 0 | 正常，FPS 回到 VSync 值 ✅ |
| D | 回归：相机 Rotation Y 改 90 后按 W | 仍朝 +X 走 ✅ |

---

## 块 4c — Step 4 形式化检查

### `Sim/` 分层（Roadmap 第一条验收标准）

**判据措辞修正**：Roadmap 原文写「`Sim/` 下检索不到任何 UnityEngine 表现类」。
但块 3a 已把 `Vector3`/`Quaternion`/`Mathf` 放行，而 `Vector3` 确实是 UnityEngine 类型。
所以措辞改成「检索不到任何 UnityEngine **引擎对象**（`Vector3`/`Quaternion`/`Mathf` 除外）」——
原措辞在本块之后已经不再准确。

**实际扫描结果**：

```
Sim/ 里出现的 UnityEngine 类型：
  Vector3 ×15   ← 铁律 1 放行
  Mathf   ×5    ← 铁律 1 放行
  Transform ×1  ┐
  Time      ×1  ├ 全部出现在 /// 文档注释里（那些注释正是在说明「Sim 不用什么」）
  Rigidbody ×1  │
  Camera    ×1  ┘
```

加 `grep -v '///'` 过滤注释行后**零命中**。`Sim/` 的 `using` 只有一条：`using UnityEngine;`。

### 全项目状态复核

- `FixedUpdate` 定义：全项目只有 `Boot/BattleLoop.cs` 一处。
- 依赖图与约定表一致：`Sim/` 无项目内依赖；`Inputs/ → Sim`；`Boot/ → Inputs,Sim,View`；
  `View/PlayerView → Sim`；`View/DebugHud → Boot,Inputs,Sim`（调试例外）。
- csproj 的 `<Compile Include>` 与磁盘文件一致（每次加文件后都核）。

### **→ M1 全部完成（Step 1~4）**

Step 1 时间轴 ✅ / Step 2 输入采集与缓冲 ✅ / Step 3 相机相对移动 ✅ / Step 4 分层与渲染插值 ✅

**当前链路完整可跑**：Input System → InputSampler → InputFrame → PlayerSim.Tick → Position →
PlayerView（插值）→ Transform。

### M1 收尾时的残留问题汇总

1. **帧率上不到 300**（约 100+），原因未查。不影响任何验收。
2. **没有重力、碰撞、相机跟随**——Step 3/4 不需要，属于 M3 之后。
3. **`PlayerView` 是唯一的表现层对象**，没有角色朝向（块 3a 决定不做）。
4. **开发用的帧率开关留在 `BattleLoop` 上**，打包前应设回 0。
5. **工程没有 git 仓库**（块 0 就提过，一直没处理）。M1 已完成，代码量在增长，风险在升高。


