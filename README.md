# HECATE G3 Max 自动切换控制器

Windows 小工具：根据 HECATE G3 Max 2.4G 耳机在线状态，自动切换播放和麦克风输出。支持 SteelSeries Sonar Classic 模式与 Windows 本地默认设备模式。

源码位于 `src/`，图标位于 `assets/`，详细调查记录位于 `docs/`。

## 构建

需要 Windows 自带的 .NET Framework 4.x 编译器：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

默认输出到 `bin\HecateAutoSwitch.exe`。`bin/` 和 `obj/` 不提交到 Git。

## 使用

构建后直接运行 `bin\HecateAutoSwitch.exe`，无需安装。

- `自动跟随耳机状态` 默认开启。
- 关闭窗口后程序继续在通知区域运行；双击托盘图标重新打开。
- 托盘菜单可以暂停自动跟随、手动切换或退出。
- 退出控制器不会卸载或修改任何音频驱动。

## 两种输出模式

### 检测到 SteelSeries Sonar 经典模式

控制器调用 Sonar 本机的 `127.0.0.1:8822` 控制接口：

- 保持 Windows 默认输出为 `SteelSeries Sonar - Gaming`。
- 耳机在线时把 Sonar 的游戏、聊天、媒体和 Aux 物理输出路由到 HECATE 耳机。
- 耳机离线时读取 Sonar 当前配置的设备顺序，把这些输出路由到排序中的下一个可用设备。
- 麦克风也按 `chatCapture` 的设备顺序切换；耳机在线切回 HECATE 麦克风，离线切到 Sonar 指定的下一支可用麦克风。
- 不绕过 Sonar 的虚拟混音、EQ 和应用路由。

### 未检测到 Sonar

控制器直接切换 Windows 默认播放设备：

- 耳机在线：`小宇耳机2.4G (HECATE G3 Max)`。
- 耳机离线：`电脑音响 (Realtek(R) Audio)`。
- 录音角色同步切换：耳机在线使用 HECATE 麦克风，离线优先恢复控制器之前观察到的非耳机默认麦克风；没有记录时选择一支非虚拟摄像头麦克风或其他可用麦克风。

Windows 本身没有可查询的全局播放设备顺序，因此本地模式使用“耳机或之前使用的回退设备”的规则；Sonar 模式则直接采用 Sonar 中配置的候选顺序。

如果 Sonar 正在启动但控制接口暂时不可用，控制器显示“模式未知”并暂停自动切换。

## 耳机状态来源

每次轮询都会按当天日期计算：

```text
%APPDATA%\HECATE\log\log_YYYYMMDD.log
```

跨过午夜后会切换到新的当天文件。新文件尚未生成时状态为未知，不会切换音频；出现下一条状态帧后恢复。

解析的是官方日志中的 `D0 35 BB 0A` 帧：

- 第二数据字节 `01`：在线。
- 第二数据字节 `00`：离线。
- 第三个数据字节：电量。

程序只读日志，不向 HID 写数据，也不锁定 HECATE 的日志文件。

## 前置条件

- HECATE 软件正在运行。
- HECATE 详细日志已开启。
- 设备为 G3 Max 无线版，日志状态帧格式与本次验证的 HECATE 1.1.44 一致。
- Sonar 当前使用 Classic 模式；其他模式会安全暂停。

## 切换规则

- 新状态连续保持约 3 秒后才执行。
- 自动切换只在耳机状态或输出模式变化时执行，不持续抢占用户手动选择的设备。
- 切换后读取实际默认设备或 Sonar 路由，校验成功才记为完成。
- 读取失败、日志缺失、设备未识别时保持当前输出。
- 不显示切换或托盘通知气泡，避免系统通知提示音。

## 未包含

- 没有写入注册表、驱动或 HECATE 配置。
- 没有建立开机启动项。
- 不支持 Sonar Streamer 模式的自动路由；检测到该模式会暂停。
