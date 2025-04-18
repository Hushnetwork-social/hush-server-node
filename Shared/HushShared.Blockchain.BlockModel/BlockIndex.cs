using System.Text.Json.Serialization;
using HushShared.Blockchain.BlockModel.Converters;

namespace HushShared.Blockchain.BlockModel;

[JsonConverter(typeof(BlockIndexConverter))]
public record BlockIndex(long Value) : IComparable<BlockIndex>
{
    public static BlockIndex Empty { get; } = new(-1);

    public override string ToString() => Value.ToString();

    // --- IComparable<BlockIndex> Implementation ---
    public int CompareTo(BlockIndex? other)
    {
        // Standard comparison: null is considered less than any instance.
        if (other is null)
        {
            return 1; // This instance is greater than null
        }

        // Compare based on the underlying Value property
        return this.Value.CompareTo(other.Value);
    }

    // --- Operator Overloads ---

    // Less than (<)
    public static bool operator <(BlockIndex? left, BlockIndex? right)
    {
        // If left is null, it's less than right (unless right is also null)
        if (left is null)
        {
            return right is not null;
        }
        // Otherwise, use the CompareTo method
        return left.CompareTo(right) < 0;
    }

    // Greater than (>)
    public static bool operator >(BlockIndex? left, BlockIndex? right)
    {
        // If right is null, left is greater (unless left is also null)
        if (right is null)
        {
           return left is not null;
        }
        // Otherwise, use the CompareTo method (inverted logic from <)
        // Or simply rely on CompareTo directly
        return left is not null && left.CompareTo(right) > 0;
        // Alternative: return Comparer<BlockIndex>.Default.Compare(left, right) > 0; (handles nulls nicely)
    }

    // Less than or equal to (<=)
    public static bool operator <=(BlockIndex? left, BlockIndex? right)
    {
        // If left is null, it's less than or equal to right
        if (left is null)
        {
            return true;
        }
        // Otherwise, use the CompareTo method
        return left.CompareTo(right) <= 0;
        // Alternative: return Comparer<BlockIndex>.Default.Compare(left, right) <= 0;
    }

    // Greater than or equal to (>=)
    public static bool operator >=(BlockIndex? left, BlockIndex? right)
    {
        // If right is null, left is greater or equal (unless left is also null)
         if (right is null)
        {
           return left is null ? true : true; // null >= null is true, non-null >= null is true
        }
        // Otherwise, use the CompareTo method
        return left is not null && left.CompareTo(right) >= 0;
        // Alternative: return Comparer<BlockIndex>.Default.Compare(left, right) >= 0;
    }
}
