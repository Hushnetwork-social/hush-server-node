using System.Collections.Concurrent;

namespace HushNode.MemPool;

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
            if (bag.TryTake(out T? item) && item is not null)
            {
                takenElements.Add(item);
            }
        }

        return takenElements;
    }
}
