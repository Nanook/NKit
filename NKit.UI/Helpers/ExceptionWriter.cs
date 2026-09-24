using System;
using System.IO;

namespace NKit.Ui.Helpers
{
    internal static class ExceptionWriter
    {
        public static void WriteExceptionToFile(Exception ex, string filePath)
        {
            string exceptionDetails = ExceptionHelper.GetFullExceptionDetail(ex);

            using (StreamWriter writer = new StreamWriter(filePath, true))
            {
                writer.WriteLine(exceptionDetails);
            }
        }
    }
}