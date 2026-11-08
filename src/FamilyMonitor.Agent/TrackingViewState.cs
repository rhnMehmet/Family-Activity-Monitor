using FamilyMonitor.Core;

namespace FamilyMonitor.Agent;

internal sealed record TrackingViewState(string TrackingText, string UserText, string IdleText, string ThresholdText,
    string CurrentApplicationText, string TrayText)
{
    public static TrackingViewState From(ActivityTracker tracker)
    {
        var tracking = !tracker.IsEnabled ? "Kapalı" : tracker.IsPaused ? "Duraklatıldı" : "Açık";
        var user = tracker.IsSuspended ? "Uykuda" : tracker.IsSessionLocked ? "Kilitli" : tracker.State switch
        {
            TrackingState.Active => "Aktif", TrackingState.Idle => "Hareketsiz",
            _ => "Ölçülmüyor"
        };
        var idle = tracker.State is TrackingState.Active or TrackingState.Idle or TrackingState.Unavailable
            ? $"Son fare/klavye girdisinden beri: {Math.Floor(tracker.LastObservedIdleTime.TotalSeconds):0} saniye"
            : "Son girdi süresi: ölçülmüyor";
        return new($"İzleme: {tracking}", $"Kullanıcı durumu: {user}", idle,
            $"Hareketsizlik eşiği: {tracker.IdleThreshold.TotalSeconds:0} saniye",
            $"Şu anda aktif olan uygulama: {tracker.CurrentApplication ?? "—"}",
            $"Family Activity Monitor — {tracking} / {user}");
    }
}
