using System;
using System.Collections.Generic;

namespace Nanook.NKit
{
    internal class SectionItems : List<ISectionItem>
    {
        public new Enumerator GetEnumerator() => base.GetEnumerator();




        /// <summary>
        /// Breaks the section up in to the different parts, file, and different gap types. Offsets are fsOffsets
        /// </summary>
        public void ProcessItems(Action<bool, ISectionData> process)
        {
            foreach (ISectionItem si in this)
            {
                if (si.File != null)
                    process(true, si.File);

                if (si.Gap != null)
                {
                    if (si.GapInfo?.Count != 0)
                    {
                        foreach (ISectionData gd in si.GapInfo)
                            process(false, gd);
                    }
                    else
                        process(false, si.Gap);
                }
            }

        }

        public IEnumerable<ISectionData> EnumSectionData()
        {
            foreach (ISectionItem si in this)
            {
                if (si.File != null)
                    yield return si.File;

                if (si.Gap != null)
                {
                    if (si.GapInfo?.Count != 0)
                    {
                        foreach (ISectionData gd in si.GapInfo)
                            yield return gd;
                    }
                    else
                        yield return si.Gap;
                }
            }
        }

    }
}