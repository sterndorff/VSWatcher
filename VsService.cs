using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace VSWatcher;

public record VsSnapshot(int Pid, string Name, bool Running);

[ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IOleMessageFilter
{
    [PreserveSig] int HandleInComingCall(int callType, nint taskCaller, int tickCount, nint interfaceInfo);
    [PreserveSig] int RetryRejectedCall(nint taskCallee, int tickCount, int rejectType);
    [PreserveSig] int MessagePending(nint taskCallee, int tickCount, int pendingType);
}

class MessageFilter : IOleMessageFilter
{
    [DllImport("ole32.dll")] static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter? oldFilter);
    [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable rot);
    [DllImport("ole32.dll")] static extern int CreateBindCtx(int reserved, out IBindCtx ctx);

    public static void Register() => CoRegisterMessageFilter(new MessageFilter(), out _);

    public int HandleInComingCall(int callType, nint taskCaller, int tickCount, nint interfaceInfo) => 0;
    public int RetryRejectedCall(nint taskCallee, int tickCount, int rejectType) =>
        rejectType == 2 && tickCount < 3000 ? 100 : -1;
    public int MessagePending(nint taskCallee, int tickCount, int pendingType) => 2;

    public static IEnumerable<(int Pid, object Dte)> EnumerateVs()
    {
        if (GetRunningObjectTable(0, out var rot) != 0) yield break;
        if (CreateBindCtx(0, out var ctx) != 0) yield break;
        rot.EnumRunning(out var en);
        var m = new IMoniker[1];
        while (en.Next(1, m, nint.Zero) == 0)
        {
            string name;
            try { m[0].GetDisplayName(ctx, null, out name); } catch { continue; }
            if (!name.StartsWith("!VisualStudio.DTE.", StringComparison.OrdinalIgnoreCase)) continue;
            var idx = name.LastIndexOf(':');
            if (idx < 0 || !int.TryParse(name[(idx + 1)..], out var pid)) continue;
            object? obj;
            try { rot.GetObject(m[0], out obj); } catch { continue; }
            if (obj != null) yield return (pid, obj);
        }
    }
}

// All COM work happens on one STA thread.
public sealed class VsService : IDisposable
{
    readonly BlockingCollection<Action> _queue = new();
    readonly Thread _thread;
    readonly Action<IReadOnlyList<VsSnapshot>> _onSnapshot;
    volatile bool _stop;

    public VsService(Action<IReadOnlyList<VsSnapshot>> onSnapshot)
    {
        _onSnapshot = onSnapshot;
        _thread = new Thread(Run) { IsBackground = true, Name = "VsService" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    void Run()
    {
        MessageFilter.Register();
        var next = DateTime.MinValue;
        while (!_stop)
        {
            var wait = Math.Max(0, (int)(next - DateTime.UtcNow).TotalMilliseconds);
            if (_queue.TryTake(out var action, wait))
            {
                try { action(); } catch { }
                next = DateTime.MinValue;
                continue;
            }
            next = DateTime.UtcNow.AddSeconds(1);
            _onSnapshot(Poll());
        }
    }

    static List<VsSnapshot> Poll()
    {
        var list = new List<VsSnapshot>();
        foreach (var (pid, obj) in MessageFilter.EnumerateVs())
        {
            try
            {
                dynamic dte = obj;
                string name = "";
                try { name = Path.GetFileNameWithoutExtension((string)dte.Solution.FullName); } catch { }
                if (string.IsNullOrEmpty(name))
                {
                    try { name = (string)dte.MainWindow.Caption; } catch { }
                }
                if (string.IsNullOrEmpty(name)) name = $"Visual Studio ({pid})";
                int mode = (int)dte.Debugger.CurrentMode; // 1 design, 2 run, 3 break
                list.Add(new VsSnapshot(pid, name, mode != 1));
            }
            catch { }
        }
        return list.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void Play(int pid) => Do(pid, d => d.Debugger.Go(false));
    public void Stop(int pid) => Do(pid, d => d.Debugger.Stop(false));
    public void Restart(int pid) => Do(pid, d => d.ExecuteCommand("Debug.Restart"));
    public void Activate(int pid) => Do(pid, d =>
    {
        d.MainWindow.Activate();
        var hwnd = new nint((int)d.MainWindow.HWnd);
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9);
        SetForegroundWindow(hwnd);
    });

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(nint h);

    void Do(int pid, Action<dynamic> act) => _queue.Add(() =>
    {
        foreach (var (p, obj) in MessageFilter.EnumerateVs())
            if (p == pid) { act((dynamic)obj); return; }
    });

    public void Dispose() { _stop = true; _queue.CompleteAdding(); }
}
