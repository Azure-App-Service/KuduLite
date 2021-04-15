using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Kudu.Core.Helpers
{
    public static class StringHelper
    {
        /// <summary>
        /// The storage key trim padding.
        /// </summary>
        public const int StorageKeyTrimPadding = 17;

        /// <summary>
        /// The escaped storage keys.
        /// </summary>
        private static readonly string[] EscapedStorageKeys = new string[128]
        {
            ":00", ":01", ":02", ":03", ":04", ":05", ":06", ":07", ":08", ":09", ":0A", ":0B", ":0C", ":0D", ":0E", ":0F",
            ":10", ":11", ":12", ":13", ":14", ":15", ":16", ":17", ":18", ":19", ":1A", ":1B", ":1C", ":1D", ":1E", ":1F",
            ":20", ":21", ":22", ":23", ":24", ":25", ":26", ":27", ":28", ":29", ":2A", ":2B", ":2C", ":2D", ":2E", ":2F",
            "0",   "1",   "2",   "3",   "4",   "5",   "6",   "7",   "8",   "9", ":3A", ":3B", ":3C", ":3D", ":3E", ":3F",
            ":40",   "A",   "B",   "C",   "D",   "E",   "F",   "G",   "H",   "I",   "J",   "K",   "L",   "M",   "N",   "O",
            "P",   "Q",   "R",   "S",   "T",   "U",   "V",   "W",   "X",   "Y",   "Z", ":5B", ":5C", ":5D", ":5E", ":5F",
            ":60",   "a",   "b",   "c",   "d",   "e",   "f",   "g",   "h",   "i",   "j",   "k",   "l",   "m",   "n",   "o",
            "p",   "q",   "r",   "s",   "t",   "u",   "v",   "w",   "x",   "y",   "z", ":7B", ":7C", ":7D", ":7E", ":7F",
        };

        /// <summary>
        /// Escapes the and trim storage key prefix.
        /// </summary>
        /// <param name="storageKeyPrefix">The storage key prefix.</param>
        /// <param name="limit">The storage key limit.</param>
        public static string EscapeAndTrimStorageKeyPrefix(string storageKeyPrefix, int limit)
        {
            return StringHelper.TrimStorageKeyPrefix(StringHelper.EscapeStorageKey(storageKeyPrefix), limit);
        }

        /// <summary>
        /// Trims the storage key prefix.
        /// </summary>
        /// <param name="storageKeyPrefix">The storage key prefix.</param>
        /// <param name="limit">The storage key limit.</param>
        private static string TrimStorageKeyPrefix(string storageKeyPrefix, int limit)
        {
            if (limit < StringHelper.StorageKeyTrimPadding)
            {
                throw new ArgumentException(message: $"The storage key limit should be at least {StringHelper.StorageKeyTrimPadding} characters.", paramName: nameof(limit));
            }

            return storageKeyPrefix.Length > limit - StringHelper.StorageKeyTrimPadding
                ? storageKeyPrefix.Substring(0, limit - StringHelper.StorageKeyTrimPadding)
                : storageKeyPrefix;
        }

        /// <summary>
        /// Escapes the storage key.
        /// </summary>
        /// <param name="storageKey">The storage key.</param>
        public static string EscapeStorageKey(string storageKey)
        {
            var sb = new StringBuilder(storageKey.Length);
            for (var index = 0; index < storageKey.Length; ++index)
            {
                var c = storageKey[index];
                if (c < 128)
                {
                    sb.Append(StringHelper.EscapedStorageKeys[c]);
                }
                else if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else if (c < 0x100)
                {
                    sb.Append(':');
                    sb.Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(':');
                    sb.Append(':');
                    sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets the name of the workflow job trigger queue.
        /// </summary>
        /// <param name="queueIndex">Index of the queue.</param>
        internal static string GetWorkflowQueueNameInternal(string prefix, int numPartitionsInJobTriggersQueue)
        {
            return string.Concat(prefix, (1 % numPartitionsInJobTriggersQueue).ToString("d2", CultureInfo.InvariantCulture));
        }
    }
}
