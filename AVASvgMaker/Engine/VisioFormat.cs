using System;
using System.Globalization;
using Avalonia;

namespace AVASvgMaker.Engine;

/// <summary>
/// The handful of conventions a Visio drawing is written in, in one place.
///
/// Visio measures in inches from the bottom-left of the page with y increasing upwards; this
/// editor measures in pixels at 96 to the inch from the top-left with y increasing downwards.
/// Every number that crosses the boundary goes through here, so the flip is done once and in
/// one direction rather than being remembered at each of the dozen places that needs it.
/// </summary>
public static class VisioFormat
{
    /// <summary>Pixels to the inch. Visio stores inches whatever unit it displays.</summary>
    public const double Dpi = 96;

    public const string Main = "http://schemas.microsoft.com/office/visio/2012/main";
    public const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static double ToPixels(double inches) => inches * Dpi;

    public static double ToInches(double pixels) => pixels / Dpi;

    /// <summary>A point on the page, from Visio's frame into this one.</summary>
    public static Point ToPage(double x, double y, double pageHeightInches) =>
        new(ToPixels(x), ToPixels(pageHeightInches - y));

    /// <summary>And back again.</summary>
    public static (double X, double Y) FromPage(Point point, double pageHeightInches) =>
        (ToInches(point.X), pageHeightInches - ToInches(point.Y));

    /// <summary>
    /// Visio turns anticlockwise in radians; this editor turns clockwise in degrees. A shape
    /// that came in turned and goes back out has to arrive at the angle it started at.
    /// </summary>
    public static double ToDegrees(double radians) => DiagramDocument.Normalise(-radians * 180 / Math.PI);

    public static double ToRadians(double degrees) => -degrees * Math.PI / 180;

    public static double Number(string? text, double fallback = 0) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    public static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
