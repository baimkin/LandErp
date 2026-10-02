using System.Numerics;

namespace LandErp.Application.Modules.Catalog.Domain;

public readonly record struct PhotoFingerprintPair(int Distance, int Left, int Right);

public static class PhotoFingerprintMatching
{
    public static IReadOnlyList<PhotoFingerprintPair> Match(
        IReadOnlyList<long> left,
        IReadOnlyList<long> right,
        int maxHammingDistance,
        IReadOnlyDictionary<long, int>? commonCounts = null,
        int commonPhotoMaxListings = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxHammingDistance);
        if (left.Count == 0 || right.Count == 0) return [];

        List<PhotoFingerprintPair> candidates = [];
        for (int leftIndex = 0; leftIndex < left.Count; leftIndex++)
        {
            long hash = left[leftIndex];
            if (commonCounts != null
                && commonCounts.TryGetValue(hash, out int count)
                && count > commonPhotoMaxListings)
                continue;

            for (int rightIndex = 0; rightIndex < right.Count; rightIndex++)
            {
                int distance = HammingDistance(hash, right[rightIndex]);
                if (distance <= maxHammingDistance)
                    candidates.Add(new(distance, leftIndex, rightIndex));
            }
        }

        bool[] usedLeft = new bool[left.Count];
        bool[] usedRight = new bool[right.Count];
        List<PhotoFingerprintPair> selected = [];
        foreach (PhotoFingerprintPair pair in candidates.OrderBy(item => item.Distance))
        {
            if (usedLeft[pair.Left] || usedRight[pair.Right]) continue;
            usedLeft[pair.Left] = true;
            usedRight[pair.Right] = true;
            selected.Add(pair);
        }

        return selected;
    }

    public static int HammingDistance(long left, long right) =>
        BitOperations.PopCount(unchecked((ulong)(left ^ right)));
}
