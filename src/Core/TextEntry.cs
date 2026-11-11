using System;

namespace ProbablyEdited
{
    public sealed class TextEntry
    {
        public TextId Id { get; }
        public string SourceName { get; }
        public string SourceVersion { get; }
        public string TableName { get; }
        public string KeyName { get; }
        public string Category { get; }
        public string OriginalText { get; }
        public bool IsFallback { get; }
        public bool IsSmart { get; }
        public string Description { get; }

        public TextEntry(
            TextId id,
            string sourceName,
            string sourceVersion,
            string tableName,
            string keyName,
            string category,
            string originalText,
            bool isFallback,
            bool isSmart,
            string description)
        {
            if (!id.IsValid)
                throw new ArgumentException(UiText.Get("core.text_entry.invalid_id"), nameof(id));

            Id = id;
            SourceName = sourceName ?? string.Empty;
            SourceVersion = sourceVersion ?? string.Empty;
            TableName = tableName ?? string.Empty;
            KeyName = keyName ?? string.Empty;
            Category = category ?? string.Empty;
            OriginalText = originalText ?? string.Empty;
            IsFallback = isFallback;
            IsSmart = isSmart;
            Description = description ?? string.Empty;
        }
    }
}
