param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $Root 'bin\LCET.dll')
$now = [DateTime]::UtcNow
$matchLanguage = [LCET.CaptionReader].GetMethod('IsRecognitionLanguage', [Reflection.BindingFlags]'Static,NonPublic').CreateDelegate([Func[string,string,bool]])
if (-not $matchLanguage.Invoke('English (United States)', 'en') -or $matchLanguage.Invoke('English (United Kingdom)', 'en')) { throw 'Recognition English must be US only' }
if (-not $matchLanguage.Invoke('Chinese (Simplified, Mainland China)', 'zh') -or $matchLanguage.Invoke('Chinese (Traditional, Taiwan)', 'zh') -or $matchLanguage.Invoke('Chinese (Traditional, Hong Kong SAR)', 'zh')) { throw 'Recognition Chinese must be simplified Mainland China only' }
Write-Output 'PASS recognition language matching accepts en-US and zh-CN only'
$tracker = New-Object LCET.CaptionTracker
function Assert($condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}
[void]$tracker.Observe('Hello wor', 'en', $now)
$id = $tracker.Segments[0].Id
[void]$tracker.Observe('Hello world', 'en', $now)
Assert ($tracker.Segments.Count -eq 1 -and $tracker.Segments[0].Id -eq $id) 'append updates same segment'
Assert (-not $tracker.Accept($id, 1, 'en', 'old')) 'stale revision rejected'
[void]$tracker.Observe('Hello word', 'en', $now)
Assert ($tracker.Segments.Count -eq 1 -and $tracker.Segments[0].Text -eq 'Hello word') 'recognition correction replaces current segment'
[void]$tracker.Observe('Hello word.', 'en', $now)
Assert ($tracker.Segments.Count -eq 1) 'punctuation does not duplicate segment'
$revision = $tracker.Segments[0].Revision
Assert ($tracker.Accept($id, $revision, 'en', 'valid')) 'latest revision accepted'
Assert (-not $tracker.Accept($id, $revision, 'zh', 'wrong')) 'wrong direction rejected'
Assert (-not $tracker.Observe('Hello word.', 'en', $now)) 'unchanged snapshot ignored'
[void]$tracker.Observe('Hello world.', 'en', $now)
Assert ($tracker.Segments.Count -eq 1 -and $tracker.Segments[0].Id -eq $id) 'revision after final punctuation keeps same segment'
[void]$tracker.Observe('Hello word.', 'en', $now)
[void]$tracker.Observe('Hello word. Next sentence', 'en', $now)
[void]$tracker.Observe('Next sentence continues', 'en', $now)
Assert ($tracker.Segments.Count -eq 2 -and $tracker.Segments[1].Text -eq 'Next sentence continues') 'history scroll preserves pending sentence'
[void]$tracker.Observe('', 'en', $now)
[void]$tracker.Observe('Hello word.', 'en', $now)
Assert ($tracker.Segments.Count -eq 3) 'same spoken sentence after clear is new segment'
$long = New-Object LCET.CaptionTracker
[void]$long.Observe(('x' * 700), 'en', $now)
Assert ($long.Segments.Count -eq 4) 'long unpunctuated input bounded'
Assert (@($long.Ready($now, 400)).Count -eq 0) 'debounce holds fresh segments'
Assert (@($long.Ready($now.AddMilliseconds(500), 400)).Count -eq 4) 'debounce releases settled segments'
$tracker.Clear()
Assert (-not $tracker.Accept($id, $revision, 'en', 'old')) 'cleared history rejects in-flight result'
$repeat = New-Object LCET.CaptionTracker
[void]$repeat.Observe('Yes.', 'en', $now)
[void]$repeat.Observe('Yes. Yes.', 'en', $now)
Assert ($repeat.Segments.Count -eq 2) 'legitimate repeated sentences are retained'
Assert (@($repeat.DisplaySegments).Count -eq 2) 'two visible repeated sentences retain separate display slots'
[void]$repeat.Observe('', 'en', $now)
Assert (@($repeat.DisplaySegments).Count -eq 0) 'empty Windows snapshot has no eligible display segments'
[void]$repeat.Observe('Yes.', 'en', $now)
Assert (@($repeat.DisplaySegments).Count -eq 1 -and @($repeat.DisplaySegments)[0].Id -eq $repeat.Segments[2].Id) 'one repeated current sentence cannot reuse identical historical display slots'
$history = New-Object LCET.CaptionTracker
[void]$history.Observe('One. Two. Three.', 'en', $now)
Assert ($history.Segments.Count -eq 3) 'initial multi-sentence snapshot split'
[void]$history.Observe('Two. Three. Four. Five.', 'en', $now)
Assert ((@($history.DisplaySegments | ForEach-Object { $_.Text }) -join '|') -eq 'Three.|Four.|Five.') 'display eligibility follows newest three sentences in scrolling Windows snapshot'
$chinese = ([string][char]0x4F60) + [char]0x597D + [char]0x3002
$mixed = New-Object LCET.CaptionTracker
[void]$mixed.Observe(($chinese + 'Hello!'), 'zh', $now)
Assert ($mixed.Segments.Count -eq 2) 'Chinese punctuation and mixed text supported'
Assert ([LCET.CaptionTracker]::Normalize(" a`r`n b`t c ") -eq 'a b c') 'whitespace normalized'
Assert ([LCET.LanguageResources]::Evaluate('en', 'zh', [string[]]@('en-US', 'zh-CN'))['state'] -eq 'available') 'Windows speech pack check requires both requested languages to be installed'
Assert ([LCET.LanguageResources]::Evaluate('en', 'ja', [string[]]@('en-US', 'zh-CN'))['missing'][0] -eq 'ja') 'Windows speech pack check identifies missing Japanese resource'
Assert ([LCET.LanguageResources]::Evaluate('en', 'zh-hant', [string[]]@('en-US', 'zh-CN'))['state'] -eq 'missing') 'simplified Chinese speech pack does not imply traditional Chinese is installed'
Assert ([LCET.LanguageResources]::Evaluate('en', 'yue', [string[]]@('en-US', 'zh-HK'))['state'] -eq 'available') 'Cantonese language selection matches the Windows Hong Kong speech pack'
Assert ([LCET.LanguageResources]::Evaluate('en', 'zh', $null)['state'] -eq 'unknown') 'failed Windows package query is never reported as available or missing'
$commas = New-Object LCET.CaptionTracker
[void]$commas.Observe('First clause, second clause, final clause.', 'en', $now)
Assert (($commas.Segments.Text -join '|') -eq 'First clause,|second clause,|final clause.') 'English comma clauses become separate translation and display segments'
$commas.Clear()
$comma = [string][char]0xFF0C
[void]$commas.Observe(('First clause' + $comma + 'second clause' + $comma + 'final clause.'), 'zh', $now)
Assert ($commas.Segments.Count -eq 3 -and $commas.Segments[0].Text.EndsWith($comma)) 'Chinese comma clauses become separate segments without dropping punctuation'
$commas.Clear()
[void]$commas.Observe('Budget is 1,000 dollars, proceed.', 'en', $now)
Assert (($commas.Segments.Text -join '|') -eq 'Budget is 1,000 dollars,|proceed.') 'numeric commas are preserved rather than splitting amounts'
$commas.Clear()
[void]$commas.Observe('First clause', 'en', $now)
$clauseId = $commas.Segments[0].Id
[void]$commas.Observe('First clause, next clause', 'en', $now)
Assert ($commas.Segments.Count -eq 2 -and $commas.Segments[0].Id -eq $clauseId -and $commas.Segments[0].Text -eq 'First clause,') 'recognition appending comma completes existing clause without duplicating it'
$bounded = New-Object LCET.CaptionTracker
foreach ($number in 1..120) {
    [void]$bounded.Observe('', 'en', $now)
    [void]$bounded.Observe("Sentence $number.", 'en', $now)
}
Assert ($bounded.Segments.Count -eq 80) 'history bounded at eighty segments'
$removed = $bounded.TrimHistory(3)
Assert ($removed.Length -eq 77 -and $bounded.Segments.Count -eq 3) 'history trimming respects requested retention bound'
$pendingHistory = New-Object LCET.CaptionTracker
[void]$pendingHistory.Observe('One. Two. Three. Four. Five.', 'en', $now)
$removed = $pendingHistory.TrimHistory(3, $true)
Assert ($removed.Length -eq 0 -and $pendingHistory.Segments.Count -eq 5) 'history cleanup preserves untranslated sentences instead of cancelling backlog'
$oldest = $pendingHistory.Segments[0]
[void]$pendingHistory.Accept($oldest.Id, $oldest.Revision, 'en', 'translated oldest')
$removed = $pendingHistory.TrimHistory(3, $true)
Assert ($removed.Length -eq 1 -and $removed[0] -eq $oldest.Id -and $pendingHistory.Segments.Count -eq 4) 'history cleanup removes completed old translation while preserving remaining pending work'
$live = New-Object LCET.CaptionTracker
[void]$live.Observe('We', 'en', $now)
foreach ($number in 1..5) { [void]$live.Observe(('We ' + ('word ' * $number)), 'en', $now.AddMilliseconds($number * 200)) }
$due = @($live.Ready($now.AddMilliseconds(1100), 400))
Assert ($due.Count -eq 1) 'continuous speech gets preview without waiting for silence'
$requested = $live.Segments[0].Text
$liveRevision = $live.Segments[0].Revision
$liveId = $live.Segments[0].Id
[void]$live.Observe(($requested + ' next'), 'en', $now.AddMilliseconds(1200))
Assert ($live.AcceptPreview($liveId, $liveRevision, 'en', $requested, 'preview')) 'append-only update accepts newest requested prefix preview'
[void]$live.Observe(($requested + ' next word'), 'en', $now.AddMilliseconds(1400))
Assert ($live.Segments[0].Translation -eq 'preview') 'append retains prior preview until replacement arrives'
[void]$live.Observe('We corrected the sentence', 'en', $now.AddMilliseconds(1600))
Assert (-not $live.AcceptPreview($liveId, $liveRevision, 'en', $requested, 'stale')) 'rewritten source rejects outdated prefix preview'
$settingsFolder = Join-Path ([IO.Path]::GetTempPath()) ('LCET-language-settings-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($settingsFolder)
try {
    $settingsFile = Join-Path $settingsFolder 'settings.json'
    $loadSettings = [LCET.Settings].GetMethod('LoadFile', [Reflection.BindingFlags]'Static,NonPublic').CreateDelegate([Func[string,LCET.Settings]])
    [IO.File]::WriteAllText($settingsFile, '{"SourceLanguage":"en","LanguageConfirmed":false}')
    $loaded = $loadSettings.Invoke($settingsFile)
    Assert ($loaded.SourceLanguage -eq 'en' -and $loaded.LanguageConfirmed -and $loaded.LanguageSetupVersion -eq 1) 'legacy unconfirmed English setup migrated to automatic English to Chinese'
    [IO.File]::WriteAllText($settingsFile, '{"SourceLanguage":"zh","LanguageConfirmed":false}')
    $loaded = $loadSettings.Invoke($settingsFile)
    Assert ($loaded.SourceLanguage -eq 'zh' -and -not $loaded.LanguageConfirmed) 'migration does not overwrite existing Chinese direction or confirmation'
    [IO.File]::WriteAllText($settingsFile, '{"SourceLanguage":"en","LanguageConfirmed":false,"LanguageSetupVersion":1}')
    $loaded = $loadSettings.Invoke($settingsFile)
    Assert (-not $loaded.LanguageConfirmed) 'migration runs once and preserves later explicit user opt-out'
    [IO.File]::WriteAllText($settingsFile, '{"SourceLanguage":"zh","LanguageConfirmed":true,"PrivacyMode":true,"TranscriptSettingsVersion":1,"FontSize":30}')
    $loaded = $loadSettings.Invoke($settingsFile)
    Assert ($loaded.SourceLanguage -eq 'zh' -and $loaded.LanguageConfirmed -and $loaded.FontSize -eq 30 -and $null -eq $loaded.GetType().GetProperty('PrivacyMode')) 'legacy privacy fields ignored while valid user settings are preserved'
    Add-Type -AssemblyName System.Web.Extensions
    $serialized = (New-Object System.Web.Script.Serialization.JavaScriptSerializer).Serialize($loaded)
    Assert ($serialized -notmatch 'PrivacyMode|TranscriptSettingsVersion') 'saved settings no longer contain removed privacy fields'
    [IO.File]::WriteAllText($settingsFile, '{"OverlayLayoutVersion":2,"Height":240}')
    $loaded = $loadSettings.Invoke($settingsFile)
    Assert ($loaded.Height -eq 160 -and $loaded.OverlayLayoutVersion -eq 3 -and $loaded.TranslationColor -eq 'Black') 'old default height migrates to 160 and missing color defaults to black'
    [IO.File]::WriteAllText($settingsFile, '{"OverlayLayoutVersion":2,"Height":420,"TranslationColor":"Blue"}')
    $loaded = $loadSettings.Invoke($settingsFile)
    Assert ($loaded.Height -eq 420 -and $loaded.TranslationColor -eq 'Blue') 'custom height and selected translation color survive loading'
    [IO.File]::WriteAllText($settingsFile, '{"OverlayLayoutVersion":3,"Height":240,"TranslationColor":"invalid"}')
    $loaded = $loadSettings.Invoke($settingsFile)
    Assert ($loaded.Height -eq 240 -and $loaded.TranslationColor -eq 'Black') 'height migration runs once and invalid color falls back to black'
    $serialized = (New-Object System.Web.Script.Serialization.JavaScriptSerializer).Serialize($loaded)
    Assert ($serialized -match '"TranslationColor":"Black"') 'translation color is included in persisted settings'
    [IO.File]::WriteAllText($settingsFile, '{"SourceLanguage":"zh"}')
    $loaded = $loadSettings.Invoke($settingsFile)
    Assert ($loaded.TargetLanguage -eq 'en') 'legacy Chinese source retains its English target'
    [IO.File]::WriteAllText($settingsFile, '{"SourceLanguage":"fr","TargetLanguage":"ja"}')
    $loaded = $loadSettings.Invoke($settingsFile)
    Assert ($loaded.SourceLanguage -eq 'fr' -and $loaded.TargetLanguage -eq 'ja') 'caption settings preserve explicit multilingual translation pair'
    [IO.File]::WriteAllText($settingsFile, '{"SourceLanguage":"en","TargetLanguage":"en"}')
    $loaded = $loadSettings.Invoke($settingsFile)
    Assert ($loaded.TargetLanguage -eq 'zh') 'invalid identical caption languages fall back to a distinct target'
} finally { Remove-Item -LiteralPath $settingsFolder -Recurse -Force }
$completed = New-Object LCET.CaptionTracker
[void]$completed.Observe('Completed.', 'en', $now)
$completedSegment = $completed.Segments[0]
[void]$completed.Accept($completedSegment.Id, $completedSegment.Revision, 'en', ([string][char]0x4F60 + [char]0x597D))
Assert (@($completed.Completed($now.AddMilliseconds(500))).Count -eq 0) 'recording waits for sentence correction grace period'
Assert (@($completed.Completed($now.AddSeconds(1))).Count -eq 1) 'completed full translation becomes recordable'
$folder = Join-Path ([IO.Path]::GetTempPath()) ('LCET-transcript-test-' + [Guid]::NewGuid().ToString('N'))
$writer = New-Object LCET.TranscriptWriter($folder)
try {
    $file = $writer.FilePath
    Assert ($writer.RecordCompleted($completed, $now.AddSeconds(1)) -eq 1) 'completed sentence queued for background recording'
    Assert ($writer.RecordCompleted($completed, $now.AddSeconds(2)) -eq 0) 'repeated ticks do not duplicate recorded sentence'
    [void]$completed.Observe('Completed correctly.', 'en', $now.AddSeconds(2))
    [void]$completed.Accept($completedSegment.Id, $completedSegment.Revision, 'en', 'corrected translation')
    Assert ($writer.RecordCompleted($completed, $now.AddSeconds(3)) -eq 1) 'recognition correction appended with same sentence id and new revision'
    Assert ($writer.RecordManual([Guid]::NewGuid().ToString(), 'zh', 'typed source', 'typed translation', $now) -eq $true) 'manual text translation uses shared bounded transcript writer'
    Assert ($writer.RecordManual([Guid]::NewGuid().ToString(), 'fr', 'ja', 'French source', 'Japanese translation', $now)) 'manual transcript accepts explicit non-English translation pairs'
    Assert (-not $writer.RecordManual([Guid]::NewGuid().ToString(), 'en', 'en', 'same language', 'same language', $now)) 'manual transcript rejects identical language pairs'
    Assert (-not [LCET.LanguageResources]::IsLanguageTag("en`nFORGED")) 'language tags reject multiline transcript header injection'
    Assert (-not [LCET.LanguageResources]::IsLanguageTag("en`n")) 'language tags also reject a trailing newline'
    $multilingual = New-Object LCET.CaptionTracker
    [void]$multilingual.Observe('Bonjour.', 'fr', 'ja', $now)
    $multilingualSegment = $multilingual.Segments[0]
    [void]$multilingual.Accept($multilingualSegment.Id, $multilingualSegment.Revision, 'fr', 'Japanese caption translation')
    Assert ($writer.RecordCompleted($multilingual, $now.AddSeconds(1)) -eq 1) 'completed caption accepts an explicit non-English target'
    $writer.Dispose()
    $content = [IO.File]::ReadAllText($file, [Text.Encoding]::UTF8)
    Assert ($content.Contains(([string][char]0x4F60 + [char]0x597D)) -and $content.Contains('Source: Completed.') -and $content.Contains('Translation: corrected translation')) 'UTF-8 transcript contains source and translation'
    Assert ([regex]::Matches($content, '(?m)^\[').Count -eq 5) 'shutdown drains sentence, correction, multilingual caption and both manual language pairs exactly once'
    Assert ($content -match '(?m)^\[.*\] #\d+ r\d+ fr->ja' -and $content.Contains('Translation: Japanese caption translation')) 'multilingual caption TXT metadata preserves actual target'
    Assert ($content.Contains('fr->ja') -and $content.Contains('Translation: Japanese translation')) 'multilingual transcript preserves explicit target rather than assuming English'
    Assert ($content.Contains('MANUAL ') -and $content.Contains('zh->en') -and $content.Contains('Translation: typed translation')) 'manual translation stored in same session file with distinct entry type'
    Assert ((Get-ChildItem -LiteralPath $folder -Filter '*.txt').Count -eq 1) 'one transcript file per recording session'
} finally {
    $writer.Dispose()
    if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
}
$finishFolder = Join-Path ([IO.Path]::GetTempPath()) ('LCET-finish-test-' + [Guid]::NewGuid().ToString('N'))
$kept = New-Object LCET.TranscriptWriter($finishFolder)
$discarded = New-Object LCET.TranscriptWriter($finishFolder)
$emptyRecord = New-Object LCET.TranscriptWriter($finishFolder)
try {
    [void]$kept.RecordManual([Guid]::NewGuid().ToString(), 'en', 'kept source', 'kept translation', $now)
    $keptPath = $kept.Finish($true)
    Assert ($keptPath -eq $kept.FilePath -and [IO.File]::ReadAllText($keptPath).Contains('kept translation')) 'saving session drains pending writes and returns actual TXT path for opening'
    [void]$discarded.RecordManual([Guid]::NewGuid().ToString(), 'en', 'discarded source', 'discarded translation', $now)
    [void]$discarded.Finish($false)
    Assert (-not (Test-Path -LiteralPath $discarded.FilePath) -and (Test-Path -LiteralPath $keptPath)) 'discarding session deletes only its own TXT and preserves other sessions'
    Assert (-not $discarded.RecordManual([Guid]::NewGuid().ToString(), 'en', 'late source', 'late translation', $now)) 'discarded session cannot queue late writes that recreate deleted TXT'
    [void]$discarded.Finish($false)
    Assert (-not (Test-Path -LiteralPath $discarded.FilePath)) 'repeated discard remains safe after writer stopped'
    Assert ($null -eq $emptyRecord.Finish($true) -and -not (Test-Path -LiteralPath $emptyRecord.FilePath)) 'empty session does not create an empty TXT just for exit prompt'
} finally {
    $kept.Dispose()
    $discarded.Dispose()
    $emptyRecord.Dispose()
    if (Test-Path -LiteralPath $finishFolder) { Remove-Item -LiteralPath $finishFolder -Recurse -Force }
}
$unfinished = New-Object LCET.CaptionTracker
[void]$unfinished.Observe('Still speaking', 'en', $now)
$unfinishedSegment = $unfinished.Segments[0]
[void]$unfinished.Accept($unfinishedSegment.Id, $unfinishedSegment.Revision, 'en', 'current full translation')
Assert (@($unfinished.Completed($now.AddSeconds(1))).Count -eq 0) 'unpunctuated speech not recorded during short pause'
Assert (@($unfinished.Completed($now.AddSeconds(2))).Count -eq 1) 'final unpunctuated speech records after longer pause'
$oldRevision = $unfinishedSegment.Revision
$prefix = $unfinishedSegment.Text
[void]$unfinished.Observe('Still speaking more', 'en', $now.AddSeconds(3))
[void]$unfinished.AcceptPreview($unfinishedSegment.Id, $oldRevision, 'en', $prefix, 'prefix preview')
Assert (@($unfinished.Completed($now.AddSeconds(6), $true)).Count -eq 0) 'prefix previews never recorded as completed full sentences'
$folder = Join-Path ([IO.Path]::GetTempPath()) ('LCET-transcript-queue-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($folder)
$blocked = Join-Path $folder 'blocked'
[IO.File]::WriteAllText($blocked, 'temporary directory obstruction')
$writer = New-Object LCET.TranscriptWriter($blocked)
$queued = New-Object LCET.CaptionTracker
try {
    [void]$queued.Observe('Blocked sentence 0.', 'en', $now)
    $segment = $queued.Segments[0]
    [void]$queued.Accept($segment.Id, $segment.Revision, 'en', 'translation 0')
    [void]$writer.RecordCompleted($queued, $now, $true)
    Assert ([Threading.SpinWait]::SpinUntil([Func[bool]]{ $null -ne $writer.LastError }, 3000)) 'write failure reported without blocking or crashing caption processing'
    $count = 0
    foreach ($number in 1..128) {
        [void]$queued.Observe('', 'en', $now)
        [void]$queued.Observe("Blocked sentence $number.", 'en', $now)
        $segment = $queued.Segments[$queued.Segments.Count - 1]
        [void]$queued.Accept($segment.Id, $segment.Revision, 'en', "translation $number")
        $count += $writer.RecordCompleted($queued, $now, $true)
    }
    Assert ($count -eq 128 -and $writer.PendingCount -eq 128) 'background recording queue bounded at 128 entries during filesystem failure'
    [void]$queued.Observe('', 'en', $now)
    [void]$queued.Observe('Blocked sentence 129.', 'en', $now)
    $segment = $queued.Segments[$queued.Segments.Count - 1]
    [void]$queued.Accept($segment.Id, $segment.Revision, 'en', 'translation 129')
    Assert ($writer.RecordCompleted($queued, $now, $true) -eq 0 -and $segment.RecordedRevision -eq 0) 'full queue returns without marking unwritten sentence as recorded'
    [IO.File]::Delete($blocked)
    [void][IO.Directory]::CreateDirectory($blocked)
    Assert ([Threading.SpinWait]::SpinUntil([Func[bool]]{ $writer.RecordCompleted($queued, $now, $true) -gt 0 }, 4000)) 'recording resumes after filesystem recovery and retries pending sentence'
    $writer.Dispose()
    $content = [IO.File]::ReadAllText($writer.FilePath)
    Assert ([regex]::Matches($content, '(?m)^\[').Count -eq 130 -and $null -eq $writer.LastError) 'all accepted sentences drained after recovery with no loss or duplication'
} finally {
    $writer.Dispose()
    if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
}