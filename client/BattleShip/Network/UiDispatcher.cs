namespace BattleShip.Network;

/// <summary>
/// Đưa việc bắn event về luồng giao diện (tránh lỗi cross-thread của WinForms).
/// Truyền <c>SynchronizationContext.Current</c> lấy trên luồng UI khi tạo client.
/// Nếu truyền null thì event được bắn trực tiếp trên luồng đang chạy (dùng cho unit test).
/// Bản NetworkClient thật của Nam cũng nên dùng lớp này.
/// </summary>
public sealed class UiDispatcher
{
    private readonly SynchronizationContext? _context;

    public UiDispatcher(SynchronizationContext? context)
    {
        _context = context;
    }

    public void Raise(Action action)
    {
        if (_context is null)
            action();
        else
            _context.Post(static state => ((Action)state!)(), action);
    }
}
