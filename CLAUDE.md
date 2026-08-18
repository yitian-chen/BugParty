# CLAUDE.md

本文件为 Claude Code 提供项目上下文，用以在本仓库中高效协作。

---

## 项目定位

**Bug Party** — Unity 2022.3.62f3c1 LTS 的 URP + Netcode for GameObjects 派对竞技游戏。工作代号 **《Bug 海岛捕鱼赛》**。仓库前身是基于 KitchenChaos 教程的胡闹厨房复刻，胡闹厨房的代码/资源/场景在 2026-08-18 已全部清理，只保留派对游戏及其依赖的基础设施。

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
| 联机 | Netcode for GameObjects 1.12.2（局域网 Host/Client 直连；UGS 未启用） |
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
├── Scenes/             ← LanMenuScene / LanLobbyScene / GameScene_PartyFishing
├── Scripts/
│   ├── PartyGame/      ← 派对游戏核心（Player/FishingSpot/Island/Mine/UI）
│   ├── PartyGame/Net/  ← 联机层（LanLobbyManager/Bootstrap/Spawner）
│   ├── Player/         ← 只剩 Input System 生成的 playerInputActions
│   ├── GameInput.cs、IHasProgress.cs、ClientNetworkTransform.cs
│   └── FollowTransform.cs、LookAtCamera.cs  ← 通用工具
├── Prefabs/PartyGame/  ← PartyPlayer / FishingSpot_* / Mine 等
├── ScriptableObjects/PartyGame/  ← 道具/鱼/PartyGameConfig SO
├── _Assets/            ← 美术资源（Meshes/Textures/Materials/Animations/Sounds/Fonts）
├── Plugins/            ← ParrelSync + QFSW Quantum Console
├── Settings/           ← URP 渲染管线配置
├── Shaders/            ← 自定义 Shader
├── TextMesh Pro/       ← TMP 内建资源
├── DefaultNetworkPrefabs.asset ← NGO 注册表（PartyPlayer/FishingSpot_*/Mine）
└── 软连接脚本/          ← 保留原样，不动
```

**注意**：清理后 `Assets/Scripts/` 下只剩 6 个通用文件 + `PartyGame/` 子目录。任何新代码放 `PartyGame/`，SO 放 `ScriptableObjects/PartyGame/`。

---

## 关键脚本 / 系统速查

| 文件 | 作用 |
|---|---|
| `Scripts/GameInput.cs` | 输入抽象层（单例），扩展按键在此加 |
| `Scripts/Player/playerInputActions.inputactions` | Input System 配置 |
| `Scripts/PartyGame/PartyPlayer.cs` | 玩家/木筏控制、捕鱼、装货、道具使用 |
| `Scripts/PartyGame/PartyGameManager.cs` | 局内主控（State/倒计时/狂暴），服务器权威 |
| `Scripts/PartyGame/FishingSpotSpawner.cs` | 分波刷渔点 |
| `Scripts/PartyGame/Island.cs` | 岛屿卸货 & 分数统计 |
| `Scripts/PartyGame/Mine.cs` | 水雷实体 |
| `Scripts/PartyGame/Net/LanLobbyManager.cs` | LAN 大厅玩家名单（NetworkList） |
| `Scripts/PartyGame/Net/NetworkedPartyBootstrap.cs` | LanMenu 之后启动 Host/Client 的引导脚本 |
| `Scripts/PartyGame/Net/PartyPlayerSpawner.cs` | 场景加载后按 Lobby 名单生成 PartyPlayer |
| `Scripts/PartyGame/Net/DisconnectReturnToMenu.cs` | 断线自动回菜单 |
| `Scripts/ClientNetworkTransform.cs` | 客户端权威 NetworkTransform（PartyPlayer 用） |
| `Scripts/IHasProgress.cs` | 读秒进度接口，头顶进度条订阅 |
| `Scripts/LookAtCamera.cs` + `FollowTransform.cs` | 世界空间 UI 面向相机 / 跟随目标 |

---

## 场景

| 场景 | 备注 |
|---|---|
| `LanMenuScene.unity` | 主菜单：输入 IP+端口，选择 Host 或 Join |
| `LanLobbyScene.unity` | 大厅：Host 看到当前玩家名单，可 Start |
| `GameScene_PartyFishing.unity` | 3 分钟对局关卡 |

`ProjectSettings/EditorBuildSettings.asset` 只注册这三个场景。

---

## 开发原则

1. **改造前先读 `Assets/Docs/游戏设计文档.md`**——所有玩法参数以该文档为准（180s 一局、道具耐久、分值、狂暴期倍率等）。
2. **网络对象**：走 Netcode 服务器权威主机模式，捕鱼点刷新、鱼数扣减、结算全在主机端计算，客户端仅表现层。改新逻辑先想「服务端要不要写？客户端要看到什么？」
3. **命名规范**（美术资源）：`类型_名称.扩展`，小写下划线，英文（见 `Assets/Docs/美术需求文档.md`）。C# 保持 PascalCase。
4. **Windows 路径**：所有 Bash 命令用 `/dev/null`、正斜杠、`""` 包含空格路径。
5. **不要动 `Assets/软连接脚本/`**——保留原样。

---

## 常见任务参考

- **新增派对玩法脚本**：放 `Assets/Scripts/PartyGame/`，网络对象继承 `NetworkBehaviour`，主机权威。
- **新增捕鱼点/道具/鱼类**：用 ScriptableObject 定义数据（分值、耐久、读秒），放 `Assets/ScriptableObjects/PartyGame/`。**新道具务必加入 `PartyGameConfig.allItems`**——客户端反查 ItemDataSO 依赖它。
- **注册新联网 Prefab**：加入 `Assets/DefaultNetworkPrefabs.asset` 的 List（NGO 会读取）。
- **UI 复用**：TextMeshPro + `Assets/_Assets/Fonts/ICE.ttf`；通用图元用 `Assets/_Assets/Textures/`。
- **测试联机**：ParrelSync 克隆项目双开；Host 用 `127.0.0.1` 或 `0.0.0.0`，Client 填 `127.0.0.1`。

---

## 已知的联机坑（K1-K6 血泪）

- **NetworkBehaviour 所在 GO 必须挂 NetworkObject**，否则 NetworkList/NetworkVariable 全不 spawn。
- **NetworkConfig.PlayerPrefab 会自动 spawn 一个副本**到 (0,0,0)，不要设置——用 PartyPlayerSpawner 手动 `SpawnAsPlayerObject` 到指定出生点。
- **场景内放的 NetworkObject 会自动被 Host 认领 Owner**，容易被 Host 一起控制——生成用的空位置只用 Transform-only 占位物。
- **NetworkObject.Spawn 只发一次初始 transform**，`Instantiate(prefab, pos)` 的 pos 不会被送到客户端——网络对象若会移动或需要按运行时位置定位，必须挂 `NetworkTransform`（渔点/水雷已挂）。
- **ScriptableObject 的数组字段 asset 里可能未填**——加了字段要手动 populate，否则 GetItemByKind 返回 null。

---

## 待办 / 未决事项

见 `Assets/Docs/游戏设计文档.md` §10（TBD 清单）—— 水雷禁区、偷鱼冷却、道具超载、金色鱼可否徒手捕、每波捕鱼点数量等，试玩前需策划确认。

---

## Git 工作流

- 主分支：`main`（当前 = 派对游戏，v0.1 胡闹厨房状态在 tag/更早 commit）
- 派对游戏改造分支：`feature/party-fishing`
- 用户：`yitianchen`
- 项目远端：`git@github.com:yitian-chen/BugParty.git`
