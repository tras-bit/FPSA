using System;
using System.Collections.Generic;
using System.Text;

namespace Kordon.Core.Serialization
{
    /// <summary>
    /// Packs an ordered list of short strings into a single wire string.
    ///
    /// Netcode for GameObjects can serialize a <c>string</c> in an RPC but not a
    /// <c>string[]</c>: the ILPP code generator rejects the array with
    /// "Don't know how to serialize System.String[]", and the build fails. The
    /// scoreboard needs a name per row, so the names travel as one string with a
    /// length prefix per entry:
    ///
    /// <code>7:Sergei9:Kovalenko</code>
    ///
    /// Length prefixed on purpose - a name may contain any character at all,
    /// including the separator, a colon, a newline or a quote, without breaking the
    /// frame. Decoding tolerates a truncated or corrupt payload by returning the
    /// entries it could read, padded with empty names, because a scoreboard is
    /// never allowed to throw on a client.
    /// </summary>
    public static class NameListWire
    {
        private const char Separator = ':';

        /// <summary>Longest name accepted from the wire, in UTF-16 chars.</summary>
        public const int MaxNameLength = 32;

        /// <summary>Most names accepted from one payload (20 slots plus headroom).</summary>
        public const int MaxNames = 64;

        public static string Encode(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(names.Count * 12);
            int written = 0;
            for (int i = 0; i < names.Count && written < MaxNames; i++, written++)
            {
                string name = Clip(names[i]);
                builder.Append(name.Length).Append(Separator).Append(name);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Decodes the payload into exactly <paramref name="count"/> names.  Missing
        /// entries become empty strings, extra entries are dropped.
        /// </summary>
        public static string[] Decode(string payload, int count)
        {
            if (count <= 0)
            {
                return Array.Empty<string>();
            }

            string[] names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = string.Empty;
            }

            if (string.IsNullOrEmpty(payload))
            {
                return names;
            }

            int index = 0;
            int position = 0;
            while (index < count && position < payload.Length)
            {
                int separator = payload.IndexOf(Separator, position);
                if (separator < 0)
                {
                    break;
                }

                if (!int.TryParse(payload.Substring(position, separator - position),
                        out int length) || length < 0 || length > MaxNameLength)
                {
                    break;
                }

                int start = separator + 1;
                if (start + length > payload.Length)
                {
                    break;
                }

                names[index++] = payload.Substring(start, length);
                position = start + length;
            }

            return names;
        }

        /// <summary>
        /// Names come from a client's text field: strip control characters, collapse
        /// whitespace and cap the length so one operator cannot pad the scoreboard
        /// packet or break the frame.
        /// </summary>
        public static string Clip(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(Math.Min(name.Length, MaxNameLength));
            bool lastWasSpace = false;
            for (int i = 0; i < name.Length && builder.Length < MaxNameLength; i++)
            {
                char character = name[i];
                if (char.IsControl(character))
                {
                    continue;
                }

                if (char.IsWhiteSpace(character))
                {
                    if (lastWasSpace || builder.Length == 0)
                    {
                        continue;
                    }

                    builder.Append(' ');
                    lastWasSpace = true;
                    continue;
                }

                builder.Append(character);
                lastWasSpace = false;
            }

            return builder.ToString().TrimEnd();
        }
    }
}
