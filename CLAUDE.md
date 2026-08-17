# CLAUDE.md

本文件为 Claude Code 提供项目上下文，用以在本仓库中高效协作。

---

## 项目定位

**当前身份**：Unity 2022.3.62f3c1 LTS 的 URP + Netcode for GameObjects 项目。**原为**基于 KitchenChaos 教程的胡闹厨房复刻，**正在改造为**一款派对竞技游戏 **《Bug 海岛捕鱼赛》**。

**核心设计文档**（阅读顺序）：
1. `Assets/Docs/脑暴产出文档.md` — 策划最初脑暴
2. `Assets/Docs/游戏设计文档.md` — 正式设计（v0.1，权威）
3. `Assets/Docs/美术需求文档.md` — 美术交付清单（Demo 版）

> **玩法一句话**：2-4 人俯视角海战，3 分钟内在共享捕鱼点抢鱼、回自己岛屿卸货、可偷别人的鱼，末 30s 中央刷金色鱼进入狂暴期。分数 = 岛上普通鱼×1 + 金色鱼×2。

---

## 技术栈

| 项 | 版本/名称 |
|---|---|
| Unity | 2022.3.62f3c1 LTS |
| 渲染 | URP 14.0.12 |
| 联机 | Netcode for GameObjects 1.12.2 + Relay/Lobby/Authentication |
| 输入 | Input System 1.14.0（`Assets/Scripts/Player/playerInputActions.inputactions`）|
| 多人开发 | ParrelSync（`Assets/Plugins/ParrelSync/`）|
| 调试台 | Quantum Console（`Assets/Plugins/QFSW/`）|
| 字体 | ICE SDF（**Git LFS 管理**，130MB） |
| Shell | bash（Windows 11）— 用 Unix 语法、正斜杠 |

**首次拉取必须**：`git lfs install && git lfs pull`，否则 UI 字体缺失。

---

## 目录约定

```
Assets/
├── Docs/               ← 设计文档，改动前先读
├── Scenes/             ← MainMenu / CharacterSelect / Lobby / Loading / GameScene
├── Scripts/            ← 现有代码（KitchenChaos 遗留 + 新玩法）
│   ├── Player/         ← 玩家控制、动画、输入
│   ├── Counter/        ← 旧胡闹厨房柜台（将逐步废弃/复用）
│   ├── Manager/        ← GameManager / SoundManager / MusicManager 等单例
│   └── SriptableObjects/  ← SO 数据（typo 保留，跟着现有拼写走）
├── Prefabs/            ← Player / KitchenObjects / UI / Counters
├── ScriptableObjects/  ← SO 实例
├── _Assets/            ← 美术资源（Meshes/Textures/Materials/Animations/Sounds）
├── MyAssets/           ← 自制 Player 相关（Idle/Wake/Animator）
├── StarterAssets/      ← Unity 官方 URP 模板残留
├── Plugins/            ← ParrelSync + QFSW Quantum Console
├── Settings/           ← URP 渲染管线配置
└── 软连接脚本/          ← 保留原样，不动
```

**新增派对游戏代码时**：新建 `Assets/Scripts/PartyGame/` 子目录，与遗留厨房代码物理隔离。SO 放 `Assets/ScriptableObjects/PartyGame/`。

---

## 现有可复用的关键脚本 / 系统

改造派对游戏时**优先复用**以下模块，避免重造：

| 现有脚本 / 系统 | 派对游戏中的用途 |
|---|---|
| `Assets/Scripts/GameInput.cs` | 输入抽象层，扩展新操作时在这加 |
| `Assets/Scripts/Player/Player.cs` + `PlayerAnimator.cs` | 玩家移动/动画基础，可裁剪为木筏控制 |
| `Assets/Scripts/Player/playerInputActions.inputactions` | Input System 配置，新增按键在此扩展 |
| `Assets/Scripts/Manager/GameManager.cs` | 局内主控（倒计时/状态机），改造为 3 分钟局 |
| `Assets/Scripts/Manager/SoundManager.cs` + `MusicManager.cs` | 音效/BGM 播放 |
| `Assets/Scripts/KitchenGameLobby.cs` + `KitchenGameMultiplayer.cs` + `Loader.cs` | 大厅/联机/场景加载，直接复用 |
| `Assets/Scripts/ClientNetworkTransform.cs` + `OwnerNetworkAnimator.cs` | Netcode 同步组件，木筏/玩家可直接用 |
| `Assets/Scripts/CharacterSelectPlayer.cs` | 角色/颜色选择流程 |
| `Assets/Scripts/IHasProgress.cs` | 读秒进度接口，捕鱼读秒可实现此接口复用现有 UI |
| `Assets/Scripts/LookAtCamera.cs` + `FollowTransform.cs` | 世界空间 UI 面向相机、跟随目标 |

**注意**：`Counter/`、`KitchenObject.cs`、`PlateKitchenObject.cs`、`DeliveryManager.cs` 是胡闹厨房专属逻辑，派对游戏不复用；但**在删除前先在派对分支上确认没有间接依赖**。

---

## 场景

| 场景 | 状态 | 备注 |
|---|---|---|
| `MainMenuScene.unity` | 复用 | 主菜单入口 |
| `LoadingScene.unity` | 复用 | 场景切换过渡 |
| `LobbyScene.unity` | 复用 | 联机大厅 |
| `CharacterSelectScene.unity` | 复用 | 角色/颜色选择 |
| `GameScene.unity` | **需大改** | 原胡闹厨房关卡；派对游戏新建 `GameScene_PartyFishing.unity`，不要覆盖原场景 |

---

## 开发原则

1. **优先复用现有代码/资源**，尤其是大厅/联机/输入/音频系统——这些已跑通。
2. **改造前先读 `Assets/Docs/游戏设计文档.md`**——所有玩法参数以该文档为准（180s 一局、道具耐久、分值、狂暴期倍率等）。
3. **不要覆盖胡闹厨房遗留场景/脚本**——用新文件、新命名空间隔离，避免误伤已跑通的联机大厅。
4. **命名规范**（美术资源）：`类型_名称.扩展`，小写下划线，英文（见 `Assets/Docs/美术需求文档.md`）。C# 保持 PascalCase。
5. **网络对象**：走 Netcode 权威主机模式，捕鱼点刷新、鱼数扣减、结算全在主机端计算，客户端仅表现层。
6. **Windows 路径**：所有 Bash 命令用 `/dev/null`、正斜杠、`""` 包含空格路径。
7. **不要动 `Assets/软连接脚本/`**——保留原样。

---

## 常见任务参考

- **新增派对玩法脚本**：放 `Assets/Scripts/PartyGame/`，继承 `NetworkBehaviour`，主机权威。
- **新增捕鱼点/道具/鱼类**：用 ScriptableObject 定义数据（分值、耐久、读秒），放 `Assets/ScriptableObjects/PartyGame/`。
- **UI 复用**：TextMeshPro + `Assets/_Assets/Fonts/ICE.ttf`；通用图元用 `Assets/_Assets/Textures/` 下 `ButtonBackground/Arrow/Warning/Tick_Border/Cross`。
- **测试联机**：ParrelSync 克隆 Unity 项目多开，本地跑主机+客户端。

---

## 待办 / 未决事项

见 `Assets/Docs/游戏设计文档.md` §10（TBD 清单）—— 水雷禁区、偷鱼冷却、道具超载、金色鱼可否徒手捕、每波捕鱼点数量等，试玩前需策划确认。

---

## Git 工作流

- 主分支：`main`
- 用户：`yitianchen`
- 项目远端：`https://github.com/Accommodate111/HuNaoChuFang.git`
- 派对游戏改造建议开新分支 `feature/party-fishing`，避免影响主分支胡闹厨房 v0.1.0 状态。
