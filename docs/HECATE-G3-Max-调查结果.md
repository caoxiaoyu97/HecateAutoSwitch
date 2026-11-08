# HECATE G3 Max 自动切换调查

日期：2026-11-08。范围：只读检查官网、本机 PnP、官方安装目录/日志、运行模块及界面。没有安装或运行下载程序，没有写驱动/注册表，没有切换音频设备，没有向 HID 发送查询报文。

## 已确认

- 实际硬件为 USB\VID_35BB&PID_A226；音频 MI_00，HID MI_03，含 COL01-COL04。此前对话的 A2268 与本机不符。
- 官方下载页 https://www.edifier.com/cn/driver.html 当前链接为 https://cn-oss.edifier.com/Uploads/images/2026/10/15/HECATE_1.1.44.exe 。未下载或运行安装包。
- 本机 D:\app\Program Files\HECATE\ver.xml 为 1.1.44；HECATE.exe 签名状态 Valid，签署者东莞市漫步者电竞科技有限公司。
- 今日日志显示程序在调查期间从 1.1.42 重启至 1.1.44；这不是本次调查执行的操作。
- 官方配置 data\devices\015_g3max.json 明确指定：

```json
{"vid":"0x35bb","pid":"0xa226","need_check_connected":true,"wireless":true,"hid_infos":[{"hid_name":"hid_for_device","need_hid_service":true,"hid_path_feature":"mi_03&col03"},{"hid_name":"hid_for_led","need_hid_service":true,"hid_path_feature":"mi_03&col04"}]}
```

- 同文件把 A225 标为有线；018_g3maxdock.json 把底座版无线标为 A22B、有线标为 A22A。型号不能混用。
- HECATE.exe 已加载 hidapi.dll、HIDUsb.dll、libdongle.dll 和 HID.DLL。
- 对文件的静态字符串检查发现 hid_write、hid_read_timeout，以及 DeviceMgr、deviceOnlineChanged、deviceConnectedChanged、DeviceConnectState。结合配置，支持官方程序通过厂商 HID 通道维护状态这一判断；未动态跟踪具体调用链或确认报文字节。
- libdongle.dll 的导出名包括 read_online_device，但也有 ota_update_client / ota_update_dongle。没有找到调用签名或官方 SDK 文档，不调用这些函数。名称本身不能证明可安全获取耳机无线状态。
- 运行服务为 thx_hecate_g3max_service（THX Spatial Audio Service）；不能据此认定服务提供无线连接状态 API。
- 对 HECATE 进程的一次 TCP 监听检查没有返回监听端口；这不能排除命名管道、共享内存等其他接口。
- AppData\Roaming\HECATE\log\log_20261108.log 仅看到启动版本、Initialize SysAudio: vid_35bb&pid_a226 及升级相关警告，没有可直接用于监控的实时连接变化。

## 界面检查的边界

Windows 辅助功能树能读取 HECATE 界面文字与表格。调查时界面在升级中心，并出现要求接收器断开、耳机 USB 连接的升级提示。没有点击升级、配对、确认或其他设置。升级页“设备已连接/接收器已连接”不能直接等同于无线链路状态。之后接收器从 PnP 列表消失，未观察到受控的耳机开关机对照。尚未验证主页“2.4G 已连接/未连接”是否可读取、是否在最小化时持续更新。

## EdifierGuard

https://raw.githubusercontent.com/2909369292/EdifierGuard/main/guard.py 的常量是 VID=0x35BB、PID=0xA22B；README 明确为底座版。没有核实到 A226 或 A2268 的兼容支持。相同 COL03 不证明报文兼容，不建议只替换 PID 运行。其策略是隐藏/恢复端点，并非明确指定扬声器作为默认输出。

## 最安全的下一步

1. 保持接收器插入，回到官方主页，验证耳机开机/关机各两轮时，辅助功能读取值与实际状态同步；再测试最小化和关闭窗口到托盘。先仅显示或记录状态，不切音频。
2. 优先复用该官方界面状态。若读取失败、界面不存在、状态过期，判为 unknown 并保持当前输出。若需要稳定后台 API，向厂商索取 A226 的状态接口说明，优先于自行猜测 HID 报文。
3. 经明确批准后，再接入默认播放设备切换。只绑定以下两个 render endpoint，不操作录音设备、驱动、设备可见性或注册表：
   - 电脑音响 (Realtek(R) Audio)：{0.0.0.00000000}.{75DFB970-5259-48F1-B6D9-47916DC7C186}
   - 小宇耳机2.4G (HECATE G3 Max)：{0.0.0.00000000}.{3A4FE0D3-5E1C-47AD-9D9D-5E3BC5876DD2}
4. 已连接稳定后切耳机，未连接持续约 3 秒后切音响；短暂重枚举不反复切换。切换后读回默认设备验证。用户手动选择第三个设备时暂停自动接管。通信默认设备是否一起切换需单独选择。
5. SoundSwitch 的官方项目提供 CLI/profile 作为可选切换层：https://github.com/Belphemur/SoundSwitch/blob/dev/SoundSwitch.CLI/README.md 。它不能独自补上尚未验证的 HECATE 无线状态检测。未安装或测试。
6. 对指定了固定输出的游戏/语音软件，系统默认输出变化未必影响现有播放；需要单独验证应用跟随行为。

Windows IMMNotificationClient 的 OnDeviceStateChanged 反映音频端点状态，不保证反映耳机到接收器的无线链路：https://learn.microsoft.com/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immnotificationclient-ondevicestatechanged

结论：已确认设备编号、官方 HID 通道及第三方工具不匹配；尚未确认可复用的稳定状态 API，尚未实现或验证自动切换。

## 开关机对照补充

用户确认升级完成且耳机通过 2.4G 连接：主页截图显示电量 83% 与 2.4G 图标，辅助功能树能读到型号和 83%，没有明确连接状态文字。
用户随后确认只关闭耳机、保持接收器插入：主页截图显示红色“未连接到耳机”，2.4G 图标仍然存在；同次辅助功能树仍包含旧电量 83%，未暴露该离线提示。PnP 中接收器、HID COL01-COL04、音频接口及耳机音频端点仍存在且 Status=OK。日志未出现新的连接状态记录。
因此，2.4G 图标表示连接类型，不能独自判断在线；电量字段可能残留，也不能用它判定在线。仅凭目前辅助功能树文本不足以实现可靠状态检测。可见窗口截图识别是后备验证路线，但尚未验证最小化/托盘时可用性；稳定无人值守方案仍优先需要官方状态接口。尚未进行自动音频切换。

### 再次刷新后的重要更正

上一段记录的是首次读取的瞬时状态，不是稳定结果。紧接着再次读取辅助功能树，官方窗口已暴露 QLabel 文本“未连接到耳机”（元素 664），旧的 83% 不再出现在本次筛选结果中。因此已确认：关机后的明确离线状态可以通过辅助功能文本直接读取。先前“仅凭目前辅助功能树文本不足”应限定为第一次瞬时快照；状态刷新存在延迟，应等待稳定结果并防抖。仍需验证重新开机后提示消失与电量恢复，以及最小化/托盘行为。

## 重新开机对照结果

用户确认重新开机后，截图显示 2.4G 图标及电量 81%，红色“未连接到耳机”消失。第一次辅助功能读取仍返回旧离线文字，紧接着第二次读取恢复为 81%，不再有离线文字。这确认了一次完整的 开机(83%) → 关机(未连接到耳机) → 开机(81%) 的官方界面状态对照，也确认读取结果可能暂时滞后于截图。滞后可能来自界面或读取工具的缓存，不能仅由本实验归因于官方程序。

当前结论：复用官方主页的辅助功能文本具有可行性，不需要猜测 HID 报文；但还没有证明它能稳定无人值守运行。实现时需获取新鲜状态、等待连续一致结果，并把页面缺失/读取失败/状态不明确作为 unknown，不能把“未看到离线文字”直接等同于在线。最小化、托盘隐藏、程序重启及重复开关机仍待验证。未安装监控工具，未切换默认输出，未设置自启动。

## 官方日志与后台页面读取复核

在 HECATE 软件打开日志后，`C:\Users\Administrator\AppData\Roaming\HECATE\log\log_20261108.log` 记录了设备通信过程：程序枚举 `HID\VID_35BB&PID_A226&MI_03&Col01` 至 `Col04`，向 COL03 写入查询帧 `D0 35 BB 01 00`，并读到 `D0 35 BB 0A 00 01 51 ...`。`0x51` 为十进制 81，与页面电量 81% 一致；在线初始化时内部字段为 `dev_vonnect:2`。再次关机时，日志出现 `device count: 0`、随后重新枚举 COL01-COL04、`dev_vonnect:1`，读到 `D0 35 BB 0A 00 00 00 ...`（电量字节约为 0），最终 `dev_vonnect:3`。这给出了官方查询帧、响应帧及离线波形的直接证据。

日志按连接状态事件追加，而不是固定频率持续轮询。耳机在 08:25 关机时写入离线帧，在 08:32 重新开机并已将 HECATE 收进托盘后仍写入在线帧；因此日志可在窗口不可见时捕获状态变化。完整历史为 `08:23:54 offline → 08:24:00 online (81%) → 08:25:22 offline → 08:32:21 online (81%)`。文件监视适合作为后台状态源，但仍需处理软件启动时的短暂假离线、日志未启用/损坏、当天文件切换及日志写入延迟。

使用 Windows Accessibility 的 Raw View 树读取 HECATE 进程的窗口，在软件普通最小化时能取得 `HECATE G3 MAX无线版` 和 `未连接到耳机`；独立只读探针成功输出 `{"State":"offline","Source":"official UI"}`。但软件完全收进托盘后 `MainWindowHandle=0`，标准辅助功能接口找不到窗口，因此页面文字不能覆盖托盘的常见状态。页面读取适合辅助验证，日志更适合后台监测。

推荐状态判定：解析当前日期日志中最后一条 `D0 35 BB 0A` 帧；payload 第 2 字节为 `01` 才判在线，`00` 判离线；其它格式或日志超过合理时间未更新判 unknown。启动后等待至少 5 秒，离线持续约 3 秒才触发切换；在线需连续两次一致再触发。页面文字作为辅助校验。切换动作应在状态稳定后触发，并在切换后回读默认播放设备验证。

## Sonar 兼容与控制器实现

本机 SteelSeries Sonar 1.106 在 `http://127.0.0.1:8822` 提供本机控制接口：

- `GET /mode` 返回 `classic`。
- `GET /classicRedirections` 返回 game/chat/media/aux 的实际物理输出。
- `PUT /classicRedirections/render/deviceId/{deviceId}` 能把 Sonar 的四路渲染输出切换到指定物理端点。
- `GET /audioDevices` 提供 `SteelSeries Sonar - Gaming` 等虚拟端点信息。

实测把 Sonar 输出从 HECATE 切到 Realtek 后，`classicRedirections` 返回四个渲染角色均为 Realtek，路径正常。控制器随后检测到耳机在线，将 Windows 默认播放设备恢复为 `SteelSeries Sonar - Gaming`，并把 Sonar 物理输出路由到 HECATE。这样保留 Sonar 的虚拟混音和应用路由，不再绕过 Sonar 直接切物理端点。

控制器可执行文件：`outputs/HecateAutoSwitch.exe`；使用说明：`outputs/HecateAutoSwitch-使用说明.md`。

控制器在 Sonar 进程运行且本机接口可用时采用 Sonar 路径；Sonar 未运行才切换 Windows 默认端点。Sonar 正在启动、控制接口失败或使用未支持的 streamer 模式时输出模式为 unknown，保持当前设备。日志文件名每次都按当前本地日期重新计算，跨日后自动读取新的 `log_YYYYMMDD.log`，新文件不存在时安全地保持未知状态。

### 最终 Sonar 设备顺序与语音测试

Sonar 的 `/fallbackSettings/lists` 提供每个角色的实际候选顺序及 `isNext` 标记。最终控制器不再固定选择扬声器，而是在耳机关闭时读取 Sonar 给出的下一台可用设备，并同时处理：

- 渲染角色：game、chat、media、aux。
- 录音角色：mic。
- Windows 默认虚拟输出：`SteelSeries Sonar - Gaming`。

2026-11-08 实测结果：

- 耳机在线：Sonar 的 game/chat/media/aux 和 mic 均为 HECATE；Windows 默认渲染为 `SteelSeries Sonar - Gaming`。
- 耳机关闭：Sonar 渲染顺序从 HECATE 前进到 `电脑音响 (Realtek(R) Audio)`；`chatCapture` 顺序从 HECATE 麦克风前进到 `麦克风 (UGREEN Camera 1080P Audio)`。
- 耳机重新开机：四个渲染角色和 mic 全部回到 HECATE；Windows 默认仍为 Sonar Gaming。

本地模式同步处理播放和录音默认设备：播放优先恢复控制器此前观察到的非耳机输出，缺省为电脑音响；录音优先恢复此前观察到的非耳机麦克风，缺省为可用摄像头麦克风或其他非虚拟麦克风。Windows 不提供系统级设备候选顺序，此行为与 Sonar 自己维护的可排序列表不同。

通知气泡已全部移除，切换与隐藏到托盘均不调用系统气球通知。没有新增开机启动项。
