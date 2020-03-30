using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SendKeyAgent.App.Extensions
{
    public static class EnumerableExtensions
    {
        public static IEnumerable<T> RemoveAt<T>(this IEnumerable<T> values, int index)
        {
            var valueList = new List<T>(values);
            
            valueList.RemoveAt(index);

            return valueList;
        }
    }
}
