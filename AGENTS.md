# 动作游戏学习项目 — 项目约定

## 项目概况

- Unity 6000.5.8f1（Unity 6），URP，3D。
- 单位用基础方块（Cube）拼，暂不做美术资源、不做动画。
- 目标：先把单机战斗核心做扎实，同时让模拟层保持「联机友好」；联机的**实现**推迟到 M5 之后再谈。
- 这是**学习项目**，不是交付项目。逐层理解优先于尽快跑起来。

## 协作模式（本项目最重要的约定）

采用「AI 分块实现 + 实现后讲解」：

- `Assets/_Project/Scripts/` 下的实现代码由 **AI 编写**。
- **一次只做一块**：一个类、或一个可独立验收的小功能。做完就停，等用户说「继续」再做下一块。宁小块多轮，不要一大坨。
- 每块做完 AI 必须讲解，讲完**等用户提问**，不要连着做下一块：
  1. 这块代码在架构里的位置——谁调用它、它调用谁、数据往哪个方向流；
  2. 每个设计选择的理由，以及被否掉的其他方案；
  3. 涉及的 C# / Unity 知识点；
  4. 这一块怎么验收（具体到按什么键、看哪个数字）。
- 用户负责：在 Unity 里实测、跑验收、反馈手感与报错。
- **每一步必须有可测的验收标准，跑通再进下一步。** 没有验收通过就往下走，等于把 bug 埋进地基。

## 实现记录

上下文会丢，代码不会自己解释自己。所以每块完成后：

- 在根目录 `DEVLOG.md` 追加一节：块编号与标题、实现了什么、关键设计选择及理由、验收结果、已知残留问题。
- 分工：`ROADMAP.md` 记录**状态**（⬜/🔵/✅），`DEVLOG.md` 记录**过程与理由**。
- 新会话恢复上下文的顺序：`AGENTS.md` → `ROADMAP.md` → `DEVLOG.md`。

## 目录约定

所有自建内容放在 `Assets/_Project/` 下，`Assets/` 根目录保持干净。

```
Assets/
  _Project/
    Scripts/
      Sim/       # 纯 C# 模拟层（战斗逻辑、状态、判定、数据）
      Sim/Data/  # ScriptableObject 定义（AttackData 等）
      Inputs/    # 输入采集 → InputFrame
      View/      # 表现层 MonoBehaviour（读 Sim，写 Transform/材质/Gizmo）
      Boot/      # 装配层：驱动 tick、把 Sim 和 View 接起来
    Scenes/      # Dev.unity 等开发场景
    Prefabs/     # 预制体
    Data/        # ScriptableObject 资产实例（.asset）
    Settings/    # 项目专属渲染/输入配置（暂空）
```

- 目录用 `Inputs/` 而不是 `Input/`，避免命名空间与 `UnityEngine.Input` 混淆。
- 一类一文件，文件名 = 类名。
- Unity 模板自带的 `Assets/TutorialInfo/`、`Assets/Scenes/SampleScene.unity`、`Assets/Settings/` **保持原样不动**。

## 架构铁律（review 时逐条检查）

### 1. Sim / View 分层

- `Scripts/Sim/` 是纯 C# 模拟层。**禁止**出现：`MonoBehaviour`、`Transform`、`GameObject`、`Input`/`InputSystem`、`Time.deltaTime`/`Time.fixedDeltaTime`、`Camera`、`Animator`、`Rigidbody`、`UnityEngine.Random`、`Physics`/`Collider`（查询见铁律 6，要经 Boot 层转交）。
- **`Vector3` / `Quaternion` / `Mathf` 例外放行。** 判准是「是否持有引擎状态或触发引擎行为」，不是「是否住在 `UnityEngine` 命名空间里」——这三个是无状态的数学值类型，安全。
  代价：`Sim/` 因此要引用 `UnityEngine.dll`，**服务器只能是 Unity headless build，独立 .NET 服务跑不了**。
  真要换成独立服务时，`System.Numerics.Vector3` 是现成的替代品，属机械替换。
- Sim 层的类**不继承 MonoBehaviour**，只持有数据、暴露 `Tick(dt)`。
- `Scripts/View/` 只读 Sim 状态并写表现，**禁止**写战斗逻辑（伤害、状态切换、命中判定都归 Sim）。
- `Scripts/Boot/` 负责装配：创建 Sim 对象、驱动 tick、把 View 绑到 Sim 上。
- **允许的依赖方向**（`A → B` = A 可以 `using` B）：

  | 从 ↓ / 到 → | Sim | Inputs | Boot | View |
  |---|---|---|---|---|
  | **Sim** | — | ✗ | ✗ | ✗ |
  | **Inputs** | ✓ | — | ✗ | ✗ |
  | **Boot** | ✓ | ✓ | — | ✓ |
  | **View** | ✓ | △ | △ | — |

  `△` = 只有调试工具（如 `DebugHud`）可以为了显示直接读，真正的游戏表现层不许依赖 `Inputs/` / `Boot/`。
  `Sim/` 谁都不依赖，连 `using` 都不该有。改代码时如果发现要加一条表里没有的边，先回来改这张表。

这一条是整个项目最值钱的约定。它同时决定了以后能不能回放、能不能联机、能不能在服务器上跑同一套逻辑。

### 2. 单一驱动点

- 全项目**只有** `Boot/BattleLoop.cs` 写 `FixedUpdate`。
- 其他需要按 tick 响应的对象实现 `ITickable`，由 `BattleLoop` 统一调用（Step 4 引入）。
- 不准出现第二个 `FixedUpdate`，也不准在 `Update` 里改模拟状态。
- **唯一例外**：`Inputs/InputSampler.cs` 可以在 `Update` 里写输入缓冲。输入缓冲不属于模拟状态——它是「渲染帧采集 → tick 消费」的传送带，联机时天然属于客户端。

### 3. 时间

- 模拟频率固定 **60 tick/秒**，定义在 `Sim/SimConstants.cs`。
- 模拟逻辑的时间只来自传入的 `dt` 参数，绝不读 `Time.xxx`。
- **禁止用 `Time.timeScale` 做顿帧或慢动作**（它会连带改变固定步长与物理，联机时更会污染别人的世界）。顿帧在 M3 单独设计。

### 4. 输入

- 输入只在 `Scripts/Inputs/` 采样，产物是 `InputFrame` 结构体。
- **边沿检测（刚按下）只在渲染帧 `Update` 里做**，压进缓冲队列；模拟层按 tick 从队列消费。
- **三类输入用三种传递方式，不许混**：
  - **状态**（Move、键的持续按住）：不缓冲，每 tick 直接读当前值。时长由 tick 次数体现，值只回答「现在朝哪」。
  - **增量**（Look 的鼠标 delta）：在 `Update` 里**累加**到余额，tick 一次性用掉并清零。可加，所以用加法不用队列。
  - **离散事件**（Attack / Jump 的按下瞬间）：进 `InputBuffer` **队列**，每 tick 消费一个。不可加，按 2 次 ≠ 1 次的双倍。
  - 判据：**这一帧的值没被消费，下一帧读还能补回来吗？** 能 → 状态；不能且可加 → 增量；不能且不可加 → 事件。
- 输入资产是 `Assets/_Project/Settings/PlayerActions.inputactions`（从模板裁剪而来，只含 `Player` map 的 `Move`/`Look`/`Attack`）。`Assets/InputSystem_Actions.inputactions` 是模板原件，保持原样、不被引用。
- Sim 层只认 `InputFrame`，不接触 Input System。
- 依赖方向单向：`Inputs/` → `Sim/`，反过来不允许。
- 输入缓冲队列**只能有一个消费者**（`BattleLoop`）。两个消费者会互相偷事件。

### 5. 确定性

- 模拟层用 `System.Random`（显式 seed），禁止 `UnityEngine.Random`。
- 遍历并修改状态的集合必须排序或用固定索引顺序。
- 浮点数不用作 `Dictionary` 的 key。
- 命中判定用主动查询（`Physics.OverlapBox` 等），不用 Trigger 回调。

### 6. 物理的使用边界

- 物理引擎只做**查询**（射线、重叠、`CharacterController.Move`）。
- 角色移动是运动学的，**不用 `Rigidbody` 的力或速度驱动**。

## 命名约定

- 私有字段 `_camelCase`，公开属性/方法 `PascalCase`，常量 `PascalCase`。
- 命名空间与目录一致：`ActionGame.Sim`、`ActionGame.Sim.Data`、`ActionGame.Inputs`、`ActionGame.View`、`ActionGame.Boot`。
- 结构体用 `struct`，纯数据类用 `class`；接口以 `I` 开头。

## 验证方式

- 本机 bash 可用（Git Bash），AI 能做静态检查与文件读写。
- Unity 装在 `C:/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe`。
- **编译校验（已验证可用）**：跑
  `dotnet build Assembly-CSharp.csproj -v quiet --nologo`
  用的是 Unity 自己生成的 csproj——程序集引用、预处理宏、`LangVersion 9.0` 都与 Unity 一致，
  所以结果等价于 Unity 编译。**Editor 开着也能跑**，约 5 秒。这是动 Editor 之前的第一道关。
  - 前提：新加的 `.cs` 必须已在 csproj 的 `<Compile Include>` 列表里。新增文件后 csproj 可能要
    先切回 Unity 焦点触发一次重新生成，否则会漏编译（漏了会静默通过，最危险）。
  - 会往 `Temp/obj`、`Temp/bin` 写东西；避开 Unity 正在编译的时刻，防止撞文件锁。
- **编译校验（备用，未实测）**：Editor 关闭时可跑
  `"C:/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Unity.exe" -batchmode -quit -projectPath "D:/Unity Projects/动作" -logFile -`
  从输出里读编译错误。
- AI 每次都做的静态检查：
  ```
  grep -rnE '\b(MonoBehaviour|Transform|GameObject|Camera|Animator|Rigidbody|Collider|Physics|InputSystem|UnityEngine\.Random|Time\.)\b' Assets/_Project/Scripts/Sim/ | grep -v '///'
  grep -rn "void FixedUpdate" Assets/_Project/Scripts/          # 应只有 Boot/BattleLoop.cs 一处
  grep -rn "ActionGame.View" Assets/_Project/Scripts/Inputs/    # 应为空
  ```
  `\b` 词边界是为了让 `CameraRelativeDirection` 这类名字不误报；`grep -v '///'` 是为了不看注释里的说明文字。
- **新增 `.cs` 文件后，必须手动补进 `Assembly-CSharp.csproj` 的 `<Compile Include>`**，否则 `dotnet build` 会**静默漏编译**——比报错更危险。Unity 重新获得焦点时生成的 csproj 会自动带上，但在那之前得手动加。
- 手感、画面、帧率这类只能由用户在 Editor 里实测，验收标准见 `ROADMAP.md`。

## 红线

- 不删除任何文件/目录（含 `Assets/TutorialInfo/`、`SampleScene.unity`）。
- 不改 `ProjectSettings/` 下除 `Time.fixedDeltaTime` 以外的设置；需要时先讨论。
- 不新增 Package（Cinemachine、Netcode for GameObjects 等）之前先讨论。
- 改约定时：**先改本文件，再改代码。**
