using System.Collections.Generic;
using System.Linq;

namespace DeckOrdering
{
    internal sealed class Permutation
    {
        internal readonly IReadOnlyList<int> Indexes;

        private Permutation(IReadOnlyList<int> indexes)
        {
            Indexes = indexes;
        }

        internal int Count => Indexes.Count;

        internal bool IsIdentity => Indexes.Select((index, slot) => index == slot).All(same => same);

        internal static Permutation Identity(int count)
        {
            return new Permutation(Enumerable.Range(0, count).ToList());
        }

        internal static Permutation From(IReadOnlyList<int?> indexes)
        {
            return indexes.All(i => i.HasValue)
            && indexes.Select(i => i.Value).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, indexes.Count))
                ? new Permutation(indexes.Select(i => i.Value).ToList())
                : null;
        }

        internal Permutation Swapped(int a, int b)
        {
            return new Permutation(Indexes.Select((index, slot) => slot == a ? Indexes[b] : slot == b ? Indexes[a] : index).ToList());
        }

        internal IReadOnlyList<T> ApplyTo<T>(IReadOnlyList<T> source)
        {
            return Indexes.Select(i => source[i]).ToList();
        }
    }
}
