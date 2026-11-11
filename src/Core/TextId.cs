using System;

namespace ProbablyEdited
{
    public readonly struct TextId : IEquatable<TextId>
    {
        public string SourceId { get; }
        public string Locale { get; }
        public string TableId { get; }
        public string Key { get; }

        public TextId(string sourceId, string locale, string tableId, string key)
        {
            SourceId = RequirePart(sourceId, nameof(sourceId));
            Locale = RequirePart(locale, nameof(locale));
            TableId = RequirePart(tableId, nameof(tableId));
            Key = RequirePart(key, nameof(key));
        }

        private static string RequirePart(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(UiText.Get("core.text_id.field_empty"), parameterName);

            return value;
        }


        internal bool IsValid =>
            !string.IsNullOrWhiteSpace(SourceId) &&
            !string.IsNullOrWhiteSpace(Locale) &&
            !string.IsNullOrWhiteSpace(TableId) &&
            !string.IsNullOrWhiteSpace(Key);

        public bool Equals(TextId other) =>
            StringComparer.Ordinal.Equals(SourceId, other.SourceId) &&
            StringComparer.Ordinal.Equals(Locale, other.Locale) &&
            StringComparer.Ordinal.Equals(TableId, other.TableId) &&
            StringComparer.Ordinal.Equals(Key, other.Key);

        public override bool Equals(object obj) => obj is TextId other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + HashPart(SourceId);
                hash = hash * 31 + HashPart(Locale);
                hash = hash * 31 + HashPart(TableId);
                hash = hash * 31 + HashPart(Key);
                return hash;
            }
        }

        public static bool operator ==(TextId left, TextId right) => left.Equals(right);

        public static bool operator !=(TextId left, TextId right) => !left.Equals(right);

        private static int HashPart(string value) =>
            value == null ? 0 : StringComparer.Ordinal.GetHashCode(value);
    }
}
