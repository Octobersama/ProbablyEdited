using System;

namespace ProbablyEdited
{
    internal enum TextHistoryCommand
    {
        None,
        Undo,
        Redo
    }

    internal static class TextHistoryShortcut
    {
        public static TextHistoryCommand Match(bool macOS, bool control, bool command,
            bool shift, bool alt, bool zPressed, bool yPressed, bool composing)
        {
            if (composing || alt || !(macOS ? command : control))
                return TextHistoryCommand.None;
            if (zPressed)
                return shift ? TextHistoryCommand.Redo : TextHistoryCommand.Undo;
            return !macOS && yPressed && !shift ? TextHistoryCommand.Redo : TextHistoryCommand.None;
        }

        public static bool BlocksCharacter(TextHistoryCommand command, char character)
        {
            return command != TextHistoryCommand.None &&
                (character == 'z' || character == 'Z' || character == 'y' || character == 'Y'
                    || character == '\u001a' || character == '\u0019');
        }
    }
}
