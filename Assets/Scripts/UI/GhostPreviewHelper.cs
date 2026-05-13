namespace BCIKeyboardXR.UI
{
    public static class GhostPreviewHelper
    {
        public static string PreviewWithWord(string committed, string currentPartialWord, string candidateWord)
        {
            string baseText = RemovePartial(committed, currentPartialWord).TrimEnd();
            string word = (candidateWord ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(word))
                return baseText;

            return string.IsNullOrEmpty(baseText) ? word + " " : baseText + " " + word + " ";
        }

        public static string PreviewWithPhrase(string committed, string currentPartialWord, string candidatePhrase)
        {
            string baseText = RemovePartial(committed, currentPartialWord).TrimEnd();
            string phrase = (candidatePhrase ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(phrase))
                return baseText;

            return string.IsNullOrEmpty(baseText) ? phrase + " " : baseText + " " + phrase + " ";
        }

        private static string RemovePartial(string committed, string currentPartialWord)
        {
            committed ??= string.Empty;
            currentPartialWord ??= string.Empty;

            if (currentPartialWord.Length == 0 || committed.Length < currentPartialWord.Length)
                return committed;

            return committed.EndsWith(currentPartialWord)
                ? committed.Substring(0, committed.Length - currentPartialWord.Length)
                : committed;
        }
    }
}
