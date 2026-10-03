# LoadOrderManager

Slay the Spire 2 mod that lets you manually edit mod load order in-game.

`Load Order` button is injected into the official Modding screen. Reorder mods, click apply, and the mod writes the order into `settings.save` (`mod_settings.mod_list`).  
Changes apply on **next game launch**.

---

## 功能

- 在官方 `Modding` 页面注入 `Load Order` 按钮
- 支持上移/下移/置顶/置底
- 启用/禁用单个模组（✓/✗）
- Smart Sort（依赖优先的拓扑排序）与首字母排序
- 模组预设（Preset）：命名并切换"启用/禁用"配置
- **预设的新模组策略**：预设保存之后才订阅的模组，只在 `Default` 预设里默认开启，其它预设默认关闭（每个预设独立开关，见下）
- 剪贴板导入/导出
- 一键保存到 `settings.save`
- 自动检测客户端语言并切换 UI 文案
- i18n 采用**外部文件**，便于后续维护和社区共建

---

## 安装（包体结构）

```text
LoadOrderManager/
  LoadOrderManager.dll          # 版本启动器（游戏只会加载这一个 DLL）
  mod_manifest.json             # 一份版本号 + min_game_version = 最低支持版本
  i18n/*.lang
  bin/g0.107.1/
    LoadOrderManager.Impl.dll   # 真正的实现，由启动器按游戏版本挑选后加载
```

游戏只加载 `<mod.path>/<manifest.id>.dll`（`ModManager.TryLoadMod`），所以 `bin/` 下的实现不会被误加载。这样**一份包体在所有 Steam 分支上通用**：正式版（Latest Version）与 public-beta 拿到的是字节相同的内容，不存在"Steam 把 beta 的包发给正式版"这类错位。

本 mod 的全部游戏 API 都走 `AccessTools` 反射、编译期不引用 `sts2.dll`，反射目标在 v0.107.1 与 v0.111.0 均存在，因此**只需要一份实现**。将来某版本改名/改签名时，往 `bin/g<新版本>/` 放一份实现即可，玩家无需重新订阅。

---

## 预设的新模组策略

`presets.json`（schema v2）为每个预设保存三个字段：

| 字段 | 含义 |
|---|---|
| `disabledMods` | 该预设明确禁用的模组 |
| `knownMods` | 该预设"见过"的模组（创建/应用预设时记录） |
| `autoEnableNewMods` | 之后新订阅的模组在此预设中的默认状态 |

规则：

- 预设**见过**的模组：按 `disabledMods` 判定（未列入即启用）。
- 预设**没见过**的模组（订阅发生在预设保存之后）：按 `autoEnableNewMods` 判定，并在列表里标记 `NEW`。
- 打开面板只改**视图**，不写盘；只有点「应用」才写入 `settings.save`，同时把当前全部模组记为"见过"。
- 从 v0.3.0 升级时自动迁移：迁移那一刻已安装的模组全部视为"见过"（状态不变），且**只有第 1 个预设**（Default）`autoEnableNewMods = true`，其余预设为 `false` —— 这正是玩家要求的"只在 default 组默认开启"。

> 已知限制：预设按 **模组 Id** 匹配（与游戏 `ModManager.Initialize` 的匹配方式一致）。同名模组同时存在于 `mods/` 与创意工坊时，二者共用一个预设条目，无法分别开关。

---

## i18n 外部文件

语言文件目录：`i18n/*.lang`  
每个语言一个文件，例如：

- `en.lang`
- `zhs.lang`
- `zht.lang`
- `ja.lang`

文件格式：

```ini
# comment
key=value
status_loaded=Loaded {0} mods.
```

约定：

- UTF-8 编码、LF 行尾（见 `.gitattributes`）
- 每行 `key=value`
- 支持 `\n`
- 占位符用 `{0}`, `{1}`（`string.Format`）
- 缺键回退到 `en.lang`

当前支持：

- English (`en`)
- 简体中文 (`zhs`)
- 繁體中文 (`zht`)
- Korean (`ko`)
- German (`de`)
- Japanese (`ja`)
- French (`fr`)
- Russian (`ru`)
- Spanish - Spain (`es-ES`)
- Spanish - Latin America (`es-419`)
- Portuguese - Brazil (`pt-BR`)
- Polish (`pl`)
- Turkish (`tr`)
- Italian (`it`)

欢迎 PR 提交新语言或改进现有翻译。

> TODO：除 `en` / `zhs` 外的 12 个语言文件缺 v0.3.0 新增的 18 个键（预设、智能排序、剪贴板等），当前回退显示英文。

---

## 兼容说明

- 本 mod 不热重载游戏资源，不会在运行中重新加载 DLL/PCK
- 改动是"下次启动生效"，这与游戏原生加载机制一致
- `affects_gameplay = false`，不改战斗逻辑
- `min_game_version = 0.107.1`（最低支持版本，不是最新版本——写成最新版本会让老分支被游戏直接拒载）

---

## 排查日志

- 本 mod 会写独立日志到：`user://LoadOrderManager/load_order_manager.log`
- Windows + Steam 通常位于：`%APPDATA%\SlayTheSpire2\steam\<你的SteamId>\LoadOrderManager\load_order_manager.log`
- 关键记录：启动器选中的版本目录、反射读取到的当前顺序、i18n 解析到的目录、UI 布局尺寸与页脚是否在屏内、Apply 保存结果、预设迁移与新模组策略命中数
- 启动器自身也会往 `godot.log` 写一行 `[LoadOrderManager/Loader] game <版本> -> <版本目录>`

---

## 构建

```powershell
cd LoadOrderManager
.\build.ps1                    # 编译实现 + 启动器 + 组装 + 自检 + 部署到本机 mods/
.\build.ps1 -NoLocalDeploy     # 只打包
.\build.ps1 -StageWorkshop     # 额外同步工坊 workspace 的 content/
```

构建脚本会：

1. 从 `LoadOrderManager.csproj` 读版本号（**唯一真源**）
2. 按 `$Targets` 表逐个编译实现 → `build\mods\LoadOrderManager\bin\<版本目录>\LoadOrderManager.Impl.dll`
3. 用 `tools\ModVersionLoader` 编译启动器 → 根目录 `LoadOrderManager.dll`
4. 复制 `i18n/`，写 `mod_manifest.json`（`version` + `min_game_version` = 所有实现里最低的游戏版本）
5. 打包自检（根目录唯一 DLL / 实现就位 / `bin/` 下无 json / 语言文件数 / manifest 版本一致）
6. 部署到游戏 `mods\LoadOrderManager`（检测到游戏在运行会直接报错退出，避免半旧半新）
7. `-StageWorkshop` 时同步到 `_workshop_workspaces\LoadOrderManager\content`

新增一个游戏版本实现：

1. 把该版本的 SDK 存到 `tools\sts2_sdk_by_version\v<版本>\`（需要 `sts2.dll` / `GodotSharp.dll` / `0Harmony.dll`）
2. 在 `build.ps1` 的 `$Targets` 加一行
3. 若该版本需要专属代码，按 `AutoModSubscriber` 的 `src/versions/<vXXX>/` 模式拆分

### 版本分发机制

启动器 `tools\ModVersionLoader\` 是跨项目共用的通用件（零第三方依赖），选择顺序为
「精确命中 → 不高于宿主的最大版本 → `bin/latest` → 可用最高版本 → 显式报错」。
完整约定与踩坑记录见 `tools\ModVersionLoader\README.md` 与
`AutoModSubscriber\docs\VERSION_BUNDLE.md`。**约定：`mod_manifest.json` 的 `version`
在所有实现之间必须完全一致**（本仓库由 `build.ps1` 统一写回）。

---

## 开发自检

不启动游戏也能验证的两件事：

```powershell
# 1) 启动器版本解析 / 目录挑选逻辑（19 项断言）
dotnet run -c Release --project D:\A-Developing\main\sts2\tools\ModVersionLoader.Tests\logic

# 2) 模拟游戏加载：启动器 → 挑版本 → 加载实现 → 定位入口
#    需要先准备一个探针目录（harness + release_info.json + sts2/GodotSharp/0Harmony + 包体）
.\AmsLoaderHarness.exe <包体绝对路径> --mod LoadOrderManager --dry
# 期望输出
#   [LoadOrderManager/Loader] game 0.111.0.0 -> g0.107.1 (LoadOrderManager.Impl.dll)
#   [harness] OK: entry point LoadOrderManager.LoadOrderManagerMod.Initialize()
```

UI 布局的桌面复现（模拟移动端小画布）：

```powershell
$env:LOADORDER_UI_SIMULATE_H=654   # 模拟逻辑画布高度
$env:LOADORDER_UI_COMPACT=1        # 强制紧凑布局
```

启动后看 `load_order_manager.log` 里的 `UI layout:` / `UI layout check:` 两行：
`footerInside=True` 与 `dialogInside=True` 即表示页脚完整落在画布内。

---

## License

MIT
