using BattleShip.Network;

namespace BattleShip.Forms;

/// <summary>Logic nhỏ của màn đặt tàu: tàu nào đặt tiếp theo và chữ hiển thị trên danh sách tàu. Không dùng giao diện.</summary>
public static class PlacementPlan
{
    /// <summary>Tàu đầu tiên (theo thứ tự trong hạm đội) chưa được đặt, hoặc null nếu đã đặt đủ.</summary>
    public static ShipSpec? NextUnplaced(IEnumerable<ShipPlacement> placed)
    {
        var names = placed.Select(p => p.Name).ToHashSet();
        return Fleet.Ships.FirstOrDefault(s => !names.Contains(s.Name));
    }

    /// <summary>Chữ trên nút của một tàu, ví dụ "Tàu sân bay (5 ô)"; tàu đã đặt có dấu ✓ đứng trước.</summary>
    public static string Label(ShipSpec spec, bool placed) =>
        $"{(placed ? "✓  " : "")}{spec.DisplayName} ({spec.Length} ô)";
}
