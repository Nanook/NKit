using System;
using System.Collections.Generic;

namespace Nanook.NKit
{
    internal class OrderedList<T> : List<T>
    {
        private Func<T, long> _key;
        private bool _descending;

        public OrderedList(Func<T, long> getKey, bool descending) : base()
        {
            _key = getKey;
            _descending = descending;
        }

        /// <summary>
        /// Insert the item sorted by the key
        /// </summary>
        /// <param name="item">item to insert</param>
        /// <param name="existed">Returns true if the item existed and will not </param>
        /// <returns></returns>
        public int InsertIfMissing(T item, out bool existed)
        {
            int index = KeyIndex(_key(item), out existed);
            if (!existed)
                this.Insert(index, item);
            return index;
        }

        /// <summary>
        /// Finds the item or the index to insert it add
        /// </summary>
        /// <param name="key"></param>
        /// <param name="existed"></param>
        /// <returns></returns>
        public int KeyIndex(long key, out bool existed)
        {
            int min = 0;
            int max = this.Count;
            existed = false;
            if (max == 0)
                return 0;

            long k = 0;
            while (true) //binary chop search
            {
                if (max == min)
                {
                    existed = key == k;
                    return max;
                }

                int index = min + ((max - min) >> 1);
                k = _key(this[index]);
                long result = _descending ? k - key : key - k;
                if (result < 0)
                    max = index - (max != index ? 0 : 1);
                else if (result > 0)
                    min = index + (min != index ? 0 : 1);
                else
                {
                    existed = true;
                    return index;
                }
            }
        }

    }
}