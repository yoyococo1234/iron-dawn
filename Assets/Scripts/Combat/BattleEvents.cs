using UnityEngine;

/// <summary>战斗事件总线：载具被击毁、资源点易主等，HUD 和 AI 都订阅这里。</summary>
public static class BattleEvents
{
    /// <summary>(被击毁的载具, 击杀者——可能是 null，比如自己淹死/撞毁)</summary>
    public static System.Action<VehicleCombat, VehicleCombat> VehicleDestroyed;
    public static System.Action<VehicleCombat> VehicleDamaged;
    public static System.Action<ZoneState> ZoneCaptured;

    public static void RaiseVehicleDestroyed(VehicleCombat victim, VehicleCombat killer = null)
        => VehicleDestroyed?.Invoke(victim, killer);
    public static void RaiseVehicleDamaged(VehicleCombat v) => VehicleDamaged?.Invoke(v);
    public static void RaiseZoneCaptured(ZoneState z) => ZoneCaptured?.Invoke(z);
}
