using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpPcap;

namespace DrunkDeer.ProtocolAnalyzer.Capture;

/// <summary>
/// Live USB capture via Npcap + USBPcap driver.
/// Produces the same <c>(LinkType, TimestampUs, Data)</c> tuples as <see cref="PcapFileReader"/>
/// so the downstream pipeline (UsbPcapDecoder -> SessionAnalyzer -> NdjsonLog) is unchanged.
/// <para>
/// Requires: Npcap installed with USBPcap support enabled (<see href="https://npcap.com/"/>).
/// </para>
/// </summary>
public static class LiveUsbCapture
{
    // ── Device discovery ──────────────────────────────────────────────────────

    /// <param name="IsUsbPcap">
    /// Heuristic guess based on name/description containing "usb", or explicitly probed as a
    /// <c>\\.\USBPcapN</c> path. Use as a display hint only; any device name can be passed
    /// to <see cref="Capture"/>.
    /// </param>
    public sealed record DeviceInfo(string Name, string Description, bool IsUsbPcap);

    /// <summary>
    /// Returns <b>all</b> Npcap capture interfaces, each tagged with a <c>IsUsbPcap</c> hint.
    /// Also probes <c>\\.\USBPcap1</c> – <c>\\.\USBPcap9</c> directly because
    /// <c>pcap_findalldevs</c> does not enumerate USBPcap devices on all Npcap configurations.
    /// Throws <see cref="InvalidOperationException"/> if Npcap is not installed.
    /// </summary>
    public static IReadOnlyList<DeviceInfo> ListDevices()
    {
        List<DeviceInfo> results;
        try
        {
            results = CaptureDeviceList.Instance
                .Select(d => new DeviceInfo(d.Name, d.Description ?? d.Name, LooksLikeUsb(d)))
                .ToList();
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "Npcap is not installed or its DLLs are not on PATH. " +
                "Download from https://npcap.com/ and reinstall with 'USBPcap' checked.", ex);
        }

        // pcap_findalldevs does not always include USBPcap devices.
        // Probe \\.\USBPcap1..9 directly via CreateFile; add any that exist and are not
        // already in the list returned above.
        var knownNames = results.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i <= 9; i++)
        {
            string path = $"\\\\.\\USBPcap{i}";
            if (!knownNames.Contains(path) && WinDeviceExists(path))
                results.Add(new DeviceInfo(path, $"USBPcap{i} (probed)", IsUsbPcap: true));
        }

        return results;
    }

    // ── Capture loop ──────────────────────────────────────────────────────────

    /// <summary>
    /// Opens <paramref name="deviceName"/> and streams packets until <paramref name="ct"/> is cancelled.
    /// Devices not returned by <c>pcap_findalldevs</c> (common for USBPcap) are opened directly
    /// via <c>pcap_open</c> P/Invoke so that <c>\\.\USBPcap1</c> etc. always work.
    /// </summary>
    /// <exception cref="ArgumentException">When the named device cannot be opened.</exception>
    /// <exception cref="InvalidOperationException">When Npcap is not installed.</exception>
    public static IEnumerable<(uint LinkType, long TimestampUs, byte[] Data)> Capture(
        string deviceName, CancellationToken ct)
    {
        ICaptureDevice? sharpDevice;
        try
        {
            sharpDevice = CaptureDeviceList.Instance
                .FirstOrDefault(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException("Npcap is not installed.", ex);
        }

        if (sharpDevice is not null)
        {
            // Device is in the normal pcap device list — use SharpPcap's managed path.
            // read_timeout: how long GetNextPacket blocks before returning ReadTimeout.
            sharpDevice.Open(DeviceModes.None, read_timeout: 250);
            try
            {
                uint linkType = (uint)sharpDevice.LinkType;
                while (!ct.IsCancellationRequested)
                {
                    var status = sharpDevice.GetNextPacket(out var capture);
                    if (status == GetPacketStatus.PacketRead)
                    {
                        var raw = capture.GetPacket();
                        long ts = (long)raw.Timeval.Seconds * 1_000_000 + (long)raw.Timeval.MicroSeconds;
                        yield return (linkType, ts, raw.Data);
                    }
                    else if (status == GetPacketStatus.Error)
                        break;
                }
            }
            finally { sharpDevice.Close(); }
        }
        else
        {
            // Device is not in pcap_findalldevs (typical for USBPcap on some Npcap builds).
            // pcap_open/pcap_open_live only work for devices returned by pcap_findalldevs;
            // USBPcap devices are accessed via USBPcapCMD.exe (same mechanism Wireshark uses).
            foreach (var packet in CaptureViaUsbPcapCmd(deviceName, ct))
                yield return packet;
        }
    }

    // ── USBPcapCMD.exe path ───────────────────────────────────────────────────
    // Wireshark accesses USBPcap devices via extcap (USBPcapCMD.exe), not through
    // the pcap/Npcap driver stack. We do the same: spawn USBPcapCMD.exe and read
    // its raw PCAP stdout stream.

    static IEnumerable<(uint LinkType, long TimestampUs, byte[] Data)> CaptureViaUsbPcapCmd(
        string devicePath, CancellationToken ct)
    {
        string? exe = FindUsbPcapCmd();
        if (exe is null)
            throw new ArgumentException(
                $"USBPcapCMD.exe not found. Ensure USBPcap is installed. " +
                $"Expected at: C:\\Program Files\\USBPcap\\USBPcapCMD.exe");

        var psi = new ProcessStartInfo(exe)
        {
            // -d: USBPcap device path  -o -: write PCAP to stdout  -A: capture all ports on bus
            Arguments        = $"-d \"{devicePath}\" -o - -A",
            UseShellExecute  = false,
            RedirectStandardOutput = true,
            CreateNoWindow   = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start USBPcapCMD.exe");

        ct.Register(() => { try { proc.Kill(); } catch { } });

        using var stdout = proc.StandardOutput.BaseStream;
        foreach (var packet in PcapFileReader.ReadStream(stdout))
            yield return packet;
    }

    static string? FindUsbPcapCmd() =>
        new[]
        {
            @"C:\Program Files\USBPcap\USBPcapCMD.exe",
            @"C:\Program Files (x86)\USBPcap\USBPcapCMD.exe",
        }.FirstOrDefault(File.Exists);

    // ── Helpers ───────────────────────────────────────────────────────────────

    static bool LooksLikeUsb(ICaptureDevice d) =>
        d.Name.Contains("usb", StringComparison.OrdinalIgnoreCase) ||
        (d.Description?.Contains("usb", StringComparison.OrdinalIgnoreCase) ?? false);

    // ── Win32 / Npcap P/Invokes ───────────────────────────────────────────────

    // File.Exists does not work for \\.\DevicePaths. CreateFile with zero desired-access
    // and OPEN_EXISTING will succeed iff the device exists, even without read/write permission.
    static bool WinDeviceExists(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var h = CreateFileW(path, dwDesiredAccess: 0,
                            dwShareMode: 7 /* READ|WRITE|DELETE */,
                            IntPtr.Zero, dwCreationDisposition: 3 /* OPEN_EXISTING */,
                            dwFlagsAndAttributes: 0, IntPtr.Zero);
        if (h == new IntPtr(-1)) return false;
        CloseHandle(h);
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
                                     IntPtr lpSecurityAttributes, uint dwCreationDisposition,
                                     uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

}
