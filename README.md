# Action Combat Lab

Unity 6 动作游戏学习项目。固定 **60 tick/秒** 的纯 C# 战斗模拟、每角色状态机、
韧性 / 破韧、锁定相机；Sim 与 View 严格分离。

目标是把单机动作战斗的核心做扎实，同时让模拟层保持可测试、
可回放，为以后的联机留路。

> 当前阶段：单人角色 + 静态沙包。敌人 AI、死亡、武器系统尚未实装。
> 详细状态见 [ROADMAP.md](ROADMAP.md)。

## 当前能玩什么

| 系统 | 内容 |
|---|---|
| 移动 / 跳跃 | 相机相对移动、加减速、重力与落地 |
| 攻击 | 左键出招；逐帧 hitbox 判定、预输入、收招取消 |
| 命中反馈 | 伤害、击退、顿帧、硬直、无敌帧、命中事件流 |
| 闪避 | 无敌帧；完美闪避触发半速慢放 |
| 韧性 | 削韧、回复延迟、破韧击倒、闪避消耗韧性 |
| 锁定 | 中键锁定 / 解锁；锁定转向限速、移动方向保持 |
| 调试 | 实时 HUD、命中事件 |

## 操作

| 操作 | 键位 |
|---|---|
| 移动 | WASD / 方向键 |
| 视角 | 鼠标 |
| 攻击 | 鼠标左键 / Enter |
| 跳跃 | Space |
| 闪避 | Left Shift |
| 锁定 / 解锁 | 鼠标中键 |
| 调试受击 | 左下角按钮（仅开发场景） |

Input Actions 里也配置了手柄绑定，但主要按键鼠验收。

## 架构

```
Assets/_Project/
  Scripts/
    Sim/        纯 C# 模拟层：状态机、纯数据、命中、判定、相机模拟
      Core/     PlayerSim / WorldSim / SimClock / SimConstants
      Data/     AttackData / CharacterData / GlobalData 等 struct
      Combat/   HitTest / HitEvent / HitResult
      States/   PlayerStateMachine + 6 个状态类
      Camera/   CameraSim / MoveDirectionResolver
    Data/       ScriptableObject 包装层（数值与表现资源）
    Inputs/     输入采集 → InputFrame
    Boot/       装配层：唯一 FixedUpdate，驱动 tick
    View/       表现层：读 Sim，写 Transform / 动画 / HUD
  Editor/       Editor-only 工具（Attack Hitbox Editor）
  Config/       ScriptableObject 资产实例
  Scenes/       Dev.unity
  Prefabs/
  FBX/
```

约束：

- **Sim 不持有任何 Unity 引擎对象**：没有 `MonoBehaviour`、`Transform`、`Time`、
  `Physics`、`Animator`；无状态数学类型（`Vector3` / `Quaternion` / `Mathf`）除外。
  模拟只读传入的 `dt`。
- **只有一个驱动点**：全项目只有 `Boot/BattleLoop.cs` 写 `FixedUpdate`。
- **数据单向流动**：资产 → Boot 每 tick 值拷贝 → Sim；View 只读 Sim。
- **动画由代码驱动**：状态机在 Sim，View 按 `State + StateTicks` 采样 clip，
  顿帧 / 硬直会自动冻住动画。


编辑器工具：`Tools > ActionGame > Attack Hitbox Editor`。

## 文档

- [AGENTS.md](AGENTS.md) — 分层、驱动、命名、验证等工作约定
- [ROADMAP.md](ROADMAP.md) — 已完成 / 进行中 / 待办
- [DEVLOG.md](DEVLOG.md) — 每块的过程、设计选择与验收记录
- [Docs/PoiseRules.md](Docs/PoiseRules.md) — 面向策划的韧性 / 破韧 / 闪避规则

## 当前限制

- 敌人 AI 未实装，沙包不会还手；受击测试走调试按钮。
- 死亡、武器系统、真霸体、多招式 / 连段未实装。
- 同一 tick 多个攻击源只结算最后一笔。
- 没有音效、特效和正式 UI；HUD 是调试用的。

## 说明

这是个人学习项目，代码与资产均为学习过程中的自建内容。
