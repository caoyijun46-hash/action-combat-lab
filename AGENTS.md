# 动作游戏学习项目 — 项目约定

## 项目概况

- Unity 6000.5.8f1（Unity 6），URP，3D。
- 单位用基础方块（Cube）拼；角色开始换成自建的简易人偶（Blender 出 FBX），**动画已开工**——纪律见「动画」一节。
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
      Sim/       # 纯 C# 模拟层（战斗逻辑、状态、判定、纯数据 struct）
      Data/      # ScriptableObject 资产定义（AttackDataAsset 等），只做「编辑 + 交值」
      Inputs/    # 输入采集 → InputFrame
      View/      # 表现层 MonoBehaviour（读 Sim，写 Transform/材质/Gizmo）
      Boot/      # 装配层：驱动 tick、把 Sim 和 View 接起来
    Editor/      # Editor-only 工具（窗口 / 手柄 / 资产编辑）；可引用 UnityEditor，不被运行时引用
    Scenes/      # Dev.unity 等开发场景
    Prefabs/     # 预制体
    Data/        # ScriptableObject 资产实例（.asset）
    Settings/    # 项目专属渲染/输入配置（暂空）
```

- 目录用 `Inputs/` 而不是 `Input/`，避免命名空间与 `UnityEngine.Input` 混淆。
- 一类一文件，文件名 = 类名。
- 根目录另有 `Docs/`：规则/设计文档（面向策划/玩家，中文；写数值、公式、边界、调参，不写代码架构），不参与 Unity 资产管线。
- 过程记录仍写 `DEVLOG.md`，状态写 `ROADMAP.md`；**改规则先改 `Docs/` 里的规则文档，再改代码/资产**。
- Unity 模板自带的 `Assets/TutorialInfo/`、`Assets/Scenes/SampleScene.unity`、`Assets/Settings/` **保持原样不动**。

## 架构铁律（review 时逐条检查）

### 1. Sim / View 分层

- `Scripts/Sim/` 是纯 C# 模拟层。**禁止**出现：`MonoBehaviour`、`ScriptableObject`、`Transform`、`GameObject`、`Input`/`InputSystem`、`Time.deltaTime`/`Time.fixedDeltaTime`、`Camera`、`Animator`、`Rigidbody`、`UnityEngine.Random`、`Physics`/`Collider`（查询见铁律 6，要经 Boot 层转交）。
- **`Vector3` / `Quaternion` / `Mathf` 例外放行。** 判准是「是否持有引擎状态或触发引擎行为」，不是「是否住在 `UnityEngine` 命名空间里」——这三个是无状态的数学值类型，安全。
  ⚠️ 实测补充（2026-10-13）：`Quaternion.Euler` 这类 **ECall**（打进 native 的方法）在离线 harness 里
  会抛 `SecurityException`（native 侧不在场）——Sim 里算旋转请沿用项目惯例的 **sin/cos 手算**
  （见 `PlayerSim.FacingDirection` / `ActorCommand.CameraRelativeDirection`），托管数学两边都能跑。
  代价：`Sim/` 因此要引用 `UnityEngine.dll`，**服务器只能是 Unity headless build，独立 .NET 服务跑不了**。
  真要换成独立服务时，`System.Numerics.Vector3` 是现成的替代品，属机械替换。
- **`ScriptableObject` 属于「持有引擎状态」，禁止。** 它是 `UnityEngine.Object`，有 native 对象、实例 ID、
  资源生命周期——和 `Vector3` 那类无状态值类型不是一回事。所以「数据资产」（`AttackData` 等）要拆成两半：

  | | 住哪 | 是什么 | 职责 |
  |---|---|---|---|
  | **纯数据** | `Scripts/Sim/` | `[System.Serializable]` 的 **`struct`** | 只有数字和派生只读属性 |
  | **资产包装** | `Scripts/Data/` | `ScriptableObject` | 在 Inspector 里编辑，把值交出去 |

  - `[System.Serializable]` 来自 **mscorlib 不是 `UnityEngine`**。加它只是为了让 Unity 能在 Inspector 里
    展开编辑，`Sim/` 里仍然没有 `using UnityEngine`，「不持有引擎状态」字面成立。
  - **必须是 `struct` 而不是 `class`**：Sim 拿到的是**值拷贝**，物理上不可能反向改到资产。
    `class` 会让 Sim 和资产共享同一个对象，一旦哪天 Sim 手滑写了字段，改的是磁盘上的资产。
  - 包装器只做一件事：`public AttackData Data => _data;`。不放任何逻辑。
- Sim 层的类**不继承 MonoBehaviour**，只持有数据、暴露 `Tick(dt)`。
- `Scripts/View/` 只读 Sim 状态（外加从 `Data/` 读表现资源，见依赖表）并写表现，**禁止**写战斗逻辑（伤害、状态切换、命中判定都归 Sim）。
- `Scripts/Boot/` 负责装配：创建 Sim 对象、驱动 tick、把 View 绑到 Sim 上。
- **允许的依赖方向**（`A → B` = A 可以 `using` B）：

  | 从 ↓ / 到 → | Sim | Data | Inputs | Boot | View |
  |---|---|---|---|---|---|
  | **Sim** | — | ✗ | ✗ | ✗ | ✗ |
  | **Data** | ✓ | — | ✗ | ✗ | ✗ |
  | **Inputs** | ✓ | ✗ | — | ✗ | ✗ |
  | **Boot** | ✓ | ✓ | ✓ | — | ✓ |
  | **View** | ✓ | ✓ | △ | △ | — |

  `△` = 只有调试工具（如 `DebugHud`）可以为了显示直接读，真正的游戏表现层不许依赖 `Inputs/` / `Boot/`。
  `View → Data` 那条 ✓ 是**限定的**（2026-10-13 加）：只读**表现资源**（招式 / 角色文件里的 `AnimationClip` 等）；
  游戏数值（时序 / 命中 / 状态）一律仍从 Sim 读。一句话：**数值问 Sim、资源问 Data**。
  `Sim/` 谁都不依赖，连 `using` 都不该有。改代码时如果发现要加一条表里没有的边，先回来改这张表。
  **`Data/` 是「Unity 认识、Sim 不认识」的那一层**：它 `using` Sim 的纯数据类型，把资产里的数字变成值拷贝交出去；表现资源（clip 等）也住在这里，由表现层直接读。

  **`Assets/_Project/Editor/`（Editor 层，2026-10-20 加）**：只由 Unity 的 Editor 程序集编译，
  可以引用全部运行时层 + `UnityEditor`；运行时层**永远不能**引用它（Unity 的 Editor 文件夹规则自动保证）。
  工具只**编辑数据资产**、复用 Sim 的公开数学（几何换算等），**不复制战斗逻辑**；
  数值仍然只有资产这一个来源。编辑资产走 `SerializedObject`，不给运行时加可变口子。
  命名空间用 **`ActionGame.EditorTools`**（不叫 `ActionGame.Editor`——会和 `UnityEditor.Editor` 类名打架）。

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
- 输入资产是 `Assets/_Project/Settings/PlayerActions.inputactions`（从模板裁剪而来，`Player` map 含 `Move`/`Look`/`Attack`/**`Jump`**/**`Dodge`**——`Jump` 和 `Dodge` 的动作定义分别是 2026-10-09 / 10-10 补的，绑定本来就存在（裁模板时留下的孤儿数据）；剩下 `Next`/`Sprint`/`Previous`/`Interact`/`Crouch` 的绑定仍是孤儿，在编辑器里保存一次会被清掉，不用管）。`Assets/InputSystem_Actions.inputactions` 是模板原件，保持原样、不被引用。
- Sim 层只认 `InputFrame`，不接触 Input System。
- 依赖方向单向：`Inputs/` → `Sim/`，反过来不允许。
- 输入缓冲队列**只能有一个消费者**（`BattleLoop`）。两个消费者会互相偷事件。

### 5. 确定性

- 模拟层用 `System.Random`（显式 seed），禁止 `UnityEngine.Random`。
- 遍历并修改状态的集合必须排序或用固定索引顺序。
- 浮点数不用作 `Dictionary` 的 key。
- 命中判定用**主动查询**，不用 Trigger / 碰撞回调（`OnTriggerEnter`、`OnCollisionEnter` 一律不用）。
- **查询优先用 `Sim/` 自己算的纯数学**（球 / 胶囊 / 盒之间的距离比较），而不是 `Physics.OverlapBox`。
  三个理由：
  1. **确定性**。PhysX 的浮点结果跨平台不保证一致，而 `Sim/` 要能回放和联机——
     同一串输入必须得到同一结果，不能押在一个不保证确定的子系统上。
  2. **不用为每 tick 穿一层引擎通道**。`Sim/` 不能碰 `Physics`（铁律 6），
     用 `Physics.OverlapBox` 就得设计一层每 tick 调用的转交接口；
     纯数学只需要**启动时把场景几何搬一次**成纯数据。
  3. **够用**。本项目的几何全是基础方块，球 / 胶囊 / 盒的距离判定覆盖得了。
- **什么时候才该换成 `Physics.OverlapBox`**：几何变成导入的网格或地形、
  或物体数量大到需要物理引擎的空间划分时。那时由 Boot 层提供查询接口，
  并且要接受确定性上的代价。

### 6. 物理的使用边界

- 物理引擎只做**查询**（射线、重叠、`CharacterController.Move`）。
- 角色移动是运动学的，**不用 `Rigidbody` 的力或速度驱动**。

## 动画（2026-10-08 开工）

用户开始做角色动画。四条纪律：

1. **动画是 View 层的事，不进 `Sim/`**：Sim 不知道有没有模型，它只管状态和 tick 数。
2. **动画不驱动游戏逻辑**：判定 / 伤害 / 位移全在 Sim 做完；Animation Event 只允许做纯表现。
   动作的时间轴由 `AttackData.TotalTicks` 说了算，**动画照着它烤**（当前 30 tick = 0.5 秒）；
   判定窗口由 hitbox 数据的 tick 区间另行定义（块 T1 起，不再有前摇 / 判定 / 后摇三段）。
3. **播放由代码驱动**：Animator 只当播放器（代码按 `State` + `StateTicks` 设播放位置），
   不让 Animator Controller 自己跑转移——否则会出现两套状态机互相追；顿帧 / 硬直也因此**自动冻住动画**。
4. **静态摆放放 View 的常量偏移**（如「半离地」），不烘进模型、不烘进动画；
   动画里也不做水平根位移——要位移先在 Sim 里加。

**clip 引用住在数据文件里**（2026-10-13 起，2026-10-15 补全）：攻击动画写在招式配置文件
（`Data/AttackDataAsset`）里，`Idle` / `Hitstun` 等**角色级**动画写在角色配置文件
（`Data/CharacterDataAsset`）里——表现层（`ActorAnimation`）直接读，依赖表里
`View → Data` 那条限定边就是它。对表校验的两头分工：clip 长度问 Data（资源在那），
tick 数问 Sim（正在执行的是它）。

模型管线：Blender 出 FBX，**模型底部贴 Z=0、原点在底部**；导出时勾 **Bake Animation**——
实测：不勾朝向会反 180°，勾了就正（2026-10-09，用户实测）。

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
- **离线行为测试（验证纯逻辑，不依赖 Editor）**：在 `%TEMP%` 下建临时 dotnet 控制台工程，
  把要测的 `.cs` 拷进去，写断言跑 `dotnet run`。**跑完即删，不留档**（用户明确要求）。
  - 目标代码不碰 Unity 类型时（如 `InputBuffer`）：直接拷文件即可。
  - 目标代码用了 `Vector3`/`Mathf` 时：csproj 里加
    `<Reference Include="UnityEngine.CoreModule"><HintPath>C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Data\Managed\UnityEngine\UnityEngine.CoreModule.dll</HintPath><Private>true</Private></Reference>`
    **引用真的 Unity DLL 而不是自己写替身**，否则验的是替身的行为，没有意义。
  - 这套方法在块 2a（InputBuffer）和 3a（PlayerSim）各用过一次，都抓到过真问题。
  - 断言要写成**可证伪的不变式**（如「消费 + 积压 == 按键」），别写「看起来对」。
- **接线核对：读场景文件，不要让用户截图。** AI 不能点编辑器，但 `Dev.unity` 是 YAML，
  可以直接解析。做法：用 Python 提取 GameObject 名字表 + 各 MonoBehaviour 的脚本 guid
  （对照 `*.cs.meta`）+ `_xxx:` 字段，然后把每个 fileID 解析回它指向的组件与所属对象。
  块 3c 用这方法确认过 `BattleLoop` 的三个引用和 `DebugHud` 的两个引用全部正确。
  比截图快、而且不会漏看。
- 手感、画面、帧率这类只能由用户在 Editor 里实测，验收标准见 `ROADMAP.md`。

## 版本控制

- Git 仓库在工程根目录，主分支 `main`。**不做 push**（红线），远端由用户自己决定。
- **提交时机：每完成一块、且该块验收通过后提交一次。** 没验收就提交等于把半成品写进历史。
- 提交信息用中文说清「做了什么 + 为什么」，结尾附 `Co-Authored-By: Claude Code <noreply@anthropic.com>`。
- `.gitignore` 忽略 Unity 生成物：`Library/`、`Temp/`、`Logs/`、`UserSettings/`、`obj/`，
  以及生成的 `*.csproj` / `*.slnx`。
- ⚠️ **副作用：`dotnet build Assembly-CSharp.csproj` 依赖 Unity 生成的 csproj。**
  新克隆、或手动删掉 csproj 之后，必须先让 Unity 打开一次工程把它生成出来，才能做编译校验；
  在那之前只能做静态检查。这也是「新增 `.cs` 要手动补进 csproj」那条规则的来源。
- Windows 上 `core.autocrlf=true`（LF 存储、CRLF 检出）。提交时刷一堆换行符警告是正常的，不用管。

## 红线

- 不删除任何文件/目录（含 `Assets/TutorialInfo/`、`SampleScene.unity`）。
- 不改 `ProjectSettings/` 下除 `Time.fixedDeltaTime` 以外的设置；需要时先讨论。
- 不新增 Package（Cinemachine、Netcode for GameObjects 等）之前先讨论。
- 改约定时：**先改本文件，再改代码。**
