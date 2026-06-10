# 胡闹厨房（HuNaoChuFang）

基于 Unity 2022 LTS 的 **4 人合作烹饪联机游戏**，玩法类似 *Overcooked*。支持本地局域网多人联机，采用 Netcode for GameObjects 实现网络同步。

---

## 环境要求

| 项目 | 版本 |
|------|------|
| Unity Editor | **2022.3.62f3c1**（LTS） |
| Git LFS | 必须安装（项目含大体积字体资源） |
| 操作系统 | Windows（已在 Win10/11 测试） |

---

## 克隆与运行

### 1. 克隆仓库

```bash
git clone https://github.com/Accommodate111/HuNaoChuFang.git
cd HuNaoChuFang
```

### 2. 安装 Git LFS（首次克隆必须）

```bash
git lfs install
git lfs pull
```

> 若未安装 LFS，字体文件 `ICE SDF.asset`（约 130MB）将无法正确下载，UI 文字可能显示异常。

### 3. 用 Unity Hub 打开项目

1. 打开 Unity Hub → **添加** → 选择项目根目录
2. 确认编辑器版本为 **2022.3.62f3c1**
3. 首次打开等待 Package 自动导入（Netcode、Input System、URP 等）

### 4. 运行游戏

1. 打开场景 `Assets/Scenes/MainMenuScene.unity`
2. 点击 **Play** 进入主菜单

---

## 使用说明

### 游戏流程

```
主菜单 → 大厅 → 角色选择 → 游戏场景
```

| 场景 | 说明 |
|------|------|
| **MainMenuScene** | 主菜单，点击 Play 进入大厅 |
| **LobbyScene** | 创建/加入房间，设置玩家昵称 |
| **CharacterSelectScene** | 选择角色颜色，全员 Ready 后进入游戏 |
| **GameScene** | 正式游戏，限时 90 秒完成订单 |

### 多人联机（当前版本：本地 LAN）

> 当前版本使用 **本地直连**（Host / Client），UGS 在线大厅（Lobby + Relay）代码已预留但尚未启用。

#### 创建房间（Host）

1. 主菜单 → **Play** 进入大厅
2. 输入玩家昵称
3. 点击 **Create Lobby** 创建房间
4. 自动进入角色选择场景
5. 选择颜色 → 点击 **Ready**
6. 全员 Ready 后自动开始游戏

#### 加入房间（Client）

1. 主菜单 → **Play** 进入大厅
2. 输入玩家昵称
3. 点击 **Quick Join** 加入局域网内 Host
4. 进入角色选择 → 选颜色 → **Ready**

#### 本地双开测试（推荐 ParrelSync）

项目已集成 [ParrelSync](Assets/Plugins/ParrelSync/) 插件：

1. 在 Unity 菜单 **ParrelSync → Clones Manager** 创建克隆项目
2. 原项目作为 **Host**，克隆项目作为 **Client**
3. 两个 Editor 同时 Play，即可本地联机调试

### 操作说明

| 操作 | 默认按键 |
|------|----------|
| 移动 | WASD / 方向键 |
| 交互（放置/拿取） | E / 左键 |
| 备用交互（切菜等） | F / 右键 |
| 暂停 | Esc |

> 可在游戏内 **Options** 菜单中重新绑定按键。

### 游戏玩法简述

1. 根据屏幕上方 **订单列表** 准备菜品
2. 从 **容器台** 拿食材 → **切菜台** 处理 → **灶台** 烹饪
3. 将成品放到 **盘子** 上，送至 **配送台** 得分
4. 用 **垃圾桶** 丢弃错误物品
5. 90 秒内尽可能完成更多订单

### Host 管理

- 在 **角色选择场景**，Host 可以点击其他玩家旁的按钮 **踢出玩家**
- 游戏中按 **Esc** 可暂停（任一玩家暂停会暂停全员）

---

## 版本记录

### v0.1.0（当前版本）

**功能**

- 4 人本地局域网联机（Host / Client）
- 完整烹饪流程：切菜、煎炸、装盘、配送
- ScriptableObject 数据驱动（食材、配方、转换规则）
- 角色颜色选择、订单系统、计分与结算
- 键位重绑定、音量设置
- 客户端权威移动 + 服务端权威交互

**已知问题**

#### 🔴 踢人后新玩家出生点重叠

**现象：** 在四人联机中，Host 踢出一名玩家后，若有新玩家加入，新玩家会刷新到 **第 4 个出生点**（`spawnPoints[3]`），导致该位置出现 **两名玩家重叠**。

**复现步骤：**

1. 开满 4 人房间并开始游戏
2. Host 在角色选择场景踢出其中一名玩家（如 ClientId = 2 的玩家）
3. 新玩家加入房间
4. 新玩家会被分配到第 4 个出生点，与仍在该位置的玩家重叠

**原因分析：**

出生点索引直接使用 `OwnerClientId`，而非玩家槽位序号：

```csharp
// Player.cs - OnNetworkSpawn()
int spawnIndex = (int)OwnerClientId;
if (spawnIndex >= spawnPoints.Count)
{
    spawnIndex = spawnPoints.Count - 1;  // 超出范围时固定到最后一个出生点
}
transform.position = spawnPoints[spawnIndex];
```

Netcode 分配的 `ClientId` 是递增的网络 ID，**不会在踢人后复用**。例如：

| 阶段 | 在线 ClientId | 实际占用出生点 |
|------|---------------|----------------|
| 满员 | 0, 1, 2, 3 | 点 0, 1, 2, 3 |
| 踢掉 ClientId=2 | 0, 1, 3 | 点 0, 1, 3 |
| 新玩家加入（ClientId=4） | 0, 1, 3, 4 | 点 0, 1, 3, **3**（4 ≥ 4，被 clamp 到索引 3） |

因此 ClientId=3 和 ClientId=4 的玩家会同时出现在第 4 个出生点。

**计划修复方向：**

- 使用 `playerDataNetworkList` 中的槽位索引（0~3）分配出生点，而非 `OwnerClientId`
- 或在玩家断开时回收并复用槽位编号

---

## 项目结构

```
Assets/
├── Scripts/
│   ├── Player/        # 玩家控制、动画、音效
│   ├── Counter/       # 操作台（切菜、灶台、配送等）
│   ├── Manager/       # 游戏状态、订单、音效
│   └── UI/            # 各场景界面
├── ScriptableObjects/ # 食材、配方等数据资产
├── Prefabs/           # 玩家、操作台、食材预制体
├── Scenes/            # 5 个游戏场景
└── Plugins/
    ├── ParrelSync/    # 本地多开联机测试
    └── QFSW/          # Quantum Console 调试工具
```

---

## 技术栈

- **Unity 2022.3 LTS** + **URP**
- **Netcode for GameObjects** 1.12.2
- **Input System** 1.14.0（支持键位重绑定）
- **TextMeshPro** 3.0.9
- Unity Gaming Services（Lobby / Relay，代码已预留，当前未启用）

---

## 许可证

本项目仅供学习与交流使用。第三方插件（ParrelSync、Quantum Console 等）遵循各自许可证。

---

## 相关链接

- 仓库地址：https://github.com/Accommodate111/HuNaoChuFang
