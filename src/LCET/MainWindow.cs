using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LCET
{
    public sealed class MainWindow : Window, IDisposable
    {
        private readonly Settings settings;
        private readonly string edgePath;
        private readonly string[][] languageOptions;
        private readonly CaptionTracker tracker = new CaptionTracker();
        private readonly Dictionary<long, int> emitted = new Dictionary<long, int>();
        private readonly Dictionary<long, Work> inFlight = new Dictionary<long, Work>();
        private readonly HashSet<string> models = new HashSet<string>();
        private readonly Dictionary<string, string> modelStates = new Dictionary<string, string>();
        private readonly DispatcherTimer timer;
        private readonly CaptionReader reader;
        private readonly LoopbackServer server;
        private TranscriptWriter transcript;
        private readonly Dictionary<string, string> manualReceipts = new Dictionary<string, string>();
        private readonly Queue<string> manualReceiptOrder = new Queue<string>();
        private readonly StackPanel rows = new StackPanel();
        private readonly SortedDictionary<long, string> displayedTranslations = new SortedDictionary<long, string>();
        private readonly TextBlock translationText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(6, 0, 6, 8),
            Foreground = Brushes.Black
        };
        private readonly TextBlock readinessStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 16, Margin = new Thickness(6, 8, 6, 8),
            Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Top
        };
        private readonly SolidColorBrush overlayBackground = new SolidColorBrush(Color.FromRgb(245, 248, 246));
        private readonly TextBlock captureStatus = Label("UIA: \u521d\u59cb\u5316");
        private readonly TextBlock edgeStatus = Label("Edge: \u521d\u59cb\u5316");
        private readonly TextBlock queueStatus = Label("");
        private readonly ComboBox direction = new ComboBox { Width = 150, Margin = new Thickness(4) };
        private readonly ComboBox targetDirection = new ComboBox { Width = 150, Margin = new Thickness(4) };
        private readonly TextBlock languagePairStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4), Foreground = Brushes.DimGray };
        private Button pauseButton;
        private Button captionMicrophoneButton;
        private Button captionLanguageButton;
        private bool? microphoneEnabled;
        private bool captionControlBusy;
        private bool updatingLanguageSelection;
        private Window settingsWindow;
        private FrameworkElement settingsContent;
        private ScrollViewer captionScroll;
        private Slider heightControl;
        private Point? resizePointerOrigin;
        private Size resizeInitialSize;
        private bool followingLatest = true;
        private bool updatingRows;
        private bool docked;
        private Rect floatingBounds;
        private volatile string snapshot;
        private volatile string readerStatus;
        private string pageId;
        private string lastSnapshot;
        private DateTime lastSeen = DateTime.UtcNow;
        private DateTime lastLaunch;
        private int generation = 1;
        private int recoveries;
        private bool paused;
        private bool sessionLocked;
        private bool disposed;
        private bool closing;
        private bool allowClose;
        private bool dirty = true;
        private long accepted;
        private long failedTranslations;
        private readonly Queue<double> durations = new Queue<double>();

        private sealed class Work
        {
            public Segment Segment;
            public int Revision;
            public DateTime Sent;
            public int Retries;
            public DateTime RetryAt;
            public DateTime Deadline;
            public string Text;
        }

        public MainWindow(string root, string edge)
        {
            edgePath = edge;
            settings = Settings.Load(root);
            settings.LanguageConfirmed = true;
            languageOptions = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<string[][]>(File.ReadAllText(Path.Combine(root, "web", "languages.json")));
            if (!languageOptions.Any(option => option[0] == settings.SourceLanguage) || !languageOptions.Any(option => option[0] == settings.TargetLanguage))
            { settings.SourceLanguage = "en"; settings.TargetLanguage = "zh"; }
            Title = "LCET | LiveCaption Edge Translator";
            Width = settings.Width;
            Height = settings.Height;
            MinWidth = 420;
            MinHeight = 160;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(settings.Left, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Max(SystemParameters.VirtualScreenTop, Math.Min(settings.Top, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
            floatingBounds = new Rect(Left, Top, Width, Height);
            Topmost = true;
            overlayBackground.Opacity = settings.Opacity;
            translationText.Foreground = (Brush)new BrushConverter().ConvertFromString(settings.TranslationColor);
            Background = overlayBackground;
            Foreground = new SolidColorBrush(Color.FromRgb(30, 45, 37));
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 14;
            BuildUi();
            server = new LoopbackServer(Path.Combine(root, "web"));
            server.ModelDownloadRequested += () => Process.Start(new ProcessStartInfo(edgePath,
                "--new-tab \"https://microsoftedge.github.io/Demos/built-in-ai/playgrounds/translator-api/\"") { UseShellExecute = true });
            server.Connected += () => OnUi(() =>
            {
                pageId = null;
                models.Clear();
                modelStates.Clear();
                lastSeen = DateTime.UtcNow;
                Invalidate();
                edgeStatus.Text = "Edge: \u5df2\u8fde\u63a5\uff0c\u7b49\u5f85\u6a21\u578b";
            });
            server.Message += (path, data) => OnUi(() => Receive(path, data));
            reader = new CaptionReader { PollMs = settings.PollMs };
            reader.Snapshot += text => snapshot = text;
            reader.Status += text => readerStatus = text;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += Tick;
            Loaded += (sender, args) =>
            {
                reader.Start(); timer.Start(); OpenEdge();
            };
            Closing += Shutdown;
            SystemEvents.SessionSwitch += SessionSwitch;
            Diagnostics.Write("app", "started");
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        }

        private Button Button(Panel parent, string title, Action action, string tip = null)
        {
            var button = new Button { Content = title, Margin = new Thickness(4), Padding = new Thickness(11, 6, 11, 6), MinHeight = 32, ToolTip = tip ?? title };
            button.Click += (sender, args) => Guard(action);
            parent.Children.Add(button);
            return button;
        }

        private void Guard(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                Diagnostics.Write("ui", exception.GetType().Name);
                MessageBox.Show(this, "\u64cd\u4f5c\u5931\u8d25: " + exception.GetType().Name, "LCET", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BuildUi()
        {
            var layout = new DockPanel { Margin = new Thickness(12, 4, 12, 6), Background = Brushes.Transparent };
            PreviewMouseLeftButtonDown += (sender, args) =>
            {
                if (docked || args.ButtonState != MouseButtonState.Pressed || !CanDragFrom(args.OriginalSource as DependencyObject)) return;
                args.Handled = true;
                DragMove();
            };
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(toolbar, Dock.Top);
            layout.Children.Add(toolbar);
            captionMicrophoneButton = IconButton(toolbar, "\uE720", "\u5f00\u5173 Windows \u5b57\u5e55\u9ea6\u514b\u98ce\u8bc6\u522b", ToggleCaptionMicrophone);
            AutomationProperties.SetAutomationId(captionMicrophoneButton, "CaptionMicrophone");
            captionLanguageButton = IconButton(toolbar, "", "\u540c\u6b65\u5207\u6362 Windows \u8bc6\u522b\u8bed\u8a00\u548c\u7ffb\u8bd1\u65b9\u5411",
                () => SwitchCaptionLanguage(settings.SourceLanguage == "en" ? "zh" : "en"));
            captionLanguageButton.FontFamily = FontFamily;
            captionLanguageButton.FontSize = 12;
            captionLanguageButton.Width = 76;
            AutomationProperties.SetAutomationId(captionLanguageButton, "CaptionLanguage");
            UpdateCaptionControls();
            IconButton(toolbar, "\uE713", "\u8bbe\u7f6e", OpenSettings);
            IconButton(toolbar, "\uE7E8", "\u9000\u51fa", Close);
            var footer = new DockPanel();
            DockPanel.SetDock(footer, Dock.Bottom);
            layout.Children.Add(footer);
            var grip = new Thumb
            {
                Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNWSE, ToolTip = "\u62d6\u52a8\u8c03\u6574\u7a97\u53e3\u5927\u5c0f"
            };
            var gripVisual = new FrameworkElementFactory(typeof(ResizeGrip));
            gripVisual.SetValue(IsHitTestVisibleProperty, false);
            var gripHitArea = new FrameworkElementFactory(typeof(Border));
            gripHitArea.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(1, 245, 248, 246)));
            gripHitArea.AppendChild(gripVisual);
            grip.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = gripHitArea };
            AutomationProperties.SetName(grip, "\u8c03\u6574\u7a97\u53e3\u5927\u5c0f");
            AutomationProperties.SetAutomationId(grip, "OverlayResizeGrip");
            grip.DragStarted += (sender, args) =>
            {
                resizePointerOrigin = PointToScreen(Mouse.GetPosition(this));
                resizeInitialSize = new Size(Width, docked ? floatingBounds.Height : Height);
            };
            grip.DragDelta += (sender, args) =>
            {
                double width = Width + args.HorizontalChange;
                double height = (docked ? floatingBounds.Height : Height) + args.VerticalChange;
                if (resizePointerOrigin.HasValue)
                {
                    Vector delta = PointToScreen(Mouse.GetPosition(this)) - resizePointerOrigin.Value;
                    ResizeFromPointer(delta);
                }
                else ResizeOverlay(width, height);
                args.Handled = true;
            };
            grip.DragCompleted += (sender, args) => { resizePointerOrigin = null; Save(); };
            DockPanel.SetDock(grip, Dock.Right);
            footer.Children.Add(grip);
            var status = new Grid();
            status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            foreach (TextBlock label in new[] { captureStatus, edgeStatus, queueStatus })
            {
                label.FontSize = 7;
                label.LineHeight = 8.5;
                label.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                label.Margin = new Thickness(3);
                label.TextWrapping = TextWrapping.NoWrap;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("Text") { Source = label });
                Grid.SetColumn(label, status.Children.Count);
                status.Children.Add(label);
            }
            footer.Children.Add(status);
            captionScroll = new ScrollViewer
            {
                Content = rows, Margin = new Thickness(0, 4, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            captionScroll.ScrollChanged += (sender, args) =>
            {
                if (!updatingRows && args.VerticalChange != 0 && args.ExtentHeightChange == 0)
                    followingLatest = captionScroll.VerticalOffset >= captionScroll.ScrollableHeight - 2;
            };
            var captionArea = new Grid();
            captionArea.Children.Add(captionScroll);
            captionArea.Children.Add(readinessStatus);
            layout.Children.Add(captionArea);
            Content = layout;
            settingsContent = BuildSettings();
        }

        private static bool CanDragFrom(DependencyObject source)
        {
            if (source == null) return false;
            while (source != null)
            {
                if (source is ButtonBase || source is Thumb || source is ScrollBar || source is Slider) return false;
                source = source is Visual
                    ? VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }
            return true;
        }

        private Button IconButton(Panel parent, string glyph, string name, Action action)
        {
            var button = new Button
            {
                Content = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16,
                Width = 28, Height = 28, Padding = new Thickness(0), Margin = new Thickness(2),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = name
            };
            AutomationProperties.SetName(button, name);
            button.Click += (sender, args) => Guard(action);
            parent.Children.Add(button);
            return button;
        }

        private void UpdateCaptionControls()
        {
            bool enabled = reader != null && reader.WindowHandle != IntPtr.Zero && !captionControlBusy && !closing && !sessionLocked;
            captionMicrophoneButton.IsEnabled = enabled;
            captionLanguageButton.IsEnabled = enabled;
            string microphone = microphoneEnabled.HasValue ? (microphoneEnabled.Value ? "\u5df2\u5f00\u542f" : "\u5df2\u5173\u95ed") : "\u72b6\u6001\u672a\u8bfb\u53d6";
            string tip = "Windows \u5b57\u5e55\u9ea6\u514b\u98ce\u8bc6\u522b\uff1a" + microphone + "\uff1b\u70b9\u51fb\u5207\u6362";
            if (!Equals(captionMicrophoneButton.ToolTip, tip))
            {
                captionMicrophoneButton.ToolTip = tip;
                AutomationProperties.SetName(captionMicrophoneButton, tip);
                captionMicrophoneButton.Background = microphoneEnabled == true ? new SolidColorBrush(Color.FromArgb(65, 0, 105, 78)) : Brushes.Transparent;
            }
            bool bilingual = (settings.SourceLanguage == "en" && settings.TargetLanguage == "zh") || (settings.SourceLanguage == "zh" && settings.TargetLanguage == "en");
            string language = bilingual ? settings.SourceLanguage == "en" ? "EN \u2192 \u4e2d" : "\u4e2d \u2192 EN" : "EN/\u4e2d";
            if (!Equals(captionLanguageButton.Content, language)) captionLanguageButton.Content = language;
            captionLanguageButton.ToolTip = settings.SourceLanguage == "en" ?
                "\u5207\u6362\u4e3a\u4e2d\u6587\uff08\u7b80\u4f53\uff0c\u4e2d\u56fd\u5927\u9646\uff09\u8bc6\u522b\u548c\u4e2d\u8bd1\u82f1" :
                "\u5207\u6362\u4e3a English (United States) \u8bc6\u522b\u548c\u82f1\u8bd1\u4e2d";
        }

        private async void ToggleCaptionMicrophone()
        {
            if (captionControlBusy || closing || sessionLocked) return;
            captionControlBusy = true;
            UpdateCaptionControls();
            try { microphoneEnabled = await reader.ToggleMicrophoneAsync(); }
            catch (Exception exception) { ShowCaptionControlError(exception); }
            finally { captionControlBusy = false; if (!closing) UpdateCaptionControls(); }
        }

        private async Task<bool> ChangeCaptionLanguageAsync(string source, Func<string, Task<string>> change)
        {
            if (source != "en" && source != "zh") throw new ArgumentOutOfRangeException("source");
            if (captionControlBusy || closing || sessionLocked) return false;
            captionControlBusy = true;
            reader.Suspended = true;
            UpdateCaptionControls();
            try
            {
                string applied = await change(source);
                if (closing || sessionLocked) return false;
                if (applied != source) throw new InvalidOperationException("Windows recognition language not confirmed");
                ArchiveCompleted(DateTime.UtcNow, true);
                settings.SourceLanguage = source;
                settings.TargetLanguage = source == "en" ? "zh" : "en";
                settings.LanguageConfirmed = true;
                UpdateLanguageSelectors();
                UpdateLanguagePairStatus();
                snapshot = null;
                lastSnapshot = null;
                ResetCaptions();
                return true;
            }
            finally
            {
                captionControlBusy = false;
                reader.Suspended = paused || sessionLocked;
                if (!closing) UpdateCaptionControls();
            }
        }

        private async void SwitchCaptionLanguage(string source)
        {
            try { if (await ChangeCaptionLanguageAsync(source, reader.SetRecognitionLanguageAsync)) Save(); }
            catch (Exception exception) { ShowCaptionControlError(exception); }
        }

        private void ShowCaptionControlError(Exception exception)
        {
            if (closing) return;
            Diagnostics.Write("caption_control", exception.GetType().Name);
            MessageBox.Show(this,
                "Windows \u5b57\u5e55\u64cd\u4f5c\u672a\u786e\u8ba4\u6210\u529f\uff0c\u8bf7\u5728\u539f\u7248\u5b57\u5e55\u8bbe\u7f6e\u4e2d\u68c0\u67e5\u3002\n" +
                "\u8bed\u8a00\u4ec5\u4f7f\u7528 en-US \u548c zh-CN\uff1b\u5982\u9700\u8bed\u8a00\u5305\uff0c\u8bf7\u624b\u52a8\u5b89\u88c5\u3002\n" + exception.Message,
                "LCET", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private FrameworkElement BuildSettings()
        {
            var content = new StackPanel { Margin = new Thickness(12) };
            var tools = new WrapPanel();
            content.Children.Add(tools);
            direction.SelectedValuePath = targetDirection.SelectedValuePath = "Tag";
            for (int index = 0; index < languageOptions.Length; index++)
            {
                string code = languageOptions[index][0];
                string label = (index == 0 ? "\u82f1\u6587" : index == 1 ? "\u4e2d\u6587\uff08\u7b80\u4f53\uff09" : index == 2 ? "\u65e5\u6587" : languageOptions[index][1]) + " (" + code + ")";
                direction.Items.Add(new ComboBoxItem { Content = label, Tag = code, ToolTip = label });
                targetDirection.Items.Add(new ComboBoxItem { Content = label, Tag = code, ToolTip = label });
            }
            direction.SelectedValue = settings.SourceLanguage;
            targetDirection.SelectedValue = settings.TargetLanguage;
            AutomationProperties.SetAutomationId(direction, "CaptionSourceLanguage");
            AutomationProperties.SetName(direction, "\u5b57\u5e55\u6e90\u8bed\u8a00");
            AutomationProperties.SetAutomationId(targetDirection, "CaptionTargetLanguage");
            AutomationProperties.SetName(targetDirection, "\u8bd1\u6587\u76ee\u6807\u8bed\u8a00");
            tools.Children.Add(Label("\u6e90\u8bed\u8a00"));
            tools.Children.Add(direction);
            var swapDirection = Button(tools, "\u21c4", () => { if (SwapTranslationPair()) Save(); }, "\u4ea4\u6362\u6e90\u8bed\u8a00\u548c\u76ee\u6807\u8bed\u8a00");
            swapDirection.Width = swapDirection.Height = 36;
            swapDirection.Padding = new Thickness(0);
            swapDirection.FontSize = 20;
            AutomationProperties.SetAutomationId(swapDirection, "SwapCaptionDirection");
            AutomationProperties.SetName(swapDirection, "\u4ea4\u6362\u6e90\u8bed\u8a00\u548c\u76ee\u6807\u8bed\u8a00");
            tools.Children.Add(Label("\u76ee\u6807\u8bed\u8a00"));
            tools.Children.Add(targetDirection);
            content.Children.Add(languagePairStatus);
            UpdateLanguagePairStatus();
            direction.SelectionChanged += TranslationPairChanged;
            targetDirection.SelectionChanged += TranslationPairChanged;
            pauseButton = Button(tools, "\u6682\u505c", () =>
            {
                paused = !paused;
                pauseButton.Content = paused ? "\u7ee7\u7eed" : "\u6682\u505c";
                Invalidate();
                if (!paused) lastSnapshot = null;
                dirty = true;
            });
            Button(tools, "\u590d\u5236", Copy);
            Button(tools, "\u6e05\u7a7a", Clear);
            Button(tools, "\u8bb0\u5f55\u6587\u4ef6\u5939", OpenTranscripts);
            Button(tools, "\u6062\u590d Edge", () => { recoveries = 0; Invalidate(); OpenEdge(); });
            Button(tools, "\u8bca\u65ad", ShowDiagnostics);
            Button(tools, "\u9a8c\u8bc1\u6a21\u578b", () => server.Send("verify", new { }));
            var options = new WrapPanel();
            content.Children.Add(options);
            var follow = new CheckBox
            {
                Content = "\u8ddf\u968f Windows \u5b57\u5e55", IsChecked = settings.FollowWindowsCaptions,
                Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "\u540c\u6b65\u4f4d\u7f6e\u4e0e\u5bbd\u5ea6\uff1b\u5173\u95ed\u540e\u6062\u590d\u81ea\u7531\u62d6\u52a8\u3002\u8ddf\u968f\u65f6\u63e1\u67c4\u4ec5\u8c03\u6574\u9ad8\u5ea6\u3002"
            };
            AutomationProperties.SetAutomationId(follow, "FollowWindowsCaptions");
            RoutedEventHandler followChanged = (sender, args) => { settings.FollowWindowsCaptions = follow.IsChecked == true; UpdateDocking(); Save(); };
            follow.Checked += followChanged;
            follow.Unchecked += followChanged;
            options.Children.Add(follow);
            Button(options, "Windows \u5b57\u5e55\u8bbe\u7f6e", () => Process.Start(new ProcessStartInfo("ms-settings:easeofaccess-captions") { UseShellExecute = true }));
            options.Children.Add(Label("\u5b57\u53f7"));
            var font = new Slider { Minimum = 14, Maximum = 40, Value = settings.FontSize, Width = 90, Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center, ToolTip = "\u5b57\u5e55\u5b57\u53f7" };
            AutomationProperties.SetAutomationId(font, "CaptionFontSize");
            font.ValueChanged += (sender, args) => { settings.FontSize = font.Value; ResizeForFont(); };
            options.Children.Add(font);
            options.Children.Add(Label("\u7a97\u53e3\u9ad8\u5ea6"));
            heightControl = new Slider
            {
                Minimum = 160, Maximum = 1200, Value = Height, Width = 100, Margin = new Thickness(6),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "\u7ffb\u8bd1\u7a97\u53e3\u9ad8\u5ea6\uff1b\u8ddf\u968f\u65f6\u53d7\u5c4f\u5e55\u53ef\u7528\u7a7a\u95f4\u9650\u5236"
            };
            AutomationProperties.SetAutomationId(heightControl, "OverlayHeight");
            AutomationProperties.SetName(heightControl, "\u7ffb\u8bd1\u7a97\u53e3\u9ad8\u5ea6");
            heightControl.ValueChanged += (sender, args) =>
            {
                if (args.NewValue == (docked ? floatingBounds.Height : Height)) return;
                ResizeOverlay(Width, args.NewValue);
                Save();
            };
            options.Children.Add(heightControl);
            options.Children.Add(Label("\u8bd1\u6587\u989c\u8272"));
            string[] colors = { "Black", "White", "Green", "Blue", "Red" };
            string[] colorNames = { "\u9ed1\u8272", "\u767d\u8272", "\u7eff\u8272", "\u84dd\u8272", "\u7ea2\u8272" };
            for (int index = 0; index < colors.Length; index++)
            {
                string color = colors[index];
                var brush = (Brush)new BrushConverter().ConvertFromString(color);
                var choice = new RadioButton
                {
                    GroupName = "TranslationColor", IsChecked = settings.TranslationColor == color,
                    Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center, ToolTip = colorNames[index],
                    Content = new Border { Width = 16, Height = 16, Background = brush, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2) }
                };
                AutomationProperties.SetAutomationId(choice, "CaptionColor" + color);
                AutomationProperties.SetName(choice, "\u8bd1\u6587\u989c\u8272 " + colorNames[index]);
                choice.Checked += (sender, args) => { settings.TranslationColor = color; translationText.Foreground = brush; };
                options.Children.Add(choice);
            }
            options.Children.Add(Label("\u80cc\u666f\u4e0d\u900f\u660e\u5ea6"));
            var opacity = new Slider { Minimum = 0, Maximum = 1, Value = settings.Opacity, Width = 80, Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center, ToolTip = "\u80cc\u666f\u4e0d\u900f\u660e\u5ea6\uff08\u4e0d\u5f71\u54cd\u5b57\u5e55\uff09" };
            AutomationProperties.SetAutomationId(opacity, "BackgroundOpacity");
            AutomationProperties.SetName(opacity, "\u80cc\u666f\u4e0d\u900f\u660e\u5ea6");
            opacity.ValueChanged += (sender, args) => { settings.Opacity = opacity.Value; overlayBackground.Opacity = opacity.Value; };
            options.Children.Add(opacity);
            return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }

        private void OpenSettings()
        {
            if (settingsWindow != null) { settingsWindow.Activate(); return; }
            settingsWindow = new Window
            {
                Title = "\u8bbe\u7f6e", Owner = this, Topmost = true, ShowInTaskbar = false,
                Width = 620, Height = 300, MinWidth = 480, MinHeight = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                FontFamily = FontFamily, FontSize = 14, Background = new SolidColorBrush(overlayBackground.Color), Foreground = Foreground,
                Content = settingsContent
            };
            settingsWindow.Closed += (sender, args) =>
            {
                ((Window)sender).Content = null;
                settingsWindow = null;
                Save();
            };
            settingsWindow.Show();
        }

        private void UpdateLanguageSelectors()
        {
            updatingLanguageSelection = true;
            try
            {
                direction.SelectedValue = settings.SourceLanguage;
                targetDirection.SelectedValue = settings.TargetLanguage;
            }
            finally { updatingLanguageSelection = false; }
        }

        private void UpdateLanguagePairStatus()
        {
            languagePairStatus.Text = settings.SourceLanguage + " \u2192 " + settings.TargetLanguage +
                "\uff1b\u8bf7\u5728 Windows \u5b57\u5e55\u4e2d\u624b\u52a8\u9009\u62e9\u76f8\u540c\u6e90\u8bed\u8a00\uff0c\u4e0d\u4f1a\u81ea\u52a8\u68c0\u6d4b\u6216\u5207\u6362\u3002";
        }

        private void TranslationPairChanged(object sender, SelectionChangedEventArgs args)
        {
            if (updatingLanguageSelection) return;
            Guard(() =>
            {
                var selected = args.AddedItems.Count > 0 ? args.AddedItems[0] as ComboBoxItem : null;
                if (selected == null) return;
                string source = ReferenceEquals(sender, direction) ? selected.Tag as string : settings.SourceLanguage;
                string target = ReferenceEquals(sender, targetDirection) ? selected.Tag as string : settings.TargetLanguage;
                if (source == target)
                {
                    if (ReferenceEquals(sender, direction)) target = settings.SourceLanguage;
                    else source = settings.TargetLanguage;
                }
                if (SetTranslationPair(source, target)) Save();
                else Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() => { if (!disposed) UpdateLanguageSelectors(); }));
            });
        }

        private bool SetTranslationPair(string source, string target)
        {
            if (captionControlBusy || closing || sessionLocked)
            { languagePairStatus.Text = "\u5f53\u524d\u65e0\u6cd5\u5207\u6362\u65b9\u5411\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5"; return false; }
            if (source == target || !languageOptions.Any(option => option[0] == source) || !languageOptions.Any(option => option[0] == target))
            { languagePairStatus.Text = "\u8bf7\u9009\u62e9\u4e0d\u540c\u7684\u6e90\u8bed\u8a00\u548c\u76ee\u6807\u8bed\u8a00"; return false; }
            if (source == settings.SourceLanguage && target == settings.TargetLanguage) return true;
            ArchiveCompleted(DateTime.UtcNow, true);
            settings.SourceLanguage = source;
            settings.TargetLanguage = target;
            settings.LanguageConfirmed = true;
            models.RemoveWhere(key => key.Contains("->"));
            foreach (string key in modelStates.Keys.Where(key => key.Contains("->")).ToArray()) modelStates.Remove(key);
            UpdateLanguageSelectors();
            snapshot = null;
            lastSnapshot = null;
            ResetCaptions();
            UpdateLanguagePairStatus();
            UpdateCaptionControls();
            return true;
        }

        private bool SwapTranslationPair()
        {
            return SetTranslationPair(targetDirection.SelectedValue as string, direction.SelectedValue as string);
        }

        private static string ModelKey(string source, string target)
        {
            return (source == "en" && target == "zh") || (source == "zh" && target == "en") ? source : source + "->" + target;
        }

        private string CurrentModelKey { get { return ModelKey(settings.SourceLanguage, settings.TargetLanguage); } }

        private void OnUi(Action action)
        {
            if (!disposed && !Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(new Action(() => { if (!disposed && !closing) Guard(action); }));
        }

        private void Invalidate()
        {
            generation++;
            emitted.Clear();
            inFlight.Clear();
            if (server != null) server.Send("control", new { generation, paused = paused || sessionLocked, sourceLanguage = settings.SourceLanguage, targetLanguage = settings.TargetLanguage });
        }

        private void Clear()
        {
            ArchiveCompleted(DateTime.UtcNow, true);
            ResetCaptions();
        }

        private void ResetCaptions()
        {
            displayedTranslations.Clear();
            followingLatest = true;
            Invalidate();
            tracker.Clear();
            lastSnapshot = snapshot;
            dirty = true;
        }

        private void Tick(object sender, EventArgs args)
        {
            if (closing) return;
            UpdateDocking();
            if (readerStatus != null) captureStatus.Text = "UIA: " + readerStatus;
            var now = DateTime.UtcNow;
            UpdateCaptionControls();
            bool running = !paused && !sessionLocked && !captionControlBusy;
            reader.Suspended = paused || sessionLocked || captionControlBusy;
            if (running && snapshot != null && snapshot != lastSnapshot)
            {
                dirty |= tracker.Observe(snapshot, settings.SourceLanguage, settings.TargetLanguage, now);
                lastSnapshot = snapshot;
            }
            ArchiveCompleted(now);
            long[] removed = tracker.TrimHistory(80, true);
            if (removed.Length > 0)
            {
                foreach (long id in removed) { inFlight.Remove(id); emitted.Remove(id); }
                server.Send("cancel", new { segmentIds = removed });
            }
            if ((now - lastSeen).TotalSeconds > 25)
            {
                edgeStatus.Text = "Edge: \u5fc3\u8df3\u8d85\u65f6\uff0c\u53ef\u80fd\u5df2\u5173\u95ed\u6216\u4f11\u7720";
                models.Clear();
                if ((now - lastLaunch).TotalSeconds > 30 && recoveries < 3) { recoveries++; OpenEdge(); }
            }
            if (running && models.Contains(CurrentModelKey) && (now - lastSeen).TotalSeconds <= 25)
            {
                foreach (Work work in inFlight.Values.ToArray())
                {
                    if (now < (work.RetryAt != default(DateTime) ? work.RetryAt : work.Deadline)) continue;
                    if (work.Retries < 2) { work.Retries++; Send(work); }
                    else
                    {
                        inFlight.Remove(work.Segment.Id);
                        failedTranslations++;
                        Diagnostics.Write("translation", "retry_exhausted");
                        edgeStatus.Text = "Edge: \u6709\u53e5\u5b50\u91cd\u8bd5\u540e\u4ecd\u5931\u8d25";
                    }
                }
                foreach (Segment segment in tracker.Ready(now, settings.DebounceMs))
                {
                    int revision;
                    if (emitted.TryGetValue(segment.Id, out revision) && revision == segment.Revision) continue;
                    if (inFlight.ContainsKey(segment.Id)) continue;
                    if (inFlight.Count >= 12 && !inFlight.ContainsKey(segment.Id)) break;
                    var work = new Work { Segment = segment, Revision = segment.Revision, Text = segment.Text };
                    inFlight[segment.Id] = work;
                    emitted[segment.Id] = segment.Revision;
                    Send(work);
                }
                var retained = new HashSet<long>(tracker.Segments.Select(segment => segment.Id));
                foreach (long id in emitted.Keys.Where(id => !retained.Contains(id)).ToArray()) emitted.Remove(id);
                foreach (long id in inFlight.Keys.Where(id => !retained.Contains(id)).ToArray()) inFlight.Remove(id);
            }
            queueStatus.Text = (sessionLocked ? "\u4f1a\u8bdd\u5df2\u9501\u5b9a" : paused ? "\u5df2\u6682\u505c\u91c7\u96c6\u5904\u7406\u548c\u7ffb\u8bd1" : !models.Contains(CurrentModelKey) ? "\u7b49\u5f85\u5f53\u524d\u65b9\u5411 Edge \u6a21\u578b\u5c31\u7eea" : "\u8fd0\u884c\u4e2d") +
                "  |  \u5f85\u7ffb\u8bd1 " + inFlight.Count + "  |  \u5df2\u8fd4\u56de " + accepted;
            if (failedTranslations > 0) queueStatus.Text += "  |  \u5931\u8d25 " + failedTranslations;
            if (transcript != null && transcript.LastError != null)
                queueStatus.Text += "  |  TXT \u8bb0\u5f55\u5f02\u5e38: " + transcript.LastError;
            if (dirty) { Render(); dirty = false; }
            UpdateReadiness();
        }

        private void UpdateReadiness()
        {
            string modelState;
            modelStates.TryGetValue(CurrentModelKey, out modelState);
            string message = sessionLocked ? "Windows \u4f1a\u8bdd\u5df2\u9501\u5b9a" :
                paused ? "\u5b57\u5e55\u7ffb\u8bd1\u5df2\u6682\u505c" :
                !models.Contains(CurrentModelKey) ? modelState == "unavailable" ? "Edge \u4e0d\u652f\u6301\u6240\u9009\u5b57\u5e55\u8bed\u8a00\u5bf9: " + settings.SourceLanguage + " \u2192 " + settings.TargetLanguage :
                    modelState == "downloadable" ? "\u8bf7\u5728 Edge \u7f51\u9875\u4e0b\u8f7d\u6240\u9009\u5b57\u5e55\u6a21\u578b" :
                    modelState == "error" ? "Edge \u5b57\u5e55\u6a21\u578b\u51c6\u5907\u5931\u8d25\uff0c\u8bf7\u68c0\u67e5 Edge \u7f51\u9875" : "\u7b49\u5f85 Edge \u7ffb\u8bd1\u6a21\u578b\u5c31\u7eea" :
                string.IsNullOrEmpty(readerStatus) ? "\u7b49\u5f85 Windows \u5b57\u5e55\u5185\u5bb9" :
                readerStatus == "\u5b57\u5e55\u5df2\u8fde\u63a5" ? "\u5b57\u5e55\u5df2\u91c7\u96c6\uff0c\u7b49\u5f85\u8bd1\u6587" : readerStatus;
            if (readinessStatus.Text != message) readinessStatus.Text = message;
            readinessStatus.Visibility = string.IsNullOrEmpty(translationText.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ArchiveCompleted(DateTime now, bool flush = false)
        {
            if (!tracker.Completed(now, flush).Any()) return;
            GetTranscript().RecordCompleted(tracker, now, flush);
        }

        private TranscriptWriter GetTranscript()
        {
            if (transcript == null)
            {
                transcript = new TranscriptWriter(Path.Combine(Settings.UserDirectory, "transcripts"));
                transcript.ManualRecorded += requestId => OnUi(() =>
                {
                    if (manualReceipts.ContainsKey(requestId)) manualReceipts[requestId] = "saved";
                    server.Send("manual-recorded", new { requestId, state = "saved" });
                });
            }
            return transcript;
        }

        private void ReceiveManual(Dictionary<string, object> data)
        {
            string requestId = Field(data, "requestId");
            Guid parsedId;
            if (!Guid.TryParseExact(requestId, "D", out parsedId)) return;
            string source = Field(data, "sourceLanguage");
            string target = Field(data, "targetLanguage");
            string text = Field(data, "text");
            string translation = Field(data, "translatedText");
            string state;
            if (manualReceipts.TryGetValue(requestId, out state))
            {
                if (state == "saved") server.Send("manual-recorded", new { requestId, state });
                return;
            }
            if (Field(data, "status") != "ok" || Field(data, "kind") != "manual" ||
                !LanguageResources.IsLanguageTag(source) || !LanguageResources.IsLanguageTag(target) ||
                string.Equals(source, target, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(text) || text.Length > 4000 || string.IsNullOrWhiteSpace(translation) || translation.Length > 8000)
                state = "invalid";
            else if (sessionLocked) state = "locked";
            else if (!GetTranscript().RecordManual(requestId, source, target, text, translation, DateTime.UtcNow)) state = "full";
            else
            {
                while (manualReceiptOrder.Count >= 128) manualReceipts.Remove(manualReceiptOrder.Dequeue());
                manualReceiptOrder.Enqueue(requestId);
                manualReceipts[requestId] = "queued";
                return;
            }
            server.Send("manual-recorded", new { requestId, state });
        }

        private void UpdateDocking()
        {
            if (sessionLocked || WindowState != WindowState.Normal) return;
            Rect placement;
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (settings.FollowWindowsCaptions && reader != null &&
                WindowDocking.TryGetPlacement(reader.WindowHandle, handle, docked ? floatingBounds.Height : Height, ActualHeight, out placement))
            {
                if (!docked)
                {
                    floatingBounds = new Rect(Left, Top, Width, Height);
                    MinWidth = 1;
                    MinHeight = 1;
                    docked = true;
                }
                if (WindowDocking.Apply(handle, placement)) return;
            }
            if (!docked) return;
            docked = false;
            MinWidth = 420;
            MinHeight = 160;
            Width = floatingBounds.Width;
            Height = floatingBounds.Height;
            Left = floatingBounds.Left;
            Top = floatingBounds.Top;
        }

        private void Send(Work work)
        {
            work.RetryAt = default(DateTime);
            work.Sent = DateTime.UtcNow;
            work.Deadline = work.Sent.AddSeconds(18 * (inFlight.Count + 1));
            work.Segment.LastSubmitted = work.Sent;
            server.Send("task", new
            {
                segmentId = work.Segment.Id, revision = work.Revision, generation,
                sourceLanguage = work.Segment.Source,
                targetLanguage = work.Segment.Target ?? settings.TargetLanguage,
                text = work.Text, timestamp = work.Sent.ToString("o")
            });
        }

        private static string Field(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : "";
        }

        private void Receive(string path, Dictionary<string, object> data)
        {
            int messageGeneration;
            if (!int.TryParse(Field(data, "generation"), out messageGeneration) || messageGeneration != generation) return;
            string incomingPage = Field(data, "pageId");
            if (incomingPage.Length > 80 || incomingPage.Length == 0) return;
            if (pageId == null) pageId = incomingPage;
            if (pageId != incomingPage) return;
            lastSeen = DateTime.UtcNow;
            if (path == "/heartbeat") return;
            if (path == "/manual-result") { ReceiveManual(data); return; }
            if (path == "/status")
            {
                string state = Field(data, "state");
                string source = Field(data, "sourceLanguage");
                string target = Field(data, "targetLanguage");
                if (target.Length == 0 && (source == "en" || source == "zh")) target = source == "en" ? "zh" : "en";
                string key = ModelKey(source, target);
                bool relevant = key == CurrentModelKey || key == "en" || key == "zh";
                if (relevant) modelStates[key] = state;
                if (state == "ready" && relevant) models.Add(key);
                if (state == "error" || state == "unavailable")
                {
                    if (source.Length == 0) models.Clear();
                    else models.Remove(key);
                }
                string detail = Field(data, "detail");
                if (state == "verification_failed" && !models.Contains(CurrentModelKey))
                {
                    string currentState;
                    modelStates.TryGetValue(CurrentModelKey, out currentState);
                    detail = "\u5f53\u524d\u65b9\u5411\u6a21\u578b\u672a\u5c31\u7eea: " + (currentState ?? "\u68c0\u67e5\u4e2d") +
                        "\uff0c\u8bf7\u5728 Edge \u9875\u9762\u51c6\u5907\u6a21\u578b\u5e76\u68c0\u67e5\u7b56\u7565";
                }
                edgeStatus.Text = "Edge: " + detail.Substring(0, Math.Min(220, detail.Length));
                Diagnostics.Write("edge", new[] { "ready", "error", "unavailable", "loading", "downloadable", "verified", "verification_failed" }.Contains(state) ? state : "unknown");
                return;
            }
            long id;
            int revision, resultGeneration;
            if (!long.TryParse(Field(data, "segmentId"), out id) || !int.TryParse(Field(data, "revision"), out revision) ||
                !int.TryParse(Field(data, "generation"), out resultGeneration) || resultGeneration != generation) return;
            Work work;
            if (!inFlight.TryGetValue(id, out work) || work.Revision != revision) return;
            if (Field(data, "sourceLanguage") != settings.SourceLanguage || Field(data, "targetLanguage") != settings.TargetLanguage) return;
            string translation = Field(data, "translatedText");
            if (Field(data, "status") != "ok" || string.IsNullOrWhiteSpace(translation) || translation.Length > 8000)
            {
                work.RetryAt = DateTime.UtcNow.AddSeconds(1);
                Diagnostics.Write("translation", "api_error");
                edgeStatus.Text = "Edge: \u5355\u53e5\u7ffb\u8bd1\u5931\u8d25\uff0c\u7b49\u5f85\u91cd\u8bd5";
                return;
            }
            inFlight.Remove(id);
            if (!tracker.Accept(id, revision, settings.SourceLanguage, translation) &&
                !tracker.AcceptPreview(id, revision, settings.SourceLanguage, work.Text, translation)) return;
            accepted++;
            double elapsed = (DateTime.UtcNow - work.Segment.Changed).TotalMilliseconds;
            durations.Enqueue(elapsed);
            while (durations.Count > 500) durations.Dequeue();
            Diagnostics.Write("translation", "display_after_change", elapsed);
            double apiMs;
            if (double.TryParse(Field(data, "durationMs"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out apiMs))
                Diagnostics.Write("translation", settings.SourceLanguage + "_api", Math.Min(60000, Math.Max(0, apiMs)));
            dirty = true;
        }

        private void Render()
        {
            if (rows.Children.Count == 0) rows.Children.Add(translationText);
            Segment[] visible = tracker.DisplaySegments.ToArray();
            var visibleIds = new HashSet<long>(visible.Select(segment => segment.Id));
            foreach (long id in displayedTranslations.Keys.Where(id => !visibleIds.Contains(id)).ToArray())
                displayedTranslations.Remove(id);
            foreach (Segment segment in visible)
            {
                if (string.IsNullOrWhiteSpace(segment.Translation)) continue;
                displayedTranslations[segment.Id] = CaptionTracker.Normalize(segment.Translation);
                while (displayedTranslations.Count > 3) displayedTranslations.Remove(displayedTranslations.Keys.First());
            }
            string text = string.Join(Environment.NewLine, displayedTranslations.Values);
            bool changed = translationText.Text != text || translationText.FontSize != settings.FontSize;
            if (translationText.Text != text) translationText.Text = text;
            translationText.Visibility = translationText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            translationText.FontSize = settings.FontSize;
            translationText.LineHeight = settings.FontSize * 1.35;
            UpdateReadiness();
            if (!changed) return;
            double previousOffset = captionScroll.VerticalOffset;
            updatingRows = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                try
                {
                    if (disposed) return;
                    captionScroll.UpdateLayout();
                    if (followingLatest) captionScroll.ScrollToEnd();
                    else captionScroll.ScrollToVerticalOffset(previousOffset);
                    captionScroll.UpdateLayout();
                }
                finally { updatingRows = false; }
            }));
        }

        private void ResizeForFont()
        {
            Render();
            dirty = false;
            rows.Measure(new Size(Math.Max(100, ActualWidth > 0 ? ActualWidth - 48 : Width - 48), double.PositiveInfinity));
            double baseline = 68 + 3 * settings.FontSize * 1.35;
            double desired = Math.Max(160, Math.Min(720, Math.Max(baseline, rows.DesiredSize.Height + 68)));
            ResizeOverlay(Width, desired);
        }

        private void ResizeFromPointer(Vector delta)
        {
            var source = PresentationSource.FromVisual(this);
            if (source != null) delta = source.CompositionTarget.TransformFromDevice.Transform(delta);
            ResizeOverlay(resizeInitialSize.Width + delta.X, resizeInitialSize.Height + delta.Y);
        }

        private void ResizeOverlay(double width, double height)
        {
            height = Math.Max(160, Math.Min(1200, height));
            if (docked) { floatingBounds.Height = height; UpdateDocking(); }
            else
            {
                Width = Math.Max(MinWidth, width);
                Height = height;
            }
            if (heightControl != null && heightControl.Value != height) heightControl.Value = height;
        }

        private void Copy()
        {
            string text = tracker.Segments.LastOrDefault(segment => !string.IsNullOrEmpty(segment.Translation))?.Translation;
            if (text != null) Clipboard.SetText(text);
        }

        private void OpenTranscripts()
        {
            string directory = Path.Combine(Settings.UserDirectory, "transcripts");
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }

        private void ShowDiagnostics()
        {
            double[] values = durations.OrderBy(value => value).ToArray();
            string p95 = values.Length == 0 ? "n/a" : values[(int)Math.Ceiling(values.Length * .95) - 1].ToString("F0") + " ms";
            MessageBox.Show(this, "LCET " + Entry.Version + "\n" + server.Url + "\n" + captureStatus.Text + "\n" + edgeStatus.Text +
                "\nSourceLanguage=" + settings.SourceLanguage + "; LanguageConfirmed=" + settings.LanguageConfirmed + " (Windows language not auto-detected)" +
                "\n" + queueStatus.Text +
                "\n\u6700\u8fd1\u8fd4\u56de " + values.Length + " / \u6587\u672c\u53d8\u66f4\u5230\u7ed3\u679c P95: " + p95 +
                "\n\u6280\u672f\u65e5\u5fd7: " + Path.Combine(Settings.UserDirectory, "logs") +
                "\nTXT: " + (transcript == null ? Path.Combine(Settings.UserDirectory, "transcripts") : transcript.FilePath) +
                "\n\u79bb\u7ebf\u53ca\u540e\u53f0\u957f\u7a33\u5b9a\u6027: \u5f85\u771f\u673a\u9a8c\u6536", "LCET \u8bca\u65ad");
        }

        private void OpenEdge()
        {
            lastLaunch = DateTime.UtcNow;
            try
            {
                Process.Start(new ProcessStartInfo(edgePath, "--new-window \"" + server.Url + "\"") { UseShellExecute = true });
                Diagnostics.Write("edge", "opened");
            }
            catch (Exception exception)
            {
                edgeStatus.Text = "Edge: \u542f\u52a8\u5931\u8d25 " + exception.GetType().Name;
                Diagnostics.Write("edge", "launch_failed");
            }
        }

        private void SessionSwitch(object sender, SessionSwitchEventArgs args)
        {
            OnUi(() =>
            {
                if (args.Reason == SessionSwitchReason.SessionLock || args.Reason == SessionSwitchReason.SessionLogoff)
                { sessionLocked = true; Clear(); }
                if (args.Reason == SessionSwitchReason.SessionUnlock || args.Reason == SessionSwitchReason.SessionLogon)
                { sessionLocked = false; Clear(); lastSnapshot = null; }
            });
        }

        private void Save()
        {
            try { settings.Save(); }
            catch (Exception exception) { Diagnostics.Write("settings", exception.GetType().Name); }
        }

        private void FinishTranscriptOnExit(Func<bool> confirmKeep, Action<string> openRecord)
        {
            if (transcript == null) return;
            transcript.Dispose();
            if (!File.Exists(transcript.FilePath) && transcript.LastError == null) return;
            bool keep = confirmKeep();
            string path = transcript.Finish(keep);
            if (keep && path == null) throw new IOException("transcript_missing");
            if (path != null) openRecord(path);
            if (transcript.LastError != null && path != null)
                throw new IOException("transcript_incomplete");
        }

        private async void Shutdown(object sender, CancelEventArgs args)
        {
            if (allowClose) return;
            args.Cancel = true;
            if (closing) return;
            closing = true;
            timer.Stop();
            ArchiveCompleted(DateTime.UtcNow, true);
            server.Send("control", new { generation = ++generation, shutdown = true });
            await Task.Delay(350);
            Rect savedBounds = docked ? floatingBounds : new Rect(Left, Top, Width, Height);
            settings.Width = savedBounds.Width; settings.Height = savedBounds.Height;
            settings.Left = savedBounds.Left; settings.Top = savedBounds.Top;
            Save();
            Dispose();
            try
            {
                FinishTranscriptOnExit(() => MessageBox.Show(this,
                    "\u662f\u5426\u4fdd\u7559\u672c\u6b21\u4f1a\u8bae\u8bb0\u5f55\uff1f\n\n" +
                    "\u662f\uff1a\u4fdd\u7559\u5e76\u6253\u5f00 TXT\u3002\n" +
                    "\u5426\uff1a\u5220\u9664\u672c\u6b21 TXT\uff0c\u4e0d\u5f71\u54cd\u4ee5\u524d\u7684\u8bb0\u5f55\u3002",
                    "LCET", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) == MessageBoxResult.Yes,
                    path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
            }
            catch (Exception exception)
            {
                Diagnostics.Write("transcript", "finish_" + exception.GetType().Name);
                MessageBox.Show(this,
                    "\u8bb0\u5f55\u4fdd\u7559\u3001\u5220\u9664\u6216\u6253\u5f00\u672a\u80fd\u5b8c\u5168\u5b8c\u6210\uff0c\u8bf7\u68c0\u67e5\u672c\u6b21\u6587\u4ef6\uff1a\n" +
                    transcript.FilePath + "\n" + exception.GetType().Name,
                    "LCET", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            allowClose = true;
            Close();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SystemEvents.SessionSwitch -= SessionSwitch;
            timer.Stop();
            reader.Dispose();
            server.Dispose();
            if (transcript != null) transcript.Dispose();
            tracker.Clear();
            Diagnostics.Write("app", "stopped");
        }
    }
}