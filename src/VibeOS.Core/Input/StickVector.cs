namespace VibeOS.Core.Input;

/// <summary>A stick reading normalized to the -1..1 range on both axes.
/// Y is positive-up (SDL reports positive-down; the SDL source negates it).</summary>
public readonly record struct StickVector(float X, float Y)
{
    public static readonly StickVector Zero = new(0f, 0f);

    public float Magnitude => MathF.Sqrt(X * X + Y * Y);

    public bool IsZero => X == 0f && Y == 0f;
}
