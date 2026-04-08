# LoadOrderManager

Slay the Spire 2 mod that lets you manually edit mod load order in-game.

`Load Order` button is injected into the official Modding screen. Reorder mods, click apply, and the mod writes the order into `settings.save` (`mod_settings.mod_list`).  
Changes apply on **next game launch**.

---

## 功能

- 在官方 `Modding` 页面注入 `Load Order` 按钮
- 支持上移/下移/置顶/置底
- 一键保存到 `settings.save`
- 自动检测客户端语言并切换 UI 文案
- i18n 采用**外部文件**，便于后续维护和社区共建

---

## 安装

将以下结构放到游戏目录 `mods/LoadOrderManager/`：

```text
LoadOrderManager/
  LoadOrderManager.dll
  mod_manifest.json
  i18n/
    *.lang
```

---

## 兼容说明

- 本 mod 不热重载游戏资源，不会在运行中重新加载 DLL/PCK
- 改动是“下次启动生效”，这与游戏原生加载机制一致
- `affects_gameplay = false`，不改战斗逻辑

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

- UTF-8 编码
- 每行 `key=value`
- 支持 `\n`
- 占位符用 `{0}`, `{1}`（`string.Format`）

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

---

## 构建

```powershell
cd LoadOrderManager
.\build.ps1
```

构建脚本会：

1. `dotnet build`
2. 拷贝 `dll + manifest + i18n` 到游戏 `mods/LoadOrderManager`
3. 同步 `torelease/` 快照用于发版

---

## License

MIT
