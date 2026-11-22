using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace LiveScreenshot;

// Draws a short-lived, click-through ping in physical desktop coordinates.
internal sealed partial class PingOverlay : Window
{
    private const int Diameter = 184;
    private const int ExtendedStyle = -20;
    private const int TransparentStyle = 0x20;
    private const int NoActivateStyle = 0x08000000;
    private const uint NoActivate = 0x0010;
    private static readonly nint TopmostWindow = new(-1);
    private const double RingOpacity = 0.82;
    private const int PulseCount = 5;
    private const double OutlineThickness = 4;
    private const double InnerMaximumDiameter = 43;
    private const double InnerMinimumDiameter = InnerMaximumDiameter * 0.72;
    private const double OuterMinimumDiameter = InnerMaximumDiameter * 2;
    private static readonly double[] MinimumDiameters =
        [InnerMinimumDiameter, (InnerMinimumDiameter + OuterMinimumDiameter) / 2, OuterMinimumDiameter];
    private static readonly double[] MaximumDiameters =
        [InnerMaximumDiameter, InnerMaximumDiameter * 2, InnerMaximumDiameter * 3];
    private readonly Grid rings;

    // Configures the transparent overlay and its three concentric rings.
    internal PingOverlay()
    {
        Width = Diameter;
        Height = Diameter;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;
        rings = new Grid
        {
            Width = Diameter,
            Height = Diameter,
            Opacity = RingOpacity,
        };
        foreach (double minimumDiameter in MinimumDiameters)
        {
            Grid ring = new()
            {
                Width = minimumDiameter + OutlineThickness,
                Height = minimumDiameter + OutlineThickness,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ring.Children.Add(new Ellipse { Stroke = Brushes.Black, StrokeThickness = OutlineThickness });
            ring.Children.Add(new Ellipse { Stroke = Brushes.Gold, StrokeThickness = 2.5, Margin = new Thickness(0.75) });
            rings.Children.Add(ring);
        }

        Content = new Viewbox { Child = rings };
    }

    // Shows the overlay without activating it and starts its animation.
    internal void ShowAt(Point pixel)
    {
        nint handle = new WindowInteropHelper(this).EnsureHandle();
        int style = GetWindowLong(handle, ExtendedStyle);
        if (style == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        if (SetWindowLong(handle, ExtendedStyle, style | TransparentStyle | NoActivateStyle) == 0 &&
            Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        Show();
        if (!SetWindowPos(handle, TopmostWindow, (int)pixel.X - Diameter / 2, (int)pixel.Y - Diameter / 2,
            Diameter, Diameter, NoActivate))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        AnimateRings();
    }

    // Pulses the rings, then fades and closes the overlay.
    private void AnimateRings()
    {
        TimeSpan halfPulse = TimeSpan.FromMilliseconds(230);
        for (int index = 0; index < MaximumDiameters.Length; index++)
        {
            Grid ring = (Grid)rings.Children[index];
            DoubleAnimation pulse = new(ring.Width, MaximumDiameters[index] + OutlineThickness, halfPulse)
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(PulseCount),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            ring.BeginAnimation(WidthProperty, pulse);
            ring.BeginAnimation(HeightProperty, pulse);
        }
        DoubleAnimation fade = new(RingOpacity, 0, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = halfPulse * (2 * PulseCount),
        };
        fade.Completed += (_, _) =>
        {
            if (IsVisible)
            {
                Close();
            }
        };
        rings.BeginAnimation(OpacityProperty, fade);
    }

    // Reads the overlay's extended window style.
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static partial int GetWindowLong(nint window, int index);

    // Applies click-through and no-activate extended styles.
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong(nint window, int index, int style);

    // Positions the overlay in physical screen coordinates.
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
