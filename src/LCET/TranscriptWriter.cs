using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;

namespace LCET
{
    public sealed class TranscriptWriter : IDisposable
    {
        private sealed class Entry
        {
            public string Text;
            public string RequestId;
        }

        private readonly BlockingCollection<Entry> pending = new BlockingCollection<Entry>(128);
        private readonly ManualResetEvent stopping = new ManualResetEvent(false);
        private readonly ManualResetEvent flushRequested = new ManualResetEvent(false);
        private readonly Thread worker;
        private volatile string lastError;
        private int disposed;
        public event Action<string> ManualRecorded;

        public string FilePath { get; private set; }
        public string LastError { get { return lastError; } }
        public int PendingCount { get { return pending.Count; } }

        public TranscriptWriter(string directory)
        {
            FilePath = Path.Combine(Path.GetFullPath(directory), DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
                "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            worker = new Thread(Write) { IsBackground = true, Name = "LCET transcript writer" };
            worker.Start();
        }

        public int RecordCompleted(CaptionTracker tracker, DateTime now, bool flush = false)
        {
            int count = 0;
            foreach (Segment segment in tracker.Completed(now, flush))
            {
                string entry = string.Format(CultureInfo.InvariantCulture,
                    "[{0:o}] #{1} r{2} {3}->{4}\r\nSource: {5}\r\nTranslation: {6}\r\n\r\n",
                    now, segment.Id, segment.Revision, segment.Source, segment.Target ?? (segment.Source == "en" ? "zh" : "en"),
                    segment.Text, segment.Translation);
                if (!TryQueue(entry)) break;
                segment.RecordedRevision = segment.Revision;
                count++;
            }
            return count;
        }

        public bool RecordManual(string requestId, string source, string text, string translation, DateTime now)
        {
            return RecordManual(requestId, source, source == "en" ? "zh" : "en", text, translation, now);
        }

        public bool RecordManual(string requestId, string source, string target, string text, string translation, DateTime now)
        {
            if (!LanguageResources.IsLanguageTag(source) || !LanguageResources.IsLanguageTag(target) ||
                string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return false;
            string entry = string.Format(CultureInfo.InvariantCulture,
                "[{0:o}] MANUAL {1} {2}->{3}\r\nSource: {4}\r\nTranslation: {5}\r\n\r\n",
                now, requestId, source, target, text, translation);
            return TryQueue(entry, requestId);
        }

        private bool TryQueue(string text, string requestId = null)
        {
            if (Volatile.Read(ref disposed) != 0) return false;
            try
            {
                if (pending.TryAdd(new Entry { Text = text, RequestId = requestId })) return true;
                lastError = "queue_full";
            }
            catch (InvalidOperationException) { }
            return false;
        }

        private void Write()
        {
            while (!stopping.WaitOne(0))
            {
                flushRequested.WaitOne(4000);
                if (stopping.WaitOne(0)) return;
                var batch = new List<Entry>();
                Entry entry;
                while (batch.Count < 128 && pending.TryTake(out entry)) batch.Add(entry);
                if (batch.Count == 0)
                {
                    if (pending.IsCompleted) return;
                    continue;
                }
                var text = new StringBuilder();
                foreach (Entry item in batch) text.Append(item.Text);
                while (!stopping.WaitOne(0))
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                        File.AppendAllText(FilePath, text.ToString(), new UTF8Encoding(true));
                        lastError = null;
                        foreach (Entry item in batch)
                            if (item.RequestId != null) ManualRecorded?.Invoke(item.RequestId);
                        break;
                    }
                    catch (IOException exception) { lastError = exception.GetType().Name; }
                    catch (UnauthorizedAccessException exception) { lastError = exception.GetType().Name; }
                    catch (SecurityException exception) { lastError = exception.GetType().Name; }
                    if (stopping.WaitOne(1000)) return;
                }
                if (pending.IsCompleted) return;
            }
        }

        public string Finish(bool keep)
        {
            Dispose();
            if (!worker.Join(1500)) throw new IOException("transcript_writer_active");
            if (!keep)
            {
                File.Delete(FilePath);
                return null;
            }
            return File.Exists(FilePath) ? FilePath : null;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            pending.CompleteAdding();
            flushRequested.Set();
            if (!worker.Join(5000))
            {
                lastError = "flush_timeout";
                Diagnostics.Write("transcript", "flush_timeout");
                stopping.Set();
                return;
            }
            pending.Dispose();
            stopping.Dispose();
            flushRequested.Dispose();
        }
    }
}