using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LeaderboardBlock
{
    internal sealed class BlockList
    {
        private const string Header = "LeaderboardBlock:1";
        private readonly string path;
        private HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

        internal BlockList(string path) { this.path = path; }
        internal int Count { get { return ids.Count; } }

        internal void Load()
        {
            if (File.Exists(path)) ids = Read(path);
        }

        internal bool Contains(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && ids.Contains(Key(id));
        }

        // Commit to disk first. A failed save must not appear to be a successful block.
        internal void Set(string id, bool blocked)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Missing player ID.");
            var next = new HashSet<string>(ids, StringComparer.Ordinal);
            if (blocked) next.Add(Key(id)); else next.Remove(Key(id));
            var sorted = new List<string>(next);
            sorted.Sort(StringComparer.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(Header + "\n" + string.Join("\n", sorted) + "\n");
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                ids = next;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static HashSet<string> Read(string file)
        {
            string[] lines = File.ReadAllLines(file);
            if (lines.Length == 0 || lines[0] != Header)
                throw new InvalidDataException("Unrecognized block list. The original file was left untouched.");
            var result = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                if (line.Length != 64) throw new InvalidDataException("Invalid block list entry.");
                foreach (char c in line)
                    if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f'))
                        throw new InvalidDataException("Invalid block list entry.");
                result.Add(line);
            }
            return result;
        }

        private static string Key(string id)
        {
            using (var hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(id));
                var text = new StringBuilder(64);
                foreach (byte value in bytes) text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }
    }
}
