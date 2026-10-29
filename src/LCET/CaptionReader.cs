using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace LCET
{
    public sealed class CaptionReader : IDisposable
    {
        private readonly ManualResetEvent stop = new ManualResetEvent(false);
        private Thread thread;
        private AutomationElement caption;
        private AutomationElement window;
        private AutomationElement ownedWindow;
        private bool ownershipResolved;
        private DateTime lastScan;
        private bool startAttempted;
        private DateTime started;
        private string lastStatus;
        private long nativeWindowHandle;
        private readonly object controlGate = new object();
        private Action pendingControl;
        private Action cancelControl;
        public IntPtr WindowHandle { get { return new IntPtr(Interlocked.Read(ref nativeWindowHandle)); } }
        public event Action<string> Snapshot;
        public event Action<string> Status;
        public int PollMs { get; set; } = 250;
        public volatile bool Suspended;

        [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

        public void Start()
        {
            started = DateTime.UtcNow;
            thread = new Thread(Run) { IsBackground = true, Name = "LCET UI Automation" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void Report(string message)
        {
            if (lastStatus == message) return;
            lastStatus = message;
            Status?.Invoke(message);
        }

        private AutomationElement LocateWindow()
        {
            var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement candidate in windows)
            {
                try
                {
                    string name = candidate.Current.Name ?? "";
                    bool match = name.Equals("Live captions", StringComparison.OrdinalIgnoreCase) ||
                        name == "\u5b9e\u65f6\u8f85\u52a9\u5b57\u5e55";
                    using (var process = Process.GetProcessById(candidate.Current.ProcessId))
                        match |= process.ProcessName.Equals("LiveCaptions", StringComparison.OrdinalIgnoreCase);
                    if (match) return candidate;
                }
                catch (Exception exception) when (exception is ElementNotAvailableException || exception is ArgumentException || exception is System.ComponentModel.Win32Exception) { }
            }
            return null;
        }

        private void TryOpen()
        {
            if (startAttempted) return;
            startAttempted = true;
            Report("\u6b63\u5728\u542f\u52a8 Windows \u5b9e\u65f6\u5b57\u5e55");
            try
            {
                keybd_event(0x5B, 0, 0, UIntPtr.Zero);
                keybd_event(0x11, 0, 0, UIntPtr.Zero);
                keybd_event(0x4C, 0, 0, UIntPtr.Zero);
            }
            finally
            {
                keybd_event(0x4C, 0, 2, UIntPtr.Zero);
                keybd_event(0x11, 0, 2, UIntPtr.Zero);
                keybd_event(0x5B, 0, 2, UIntPtr.Zero);
            }
        }

        private string ReadText(AutomationElement element)
        {
            object pattern;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
            {
                try { return string.Join(" ", ((TextPattern)pattern).GetVisibleRanges().Select(range => range.GetText(-1))); }
                catch (NotSupportedException) { }
            }
            string text = element.Current.Name;
            if (!string.IsNullOrEmpty(text)) return text;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                return ((TextPattern)pattern).DocumentRange.GetText(-1);
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                return ((ValuePattern)pattern).Current.Value;
            return "";
        }

        private AutomationElement FindControl(string id)
        {
            AutomationElement found = TryFindControl(id);
            if (found != null) return found;
            throw new InvalidOperationException("Windows caption control unavailable: " + id);
        }

        private AutomationElement TryFindControl(string id)
        {
            var process = new PropertyCondition(AutomationElement.ProcessIdProperty, window.Current.ProcessId);
            var target = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
            AutomationElement local = window.FindFirst(TreeScope.Descendants, target);
            if (local != null) return local;
            foreach (AutomationElement root in AutomationElement.RootElement.FindAll(TreeScope.Children, process))
            {
                AutomationElement found = root.FindFirst(TreeScope.Descendants, target);
                if (found != null) return found;
            }
            return null;
        }

        private static void Invoke(AutomationElement element)
        {
            ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }

        private Task<T> RequestControl<T>(Func<T> action)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (controlGate)
            {
                if (thread == null || !thread.IsAlive || pendingControl != null)
                    completion.SetException(new InvalidOperationException("Windows captions not ready or busy"));
                else
                {
                    cancelControl = () => completion.TrySetException(new ObjectDisposedException("CaptionReader"));
                    pendingControl = () =>
                    {
                        try
                        {
                            if (window == null) throw new InvalidOperationException("Windows captions not connected");
                            completion.TrySetResult(action());
                        }
                        catch (Exception exception) { completion.TrySetException(exception); }
                    };
                }
            }
            return completion.Task;
        }

        public Task<bool> ToggleMicrophoneAsync()
        {
            return ChangeMicrophoneAsync(null);
        }

        public Task<bool> SetMicrophoneAsync(bool enabled)
        {
            return ChangeMicrophoneAsync(enabled);
        }

        private TogglePattern MicrophoneToggle()
        {
            AutomationElement microphone = TryFindControl("MicrophoneMenuFlyoutItem");
            if (microphone == null)
            {
                AutomationElement preferences = TryFindControl("PreferencesButton");
                if (preferences == null) { Invoke(FindControl("SettingsButton")); preferences = FindControl("PreferencesButton"); }
                ((ExpandCollapsePattern)preferences.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                microphone = FindControl("MicrophoneMenuFlyoutItem");
            }
            return (TogglePattern)microphone.GetCurrentPattern(TogglePattern.Pattern);
        }

        private Task<bool> ChangeMicrophoneAsync(bool? enabled)
        {
            return RequestControl(() =>
            {
                AutomationElement settingsControl = TryFindControl("SettingsButton");
                TogglePattern toggle = MicrophoneToggle();
                ToggleState before = toggle.Current.ToggleState;
                if (before == ToggleState.Indeterminate) throw new InvalidOperationException("Microphone state unknown");
                bool desired = enabled ?? before != ToggleState.On;
                if ((before == ToggleState.On) != desired) toggle.Toggle();
                ToggleState after = MicrophoneToggle().Current.ToggleState;
                if (after == ToggleState.Indeterminate || (after == ToggleState.On) != desired)
                    throw new InvalidOperationException("Microphone change unconfirmed");
                if (settingsControl != null)
                {
                    try { Invoke(settingsControl); }
                    catch (ElementNotAvailableException) { }
                    catch (InvalidOperationException) { }
                }
                return after == ToggleState.On;
            });
        }

        private static bool IsRecognitionLanguage(string name, string source)
        {
            string value = CaptionTracker.Normalize(name).Replace(" ", "").Replace('\uff08', '(').Replace('\uff09', ')').Replace('\uff0c', ',');
            return source == "en" ? value == "English(UnitedStates)" || value == "\u82f1\u8bed(\u7f8e\u56fd)" || value == "en-US" :
                source == "zh" && (value == "Chinese(Simplified,MainlandChina)" || value == "\u4e2d\u6587(\u7b80\u4f53,\u4e2d\u56fd\u5927\u9646)" ||
                value == "\u4e2d\u6587(\u7b80\u4f53,\u4e2d\u56fd)" || value == "zh-CN");
        }

        public Task<string> SetRecognitionLanguageAsync(string source)
        {
            if (source != "en" && source != "zh") throw new ArgumentOutOfRangeException("source");
            return RequestControl(() =>
            {
                AutomationElement combo = TryFindControl("SpeechModelDropDown");
                if (combo == null)
                {
                    if (TryFindControl("ChangeLanguageMenuFlyoutItem") == null) Invoke(FindControl("SettingsButton"));
                    Invoke(FindControl("ChangeLanguageMenuFlyoutItem"));
                    combo = FindControl("SpeechModelDropDown");
                }
                ((ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                AutomationElement selected = null;
                foreach (AutomationElement root in AutomationElement.RootElement.FindAll(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ProcessIdProperty, window.Current.ProcessId)))
                    foreach (AutomationElement item in root.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)))
                        if (IsRecognitionLanguage(item.Current.Name, source)) { selected = item; break; }
                if (selected == null) throw new InvalidOperationException("Required recognition language unavailable: " + (source == "en" ? "en-US" : "zh-CN"));
                var selection = (SelectionItemPattern)selected.GetCurrentPattern(SelectionItemPattern.Pattern);
                selection.Select();
                if (!selection.Current.IsSelected) throw new InvalidOperationException("Recognition language selection unconfirmed");
                AutomationElement continueButton = FindControl("ContinueButton");
                if (continueButton.Current.Name != "Continue" && continueButton.Current.Name != "\u7ee7\u7eed")
                    throw new InvalidOperationException("Install the requested Windows recognition language manually first");
                Invoke(continueButton);
                var deadline = Stopwatch.StartNew();
                while (deadline.ElapsedMilliseconds < 8000 && !stop.WaitOne(150))
                {
                    if (TryFindControl("SpeechModelDropDown") == null &&
                        (TryFindControl("CaptionsTextBlock") != null || TryFindControl("ReadyToCaptionTextBlock") != null))
                    {
                        caption = null;
                        lastScan = DateTime.MinValue;
                        Snapshot?.Invoke("");
                        return source;
                    }
                }
                throw new InvalidOperationException("Recognition language not confirmed; complete Windows language setup: " + (source == "en" ? "en-US" : "zh-CN"));
            });
        }

        private void Run()
        {
            while (!stop.WaitOne(Math.Max(200, PollMs)))
            {
                try
                {
                    if (window != null && !WindowDocking.IsWindow(WindowHandle))
                    {
                        window = null;
                        caption = null;
                        Interlocked.Exchange(ref nativeWindowHandle, 0);
                    }
                    Action control;
                    lock (controlGate) { control = pendingControl; pendingControl = null; cancelControl = null; }
                    control?.Invoke();
                    if (Suspended && window != null) continue;
                    if (caption != null)
                    {
                        string text = ReadText(caption);
                        Report(string.IsNullOrWhiteSpace(text) ? "\u7b49\u5f85\u8bed\u97f3" : "\u5b57\u5e55\u5df2\u8fde\u63a5");
                        Snapshot?.Invoke(text);
                        continue;
                    }
                    if ((DateTime.UtcNow - lastScan).TotalSeconds < 2) continue;
                    lastScan = DateTime.UtcNow;
                    window = LocateWindow();
                    if (window == null)
                    {
                        Interlocked.Exchange(ref nativeWindowHandle, 0);
                        TryOpen();
                        if ((DateTime.UtcNow - started).TotalSeconds > 20)
                            Report("\u672a\u627e\u5230\u5b57\u5e55\u7a97\u53e3\uff1a\u8bf7\u68c0\u67e5 Windows \u5b9e\u65f6\u5b57\u5e55\u6743\u9650\u548c\u8bed\u8a00\u5305");
                        continue;
                    }
                    Interlocked.Exchange(ref nativeWindowHandle, window.Current.NativeWindowHandle);
                    if (!ownershipResolved)
                    {
                        if (startAttempted) ownedWindow = window;
                        ownershipResolved = true;
                    }
                    caption = window.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "CaptionsTextBlock"));
                    if (caption == null)
                    {
                        var ready = window.FindFirst(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.AutomationIdProperty, "ReadyToCaptionTextBlock"));
                        Report(ready != null ? "\u7b49\u5f85\u8bed\u97f3" : "\u5df2\u627e\u5230\u5b57\u5e55\u7a97\u53e3\uff1b\u8bf7\u5b8c\u6210\u9996\u6b21\u8bbe\u7f6e\uff0c\u6216\u63a7\u4ef6\u7248\u672c\u4e0d\u53d7\u652f\u6301");
                    }
                }
                catch (Exception exception)
                {
                    caption = null;
                    window = null;
                    Interlocked.Exchange(ref nativeWindowHandle, 0);
                    Report("UIA: " + exception.GetType().Name + "\uff0c\u6b63\u5728\u91cd\u8fde");
                }
            }
            if (ownedWindow != null)
            {
                try
                {
                    object pattern;
                    if (ownedWindow.TryGetCurrentPattern(WindowPattern.Pattern, out pattern))
                        ((WindowPattern)pattern).Close();
                }
                catch (Exception exception) { Diagnostics.Write("uia", "owned_window_close_" + exception.GetType().Name); }
            }
        }

        public void Dispose()
        {
            lock (controlGate) { cancelControl?.Invoke(); cancelControl = null; pendingControl = null; }
            stop.Set();
            if (thread == null || thread.Join(1500)) stop.Dispose();
        }
    }
}