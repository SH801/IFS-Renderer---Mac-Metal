#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MacDisplay.Helper;
using MacDisplay.ViewModels;
using Point = Avalonia.Point;
using Vector = Avalonia.Vector;

namespace MacDisplay.Views;

/// <summary>
/// Interaction logic for NodeMap.axaml
/// </summary>
public partial class NodeMap : UserControl
{
    private IFSViewModel? Vm => DataContext as IFSViewModel;

    public NodeMap()
    {
        InitializeComponent();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Vm?.ConnectingIterator is not null)
        {
            dragArrow.EndPoint = BindablePoint.FromPoint(e.GetPosition(wrapperGrid));
            dragArrow.UpdateGeometry();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Vm?.ConnectingIterator is not null)
        {
            dragArrow.EndPoint = BindablePoint.FromPoint(e.GetPosition(wrapperGrid));
            dragArrow.UpdateGeometry();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Vm is not null)
        {
            Vm.ConnectingIterator = null;
        }
    }

    internal double CalculateLoopbackAngle(Point p)
    {
        if (Vm is null) return 0;
        
        IEnumerable<ConnectionViewModel> arrows = Vm.ConnectionViewModels;
        Vector dir = new Vector(0, 0);
        
        foreach (var c in arrows.Where(nc => nc.StartPoint.ToPoint() == p || nc.EndPoint.ToPoint() == p))
        {
            Vector of = new Vector(
                c.StartPoint.X + c.EndPoint.X,
                c.StartPoint.Y + c.EndPoint.Y
            ) / 2;
            dir += of - (Vector)p;
        }
        return Math.Atan2(-dir.Y, -dir.X);
    }
}