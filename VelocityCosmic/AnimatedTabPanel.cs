using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VelocityCosmic;

public class AnimatedTabPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double totalWidth = 0.0;
        double maxHeight = 0.0;

        foreach (UIElement child in this.InternalChildren)
        {
            child.Measure(availableSize);
            totalWidth += child.DesiredSize.Width;
            maxHeight = Math.Max(maxHeight, child.DesiredSize.Height);
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? totalWidth : availableSize.Width,
            maxHeight
        );
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0.0;

        foreach (UIElement child in this.InternalChildren)
        {
            double width = child.DesiredSize.Width;

            if (!(child.RenderTransform is TranslateTransform transform))
            {
                transform = new TranslateTransform();
                child.RenderTransform = transform;
            }

            var animation = new DoubleAnimation
            {
                To = x,
                Duration = TimeSpan.FromSeconds(0.25),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            transform.BeginAnimation(TranslateTransform.XProperty, animation);
            child.Arrange(new Rect(0.0, 0.0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
