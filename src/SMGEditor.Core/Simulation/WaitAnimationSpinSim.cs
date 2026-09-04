namespace SMGEditor.Core.Simulation;

public static class WaitAnimationSpinSim
{
    public static float? DegreesPerFrame(string internalName) => internalName switch
    {
        _ when string.Equals(internalName, "Coin", StringComparison.OrdinalIgnoreCase) => 8f,
        _ when string.Equals(internalName, "PurpleCoin", StringComparison.OrdinalIgnoreCase) => 8f,
        _ when string.Equals(internalName, "PowerStar", StringComparison.OrdinalIgnoreCase) => 3f,
        _ when string.Equals(internalName, "GrandStar", StringComparison.OrdinalIgnoreCase) => 2f,
        _ when string.Equals(internalName, "StarPiece", StringComparison.OrdinalIgnoreCase) => 1f,
        _ => null,
    };

    public static float? ComputeSpinDegrees(string internalName, float clockSeconds)
    {
        float? degPerFrame = DegreesPerFrame(internalName);
        return degPerFrame is { } d ? (clockSeconds * 60f * d) % 360f : null;
    }
}
