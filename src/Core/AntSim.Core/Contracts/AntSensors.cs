using System;

namespace AntSim.Core.Contracts;

/// <summary>
/// Entrada del cerebro: 19 canales normalizados (ver <see cref="AntSensorChannel"/>).
/// Struct blittable sin referencias: se pasa por <c>in</c> y se copia a un span
/// para alimentar la red sin asignaciones.
/// </summary>
public struct AntSensors
{
    public float FoodTrailCenter;
    public float HomeTrailCenter;
    public float AlarmCenter;
    public float FoodTrailDiff;
    public float HomeTrailDiff;
    public float AlarmDiff;
    public float FoodDx;
    public float FoodDy;
    public float FoodSize;
    public float HomeDx;
    public float HomeDy;
    public float ProxLeft;
    public float ProxFront;
    public float ProxRight;
    public float HasLoad;
    public float LoadFraction;
    public float Energy;
    public float AgeNormalized;
    public float ColonyFoodRatio;

    public static AntSensors Default()
    {
        AntSensors s = default;
        s.HasLoad = 0f;
        s.FoodSize = 0f;
        s.LoadFraction = 0f;
        s.Energy = 1f;
        s.AgeNormalized = 0f;
        return s;
    }

    /// <summary>Copia los canales en orden canónico a un span de al menos <see cref="AntSensorChannelInfo.Count"/>.</summary>
    public void CopyTo(Span<float> target)
    {
        target[0] = FoodTrailCenter;
        target[1] = HomeTrailCenter;
        target[2] = AlarmCenter;
        target[3] = FoodTrailDiff;
        target[4] = HomeTrailDiff;
        target[5] = AlarmDiff;
        target[6] = FoodDx;
        target[7] = FoodDy;
        target[8] = FoodSize;
        target[9] = HomeDx;
        target[10] = HomeDy;
        target[11] = ProxLeft;
        target[12] = ProxFront;
        target[13] = ProxRight;
        target[14] = HasLoad;
        target[15] = LoadFraction;
        target[16] = Energy;
        target[17] = AgeNormalized;
        target[18] = ColonyFoodRatio;
    }

    public readonly bool AllFinite()
    {
        return FloatUtil.IsFinite(FoodTrailCenter) && FloatUtil.IsFinite(HomeTrailCenter)
            && FloatUtil.IsFinite(AlarmCenter) && FloatUtil.IsFinite(FoodTrailDiff)
            && FloatUtil.IsFinite(HomeTrailDiff) && FloatUtil.IsFinite(AlarmDiff)
            && FloatUtil.IsFinite(FoodDx) && FloatUtil.IsFinite(FoodDy) && FloatUtil.IsFinite(FoodSize)
            && FloatUtil.IsFinite(HomeDx) && FloatUtil.IsFinite(HomeDy)
            && FloatUtil.IsFinite(ProxLeft) && FloatUtil.IsFinite(ProxFront) && FloatUtil.IsFinite(ProxRight)
            && FloatUtil.IsFinite(HasLoad) && FloatUtil.IsFinite(LoadFraction)
            && FloatUtil.IsFinite(Energy) && FloatUtil.IsFinite(AgeNormalized)
            && FloatUtil.IsFinite(ColonyFoodRatio);
    }
}
