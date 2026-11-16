namespace VpnHood.Net.Toolkit.Graphics;

/// <summary>Represents a rectangle by its position and size.</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct VhRect(double X, double Y, double Width, double Height);
