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

---

## 块 4d — 建立版本控制

日期：2026-09-29

### 背景

块 0 就发现工程没有 git 仓库，一直挂着。M1 完成后代码量已增长到 9 个文件 + 一条完整链路，
再不做版本控制，一次误操作就能带走四个步骤的成果，而且没有 diff 可看、没有历史可回滚。
用户决定现在处理。

### 做了什么

- 新增 `.gitignore`（Unity 标准那套）。
- `git init -b main`，提交一次 M1 成果：**100 个文件**，commit `9210ea0`。
- 约定写进 `AGENTS.md` 的「版本控制」一节。

### 关键设计选择

1. **忽略生成的 `*.csproj` / `*.slnx`。** 它们由 Unity 生成，不该进版本库。
   **但有个副作用必须记下来**：`dotnet build Assembly-CSharp.csproj` 依赖它——
   新克隆之后必须先让 Unity 生成一次 csproj 才能做编译校验，在那之前只能静态检查。
   这也正好解释了「新增 `.cs` 要手动补进 csproj」那条规则为什么存在。
2. **忽略 `Library/`、`Temp/`、`Logs/`、`UserSettings/`、`obj/`。** 全是可再生的缓存。
   提交前验证过：100 个文件里没有一个是这些目录下的。
3. **第一次提交放在 `main` 上**，不开分支。初始提交本来就没有"默认分支要保护"这回事——
   开分支会让 main 空着，反而怪。
4. **不 push。** 用户红线里 push 需要单独确认，远端由他自己决定。

### 验证

`git status` 干净；`git log --oneline` 只有那一个提交；
逐个检查了暂存列表，`Library/`/`Temp/`/`Logs/`/`obj/`/`UserSettings/`/`*.csproj`/`*.slnx` 零漏网。

Windows 上 `core.autocrlf=true`，提交时刷了一屏 "LF will be replaced by CRLF" 警告。
这是正常的换行符转换提示，不是问题，已写进约定免得以后每次都被它干扰。

### 残留问题

1. **远端未建。** 现在只有本地仓库，硬盘坏了照样全丢。用户需要时自己加 remote 并 push。
2. **提交粒度约定刚定下**（每块验收通过提交一次），从 Step 5 开始执行。

---

## 块 5a — 状态机（Step 5）

日期：2026-09-29

### 背景

`PlayerSim.Tick` 里一直有个隐式判断：`direction.sqrMagnitude > 0f` 同时决定了「用加速度还是减速度」。
这其实是两个状态被压缩进一个三元表达式。Step 5 把它展开成有名字的 `PlayerState`，
为 Step 6 的攻击状态、Step 7 的三段帧、M6 的连招/取消窗口打地基。

**这一块不改变任何运动行为**——`switch` 的两个分支与原来的三元表达式逐字等价。
这是优点（零回归风险），也是局限（状态机的真正价值要等 Step 6 有了攻击状态才体现）。

### 做了什么

- 新增 `Sim/PlayerState.cs`：`enum PlayerState { Idle, Move }`，无 `using`。
- `Sim/PlayerSim.cs`：加 `public PlayerState State { get; private set; }`；`Tick` 改为「先转移、再 `switch` 出目标速度与逼近速率」。
- `View/DebugHud.cs`：`-- Player --` 段加一行 `State`。
- `Assembly-CSharp.csproj`：手动补 `<Compile Include>`（老规矩，漏了会静默不编译）。
- `Boot/BattleLoop.cs` **未动**：`Tick` 签名不变、`InputFrame` 不变、装配关系不变。

### 关键设计选择

1. **状态放 `PlayerSim` 内部，不单独成类。** 只有两个状态、判据只看输入。
   拆成独立的 `PlayerStateMachine` 类的话，它要做的决定（速度怎么变）需要 `Velocity` 和
   `InputFrame` 两个外部数据，接口会比实现还长。等 M6 状态涨到六七个（攻击/受击/闪避/连招）
   再拆——这正是 ROADMAP 里「先 switch，后重构」的意思。

2. **转移判据用输入，不用速度。**
   被否方案：用 `Velocity.magnitude > 0` 判。松手后角色还在滑行 0.2 秒，
   状态会一直挂在 `Move` 直到速度精确归零——玩家早就松手了，状态还说他「在移动」。
   **代价（明确记下）**：滑行那 0.2 秒里状态已是 `Idle`、速度还有 6→0 的一段。
   离线测试专门断言了这一点（滑行 180 tick 全部是 `Idle`）。
   Step 5 无所谓，M6 决定「攻击能不能取消滑行」时再回来看这条。

3. **状态不许碰 `Velocity`。** 这是验收标准第二条「切换瞬间速度连续无突变」的实现方式。
   状态的 `switch` 只决定「朝哪个目标速度逼近、多快逼近」，`Velocity` 唯一的改写点仍是那行 `MoveTowards`。
   - `Idle → Move`：上一 tick 速度就是 6，这一 tick 目标还是 6，`MoveTowards` 原地不动 → 无突变
   - `Move → Idle`：上一 tick 速度是 6，这一 tick 目标 0，从 6 按减速度往下拉 → 连续

   写成可证伪的不变量：**任意相邻两 tick，`|Δv| ≤ max(60, 30) × (1/60)` = 1.0 m/s**。
   `Velocity` 用 `{ get; private set; }` 而**不是**公开字段，就是为了让「谁在改状态」可搜索——
   不过注意 `Velocity` / `Position` 本身仍是公开字段（View 要直接读），这一块没顺手改，
   属于既有约定，不在这块范围内。

4. **不加 `StateTicks`（状态已持续 tick 数）。** Step 6 的「收招最后 3 tick 内能接上」需要它，
   Step 5 没有任何一条验收用到。现在加就是没被验证过的代码。

5. **HUD 只加状态名，不做实时 Δv 显示。** 考虑过在 HUD 上实时显示每 tick 的 Δv 让不变量可见。
   否掉的理由：`OnGUI` 每帧跑多次、渲染帧与 tick 不是一一对应，掉一帧就跨两个 tick，读数失去意义。
   Δv 不变量的严格验证放在离线测试里（能精确控制 tick 循环）；
   HUD 上 `Speed` 是否连续，肉眼已足够判断。

### 验证

**AI 侧（全部通过）**

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**。
- 三条静态检查：`Sim/` 无引擎符号；`FixedUpdate` 全项目仅 `Boot/BattleLoop.cs:95` 一处；`Inputs/` 不依赖 `View`。
- csproj 完整性双向核对：磁盘上 9 个 `.cs` 与 `<Compile Include>` 列表完全一致。
- **离线行为测试 19 项全过**（`%TEMP%` 临时工程 + 真 `UnityEngine.CoreModule.dll`，跑完即删）：
  - 构造后未 tick 即为 `Idle`（靠枚举首成员 = 0 的默认值）；静止 2 秒不位移、速度为零
  - 有输入的**第一个 tick** 就变 `Move`；松手**第一个 tick** 就回 `Idle`（此刻速度仍 5.5 m/s）
  - 摇杆半推（输入长度 0.5）→ `State = Move`、稳态速度 3 m/s（慢走语义没被吃掉）
  - **513 tick 全程逐 tick 检查 `|Δv| ≤ 1.0 m/s`，零违例**；含「滑行途中直接反向、速度穿过零点」这段
  - 状态切换精确发生在 tick `[60, 180, 183, 393]`（输入边沿处，无多余切换），**切换那一 tick 的 Δv 同样 ≤ 1.0**
  - **行为回归：与 Step 4 的旧逻辑 570 tick 逐位完全一致**（`mismatches=0`）——证明状态机没改变任何运动行为
  - 回归量核对：静止按 W 满 1 秒位移 = 5.75 m、松手滑行 = 0.55 m，与 Step 3 记录值一致

**用户侧（Editor 实测）**：**已验收通过**（连同块 5b~5d 的 HUD 一起）。
用户确认：状态跟手切换、右栏切换日志的 `|Δv|` 为 `Idle -> Move` 的 1.000 与 `Move -> Idle` 的 0.500、
`超限 0 次`。验收过程中额外暴露并修掉了两个 HUD 的 bug，见块 5c/5d。

### 残留问题

1. **滑行期间状态是 `Idle` 但速度非零**——决策 2 的已知代价，M6 再议（见上）。
2. **`PlayerState` 只有两个状态**，`switch` 的 `default` 分支目前等价于 `Idle`。
   Step 6 加 `Attack` 时必须回来检查这个 `default`——否则新状态会被静默当成 `Idle` 处理。
   这是 `enum` + `switch` 写法的固有风险，也是 M6 要重构成状态类的原因之一。
3. 状态机没有回退保护：如果将来出现 `Idle ↔ Move` 之外的转移合法性要求（比如攻击中不许移动），
   现在这套「每 tick 重算状态」的写法需要换成带转移表的写法。

---

## 块 5b — 调试可见性：HUD 分两栏 + 记录历史

日期：2026-09-29

### 背景

用户反馈：「现在的 hud 上少很多东西，我不是测试脚本，很多东西看不到」。

**原因一（真 bug）**：`GUI.Label` 的矩形是写死的 `new Rect(10, 10, 560, 380)`。
`fontSize 20` 的行高约 23px，而文字已经涨到 22 行（约 510px）——**底部约 6 行被裁掉了**，
正好是 `-- Player --` 那一整段。所以 Step 5 加的 `State` 用户根本看不见。
根子在于「按一屏能放多少来定内容」而不是「让框去适应内容」。

**原因二（设计问题）**：模拟 60 tick/秒、屏幕 100+ 帧/秒，瞬时值每秒变一百多次。
「切换瞬间速度有没有突变」这类问题盯屏幕是看不出来的。之前我以「`OnGUI` 采样跨 tick 不可靠」
为由否掉了在 HUD 上显示 Δv，结论下错了——**该改的是采样位置和呈现方式，不是放弃呈现**。

### 做了什么

- **`View/DebugHud.cs` 重写**：
  - 矩形改由 `GUIStyle.CalcSize` 量出来，框跟着内容走，以后加减行都不会再被裁。
  - 拆成左右两栏：左栏当前快照，右栏历史记录。第二栏的 x 由第一栏实测宽度推出，改字号不会重叠。
  - 新增**速度连续性统计**：滑动窗口（默认 600 tick = 10 秒）内的 `max|Δv|`、超限次数、
    有效/作废样本数。
  - 新增**状态切换日志**：最近 6 次切换，每条带 tick 号、`from -> to`、切换前后速度、
    那一刻的 `|Δv|`。这条记录本身就能同时验收 Step 5 的两条标准。
  - 超限时 `Debug.LogError` 打一条到 Console（罕见，不会刷屏；且可以直接复制出来定位）。
  - 文字改为在 `LateUpdate` 里建一次、`OnGUI` 只画——原来的实现每次 `OnGUI` 都重建整个
    40 行字符串，而 `OnGUI` 每帧要跑多次。
  - 新增三个 Inspector 字段：`_fontSize`(16) / `_windowTicks`(600) / `_transitionLogSize`(6)，
    都用惰性重建，Play 中改也生效。
- **`Boot/BattleLoop.cs`**：新增 `LastCameraYaw` 属性，缓存上一个 tick 实际传给 Sim 的偏航角。
  HUD 左栏新增 `CamYaw` 一行——这是唯一一个影响移动方向、之前却完全看不到的量。

### 关键设计选择

1. **采样放 `LateUpdate`，靠比对 tick 号保证「一个样本 = 一个 tick」。**
   帧没跑 tick（帧率 > 60 时常见）就跳过——**跳过不等于作废**，没有 tick 就没有信息损失。
   一帧跨了多个 tick（掉帧）就作废并计数，**不静默算错**。
   被否方案：把采样挪进 `BattleLoop.FixedUpdate`。那样能与帧率完全无关、更精确，
   但要新建一个统计类并决定它住哪个目录（`Sim/` 会污染模拟层、`Boot/` 是装配层、
   新建 `Debug/` 要先改 `AGENTS.md` 的目录约定）。当前帧率 100+，用不上这个精度。
   **代价明确记下**：帧率低于 60 时有效样本会明显减少，右栏会写出来，不假装数据是好的。
   真需要时，改动点是「把采样挪进 `FixedUpdate`」。

2. **不绑定重置热键。** 统计是滑动窗口（10 秒自动滚掉），日志只留最近 6 条，
   所以重新测一次不需要清空——直接再按一次就行。避免为此往玩家输入资产里塞开发专用的 action。

3. **不用 `Time.timeScale` 做暂停来读屏。** 铁律 3 明确禁止（会连带改变固定步长与物理）。
   历史记录本身就是「冻结的读数」，比暂停更好用。

### 验证

**AI 侧（全部通过）**

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**。
- 三条静态检查：`Sim/` 无引擎符号；`FixedUpdate` 全项目仍只有 `BattleLoop.cs` 一处；`Inputs/` 不依赖 `View`。
- **环形下标独立验证（Python 复刻算式，跑完即删）**——这是最可能写错又最难肉眼看出来的部分：
  - 状态切换日志的「新 -> 旧」读取顺序：`len 1..7`，每个都推 3 圈以上，全部与期望倒序一致
  - 滑动窗口的 `max` 扫描（含「未绕圈」与「已绕圈」两种状态）：`len 1..7` 全部正确
  - 超限计数扫描：`len 1 / 3 / 600` 全部正确

**用户侧（Editor 实测）**：待做。

### 残留问题

1. **布局宽度没做自适应**：两栏总宽约 800px，Game 视图更窄时会被右边裁掉。
   缓解手段是调小 `_fontSize`。做自动换行/缩放属于过度设计，先不做。
2. **`CalcSize` 依赖 `GUI.skin`**，所以尺寸必须在 `OnGUI` 里算，不能在 `LateUpdate` 里算。
   目前的代价是每次 `OnGUI` 调用会 new 两个 `GUIContent`（文字本身已缓存，不是主要开销）。
3. **统计只覆盖速度连续性这一条不变量**。以后要盯别的不变量（比如 M6 的取消窗口），
   得往这里继续加，右栏会越来越长——到那时应该考虑把统计逻辑从 `DebugHud` 里拆出去。

---

## 块 5c — 修两个 bug：判据缺容差 + 版式超出屏幕

日期：2026-09-29

### 背景

用户实测反馈两条：「左侧超出左侧屏幕了」，以及 Console 里刷
`[DebugHud] 速度不连续：tick 63，|Δv| = 1.0000 m/s，上限 1.0000。状态 Move，速度 4.421 -> 4.250`。

### 问题一：判据缺浮点容差（**是我的 bug**）

日志里 `|Δv| = 1.0000`、上限也是 `1.0000`，但速度大小只从 4.421 变到 4.250——
说明速度**方向**转了，向量差才是 1.0。这是「低于满速时转向」的正常情况，
`MoveTowards` 本来就允许每 tick 变 1.0。所以问题不在模拟层，在判据。

**根因**：`Vector3.MoveTowards` 内部做 `current + a / magnitude * maxDistanceDelta`，
那个除法让结果离 `current` 的距离比 `maxDistanceDelta` 大几个 ULP。
而 HUD 里用的是**零容差**的 `deltaV > DeltaLimit`。

**实测取证**（离线，引用真 `UnityEngine.CoreModule.dll`，72000 组「速度大小 × 转向角」样本）：

```
DeltaLimit        = 1            ← 60f * (1f/60f) 在 float 里恰好就是 1.0
超过零容差上限的  : 6606  (9.18%)
最大超出量        : 3.5762787E-07 m/s
```

**9.18% 的转向 tick 都会触发假警**，最大超出 3.6e-7。

**修法**：加 `DeltaTolerance = 1e-4f`。真正的突变是 0.5~6.0 量级，比容差大几千倍，
漏不掉任何真问题。日志也改成打印 6 位小数和含容差的上限，下次一眼能分辨。

**教训**：离线测试里我写的是 `1f + 1e-4f`（带容差），HUD 里却把容差丢了——
**线上判据比线下松/紧不一致**，等于两套标准。以后凡是从测试搬进产品的判据，容差要一起搬。

### 问题二：两栏版式超出屏幕

块 5b 把 HUD 改成左右两栏，两栏总宽约 834px（16 号字）。Game 视图比这窄时就会溢出。

**修法：版式自适应**，不再靠手调字号：

- 两栏（矮而宽）和单栏（高而窄）**各找一个放得下的最大字号，谁的字号大用谁**。
  不需要「屏幕够不够宽」这类阈值——屏幕宽自然选两栏，屏幕窄自然选单栏，
  因为另一种版式会被迫缩得更小。
- 缩到 `MinFontSize`(9) 还放不下就硬画，**宁可超出屏幕也不裁内容**（块 5b 的教训）。
- **屏幕尺寸没变时先试上一帧的结论**。这不是省那几个 `CalcSize`，而是省 `GUIStyle`：
  往下搜字号的过程中每个字号都会 new 两个 style，每帧重搜等于每帧制造一堆垃圾。
- 触发自动缩小时，`FPS` 那行会显示 `[字号自适应 N]`，免得用户以为字变小是坏的。

### 验证

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**。
- 容差：离线实测 72000 组样本取证（见上）。
- 版式：Python 复刻 `ResolveLayout` 控制流，跑 **13 种屏幕尺寸 × 2 种日志长度**：
  - 不变式「只要存在放得下的版式，就绝不超屏」全部成立；
    唯一超屏的是 300×300（连 9 号字都放不下，属已注明的兜底情况）
  - 1920×1080 → 640×480：字号 16 → 12，正确重搜（不沿用旧结论）
  - 日志从空变满（右栏变高）时正确重判
  - 实测占用举例：1024×768 及以上用 16 号两栏 834×470；800×450 缩到 15 号；640×480 缩到 12 号

### 残留问题

1. **`_fontSize` 现在是「最大字号」而不是「字号」**，实际值可能更小。Inspector 的 Tooltip 已说明。
2. 布局搜索的成败依赖 `CalcSize` 的返回值，而它基于 `GUI.skin` 的实际字体度量——
   上面的验证用的是**模拟的尺寸**，不是真实度量。真实数字要等用户在 Editor 里看。
3. 屏幕尺寸变化才触发重搜；文字变短时不会主动放大字号（只会维持，直到放不下才重搜）。
   对调试 HUD 够了，写进注释说明。

---

## 块 5d — HUD 压缩版式（组名当行首标签）

日期：2026-09-29

### 背景

用户误拉了 Game 视图的 **Scale 滑块**，那才是「左侧超出屏幕」的真正原因——被拉离 `Fit` 之后，
`Screen.width/height` 报的仍是**游戏分辨率**，而面板实际可见区域更小，
所以块 5b/5c 再怎么自适应也救不了。**这条记下来：以后遇到「HUD 超出屏幕」，先确认 Scale 是不是 `Fit`。**

排除这一项后用户仍要求精简，选的是「保留信息、改折叠」而不是直接删段
（因为 Step 6 攻击要用 `-- Input --` 和 `Attack`）。

### 做了什么

只改 `BuildText` / `BuildHistory` 的排版，**采样逻辑与判据一行没动**。

盘点发现：左栏 25 行里有 **10 行是纯装饰**——`-- Input --` / `-- Frame --` / `-- Player --`
三行标题，加三处空行，再加 `Tick:`/`Time:`/... 的冒号对齐占的宽度。
占地方的是排版，不是信息本身。

改法：**组名挪到行首当标签，一组一行**。

- 左栏 25 行 → **4 行**（`tick|time|frame|fps` / `Input` / `Frame` / `Player`）
- 右栏 8~14 行 → **4~10 行**（`连续` 2 行 + 空行 + `切换` 标题 + 最多 6 条记录）
- 信息一个没删，只有 `水平速度` 去掉了——`speed` 是三维速度大小，这一步 y 分量恒为 0
  （没有重力/跳跃/击退），两者是同一个数。等 M3 加了竖直运动再拆开，注释里写了。

顺带两处：
- 输入不变式正常时只显示 `[OK]`，真不相等了才摊开算式 `[MISMATCH 12 != 11]`
  ——原来的写法平时就在占宽度。
- `PlayerState` 用 `{,-4}` 左对齐，`Idle`/`Move` 长度一样所以数字不会左右跳。

### 验证

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**。
- 版式：左栏最长行从 33 字符涨到约 66 字符（`Input` 那行），右栏不变。
  两栏总宽从 834px 变成约 1000px（16 号字估算）。1200px 以上的 Game 视图不再触发自动缩小。
- 实际观感待用户在 Editor 确认——`CalcSize` 的真实度量仍然只能在那里跑。

---

## 块 6a — Attack 状态 + 计时 + 自动收招

日期：2026-10-03

### 背景

Step 5 结束时攻击的管道已经**全通**，只差最后一环：

```
InputBuffer 队列                    ✓ 块 2a
InputSampler 每 tick 消费一个事件    ✓ 块 2b
InputFrame.Attack                   ✓
BattleLoop 每 tick 递给 PlayerSim    ✓
PlayerSim 完全无视 frame.Attack      ← 本块补的就是这里
```

用户决定「**攻击期间不可以移动，按状态来做**」。这条决定同时激活了块 5a 残留问题第 3 条
记下的那个转折点，见下。

### 做了什么

- `Sim/PlayerState.cs`：加 `Attack`（追加在末尾，`Idle` 仍是 0，不动枚举默认值的语义）。
- `Sim/SimConstants.cs`：加 `AttackDurationTicks = 30`（0.5 秒），**标注为 Step 6 的临时占位**，
  Step 7 搬进 `AttackData`。
- `Sim/PlayerSim.cs`：
  - 加 `StateTicks` 属性和 `SetState(next)` 私有方法——**所有状态改变都走这一个口子**，
    所以 `StateTicks` 不可能忘记维护。
  - 转移从「每 tick 重算」改成「**按当前状态分派**」（下面详述）。
  - 行为 `switch` 里 `Idle` / `Attack` 共用「停下」分支，但**写成两个 case 标签**。
- `View/DebugHud.cs`：`Player` 行加 `t{StateTicks}`，出招时能看到它从 0 数到 29。

### 关键设计选择

1. **转移必须改成「按状态分派」——这是本块最重要的一处，也是用户那句「按状态来做」的落点。**

   Step 5 时转移是一行：
   ```csharp
   State = direction.sqrMagnitude > 0f ? Move : Idle;   // 每 tick 无条件重算
   ```
   只有两个状态、判据只看输入时这是对的。加了 `Attack` 之后它**立刻就是错的**：
   玩家在攻击中碰一下方向键，下一 tick 就被这行改回 `Move`，攻击根本挂不住。

   改成：
   ```csharp
   switch (State)
   {
       case Attack:   // 无视移动输入，只有计时满了才收招
       default:       // Idle / Move：看输入决定去哪
   }
   ```
   同一份 `switch`，从「算出一个状态」变成「当前状态决定能去哪」。
   代价是转移规则散在了 `case` 里，状态再多就该换成转移表——M6 的事。

2. **`StateTicks` 用 0-based（进入那 tick 记 0），代价是判断写成 `>= N - 1`。**
   这个 `- 1` 很容易差一，所以注释里写死了「写判断时先回来对一遍」，
   并且离线测试专门验了序列 `(Attack,0) → (Attack,29) → (Idle,0)` 和「每 tick 严格 +1 不跳号」。

3. **`Idle` 和 `Attack` 行为相同，但仍然写成两个 case 标签。**
   合并成一个 `case Idle: case Attack:` 少一行，但哪天攻击要带前冲（位移突进）或受击要带击退，
   改动的第一步就变成「先把它拆开」。分开写等于把拆分点标出来了。

4. **`default` 分支保留，并在注释里写明它是敌人。**
   C# 不对枚举做穷尽性检查，没有 `default` 会报 CS0165（使用了未赋值的局部变量），
   所以它必须存在。但它一旦存在就有了具体行为——新增状态忘了加 `case` 会**静默**落进来表现成 Idle。
   离线测试里加了一条：`PlayerState` 的每个枚举成员都必须被实际驱动到过。

5. **攻击期间用减速度而不是直接归零。** 这是验收标准「无突变」的要求，也顺带产生了
   「出招会有个短滑行」的副产品：满速出招会滑约 0.3 米后停住。这是**有意的**，
   直接归零会让速度在一 tick 内从 6 掉到 0。

### 验证

**AI 侧（全部通过）**

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**。
- 三条静态检查全绿（`FixedUpdate` 全项目仍只有 `BattleLoop.cs:103` 一处）。
- **离线行为测试 30 项全过**（`%TEMP%` 临时工程 + 真 `UnityEngine.CoreModule.dll`，跑完即删）：
  - 出招的第一个 tick 就进 `Attack`；**恰好持续 30 tick** 后收招；松手出招回 `Idle`、按着 W 出招回 `Move`
  - `StateTicks` 序列验到逐项：`(Attack,0)` → `(Attack,29)` → `(Idle,0)`，且每 tick 严格 +1 不跳号
  - **攻击期间不能移动**：满速出招后 12 tick 内停稳，之后 16 tick **位置一个毫米不动，即使 W 一直按着**
  - **出招那一 tick 速度只降一个减速度步长 0.50**（6.0000 → 5.5000），不是归零 —— 无突变
  - 攻击中再按攻击键**不会重开**：`StateTicks` 从 10 继续到 11，收招时刻不变
  - **345 tick 全程速度连续性不变量零违例**，含 3 次进出攻击的切换 tick
  - **行为回归：不按攻击键时与 Step 5 逻辑 570 tick 逐位一致**；1 秒位移仍是 5.75 m
  - 枚举成员覆盖检查：`PlayerState` 三个成员都被实际驱动到过

  测试过程里失败过 2 次，**两次都是测试自己的断言写错**（循环少跑一 tick；把加速度步长 1.0
  错当成减速度步长，实际是 0.5）。实现从头到尾没为这两条改过。
  **教训**：手写期望值的断言容易错，改成从常量算（`MoveDeceleration * Dt`）。

**用户侧（Editor 实测）**：待做，验收点见 `ROADMAP.md`。

### 残留问题

1. **`Default` 攻击键是什么还没验过**。`PlayerActions.inputactions` 里 `Attack` 绑的是模板默认键，
   按下去 `InputSampler` 会数到，但具体是哪个键要用户实测确认。
2. **攻击期间不接受新指令，那一下会被丢掉**（不是缓存）。这正是块 6b 要修的——加锁存。
3. **`AttackDurationTicks` 是常量**，改手感要改代码。Step 7 换成 `AttackData`。
4. **出招滑行约 0.3 米**（满速时）。是有意的（见设计选择 5），但如果手感不好，
   改法是在 `Attack` 分支用更大的减速度，而不是归零。

---

## 块 6b — 取消窗口 + 预输入

日期：2026-10-03

### 背景

用户问「6b 是不是就是预输入的意思」——**是**，就是 input buffering。

6a 里攻击是「六亲不认」的：攻击中按的键会被 `InputSampler` 消费掉、变成一帧 `Attack = true`，
然后 `PlayerSim` 因为在攻击状态里无视它，**这一下就没了**，接不出第二招。

### 做了什么

- `Sim/SimConstants.cs` 加两个常量：
  - `AttackCancelWindowTicks = 3` —— 收招最后 3 tick 能接招（验收标准点名的）
  - `AttackInputBufferTicks = 12`（0.2 秒）—— 预输入有效期
- `Sim/PlayerSim.cs`：
  - 加 `_attackBufferTicksLeft` 倒计时 + `BufferedAttackTicksLeft` 只读属性
  - `SetState` 加 `restart` 参数（下面详述）
  - `Attack` 分支加取消窗口判断
- `View/DebugHud.cs`：`Player` 行在预输入非 0 时显示 `buf{N}`。

### 关键设计选择

1. **为什么要两个窗口，而不是一个。**
   验收标准只说「收招最后 3 tick 内按攻击能接上」。但如果只有那 3 tick 收键，
   在窗口开之前一点按的键会被直接丢掉，手感很差。
   真正的预输入都有时限：**按下后保留 12 tick，过期作废**。
   - 按早了（超过 0.2 秒才到窗口）→ 过期丢掉，不会「攻击开头按一下、0.5 秒后突然又出一招」
   - 在窗口附近按 → 存着，窗口一开自动放出去
   两个窗口一个管「什么时候能用」（3），一个管「记多久」（12）。

2. **用倒计时，不记 tick 号。**
   记「按下的 tick 号、用的时候相减」需要读 `frame.Tick`（把「帧号戳得对不对」变成 Sim 的
   隐式依赖），或者自己维护一个全局 tick 计数器（和 `SimClock` 重复，还让 `Tick` 不再只依赖参数）。
   倒计时只需要一个 `int`。

3. **`SetState` 加 `restart` 参数——这是本块最容易出错的地方。**
   接招时状态**没变**（还是 `Attack`），但那是**新的一次**攻击，`StateTicks` 必须归零。
   而 `SetState` 原来的逻辑是「`next == State` 就当成留在原状态、计时 +1」，
   直接复用会让接出来的第二招**短掉**（计时没归零）。
   加 `restart` 开关而不是绕过 `SetState` 手动改 `StateTicks`——
   「所有状态改变都走一个口子」这条不能破。

4. **窗口判断读的是「上一 tick 结束时的 `StateTicks`」。**
   这不是 bug，但很容易看错，所以写清楚：
   - 攻击占 `StateTicks` 0..29，屏幕上看到 27/28/29 就是最后 3 tick
   - 在 Tick T 做的判断，读到的是 T-1 结束时的值
   - 所以「屏幕上显示 27（或 28、29）时按下的键」会在这三个 tick 的**下一次 Tick** 被放出去
   - 判断必须排在「攻击是否结束」之前，否则在 `StateTicks=29` 时按的键会被收招抢先吃掉

5. **具体数字：连按时每次接招间隔 28 tick**（不是 30）。验收里 `ROADMAP.md` 说的是
   「最后 3 tick 内按能接上」，实际节省的是 2 tick——因为链式接招发生在窗口的第一个可用 Tick。
   按得越晚省得越少（在 `StateTicks=29` 按就完全不省）。这是取消窗口的正常语义。

### 验证

**AI 侧（全部通过）**

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**；三条静态检查全绿；csproj 与磁盘一致。
- **离线行为测试 29 项全过**（`%TEMP%` 临时工程 + 真 `UnityEngine.CoreModule.dll`，跑完即删）：
  - **窗口的 3 个 tick 逐个验**：在 `t=27`、`t=28`、`t=29` 分别按下，都能接上，
    且接招时预输入被消耗掉（`buf` 归 0）
  - 接出来的第二招是**完整的 30 tick**，不是残缺的（验证 `restart` 参数确实生效）
  - **按太早会过期**：在 `t=1` 按（离窗口 26 tick > 12），走到窗口时 `buf` 已归 0，按原时长收招，不接
  - **按早了但没过期能补上**：在 `t=23` 按，窗口一开自动接上 —— 这才是预输入的核心价值
  - **疯狂连按 300 tick**：接招 11 次，**相邻两次最小间隔 28 tick**（隔得近就说明一次按键触发了两次）
  - 241 tick 速度连续性不变量**零违例**，含 7 次接招
  - 回归：不按攻击键时与 Step 5 逻辑 570 tick 逐位一致；单次攻击仍是 30 tick

  测试过程失败过 2 次，**又是测试自己的断言算错**（本轮第 4、5 次）：
  都是同一个原因——**忘了接招发生在按下的下一 tick**。实现没为这两条改过。

**用户侧（Editor 实测）**：待做，验收点见 `ROADMAP.md`。

### 残留问题

1. **预输入是「1 层」的**：锁存是一个倒计时，狂按也只会接出**一下**。
   连招要接出 N 段得做成队列——M6 的事。
2. **`AttackInputBufferTicks` 和 `AttackCancelWindowTicks` 都是常量**，调手感要改代码。
   Step 7 会把攻击时长搬进 `AttackData`，届时这两个也该一起考虑。
3. **只有攻击有预输入**。跳跃/闪避（如果以后有）要不要各自的预输入，到时候再定——
   现在没有通用的预输入机制，是写死在 `PlayerSim` 里的。

---

## 块 7a — 攻击数据外置（AttackData）

日期：2026-10-03

### 背景

`AttackDurationTicks` 是 `SimConstants` 里的常量，改手感要改代码。
Step 7 的验收标准是「改 Inspector 里的前摇/判定/后摇数字，手感立刻变化，无需改代码」。

### 先改约定，再改代码

这一步和铁律 1 **直接冲突**：目录约定里写的是 `Sim/Data/ # ScriptableObject 定义`，
但 `ScriptableObject` 是 `UnityEngine.Object`，**持有引擎状态**（native 对象、实例 ID、资源生命周期）。
按铁律 1 自己那句判准「是否持有引擎状态或触发引擎行为」，它不该进 `Sim/`。
禁止清单里没列它，是因为当初没想到——**这是清单的漏洞，不是放行的理由**。

用户拍板：拆成两半，字段只写一遍。所以先改了 `AGENTS.md`：

- 铁律 1 的禁止清单加 `ScriptableObject`，并补一张表说明「纯数据 / 资产包装」怎么分。
- 目录约定：`Sim/Data/` → **`Scripts/Data/`**（新的第 5 个目录）。
- 依赖表加 `Data` 一列一行：`Data → Sim`（用纯数据类型）、`Boot → Data`（装配时读取）。

### 做了什么

| 文件 | 内容 |
|---|---|
| `Sim/AttackData.cs` | **纯 C# struct**：`StartupTicks` / `ActiveTicks` / `RecoveryTicks` + 派生 `TotalTicks` |
| `Data/AttackDataAsset.cs` | `ScriptableObject` 包装：`[SerializeField] AttackData _data` + `public AttackData Data => _data` |
| `Sim/SimConstants.cs` | 删掉 `AttackDurationTicks`（时长搬去 `AttackData`） |
| `Sim/PlayerSim.cs` | `Tick` 多一个 `AttackData` 参数；两处 `AttackDurationTicks` 换成 `attackData.TotalTicks` |
| `Boot/BattleLoop.cs` | 加 `_attackData` 字段；每 tick 把 `_attackData.Data` 传给 `Tick` |

### 关键设计选择

1. **`AttackData` 是 `struct` 不是 `class`（用户要求，理由比「更纯粹」更硬）。**
   `Data` 属性返回的是**值拷贝**，所以 Sim 物理上不可能反向改到磁盘上的 `.asset`。
   用 `class` 的话两边共享同一个对象，哪天 Sim 手滑写了字段，改的就是资产本身。

2. **特性写成 `[System.Serializable]` 而不是 `[Serializable]`（不加 using）。**
   它是 mscorlib 的东西不是 `UnityEngine` 的，加它只是为了 Unity 能在 Inspector 里展开编辑。
   这样 `AttackData.cs` 能保持**零 using**，和 `SimConstants` / `PlayerState` 一样——
   「Sim 不认识 Unity」看一眼文件头就能确认，不用去查 using 列表。

3. **数据每 tick 传进 `Tick`，不是构造时存起来。**
   两个理由：一是播放中改 Inspector 的数字要**立刻**生效（这是本块的验收标准）；
   二是 `Tick` 保持「只依赖参数」，重放同一串参数必然得到同一结果。
   代价是 `Tick` 现在 4 个参数，Step 8 加判定数据时应该考虑收进一个小 context 结构体。

4. **三段字段现在就要，不做「先加一个 `DurationTicks`、7b 再拆成三段」。**
   因为 `.asset` 是**用户在 Editor 里手工建的**，拆两次等于让他建两次、迁移两次。
   三段的**和**当场就被用上（`TotalTicks`），没有死字段。

5. **`TotalTicks` 最小返回 1。**
   Sim 不该能被错误数据搞坏：总时长为 0 的话攻击占不满一个 tick，
   取消窗口那一堆 `StateTicks >= 总时长 - N` 会立刻为真，变成「每 tick 都接招」的退化状态。
   但这里**只是不让它崩**——配置得不合理仍然要报警，那个由 `AttackDataAsset.OnValidate` 做（只警告不修正，
   默默改掉等于把配置错误藏起来）。

### 验证

**AI 侧（全部通过）**

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**。
- 静态检查全绿。`Sim/` 唯一一条 `using UnityEngine` 仍是 `PlayerSim.cs`（`Vector3`/`Mathf` 例外）；
  `Data/AttackDataAsset.cs` 是 `using ActionGame.Sim` + `using UnityEngine`，与新的依赖表一致。
  （顺手把 `SimConstants` 里一条**注释**中的 `ScriptableObject` 字样改掉了——
  它让检查无故报警，而老是无故报警的检查会被无视。）
- **离线行为测试 30 项全过**：
  - 6 组三段组合（`(8,6,16)` / `(1,1,1)` / `(0,0,5)` / `(20,0,0)` / `(0,30,0)` / `(10,3,7)`）的
    `TotalTicks` 与**实测攻击时长**逐一对上
  - **播放中换数据立刻生效**：60 tick 的招 → 换 `(2,2,2)` → 下一招就是 6 tick → 换回来又是 60
  - **一招进行中改数据**，那一招按新数据收招（证明是每 tick 现读，不是起手时锁死）
  - 三段全 0 / 负数都兜到 1，攻击退化成极短但**不崩、不死循环**
  - 取消窗口跟着总时长走：`(8,6,16)`→窗口从 t=27 开、`(2,2,6)`→t=7、`(5,5,5)`→t=12，三种都能接上
  - 回归：`(8,6,16)` 时与 Step 5 逻辑 570 tick 逐位一致；单次攻击 30 tick；疯狂连按间隔仍是 28

**⚠️ 测试抓到一个真 bug（这次不是测试自己写错）**

首轮 4 项失败，全是同一个根因：**初始按键既触发了攻击、又留在了预输入缓冲里。**

`PlayerSim` 原来是「先把按键写进缓冲，再做转移」。于是起手那一 tick 的按键被记了两份——
攻击已经出了，缓冲里还留着 12 tick 的有效期。等取消窗口一开，缓冲还活着，就**凭空多接一招**。

**为什么 Step 6 没暴露**：攻击时长 30 tick，窗口在 tick 28 才开，而缓冲早在 tick 12 就过期了。
**时长一短就露馅**——这正是「把时长做成可配置」才会暴露的问题。

修法：把预输入的记账**挪到转移之后**，因为「这一 tick 的按键有没有被用掉」只有转移才知道。
并且 `default` 分支改用「有没有攻击请求」而不是「这一 tick 有没有按」，
让「按键 → 要么立刻出招、要么进缓冲」变成互斥的两条路。

**用户侧（Editor 实测）**：待做，见 `ROADMAP.md`。

### 残留问题

1. **`Tick` 现在 4 个参数**，Step 8 加判定数据后应该收进一个小 context 结构体。
2. **`AttackCancelWindowTicks` / `AttackInputBufferTicks` 还在 `SimConstants`**，没进 `AttackData`。
   7b 会把后摇做成真正的阶段，到时取消窗口天然属于后摇段，那时再搬更顺。
3. **没有校验「总时长 ≥ 取消窗口」**。总时长比窗口还短时，整个攻击都是窗口，
   变成「每 tick 都能接招」——数据极端才出现，没加检查。

---

## 块 7b — 三段帧：攻击拆成三个状态

日期：2026-10-03

### 背景

用户定的是「三段的差别会大到需要各自的转移规则（受击打断之类），所以做成三个状态」。
`Attack` 一个状态拆成 `AttackStartup` / `AttackActive` / `AttackRecovery`。

### 做了什么

- `Sim/PlayerState.cs`：`Attack` 拆成三个，各自注明时长从 `AttackData` 的哪一段来、谁会用到它
  （判定段是 Step 8 判定框要挂的地方，后摇段有取消窗口）。
- `Sim/AttackData.cs`：`TotalTicks` 改成 `Σ max(1, 段)`（见设计选择 2）。
- `Sim/PlayerSim.cs`：
  - 转移从「一个 Attack 分支」变成三个分支，每段自己的 `StateTicks >= 本段时长 - 1` 判断
  - **删掉 `SetState` 的 `restart` 参数**（见设计选择 1）
  - 行为 `switch` 从 2 个 case 变 4 个
- `View/DebugHud.cs`：`State` 的对齐宽度 6 → 14（`AttackRecovery` 是 14 字符）。

### 关键设计选择

1. **三个状态把 `restart` 参数吃掉了——这是本块最漂亮的一处简化。**

   块 6b 为了让「接招时状态没变但要重新计时」成立，给 `SetState` 加了 `restart` 开关，
   还在注释里专门解释它为什么必要。拆成三个状态后，接招是
   `AttackRecovery` → `AttackStartup`，**一次普通的状态切换**，清零是本来就有的行为，
   参数直接删掉。

   这不是「6b 做错了」——是那时候只有一个 Attack 状态，接招确实不换状态，必须特殊处理。
   **状态的粒度决定了转移逻辑的形状**：粒度对了，特例自己就消失了。
   这也正好印证了用户选 (A) 的判断。

2. **`TotalTicks` = `Σ max(1, 段)`，不是直接相加。**
   三个状态是独立的，状态机每 tick 只评估一次转移，
   **进入一个状态的那一 tick 必然属于那个状态**——所以填 0 的段实际占 1 tick。
   这不是防御性编程，是描述事实：`TotalTicks` 必须报真实值，
   否则以后拿它算进度条、算判定帧都会差。
   于是「填 0」和「填 1」等价，写进注释和 `AttackDataAsset` 的文档。

   被否方案：加一个级联循环，让 0 时长的段真的占 0 tick。最精确，但状态机里多一层循环，
   「一 tick 一步」的模型被打破，转移逻辑会难读得多。不值——1/60 秒没人感觉得到。

3. **行为 `switch` 里四个状态共用「停下」分支，但仍然一条条列出来。**
   合并成 `case Idle: case AttackStartup: case AttackActive: case AttackRecovery:` 更短，
   但那样「哪天攻击要带前冲」的改动点就藏起来了。

4. **`AttackCancelWindowTicks` 没有跟着搬进 `AttackData`。**
   ROADMAP 里原本写 7b 要搬，**没搬，是有意的**：`AttackData.asset` 用户已经建好了，
   给 struct 加字段有「旧资产里新字段拿不到初始值、静默变 0」的风险
   （0 的取消窗口 = 每 tick 都能接招）。等以后有第二个招、真的需要「重招取消窗口更小」时再搬，
   那时候会同时加上「后摇时长 ≥ 取消窗口」的校验。

### 验证

**AI 侧（全部通过）**

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**；静态检查全绿；csproj 与磁盘一致。
- **离线行为测试 41 项全过**：
  - **状态序列**：默认 (8,6,16) 下前摇占 8、判定占 6、后摇占 16，顺序是 前摇→判定→后摇→Idle 不回头
  - **段边界逐 tick 验**：tick 0/7 是前摇、8/13 是判定、14/29 是后摇、30 是 Idle
  - **填 0 的段占 1 tick**：(0,30,0)→32、(0,0,5)→7、(1,1,1)→3、(0,0,0)→3，
    `TotalTicks` 与实测**逐一对上**；且「填 0」与「填 1」等价
  - **取消窗口只在后摇段**：后摇 t=13/14/15 三个 tick 逐个验，按下都**当场**接回前摇、计时归零；
    接招后下一 tick 是前摇 t=1（确实是重头数）
  - **判定段按太早会过期**：判定第一 tick 按下（离窗口 20 tick > 有效期 12），
    攻击按原时长收招，没接出第二招
  - 300 tick 速度连续性不变量零违例
  - 回归：单次攻击仍 30 tick、疯狂连按间隔仍 28、不按攻击键时与 Step 5 逻辑 570 tick 逐位一致
  - 枚举覆盖：`PlayerState` 五个成员全部被驱动到过，并逐一列出无遗漏

**⚠️ 测试自己出了两种问题（这次比真 bug 更值得记）**

首轮 4 项失败，全是我的测试写错；但更糟的是**其中 3 条是「假通过」**：

- 我用 `while (p.State == AttackRecovery && ...)` 想走到后摇段，
  可一开始状态是 `AttackStartup`，**循环一次都没跑**。角色压根没到过后摇，
  而我断言的是 `State == AttackStartup`——**恒真**，所以那三条「接回前摇」是白过的。
- 另一条速度连续性测试里 `max|dv| = 0.00000`：出招间隔（11 tick）比一招（15 tick）还短，
  角色一直黏在攻击状态里、速度恒为 0，**不变量成了空跑**。

**教训**：断言要能失败。「某状态等于某值」如果那个值在当前路径下必然成立，它就没在验证任何东西。
现在这条速度测试加了 `max|dv| > 0.4` 的反空跑断言，后摇那条也改成断言
「按下的**那一 tick 当场**切换、计时归零」，而不是只断言结果状态。

**用户侧（Editor 实测）**：待做，见 `ROADMAP.md`。

### 残留问题

1. **`AttackCancelWindowTicks` 还在 `SimConstants`**，见设计选择 4。
2. **`Tick` 现在 4 个参数**，Step 8 加判定数据后应该收进一个小 context 结构体。
3. **五状态之后 `default` 那个坑更值钱了**。`PlayerSim` 的行为 `switch` 里 `default` 仍然等价于 Idle，
   新增状态忘了加 `case` 会静默落进去。离线测试里「每个枚举成员都被驱动到过」那条现在是主力防线，
   Step 8 加判定/命中相关状态时不能删。

---

## 块 8a — 朝向 + 判定胶囊 + Gizmo 线框

日期：2026-10-05

### 背景

Step 8 的验收标准是「红盒在判定帧出现、命中变绿；靶子后退掉血；同一招式不重复命中同一目标」。
拆成三块：8a 让判定体**看得见**，8b 加靶子和命中检测，8c 让靶子会掉血后退。

判定体用**胶囊**、朝向**要加**、靶子**做成 Sim 对象**——都是用户定的。

### 先改约定，再改代码

铁律 5 原来写的是「命中判定用主动查询（`Physics.OverlapBox` 等），不用 Trigger 回调」。
我们的判定是**纯数学**，和它的措辞不一致，所以先改了文档：

- 明确「不用 Trigger / 碰撞回调」和「主动查询」两条分开说。
- 加了一条：**查询优先用 Sim 自己算的纯数学**，三个理由——
  ① 确定性（PhysX 浮点跨平台不保证，而 `Sim/` 要能回放和联机）；
  ② 不用为每 tick 穿一层引擎通道（纯数学只要启动时把场景几何搬一次）；
  ③ 本项目的几何全是方块，够用。
- 并写明什么时候才该换成 `Physics.OverlapBox`（网格/地形、物体多到需要空间划分）。

### 做了什么

| 文件 | 内容 |
|---|---|
| `Sim/AttackData.cs` | 加判定胶囊三字段：`HitStartOffset` / `HitEndOffset` / `HitRadius` |
| `Data/AttackDataAsset.cs` | 默认值 0.6 / 1.8 / 0.5；`OnValidate` 加两条校验（见设计选择 3） |
| `Sim/PlayerSim.cs` | 加 `FacingYaw` + `FacingDirection` + `IsHitFrame` + 判定胶囊三个只读属性 + `IsAttacking()` 静态方法；缓存 `_attackData` |
| `View/PlayerView.cs` | 把方块按 `FacingYaw` 转过去 |
| `View/HitboxGizmo.cs` | **新增**：判定帧画线框胶囊 |

### 关键设计选择

1. **`FacingYaw` 的更新规则只有一条：有移动输入 且 不在攻击中。**

   三个后果一次说完：移动时跟输入方向、站定时保持不动、**攻击中锁定**。

   攻击中锁定是有意的——不锁的话，挥到一半转个身，判定胶囊会在判定帧中途换方向。
   判断放在 `SetState` **之后**，所以读到的 `State` 是这一 tick 的最终状态：
   这一 tick 刚起手的话当场就锁住了，不用等下一 tick。

2. **角度约定必须和 `CameraRelativeDirection` 完全一致。**
   那边用的是「偏航 θ 的方向向量 = `(sinθ, 0, cosθ)`」，所以这边用 `Atan2(x, z)` 而不是 `Atan2(z, x)`。
   离线测试拿 `yaw` 从 -360° 到 360° 每 15° 验了一遍：
   `FacingDirection` 必须等于 `CameraRelativeDirection(0, 1, yaw)`。**两处约定不一致的话，
   「朝移动方向」和「朝相机前方」就会对不上**，而且这种错很难一眼看出来。

3. **`AttackDataAsset.OnValidate` 加了两条校验，其中一条是给「老资产加新字段」兜底的。**

   给**已经存在**的 `.asset` 加 `[SerializeField]` 字段时，序列化数据里没有这个字段，
   Unity 会把它留成 **0**——字段初始化器**不会**补上。半径 0 的胶囊**永远打不到任何东西**，
   而且不报错，是最难查的那种 bug。所以：

   - `HitRadius <= 0` → 明确警告，并提示「如果是给老资产新加了这个字段，请手动填一个值」
   - `HitEndOffset < HitStartOffset` → 警告胶囊方向反了
   - 两条都是**只警告不修正**（前面几块定的原则：默默改掉等于把配置错误藏起来）

   7b 时我特意没把 `AttackCancelWindowTicks` 搬进 `AttackData`，就是为了躲这个坑；
   这次躲不掉，所以正面处理。

4. **`View` 把方块转过去，而且朝向不做插值、直接设。**

   位置插值是为了消抖动，**朝向插值却是害的**：判定胶囊用的是 Sim 里那个 `FacingYaw`，
   方块必须显示同一个值，否则你看到方块朝一边、判定框朝另一边，验收时根本判断不了对错。
   代价是转向会「啪」一下跳（输入方向一变就跳），动作游戏里通常要加转身速度——
   那是后面的事，现在**看见真相**比好看重要。

5. **`PlayerSim` 缓存一份 `_attackData`。**
   判定胶囊那几个属性是从 Tick **外面**读的（View、Gizmo），拿不到 Tick 的参数。
   这份缓存只是参数的值拷贝，**不影响「Tick 只依赖传入参数」**这条性质——
   重放同样的参数仍然得到同样的结果。

6. **Gizmo 单独一个类，不塞进 `PlayerView`。**
   `PlayerView` 是真正的游戏表现层，按铁律 1 它**不许**依赖 Boot；
   而画线框要读 `BattleLoop.Player`。所以单独建 `View/HitboxGizmo.cs`，
   走依赖表里那条 `△`（只有调试工具能走的边），和 `DebugHud` 同一个待遇。

### 验证

**AI 侧（全部通过）**

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**；静态检查全绿；csproj 与磁盘一致。
- **离线行为测试 39 项全过**：
  - **朝向跟输入**：W/D/S/A 分别得 0°/90°/180°/-90°，W+D 得 45°，S+A 得 -135°；
    相机 yaw=37 时按 W 得 37°；**松手滑行 20 tick 朝向不回弹**
  - **攻击中锁朝向**：按住 W 朝 0°，出招后改按 D，朝向**仍是 0°**；收招后立刻跟上变成 90°
  - **角度约定一致**：`yaw` 从 -360° 到 360° 每 15° 一组（共 49 组），
    `FacingDirection` 与 `CameraRelativeDirection(0,1,yaw)` 全部吻合，零不符
  - **判定帧**：默认 (8,6,16) 下正好落在 tick `8..13`（6 个 tick）；前摇那一 tick 不是判定帧
  - **胶囊几何**：朝 0° 时在 +Z 方向（身前 0.6 → 1.8 米），
    **朝 90° 时转到 +X 方向**（右侧 0.6 → 1.8 米），长度 1.2 米、半径 0.5
  - **回归**：与 Step 5 逻辑 570 tick 逐位一致；单次攻击仍 30 tick；连按间隔仍 28；
    `IsAttacking()` 逐个枚举值验过（3 个攻击状态为真，Idle/Move 为假）

**用户侧（Editor 实测）**：待做，见 `ROADMAP.md`。

### 残留问题

1. **转向是「啪」一下跳的**（见设计选择 4）。要平滑得在 `Sim` 里加转身速度，现在没做。
2. **判定胶囊参数在 `AttackData` 里，但靶子还没有**，所以这一块只验证「框画得对不对」，
   验证不了「打得中打不中」——那是 8b。
3. **`HitboxGizmo` 只在 Scene 视图可见**（Gizmos 的固有限制）。Play 时要把 Scene 视图和 Game 视图并排看。

---

## 块 8b — 靶子 + 命中检测 + 每招命中记录

日期：2026-10-06

### 背景

8a 让判定体**看得见**了，但还不能判中谁。8b 补上：靶子作为 Sim 对象、胶囊对球的判定、
以及「同一招式不重复命中同一目标」的去重名单。

### 顺带记下的三个决定（用户在本轮定的，属于 M5 的内容）

用户说现在的移动方式不是他要的，想做**第一人称**或过肩第三人称。定了四条：

1. **第一人称**（不是过肩第三人称）
2. **角色朝向跟一定距离内的敌人**——但敌人还不存在，**现在先按跟鼠标写**
3. **转身角速度无限制**（= 瞬间对准，不做平滑转身）
4. **先做 8b**，相机改造往后放

**这几条对架构的影响，先记下来免得丢**：

- **`Sim/` 几乎不用改**。相机朝向已经是通过 `cameraYaw` 这个 float 传进来的，
  Sim 不知道相机是什么东西。换视角主要改 Boot（相机怎么摆怎么转）和 View（角色怎么显示）。
- **但相机朝向会变成「模拟状态」**：它决定移动方向，也可能决定判定胶囊朝向，
  所以它必须**可回放**。做法是把 `Look` 输入真正消费掉，在 Sim 里维护相机 yaw
  （现在 `InputFrame.LookX/LookY` 采集了但**没有任何消费者**）。
- **第一人称下「朝向跟移动方向」这条规则不适用**：按 S 应该是**倒退走**，不是转身朝后走。
  所以第一人称的朝向来源是「相机 / 最近敌人」，不是移动输入。
- **转身无限制 = 不做平滑转身**，所以 8a 里「朝向插值」那个话题也一并搁置了。

### 做了什么

| 文件 | 内容 |
|---|---|
| `Sim/TargetSim.cs` | **新增**：靶子。8b 只有 `Position` + `Radius` |
| `Sim/HitTest.cs` | **新增**：纯数学判定。`CapsuleOverlapsSphere` + `SqrDistanceToSegment` |
| `Sim/PlayerSim.cs` | `Tick` 加 `TargetSim[] targets` 参数；加判定、去重名单、`HitTargetCount` |
| `Boot/BattleLoop.cs` | 收集靶子 Transform → 翻译成 `TargetSim[]`；每 tick 传进去 |
| `View/HitboxGizmo.cs` | 命中变绿（`HitTargetCount > 0`） |
| `View/DebugHud.cs` | `Player` 行在命中时显示 `hit{n}` |

### 关键设计选择

1. **判定放在 `Tick` 的最后。**
   上面刚更新完 `Position`，判定胶囊要用这一 tick 的最新位置。放前面的话判定永远慢一 tick，
   角色移动快时会「穿过去没打到」。

2. **每个判定帧 tick 都要查一遍，不能只查第一 tick。**
   判定帧有 6 个 tick，角色在这几 tick 里可能还在滑行、胶囊在动。
   只在第一 tick 查的话，靶子晚一点进入范围就打不到了。
   **正因为要查 6 次，去重名单才是必需的**——不做的话同一个靶子会被记 6 次。

3. **去重名单用「目标在数组里的下标」当身份，不给 `TargetSim` 加 Id 字段。**
   靶子数组在场景里一次建好、顺序不变，下标就是稳定身份。**代价是这条约定必须守住**：
   哪天靶子会动态增删，下标就会变，得改成真正的 Id。写进注释了。

4. **名单清零挂在「起手」上，不是挂在「按键」上。**
   这是测试意外验出来的：在后摇窗口**没开**的时候按攻击，那次按键只是进了预输入缓冲，
   招式并没有起手——这时候名单**不该**清，还得留着报上一招的战绩。
   所以清零写在 `if (startsAttack)` 里，而不是 `if (frame.Attack)` 里。

5. **`Tick` 的参数从 4 个变 5 个**（加了 `TargetSim[] targets`），**没有上 `TickContext`**。
   DEVLOG 里预告过「Step 8 加判定数据后应该收进 context」，现在看 5 个还能读，
   6 个才真的丑。**下次再加参数就上 context**，别硬撑。

6. **靴子用 `TargetSim[]` 传参，不是让 `PlayerSim` 持有一份。**
   玩家不该知道世界上有哪些敌人；而且这样 `Tick` 仍然只是「传入参数的函数」，
   重放同一串参数必然得到同一结果。用数组长度当数量，省一个 `count` 参数。

7. **Gizmo 用「本次挥砍打中过没有」变色，不是「此刻正重叠着没有」。**
   判定帧只有 6 个 tick（0.1 秒），而且去重让命中只记一次——
   按瞬间重叠变色的话绿色会一闪而过，肉眼根本抓不住。

### 验证

**AI 侧（全部通过）**

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**；静态检查全绿；csproj 与磁盘一致。
- **离线行为测试 48 项全过**：
  - **点到线段的平方距离**：垂足在段内 / 垂足在段外（**夹紧生效**，两头各验一次）/
    点在线段上 / **线段退化成点不产生 NaN** / 远在延长线上的点不会被算成距离 0
  - **胶囊 vs 球**：正前方和侧向各验「命中 / 不命中 / 临界」三档；
    并且专门验了「同一个位置**胶囊不中、球命中**」——这就是球侧向太胖的证据
  - **判定只在判定帧跑**：前摇期间靶子就在范围内也不记；判定帧第一 tick 就记上；
    后摇期间名单保留
  - **同一招式不重复命中**：靶子大到判定帧全程包住，6 个 tick 里 `hit` 一直是 `1`（`[1,1,1,1,1,1]`），
    没有累加到 6
  - **多目标**：三个靶子（两个在范围内、一个在远处）→ 记 2 个；去掉远处那个仍是 2
    （证明是「中」不是「被截断」）；空数组和 `null` 都不崩
  - **朝向影响**：朝 90° 时 +X 方向的靶子被打中；同一个靶子位置，朝 0° 时打不中
  - **名单清零挂在起手上**：收招后重新出招，起手那一 tick 就清零；
    **窗口外按攻击时招式没起手、名单不清**；接招那一 tick 也清零
  - **回归**：与 Step 5 逻辑 570 tick 逐位一致；单次 30 tick；连按间隔 28；判定帧仍是 tick 8..13

**⚠️ 测试自己错了两次（本轮第 6、7 次）**

1. 退化线段那条我期望 9，忘了端点 `a` 的 z 是 0.6 —— 实际是 `3² + 0.6² = 9.36`。
2. 「第二招起手清零」那条，我按的那一 tick 状态还是 `AttackRecovery`、`StateTicks=5`，
   **窗口（13）还没开**，所以那次按键只是进了预输入缓冲、招式并没有起手。
   实现是对的，是我把「按键」当成了「起手」。

第二条顺手改成了更严格的断言（同时验「窗口外按不清零」和「起手才清零」），
这比原来的写法多覆盖了一个真实场景。

**用户侧（Editor 实测）**：待做，见 `ROADMAP.md`。

### 残留问题

1. **靶子还不会动、不会掉血**——那是 8c。
2. **靶子被近似成球**，立方体的角落会被近似掉（边长 1 的方块，内切球 0.5 / 外接球 0.707）。
3. **去重名单上限 8 个**。超出的那一刀仍然会造成伤害，只是不会被去重——极端情况，没处理。
4. **`Tick` 已经 5 个参数了**，见设计选择 5。

---

## 块 8c — 靶子掉血后退

日期：2026-10-06

### 背景

8b 能打中了，但靶子不会反应。8c 补上验收标准的第三条：**靶子后退掉血**。

这一块有个新情况：**靶子会动了**。之前靶子是静态的，场景里的 Cube 摆在哪、Sim 就在哪，
两者天然一致；一旦靶子会后退，不把 Sim 的位置搬到 Transform 上就看不出来。
所以本块必须引入 `TargetView`。

### 做了什么

| 文件 | 内容 |
|---|---|
| `Sim/TargetSim.cs` | 加 `PreviousPosition` / `KnockbackVelocity` / `MaxHealth` / `Health`；加 `ApplyHit()` / `Tick(dt)` / 静态 `TickAll()` |
| `Sim/AttackData.cs` | 加 `HitDamage` / `KnockbackDistance` |
| `Sim/SimConstants.cs` | 加 `KnockbackDeceleration = 20` |
| `Sim/PlayerSim.cs` | `ResolveHits` 命中时**当场结算**：扣血 + 给出击退初速度 |
| `Boot/BattleLoop.cs` | 初始化靶子血量；每 tick 推进靶子；`GetComponent<TargetView>()` 自动绑定；暴露 `Targets` |
| `View/TargetView.cs` | **新增**：把靶子位置搬到 Transform，带渲染插值 |
| `View/DebugHud.cs` | 加一行 `Targets  #0 hp 90/100` |

### 关键设计选择

1. **击退参数用「距离」而不是「初速度」。**
   配参数的人是按「这一刀把敌人推开几米」想的，不是按「初速度几米每秒」。
   代码里反推：匀减速位移 `d = v²/(2a)` → `v = sqrt(2ad)`。
   **好处是参数即结果**——Inspector 填 1 米，验收时直接量靶子退了多远就行。

   **实测**：填 1 米 → 实际后退 **1.0528 米**（离散积分偏大 5.3%），
   和玩家「1 秒位移 5.75 vs 解析值 5.70」是同一回事（显式欧拉）。填 2 米 → 2.0749 米。

2. **推进顺序：玩家先、靶子后。**
   ```
   Player.Tick(...)          ← 里面做判定，给靶子击退初速度
   TargetSim.TickAll(...)    ← 紧接着推进靶子
   ```
   这样击退在**同一 tick** 就生效，打击感不慢半拍。
   反过来（先推靶子再判定）的话，击退要等下一 tick 才开始动。

3. **`TargetSim.TickAll` 做成 `Sim/` 里的静态方法，而不是让 Boot 自己写 for 循环。**
   「怎么推进」是模拟逻辑，归 Sim；Boot 只该负责「调用它」。
   以后 M4 加 AI 敌人时，这段会搬进一个统一推进所有实体的地方（`WorldSim` 之类）。

4. **`TargetView` 走 `PlayerView` 那个模式：Boot 通过 `Bind()` 注入数据。**
   它**只依赖 Sim、不依赖 Boot**——真正的表现层不许走铁律 1 依赖表里那条 `△`（那是给调试工具的）。
   绑定用 `GetComponent` 自动做，不让用户在 Inspector 里填下标：**下标填错了很难看出来**
   （靶子显示的是别人的位置），自动绑不会错。

5. **`TargetSim` 也带 `PreviousPosition`，也做渲染插值。**
   击退是 60 tick/秒推进的，屏幕 100+ 帧/秒，不插值的话靶子在击退的那 18 tick 里会一顿一顿。
   **Step 4 在玩家身上踩过一次，这是同一个坑的第二次**——所以这次一开始就带上，不再踩。

6. **血量夹在 0，但不处理死亡。**
   这一块不做销毁、不做死亡动画。但显示 `-10/100` 很怪，所以夹住。
   真正处理死亡是后面的事，记在残留问题里。

### 验证

**AI 侧（全部通过）**

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**；静态检查全绿；csproj 与磁盘一致。
- **离线行为测试 32 项全过**：
  - **掉血**：满血 100 → 打中一下变 90；判定帧 6 个 tick 里血量一直是 `90`（`[90,90,90,90,90,90]`），
    没有掉到 40；打不中则一滴不掉
  - **击退距离**：填 1 米 → 实测 **1.0528 米**；填 2 米 → 2.0749 米（**翻倍关系成立**）；填 0 → 完全不动
  - **击退方向**：正前方的靶子往 +Z 推、x 不偏移；右前方的靶子往右前方推；
    **y 保持不变**（击退是水平的，靶子不会被推上天）
  - **衰减**：速度单调递减、从不反弹；最终**精确归零**（证明用的是 `MoveTowards` 不是 `Lerp`）；
    停下用 18 tick；**停住后位置一个毫米都不动**
  - **`PreviousPosition`**：静止时等于 `Position`；击退过程中**不相等**（插值才有意义）；停稳后又相等
  - **多靶子**：近处和稍远处的都掉血、太远的一滴不掉；一拳 200 伤害打 100 血 → 夹在 0；连打 15 次仍是 0
  - **回归**：玩家运动与 Step 5 逻辑 570 tick 逐位一致；单次攻击 30 tick；连按间隔 28；
    `TickAll` 对 `null` 和空数组都不崩

**⚠️ 测试自己错了四次（本轮第 8~11 次），错法很典型**

1. **击退方向那两条**：我把靶子放在 `y=1.5`，可胶囊在**水平方向**、判定半径和只有 0.8。
   **那个靶子压根没被打中**，位移当然是 0。第二次是放在 `(0.9, 0, 0.9)`，到线段距离 0.9 > 0.8，也没中。
   → 教训：**写几何相关的断言前，先把距离算出来**，别凭感觉摆位置。
2. **`PreviousPosition` 那条**：击退只持续 18 tick，而一招 30 tick，
   **等我开始检查时早就停稳了**，两个值当然相等。
   → 教训：要验「过程中的状态」，就得在**过程中**采样，不能等结束再看。
3. **单次攻击 31 tick**：我的 `FullSwing` 辅助函数把「退出那一 tick」也数进去了。
   **这个 off-by-one 本轮已经犯过好几次**，这次改成数「状态还是攻击中」的 tick 数。

**用户侧（Editor 实测）**：待做，见 `ROADMAP.md`。

### 残留问题

1. **靶子不会死**。血量到 0 就停在那——没有销毁、没有死亡表现、没有「已经死了就别再打」的判断。
2. **击退会叠加速度**：连着两刀打中，第二刀的初速度**覆盖**第一刀的（不是相加）。
   近距离连招时靶子不会被打飞，只会匀速后退。要不要改成叠加，看手感再说。
3. **靶子之间不碰撞**：可以互相穿过。
4. **`BattleLoop` 里开始出现「推进实体」了**（那行 `TargetSim.TickAll`）。
   现在只有一行还好，M4 加 AI 敌人时该上 `WorldSim`，把这段搬过去。

---

## 块 C1 — 相机朝向进 Sim + 过肩第三人称

日期：2026-10-06

### 背景

M2 做完了，用户提出现在的移动方式不是他要的，想做第一人称。讨论后定了四条
（见「视角改造」一节），并把 **M5「锁定与相机」提前**。

讨论时拆成 C1/C2 两块：**C1 先做「过肩第三人称」**——因为**能看见角色**，
调朝向、调判定胶囊方向时肉眼就能判断对错；C2 只是把相机挪到眼睛位置 + 隐藏角色。
C1 是 C2 的全部基础设施。

### 做了什么

| 文件 | 内容 |
|---|---|
| `Sim/CameraSim.cs` | **新增**：`Yaw` / `Pitch`，由 `Look` 输入驱动 |
| `Sim/SimConstants.cs` | 加 `MouseSensitivityDegreesPerPixel` / `MaxPitchDegrees` / `CameraDistance` / `CameraHeight` |
| `Sim/PlayerSim.cs` | **朝向来源从「移动输入」改成「相机」**；修了一个起手那一 tick 朝向不更新的 bug |
| `Boot/BattleLoop.cs` | 持有并推进 `CameraSim`；删掉 `LastCameraYaw`；`_camera` 改为「用来找 `CameraRig`」 |
| `View/CameraRig.cs` | **新增**：把相机摆到角色后上方，朝向跟 `CameraSim` |
| `View/DebugHud.cs` | 显示 `cam yaw` / `pitch` |

### 关键设计选择

1. **方向反过来了——这是本块最值钱的一处。**

   ```
   改造前：Sim 每 tick 读相机的 eulerAngles.y     ← Sim 依赖场景
   改造后：View 读 CameraSim 来摆相机             ← 单向，Sim 不再碰场景
   ```

   改造前其实留了个**确定性的口子**：谁能手动转一下那个相机，同一串输入就会得到不同的移动方向。
   只是从来没人去转它。现在 `Sim/` 里 **`eulerAngles` 和 `.position` 一个都没有了**
   （静态检查里专门加了一条盯这个）。

2. **`CameraSim.Tick` 没有 `dt` 参数——这是有意的。**

   鼠标给的是**位移**（这一 tick 累计移动了多少像素），不是速率。
   转多少角度 = 位移 × 灵敏度，和时间长短无关：一 tick 内移 10 像素就转 1.5°，
   哪怕这一 tick 因为卡顿花了 33ms 也一样。

   对比 `PlayerSim.Tick` 就看得出来：那边是速度积分，必须要 `dt`。
   **参数里没有 `dt`，本身就是「这是位移不是速度」的信号。**

3. **Yaw 用 `Mathf.Repeat` 归一化到 `[0, 360)`，Pitch 用 `Mathf.Clamp` 夹在 ±89°。**

   - Yaw 要归一化：连续转几十圈后角度会大到 float 精度开始损失（会看出抖动）。
   - Pitch 要夹住：拉到底不夹的话世界会整个翻过来，玩家直接失去方向感。
   - 上限 89 而不是 90：正好 90° 时视线和世界上方向平行，`Quaternion.Euler` 退化成万向节死锁，画面会抖。

4. **朝向更新判断**前后两边都看**——这是被测试抓出来的 bug，见下。

5. **`CameraRig` 也走 `Bind` 注入，只依赖 Sim。**
   静态检查确认了：`View/` 里依赖 `Boot` 的**只有 `DebugHud` 和 `HitboxGizmo`** 两个调试工具，
   `PlayerView` / `TargetView` / `CameraRig` 三个真正的表现层都只依赖 `Sim`。
   这是依赖表里那条 `△` 一直在起作用。
   绑定用 `GetComponent` 从 `_camera` 上找，不另开 Inspector 槽——**线不用重接**，
   而且「相机夹具挂在相机上」这个约束自动成立。

### 验证

**AI 侧（全部通过）**

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**；静态检查全绿；csproj 与磁盘一致。
- **离线行为测试 36 项全过**：
  - **相机 yaw**：横移 100 像素转 15°（= 100 × 0.15）；反方向移回去归零；
    初始值 400°/-30° 归一化成 40°/330°；**连续转 200 tick（相当于 15000°）后 yaw 仍在 [0,360)**，
    且角度正确（15000 ≡ 240）；鼠标不动则不变
  - **相机 pitch**：上移 100 像素抬头 15°；一直推到顶夹在 +89°、拉到底夹在 -89°
  - **朝向跟相机**：不按任何方向键、只转相机，朝向跟着变；
    **连续 120 tick 鼠标乱动，朝向逐 tick 都等于相机 yaw**（零不符）
  - **按 S 是倒退走**：朝向仍是相机方向（0°），不是 180°；速度方向确实朝后；
    连续按 30 tick 也没转过去
  - **攻击中锁朝向**：攻击中把相机转到 90°，朝向仍是 0°；**收招后立刻**跟上变成 90°
  - **移动和判定都跟相机**：相机朝 90° 时按 W 往 +X 走；
    同一个 +X 方向的靶子，相机朝 90° 时打中、朝 0° 时打不中
  - **回归**：移动积分与 Step 5 逻辑 570 tick 逐位一致；单次 30 tick；连按间隔 28；胶囊长度 1.2 米

**⚠️ 测试抓到一个真 bug（不是测试自己错）**

首轮 1 项失败：相机朝 90° 时，+X 方向的靶子**没被打中**。

根因：朝向更新写在 `SetState` **之后**，所以起手那一 tick 读到的新状态已经是 `AttackStartup`，
`!IsAttacking(State)` 为假 → **朝向不更新**。结果是**攻击锁定在「上一次更新时的朝向」，
而不是「按下攻击时你看到的方向」**。

正常游玩时两者只差一个 tick（因为朝向前面每 tick 都在跟相机），几乎看不出来——**但那是碰巧**。
测试里相机从第一 tick 就是 90°、角色朝向初始是 0°，就露馅了。

修法：判断条件改成**前后两边都看**——
只要这一 tick 的前后至少有一边不在攻击中，就跟随相机：

| 转移 | 跟不跟 | 为什么 |
|---|---|---|
| Idle/Move → 攻击 | **跟** | 起手那一 tick 就对齐到按下时看的方向 |
| 攻击内部（段间、接招） | 不跟 | 锁定 |
| 攻击 → Idle/Move | **跟** | 收招立刻跟上，不用等下一 tick |

只看新状态 → 起手那一 tick 用旧方向；只看旧状态 → 收招后慢一 tick。**两边都要看。**

**用户侧（Editor 实测）**：待做，见 `ROADMAP.md`。

### 残留问题

1. **还是过肩第三人称**，第一人称是 C2。
2. **`Look` 的正负号没实测过**。现在写的是「鼠标上推 → 抬头」，
   但这依赖 Input System 对 `<Pointer>/delta` 的 y 轴约定。**方向反了就把 `CameraSim.Tick`
   那行的 `+` 改成 `-`。**
3. **相机位置只受 yaw 影响，不受 pitch 影响**。所以抬头低头时相机不绕着角色转，只是原地转头。
   过肩相机通常会让位置也绕（保持距离固定），第一版先简单。
4. **相机会穿墙**。没有遮挡检测，贴墙时相机会穿进墙里。
5. **鼠标灵敏度/眼高这些是常量**，不能热调。等真的要调手感时进数据资产。

---

## 块 C2 — 切成第一人称

日期：2026-10-06

### 背景

C1 做完的是**过肩第三人称**（相机在角色后上方）。之所以先做那一版，是因为**能看见角色**，
调朝向、调判定胶囊方向时肉眼就能判断对错——C1 的验收就是这么过的。

C1 验收时发现 `Look` 的 **y 轴方向反了**（上下颠倒），改了一个符号。改完直接进 C2。

### 做了什么

| 文件 | 内容 |
|---|---|
| `Sim/CameraSim.cs` | **改符号**：`Pitch + ...` → `Pitch - ...`（已实测确认） |
| `Sim/SimConstants.cs` | 删 `CameraDistance` / `CameraHeight`，加 `EyeHeight = 1.6` |
| `View/CameraRig.cs` | 相机贴到眼睛位置（不再有后方偏移） |
| `View/PlayerView.cs` | 隐藏身体（只关渲染，组件照常跑） |

### 关键设计选择

1. **`CameraSim.Tick` 的符号是「减」——这条已经实测确认，不再是猜测。**

   Input System 的 `<Pointer>/delta` 在屏幕坐标系下 **y 向下为正**，
   所以鼠标往下推时 `LookY` 是正的，要抬头就得减。写成加号上下会整个反过来。

   第一版我按「y 向上为正」写的，留了句「实测反了就翻符号」。结果真反了。
   **这类依赖引擎约定的符号没法离线验证，只能靠实测——所以第一版就该写成一眼能改的形式**，
   而不是花力气猜。这次的做法（写一行、注释里写明怎么改）是对的。

2. **第一人称下相机位置不受 yaw 影响。**

   眼睛就在身上，转视角不会让眼睛绕着身体转。所以：

   ```csharp
   transform.position = basePosition + Vector3.up * EyeHeight;   // 没有 yaw 的事
   transform.rotation = Quaternion.Euler(Pitch, Yaw, 0f);
   ```

   对比 C1 那版（过肩）是 `位置 = 角色位置 + Quaternion.Euler(0, Yaw, 0) * 后方偏移`——
   偏移要跟着 yaw 转。第一人称把那个旋转整个去掉了，**更简单**。

3. **隐藏身体只关渲染，不关组件。**

   ```csharp
   renderers[i].enabled = false;    // 而不是 gameObject.SetActive(false)
   ```

   Transform 照常每帧更新。以后要挂手、武器、影子（它们得跟着身体走），
   或者哪天切回第三人称，**删掉这一行就恢复显示**——不用重新接线。

   用 `GetComponentsInChildren<Renderer>()` 而不是 `GetComponent`：身体可能是这个物体本身，
   也可能是子物体（比如以后换成真模型，MeshRenderer 在子物体上）。

4. **判定胶囊**不**在眼睛高度上。**

   `Position` 是角色位置（方块中心），胶囊是从 `Position` 出发的水平线段；
   相机在 `Position + up * 1.6`。两者差 1.6 米。

   打地平面附近的靶子没问题（靶子也在那个高度），但**目标浮空时就要回来处理**。
   写进 `SimConstants.EyeHeight` 的注释和残留问题了。

### 验证

**AI 侧**

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**；静态检查全绿；csproj 与磁盘一致。
- **离线行为测试 17 项全过**（C2 改的是 `View/`，离线测不了；
  但**符号翻转改了 `Sim/` 的行为**，必须重测）：
  - **符号**：鼠标上推 100 像素 → 抬头 15°（Pitch 为正）；下推 → 低头 15°；
    上推 250 再下推 250 → 回到 0°。**并且专门写了一条反证**：
    「把上推当成正数就会变成低头」——把那个 bug 长什么样固化成了一条测试。
  - **上下限**：一直上推夹在 +89°、下推夹在 -89°；到顶后继续推 500 tick 也不越界
  - **互不干扰**：只上下推不影响 yaw；只左右推不影响 pitch；斜着推两个都动
  - **回归**：连续 120 tick 鼠标乱动，朝向逐 tick 都等于相机 yaw；
    按 S 仍是倒退走；攻击中仍锁朝向
  - **常量**：`EyeHeight` 1.6 米在合理范围；俯仰上限 < 90°；
    灵敏度 0.15 度/像素 → 横跨 1920 像素约 288°（转视角不吃力）

**用户侧（Editor 实测）**：待做，见 `ROADMAP.md`。

### 残留问题

1. **判定胶囊不在视线高度**（见设计选择 4）。
2. **角色方块被隐藏了**，所以 C1 里「转视角看方块朝向」那条验收方式没了。
   现在要看朝向只能靠 `HitboxGizmo` 的判定胶囊（它会跟着朝向转）。
3. **相机会穿墙**，还是没有遮挡检测。
4. **`CameraRig` 没有「切回第三人称」的开关**——要切得改代码。
   这一版是有意写死的：加开关就是给想象中的需求写代码。

---

## 块 C2b — 相机高度、恢复渲染、修抖动

日期：2026-10-06

### 背景

C2 验收时用户提了三件事：

1. 相机高度改成**比物体高 0.75 米**（原来是 1.6 米）
2. **不要隐藏渲染**
3. **「现在插值关掉了？抖动有点严重」**

### 做了什么

| 文件 | 内容 |
|---|---|
| `Sim/SimConstants.cs` | `EyeHeight = 1.6` → `CameraHeightOffset = 0.75`（名字也改了，见下） |
| `Sim/CameraSim.cs` | 加 `PreviousYaw` / `PreviousPitch`，`Tick` 里先记旧值 |
| `View/CameraRig.cs` | **旋转也做插值**（`Mathf.LerpAngle`）+ 用新常量 |
| `View/PlayerView.cs` | 删掉 `HideBodyForFirstPerson()` |

### 关键设计选择

1. **常量从 `EyeHeight` 改名成 `CameraHeightOffset`。**

   0.75 米不是「眼睛的高度」，是「相机挂在身上哪个高度」——**调它就是调视角高低，按看着舒服来**。
   名字叫 `EyeHeight` 但值是 0.75 会误导人（成年人眼睛哪有 0.75 米高）。

2. **旋转插值：C1 时我的判断是错的，这里改对了。**

   C1 我在 `CameraRig` 里写的是「朝向**不插值**，直接设。朝向插值会让鼠标感觉黏」。
   那是**过肩第三人称**下得出的结论——相机离角色 2.5 米，视角变化不刺眼，抖动不明显。

   **换到第一人称就站不住了：相机旋转就是画面旋转。** 而 `CameraSim` 每 tick 才推进一次
   （60Hz），渲染 100+ 帧，不插值的话画面每 16.7ms 跳一格。

   同一个「要不要插值」的问题，**换个视角就得重答一遍**——身体位置、靶子位置、相机位置、
   相机旋转，四个地方都问过这个问题，答案还不都一样。

3. **`Mathf.LerpAngle` 而不是 `Mathf.Lerp`。**

   Yaw 被 `Mathf.Repeat` 归一化在 `[0, 360)`，从 359° 变到 1° 时**数值上**是往回走 358°，
   但**角度上**是往前走 2°。`LerpAngle` 会走最短路径，`Lerp` 会让画面倒着转一圈。

4. **鼠标的延迟代价：多了 1 tick（约 16.7ms）。**

   这是渲染插值的固有代价，和 `PlayerView` 那边一样——只能在两个已知状态之间插值，
   不能预测还没算出来的。换来的是画面连续。

### 验证

**AI 侧（全部通过）**

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**；静态检查全绿；csproj 与磁盘一致；
  `Sim/` 无引擎符号；`View/` 里依赖 Boot 的仍只有 `DebugHud` / `HitboxGizmo` 两个调试工具。
- **离线行为测试 26 项全过**。核心是一条**对比模拟**：复刻 Unity 的真实时序
  （鼠标增量每渲染帧累加、相机 yaw 每 tick 消费、画面每渲染帧取一次角度），
  在 **100 fps 渲染 / 60 Hz tick / 鼠标 600 像素每秒**下跑 2 秒，比较逐帧角度变化：

  | | 每帧平均 | 单帧最大 | 峰值/均值 | **完全没动的帧** |
  |---|---|---|---|---|
  | 不插值 | 0.9000° | 1.8000° | **2.00** | **40.2%** |
  | 插值 | 0.9000° | 1.0800° | 1.20 | **0.0%** |

  - **台阶的本质是「有些帧完全没动，然后一下跳一大步」**——不插值时 40.2% 的帧是静止的，
    插值后一帧都没有。这比「峰值/均值」更能说明问题。
  - 单帧最大跳动降了 **40%**（1.8° → 1.08°）
  - **总转动量完全一致**（179.1001° vs 179.1001°）——插值只改变「怎么分配」，不改变「转了多少」
  - 插值后每帧平均步长 = 鼠标速度 × 灵敏度 / 帧率 = 0.9000° ✓ 对得上
  - `PreviousYaw`/`PreviousPitch` 行为正确：构造时等于当前值（否则第一帧会从 0 飞过来）；
    鼠标不动时两者完全相等（插值结果恒定，不会自己漂）
  - 插值结果始终落在 `[上一 tick, 这一 tick]` 之间（取 21 个 alpha 点验证）

**关键：这次写测试也踩了个坑**——第一版我断言「插值后峰值/均值 < 1.15」，
实测是 1.20，**没到**。查下来不是实现问题，是**阈值定得太紧**：
插值能抹平「台阶」，**抹不平「速度摆动」**（一 tick 摊到 1 个还是 2 个渲染帧，
取决于帧率相位，所以每次消费的鼠标量本身就不均匀）。
换成「完全没动的帧占比」这个指标才对得上现象。**残留的 1.20 倍速度摆动如实写进残留问题。**

**用户侧（Editor 实测）**：待做，见 `ROADMAP.md`。

### 残留问题

1. **残留速度摆动：插值后单帧最大跳动仍是平均值的 1.20 倍**（不插值时是 2.00 倍）。
   根因是**鼠标增量在渲染帧累积、在 tick 消费**——一 tick 摊到几个渲染帧取决于帧率相位，
   所以每次消费的量本身就不均匀。插值能把台阶抹平，抹不平这个。
   真要根治得让画面朝向直接吃「还没被 tick 消费掉的增量」，**那要动分层**
   （View 得读 `Inputs/`，或者 Boot 每帧搬运一次）。等这个 1.20 倍也被嫌抖了再说。
2. **同一件事在更深一层还有个隐患**：`CameraSim` 的 yaw 依赖「哪一帧的鼠标量归到哪一 tick」，
   而那取决于**渲染帧率**。两台帧率不同的机器跑同样的鼠标操作，yaw 序列会有微小差异
   → 移动方向有差异 → 位置发散。**这是联机（M7）要面对的问题**，现在单机无所谓。
3. **判定胶囊仍不在相机高度上**（差 0.75 米）。
4. **相机会穿墙**。

## 块 M3a — 顿帧（hitstop）

### 背景

M3（受击反应 / 顿帧 / 击退 / 无敌帧）里只有**顿帧**不需要敌人——玩家打靶子就能验。
另外三件都需要「有人能打玩家」，而那要等 M4（追击型 AI）。所以 M3 按路线**甲**劈成两半：
**M3a 顿帧（本块）→ M4 敌人 AI → M3b 受击反应**。

开工前用户定了四件事（见 `ROADMAP.md`「顿帧的设计问题」）：
判断放 **Boot**、相机**也冻**、**不上 `WorldSim`**、冻结期间的输入**丢位移、留按键**（方案 C''）。

### 做了什么

| 文件 | 改动 |
|---|---|
| `Sim/PlayerSim.cs` | `HitThisTick`：`Tick` 开头清零、`ResolveHits` 记下新目标时置真 |
| `Sim/AttackData.cs` | `HitStopTicks` 字段（**不进** `TotalTicks`） |
| `Data/AttackDataAsset.cs` | 默认 6 tick（0.1 秒）；`OnValidate` 对负值警告 |
| `Inputs/InputSampler.cs` | `DiscardLook()`：清掉攒下的鼠标位移余额，不交付 |
| `Boot/BattleLoop.cs` | `_hitStopTicksLeft`；冻结分支（`Advance` 之后、`BuildFrame` 之前）；tick 末尾装载 |
| `View/DebugHud.cs` | Player 行加 `stop N`（冻结期间从 6 数到 1） |

`BattleLoop.FixedUpdate` 的形状：

```csharp
SimClock.Advance();                  // 时钟照数，冻的只有模拟推进
if (_hitStopTicksLeft > 0) { _hitStopTicksLeft--; _inputSampler.DiscardLook(); return; }
LastFrame = _inputSampler.BuildFrame(SimClock.Tick);
Camera.Tick(...); Player.Tick(...); TargetSim.TickAll(...);
if (Player.HitThisTick && _attackData.Data.HitStopTicks > 0) { _hitStopTicksLeft = ...; }
```

### 关键设计选择

1. **`HitThisTick` 而不是 Boot diff `HitTargetCount`**：命中在三类输入的判据下是**事件**，
   用 diff 是把事件当状态轮询。这是「命中事件流（乙）」的最小版本——只回答「发生了没有」。
2. **装载点放 tick 末尾**：命中那一 tick 完整跑完（击退同 tick 生效），**下一 tick** 才开始冻。
   HUD 上 `stop` 因此比 `hit` 晚一 tick 出现。
3. **C''：丢位移、留按键**（用户选）。被否的 A 方案（`BuildFrame` 照跑、整个 frame 丢弃）
   会把冻结期间按下的键**消费给空气**——离线测试里有 A 的对照，实测确实如此。
   位移不丢的话，解冻第一帧要一次转 7px = **1.05°**（对照实测）；丢了之后单 tick 恒为 0.15°。
4. **只在中了的时候顿**：判定已经知道结果了；打空不冻。
5. **`HitStopTicks` 不进 `TotalTicks`**：顿帧不推进招式的 tick 时间轴（`StateTicks` 不动），
   只拉长真实时间。测试验证：带顿帧与不带顿帧两次跑，攻击都恰好占 **30 个执行 tick**。

### 验证

**AI 侧（全部通过）**

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**；三条静态检查全绿；无新文件（csproj 没动）。
- **离线行为测试 59 项全过**（临时工程，跑完即删）。10 组：命中信号语义 / 第二个目标中途进入 /
  冻结时长与时间轴 / C'' 投递 / A 方案对照 / 位移丢弃与猛跳 / 打空不顿 / 边界扫描 /
  `TotalTicks` / 连续两招各冻一次。

**这次测试抓到的是我的手算错误（3 处），不是实现错误**——0-based 计数的 off-by-one 第三次咬人：

1. 以为「攻击在执行 tick 29 回到 Idle」——实际 **30**（E29 是攻击的最后一 tick，
   「变回 Idle」是再后一拍）。改用不变量断言：「攻击占 30 个执行 tick = `TotalTicks`」。
2. 以为取消窗口首个 tick = `命中 + r + (后摇 − 3)`——实际是 **`− 2`**：
   后摇段首个 tick 才把 `StateTicks` 归 0，要到**第二次执行**才被读到。
   这条公式已经写进 `ROADMAP.md`，被测试扫出来后修正；重算后「后摇 + r ≤ 15 时保留按键才有意义」
   ——默认 16 下 A 与 C'' 行为相同，**C'' 的决定不受影响**。
3. 「第二招在窗口第一 tick 出现」期望 27 实际 **28**——同一个 off-by-one 的另一面。

**边界扫描**（把第 2 条变成实测数字）：命中固定在第 8 执行 tick（r=6），冻结期间按一下，
后摇 4~9 都能接上（后摇 9 时第二招正好在执行 tick 21 出现），**后摇 ≥10 接不上**。

**用户侧（Editor 实测）：验收通过**。

### 残留问题

1. **`HitThisTick` 在冻结期间停在 true**（`Tick` 不跑，没人清）。现在无害——消费它的
   `BattleLoop` 只在 `Tick` 跑过的 tick 上读。以后加别的消费者（事件流、表现层）要重新看这条。
2. **冻结期间 `LastFrame` 不更新**（C'' 的必然结果：不取帧）。HUD 的 `Frame` 行显示的是命中那 tick 的旧值。
3. **顿帧目前只有玩家命中会触发**（`HitThisTick` 是 `PlayerSim` 的）。敌人打中玩家要不要也顿、
   双方都顿还是只顿一边——M3b 定。
4. **`_hitStopTicksLeft` 是驱动层状态**：联机时「冻几 tick」需要和命中结果一起由服务器同步（M7）。

## 块 M3b — 受击反应（调试触发）

### 背景

路线调整（用户）：**不做敌人 AI，受击先做**——受击是「防守方」的属性，不需要攻击源；
「解耦复用代码很多部分」是以后的事（`ROADMAP.md` 记着）。触发源用 DebugHud 上的调试按钮替代，
M4 做完之后换成「敌人的判定帧调 `PlayerSim.ApplyHit`」，接口不用动。

### 做了什么

| 文件 | 改动 |
|---|---|
| `Sim/PlayerState.cs` | 新增状态 `Hitstun`（追加在最后，枚举值不动） |
| `Sim/SimConstants.cs` | `HitstunTicks = 12`、`InvincibilityTicks = 30`、`PlayerMaxHealth = 100` |
| `Sim/PlayerSim.cs` | `Health/MaxHealth`、`KnockbackVelocity`（独立通道）、`InvincibleTicksLeft`、`ApplyHit`（装填 pending）、`ConsumePendingHit`（Tick 开头结算）、转移里的受击分支、行为分支 + 击退积分 |
| `Boot/BattleLoop.cs` | 调试触发：`RequestDebugHit` + tick 侧消费（伤害/击退距离可在 Inspector 调） |
| `View/DebugHud.cs` | 左下角「受击测试」按钮；Player 行加 `hp` / `inv` / `推` |

### 关键设计选择

1. **`ApplyHit` 只装填、`Tick` 开头结算**（pending 槽）。调用方（现在调试触发、以后敌人判定帧）
   随便什么时候调，「状态转移只发生在 `Tick` 里」不破；受击第一 tick `StateTicks = 0` 对得上。
2. **受击优先、不评估原转移**：`if (ConsumePendingHit()) next = Hitstun; else switch(...)`。
   否决「先 switch 再覆盖 next」——那要记得清 `startsAttack` 之类的副作用，迟早漏。
3. **击退是独立通道**，绝不写 `Velocity`：`Velocity` 的唯一改写点仍是那条 `MoveTowards`，
   「相邻 tick 速度无突变」的不变量继续成立（离线测试全程盯着，max|Δv| ≤ 1.0）。
   位置允许突变——被打飞就是要突变。参数按**距离**反推（`v = sqrt(2ad)`），
   所以「推开 1 米」在玩家身上量出来和靶子一模一样：**1.0528 米**。
4. **无敌先递减、再消费**：于是「受击那 tick + 之后 29 tick」恰好免疫 30 tick，
   第 31 tick 起可以再被打（离线测试连打 100 tick，落点 E0 / E30 / E60 / E90）。
5. **硬直比滑行短**（12 < 19 tick）：玩家「在滑行中恢复控制」——两条通道解耦的自然结果。
   手感不对就调常量。
6. 签名和 `TargetSim.ApplyHit` 一致（damage + 击退初速度）。

### 验证

- `dotnet build Assembly-CSharp.csproj` → **0 警告 0 错误**；三条静态检查全绿；无新文件（csproj 没动）。
- **离线行为测试 68 项全过**（临时工程，跑完即删）。12 组：pending 语义 / 三个攻击段各打断一次 /
  判定被取消 / 击退距离与滑行 / 无敌 30 tick 边界 / 无敌期间完全无效 / 硬直时长与出口 /
  硬直期间按键边界 / 速度不变量 / 血量夹 0 / 无受击回归 / 同 tick 两次。
- **测试抓到 3 处我的期望值错**：断言「击退速度 == 给的值」，实际同一 tick 里已经衰减一步——
  和 `TargetSim.Tick` 的「先移动、再衰减」完全一致。修的是断言，不是实现；
  这个一致性反而是本块**最该钉住**的性质（玩家和靶子量出同一个 1.0528 就是它给的）。
- **用户侧（Editor 实测）：验收通过**。

### 残留问题

1. **硬直期间按攻击的边界**（实测）：受击那一 tick 按下的差 1 tick 过期（接不上）；
   晚一 tick 就能接上，出招落在 E13。要「按下就接上」得给硬直开取消窗口——没做（不加没被验证过的规则）。
2. **同一 tick 两次 `ApplyHit` 只有最后一次生效**（伤害不叠加、击退取后者）。
   单攻击源够用；多敌人时改成结算列表。
3. **「判定段不可打断」没做**：现在是全段打断（`PlayerState.AttackStartup` 注释里记着这个可调方向）。
4. **玩家死亡、受伤的表现**（闪色/音效）都没有；血量夹 0。
5. **受击不触发顿帧**（顿帧仍只由玩家的命中触发，M3a 的决定）——敌人打中玩家要不要顿，M4 试了手感再定。

## 块 P1 — 输入与角色解耦（ActorCommand）

### 背景

用户重新定了 M4 的前置方向（**不是**之前提的 HitResolver 提取）：
**先把「谁在操作」和「角色会做什么」拆开**——`PlayerSim` 只吃一个「操作意图」结构，
玩家输入只是这个结构的第一个生产者，AI 是第二个。原话：
「player 不管是玩家输入还是 ai 输入都不管，正常做出操作」。

### 做了什么

| 文件 | 改动 |
|---|---|
| `Sim/ActorCommand.cs` | **新增**：`MoveDirection`（世界空间，≤1）、`FacingYaw`、`Attack`；`FromPlayerInput(frame, cameraYaw)` 是玩家路径的解析器；`CameraRelativeDirection` 从 `PlayerSim` 搬进来（只换住处） |
| `Sim/PlayerSim.cs` | `Tick` 签名 `(InputFrame, cameraYaw, dt, …)` → `(ActorCommand, dt, …)`；朝向来源改成 `cmd.FacingYaw`；移动直接用 `cmd.MoveDirection`（+ 兜底 saturate）；**从此不认识 `InputFrame`、不认识相机** |
| `Boot/BattleLoop.cs` | 接缝那两行：`ActorCommand command = ActorCommand.FromPlayerInput(LastFrame, Camera.Yaw);` → `Player.Tick(command, …)` |
| `Assembly-CSharp.csproj` | 手工补 `ActorCommand.cs`（AGENTS.md 规矩） |

### 关键设计选择

1. **命令带世界空间方向**（不是「摇杆 + 参考朝向」）：AI 想「朝玩家走」时天然算出的就是世界方向，
   直接填；**相机这个概念彻底离开角色**。被否方案：带「摇杆量 + 参考朝向」——改动更小，
   但角色身上永远挂着一个「参考坐标系」，AI 还得把世界方向反拆成角度绕一圈。
2. **`ActorCommand` 和 `InputFrame` 是两种数据**：前者是**角色意图**（世界空间），后者是**设备输入**
   （含鼠标位移，相机的口粮 + 玩家路径的原料）。一帧一份 InputFrame，将来**每个角色各有一份** ActorCommand。
3. **不建 C# `interface` / 输入源抽象类**：第二个生产者（AI）还没出现，接口的形状该由它来定。
   现在的「接口」就是那个 struct + Boot 里那一行。
4. **角色侧做一次兜底 saturate**：生产者若给了长度 > 1 的方向（AI 容易犯），速度不能跟着翻倍——
   这类「静默失效」值得一行防御（和 switch 的 `default` 同一个理由）。
   玩家路径本来就压过一次，重复压在浮点误差内是幂等的。
5. **受击不走这个口子**：`ApplyHit` 不是输入、是外力，继续走自己的通道。
   AI 可以「命令」移动和攻击，不能「命令」自己挨不挨打。
6. 「朝向跟最近敌人」那条规则现在**属于生产者**（怎么填 `FacingYaw` 是 `FromPlayerInput` 的事），
   将来加在那边，角色一个字不用动。

### 验证

- `dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误（中间修过一次自己漏的 `using UnityEngine;`）；
  三条静态检查全绿；csproj 与磁盘对账一致（唯一差异是 Unity 模板自带的 `Readme.cs`，不归我们管）。
- **离线行为测试 46 项全过**（临时工程，跑完即删）。8 组：换算（yaw 0/90/180/270 × 前推/右推/斜向/
  半推/Attack 透传）/ 四向轨迹 / 斜向不超速 / 半推慢走 / 攻击与命中回归 /
  受击回归（硬直 12、无敌落点 E0·E30·E60·E90、击退 1.0528）/ 速度不变量 /
  **AI 路径**（手写命令：走、转向由命令决定、出招命中、长度 2 被压到 1、两条路径逐位一致）。
- 回归锚点全部复现：5.75 米、30 执行 tick、E8 命中、1.0528 米——**这次重构没有改变任何行为**。
- 「两条路径逐位一致」断言容差 1e-6，实测差 **0**。
- **用户侧（Editor 实测）：验收通过**（与块 P2a 合并跑一遍 Play——两块的运行时行为一次验完）。

### 残留问题

1. **相机仍是玩家专属**：AI 驱动的角色没有相机（不需要）。但若以后要让 AI 驱动**玩家角色本身**，
   相机得另找一个朝向来源——现在没这个需求。
2. 命令里没有「跳跃 / 闪避」——还没有这些操作，加了再说。
3. **多角色时 Boot 要按角色传各自的 `AttackData`**：`Tick` 已经从参数收它（形状就绪），
   但现在 `_attackData` 是全局一份。

## 块 P2a — 受害者换成角色（TargetSim → PlayerSim）

### 背景

用户定的方向：**用「第二个角色」彻底代替 TargetSim**——世界里只有一种可被打的东西：角色。
沙包（不能操作的第二个角色）先落地；以后 AI 来了，它只多一个「脑子」。
所以本块把「谁是受害者」从 `TargetSim[]`（球数组）换成 `PlayerSim[]`（角色数组）。
`TargetSim.cs` / `TargetView.cs` 的文件删除留到 P2b（**删文件要用户点头**）。

### 做了什么

| 文件 | 改动 |
|---|---|
| `Sim/PlayerSim.cs` | 加 `Radius`（被判定用的球半径，构造时给）；`Tick`/`ResolveHits` 的目标组 `TargetSim[]` → `PlayerSim[]`；判定 `ReferenceEquals` 跳过自己 |
| `Boot/BattleLoop.cs` | `_targets/_targetRadius/_targetMaxHealth` → `_otherActors/_actorRadius`；`_targetSims` → `_actors`（下标 0 = 玩家）；`BuildTargets` → `BuildActors`（建角色 + 绑 `PlayerView`）；新增 `TickOtherActors()`（喂 `default(ActorCommand)`）；`Targets` 属性 → `Actors` |
| `View/DebugHud.cs` | `Targets` 行 → `Actors` 行：血量 + **状态**（沙包那栏会出现 `Hitstun`）+ `推`；玩家不在这一行（它上面那行字段更全） |

### 关键设计选择

1. **判定目标数组含自己，靠 `ReferenceEquals` 跳过**：Boot 只维护一份「全体角色」数组
   （每个角色的 Tick 都收同一份），比给每人拼一份「除我之外」的数组省事且不会错。
   不跳过的话自己就在判定里——胶囊从身前 0.6 米开始、半径 0.5，正好够得着自己。
2. **推进顺序：玩家先、其他角色后**。玩家那一 tick 里做判定、给受害者装填受击；
   紧接着推进其他角色，它们**同一 tick** 吃到反应（进硬直、开始被推）——和靶子时代打击感一致。
3. **沙包 = 同一套受击规则**：硬直 12、无敌 30、击退 1.0528 米，全是 M3b 那套，一行没改。
   离线测试验证它的连打落点和玩家一模一样（E0/E30/E60/E90）。
4. **测试钉住一个新行为**：挥砍撞上无敌帧时，去重名单是在「几何重叠」那一刻记的，
   所以**这一刀的名额被消耗、后面几 tick 不重试**——第二刀完全打不进。
   这是 8b 去重设计的固有形状（攻击方不知道伤害落没落下），进残留问题。
5. **沙包的朝向固定 0**（default 命令）——「会面向你」是脑子的活，AI 来了再说。

### 验证

- `dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误；三条静态检查全绿。
- **离线行为测试 43 项全过**（临时工程，跑完即删，一次通过）。8 组：打沙包（掉血 / 硬直 /
  击退 1.0528 / 去重只扣一次）/ 不打自己 / 沙包无敌帧落点 / 挥砍撞无敌 / 沙包不操作 /
  一刀两个 / 顿帧联动（沙包和击退一起冻）/ 回归锚点（5.75 米、30 执行 tick、玩家受击落点）。
- 回归锚点全部复现——换受害者类型没有动到它们。
- **用户侧（Editor 实测）：验收通过**。

### 残留问题

1. **去重记在「几何重叠」而不是「伤害落下」**（见设计选择 4）。要改得让攻击方知道
   「打中了没有」——那是命中事件流 / `HitResolver` 形状的一部分，触发条件没到。
2. **沙包的血量/硬直/无敌还是全局常量**（和玩家共用）。「木桩模式」（0 硬直 0 无敌、刀刀算）
   要等这两个数搬成每个角色一份——真觉得无敌碍事时再做。
3. `TargetSim.cs` / `TargetView.cs` 现在是**死代码**（只有场景里的旧靶子还挂着 TargetView），
   等 P2b 清理。
4. 沙包被打会一直往外飘（每次 1 米），不会自己走回来——它是「没有脑子的角色」，正常。

## 块 P2b — 退役 TargetSim（收尾）

### 背景

P2a 之后 `TargetSim` / `TargetView` 成为死代码（判定目标组已经换成角色）。
用户点名删除——**删文件是红线，要用户逐个点名**：「收尾吧」这种说法不够
（权限分类器也拦了一道，要求文件被点名）。

### 做了什么

- 删除 `Sim/TargetSim.cs`（+ `.meta`）、`View/TargetView.cs`（+ `.meta`）
- `Assembly-CSharp.csproj` 去掉两条 `<Compile Include>`
- `PlayerSim` / `DebugHud` / `SimConstants` 里 10 处提到「靶子 / TargetSim」的注释清理掉
  （改成「对手 / 被推的物体」，或直接删掉历史对比句）
- **Editor 侧（用户做）**：3 个旧靶子身上的 `TargetView` 组件删掉（脚本没了会显示 Missing）

### 验证

- `dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误；三条静态检查全绿。
- **全工程 grep `TargetSim|TargetView` → 0 命中**——这次删除没有行为可测，
  「删干净、没有悬空引用」就是它全部的判据。
- csproj 与磁盘对账：双向都无差异。

### 残留问题

- 无。旧靶子的 `TargetView` 组件不清理也只是显示 Missing，不影响运行。

## 块 A1 — 动画驱动：状态机 → 骨架

### 背景

动画线的第一块（最小管线）。用户已导出三个 FBX：`Idle`（180 帧循环）、
`AttackRight`（0~29 帧）、`AttackLeft`（备用）——**Generic rig，AttackRight 的 30 帧
正好 = 攻击的 30 tick** ✓（fps 设 60、帧号 = tick 号的约定生效了）。

本块做代码侧：Sim 加「整招时间轴」的只读属性，View 加采样播放器 + 移动倾斜。

### 做了什么

| 文件 | 改动 |
|---|---|
| `Sim/AttackData.cs` | 加 `ActiveStartTick` / `RecoveryStartTick`（段起点在整招时间轴上的位置，和 `TotalTicks` 同一套「每段至少 1」规则）；**顺手修了 M3a 留下的注释残缺**——那次编辑把 `TotalTicks` 注释的头两行吃掉了，编译不受影响所以当时没暴露 |
| `Sim/PlayerSim.cs` | 加 `AttackElapsedTicks`（跨三段连读的整招进度）和 `CurrentAttackTotalTicks`——**相位算术只在 Sim 里算这一次**，View 不重推 |
| `View/ActorAnimation.cs`（新） | `Bind(PlayerSim)`；状态 → clip 映射（**Move 和 Idle 同一个 clip**）；每帧 `SampleAnimation(时间)`；时间全由 tick 数算；进攻击时对一次表（clip 长度 ≈ TotalTicks/60，容差 1.5 帧，不对就报警） |
| `View/PlayerView.cs` | 加「移动倾斜」（读 `Velocity`，前倾 / 后仰 / 侧倾，上限默认 12°）、`ModelPivot` 子物体、`VisualOffsetY`（「半离地」这类摆放偏移） |
| `Boot/BattleLoop.cs` | `BindAnimation`：玩家和其他角色装配时都绑（**有组件才绑**——还没换模型的方块角色可以不挂） |

### 关键设计选择

1. **`SampleAnimation` 而不是 Animator**：语义就是「按时间采样这个 clip」，和「Sim 是时间源」
   字面吻合；而且不需要 Controller 资产（少配一堆东西）。以后要做混合 / IK 再换 Animator——
   换的是 `ActorAnimation` 内部，对外只有 `Bind` + 每帧采样。另外，改 clip 长度不用重导 Controller。
2. **层级：角色根 → ModelPivot → FBX 实例**。采样会覆盖整棵子树（含 FBX 实例根自己的曲线），
   所以「我们的姿态」（倾斜、偏移）写在**它的父级**上，两边互不打架；
   yaw 和倾斜也因此拆成两个 transform，躲开欧拉顺序问题。
3. **时间全从 tick 数算**：顿帧 / 硬直时 `StateTicks` 不走 → 动画自动冻住。
   渲染插值沿用位置那一套 alpha，再往回退 1 tick——动画和位置显示同一个相位，
   否则 60Hz 的姿势在 100+ 帧下会比位置更跳。
4. **Move 不单独做动作**（用户定的）：和 Idle 同一个 clip，靠倾斜表现移动。
   驱动源用 `Velocity` 而不是输入方向：输入是突变的（按下瞬间 90°，倾斜会跳），
   `Velocity` 自带加减速 → 倾斜角连续长出来、松手自动回正。
5. **开机对表**：进攻击时查一次 `clip.length ≈ TotalTicks / 60`——
   「动画没照 AttackData 烤」这种错位肉眼几乎看不出来（差 2 tick 根本发现不了），用一行代码兜住。

### 验证

- `dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误；三条静态检查全绿；
  `View/` 不依赖 `Data/`（依赖表没变）。
- **离线测试 21 项全过**（临时工程，跑完即删）：整招时间轴恰好 0..29（每执行 tick 加一、
  无跳号无重复）、三个段的边界（0 / 8 / 14 / 29）、零长段不撞号（0 长的前摇占 1 tick，
  判定从 1 开始）、非攻击状态读到 0、阶段起点 / 总长对账。
- **用户侧（Editor 搭场景 + Play）：待做**，步骤见 `ROADMAP.md`。

### 残留问题

1. **倾斜的符号是手推的**（前倾 +X、右倒 -Z）——方向反了的话，改 `PlayerView` 里那两行的符号。
2. **三个 FBX 各带一份模型**（同一套骨架）。现在拖哪个都行；以后要做多套外观 / 装备时再统一。
3. clip 在 Unity 里都叫「骨架|骨架动作」（Blender 的默认命名），三个文件同名——可读性差，
   以后可以在各自 FBX 的 Animation 页签里改名（**代码按引用挂，不受影响**）。
4. **没有混合 / 过渡**（状态切换是硬切）——人偶阶段可以接受，动作多了再说。

## 块 A2 — 重力与落地

### 背景

用户定的方向：**角色落在地面，而不是固定高度**——顺便为跳跃铺路。
之前 `Position.y` 从出生起就冻着（`Velocity.y` 恒为 0）。本块把它变成真通道。

### 做了什么

| 文件 | 改动 |
|---|---|
| `Sim/SimConstants.cs` | `Gravity = 30`（游戏重力，不是 9.8——跳跃手感的第一旋钮）、`GroundY = 0`（世界是一块平地，先当常量） |
| `Sim/PlayerSim.cs` | 加 `HalfHeight`（中心到脚底，构造时给）和 `IsGrounded`；速度积分拆两条通道（水平照旧 `MoveTowards`、竖直走重力）；新增落地夹紧 |
| `Boot/BattleLoop.cs` | `_actorHalfHeight = 0.5` 传给每个角色 |
| `View/PlayerView.cs` | 模型竖直摆放改成**自动**：`pivot.y = -HalfHeight + _visualOffsetY`（模型原点在脚底 → 脚自动踩在 Sim 认的地面上；「半离地」这类调整加在 `_visualOffsetY`） |
| `View/DebugHud.cs` | 连续性判据**只盯水平通道**；Player 行离地时显示 `vy` 和「离地」 |

### 关键设计选择

1. **重力绝不能经过那条 `MoveTowards`**：老代码 `MoveTowards(Velocity, targetVelocity, …)` 是整个三维矢量，
   而目标速度的 y 恒为 0——等于给重力装了个阻尼（下落会被「拉回」）。
   所以积分拆两段：水平 `MoveTowards`、竖直 `-g·dt`，再合成。
2. **落地 = 每 tick 往下压一点、被夹回来**：脚底（`Position.y - HalfHeight`）碰到 `GroundY` 就停住，
   `vy` 清零、`IsGrounded = true`。站着时每 tick 都走这条路径，`IsGrounded` 因此恒真——不需要额外状态。
3. **HUD 的不变量改成分通道**：竖直速度**本来就会**设计内突变（落地 −12 → 0，以后起跳 +8 也是）。
   「相邻 tick 无突变」只对水平通道成立——离线测试两边都钉住了（落地 tick：|Δvy| > 10，|Δv水平| ≤ 1.0）。
4. **半隐式欧拉**（先重力后积分）：离散落点可精确预测——从 y=3 落到 y=0.5（2.5 米），
   位移 = n(n+1)/240，**n=24 时正好 2.5**，测试按这个数验。
5. **地面是常量**：`GroundY` 先放 SimConstants；以后有平台 / 台阶时变成世界数据（Boot 传进 `Tick`）。
6. **跳跃本身没做**——只铺路：竖直速度是真通道、`IsGrounded` 就位；
   以后起跳 = `if (IsGrounded && 跳跃键) Velocity.y = JumpSpeed`。

### 验证

- `dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误；静态检查全绿。
- **离线测试 29 项全过**（临时工程，跑完即删）：站着 y 恒等于半高（**精确相等**）、
  从 3 米落下约 24 tick 落地（22 tick 还在空中、26 tick 已落地）、出生在地面以下会被顶上来、
  空中水平照常加速（12 tick 达 6 m/s 且 vy = −6——两条通道互不干扰）、落地不打扰水平速度、
  落地 tick 的竖直突变 vs 水平不变量、攻击中重力照常（招式仍是 30 tick）、
  平地回归（5.75 米 / 硬直 12）。
- **测试里也踩了一次「三维速度 vs 水平速度」的坑**：断言写 `Velocity.magnitude` 期望 6，
  实际 8.49（水平 6 + 下落 6）——实现是对的，改看水平分量。这正是本块引入的新区分。

### 残留问题

1. **判定还是纯水平的**：胶囊和受害者球都以 `Position`（中心）为准，浮空时的高度差没进判定——
   平地阶段无所谓，跳跃上线时要回来处理（ROADMAP「判定胶囊不在相机高度」那笔账的延伸）。
2. **落地没有反馈**（落地硬直 / 挤压动画 / 音效）——需要时再说。
3. **掉落没有终点**：现在地面处处是平的，不存在「掉出世界」；有坑洞时要做杀伤平面。

## 块 A3 — 跳跃

### 背景

A2 刚把路铺好（竖直速度是真通道、`IsGrounded` 就位），本块做跳跃本身。

### 做了什么

| 文件 | 改动 |
|---|---|
| `Settings/PlayerActions.inputactions` | **补上 `Jump` 的动作定义**——绑定（空格 / 手柄 buttonSouth）本来就是裁模板时留下的**孤儿数据**（有绑定、没动作），补上定义就活了 |
| `Sim/InputFrame.cs` | 加 `Jump` 字段（事件；**锁存**，不进队列——同 tick 合并无损失） |
| `Inputs/InputSampler.cs` | 读 `Jump` 动作：`WasPressedThisFrame` 置位、`BuildFrame` 取走清零；`ResetState` 一并清 |
| `Sim/ActorCommand.cs` | 加 `Jump` 字段 + `FromPlayerInput` 透传 |
| `Sim/SimConstants.cs` | `JumpSpeed = 8`（配重力 30 → 高约 1 米、滞空约 0.5 秒） |
| `Sim/PlayerSim.cs` | 起跳：落地检查**之后**读 `IsGrounded`，`Velocity.y = JumpSpeed` 并立刻置 `IsGrounded = false` |

### 关键设计选择

1. **Jump 不进 `InputBuffer` 队列**：那个队列是为「连招意义上的多次按键」（两下 ≠ 一下）准备的；
   跳跃在同一 tick 里合并成一次**没有损失**（16ms 里按两下本来也只该跳一次）。
   所以锁存一个 bool 就够——和 Look 一个套路（取走清零），只是它是事件。
2. **起跳判断放在落地检查之后**：`IsGrounded` 是这一 tick 刚刷新的值（「现在还贴着地」）；
   起跳时顺手把它置假——从这一 tick 起就不再算站着。
3. **起跳门槛**：只有硬直中不能跳；空中的请求直接丢（没有二段跳）。
   （**原设计是「只在 Idle / Move 能跳」，本块验收前用户改成「攻击中也能跳」**——
   跳跃只推竖直通道、**不打断**招式，招式在空中继续挥完（「空中攻击」的雏形）。）
4. **跳跃 = 给竖直通道一个冲量**，水平速度原样保留——跑跳的抛物线是免费的（A2 拆通道的直接回报）。

### 验证

- `dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误；静态检查全绿。
- **离线测试 20 项全过**，手算数字全部命中：起跳 tick `vy` **精确等于 8**、峰值 **1.5**
  （半高 0.5 + 恰好 1.00 米）、**滞空 31 tick**（≈0.52 秒）、落地 `y` 精确回 0.5；
  空中每 tick 都按跳 → 80 tick 里只真正起跳 **3 次**（间隔 31，其余全被丢——「没有二段跳」的直接证据）；
  攻击中 vy 从不为正、硬直中同理，两者结束后立刻能跳；跑跳全程水平速度保持 6；落地后可立即再跳。
- **测试又踩了两个自己的坑**（都改断言，不是实现）：① 拿三维速度大小当水平速度（A2 也踩过）；
  ② 「数接地 tick」的度量和「落地即再跳」对不上——换成数「真正起跳的 tick」。

### 残留问题

1. **没有跳跃动画**：`ActorAnimation` 没有 jump clip，滞空时播的是 Idle。做一版 `Jump` 动作后
   加个映射即可（`_jumpClip` 字段 + 按 `IsGrounded` / `Velocity.y` 选）。
2. **跳跃输入不缓冲**：起跳前提前几 tick 按不会记住——攻击有 12 tick 预输入，跳跃没有。要的话照 `InputBuffer` 加。
3. **按住空格不会连跳**（`WasPressedThisFrame` 是按下沿）——有意为之，但验收时要说明。
4. **攻击中可跳但不打断招式**（跳跃取消是另一回事，留给 M6）。顺带的两个后果：
   ① 攻击状态把水平速度拉向 0 → 「跑着跳、中途出招」会在**空中刹车**、抛物线变陡；
   ② 空中挥砍的判定还是有的（胶囊跟着 Position 一起升，太高的目标会「打不着」——
   A2 记的「判定没进高度」那笔账）。

## 块 H1 — 命中由受害者消费（职责切分）

### 背景

用户提出的问题：「命中事件是不是应该放在被攻击者消费？」讨论后定的分工（2026-10-10）：

> **攻击名单由攻击者记（几何重叠就记）；命中结果由受害者判（收不收、以及收下后的
> 伤害/击退/顿帧）；被无敌挡掉 → 无事发生。**

### 做了什么

| 文件 | 改动 |
|---|---|
| `Sim/PlayerSim.cs` | `ApplyHit` → **`ReceiveHit`**（仍然只装填，但明确「不判、不设门」）；删掉攻击者侧的 `HitThisTick`，换成受害者侧的 **`WasHitThisTick`**（`ConsumePendingHit` 收下时置真、每 tick 开头清零）；`ResolveHits` 里名单照记（几何）、不再报命中 |
| `Boot/BattleLoop.cs` | 顿帧触发从 `Player.HitThisTick` 改成 **`AnyOtherActorWasHit()`**（遍历玩家以外的角色看 `WasHitThisTick`） |
| `Sim/PlayerState.cs` | Hitstun 注释里的入口改名 |

### 关键设计选择

1. **名单判几何、结果判收下——两本账分开**（用户定的）：
   攻击者的名单是「我的刀碰到过你」的记录（碰到就算用掉、这一刀不重试）；
   受害者的 `WasHitThisTick` 是「我真挨了」的记录（收下才记）。
   直接后果：撞无敌帧时——名单照记、血量不动、**不顿帧**。
2. **收不收的门在受害者的消费里**（不提前问）：所以无敌倒计时的递减位置**不用动**
   （讨论中设想的「挪到 tick 末尾」不需要了——没有任何人提前问，消费读到的就是最新值）。
3. **顿帧从「攻击者报命中」改成「受害者记账」**：语义更准（冻 = 真打中了），
   「打无敌目标不顿帧」成了自然结果而不是特判。过滤掉玩家自己（顿帧语义仍是
   「玩家的刀砍中了谁」——M3b 定的），调试按钮打玩家依旧不冻。
4. 这是 ROADMAP「命中事件流」的中间形态：攻击者不再自称「命中了」，只自称「碰到了」；
   「真的落下」这个事实的唯一作者是受害者。

### 验证

- `dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误；静态检查全绿；全工程无 `HitThisTick` 残留。
- **离线测试 30 项全过**：打沙包两边记账（名单 1 个、受害者 E8 记账、E9 清零）；
  **第二刀整段撞在无敌上——名单照记、血量不动、`WasHitThisTick` 零次、不顿帧**；
  无敌契约不变（连打落点仍 E0/E30/E60/E90）；玩家挨调试刀同样记账；
  一刀两个都记账；顿帧触发后玩家和沙包同冻同解；平地回归（5.75 / 30 tick / 硬直 12）。
- 测试写挂了一次编译：`InputFrame.Jump` 在 harness 里漏赋值（C# 结构体局部变量必须全字段赋值）。

### 残留问题

1. 「撞无敌帧浪费挥砍名额」（P2a 记的那条）**不再是问题，是设计**：名单按几何记（用户定的）。
2. 攻击者侧的 `HitThisTick` 删除——以后要「攻击者知道碰到了谁/被谁挡了」是命中事件流（乙）的活。
3. 多攻击者同 tick 打同一目标仍只有最后一次生效（pending 槽容量 1）——等第二个攻击者出现改队列。

## 块 D1 — 闪避

### 背景

用户定的下一个新动词。和跳跃不同，闪避是**新状态**（跳跃只推竖直通道、不进状态机）——
判准还是那句「新动词进状态机」。

### 做了什么

| 文件 | 改动 |
|---|---|
| `Settings/PlayerActions.inputactions` | 新增 `Dodge` 动作：**左 Shift** + 手柄 **B/Circle**（复用孤儿 `Sprint` / `Crouch` 的按键路径） |
| `Sim/InputFrame.cs` + `Inputs/InputSampler.cs` | `Dodge` 字段（锁存事件，和 Jump 同款）；`ResetState` 一并清 |
| `Sim/ActorCommand.cs` | `Dodge` 字段 + `FromPlayerInput` 透传 |
| `Sim/SimConstants.cs` | `DodgeTicks = 20`（0.33 秒，**全程无敌**）、`DodgeDistance = 3`（米） |
| `Sim/PlayerState.cs` | 新状态 `Dodge` |
| `Sim/PlayerSim.cs` | 转移：受击之后第二个「万能打断」（可从任何非硬直 / 非闪避状态进——**能取消招式**）；`StartDodge`（击退通道给冲量 + 无敌取 max）；`Dodge` 的计时退出；行为归「停下」组；**跳跃门加了「闪避中不能跳」** |

### 关键设计选择

1. **位移走击退通道**（不是自己的速度通道）：冲量按距离参数化（`v = sqrt(2ad)`，和击退同一套），
   所以「参数即结果」——填 3 米，实测滑 **3.09** 米。副作用：闪避结束后可能还在滑一小段
   （和受击滑行同一个模式），之后恢复控制。自己的 `Velocity` 照常归零（行为归「停下」组）。
2. **全程无敌，且取 `max(现有的, DodgeTicks)`**：不能直接赋值——无敌可能来自「刚受击」
   （30 tick 起步），闪避不该把它缩回 20。（当前节奏下这条路走不到——硬直 12 挡住了前 12 tick，
   到能闪时剩 18——但写 `max` 是零成本的正确。）
3. **优先级：挨打 > 闪避 > 其它**。同一 tick 又被打又按闪避，挨打赢——和受击打断的口径一致。
4. **能取消招式**（跳跃不能）——这是它作为新动词的份量所在。硬直中不能闪；闪避中再按不生效、
   **不会刷新时长**（狂按不能续无敌）。
5. **不设接地门槛**：空中也能闪（顺带的空中位移）。闪避期间不能跳。

### 验证

- `dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误；静态检查全绿。
- **离线测试 27 项全过**：冲 3.09 米（离散值手算命中）/ 状态恰好 20 tick /
  闪避期间连打 19 tick 一滴血不掉、`WasHitThisTick` 零次（**H1 的回报**）/
  方向（移动输入方向、无输入朝朝向、半推满距离）/ 打断攻击 / 硬直中进不去、结束后的下一 tick 能进 /
  闪避中跳跃键被吞、再按闪避不刷新 / 无敌取 max / 老锚点（5.75 米 / 30 tick / 跳跃峰值 1.5）。
- **测试又踩了三个自己的坑**（都修断言，不是实现）：① 把「1 米击退 ×1.0528」外推到 3 米——
  离散偏差和距离有关（3 米是 ×1.0305），`SimConstants` 的注释也跟着改了；
  ② 连打循环多跑一下、打在了闪避结束后的第一 tick（那一 tick 本来就该挨打）；
  ③ 半推那条忘了闪避后松手，多出来的 1 米是「闪避结束后继续走路」。
- **用户侧（Editor 实测）：验收通过**（2026-10-13）。

### 残留问题

1. **没有闪避动画**：`ActorAnimation` 的 default 分支把 `Dodge` 映射到 Idle ✓（看起来是滑步）。
2. **闪避结束后的滑行段没有无敌**——想延长的话是一个数的事，等实战手感说了算。
3. **空中闪避** = 纯水平位移（没有独立的空中闪避规则 / 竖直修正）——按「不设门槛」简化。
4. **没有冷却 / 次数限制**（可以无限连闪）——要加的话是状态机里一行（冷却计时）。

## 块 D2 — 完美闪避慢放

### 背景

用户定：**闪避中挨的刀被无敌挡掉 = 完美闪避 → 直接慢放**（不做特效、不做镜头，就是世界变慢）。
这正是 H1 分工表里预留的那个分支：`命中闪避 →（后续再加）`——现在加了。

### 做了什么

| 文件 | 改动 |
|---|---|
| `Sim/PlayerSim.cs` | 加 `DodgedHitThisTick`：`ConsumePendingHit` 里「无敌 **且状态是 `Dodge`**」时置真；每 tick 开头清零。**和 `WasHitThisTick` 互斥**：躲掉的不算挨了 |
| `Sim/SimConstants.cs` | `SlowMotionTicks = 120`（2 秒真实时间，世界走 1 秒）、`SlowMotionSkipEvery = 2`（半速） |
| `Boot/BattleLoop.cs` | `_slowMotionTicksLeft` + `FixedUpdate` 的**跳 tick** 分支 + 末尾的触发（只读**玩家**的 `DodgedHitThisTick`）；HUD 加 `slow{N}` |

### 关键设计选择

1. **机制 = 跳 tick，和顿帧同一套**（铁律 3 禁止 `Time.timeScale`）：每 2 个真实 tick 跳掉 1 个，
   世界以半速推进。倍率是常量，想调在一个地方。
2. **和顿帧的区别：不丢输入**。冻结期间是「不取帧 + 主动丢鼠标位移」；
   慢放期间**不取帧但什么都不丢**——按键留在队列、鼠标位移继续攒，下一个执行 tick 一起交付。
   所以**相机的转向速度不掉**（所有位移都会被消费），只是步子变成 30Hz 的。
3. **触发在受害者那边**（H1 的分工延续）：攻击者不参与——「躲掉了」是受害者自己的事实。
   只看玩家的完美闪避：敌人躲开你的刀不该给你慢放。
4. **重复触发 = 刷新时长**（不叠加）。
5. 已知的观感代价：慢放期间世界以 30Hz 的步子跳（渲染插值抹平一部分，但不完全丝滑）——
   「直接慢放就行」阶段接受；要更顺得让 View 知道当前的有效 tick 周期。

### 验证

- `dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误；静态检查全绿；
  全工程 `timeScale` 只出现在注释里（铁律 3 ✓）。
- **离线测试 23 项全过**（harness 复刻了 Boot 的跳 tick 循环）：完美闪避记账
  （且**受击后的无敌不算**——两本账分得清）/ 闪避结束后挨打照常 /
  **慢放 120 真实 tick 只执行 60 个**、同窗口内的位移 = 平时 30 执行 tick 的距离（半速的直接证据）/
  **慢放期间按键不丢** / 重复触发刷新 / 闪避与老锚点回归。
- **测试三次翻车全是时序模型错**（慢放让「真实 tick」和「执行 tick」分了家）：
  ① 触发当步还没开始倒数（差一）；② 在闪避没结束时按键（被缓冲后过期——实现是对的）；
  ③ **手动清锁存**：按键在一个「被跳过的 tick」上还没被取走就被测试擦了
  （真实 `InputSampler` 只在 `BuildFrame` 取走时才清——测试没复刻这条，加诊断打印才看清）。

### 残留问题

1. 慢放的 30Hz 步子（见设计选择 5）。
2. 声音 / 表现层不跟着慢（以后有音效再说）。
3. 慢放的触发依赖「有刀打向玩家」——现在只有调试按钮能制造，真正的实战验证要等敌人（M4）。

## 块 W1 — WorldSim 提取

### 背景

ROADMAP 在 M4 那节早就写着「加 AI 敌人时，这段会搬进一个统一推进所有实体的地方（WorldSim 之类）」——
本块就是那一步，提前在 M4 之前搬：M4 的「脑子」和「每角色一份 attackData」直接长进去。
动机两条：①「有哪些角色、谁先谁后」是**世界的知识**，住在 Boot 是历史遗留；
② 离线测试一直在**手工复刻** Boot 的循环，复刻漏一条语义就是一轮假通过 / 假失败
（D2 那次「手动清锁存」就是复刻漏了 `BuildFrame` 的语义）。

### 做了什么

| 文件 | 改动 |
|---|---|
| `Sim/WorldSim.cs`（新） | 实体表（`PlayerSim[]`，`[0]` = 玩家）+ `Tick(playerCommand, dt, attackData)`（玩家先、其他角色后）+ `AnyOtherActorWasHit()`。**零 `using`**（只做转发） |
| `Boot/BattleLoop.cs` | `_actors` → `_world`；`Player` / `Actors` 变转发属性；`BuildActors` → `BuildWorld`（翻译场景 + 装配 View + `new WorldSim(actors)`）；删掉 `TickOtherActors` / `AnyOtherActorWasHit`（搬走）；FixedUpdate 里那一大段推进变成 `_world.Tick(...)` 一行 |

### 关键设计选择

1. **它不拥有时钟**：顿帧 / 慢放的计数仍在 Boot——那是驱动层的时钟决策
   （M3a 定的，联机时由服务器同步）。WorldSim 只回答「给定玩家这一 tick 的意图，世界怎么往前迈一步」。
2. **`attackData` 仍由 `Tick` 参数传入**（每 tick 现读，Inspector 热改继续生效）；现在所有角色共用一份
   （和搬家前一模一样）。M4 给敌人自己的资产时再拆成每角色一份。
3. **数组所有权**：Boot 建、交进去、WorldSim 保管（下标 = 身份，判定名单靠它）——顺序稳定的约定不变。
4. 判定流程、`PlayerSim` 的签名**一个字没动**——这块只搬「编排」，不碰任何规则。

### 验证

- `dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误；静态检查全绿；`WorldSim.cs` 零 `using` ✓。
- **离线回归 22 项全过**，而且 harness 换了写法：**世界的推进用真的 `WorldSim`**
  （只复刻 Boot 的时钟骨架 + 输入侧）——这就是本块的收益，测试不再手工复刻那段循环。
  锚点全复现：推进顺序（沙包同 tick 进硬直）/ 走 5.75 米 / 攻击 30 执行 tick /
  顿帧 6（含沙包同冻）/ 硬直 12 / 无敌落点 E0·E30·E60·E90 / 跳跃峰值 1.5 /
  闪避 3.09 米 / 完美闪避 → 慢放半速。**逐位不变，搬家无回归。**
- 一处断言时机错（我的）：命中那一 tick 之后读 `AnyOtherActorWasHit` 当然为真——
  它要到下一 tick 才清（事件信号的语义）。

### 残留问题

无。M4 的脑子直接长在 `WorldSim.Tick` 那个 for 循环里（代码里留了注释位置）。

## 块 W2 — TimeControl 提取

### 背景

承接 W1 的同一种收益：顿帧 / 慢放的**计数 + 跳 tick 判断**从 `BattleLoop` 里收成一个
**纯 C# 类**（`Boot/TimeControl`），离线 harness 从此连时钟骨架都不用复刻。
（讨论里提的 H2——「顿帧时长随命中记录走」——用户说以后连同招式配置化一起处理，本块不含。）

### 做了什么

| 文件 | 改动 |
|---|---|
| `Boot/TimeControl.cs`（新） | `FreezeFor(ticks)` / `SlowFor(ticks)` / `Advance() → AdvanceKind { Run, Freeze, SlowSkip }`；只 `using ActionGame.Sim`（用 `SlowMotionSkipEvery`），不碰 UnityEngine |
| `Boot/BattleLoop.cs` | 两个计数器字段删掉 → `private readonly TimeControl _time`；`FixedUpdate` 里两段 if 变成 `switch (_time.Advance())`；触发改成 `_time.FreezeFor(...)` / `_time.SlowFor(...)`；HUD 两个属性转发 |

### 关键设计选择

1. **为什么还在 Boot，不搬进 Sim**：时钟快慢不是被模拟的世界状态——不参与确定性计算，
   联机时由服务器同步（M3a 定的）。Sim 产出「发生了什么」，TimeControl 决定「要不要停」。
2. **`AdvanceKind` 是枚举不是 bool**：两种「跳」对输入的处理**不一样**（顿帧丢位移、慢放不丢），
   让调用方用一个 `switch` 分清——把差异做进类型里，比两个 bool 参数不容易错。
3. **顿帧优先、且不递减慢放**：冻住时慢放的秒表也停（解冻后接着慢）——写进 `Advance` 的语义里。
4. **重复请求语义**：顿帧**覆盖**、慢放**刷新**（注释写明）。
5. 原 `BattleLoop` 里的长注释（A / C'' 取舍）浓缩进 `AdvanceKind` 的文档——单一出处。

### 验证

- `dotnet build Assembly-CSharp.csproj` → 0 警告 0 错误；静态检查全绿；无旧计数器残留。
- **离线测试 22 项全过（一次过）**：单元（顿帧计数 6 → 冻 6、慢放 60 跑 60 跳、
  顿帧优先且慢放秒表停、覆盖 / 刷新语义）+ 集成（**真的 TimeControl + 真的 WorldSim**：
  顿帧同冻同解、完美闪避 → 半速 60/120、走 5.75 米、攻击 30 执行 tick）。
- 这轮 harness 的 `Step` 只剩「调 `Time.Advance()`，按返回跳 / 跑」——**时钟骨架也不再复刻**。
- **用户侧（Editor 实测）：验收通过**（2026-10-13）——无可见变化，与验收过的行为一致。

### 残留问题

无。（H2「顿帧时长随命中记录走」记在 ROADMAP，等招式配置化时一起做。）

## 块 MV1 — 招式配置文件（动画进配置）

### 背景

用户定的方向：**把「一个招式」的全部内容收进一个配置文件**——手感数值、判定参数、动画。
性质是纯搬家（和 W1 / W2 同一级）：触发链路（输入 → 命令 → 状态机）一个字不动，行为零变化。
盘点后发现：数值（三段 / 伤害 / 击退距离 / 顿帧）和判定（胶囊三项）**本来就在**
`AttackData.asset` 里，真正住在文件外面的只有**动画**（挂在 `ActorAnimation` 的 `_attackClip` 上）。

讨论中澄清过两件事（用户问「动画写进配置文件会不会没法离线测试」「为什么不许」）：
文件里**可以**写（两半共存），不许的是进 **Sim 那一半**——clip 若进 `AttackData`，
harness 要多引 `AnimationModule.dll`、构造字面量里永远有个 null 字段，
而且等于把引擎资源变成模拟输入（重放 / 联机的「Tick 只依赖参数」就破了）。

### 做了什么

| 文件 | 改动 |
|---|---|
| `AGENTS.md`（先改） | 依赖表 `View → Data`：✗ → **限定 ✓**（只读表现资源）；动画节加「clip 引用住在招式文件里」；铁律 1 里 View / Data 两句同步 |
| `Data/AttackDataAsset.cs` | + `AnimationClip` 字段与属性（**永不进 Sim**）；类注释改成「一个招式的配置文件」 |
| `View/ActorAnimation.cs` | `_attackClip` → `AttackDataAsset _attackData` 引用；`ClipFor` 攻击三态改读资产；对表检查改读资产；缺资产警告一次 |
| `Dev.unity` + `AttackData.asset` | 用户侧接线：clip 进资产、两个角色拖上资产 |

### 关键设计选择

1. **clip 进文件、但不进 Sim 那一半**：文件里两半共存、走两条路——数值值拷贝给 Sim，
   clip 交给表现层。这是 7a 拆法（struct 纯数据 + ScriptableObject 包装）的直接延续。
2. **接线走 `View → Data` 限定边**（依赖表新增，先改的 AGENTS.md）：用户明确「不想在 Boot 里装配」
   ——Boot 的职责定义是「把 View 绑到 Sim 上」，转发 clip 超出这句；且 M4 每个敌人一套资产时
   会越走越重。能接 clip 的位置只有两个（Boot 转发 / View 直接引用），选后者；限定死：
   **只读表现资源**。对表校验正好演示分工：clip 长度问 Data（资源在那）、
   tick 数问 Sim（`CurrentAttackTotalTicks`，正在执行的是它）。
3. **对表校验留在运行时、只换来源**（我原本提议挪进资产 `OnValidate`，**收回**）：
   FBX 重新导出时 Unity 不会重跑引用它的资产的 OnValidate（只在资产加载 / 被改时跑），
   有漏报洞；运行时检查（进攻击时对一次）没有洞。不新增 OnValidate 版。
4. **警告的边界**：`_attackData` 没拖（配置错误）→ 出声一次；clip **留空**
   （「这招没动画」的正当配置）→ 不出声。
5. **Idle / Hitstun 不动**（角色级动画，不属于招式）；类名不改，等多招那步一起说。

### 验证

- `dotnet build` 0 警告 0 错误；静态检查全绿；`git diff --stat -- Scripts/Sim/` **为空**（Sim 逐字节未动）。
- 本块**没有离线行为测试可跑**：改动全在 ScriptableObject / MonoBehaviour 里，控制台进程造不出来
  ——兜底是「Sim 零改动」的静态证明 + 用户 Play。
- **用户侧（Editor 实测）：验收通过**（2026-10-13）——打一刀动画与之前一致，Console 无警告。

### 残留问题

1. 每个角色的 `ActorAnimation` 都要手动拖一次资产——M4 接敌人时别漏（漏了第一次出招会警告）。
2. 击退方向 / 竖直击退 / 取消窗口进文件 / H2 都在门外（见 ROADMAP）。

## 块 V1 — 速度通道分离

### 背景

用户拍板：「竖直通道和水平通道分离吧，确实需要这样的。」此前水平和竖直只是**约定**——
两套规则（自驱水平：MoveTowards + 无突变不变量；竖直：重力 + 允许突变）挤在同一个
`Vector3 Velocity` 里，「重力绝不能经过那条 MoveTowards」要靠注释拦住；冲量通道（原名
击退通道）也只靠「入口都抹 y」的纪律维持水平。这一块把约定升级成结构。

讨论中定死的两条（「用这条通道处理所有突变速度是否合理」那一轮的结论）：
- **冲量通道只收水平**；竖直的一切（跳跃、将来的挑空）走弹道通道——竖直击退的 y 分量
  直接写 `VerticalSpeed`（像跳跃，重力独占弧线），y=0 表示不碰（空中挨一刀不清零下坠速度）。
- **被否**：把跳放进冲量通道（上升同时吃 20 + 30 两套减速，跳高从 1.00 米掉到约 0.64 米，
  校准过的数字全作废）；把 `Velocity` 和冲量合并成一条（放弃无突变不变量和自驱/外力正交性）。

### 做了什么

| 文件 | 改动 |
|---|---|
| `Sim/PlayerSim.cs` | 公开字段 `Velocity` / `KnockbackVelocity` 拆成三个 `{ get; private set; }` 通道：`MoveVelocity`（自驱水平）/ `VerticalSpeed`（float 弹道）/ `ImpulseVelocity`（外力冲量）；位置积分不再需要「拆开 y 再装回去」；`KnockbackVelocityTowards` → `ImpulseVelocityTowards` |
| `Sim/SimConstants.cs` | `KnockbackDeceleration` → `ImpulseDeceleration`（值 20 不变） |
| `View/PlayerView.cs` | 倾斜改读 `MoveVelocity`（本来只用 x/z，数值不变） |
| `View/DebugHud.cs` | 连续性判据读 `MoveVelocity`（不再需要手动抹 y）；`vy` 行读 `VerticalSpeed`；两处「推」读 `ImpulseVelocity` |
| `Boot/BattleLoop.cs` | 调试受击的初速度用 `ImpulseDeceleration` |

### 关键设计选择

1. **三个通道、三个字段、写面收进 `Tick`**：y 恒 0 的两条 + 一个 float——「把重力写进水平」
   从「不许」变成「没地方写」。
2. **冲量通道改名**（Knockback → Impulse）：它同时承载受击击退和闪避位移，「击退」名不副实；
   而「这一刀的击退」（`ReceiveHit` 的参数名、`AttackData.KnockbackDistance`、
   `_pendingHitKnockback`）**保留原名**——那是概念，不是通道。
3. **不加聚合读口**：不再暴露「混合向量」`Velocity`——留着就是能踩回老坑的洞。

### 验证

- `dotnet build` 0 警告 0 错误；静态检查全绿；旧名字零残留。
- **离线 40 项全过**，锚点逐位不变：走 5.75000 / 滑 0.55000 / 跳跃 1.00000·滞空 31 /
  攻击 30 执行 tick（含空中挥完）/ 硬直 12 / 击退纯滑动 1.05278 / 闪避 20 tick·3.09162 /
  无敌 E0·E30·E60·E90（间隔 15 只落两刀）/ 顿帧 6 冻 6、慢放 60-120。
  **新增结构不变量**：两条水平通道 y 恒 0——逐 tick 覆盖行走 / 跳跃 / 击退 / 闪避 +
  600 tick 混合场景（双角色）。
- 测试自己踩了两个坑（都修断言，不是实现）：① 数「空中招式 30 tick」时把起跳那一 tick
  数了两遍；② 量纯滑动距离时没先让 `ReceiveHit` 被消费（pending 语义——**D2 踩过同一形状**）。
- **用户侧（Editor 实测）：验收通过**（2026-10-13）——无可见变化，与验收过的行为一致。

### 残留问题

1. 冲量「覆盖」语义（单 pending 槽）不变——多源合流（两把刀 / 冲刺中挨打）仍是
   「后写的赢」；改累积要做结算列表（记账）。
2. 通道不做碰撞（冲量直接加位置）——有墙之后要补位移求解，或接受穿墙。
3. 衰减率仍是世界常量（`ImpulseDeceleration = 20`）——重击 / 轻击要不同衰减时再进招式文件。


