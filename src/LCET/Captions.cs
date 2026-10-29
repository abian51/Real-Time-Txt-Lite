using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LCET
{
    public sealed class Segment
    {
        public long Id { get; set; }
        public int Revision { get; set; }
        public string Text { get; set; }
        public string Translation { get; set; }
        public string Source { get; set; }
        public string Target { get; set; }
        public bool Stable { get; set; }
        public DateTime Changed { get; set; }
        public DateTime Created { get; set; }
        public DateTime LastSubmitted { get; set; }
        public int TranslationRevision { get; set; }
        public bool Complete { get; set; }
        public int RecordedRevision { get; set; }
    }

    public sealed class CaptionTracker
    {
        private readonly List<Segment> segments = new List<Segment>();
        private string previous = "";
        private string pending = "";
        private Segment current;
        private long nextId;
        public string LastChange { get; private set; }
        public int DisplayLimit { get; set; } = 3;
        public IList<Segment> Segments { get { return segments.AsReadOnly(); } }
        public IEnumerable<Segment> DisplaySegments
        {
            get
            {
                var visible = new List<Segment>();
                int end = previous.Length;
                int limit = Math.Max(2, Math.Min(6, DisplayLimit));
                for (int index = segments.Count - 1; index >= 0 && end > 0 && visible.Count < limit; index--)
                {
                    Segment segment = segments[index];
                    int match = previous.LastIndexOf(segment.Text, end - 1, end, StringComparison.Ordinal);
                    if (match < 0 && ReferenceEquals(segment, current) && segment.Text.EndsWith(previous, StringComparison.Ordinal))
                        match = 0;
                    if (match < 0) continue;
                    visible.Add(segment);
                    end = match;
                }
                visible.Reverse();
                return visible;
            }
        }

        public static string Normalize(string text)
        {
            var builder = new StringBuilder();
            foreach (char character in text ?? "")
            {
                if (character == '\r' || character == '\n' || character == '\t') builder.Append(' ');
                else if (!char.IsControl(character)) builder.Append(character);
            }
            return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
        }

        private static int Overlap(string older, string newer)
        {
            int limit = Math.Min(older.Length, newer.Length);
            for (int length = limit; length >= 2; length--)
                if (string.CompareOrdinal(older, older.Length - length, newer, 0, length) == 0)
                    return length;
            return 0;
        }

        private static int Boundary(string text)
        {
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                if (character == '\u3002' || character == '\uff01' || character == '\uff1f' || character == '\uff0c' ||
                    (character == ',' && !(index > 0 && index + 1 < text.Length && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]))) ||
                    character == '!' || character == '?' ||
                    (character == '.' && (index + 1 == text.Length || char.IsWhiteSpace(text[index + 1]))))
                    return index + 1;
            }
            if (text.Length > 220)
            {
                int space = text.LastIndexOf(' ', 220);
                return space > 100 ? space + 1 : 220;
            }
            return 0;
        }

        public bool Observe(string raw, string source, DateTime now)
        {
            return Observe(raw, source, source == "en" ? "zh" : "en", now);
        }

        public bool Observe(string raw, string source, string target, DateTime now)
        {
            string text = Normalize(raw);
            if (text == previous) { LastChange = "unchanged"; return false; }
            if (text.Length == 0)
            {
                previous = "";
                pending = "";
                if (current != null) { current.Stable = true; current.Complete = true; }
                current = null;
                LastChange = "reset";
                return true;
            }
            if (previous.Length == 0)
            {
                pending = text;
                LastChange = "initial";
            }
            else if (text.StartsWith(previous, StringComparison.Ordinal))
            {
                pending += text.Substring(previous.Length);
                LastChange = "append";
            }
            else
            {
                int overlap = Overlap(previous, text);
                if (overlap > 0)
                {
                    pending += text.Substring(overlap);
                    LastChange = "scroll";
                }
                else
                {
                    int common = 0;
                    while (common < Math.Min(text.Length, previous.Length) && text[common] == previous[common]) common++;
                    Segment last = segments.LastOrDefault();
                    if (current == null && last != null && common > 0 &&
                        previous.EndsWith(last.Text, StringComparison.Ordinal) &&
                        common >= previous.Length - last.Text.Length)
                    {
                        current = last;
                        pending = last.Text;
                    }
                    int committedLength = Math.Max(0, previous.Length - pending.Length);
                    if (common >= committedLength && (current != null || common > 0))
                    {
                        pending = text.Substring(Math.Min(committedLength, text.Length));
                        LastChange = "revision";
                    }
                    else
                    {
                        if (current != null) { current.Stable = true; current.Complete = true; }
                        current = null;
                        pending = text;
                        LastChange = "reset";
                    }
                }
            }
            previous = text;
            while (pending.Length > 0)
            {
                int boundary = Boundary(pending);
                string piece = boundary > 0 ? pending.Substring(0, boundary).Trim() : pending.Trim();
                if (piece.Length == 0) break;
                if (current == null)
                {
                    current = new Segment { Id = ++nextId, Revision = 1, Text = piece, Source = source, Target = target, Changed = now, Created = now };
                    segments.Add(current);
                }
                else if (current.Text != piece)
                {
                    if (!piece.StartsWith(current.Text, StringComparison.Ordinal)) current.Translation = null;
                    current.Text = piece;
                    current.Revision++;
                    current.Changed = now;
                }
                current.Stable = boundary > 0;
                current.Complete = boundary > 0;
                if (boundary == 0) break;
                pending = pending.Substring(boundary).TrimStart();
                current = null;
            }
            while (segments.Count > 80) segments.RemoveAt(0);
            return true;
        }

        public IEnumerable<Segment> Ready(DateTime now, int debounceMs)
        {
            foreach (Segment segment in segments)
            {
                if (segment.TranslationRevision == segment.Revision && !string.IsNullOrWhiteSpace(segment.Translation)) continue;
                bool settled = (now - segment.Changed).TotalMilliseconds >= debounceMs;
                bool previewDue = (now - segment.Created).TotalMilliseconds >= 1500 &&
                    (now - segment.LastSubmitted).TotalMilliseconds >= 1500;
                if (!settled && !previewDue) continue;
                if (settled) segment.Stable = true;
                yield return segment;
            }
        }

        public bool Accept(long id, int revision, string source, string translation)
        {
            Segment segment = segments.FirstOrDefault(item => item.Id == id);
            if (segment == null || segment.Revision != revision || segment.Source != source) return false;
            segment.Translation = translation;
            segment.TranslationRevision = revision;
            return true;
        }

        public bool AcceptPreview(long id, int revision, string source, string requestedText, string translation)
        {
            Segment segment = segments.FirstOrDefault(item => item.Id == id);
            if (segment == null || segment.Source != source || revision > segment.Revision ||
                revision < segment.TranslationRevision || string.IsNullOrEmpty(requestedText) ||
                !segment.Text.StartsWith(requestedText, StringComparison.Ordinal)) return false;
            segment.Translation = translation;
            segment.TranslationRevision = revision;
            return true;
        }

        public IEnumerable<Segment> Completed(DateTime now, bool flush = false)
        {
            return segments.Where(segment => segment.RecordedRevision < segment.Revision &&
                segment.TranslationRevision == segment.Revision && !string.IsNullOrWhiteSpace(segment.Translation) &&
                (flush || ((now - segment.Changed).TotalMilliseconds >= 1000 &&
                    (segment.Complete || (now - segment.Changed).TotalMilliseconds >= 2000))));
        }

        public long[] TrimHistory(int maximum, bool preservePending = false)
        {
            var removed = new List<long>();
            while (segments.Count > maximum)
            {
                int index = preservePending ? segments.FindIndex(0, segments.Count - maximum,
                    segment => segment.TranslationRevision == segment.Revision && !string.IsNullOrWhiteSpace(segment.Translation)) : 0;
                if (index < 0) break;
                removed.Add(segments[index].Id);
                segments.RemoveAt(index);
            }
            return removed.ToArray();
        }

        public void Clear()
        {
            segments.Clear();
            current = null;
            pending = "";
            previous = "";
        }
    }
}