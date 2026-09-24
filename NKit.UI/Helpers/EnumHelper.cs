using ReactiveUI.Primitives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NKit.Ui.Extensions
{
    public static class EnumHelper
    {
        public static Dictionary<int, string> ToDictionary<T>() where T : Enum
        {
            return Enum.GetValuesAsUnderlyingType(typeof(T))
                   .Cast<T>()
                   .ToDictionary(t => (int)(object)t, t => t.ToString());
        }

        public static ObservableCollection<T> ToObservableCollection<T>() where T : Enum => new ObservableCollection<T>(Enum.GetValuesAsUnderlyingType(typeof(T)).Cast<T>());
    }
}