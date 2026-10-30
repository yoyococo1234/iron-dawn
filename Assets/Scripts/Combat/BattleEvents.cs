using UnityEngine;

/// <summary>战斗事件总线：载具被击毁、资源点易主等，HUD 和 AI 都订阅这里。</summary>
public static class BattleEvents
{
    public static System.Action<VehicleCombat> VehicleDestroyed;
    public static System.Action<VehicleCombat> VehicleDamaged;

    public static void RaiseVehicleDestroyed(VehicleCombat v) => VehicleDestroyed?.Invoke(v);
    public static void RaiseVehicleDamaged(VehicleCombat v) => VehicleDamaged?.Invoke(v);
}
