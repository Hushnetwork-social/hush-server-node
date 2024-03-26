using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HushServerNode.Blockchain;

public static class ListExtensions
{
    public static IList<T> TakeAndRemove<T>(this ConcurrentBag<T> bag, int count)
    {
        var elementsToTake = count;

        if (elementsToTake > bag.Count)
        {
            elementsToTake = bag.Count;
        }

        var takenElements = new List<T>();

        for (int i = 0; i < elementsToTake; i++)
        {
            if (bag.TryTake(out T item))
            {
                takenElements.Add(item);
            }
        }

        return takenElements;
    }
}
