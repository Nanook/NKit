using System;
using System.Collections.Generic;

namespace NKit.Ui.Helpers
{
    public static class EnumerableExtensions
    {
        public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action)
        {
            foreach (T e in enumerable)
            {
                action(e);
            }
        }
    }
}