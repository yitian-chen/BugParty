# Bug Party 🐛🎣

一款 Unity 2022 LTS 派对竞技游戏。工作代号 **《Bug 海岛捕鱼赛》**：2-4 人俯视角海战，3 分钟内在共享捕鱼点抢鱼、回自己岛屿卸货、可偷别人的鱼，末 30 秒中央刷金色鱼进入狂暴期。分数 = 岛上普通鱼 ×1 + 金色鱼 ×2。

采用 Netcode for GameObjects 实现服务器权威联机，当前版本支持本地局域网 Host / Client 直连。

---

## 环境要求

| 项目 | 版本 |
|------|------|
| Unity Editor | **2022.3.62f3c1**（LTS） |
| Git LFS | 必须安装（字体资源约 130 MB） |
| 操作系统 | Windows 10 / 11 |

---

## 克隆与运行

```bash
git clone https://github.com/Accommodate111/HuNaoChuFang.git
cd HuNaoChuFang
git lfs install && git lfs pull
```

> 未执行 `git lfs pull` 时，UI 字体 `ICE SDF.asset` 无法正常显示，中文会变方块。

用 Unity Hub 添加项目根目录并以 **2022.3.62f3c1** 打开，从 `Assets/Scenes/LanMenuScene.unity` 开始 Play。

---

## 游戏流程

```
LanMenuScene（输入 IP / 端口） → LanLobbyScene（等玩家） → GameScene_PartyFishing（3 分钟对局）
```

### 主机 / 客户端

- 主机点 **Host**（默认端口 7777）后进入大厅
- 客户端输入主机 IP + 端口，点 **Join**
- 大厅内玩家满员或主机点 **Start**，加载游戏场景
- 局内倒计时结束后自动进入 3 分钟对局

### 本地双开测试（ParrelSync）

项目已集成 [ParrelSync](Assets/Plugins/ParrelSync/)：

1. Unity 菜单 **ParrelSync → Clones Manager** 创建克隆项目
2. 主项目作为 Host，克隆作为 Client
3. Client 用 `127.0.0.1:7777` 连接主机

### 操作说明

| 操作 | 按键 |
|------|------|
| 移动木筏 | WASD |
| 捕鱼（在鱼点） | E（长按读秒） |
| 卸货（在自家岛屿） | Q |
| 使用道具 1 / 2 | 1 / 2 |

---

## 项目结构

```
Assets/
├── Docs/               ← 设计文档（脑暴 / 游戏设计 / 美术需求）
├── Scenes/             ← LanMenuScene / LanLobbyScene / GameScene_PartyFishing
├── Scripts/
│   ├── PartyGame/      ← 派对游戏核心（Player / FishingSpot / Island / Mine / UI）
│   ├── PartyGame/Net/  ← 联机层（Lobby / Bootstrap / Spawner）
│   └── GameInput.cs 等  ← 输入 & 通用工具
├── Prefabs/PartyGame/  ← PartyPlayer / FishingSpot / Mine 等
├── ScriptableObjects/PartyGame/  ← 道具、鱼、配置 SO
├── _Assets/            ← 美术资源（Meshes / Textures / Materials / Sounds）
└── Plugins/            ← ParrelSync + Quantum Console
```

---

## 技术栈

- **Unity 2022.3 LTS** + **URP 14.0.12**
- **Netcode for GameObjects** 1.12.2（服务器权威）
- **Input System** 1.14.0
- **TextMeshPro** 3.0.9
- **ParrelSync** 本地多开
- **Quantum Console** 调试台

---

## 相关链接

- 仓库地址：https://github.com/Accommodate111/HuNaoChuFang
- 设计文档：`Assets/Docs/游戏设计文档.md`
