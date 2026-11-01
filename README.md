# Chill With You — Costume Switcher

给《Chill with You : Lo-Fi Story》加一个换装面板的 BepInEx 插件。
直接调用游戏自带的 `Bulbul.CostumeChangeService.ChangeCostume()`，**不替换、不修改任何游戏资源**。

A tiny BepInEx plugin for *Chill with You : Lo-Fi Story* that adds an outfit-switching
panel. It calls the game's own `CostumeChangeService`, so no assets are touched.

---

## 效果

进房间后屏幕右侧出现「换装 Outfit」竖排面板，6 个按钮，当前穿的那套高亮；
`F8` 隐藏 / 显示，数字键 `1`–`6` 也能直接换。

游戏里一共有 6 套现成服装：

| 按钮 | `CostumeSkinType` | 值 | 模型组 |
|---|---|---|---|
| 1 默认 Default | `Default_1` | 1 | Default |
| 2 Polo ① | `Polo_1` | 1001 | Polo |
| 3 Polo ② | `Polo_2` | 1002 | Polo |
| 4 Tee ① | `Tee_1` | 2001 | Tee |
| 5 Tee ② | `Tee_2` | 2002 | Tee |
| 6 背带裤 Overalls | `Overalls_1` | 3001 | Overalls |

同一模型组内换只是换材质（`CostumeSkinChanger.ChangeSkin`），跨组会走
`TryChangeModel` 重新加载模型并挂骨骼，所以动画、表情保持正常。

## 安装

1. 装 [BepInEx 5.4.x (x64)](https://github.com/BepInEx/BepInEx) 到游戏根目录
   （本仓库不附带 BepInEx 二进制）
2. 启动一次游戏，生成 `BepInEx/plugins`、`BepInEx/config`
3. 把 `CostumeSwitcher.dll` 放进 `BepInEx/plugins/`

## 从源码构建

需要 .NET Framework / Unity 2022 的托管程序集，无需 .NET SDK：

```powershell
# 改 build.ps1 里的 $GameDir 指到你的安装目录
powershell -ExecutionPolicy Bypass -File tools/build.ps1
```

`build.ps1` 用 Visual Studio 自带的 Roslyn `csc`，直接引用游戏 `*_Data/Managed` 下的
UnityEngine / Assembly-CSharp / UniTask 和 BepInEx 的 `core` 程序集。

## 这个游戏的两个坑

折腾过程中撞出来的，写下来给后来人：

1. **游戏启动时会清掉所有根物体，连 `DontDestroyOnLoad` 里的也清掉。**
   插件自己的 `MonoBehaviour` 活不过开局。做法是用 Harmony 钩住
   `RoomGameManager.Update`，每帧检查面板还在不在，没了就重建。
2. **IMGUI 不参与合成。** `OnGUI` 有回调、按钮甚至能收到点击，
   但画面上什么都没有（整屏刷洋红都看不见）。面板只能用 UGUI Canvas。

## 排查

日志在 `BepInEx/LogOutput.log`（UTF-8）：

```powershell
Get-Content BepInEx/LogOutput.log -Encoding UTF8 -Tail 30
```

面板没出现先看日志里有没有 `Costume Switcher 2.0.0`；有的话按 `F8`，
或者确认已经进了房间（面板需要 `CostumeChangeService` 存在于场景里）。

## 卸载

删掉 `winhttp.dll`、`doorstop_config.ini`、`.doorstop_version` 和 `BepInEx/` 即可，
游戏本体一个文件都没动过。

## 免责声明

非官方 Mod，与 Nestopi Inc. 无关。不含任何游戏资源或游戏本体代码，
只通过反射/公开 API 调用游戏自身的换装方法。请在自己拥有的游戏上使用。

## License

MIT — 见 [LICENSE](LICENSE)。
