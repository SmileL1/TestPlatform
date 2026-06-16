using Microsoft.AspNetCore.SignalR;

namespace TestPlatform.API.Hubs;

public class TestHub : Hub
{
    public async Task JoinRun(string runId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"run_{runId}");

    public async Task LeaveRun(string runId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"run_{runId}");

    // ── 录制频道 ──────────────────────────────────────────────────
    public async Task JoinRecording()
        => await Groups.AddToGroupAsync(Context.ConnectionId, "recording");

    public async Task LeaveRecording()
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, "recording");
}
