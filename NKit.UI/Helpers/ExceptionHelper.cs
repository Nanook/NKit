using System;
using System.Text;

namespace NKit.Ui.Helpers
{
    public static class ExceptionHelper
    {
        public static string GetFullExceptionDetail(Exception ex)
        {
            StringBuilder sb = new StringBuilder();

            int level = 0;
            do
            {
                sb.AppendLine($"############ Exception Details - Level {level} ############");
                sb.AppendLine(ex.Message);
                sb.AppendLine(ex.StackTrace);
                ex = ex.InnerException;
                level++;
            }
            while (ex != null);

            return sb.ToString();
        }
    }
}