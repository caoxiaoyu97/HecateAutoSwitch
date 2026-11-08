using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace HecateAutoSwitch
{
    internal enum LogState { Unknown, Online, Offline }

    internal sealed class LogSnapshot
    {
        public LogState State = LogState.Unknown;
        public int Battery = -1;
        public string Timestamp = String.Empty;
        public string FileName = String.Empty;
        public string Reason = String.Empty;
    }

    internal sealed class LogReader
    {
        private static readonly Regex StateFrame = new Regex(
            @"\[(?<time>[^\]]+)\].*?HID\s+read\[64\]:\s*D0\s+35\s+BB\s+0A\s+(?<reserved>[0-9A-Fa-f]{2})\s+(?<online>[0-9A-Fa-f]{2})\s+(?<battery>[0-9A-Fa-f]{2})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private string cachedPath;
        private long cachedLength = -1;
        private DateTime cachedWriteTime = DateTime.MinValue;
        private LogSnapshot cachedSnapshot;

        public LogSnapshot ReadToday()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HECATE", "log");
            string path = Path.Combine(directory, "log_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            if (!File.Exists(path))
                return Unknown("今天的 HECATE 详细日志尚未生成");

            FileInfo info = new FileInfo(path);
            if (cachedSnapshot != null &&
                String.Equals(cachedPath, path, StringComparison.OrdinalIgnoreCase) &&
                cachedLength == info.Length && cachedWriteTime == info.LastWriteTimeUtc)
            {
                return cachedSnapshot;
            }

            string text;
            using (FileStream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                long length = stream.Length;
                long readLength = Math.Min(length, 2L * 1024L * 1024L);
                stream.Seek(length - readLength, SeekOrigin.Begin);
                byte[] bytes = new byte[readLength];
                int total = 0;
                while (total < bytes.Length)
                {
                    int read = stream.Read(bytes, total, bytes.Length - total);
                    if (read <= 0) break;
                    total += read;
                }
                text = Encoding.UTF8.GetString(bytes, 0, total);
            }

            MatchCollection matches = StateFrame.Matches(text);
            if (matches.Count == 0)
            {
                LogSnapshot missing = Unknown("日志中没有找到耳机状态帧");
                missing.FileName = info.Name;
                Cache(path, info, missing);
                return missing;
            }

            Match match = matches[matches.Count - 1];
            int onlineByte = Convert.ToInt32(match.Groups["online"].Value, 16);
            LogSnapshot result = new LogSnapshot();
            result.Timestamp = match.Groups["time"].Value.Trim();
            result.FileName = info.Name;
            if (onlineByte == 1)
            {
                result.State = LogState.Online;
                result.Battery = Convert.ToInt32(match.Groups["battery"].Value, 16);
            }
            else if (onlineByte == 0)
            {
                result.State = LogState.Offline;
            }
            else
            {
                result.State = LogState.Unknown;
                result.Reason = "状态帧格式尚未识别";
            }
            Cache(path, info, result);
            return result;
        }

        private void Cache(string path, FileInfo info, LogSnapshot snapshot)
        {
            cachedPath = path;
            cachedLength = info.Length;
            cachedWriteTime = info.LastWriteTimeUtc;
            cachedSnapshot = snapshot;
        }

        private static LogSnapshot Unknown(string reason)
        {
            LogSnapshot result = new LogSnapshot();
            result.Reason = reason;
            return result;
        }
    }

    internal enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    internal enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }
    [Flags] internal enum DeviceState { Active = 1, Disabled = 2, NotPresent = 4, Unplugged = 8, All = 15 }
    internal enum CLSCTX { All = 23 }
    internal enum STGM { Read = 0 }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid id, CLSCTX clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(STGM access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out DeviceState state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint propertyCount);
        [PreserveSig] int GetAt(uint propertyIndex, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string device, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device, [MarshalAs(UnmanagedType.Bool)] bool isDefault, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string device, [MarshalAs(UnmanagedType.Bool)] bool isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string device, out IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string device, ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string device, ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string device, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string device, int visible);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class CPolicyConfigClient { }

    internal sealed class DeviceInfo
    {
        public string Id;
        public string Name;
    }

    internal sealed class AudioTargets
    {
        public DeviceInfo Headset;
        public DeviceInfo Speakers;
    }

    internal enum RoutingMode { Unknown, Local, Sonar }

    internal static class SonarRouter
    {
        private const string BaseUrl = "http://127.0.0.1:8822";
        public static bool IsRunning()
        {
            Process[] processes = Process.GetProcessesByName("SteelSeriesSonar");
            try { return processes.Length > 0; }
            finally { foreach (Process process in processes) process.Dispose(); }
        }

        public static RoutingMode Detect(out string detail)
        {
            if (!IsRunning())
            {
                detail = "本地设备模式";
                return RoutingMode.Local;
            }

            try
            {
                string mode = Get("/mode").Trim().Trim('"');
                if (String.Equals(mode, "classic", StringComparison.OrdinalIgnoreCase))
                {
                    detail = "Sonar 经典模式";
                    return RoutingMode.Sonar;
                }
                detail = "Sonar 正在启动或使用当前未支持的模式：" + mode;
                return RoutingMode.Unknown;
            }
            catch (Exception ex)
            {
                detail = "Sonar 已运行但控制接口不可用：" + ex.Message;
                return RoutingMode.Unknown;
            }
        }

        public static string GetPhysicalRenderDevice()
        {
            string json = Get("/classicRedirections");
            Match match = Regex.Match(
                json,
                "\\{\\\"id\\\":\\\"game\\\",\\\"deviceId\\\":\\\"([^\\\"]+)\\\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : String.Empty;
        }

        public static Dictionary<string, string> GetPhysicalMappings()
        {
            string json = Get("/classicRedirections");
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(
                json,
                "\\\"id\\\":\\\"([^\\\"]+)\\\",\\\"deviceId\\\":\\\"([^\\\"]+)\\\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                result[match.Groups[1].Value] = match.Groups[2].Value;
            }
            return result;
        }

        public static void SetPhysicalRenderDevice(string deviceId)
        {
            Put("/classicRedirections/render/deviceId/" +
                Uri.EscapeDataString(deviceId));
            Thread.Sleep(120);
            string actual = GetPhysicalRenderDevice();
            if (!String.Equals(actual, deviceId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Sonar 未接受新的物理输出设备");
        }

        public static void SetClassicDevice(string role, string deviceId)
        {
            Put("/classicRedirections/" + Uri.EscapeDataString(role) +
                "/deviceId/" + Uri.EscapeDataString(deviceId));
            Thread.Sleep(120);
            string actual;
            if (!GetPhysicalMappings().TryGetValue(role, out actual) ||
                !String.Equals(actual, deviceId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Sonar 角色 " + role + " 切换后校验失败");
        }

        public static string GetNextFallback(string role)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> data =
                serializer.Deserialize<Dictionary<string, object>>(Get("/fallbackSettings/lists"));
            object value = null;
            foreach (KeyValuePair<string, object> entry in data)
            {
                if (String.Equals(entry.Key, role, StringComparison.OrdinalIgnoreCase))
                {
                    value = entry.Value;
                    break;
                }
            }
            if (!(value is IEnumerable))
                return String.Empty;

            foreach (object raw in (IEnumerable)value)
            {
                IDictionary<string, object> item = raw as IDictionary<string, object>;
                if (item == null) continue;
                bool isActive = item.ContainsKey("isActive") && item["isActive"] is bool && (bool)item["isActive"];
                bool isExcluded = item.ContainsKey("isExcluded") && item["isExcluded"] is bool && (bool)item["isExcluded"];
                bool isNext = item.ContainsKey("isNext") && item["isNext"] is bool && (bool)item["isNext"];
                if (isActive && !isExcluded && isNext && item.ContainsKey("id"))
                    return Convert.ToString(item["id"]);
            }
            return String.Empty;
        }

        public static DeviceInfo FindSonarVirtualOutput()
        {
            foreach (DeviceInfo device in AudioEndpoints.GetRenderDevices())
            {
                if (device.Name.IndexOf("SteelSeries Sonar - Gaming", StringComparison.OrdinalIgnoreCase) >= 0)
                    return device;
            }
            return null;
        }

        public static void EnsureSonarIsWindowsDefault()
        {
            DeviceInfo virtualOutput = FindSonarVirtualOutput();
            if (virtualOutput == null)
                throw new InvalidOperationException("未找到 SteelSeries Sonar - Gaming 虚拟输出");
            DeviceInfo current = AudioEndpoints.GetDefaultRender();
            if (current != null && current.Name.IndexOf("SteelSeries Sonar", StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            AudioEndpoints.SetDefaultRender(virtualOutput.Id);
            Thread.Sleep(120);
            current = AudioEndpoints.GetDefaultRender();
            if (current == null ||
                current.Name.IndexOf("SteelSeries Sonar", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("未能恢复 Sonar 默认播放设备");
        }

        private static string Get(string path)
        {
            HttpWebRequest request = CreateRequest(path, "GET");
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static string Put(string path)
        {
            HttpWebRequest request = CreateRequest(path, "PUT");
            request.ContentLength = 0;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static HttpWebRequest CreateRequest(string path, string method)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(BaseUrl + path);
            request.Method = method;
            request.Proxy = null;
            request.Timeout = 1200;
            request.ReadWriteTimeout = 1200;
            request.KeepAlive = false;
            request.ServicePoint.Expect100Continue = false;
            return request;
        }
    }

    internal static class AudioEndpoints
    {
        private const string HeadsetId = "{0.0.0.00000000}.{3a4fe0d3-5e1c-47ad-9d9d-5e3bc5876dd2}";
        private const string HeadsetCaptureId = "{0.0.1.00000000}.{dbe72694-a3cf-4946-9b0e-7187821f41c7}";
        private const string SpeakerId = "{0.0.0.00000000}.{75dfb970-5259-48f1-b6d9-47916dc7c186}";
        private static readonly Guid FriendlyNameFmtId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0");

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PROPVARIANT value);

        public static List<DeviceInfo> GetRenderDevices()
        {
            return GetDevices(EDataFlow.eRender);
        }

        private static List<DeviceInfo> GetDevices(EDataFlow dataFlow)
        {
            List<DeviceInfo> result = new List<DeviceInfo>();
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDeviceCollection collection = null;
            try
            {
                int hr = enumerator.EnumAudioEndpoints(dataFlow, DeviceState.Active, out collection);
                if (hr != 0) throw new COMException("无法枚举播放设备", hr);
                uint count;
                hr = collection.GetCount(out count);
                if (hr != 0) throw new COMException("无法读取播放设备数量", hr);
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    try
                    {
                        hr = collection.Item(i, out device);
                        if (hr != 0 || device == null) continue;
                        string id;
                        device.GetId(out id);
                        result.Add(new DeviceInfo { Id = id, Name = GetFriendlyName(device) });
                    }
                    finally
                    {
                        if (device != null) Marshal.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                if (collection != null) Marshal.ReleaseComObject(collection);
                Marshal.ReleaseComObject(enumerator);
            }
            return result;
        }

        public static AudioTargets FindTargets()
        {
            List<DeviceInfo> devices = GetRenderDevices();
            AudioTargets targets = new AudioTargets();
            foreach (DeviceInfo device in devices)
            {
                if (String.Equals(device.Id, HeadsetId, StringComparison.OrdinalIgnoreCase) ||
                    (device.Name.IndexOf("HECATE G3 Max", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     device.Name.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) >= 0))
                    targets.Headset = device;
                if (String.Equals(device.Id, SpeakerId, StringComparison.OrdinalIgnoreCase) ||
                    (device.Name.IndexOf("电脑音响", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     device.Name.IndexOf("Realtek", StringComparison.OrdinalIgnoreCase) >= 0))
                    targets.Speakers = device;
            }
            return targets;
        }

        public static DeviceInfo GetDefaultRender()
        {
            return GetDefault(EDataFlow.eRender);
        }

        public static DeviceInfo GetDefaultCapture()
        {
            return GetDefault(EDataFlow.eCapture);
        }

        private static DeviceInfo GetDefault(EDataFlow dataFlow)
        {
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice device = null;
            try
            {
                int hr = enumerator.GetDefaultAudioEndpoint(dataFlow, ERole.eConsole, out device);
                if (hr != 0) throw new COMException("无法读取当前默认播放设备", hr);
                string id;
                device.GetId(out id);
                return new DeviceInfo { Id = id, Name = GetFriendlyName(device) };
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
            }
        }

        public static void SetDefaultRender(string id)
        {
            SetDefault(id, EDataFlow.eRender);
        }

        public static void SetDefaultCapture(string id)
        {
            SetDefault(id, EDataFlow.eCapture);
        }

        private static void SetDefault(string id, EDataFlow dataFlow)
        {
            IPolicyConfig policy = (IPolicyConfig)new CPolicyConfigClient();
            try
            {
                ERole[] roles = { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications };
                foreach (ERole role in roles)
                {
                    int hr = policy.SetDefaultEndpoint(id, role);
                    if (hr != 0) throw new COMException("切换播放设备失败（" + role + "）", hr);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
        }

        public static List<DeviceInfo> GetCaptureDevices()
        {
            return GetDevices(EDataFlow.eCapture);
        }

        public static DeviceInfo FindHeadsetCapture()
        {
            foreach (DeviceInfo device in GetCaptureDevices())
            {
                if (String.Equals(device.Id, HeadsetCaptureId, StringComparison.OrdinalIgnoreCase) ||
                    device.Name.IndexOf("HECATE G3 Max", StringComparison.OrdinalIgnoreCase) >= 0)
                    return device;
            }
            return null;
        }

        public static DeviceInfo FindFallbackCapture()
        {
            DeviceInfo camera = null;
            DeviceInfo other = null;
            foreach (DeviceInfo device in GetCaptureDevices())
            {
                if (String.Equals(device.Id, HeadsetCaptureId, StringComparison.OrdinalIgnoreCase) ||
                    device.Name.IndexOf("HECATE G3 Max", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    device.Name.IndexOf("Sonar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    device.Name.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    device.Name.IndexOf("ToDesk", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (device.Name.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    camera = device;
                    break;
                }
                if (other == null) other = device;
            }
            return camera ?? other;
        }

        private static string GetFriendlyName(IMMDevice device)
        {
            IPropertyStore store = null;
            try
            {
                int hr = device.OpenPropertyStore(STGM.Read, out store);
                if (hr != 0) throw new COMException("无法读取设备属性", hr);
                PROPERTYKEY key = new PROPERTYKEY { fmtid = FriendlyNameFmtId, pid = 14 };
                PROPVARIANT value;
                hr = store.GetValue(ref key, out value);
                if (hr != 0) throw new COMException("无法读取设备名称", hr);
                try { return Marshal.PtrToStringUni(value.pointerValue); }
                finally { PropVariantClear(ref value); }
            }
            finally
            {
                if (store != null) Marshal.ReleaseComObject(store);
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly LogReader logReader = new LogReader();
        private readonly System.Windows.Forms.Timer monitorTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip trayMenu;
        private readonly ToolStripMenuItem trayAuto;
        private readonly Label stateLabel;
        private readonly Label detailLabel;
        private readonly Label modeLabel;
        private readonly Label currentLabel;
        private readonly Label captureLabel;
        private readonly Label headsetLabel;
        private readonly Label speakersLabel;
        private readonly Label microphoneLabel;
        private readonly Label footerLabel;
        private readonly CheckBox autoCheck;
        private readonly Button headsetButton;
        private readonly Button speakerButton;
        private readonly Button refreshButton;

        private LogState candidateState = LogState.Unknown;
        private LogState stableState = LogState.Unknown;
        private LogState appliedState = LogState.Unknown;
        private RoutingMode routingMode = RoutingMode.Unknown;
        private RoutingMode appliedRoutingMode = RoutingMode.Unknown;
        private string routingDetail = "检测输出模式";
        private DateTime lastRoutingCheck = DateTime.MinValue;
        private DateTime candidateSince = DateTime.UtcNow;
        private DateTime nextAutoAttempt = DateTime.MinValue;
        private LogSnapshot lastSnapshot;
        private AudioTargets targets;
        private DeviceInfo currentDefault;
        private DeviceInfo currentCapture;
        private DeviceInfo lastFallbackRender;
        private DeviceInfo lastFallbackCapture;
        private bool exiting;
        private bool logFooterInitialized;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        public MainForm()
        {
            SetProcessDPIAware();
            Text = "HECATE G3 Max 自动切换";
            ClientSize = new Size(500, 430);
            MinimumSize = new Size(516, 469);
            MaximumSize = new Size(516, 469);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 29, 35);
            ForeColor = Color.FromArgb(238, 244, 247);
            Font = new Font("Microsoft YaHei UI", 9F);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            Panel header = new Panel();
            header.SetBounds(0, 0, 500, 69);
            header.BackColor = Color.FromArgb(16, 20, 25);
            Controls.Add(header);

            Label title = new Label();
            title.Text = "HECATE G3 Max";
            title.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(245, 249, 250);
            title.AutoSize = true;
            title.Location = new Point(22, 12);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "2.4G 状态监视 · 播放与麦克风路由";
            subtitle.ForeColor = Color.FromArgb(145, 159, 169);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(24, 42);
            header.Controls.Add(subtitle);

            modeLabel = new Label();
            modeLabel.Text = "检测模式";
            modeLabel.TextAlign = ContentAlignment.MiddleRight;
            modeLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            modeLabel.ForeColor = Color.FromArgb(151, 164, 173);
            modeLabel.SetBounds(360, 17, 114, 24);
            header.Controls.Add(modeLabel);

            Panel card = new Panel();
            card.SetBounds(18, 84, 464, 94);
            card.BackColor = Color.FromArgb(36, 44, 52);
            Controls.Add(card);

            stateLabel = new Label();
            stateLabel.Text = "●  正在读取官方状态";
            stateLabel.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
            stateLabel.ForeColor = Color.FromArgb(153, 166, 175);
            stateLabel.AutoSize = true;
            stateLabel.Location = new Point(18, 14);
            card.Controls.Add(stateLabel);

            detailLabel = new Label();
            detailLabel.Text = "等待 HECATE 详细日志";
            detailLabel.ForeColor = Color.FromArgb(190, 202, 210);
            detailLabel.AutoSize = true;
            detailLabel.Location = new Point(20, 55);
            card.Controls.Add(detailLabel);

            autoCheck = new CheckBox();
            autoCheck.Text = "自动跟随耳机状态";
            autoCheck.Checked = true;
            autoCheck.AutoSize = true;
            autoCheck.ForeColor = Color.FromArgb(230, 238, 242);
            autoCheck.Location = new Point(21, 195);
            autoCheck.CheckedChanged += delegate
            {
                appliedState = LogState.Unknown;
                appliedRoutingMode = RoutingMode.Unknown;
                nextAutoAttempt = DateTime.MinValue;
                footerLabel.Text = autoCheck.Checked ? "自动跟随已启用" : "自动跟随已暂停";
            };
            Controls.Add(autoCheck);

            headsetButton = MakeButton("切换到耳机", 21, 229, 212, 42);
            headsetButton.Click += delegate { ManualSwitch(true); };
            Controls.Add(headsetButton);

            speakerButton = MakeButton("切换到电脑音响", 246, 229, 212, 42);
            speakerButton.Click += delegate { ManualSwitch(false); };
            Controls.Add(speakerButton);

            Label targetsTitle = new Label();
            targetsTitle.Text = "设备识别";
            targetsTitle.ForeColor = Color.FromArgb(119, 135, 145);
            targetsTitle.AutoSize = true;
            targetsTitle.Location = new Point(21, 284);
            Controls.Add(targetsTitle);

            headsetLabel = new Label();
            headsetLabel.SetBounds(21, 305, 342, 20);
            headsetLabel.ForeColor = Color.FromArgb(188, 201, 209);
            headsetLabel.AutoEllipsis = true;
            Controls.Add(headsetLabel);

            speakersLabel = new Label();
            speakersLabel.SetBounds(21, 326, 342, 20);
            speakersLabel.ForeColor = Color.FromArgb(188, 201, 209);
            speakersLabel.AutoEllipsis = true;
            Controls.Add(speakersLabel);

            microphoneLabel = new Label();
            microphoneLabel.SetBounds(21, 347, 342, 20);
            microphoneLabel.ForeColor = Color.FromArgb(188, 201, 209);
            microphoneLabel.AutoEllipsis = true;
            Controls.Add(microphoneLabel);

            refreshButton = MakeButton("刷新", 376, 303, 82, 38);
            refreshButton.Click += delegate { RefreshDeviceTargets(); };
            Controls.Add(refreshButton);

            currentLabel = new Label();
            currentLabel.SetBounds(21, 369, 440, 18);
            currentLabel.ForeColor = Color.FromArgb(150, 166, 175);
            currentLabel.AutoEllipsis = true;
            Controls.Add(currentLabel);

            captureLabel = new Label();
            captureLabel.SetBounds(21, 387, 440, 18);
            captureLabel.ForeColor = Color.FromArgb(150, 166, 175);
            captureLabel.AutoEllipsis = true;
            Controls.Add(captureLabel);

            footerLabel = new Label();
            footerLabel.SetBounds(21, 407, 456, 17);
            footerLabel.ForeColor = Color.FromArgb(111, 128, 138);
            footerLabel.AutoEllipsis = true;
            Controls.Add(footerLabel);

            trayMenu = new ContextMenuStrip();
            trayMenu.ShowImageMargin = false;
            ToolStripMenuItem showItem = new ToolStripMenuItem("显示控制器");
            showItem.Click += delegate { ShowFromTray(); };
            trayMenu.Items.Add(showItem);
            trayAuto = new ToolStripMenuItem("自动跟随");
            trayAuto.CheckOnClick = false;
            trayAuto.Click += delegate { autoCheck.Checked = !autoCheck.Checked; };
            trayMenu.Items.Add(trayAuto);
            trayMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem headsetItem = new ToolStripMenuItem("切换到耳机");
            headsetItem.Click += delegate { ManualSwitch(true); };
            trayMenu.Items.Add(headsetItem);
            ToolStripMenuItem speakerItem = new ToolStripMenuItem("切换到电脑音响");
            speakerItem.Click += delegate { ManualSwitch(false); };
            trayMenu.Items.Add(speakerItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { ExitApplication(); };
            trayMenu.Items.Add(exitItem);
            trayMenu.Opening += delegate { trayAuto.Checked = autoCheck.Checked; };

            tray = new NotifyIcon();
            tray.Icon = Icon;
            tray.Text = "HECATE G3 Max 自动切换";
            tray.Visible = true;
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += delegate { ShowFromTray(); };

            FormClosing += OnClosing;
            Resize += delegate { if (WindowState == FormWindowState.Minimized) HideToTray(); };
            Shown += delegate { RefreshDeviceTargets(); };

            monitorTimer.Interval = 500;
            monitorTimer.Tick += delegate { PollStatus(); };
            monitorTimer.Start();

            refreshTimer.Interval = 2000;
            refreshTimer.Tick += delegate { RefreshCurrentDefault(); };
            refreshTimer.Start();
        }

        private Button MakeButton(string text, int x, int y, int width, int height)
        {
            Button button = new Button();
            button.Text = text;
            button.SetBounds(x, y, width, height);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(70, 85, 96);
            button.BackColor = Color.FromArgb(39, 48, 57);
            button.ForeColor = Color.FromArgb(237, 243, 246);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void PollStatus()
        {
            try
            {
                LogSnapshot snapshot;
                RefreshRoutingMode();
                if (!IsHecateRunning())
                {
                    snapshot = new LogSnapshot
                    {
                        State = LogState.Unknown,
                        Reason = "HECATE 软件未运行"
                    };
                }
                else
                {
                    snapshot = logReader.ReadToday();
                }
                lastSnapshot = snapshot;
                UpdateState(snapshot);
            }
            catch (Exception ex)
            {
                SetUnknown("读取失败：" + ex.Message);
            }
        }

        private void UpdateState(LogSnapshot snapshot)
        {
            DateTime now = DateTime.UtcNow;
            if (snapshot.State != candidateState)
            {
                candidateState = snapshot.State;
                candidateSince = now;
            }
            else if (candidateState != stableState &&
                     (now - candidateSince).TotalSeconds >= 3.0)
            {
                stableState = candidateState;
            }

            if (snapshot.State == LogState.Unknown)
            {
                SetUnknown(snapshot.Reason);
            }
            else if (stableState != snapshot.State)
            {
                stateLabel.Text = "●  正在确认状态";
                stateLabel.ForeColor = Color.FromArgb(240, 181, 74);
                detailLabel.Text = snapshot.State == LogState.Online
                    ? "检测到耳机在线，等待稳定"
                    : "检测到耳机离线，等待稳定";
            }
            else if (snapshot.State == LogState.Online)
            {
                stateLabel.Text = "●  耳机在线";
                stateLabel.ForeColor = Color.FromArgb(71, 222, 183);
                detailLabel.Text = "2.4G 已连接" +
                    (snapshot.Battery >= 0 ? " · 电量 " + snapshot.Battery + "%" : "") +
                    " · " + snapshot.Timestamp;
            }
            else
            {
                stateLabel.Text = "●  耳机离线";
                stateLabel.ForeColor = Color.FromArgb(255, 111, 119);
                detailLabel.Text = "2.4G 未连接 · " + snapshot.Timestamp;
            }

            if (snapshot.FileName.Length > 0 && !logFooterInitialized)
            {
                footerLabel.Text = snapshot.FileName + " · 只读监听";
                logFooterInitialized = true;
            }

            if (autoCheck.Checked && stableState != LogState.Unknown &&
                routingMode != RoutingMode.Unknown &&
                (appliedState != stableState || appliedRoutingMode != routingMode) &&
                now >= nextAutoAttempt)
            {
                if (ApplyState(stableState, true))
                {
                    appliedState = stableState;
                    appliedRoutingMode = routingMode;
                }
                else
                    nextAutoAttempt = now.AddSeconds(5);
            }
        }

        private void SetUnknown(string reason)
        {
            stateLabel.Text = "●  状态未知";
            stateLabel.ForeColor = Color.FromArgb(151, 164, 173);
            detailLabel.Text = String.IsNullOrEmpty(reason) ? "等待官方日志中的状态帧" : reason;
        }

        private bool ApplyState(LogState state, bool automatic)
        {
            try
            {
                RefreshDeviceTargets();
                DeviceInfo headset = targets.Headset;
                DeviceInfo headsetMic = AudioEndpoints.FindHeadsetCapture();
                if (headset == null || (state == LogState.Online && headsetMic == null))
                {
                    footerLabel.Text = "未找到 HECATE 播放或录音设备";
                    return false;
                }

                if (routingMode == RoutingMode.Sonar)
                    return ApplySonarState(state, headset, headsetMic, automatic);
                if (routingMode != RoutingMode.Local)
                {
                    footerLabel.Text = "输出模式未就绪，已保持当前设备";
                    return false;
                }
                return ApplyLocalState(state, headset, headsetMic, automatic);
            }
            catch (Exception ex)
            {
                footerLabel.Text = "切换失败：" + ex.Message;
                return false;
            }
        }

        private bool ApplySonarState(LogState state, DeviceInfo headset, DeviceInfo headsetMic, bool automatic)
        {
            SonarRouter.EnsureSonarIsWindowsDefault();
            Dictionary<string, string> mappings = SonarRouter.GetPhysicalMappings();
            string currentRender;
            mappings.TryGetValue("game", out currentRender);
            string currentMic;
            mappings.TryGetValue("mic", out currentMic);

            string renderTarget;
            string captureTarget;
            if (state == LogState.Online)
            {
                renderTarget = headset.Id;
                captureTarget = headsetMic.Id;
            }
            else
            {
                renderTarget = String.Equals(currentRender, headset.Id, StringComparison.OrdinalIgnoreCase)
                    ? SonarRouter.GetNextFallback("game")
                    : currentRender;
                if (String.IsNullOrEmpty(renderTarget))
                    renderTarget = targets.Speakers == null ? String.Empty : targets.Speakers.Id;
                captureTarget = String.Equals(currentMic, headsetMic.Id, StringComparison.OrdinalIgnoreCase)
                    ? SonarRouter.GetNextFallback("chatCapture")
                    : currentMic;
                if (String.IsNullOrEmpty(captureTarget))
                    captureTarget = SonarRouter.GetNextFallback("chatCapture");
            }

            if (!String.IsNullOrEmpty(renderTarget) &&
                !String.Equals(currentRender, renderTarget, StringComparison.OrdinalIgnoreCase))
                SonarRouter.SetPhysicalRenderDevice(renderTarget);
            if (!String.IsNullOrEmpty(captureTarget) &&
                !String.Equals(currentMic, captureTarget, StringComparison.OrdinalIgnoreCase))
                SonarRouter.SetClassicDevice("mic", captureTarget);

            Thread.Sleep(120);
            RefreshCurrentDefault();
            footerLabel.Text = (automatic ? "Sonar 已自动路由" : "Sonar 已路由") +
                "：输出 " + ResolveDeviceName(renderTarget) +
                " · 麦克风 " + ResolveDeviceName(captureTarget);
            return true;
        }

        private bool ApplyLocalState(LogState state, DeviceInfo headset, DeviceInfo headsetMic, bool automatic)
        {
            DeviceInfo renderTarget;
            DeviceInfo captureTarget;
            if (state == LogState.Online)
            {
                renderTarget = headset;
                captureTarget = headsetMic;
            }
            else
            {
                renderTarget = IsHeadsetRender(currentDefault) ?
                    (lastFallbackRender ?? targets.Speakers) : currentDefault;
                captureTarget = IsHeadsetCapture(currentCapture) ?
                    (lastFallbackCapture ?? AudioEndpoints.FindFallbackCapture()) : currentCapture;
            }

            if (renderTarget != null &&
                (currentDefault == null || !String.Equals(currentDefault.Id, renderTarget.Id, StringComparison.OrdinalIgnoreCase)))
                AudioEndpoints.SetDefaultRender(renderTarget.Id);
            if (captureTarget != null &&
                (currentCapture == null || !String.Equals(currentCapture.Id, captureTarget.Id, StringComparison.OrdinalIgnoreCase)))
                AudioEndpoints.SetDefaultCapture(captureTarget.Id);

            Thread.Sleep(120);
            RefreshCurrentDefault();
            if (renderTarget != null && (currentDefault == null ||
                !String.Equals(currentDefault.Id, renderTarget.Id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("切换后播放设备校验失败");
            if (captureTarget != null && (currentCapture == null ||
                !String.Equals(currentCapture.Id, captureTarget.Id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("切换后录音设备校验失败");

            footerLabel.Text = (automatic ? "已自动切换" : "已切换") +
                "：输出 " + (renderTarget == null ? "保持" : renderTarget.Name) +
                " · 麦克风 " + (captureTarget == null ? "保持" : captureTarget.Name);
            return true;
        }

        private static bool IsHeadsetRender(DeviceInfo device)
        {
            return device != null &&
                (device.Name.IndexOf("HECATE G3 Max", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 String.Equals(device.Id, "{0.0.0.00000000}.{3a4fe0d3-5e1c-47ad-9d9d-5e3bc5876dd2}", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsHeadsetCapture(DeviceInfo device)
        {
            return device != null &&
                (device.Name.IndexOf("HECATE G3 Max", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 String.Equals(device.Id, "{0.0.1.00000000}.{dbe72694-a3cf-4946-9b0e-7187821f41c7}", StringComparison.OrdinalIgnoreCase));
        }

        private string ResolveDeviceName(string id)
        {
            if (String.IsNullOrEmpty(id)) return "保持";
            foreach (DeviceInfo device in AudioEndpoints.GetRenderDevices())
                if (String.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase)) return device.Name;
            foreach (DeviceInfo device in AudioEndpoints.GetCaptureDevices())
                if (String.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase)) return device.Name;
            return id;
        }

        private void ManualSwitch(bool headset)
        {
            if (headset) ApplyState(LogState.Online, false);
            else ApplyState(LogState.Offline, false);
        }

        private void RefreshDeviceTargets()
        {
            try
            {
                targets = AudioEndpoints.FindTargets();
                headsetLabel.Text = "耳机：" + (targets.Headset == null ? "未检测到" : targets.Headset.Name);
                speakersLabel.Text = "音响：" + (targets.Speakers == null ? "未检测到" : targets.Speakers.Name);
                DeviceInfo headsetMic = AudioEndpoints.FindHeadsetCapture();
                DeviceInfo fallbackMic = AudioEndpoints.FindFallbackCapture();
                microphoneLabel.Text = "麦克风：" + (headsetMic == null ? "未检测到耳机麦克风" : headsetMic.Name) +
                    (fallbackMic == null ? "" : " → " + fallbackMic.Name);
                headsetButton.Enabled = targets.Headset != null;
                speakerButton.Enabled = targets.Speakers != null;
                RefreshCurrentDefault();
            }
            catch (Exception ex)
            {
                headsetLabel.Text = "耳机：设备读取失败";
                speakersLabel.Text = "音响：" + ex.Message;
            }
        }

        private void RefreshRoutingMode()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - lastRoutingCheck).TotalSeconds < 2.0)
            {
                UpdateRoutingBadge();
                return;
            }
            lastRoutingCheck = now;
            RoutingMode previous = routingMode;
            routingMode = SonarRouter.Detect(out routingDetail);
            if (previous != routingMode)
            {
                appliedRoutingMode = RoutingMode.Unknown;
                nextAutoAttempt = DateTime.MinValue;
                if (routingMode == RoutingMode.Sonar)
                    footerLabel.Text = "已检测到 Sonar，将通过 Sonar 路由物理输出";
                else if (routingMode == RoutingMode.Local)
                    footerLabel.Text = "Sonar 未启用，将直接切换 Windows 默认设备";
                else
                    footerLabel.Text = routingDetail;
            }
            UpdateRoutingBadge();
        }

        private void UpdateRoutingBadge()
        {
            if (routingMode == RoutingMode.Sonar)
            {
                modeLabel.Text = "SONAR";
                modeLabel.ForeColor = Color.FromArgb(71, 222, 183);
            }
            else if (routingMode == RoutingMode.Local)
            {
                modeLabel.Text = "本地设备";
                modeLabel.ForeColor = Color.FromArgb(103, 181, 235);
            }
            else
            {
                modeLabel.Text = "模式未知";
                modeLabel.ForeColor = Color.FromArgb(240, 181, 74);
            }
        }

        private void RefreshCurrentDefault()
        {
            try
            {
                currentDefault = AudioEndpoints.GetDefaultRender();
                currentCapture = AudioEndpoints.GetDefaultCapture();
                if (routingMode == RoutingMode.Local && !IsHeadsetRender(currentDefault)) lastFallbackRender = currentDefault;
                if (routingMode == RoutingMode.Local && !IsHeadsetCapture(currentCapture) &&
                    currentCapture != null &&
                    currentCapture.Name.IndexOf("Sonar", StringComparison.OrdinalIgnoreCase) < 0)
                    lastFallbackCapture = currentCapture;
                string route = routingMode == RoutingMode.Sonar ? "Sonar 虚拟输出" : "Windows 默认输出";
                currentLabel.Text = route + "：" + (currentDefault == null ? "未知" : currentDefault.Name);
                captureLabel.Text = "录音默认：" + (currentCapture == null ? "未知" : currentCapture.Name);
            }
            catch
            {
                currentLabel.Text = "当前输出：读取失败";
                captureLabel.Text = "录音默认：读取失败";
            }
        }

        private static bool IsHecateRunning()
        {
            Process[] processes = Process.GetProcessesByName("HECATE");
            try { return processes.Length > 0; }
            finally { foreach (Process process in processes) process.Dispose(); }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!exiting)
            {
                e.Cancel = true;
                HideToTray();
            }
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
        }

        private void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void ExitApplication()
        {
            exiting = true;
            monitorTimer.Stop();
            refreshTimer.Stop();
            tray.Visible = false;
            tray.Dispose();
            Close();
            Application.Exit();
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show(e.Exception.Message, "HECATE 自动切换", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new MainForm());
        }
    }
}
