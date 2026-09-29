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
| 5 | 状态机（先 switch，后重构） | ⬜ | Idle ↔ Move 正确切换，切换瞬间速度连续无突变 |
| 6 | 输入缓冲消费 + 攻击触发 | ⬜ | 收招最后 3 tick 内按攻击能接上；快速连按不会一击触发两次 |
| 7 | AttackData（ScriptableObject）三段帧 | ⬜ | 改 Inspector 里的前摇/判定/后摇数字，手感立刻变化，无需改代码 |
| 8 | 判定 + Gizmo + 靶子 + 命中记录 | ⬜ | 红盒在判定帧出现、命中变绿；靶子后退掉血；同一招式不重复命中同一目标 |

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
