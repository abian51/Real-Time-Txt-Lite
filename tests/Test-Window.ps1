param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml, UIAutomationClient, UIAutomationTypes
Add-Type -Path (Join-Path $Root 'bin\LCET.dll')
function Assert($condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}
function Elements($element) {
    if ($element -isnot [System.Windows.DependencyObject]) { return }
    Write-Output $element
    foreach ($child in [System.Windows.LogicalTreeHelper]::GetChildren($element)) { Elements $child }
}
$userSettingsFileBeforeTest = Join-Path ([LCET.Settings]::UserDirectory) 'settings.json'
$userSettingsBackup = if (Test-Path -LiteralPath $userSettingsFileBeforeTest) { [IO.File]::ReadAllBytes($userSettingsFileBeforeTest) } else { $null }
$window = New-Object LCET.MainWindow($Root, 'not-used-by-unshown-window.exe')
$tickFolder = $null
try {
    Assert ($window.WindowStyle -eq [System.Windows.WindowStyle]::None -and $window.AllowsTransparency) 'overlay is borderless with no system title bar'
    Assert ((New-Object LCET.Settings).Opacity -eq 0.5) 'new settings default to fifty percent opacity'
    Assert ($null -eq [LCET.Settings].GetProperty('PrivacyMode')) 'privacy mode setting removed'
    Assert ((New-Object LCET.Settings).SourceLanguage -eq 'en' -and (New-Object LCET.Settings).LanguageConfirmed) 'fresh defaults run English to Chinese without first-run confirmation'
    $controls = @(Elements $window.Content)
    $buttons = @($controls | Where-Object { $_ -is [System.Windows.Controls.Button] })
    Assert ($buttons.Count -eq 4) 'main overlay has microphone, language, settings and exit buttons'
    Assert (@($buttons | Where-Object { [System.Windows.Automation.AutomationProperties]::GetName($_) }).Count -eq 4) 'all toolbar controls expose accessible names'
    $flags = [Reflection.BindingFlags]'Instance,NonPublic,DeclaredOnly'
    $type = $window.GetType()
    $micButton = $type.GetField('captionMicrophoneButton', $flags).GetValue($window)
    $languageButton = $type.GetField('captionLanguageButton', $flags).GetValue($window)
    Assert ([System.Windows.Automation.AutomationProperties]::GetAutomationId($micButton) -eq 'CaptionMicrophone' -and [System.Windows.Automation.AutomationProperties]::GetAutomationId($languageButton) -eq 'CaptionLanguage') 'new caption controls have stable automation identifiers'
    Assert (-not $micButton.IsEnabled -and -not $languageButton.IsEnabled) 'caption controls remain disabled before Windows window is connected'
    $languageSettings = $type.GetField('settings', $flags).GetValue($window)
    $originalSource = $languageSettings.SourceLanguage
    $originalTarget = $languageSettings.TargetLanguage
    $originalConfirmed = $languageSettings.LanguageConfirmed
    $setPair = $type.GetMethod('SetTranslationPair', $flags).CreateDelegate([Func[string,string,bool]], $window)
    $changeLanguage = $type.GetMethod('ChangeCaptionLanguageAsync', $flags).CreateDelegate([Func[string,Func[string,System.Threading.Tasks.Task[string]],System.Threading.Tasks.Task[bool]]], $window)
    $successfulChange = [Func[string,System.Threading.Tasks.Task[string]]]{ param($source)
        $completion = New-Object 'System.Threading.Tasks.TaskCompletionSource[string]'
        $completion.SetResult($source)
        return $completion.Task
    }
    [void]$changeLanguage.Invoke('zh', $successfulChange).GetAwaiter().GetResult()
    Assert ($languageSettings.SourceLanguage -eq 'zh' -and $languageSettings.TargetLanguage -eq 'en' -and $languageSettings.LanguageConfirmed -and $languageButton.Content.ToString().EndsWith('EN')) 'confirmed Windows zh-CN switch also activates Chinese to English translation'
    $failedChange = [Func[string,System.Threading.Tasks.Task[string]]]{ param($source)
        $completion = New-Object 'System.Threading.Tasks.TaskCompletionSource[string]'
        $completion.SetException((New-Object System.InvalidOperationException('synthetic Windows language failure')))
        return $completion.Task
    }
    $changeFailed = $false
    try { [void]$changeLanguage.Invoke('en', $failedChange).GetAwaiter().GetResult() } catch { $changeFailed = $true }
    Assert ($changeFailed -and $languageSettings.SourceLanguage -eq 'zh' -and -not $type.GetField('captionControlBusy', $flags).GetValue($window)) 'failed Windows recognition switch preserves translation direction and releases busy state'
    [void]$changeLanguage.Invoke('en', $successfulChange).GetAwaiter().GetResult()
    Assert ($languageSettings.SourceLanguage -eq 'en' -and $languageSettings.TargetLanguage -eq 'zh' -and $languageSettings.LanguageConfirmed -and $languageButton.Content.ToString().StartsWith('EN')) 'confirmed Windows en-US switch also activates English to Chinese translation'
    [void]$setPair.Invoke($originalSource, $originalTarget)
    $languageSettings.LanguageConfirmed = $originalConfirmed
    $tracker = $type.GetField('tracker', $flags).GetValue($window)
    $languageSettings.TranslationLines = 3
    [void]$tracker.Observe('One. Two. Three. Four.', 'en', [DateTime]::UtcNow)
    foreach ($segment in $tracker.Segments) { [void]$tracker.Accept($segment.Id, $segment.Revision, 'en', ('Translation ' + $segment.Id)) }
    [void]$type.GetMethod('Render', $flags).Invoke($window, @())
    $rows = $type.GetField('rows', $flags).GetValue($window)
    $currentText = $rows.Children[0]
    Assert ($rows.Children.Count -eq 1 -and $currentText -is [System.Windows.Controls.TextBlock] -and $currentText.Text -eq ("Translation 2" + [Environment]::NewLine + "Translation 3" + [Environment]::NewLine + "Translation 4")) 'only three latest translations rendered without source or unbounded history'
    $labels = @('captureStatus','edgeStatus','queueStatus') | ForEach-Object { $type.GetField($_, $flags).GetValue($window) }
    Assert (@($labels | Where-Object { $_.FontSize -eq 9 -and $_.Margin.Top -eq 1 -and $_.LineHeight -eq 12 }).Count -eq 3) 'status font increases by two points with compact vertical margins'
    $status = [System.Windows.LogicalTreeHelper]::GetParent($labels[0])
    Assert ($status -is [System.Windows.Controls.Grid] -and $status.ColumnDefinitions.Count -eq 3 -and $status.RowDefinitions.Count -le 1) 'three status fields share one horizontal grid row'
    Assert (@($labels | Where-Object { $_.TextWrapping -eq [System.Windows.TextWrapping]::NoWrap -and $_.TextTrimming -eq [System.Windows.TextTrimming]::CharacterEllipsis }).Count -eq 3) 'long status text stays on one line with ellipsis'
    Assert ($window.Content.Background -eq [System.Windows.Media.Brushes]::Transparent) 'blank overlay areas participate in pointer hit testing'
    $dragMethod = $type.GetMethod('CanDragFrom', [Reflection.BindingFlags]'Static,NonPublic,DeclaredOnly')
    $drag = $dragMethod.CreateDelegate([Func[System.Windows.DependencyObject,bool]])
    $scroll = @($controls | Where-Object { $_ -is [System.Windows.Controls.ScrollViewer] })[0]
    Assert ($drag.Invoke($scroll) -and $drag.Invoke($currentText)) 'scroll viewer and subtitle text permit window dragging'
    Assert (-not $drag.Invoke($buttons[0])) 'settings button excluded from window dragging'
    $content = $type.GetField('settingsContent', $flags).GetValue($window)
    Assert (@(Elements $content | Where-Object { [System.Windows.Automation.AutomationProperties]::GetAutomationId($_) -eq 'PrivacyMode' }).Count -eq 0) 'settings window has no privacy toggle'
    Assert ($null -eq $type.GetField('confirmed', $flags) -and @(Elements $content | Where-Object { $_ -is [System.Windows.Controls.Button] -and $_.Content -eq (([string][char]0x5E94) + [char]0x7528 + [char]0x65B9 + [char]0x5411) }).Count -eq 0) 'language settings have no confirmation checkbox or Apply Direction button'
    Assert (@(Elements $content | Where-Object { $_ -is [System.Windows.Controls.ComboBox] }).Count -eq 3) 'source target and translation line controls are available inside settings'
    $sourcePicker = $type.GetField('direction', $flags).GetValue($window)
    $targetPicker = $type.GetField('targetDirection', $flags).GetValue($window)
    Assert ($sourcePicker.Items.Count -eq 154 -and $targetPicker.Items.Count -eq 154) 'native source and target selectors share all 154 web language candidates'
    Assert ((@($sourcePicker.Items | Select-Object -First 3 | ForEach-Object { $_.Tag }) -join ',') -eq 'en,zh,ja') 'native selectors pin English Chinese and Japanese in the same order'
    Assert ($sourcePicker.SelectedValue -eq $originalSource -and $targetPicker.SelectedValue -eq $originalTarget) 'native selectors reflect saved source and target independently'
    $swapDirection = @(Elements $content | Where-Object { [System.Windows.Automation.AutomationProperties]::GetAutomationId($_) -eq 'SwapCaptionDirection' })[0]
    Assert ($swapDirection -is [System.Windows.Controls.Button] -and $swapDirection.ToolTip -and [System.Windows.Automation.AutomationProperties]::GetName($swapDirection)) 'native settings expose an accessible direction swap button'
    Assert ((New-Object LCET.Settings).Height -eq 124 -and $window.MinHeight -eq 96) 'compact overlay defaults to height 124 and permits shrinking to 96'
    Assert ((New-Object LCET.Settings).TranslationColor -eq 'Black') 'fresh translation color defaults to black'
    $colorChoices = @(Elements $content | Where-Object { $_ -is [System.Windows.Controls.RadioButton] -and $_.GroupName -eq 'TranslationColor' })
    Assert ($colorChoices.Count -eq 5) 'settings offer five translation color swatches'
    $originalColor = $languageSettings.TranslationColor
    foreach ($choice in $colorChoices) {
        $choice.IsChecked = $true
        $expectedColor = [System.Windows.Automation.AutomationProperties]::GetAutomationId($choice).Substring('CaptionColor'.Length)
        Assert ($languageSettings.TranslationColor -eq $expectedColor -and $currentText.Foreground.ToString() -eq $choice.Content.Background.ToString()) "color swatch $expectedColor updates settings and translation immediately"
        Assert (@($colorChoices | Where-Object { $_.IsChecked }).Count -eq 1 -and $choice.ToolTip -and [System.Windows.Automation.AutomationProperties]::GetName($choice)) "color swatch $expectedColor is exclusive and accessible"
    }
    ($colorChoices | Where-Object { [System.Windows.Automation.AutomationProperties]::GetAutomationId($_) -eq ('CaptionColor' + $originalColor) }).IsChecked = $true
    $opacity = @(Elements $content | Where-Object { $_ -is [System.Windows.Controls.Slider] -and [System.Windows.Automation.AutomationProperties]::GetAutomationId($_) -eq 'BackgroundOpacity' })[0]
    Assert ($null -ne $opacity -and $opacity.Minimum -eq 0 -and $opacity.Maximum -eq 1) 'background opacity control supports full adjustment range'
    foreach ($value in @(0.2, 0, 1)) {
        $opacity.Value = $value
        Assert ($window.Background.Opacity -eq $value -and $window.Opacity -eq 1 -and $rows.Opacity -eq 1) "background opacity $value does not fade window or subtitle container"
    }
    Assert ($currentText.Opacity -eq 1 -and $currentText.Foreground.Opacity -eq 1) 'translated text and its foreground remain fully opaque'
    $grip = @($controls | Where-Object { $_ -is [System.Windows.Controls.Primitives.Thumb] })[0]
    Assert ($null -ne $grip -and $window.ResizeMode -eq [System.Windows.ResizeMode]::CanResizeWithGrip) 'manual resize grip present'
    Assert (-not $drag.Invoke($grip) -and -not $drag.Invoke((New-Object System.Windows.Controls.Primitives.ScrollBar))) 'resize grip and scrollbar excluded from window dragging'
    [void]$grip.ApplyTemplate()
    $grip.Measure((New-Object System.Windows.Size($grip.Width, $grip.Height)))
    $grip.Arrange((New-Object System.Windows.Rect(0, 0, $grip.Width, $grip.Height)))
    $grip.UpdateLayout()
    Assert ($null -ne [System.Windows.Media.VisualTreeHelper]::HitTest($grip, (New-Object System.Windows.Point(2, 2)))) 'resize grip has a usable visual hit area'
    $window.Width = 800
    $window.Height = 240
    $delta = New-Object System.Windows.Controls.Primitives.DragDeltaEventArgs(60, -40)
    $delta.RoutedEvent = [System.Windows.Controls.Primitives.Thumb]::DragDeltaEvent
    $grip.RaiseEvent($delta)
    Assert ($window.Width -eq 860 -and $window.Height -eq 200) 'dragging grip changes width and height'
    $minimum = New-Object System.Windows.Controls.Primitives.DragDeltaEventArgs(-3000, -3000)
    $minimum.RoutedEvent = [System.Windows.Controls.Primitives.Thumb]::DragDeltaEvent
    $grip.RaiseEvent($minimum)
    Assert ($window.Width -eq 420 -and $window.Height -eq 96) 'resize grip enforces minimum dimensions'
    Assert ($grip.Width -ge 28 -and $grip.Height -ge 28 -and [System.Windows.Automation.AutomationProperties]::GetAutomationId($grip) -eq 'OverlayResizeGrip') 'resize grip has an enlarged stable accessible target'
    $heightControl = @(Elements $content | Where-Object { [System.Windows.Automation.AutomationProperties]::GetAutomationId($_) -eq 'OverlayHeight' })[0]
    Assert ($heightControl -is [System.Windows.Controls.Slider] -and $heightControl.Minimum -eq 96 -and $heightControl.Maximum -eq 1200) 'settings offer a bounded direct height control'
    $resizeFromPointer = $type.GetMethod('ResizeFromPointer', $flags).CreateDelegate([Action[System.Windows.Vector]], $window)
    $type.GetField('resizeInitialSize', $flags).SetValue($window, (New-Object System.Windows.Size(420, 160)))
    $resizeFromPointer.Invoke((New-Object System.Windows.Vector(0, 40)))
    $resizeFromPointer.Invoke((New-Object System.Windows.Vector(0, 60)))
    Assert ($window.Height -eq 220 -and $heightControl.Value -eq 220) 'continuous resize uses total pointer movement instead of accumulating overlapping deltas'
    $resizeFromPointer.Invoke((New-Object System.Windows.Vector(0, 60)))
    Assert ($window.Height -eq 220) 'unchanged screen pointer does not repeatedly grow or shrink the overlay'
    $heightControl.Value = 160
    $heightControl.Value = 320
    Assert ($window.Height -eq 320 -and $window.Width -eq 420) 'height control changes floating height without changing width'
    $heightControl.Value = 160
    Assert ($window.Height -eq 160) 'height control can return the overlay to its previous height'
    $heightControl.Value = 96
    Assert ($window.Height -eq 96) 'height control can remove extra whitespace down to the compact minimum'
    Assert ((New-Object LCET.Settings).FollowWindowsCaptions) 'following Windows captions enabled by default'
    $follow = @(Elements $content | Where-Object { $_ -is [System.Windows.Controls.CheckBox] -and [System.Windows.Automation.AutomationProperties]::GetAutomationId($_) -eq 'FollowWindowsCaptions' })
    Assert ($follow.Count -eq 1) 'follow switch available in settings'
    $area = New-Object System.Windows.Rect(0, 0, 1920, 1040)
    $below = [LCET.WindowDocking]::Place((New-Object System.Windows.Rect(100, 100, 800, 150)), $area, 240)
    Assert ($below.Left -eq 100 -and $below.Top -eq 250 -and $below.Width -eq 800 -and $below.Height -eq 240) 'dock below with exactly matching width'
    $above = [LCET.WindowDocking]::Place((New-Object System.Windows.Rect(100, 900, 600, 140)), $area, 240)
    Assert ($above.Top -eq 660 -and $above.Width -eq 600) 'dock above when bottom has insufficient room'
    $limited = [LCET.WindowDocking]::Place((New-Object System.Windows.Rect(0, 400, 900, 400)), $area, 600)
    Assert ($limited.Top -eq 0 -and $limited.Height -eq 400) 'clamp height to larger available space without overlap'
    $otherMonitor = [LCET.WindowDocking]::Place((New-Object System.Windows.Rect(-1600, 100, 700, 120)), (New-Object System.Windows.Rect(-1920, 0, 1920, 1040)), 200)
    Assert ($otherMonitor.Left -eq -1600 -and $otherMonitor.Top -eq 220 -and $otherMonitor.Width -eq 700) 'negative monitor coordinates preserved'
    $narrow = [LCET.WindowDocking]::Place((New-Object System.Windows.Rect(40, 100, 300, 120)), $area, 240)
    Assert ($narrow.Width -eq 300) 'narrow caption window width not forced to floating minimum'
    $blocked = [LCET.WindowDocking]::Place((New-Object System.Windows.Rect(0, 0, 1920, 1040)), $area, 240)
    Assert ($blocked.IsEmpty) 'no adjacent space returns unattached placement'
    [void]$tracker.Observe('One. Two. Three. Four. Pending', 'en', [DateTime]::UtcNow)
    [void]$type.GetMethod('Render', $flags).Invoke($window, @())
    Assert ($currentText.Visibility -eq [System.Windows.Visibility]::Visible -and $currentText.Text.EndsWith('Translation 4')) 'new pending sentence preserves three prior translations instead of flashing blank'
    Assert ([object]::ReferenceEquals($currentText, $rows.Children[0]) -and $rows.Children.Count -eq 1) 'subtitle updates reuse one fixed visual instead of accumulating history'
    $font = @(Elements $content | Where-Object { $_ -is [System.Windows.Controls.Slider] -and [System.Windows.Automation.AutomationProperties]::GetAutomationId($_) -eq 'CaptionFontSize' })[0]
    $font.Value = 22
    $font.Value = 14
    $smallHeight = $window.Height
    $font.Value = 40
    Assert ($window.Height -gt $smallHeight -and $currentText.FontSize -eq 40) 'larger font increases floating overlay height'
    $font.Value = 14
    Assert ($window.Height -eq $smallHeight) 'smaller font reduces auto-sized height'
    $type.GetField('docked', $flags).SetValue($window, $true)
    $type.GetField('floatingBounds', $flags).SetValue($window, (New-Object System.Windows.Rect(100, 100, 800, 200)))
    $heightControl.Value = 360
    Assert ($type.GetField('floatingBounds', $flags).GetValue($window).Height -eq 360 -and $heightControl.Value -eq 360) 'height control updates the desired docked height even without an active caption window'
    $type.GetField('docked', $flags).SetValue($window, $true)
    $type.GetField('floatingBounds', $flags).SetValue($window, (New-Object System.Windows.Rect(100, 100, 800, 200)))
    $font.Value = 40
    Assert ($type.GetField('floatingBounds', $flags).GetValue($window).Height -ge (32 + 3 * $font.Value * 1.35)) 'docked font change updates remembered height to fit the compact three-line baseline'
    $type.GetField('docked', $flags).SetValue($window, $false)
    $lineCount = @(Elements $content | Where-Object { [System.Windows.Automation.AutomationProperties]::GetAutomationId($_) -eq 'TranslationLines' })[0]
    Assert ((New-Object LCET.Settings).TranslationLines -eq 4 -and ($lineCount.Items -join ',') -eq '2,3,4,5,6') 'translation line setting defaults to four and offers exactly two through six'
    $lineOption = [System.Windows.LogicalTreeHelper]::GetParent($lineCount)
    Assert ($lineOption -is [System.Windows.Controls.StackPanel] -and $lineOption.Orientation -eq [System.Windows.Controls.Orientation]::Horizontal -and $lineOption.Children.Count -eq 2) 'translation row label and selector form one non-splitting horizontal setting'
    foreach ($settingsWidth in @(900, 480)) {
        $content.Measure((New-Object System.Windows.Size($settingsWidth, 340)))
        $content.Arrange((New-Object System.Windows.Rect(0, 0, $settingsWidth, 340)))
        $content.UpdateLayout()
        $labelOrigin = $lineOption.Children[0].TranslatePoint((New-Object System.Windows.Point(0, 0)), $lineOption)
        $pickerOrigin = $lineCount.TranslatePoint((New-Object System.Windows.Point(0, 0)), $lineOption)
        Assert ([Math]::Abs(($labelOrigin.Y + $lineOption.Children[0].ActualHeight / 2) - ($pickerOrigin.Y + $lineCount.ActualHeight / 2)) -lt 1 -and $pickerOrigin.X -ge ($labelOrigin.X + $lineOption.Children[0].ActualWidth)) "translation row selector stays beside its label at settings width $settingsWidth"
    }
    $tracker.Clear()
    [void]$tracker.Observe('Line one. Line two. Line three. Line four. Line five. Line six. Line seven.', 'en', [DateTime]::UtcNow)
    foreach ($segment in $tracker.Segments) { [void]$tracker.Accept($segment.Id, $segment.Revision, 'en', $segment.Text.Replace('Line', 'Translated')) }
    $lineCount.SelectedItem = 6
    Assert ($languageSettings.TranslationLines -eq 6 -and @($tracker.DisplaySegments).Count -eq 6 -and $currentText.Text.Split(@([Environment]::NewLine), [StringSplitOptions]::None).Count -eq 6 -and $currentText.Text.StartsWith('Translated two.')) 'selecting six immediately renders the latest six current translations'
    $sixLineHeight = $window.Height
    $persistedLines = [LCET.Settings]::Load($Root)
    Assert ($persistedLines.TranslationLines -eq 6) 'translation line selection is immediately persisted'
    $lineCount.SelectedItem = 2
    Assert ($languageSettings.TranslationLines -eq 2 -and @($tracker.DisplaySegments).Count -eq 2 -and $currentText.Text -eq ('Translated six.' + [Environment]::NewLine + 'Translated seven.') -and $window.Height -lt $sixLineHeight) 'selecting two prunes older display rows and reduces overlay height without clearing history'
    Assert ($tracker.Segments.Count -eq 7) 'display line selection does not discard transcript or scheduling history'
    [void]$tracker.Observe('Pending new sentence', 'en', [DateTime]::UtcNow)
    $lineCount.SelectedItem = 6
    $lineCount.SelectedItem = 2
    Assert ($type.GetField('displayedTranslations', $flags).GetValue($window).Count -le 2 -and $currentText.Text.StartsWith('Translated six.')) 'changing line count while waiting preserves a bounded last translation frame'
    $lineCount.SelectedItem = 3
    $window.Content.Measure((New-Object System.Windows.Size(900, 124)))
    $window.Content.Arrange((New-Object System.Windows.Rect(0, 0, 900, 124)))
    $window.Content.UpdateLayout()
    $textOrigin = $currentText.TranslatePoint((New-Object System.Windows.Point(0, 0)), $window.Content)
    $toolbar = [System.Windows.LogicalTreeHelper]::GetParent($buttons[0])
    Assert ([System.Windows.Controls.DockPanel]::GetDock($toolbar) -eq [System.Windows.Controls.Dock]::Right -and $textOrigin.Y -le 2) 'toolbar sits beside top-aligned captions instead of reserving a blank top row'
    Assert ($status.Parent.ActualHeight -le 16 -and $grip.ActualHeight -ge 28) 'compact footer keeps the full resize target without reserving a blank bottom row'
    $tracker.Clear()
    [void]$tracker.Observe((('One ' * 60) + '. ' + ('Two ' * 60) + '. ' + ('Three ' * 60) + '.'), 'en', [DateTime]::UtcNow)
    foreach ($segment in $tracker.Segments) { [void]$tracker.Accept($segment.Id, $segment.Revision, 'en', ('translated text ' * 40)) }
    $render = $type.GetMethod('Render', $flags).CreateDelegate([Action], $window)
    $render.Invoke()
    $window.Content.Measure((New-Object System.Windows.Size(420, 200)))
    $window.Content.Arrange((New-Object System.Windows.Rect(0, 0, 420, 200)))
    $window.Content.UpdateLayout()
    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([System.Windows.Threading.DispatcherPriority]::ContextIdle, [Action]{})
    $scroll.UpdateLayout()
    Assert ($currentText.Text.Split(@([Environment]::NewLine), [StringSplitOptions]::None).Count -eq 3) 'display still retains only three current translation segments'
    Assert ($currentText.TextWrapping -eq [System.Windows.TextWrapping]::Wrap -and $currentText.TextTrimming -eq [System.Windows.TextTrimming]::None) 'long translations wrap naturally without ellipsis truncation'
    Assert ($currentText.ActualHeight -gt 3 * $currentText.LineHeight -and $scroll.ScrollableHeight -gt 0) 'narrow window lays out long translations across multiple visual lines with scrolling'
    $previousTranslationFrame = $currentText.Text
    [void]$tracker.Observe('New pending sentence', 'en', [DateTime]::UtcNow)
    $render.Invoke()
    Assert ($currentText.Text -eq $previousTranslationFrame) 'new untranslated snapshot retains the last translation frame instead of flashing a waiting message'
    Assert ($type.GetField('readinessStatus', $flags).GetValue($window).Visibility -eq [System.Windows.Visibility]::Collapsed) 'waiting readiness does not replace retained translations'
    [void]$tracker.Observe('', 'en', [DateTime]::UtcNow)
    $render.Invoke()
    Assert ($currentText.Text -eq $previousTranslationFrame) 'temporary empty visible range retains the last translation frame'
    [void]$tracker.Observe('New pending sentence', 'en', [DateTime]::UtcNow)
    $render.Invoke()
    $newest = $tracker.Segments[$tracker.Segments.Count - 1]
    [void]$tracker.Accept($newest.Id, $newest.Revision, 'en', 'Preview translation')
    $render.Invoke()
    Assert ($currentText.Text -eq 'Preview translation' -and $type.GetField('displayedTranslations', $flags).GetValue($window).Count -eq 1) 'first new visible translation replaces the whole retained frame without pinning old top rows'
    [void]$tracker.Observe('New corrected sentence', 'en', [DateTime]::UtcNow)
    $render.Invoke()
    Assert ($currentText.Text.EndsWith('Preview translation')) 'recognition correction keeps last displayed preview until corrected translation arrives'
    [void]$tracker.Observe('Visible one. Visible two. Visible three. Visible four.', 'en', [DateTime]::UtcNow.AddSeconds(-3))
    $live = @($tracker.DisplaySegments)
    [void]$tracker.Accept($live[2].Id, $live[2].Revision, 'en', 'Current visible translation')
    $render.Invoke()
    $displayBeforeLate = $currentText.Text
    foreach ($old in @($tracker.Segments | Where-Object { $_.Id -lt $live[0].Id })) {
        [void]$tracker.Accept($old.Id, $old.Revision, 'en', 'Delayed archive-only translation')
        $render.Invoke()
        Assert ($currentText.Text -eq $displayBeforeLate) 'delayed old result never displaces current Windows-aligned translation'
    }
    Assert ($live.Count -eq 3 -and $live[0].Text -eq 'Visible two.') 'display window selects newest three source sentences before translation availability'
    [void]$tracker.Observe('Visible three. Visible four. Visible five.', 'en', [DateTime]::UtcNow)
    $render.Invoke()
    Assert ($currentText.Text -eq $displayBeforeLate) 'Windows scrolling retains only still-visible translated sentence without replaying backlog'
    $tracker.Clear()
    $rollingCorrect = $true
    $rollingLateCorrect = $true
    foreach ($number in 1..80) {
        $rollingSources = @($number..($number + 2) | ForEach-Object { "Rolling sentence $_." })
        [void]$tracker.Observe(($rollingSources -join ' '), 'en', [DateTime]::UtcNow)
        foreach ($segment in @($tracker.DisplaySegments)) { [void]$tracker.Accept($segment.Id, $segment.Revision, 'en', ('Translation for ' + $segment.Text)) }
        $render.Invoke()
        $expectedRolling = ($rollingSources | ForEach-Object { 'Translation for ' + $_ }) -join [Environment]::NewLine
        if ($currentText.Text -ne $expectedRolling) { $rollingCorrect = $false }
        $old = @($tracker.Segments | Where-Object { $_.Id -lt @($tracker.DisplaySegments)[0].Id } | Select-Object -First 1)
        if ($old.Count -gt 0) {
            [void]$tracker.Accept($old[0].Id, $old[0].Revision, 'en', 'Stale top translation')
            $render.Invoke()
            if ($currentText.Text -ne $expectedRolling) { $rollingLateCorrect = $false }
        }
    }
    Assert $rollingCorrect 'eighty rolling three-sentence snapshots replace both top translations as well as the last'
    Assert ($rollingLateCorrect -and $type.GetField('displayedTranslations', $flags).GetValue($window).Count -eq 3) 'late old translations cannot pin top rows or expand the three-entry display cache'
    foreach ($number in 1..500) {
        [void]$tracker.Observe('', 'en', [DateTime]::UtcNow)
        [void]$tracker.Observe("Synthetic sentence $number.", 'en', [DateTime]::UtcNow)
        $latest = $tracker.Segments[$tracker.Segments.Count - 1]
        [void]$tracker.Accept($latest.Id, $latest.Revision, 'en', "Current translation $number")
        $render.Invoke()
    }
    Assert ($rows.Children.Count -eq 1 -and [object]::ReferenceEquals($currentText, $rows.Children[0]) -and $currentText.Text -eq 'Current translation 500' -and $type.GetField('displayedTranslations', $flags).GetValue($window).Count -eq 1) 'five hundred sentences retain fixed visual and only currently visible translation'
    Assert ($tracker.Segments.Count -le 80) 'backend processing history stays bounded during long meeting simulation'
    $folder = Join-Path ([IO.Path]::GetTempPath()) ('LCET-window-transcript-' + [Guid]::NewGuid().ToString('N'))
    $recording = New-Object LCET.TranscriptWriter($folder)
    $savedSettings = $type.GetField('settings', $flags).GetValue($window)
    try {
        $type.GetField('transcript', $flags).SetValue($window, $recording)
        $archive = $type.GetMethod('ArchiveCompleted', $flags).CreateDelegate([Action[DateTime,bool]], $window)
        $type.GetMethod('ResetCaptions', $flags).CreateDelegate([Action], $window).Invoke()
        [void]$tracker.Observe('Allowed sentence.', 'en', [DateTime]::UtcNow)
        $latest = $tracker.Segments[$tracker.Segments.Count - 1]
        [void]$tracker.Accept($latest.Id, $latest.Revision, 'en', 'Allowed translation')
        $archive.Invoke([DateTime]::UtcNow, $true)
        [void]$tracker.Observe('Delayed old source.', 'en', [DateTime]::UtcNow.AddSeconds(-3))
        $delayed = $tracker.Segments[$tracker.Segments.Count - 1]
        [void]$tracker.Observe('Current Windows source.', 'en', [DateTime]::UtcNow.AddSeconds(-3))
        $visibleCurrent = $tracker.Segments[$tracker.Segments.Count - 1]
        [void]$tracker.Accept($visibleCurrent.Id, $visibleCurrent.Revision, 'en', 'Visible current translation')
        $render.Invoke()
        [void]$tracker.Accept($delayed.Id, $delayed.Revision, 'en', 'Saved delayed translation')
        $render.Invoke()
        Assert ($currentText.Text -eq 'Visible current translation') 'late archive-only translation stays off overlay while current Windows translation remains'
        $archive.Invoke([DateTime]::UtcNow, $false)
        $recording.Dispose()
        $recorded = [IO.File]::ReadAllText($recording.FilePath)
        Assert ($recorded.Contains('Allowed translation') -and -not $recorded.Contains('Current translation 500')) 'window automatically archives completed translations after reset'
        Assert ($recorded.Contains('Source: Delayed old source.') -and $recorded.Contains('Translation: Saved delayed translation') -and $recorded.Contains('Visible current translation')) 'delayed hidden translation and current translation are both actually written to meeting TXT'
        [void]$type.GetMethod('Clear', $flags).Invoke($window, @())
        $render.Invoke()
        Assert ($currentText.Text -eq '' -and (Test-Path -LiteralPath $recording.FilePath)) 'clearing overlay clears current translation but preserves transcript file'
    } finally {
        $type.GetField('transcript', $flags).SetValue($window, $null)
        $recording.Dispose()
        if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    }
    $folder = Join-Path ([IO.Path]::GetTempPath()) ('LCET-manual-test-' + [Guid]::NewGuid().ToString('N'))
    $recording = New-Object LCET.TranscriptWriter($folder)
    try {
        $type.GetField('transcript', $flags).SetValue($window, $recording)
        $receive = $type.GetMethod('Receive', $flags).CreateDelegate([Action[string,System.Collections.Generic.Dictionary[string,object]]], $window)
        $data = New-Object 'System.Collections.Generic.Dictionary[string,object]'
        $data['pageId'] = 'manual-test-page'
        $data['generation'] = $type.GetField('generation', $flags).GetValue($window)
        $data['requestId'] = [Guid]::NewGuid().ToString()
        $data['kind'] = 'manual'
        $data['status'] = 'ok'
        $data['sourceLanguage'] = 'zh'
        $data['targetLanguage'] = 'en'
        $data['text'] = 'typed source'
        $data['translatedText'] = 'typed translation'
        $type.GetField('sessionLocked', $flags).SetValue($window, $true)
        $receive.Invoke('/manual-result', $data)
        Assert (-not (Test-Path -LiteralPath $recording.FilePath)) 'locked session still rejects manual recording independently of removed privacy mode'
        $type.GetField('sessionLocked', $flags).SetValue($window, $false)
        $data['text'] = 'x' * 4001
        $receive.Invoke('/manual-result', $data)
        Assert (-not (Test-Path -LiteralPath $recording.FilePath)) 'oversized manual text rejected by native recorder'
        $data['text'] = 'typed source'
        $data['generation'] = [int]$data['generation'] - 1
        $receive.Invoke('/manual-result', $data)
        Assert (-not (Test-Path -LiteralPath $recording.FilePath)) 'stale generation manual translation not recorded'
        $data['generation'] = $type.GetField('generation', $flags).GetValue($window)
        $receive.Invoke('/manual-result', $data)
        $receive.Invoke('/manual-result', $data)
        $data['requestId'] = [Guid]::NewGuid().ToString()
        $data['sourceLanguage'] = 'fr'
        $data['targetLanguage'] = 'ja'
        $data['translatedText'] = 'Japanese manual translation'
        $receive.Invoke('/manual-result', $data)
        $receive.Invoke('/manual-result', $data)
        $recording.Dispose()
        $recorded = [IO.File]::ReadAllText($recording.FilePath)
        Assert ($recorded.Contains('MANUAL ') -and [regex]::Matches($recorded, '(?m)^\[').Count -eq 2) 'native manual results archive each language pair exactly once independently of subtitle language'
        Assert ($recorded.Contains('fr->ja') -and $recorded.Contains('Japanese manual translation')) 'native manual receiver preserves non-English source and target languages in TXT'
        Assert ($currentText.Text -eq '') 'manual translation does not replace current subtitle overlay'
    } finally {
        $type.GetField('sessionLocked', $flags).SetValue($window, $false)
        $type.GetField('transcript', $flags).SetValue($window, $null)
        $recording.Dispose()
        if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    }
    $finishFolder = Join-Path ([IO.Path]::GetTempPath()) ('LCET-exit-test-' + [Guid]::NewGuid().ToString('N'))
    $exitRecord = New-Object LCET.TranscriptWriter($finishFolder)
    $discardRecord = New-Object LCET.TranscriptWriter($finishFolder)
    $emptyRecord = New-Object LCET.TranscriptWriter($finishFolder)
    try {
        $finishExit = $type.GetMethod('FinishTranscriptOnExit', $flags).CreateDelegate([Action[Func[bool],Action[string]]], $window)
        $exitState = @{ Prompts = 0; Opened = $null }
        $keepRecord = [Func[bool]]{ $exitState.Prompts++; return $true }
        $discard = [Func[bool]]{ $exitState.Prompts++; return $false }
        $openRecord = [Action[string]]{ param($path) $exitState.Opened = $path }
        [void]$exitRecord.RecordManual([Guid]::NewGuid().ToString(), 'en', 'exit source', 'exit translation', [DateTime]::UtcNow)
        $type.GetField('transcript', $flags).SetValue($window, $exitRecord)
        $finishExit.Invoke($keepRecord, $openRecord)
        Assert ($exitState.Prompts -eq 1 -and $exitState.Opened -eq $exitRecord.FilePath -and [IO.File]::ReadAllText($exitState.Opened).Contains('exit translation')) 'exit save choice prompts once and opens fully written current TXT'
        [void]$discardRecord.RecordManual([Guid]::NewGuid().ToString(), 'en', 'discard source', 'discard translation', [DateTime]::UtcNow)
        $exitState.Opened = $null
        $type.GetField('transcript', $flags).SetValue($window, $discardRecord)
        $finishExit.Invoke($discard, $openRecord)
        Assert ($exitState.Prompts -eq 2 -and $null -eq $exitState.Opened -and -not (Test-Path -LiteralPath $discardRecord.FilePath) -and (Test-Path -LiteralPath $exitRecord.FilePath)) 'exit discard choice deletes only current TXT without opening or deleting saved sessions'
        $type.GetField('transcript', $flags).SetValue($window, $emptyRecord)
        $finishExit.Invoke($keepRecord, $openRecord)
        Assert ($exitState.Prompts -eq 2 -and $null -eq $exitState.Opened) 'empty session exits without asking to save nonexistent TXT'
    } finally {
        $type.GetField('transcript', $flags).SetValue($window, $null)
        $exitRecord.Dispose()
        $discardRecord.Dispose()
        $emptyRecord.Dispose()
        if (Test-Path -LiteralPath $finishFolder) { Remove-Item -LiteralPath $finishFolder -Recurse -Force }
    }
    $tracker.Clear()
    $tickFolder = Join-Path ([IO.Path]::GetTempPath()) ('LCET-tick-test-' + [Guid]::NewGuid().ToString('N'))
    $type.GetField('transcript', $flags).SetValue($window, (New-Object LCET.TranscriptWriter($tickFolder)))
    $savedSettings.SourceLanguage = 'en'
    $savedSettings.TargetLanguage = 'zh'
    $savedSettings.LanguageConfirmed = $false
    $models = $type.GetField('models', $flags).GetValue($window)
    [void]$models.Add('en')
    $type.GetField('snapshot', $flags).SetValue($window, 'First-run caption example.')
    $type.GetField('readerStatus', $flags).SetValue($window, ([string][char]0x5B57 + [char]0x5E55 + [char]0x5DF2 + [char]0x8FDE + [char]0x63A5))
    $type.GetField('lastSeen', $flags).SetValue($window, [DateTime]::UtcNow)
    $tick = $type.GetMethod('Tick', $flags).CreateDelegate([EventHandler], $window)
    $tick.Invoke($null, [EventArgs]::Empty)
    $readiness = $type.GetField('readinessStatus', $flags).GetValue($window)
    Assert ($tracker.Segments.Count -eq 1) 'legacy unconfirmed language setting no longer blocks captured captions'
    Assert ($readiness.Visibility -eq [System.Windows.Visibility]::Visible -and -not $readiness.Text.Contains('Windows') -and $readiness.FontSize -ge 14) 'legacy confirmation does not show a language blocker'
    $savedSettings.LanguageConfirmed = (New-Object LCET.Settings).LanguageConfirmed
    $tick.Invoke($null, [EventArgs]::Empty)
    Assert ($tracker.Segments.Count -eq 1) 'default English enabled state consumes captured caption snapshot without manual confirmation'
    $segment = $tracker.Segments[0]
    [void]$tracker.Accept($segment.Id, $segment.Revision, 'en', 'Current translated sentence')
    $render.Invoke()
    Assert ($currentText.Text -eq 'Current translated sentence' -and $readiness.Visibility -eq [System.Windows.Visibility]::Collapsed) 'received translation replaces readiness state without extra history or source'
    $type.GetMethod('ResetCaptions', $flags).CreateDelegate([Action], $window).Invoke()
    $models.Clear()
    $render.Invoke()
    Assert ($readiness.Visibility -eq [System.Windows.Visibility]::Visible -and $readiness.Text.Contains('Edge')) 'missing current-direction model visible in empty overlay'
    [void]$models.Add('en')
    $waiting = ([string][char]0x7B49) + [char]0x5F85 + [char]0x8BED + [char]0x97F3
    $type.GetField('readerStatus', $flags).SetValue($window, $waiting)
    $render.Invoke()
    Assert ($readiness.Text -eq $waiting) 'waiting for Windows audio is distinguished from waiting for Edge model'
    $tracker.Clear()
    $type.GetField('inFlight', $flags).GetValue($window).Clear()
    $type.GetField('emitted', $flags).GetValue($window).Clear()
    $type.GetField('snapshot', $flags).SetValue($window, $null)
    $type.GetField('lastSnapshot', $flags).SetValue($window, $null)
    [void]$tracker.Observe((((1..14 | ForEach-Object { "Clause $_" }) -join ', ') + '.'), 'en', [DateTime]::UtcNow.AddSeconds(-3))
    $allIds = @($tracker.Segments | ForEach-Object { $_.Id })
    $tick.Invoke($null, [EventArgs]::Empty)
    $flight = $type.GetField('inFlight', $flags).GetValue($window)
    $scheduledIds = @($flight.Keys | Sort-Object)
    Assert ($flight.Count -eq 2 -and $flight.ContainsKey($allIds[13]) -and $flight.ContainsKey($allIds[12])) 'two interchangeable slots both accept the newest visible captions instead of reserving one for history'
    Assert ($tracker.Segments.Count -eq 14) 'backend retains pending translations beyond three display lines'
    $pendingLabel = ([string][char]0x5F85) + [char]0x63D0 + [char]0x4EA4 + ' 12'
    Assert ($labels[2].Text.Contains($pendingLabel)) 'status exposes twelve unsent clauses behind the two active slots'
    $queuedWork = $flight[$allIds[13]]
    $queuedWork.Sent = [DateTime]::UtcNow.AddSeconds(-20)
    $tick.Invoke($null, [EventArgs]::Empty)
    Assert ($queuedWork.Retries -eq 0 -and $queuedWork.Deadline -gt [DateTime]::UtcNow.AddSeconds(10)) 'in-progress translation is not retried just because later clauses are waiting'
    Assert ($flight.Count -eq 2 -and $flight.ContainsKey($allIds[13]) -and $flight.ContainsKey($allIds[12])) 'repeated ticks cannot exceed the two-slot limit while both results are pending'
    $captionReceive = $type.GetMethod('Receive', $flags).CreateDelegate([Action[string,System.Collections.Generic.Dictionary[string,object]]], $window)
    $sequentialOrder = $true
    $liveUpdatedBeforeDrain = $false
    $dispatchOrder = @($allIds | Select-Object -Last 3 | Sort-Object -Descending) + @($allIds | Select-Object -First 11)
    foreach ($segmentId in ($dispatchOrder | Select-Object -First 12)) {
        if ($flight.Count -gt 2 -or -not $flight.ContainsKey($segmentId)) { $sequentialOrder = $false; break }
        $work = $flight[$segmentId]
        $result = New-Object 'System.Collections.Generic.Dictionary[string,object]'
        $result['generation'] = $type.GetField('generation', $flags).GetValue($window)
        $result['pageId'] = $type.GetField('pageId', $flags).GetValue($window)
        $result['segmentId'] = $segmentId
        $result['revision'] = $work.Revision
        $result['sourceLanguage'] = 'en'
        $result['targetLanguage'] = 'zh'
        $result['status'] = 'ok'
        $result['translatedText'] = "Translation $segmentId"
        $captionReceive.Invoke('/result', $result)
        $tick.Invoke($null, [EventArgs]::Empty)
        if ($segmentId -eq $allIds[13]) {
            $liveUpdatedBeforeDrain = $currentText.Text -eq "Translation $segmentId" -and $flight.Count -eq 2 -and $flight.ContainsKey($allIds[12]) -and $flight.ContainsKey($allIds[11])
        }
    }
    Assert $liveUpdatedBeforeDrain 'a freed slot immediately accepts the next caption while the other slot stays busy and live text updates'
    Assert $sequentialOrder 'visible clauses run newest first and historical backlog then drains in source order'
    $liveFrame = (($allIds | Select-Object -Last 3 | ForEach-Object { "Translation $_" }) -join [Environment]::NewLine)
    Assert ($currentText.Text -eq $liveFrame) 'historical task results never replace the newer live translation frame'
    Assert ($flight.Count -eq 2 -and $flight.ContainsKey($dispatchOrder[12]) -and $flight.ContainsKey($dispatchOrder[13])) 'both remaining historical clauses can use the two slots when no live work is waiting'
    $retryId = $dispatchOrder[12]
    $work = $flight[$retryId]
    $result['segmentId'] = $retryId
    $result['revision'] = $work.Revision
    $result['status'] = 'error'
    $captionReceive.Invoke('/result', $result)
    Assert ($flight.ContainsKey($retryId) -and $work.RetryAt -gt [DateTime]::UtcNow) 'failed sentence retained for delayed retry rather than permanently skipped'
    $work.RetryAt = [DateTime]::UtcNow.AddSeconds(-1)
    $tick.Invoke($null, [EventArgs]::Empty)
    Assert ($work.Retries -eq 1) 'failed sentence is automatically resubmitted within bounded retry budget'
    $result['status'] = 'ok'
    $result['translatedText'] = ' '
    $captionReceive.Invoke('/result', $result)
    Assert ($flight.ContainsKey($retryId) -and $work.Segment.TranslationRevision -ne $work.Revision) 'empty translation is retried and never treated as successfully translated'
    $work.RetryAt = [DateTime]::UtcNow.AddSeconds(-1)
    $tick.Invoke($null, [EventArgs]::Empty)
    Assert ($work.Retries -eq 2) 'second and final retry is submitted for repeated failure'
    $result['translatedText'] = 'Recovered translation'
    $captionReceive.Invoke('/result', $result)
    Assert (-not $flight.ContainsKey($retryId) -and $work.Segment.Translation -eq 'Recovered translation') 'successful retry updates original sentence rather than losing it'
    $tick.Invoke($null, [EventArgs]::Empty)
    Assert ($flight.Count -eq 1 -and $flight.ContainsKey($dispatchOrder[13])) 'last historical clause waits for its predecessor and is eventually submitted'
    $exhausted = $flight[$dispatchOrder[13]]
    $exhausted.Retries = 2
    $exhausted.Deadline = [DateTime]::UtcNow.AddSeconds(-1)
    $tick.Invoke($null, [EventArgs]::Empty)
    Assert (-not $flight.ContainsKey($dispatchOrder[13]) -and $type.GetField('failedTranslations', $flags).GetValue($window) -eq 1) 'retry exhaustion stops work and exposes failure count instead of retrying forever'
    [void]$tracker.Accept($exhausted.Segment.Id, $exhausted.Segment.Revision, 'en', 'Completed old sentence')
    [void]$tracker.Observe('One new utterance.', 'en', [DateTime]::UtcNow.AddSeconds(-3))
    $type.GetMethod('Invalidate', $flags).CreateDelegate([Action], $window).Invoke()
    $tick.Invoke($null, [EventArgs]::Empty)
    Assert ($flight.Count -eq 1 -and @($flight.Values)[0].Text -eq 'One new utterance.') "control reset queues only the new utterance, not translated history (actual queued: $($flight.Count))"
    $liveDuringSpeech = $true
    $deliveredDuringSpeech = 0
    $returnedBeforeSpeech = $type.GetField('accepted', $flags).GetValue($window)
    $result['generation'] = $type.GetField('generation', $flags).GetValue($window)
    foreach ($number in 1..12) {
        [void]$tracker.Observe("Live $number clause A, Live $number clause B, Live $number clause C.", 'en', [DateTime]::UtcNow.AddSeconds(-3))
        foreach ($previousWork in @($flight.Values)) {
            $result['segmentId'] = $previousWork.Segment.Id
            $result['revision'] = $previousWork.Revision
            $result['status'] = 'ok'
            $result['translatedText'] = "Delayed translation $number"
            $captionReceive.Invoke('/result', $result)
            $deliveredDuringSpeech++
        }
        $tick.Invoke($null, [EventArgs]::Empty)
        $liveWork = @($flight.Values | Where-Object { $_.Text -eq "Live $number clause C." })[0]
        if ($null -eq $liveWork -or $liveWork.Text -ne "Live $number clause C.") { $liveDuringSpeech = $false; break }
        $result['segmentId'] = $liveWork.Segment.Id
        $result['revision'] = $liveWork.Revision
        $result['translatedText'] = "Live translation $number"
        $captionReceive.Invoke('/result', $result)
        $deliveredDuringSpeech++
        $tick.Invoke($null, [EventArgs]::Empty)
        if ($currentText.Text -ne "Live translation $number" -or $flight.Count -ne 2 -or @($tracker.Ready([DateTime]::UtcNow, $savedSettings.DebounceMs)).Count -le 1) { $liveDuringSpeech = $false; break }
    }
    Assert $liveDuringSpeech 'twelve continuously rolling snapshots update live text while translation and submission backlog remain active'
    Assert (($type.GetField('accepted', $flags).GetValue($window) - $returnedBeforeSpeech) -eq $deliveredDuringSpeech -and $deliveredDuringSpeech -ge 24) 'both slots can keep returning results without delaying live display until the queue stops'
    [void]$tracker.Observe('Latest requested clause.', 'en', [DateTime]::UtcNow.AddSeconds(-3))
    foreach ($previousWork in @($flight.Values)) {
        $result['segmentId'] = $previousWork.Segment.Id
        $result['revision'] = $previousWork.Revision
        $result['translatedText'] = 'Delayed prior clause'
        $captionReceive.Invoke('/result', $result)
    }
    $tick.Invoke($null, [EventArgs]::Empty)
    $movingWork = @($flight.Values)[0]
    Assert ($currentText.Text -eq 'Live translation 12') 'an older delayed live result cannot replace a newer displayed translation'
    Assert ($movingWork.Text -eq 'Latest requested clause.') 'newest live clause is selected before the source scrolls again'
    [void]$tracker.Observe('Speech already moved to another clause.', 'en', [DateTime]::UtcNow.AddSeconds(-3))
    $result['segmentId'] = $movingWork.Segment.Id
    $result['revision'] = $movingWork.Revision
    $result['translatedText'] = 'Latest available live translation'
    $captionReceive.Invoke('/result', $result)
    $tick.Invoke($null, [EventArgs]::Empty)
    Assert ($currentText.Text.EndsWith('Latest available live translation') -and $flight.Count -eq 1) 'a live result updates the last available translation even when speech scrolls before it returns'
    Assert ($setPair.Invoke('en', 'ja') -and $savedSettings.LanguageConfirmed) 'changing only target preserves recognition confirmation'
    Assert ($targetPicker.SelectedValue -eq 'ja' -and $languageButton.Content.ToString() -eq 'EN/' + [char]0x4E2D) 'native target selector updates while bilingual shortcut remains available'
    [void]$tracker.Observe('Selected target caption.', 'en', 'ja', [DateTime]::UtcNow.AddSeconds(-3))
    $tick.Invoke($null, [EventArgs]::Empty)
    Assert ($flight.Count -eq 0) 'English Chinese model readiness never unlocks English Japanese scheduling'
    $pairResult = New-Object 'System.Collections.Generic.Dictionary[string,object]'
    $pairResult['generation'] = $type.GetField('generation', $flags).GetValue($window)
    $pairResult['pageId'] = $type.GetField('pageId', $flags).GetValue($window)
    $pairResult['sourceLanguage'] = 'en'
    $pairResult['targetLanguage'] = 'ja'
    $pairResult['state'] = 'ready'
    $captionReceive.Invoke('/status', $pairResult)
    $tick.Invoke($null, [EventArgs]::Empty)
    Assert ($flight.Count -eq 1) 'explicit selected pair readiness unlocks native caption scheduling'
    $selectedWork = @($flight.Values)[0]
    Assert ($selectedWork.Segment.Source -eq 'en' -and $selectedWork.Segment.Target -eq 'ja') 'scheduled caption stores its selected target independently of source'
    $pairResult['segmentId'] = $selectedWork.Segment.Id
    $pairResult['revision'] = $selectedWork.Revision
    $pairResult['status'] = 'ok'
    $pairResult['translatedText'] = 'Japanese caption'
    $pairResult['targetLanguage'] = 'zh'
    $captionReceive.Invoke('/result', $pairResult)
    Assert ($flight.Count -eq 1 -and -not $selectedWork.Segment.Translation) 'caption result with wrong target is rejected'
    $pairResult['targetLanguage'] = 'ja'
    $captionReceive.Invoke('/result', $pairResult)
    Assert ($flight.Count -eq 0 -and $selectedWork.Segment.Translation -eq 'Japanese caption') 'caption result with selected target is accepted'
    $archive = $type.GetMethod('ArchiveCompleted', $flags).CreateDelegate([Action[DateTime,bool]], $window)
    $archive.Invoke([DateTime]::UtcNow, $true)
    $selectedRecord = $type.GetField('transcript', $flags).GetValue($window)
    $selectedRecord.Dispose()
    $selectedText = [IO.File]::ReadAllText($selectedRecord.FilePath, [Text.Encoding]::UTF8)
    Assert ($selectedText.Contains('en->ja') -and $selectedText.Contains('Translation: Japanese caption')) 'actual meeting TXT preserves selected caption target'
    Assert ($setPair.Invoke('fr', 'ja') -and $savedSettings.LanguageConfirmed) 'changing source immediately enables translation without recognition confirmation'
    Assert (-not $setPair.Invoke('fr', 'fr') -and $savedSettings.TargetLanguage -eq 'ja') 'identical native source and target are rejected without changing active pair'
    Assert (-not $setPair.Invoke('xx-invalid', 'ja') -and $savedSettings.SourceLanguage -eq 'fr') 'native selection rejects languages outside the shared catalog'
    $captionReceive.Invoke('/result', $pairResult)
    Assert ($tracker.Segments.Count -eq 0 -and $flight.Count -eq 0) 'late old-direction result never appears after a native pair change'
    $swapPair = $type.GetMethod('SwapTranslationPair', $flags).CreateDelegate([Func[bool]], $window)
    Assert ($swapPair.Invoke() -and $savedSettings.SourceLanguage -eq 'ja' -and $savedSettings.TargetLanguage -eq 'fr') 'native direction swap immediately applies the reversed arbitrary language pair'
    Assert ($sourcePicker.SelectedValue -eq 'ja' -and $targetPicker.SelectedValue -eq 'fr' -and $savedSettings.LanguageConfirmed) 'native swap updates both selectors without a recognition confirmation gate'
    Assert ($swapPair.Invoke() -and $savedSettings.SourceLanguage -eq 'fr' -and $savedSettings.TargetLanguage -eq 'ja') 'second native swap restores the original arbitrary direction'
    $selectionGeneration = $type.GetField('generation', $flags).GetValue($window)
    $sourcePicker.SelectedValue = 'ja'
    Assert ($savedSettings.SourceLanguage -eq 'ja' -and $savedSettings.TargetLanguage -eq 'fr' -and $targetPicker.SelectedValue -eq 'fr') 'selecting the current target as source automatically swaps the pair'
    Assert ($type.GetField('generation', $flags).GetValue($window) -eq $selectionGeneration + 1) 'automatic picker swap invalidates once without recursive selection events'
    $targetPicker.SelectedValue = 'zh'
    Assert ($savedSettings.SourceLanguage -eq 'ja' -and $savedSettings.TargetLanguage -eq 'zh') 'target selection immediately applies without clicking a button'
    $persistedPair = [IO.File]::ReadAllText($userSettingsFileBeforeTest) | ConvertFrom-Json
    Assert ($persistedPair.SourceLanguage -eq 'ja' -and $persistedPair.TargetLanguage -eq 'zh') 'picker change immediately persists the active pair'
    $targetPicker.SelectedValue = 'ja'
    Assert ($savedSettings.SourceLanguage -eq 'zh' -and $savedSettings.TargetLanguage -eq 'ja' -and $sourcePicker.SelectedValue -eq 'zh') 'selecting the current source as target automatically swaps the pair'
    $sourcePicker.SelectedValue = 'fr'
    Assert ($savedSettings.SourceLanguage -eq 'fr' -and $savedSettings.TargetLanguage -eq 'ja') 'source selection immediately applies without a confirmation step'
    $selectionGeneration = $type.GetField('generation', $flags).GetValue($window)
    [void]$setPair.Invoke('fr', 'ja')
    Assert ($type.GetField('generation', $flags).GetValue($window) -eq $selectionGeneration) 'unchanged pair does not cancel tasks or reset caption generation'
    $type.GetField('captionControlBusy', $flags).SetValue($window, $true)
    try { $sourcePicker.SelectedValue = 'en' } finally { $type.GetField('captionControlBusy', $flags).SetValue($window, $false) }
    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([System.Windows.Threading.DispatcherPriority]::ContextIdle, [Action]{})
    Assert ($savedSettings.SourceLanguage -eq 'fr' -and $sourcePicker.SelectedValue -eq 'fr' -and $type.GetField('generation', $flags).GetValue($window) -eq $selectionGeneration) 'busy Windows controls reject selection and restore the active picker without resetting tasks'
    $swapDirection.RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Button]::ClickEvent)))
    $persistedPair = [IO.File]::ReadAllText($userSettingsFileBeforeTest) | ConvertFrom-Json
    Assert ($savedSettings.SourceLanguage -eq 'ja' -and $savedSettings.TargetLanguage -eq 'fr' -and $persistedPair.SourceLanguage -eq 'ja' -and $persistedPair.TargetLanguage -eq 'fr' -and $type.GetField('generation', $flags).GetValue($window) -eq $selectionGeneration + 1) 'swap button atomically applies and saves exactly once with live selection handlers'
    [void]$changeLanguage.Invoke('en', $successfulChange).GetAwaiter().GetResult()
    Assert ($savedSettings.SourceLanguage -eq 'en' -and $savedSettings.TargetLanguage -eq 'zh' -and $savedSettings.LanguageConfirmed -and $targetPicker.SelectedValue -eq 'zh') 'bilingual shortcut restores English Chinese from arbitrary selected pair'
    [void]$setPair.Invoke($originalSource, $originalTarget)
    $savedSettings.LanguageConfirmed = $originalConfirmed
} finally {
    $window.Dispose()
    if ($null -ne $userSettingsBackup) { [IO.File]::WriteAllBytes($userSettingsFileBeforeTest, $userSettingsBackup) }
    elseif (Test-Path -LiteralPath $userSettingsFileBeforeTest) { Remove-Item -LiteralPath $userSettingsFileBeforeTest -Force }
    if ($tickFolder -and (Test-Path -LiteralPath $tickFolder)) { Remove-Item -LiteralPath $tickFolder -Recurse -Force }
}