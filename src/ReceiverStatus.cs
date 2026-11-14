using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HecateAutoSwitch
{
    internal static class ReceiverStatus
    {
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("cfgmgr32.dll", CharSet=CharSet.Unicode)]
        static extern int CM_Get_Device_Interface_List_SizeW(out uint size, ref Guid guid, string device, uint flags);
        [DllImport("cfgmgr32.dll", CharSet=CharSet.Unicode)]
        static extern int CM_Get_Device_Interface_ListW(ref Guid guid, string device, [Out] char[] data, uint size, uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, [Out] ushort[] caps);

        internal static LogSnapshot Read()
        {
            Guid guid;
            HidD_GetHidGuid(out guid);
            uint size;
            if (CM_Get_Device_Interface_List_SizeW(out size, ref guid, null, 0) != 0)
                throw new IOException("无法枚举 USB 接收器");
            char[] paths = new char[size];
            if (CM_Get_Device_Interface_ListW(ref guid, null, paths, size, 0) != 0)
                throw new IOException("无法读取 USB 接收器列表");
            string match = null;
            foreach (string path in new string(paths).Split('\0'))
            {
                if (path.IndexOf("hid#vid_35bb&pid_a226&mi_03&col03#", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (match != null) throw new IOException("检测到多个 G3 Max 接收器");
                match = path;
            }
            if (match == null) throw new IOException("未找到 G3 Max 2.4G 接收器");

            using (SafeFileHandle handle = CreateFile(match, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new IOException("无法打开接收器状态接口");
                IntPtr data;
                if (!HidD_GetPreparsedData(handle, out data)) throw new IOException("无法读取接收器报文格式");
                ushort[] caps = new ushort[32];
                try
                {
                    if (HidP_GetCaps(data, caps) != 0x110000 || caps[2] < 7 || caps[3] < 5)
                        throw new IOException("接收器报文格式不匹配");
                }
                finally { HidD_FreePreparsedData(data); }

                using (FileStream stream = new FileStream(handle, FileAccess.ReadWrite, 1, true))
                {
                    byte[] query = new byte[caps[3]];
                    Array.Copy(new byte[] { 0xD0, 0x35, 0xBB, 1, 0 }, query, 5);
                    var write = stream.WriteAsync(query, 0, query.Length);
                    if (!write.Wait(400)) throw new IOException("接收器查询超时");
                    byte[] report = new byte[caps[2]];
                    var deadline = System.Diagnostics.Stopwatch.StartNew();
                    while (deadline.ElapsedMilliseconds < 650)
                    {
                        var read = stream.ReadAsync(report, 0, report.Length);
                        if (!read.Wait(Math.Max(1, 650 - (int)deadline.ElapsedMilliseconds))) break;
                        if (read.Result == 0) break;
                        int i = report[0] == 0 ? 1 : 0;
                        if (read.Result < i + 7 || report[i] != 0xD0 || report[i+1] != 0x35 ||
                            report[i+2] != 0xBB || report[i+3] != 0x0A || report[i+4] != 0) continue;
                        if (report[i+5] > 1 || report[i+6] > 100) continue;
                        return new LogSnapshot {
                            State = report[i+5] == 1 ? LogState.Online : LogState.Offline,
                            Battery = report[i+5] == 1 ? report[i+6] : -1,
                            Timestamp = DateTime.Now.ToString("HH:mm:ss")
                        };
                    }
                    throw new IOException("接收器未返回有效状态");
                }
            }
        }
    }
}
