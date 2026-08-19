# Bug Party 🐛🎣

一款 Unity 2022 LTS 派对竞技游戏。工作代号 **《Bug 海岛捕鱼赛》**：2-4 人俯视角海战，3 分钟内在共享捕鱼点抢鱼、回自己岛屿卸货、可偷别人的鱼，末 30 秒中央刷金色鱼进入狂暴期。分数 = 岛上普通鱼 ×1 + 金色鱼 ×2。

采用 Netcode for GameObjects 实现服务器权威联机，当前版本支持本地局域网 Host / Client 直连。

---

## 环境要求

| 项目 | 版本 |
|------|------|
| Unity Editor | **2022.3.62f3c1**（LTS） |
| Git + Git LFS | Git 2.30+；LFS 必装（字体资源约 130 MB） |
| 操作系统 | Windows 10 / 11 |

---

## 快速启动（零环境电脑）

假设是一台什么都没装的 Windows 电脑，按顺序做完下面 5 步即可进游戏。

### 1. 安装 Git（含 Git LFS）

从 <https://git-scm.com/download/win> 下载 **Git for Windows** 安装包，一路 Next 用默认选项即可——安装包**自带 Git LFS**，无需额外下载。

装完后打开 **Git Bash** 或 **PowerShell** 验证：

```bash
git --version           # 应输出 git version 2.xx.xx
git lfs version         # 应输出 git-lfs/3.x.x
```

如果 `git lfs version` 找不到命令，去 <https://git-lfs.com> 单独下载 LFS 安装包再装一次。

初次使用还需要**为当前用户注册 LFS 过滤器**（一台机子做一次就够）：

```bash
git lfs install
```

> 这一步会把 LFS 挂到 `~/.gitconfig` 里；缺了它之后 `git clone` 拉到的 `.asset` 会是几十字节的指针文件，Unity 打开会显示中文乱码或方块。

### 2. 克隆仓库

选一个你放代码的目录（比如 `D:\dev`），进入后执行：

```bash
git clone git@github.com:yitian-chen/BugParty.git
cd BugParty
```

> 用 HTTPS 也行：`git clone https://github.com/yitian-chen/BugParty.git`——如果 SSH key 还没配置就选 HTTPS。

**确认 LFS 文件已下载**（这一步很关键，`clone` 时如果 LFS 没配好会静默失败）：

```bash
git lfs pull
git lfs ls-files
```

`ls-files` 应至少输出一行类似：

```
d1ce8f22e9 * Assets/_Assets/Fonts/ICE SDF.asset
```

如果输出为空、或该文件大小只有几百字节（`ls -la "Assets/_Assets/Fonts/ICE SDF.asset"` 显示 <1 KB），说明 LFS 没生效，回到第 1 步检查 `git lfs install` 是否成功、然后：

```bash
git lfs fetch --all
git lfs checkout
```

### 3. 安装 Unity Hub 与 Editor

1. 到 <https://unity.com/download> 下载 **Unity Hub** 并安装
2. 打开 Unity Hub → 左侧 **Installs** → 右上 **Install Editor**
3. 选 **Archive** 选项卡（长期支持版本清单）→ 找 **2022.3.62f1**（如果没有 62f1，点 **Download archive** 打开的网页里手动搜 `2022.3.62f1` 复制 unityhub://…链接回 Hub）
4. 勾选安装的 Modules：**至少勾** `Windows Build Support (IL2CPP)`——其他模块（Android/iOS/WebGL）按需
5. 装完 Editor 可能要几分钟，Hub 里 Installs 页会显示进度

> 项目严格要求 **2022.3.62f1**（LTS）。别的小版本可能触发资源升级警告。

### 4. 把项目加入 Unity Hub

- Unity Hub → 左侧 **Projects** → 右上箭头 **Add → Add project from disk**
- 选中你 clone 下来的 `BugParty` 文件夹（**不是**它里面的 `Assets`）
- 项目条目会出现，Editor 版本列应自动匹配 `2022.3.62f1`；若显示红色感叹号，点它选正确的版本

点项目名称打开，第一次加载 Unity 会花 3–10 分钟**导入所有资源+编译 Library**（正常，之后就快了）。

### 5. 打开菜单场景并 Play

Editor 打开后：

1. 顶部菜单 **File → Open Scene**（或 Project 面板双击）打开 `Assets/Scenes/LanMenuScene.unity`
2. Editor 顶部工具栏点 **▶ Play** 按钮
3. 游戏内点 **Host** 就能开一局 vs 3 名 AI 的对局
4. 想要真人多开测试见下方 **本地双开测试**

### 常见问题速查

| 现象 | 原因 | 解决 |
|------|------|------|
| 界面中文全是方块 / 显示 `ICE SDF` 找不到 | LFS 文件没拉下来 | `git lfs install && git lfs pull` |
| Play 时 Console 报 `NullReferenceException on PartyPlayer` | 场景内组件引用丢失 | 重启 Unity；如果仍报错在 issue 里贴堆栈 |
| 编译报错 `Netcode / Multiplayer.Samples not found` | Packages 目录被跳过 | 确认 `Packages/manifest.json` 未修改，`Library/` 全部清空后重开 Editor 触发重装 |
| Unity Hub 找不到 2022.3.62f1 | Archive 列表折叠 | 直接访问 <https://unity.com/releases/editor/archive> 找 2022.3.62 → 点 `Unity Hub` 打开 |
| ParrelSync 菜单没出现 | Unity 未识别到插件 | `Assets/Plugins/ParrelSync/` 应存在；如果为空，回到第 2 步重新 `git pull` |

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

- 仓库地址：https://github.com/yitian-chen/BugParty
- 设计文档：`Assets/Docs/游戏设计文档.md`
