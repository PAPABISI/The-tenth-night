# 《第十夜》多人联机游戏后端原型

《第十夜》是一个基于回合阶段推进、信息不对称博弈的多人对抗游戏原型。  
本仓库实现了服务端核心规则、阶段状态机与最小可用 API，可用于本地联调与 Unity 客户端接入。

---

## 一、项目概览

本项目采用 **Server-Authoritative（服务端权威）** 架构：

- 服务端维护完整 `GameState` 真相
- 客户端通过接口获取受权限约束的视图数据
- 关键规则（卡牌、死亡、继承、胜负）全部在服务端执行

项目当前重点是规则系统与服务端流程，不包含完整前端表现层。

---

## 二、技术栈

- **语言**：C#
- **运行时**：.NET 10
- **后端框架**：ASP.NET Core Minimal API
- **核心模块**：自定义规则引擎 + 状态机 + 卡牌策略系统

### 目录结构

```text
Tenth/
  Tenth.slnx
  NightTen.Core/                # 游戏核心规则库
    Models/
      CoreDataStructures.cs     # 基础数据结构与枚举
    Interfaces/
      GameInterfaces.cs         # 核心接口与事件定义
    Systems/
      GameStateMachine.cs       # 阶段状态机
      GameRuleEngine.cs         # 规则引擎
      CardEffects.cs            # 卡牌效果策略实现
      GameLoopRunner.cs         # 本地自动推演（可选）
  NightTenServer/               # Web API 服务端
    Program.cs
    RoomStore.cs
    Contracts.cs
  NightTen.UnityClient/         # 客户端预留目录
```

---

## 三、核心架构设计

### 1）状态机与规则引擎分层

- `GameStateMachine`：负责阶段切换与生命周期钩子（OnEnter / OnExit）
- `GameRuleEngine`：负责具体规则执行与状态变更

通过分��将“何时切换”与“切换后做什么”分离，降低耦合，便于扩展与维护。

### 2）卡牌策略模式

每张卡牌实现统一接口 `ICardEffect`，由规则引擎在运行时选择策略执行：

- GunShot（枪杀）
- Poison（毒杀）
- Antidote（解药）
- Bandage（绷带）
- BulletProof（防弹衣）

新增卡牌时可通过新增策略类完成，减少对主流程代码影响。

### 3）信息可见性控制

- `ToSelfView()`：仅返回玩家本人可见信息（手牌、HP 等）
- `ToPublicView()`：仅返回公共信息（存活状态等）

用于保证阵营身份、毒源、隐式状态等敏感信息不会被错误广播。

---

## 四、游戏规则（当前实现）

### 1）阵营设定

- **守卫（Guardian）**
- **盗贼（Thief）**
- **恋人（Lovers）**

其中守卫与盗贼阵营包含 Leader / Member 职位差异。

### 2）阶段流程

每回合按以下阶段推进：

1. **Initialization**：身份初始化与基础状态准备  
2. **DayExploration**：探索与卡牌交互  
3. **DinnerPhase**：毒药统一结算  
4. **NightPhase**：盗贼提交行窃意向并结算宝物  
5. **RoundSettlement**：回合收束与胜利判定  
6. **GameOver**：游戏结束

### 3）卡牌机制

- **枪杀（GunShot）**  
  白天使用，立即造成致命伤害；若目标有防弹衣则抵消一次。
- **毒杀（Poison）**  
  白天使用，作为暗牌叠加中毒层数，晚餐统一结算。
- **解药（Antidote）**  
  晚餐阶段可用，减少毒层并恢复生命。
- **绷带（Bandage）**  
  恢复 1 点生命。
- **防弹衣（BulletProof）**  
  白天使用，提供一次枪杀免疫。

### 4）死亡与继承

- 死亡统一进入服务端死亡管线处理
- 支持宝物转移/归位逻辑
- 支持阵营内上位继承
- 支持恋人共生联动规则

### 5）胜利条件

- 守卫或盗贼可通过“肃清对方阵营”触发即时胜利
- 到达最大回合后按宝物归属判定终局胜负
- 恋人满足特定条件时可触发独立胜利优先级

---

## 五、API 概览（最小可用）

- `POST /room/create`：创建房间
- `POST /room/{roomId}/join`：加入房间（当前为简化流程）
- `POST /room/{roomId}/start`：启动对局
- `POST /room/{roomId}/phase/next`：推进阶段
- `POST /room/{roomId}/action/draw`：抽卡
- `POST /room/{roomId}/action/use-card`：使用卡牌
- `POST /room/{roomId}/action/night-intent`：夜晚意图提交
- `GET /room/{roomId}/state/{playerId}`：获取指定玩家视图

---

## 六、快速启动

### 1）构建

```bash
dotnet build .\Tenth.slnx
```

### 2）运行服务端

```bash
cd .\NightTenServer
dotnet run
```

---

## 七、PowerShell 调用示例

### 1）创建房间

```powershell
$create = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/room/create"
$roomId = $create.roomId
```

### 2）开始游戏

```powershell
$body = @{
  playerIds = @(
    "11111111-1111-1111-1111-111111111111",
    "22222222-2222-2222-2222-222222222222",
    "33333333-3333-3333-3333-333333333333",
    "44444444-4444-4444-4444-444444444444",
    "55555555-5555-5555-5555-555555555555",
    "66666666-6666-6666-6666-666666666666"
  )
} | ConvertTo-Json

Invoke-RestMethod -Method Post -Uri "http://localhost:5000/room/$roomId/start" -ContentType "application/json" -Body $body
```

### 3）查询玩家状态

```powershell
Invoke-RestMethod -Method Get -Uri "http://localhost:5000/room/$roomId/state/11111111-1111-1111-1111-111111111111"
```

---
## 八、相关仓库

https://github.com/PAPABISI/the-tenth-night-unity.git
