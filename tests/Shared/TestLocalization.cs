using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using ProbablyEdited;

internal static class TestLocalization
{
    public static void Initialize()
    {
        Dictionary<string, string> english = ReadCsv(Path.Combine(AppContext.BaseDirectory, "Localization", "en.csv"));
        UiText.Configure(key => english.TryGetValue(key, out string value) ? value : key, CultureInfo.GetCultureInfo("en"));
    }

    public static Dictionary<string, string> ReadCsv(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var parser = new TextFieldParser(path))
        {
            parser.TextFieldType = FieldType.Delimited;
            parser.SetDelimiters(",");
            parser.HasFieldsEnclosedInQuotes = true;
            parser.TrimWhiteSpace = false;
            while (!parser.EndOfData)
            {
                string[] fields = parser.ReadFields();
                if (fields == null || fields.Length != 2 || string.IsNullOrEmpty(fields[0]))
                    throw new InvalidDataException("Localization records must have a key and one text field: " + path);
                if (!result.TryAdd(fields[0], fields[1].Replace("\\n", "\n")))
                    throw new InvalidDataException("Duplicate localization key: " + fields[0]);
            }
        }
        return result;
    }
}
