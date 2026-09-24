using Nanook.NKit;

namespace NKit.Ui.Helpers
{
    public static class VerifyHelper
    {
        public static Verify GetVerifyEnumFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Verify.Y;

            return text.StartsWith("Y") ? Verify.Y :
                   text.StartsWith("N") ? Verify.N :
                   text.StartsWith("D") ? Verify.DatLookup :
                   Verify.Y;
        }

        public static (bool, bool, bool) GetEnabledVerificationOptions(TaskType taskType)
        {
            bool isDynamicVerifyEnabled = true;
            bool isDatLookupVerifyEnabled = true;
            bool isNoneVerifyEnabled = false;

            switch (taskType)
            {
                case TaskType.Convert:
                case TaskType.Scan:
                    isDynamicVerifyEnabled = true;
                    isDatLookupVerifyEnabled = true;
                    isNoneVerifyEnabled = true;
                    break;
                case TaskType.Expand:
                case TaskType.Fix:
                case TaskType.FixExtract:
                case TaskType.Dedupe:
                case TaskType.Extract:
                case TaskType.Verify:
                default:
                    isDynamicVerifyEnabled = true;
                    isDatLookupVerifyEnabled = true;
                    isNoneVerifyEnabled = false;
                    break;
            }

            return (isDynamicVerifyEnabled, isDatLookupVerifyEnabled, isNoneVerifyEnabled);
        }
    }
}