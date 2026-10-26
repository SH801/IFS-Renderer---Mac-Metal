#nullable enable
using System;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using MacDisplay.Helper;
using MacDisplay.ViewModels;

namespace MacDisplay.Views;

/// <summary>
/// Interaction logic for ConnectionArrow.axaml
/// </summary>
public partial class ConnectionArrow : UserControl
{
    private Path ArrowBody => this.FindControl<Path>("arrowBody") ?? throw new InvalidOperationException("arrowBody not found");
    private Path ArrowClickArea => this.FindControl<Path>("arrowClickArea") ?? throw new InvalidOperationException("arrowClickArea not found");
    private Path ArrowHead => this.FindControl<Path>("arrowHead") ?? throw new InvalidOperationException("arrowHead not found");

    public ConnectionArrow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += (s2, e2) =>
                {
                    if (e2.PropertyName == "ConnectionProps")
                        UpdateGeometry();
                };
            }
            if (ParentNodeMap is not null)
                UpdateGeometry();
        };

        Loaded += (s, e) => UpdateGeometry();
    }

    public static readonly StyledProperty<BindablePoint> StartPointProperty =
        AvaloniaProperty.Register<ConnectionArrow, BindablePoint>(nameof(StartPoint), new BindablePoint(0, 0));

    public BindablePoint StartPoint
    {
        get => GetValue(StartPointProperty);
        set { SetValue(StartPointProperty, value); UpdateGeometry(); }
    }

    public static readonly StyledProperty<BindablePoint> EndPointProperty =
        AvaloniaProperty.Register<ConnectionArrow, BindablePoint>(nameof(EndPoint), new BindablePoint(0, 0));

    public BindablePoint EndPoint
    {
        get => GetValue(EndPointProperty);
        set { SetValue(EndPointProperty, value); UpdateGeometry(); }
    }

    public static readonly StyledProperty<double> ArrowHeadSizeProperty =
        AvaloniaProperty.Register<ConnectionArrow, double>(nameof(ArrowHeadSize), 20.0);

    public double ArrowHeadSize
    {
        get => GetValue(ArrowHeadSizeProperty);
        set => SetValue(ArrowHeadSizeProperty, value);
    }

    public static readonly StyledProperty<double> ArrowCurveProperty =
        AvaloniaProperty.Register<ConnectionArrow, double>(nameof(ArrowCurve), Math.PI / 4.0);

    public double ArrowCurve
    {
        get => GetValue(ArrowCurveProperty);
        set => SetValue(ArrowCurveProperty, value);
    }

    public static readonly StyledProperty<ICommand?> SelectCommandProperty =
        AvaloniaProperty.Register<ConnectionArrow, ICommand?>(nameof(SelectCommand));

    public ICommand? SelectCommand
    {
        get => GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public bool IsLoopback => StartPoint == EndPoint;

    public static readonly StyledProperty<NodeMap?> ParentNodeMapProperty =
        AvaloniaProperty.Register<ConnectionArrow, NodeMap?>(nameof(ParentNodeMap));

    public NodeMap? ParentNodeMap
    {
        get => GetValue(ParentNodeMapProperty);
        set => SetValue(ParentNodeMapProperty, value);
    }

    public void UpdateGeometry()
    {
        if (IsLoopback)
            CalcGeometryToSelf();
        else
            CalcGeometryByPoints();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        SelectCommand?.Execute(DataContext);
    }

    private void CalcGeometryByPoints()
    {
        Point p1 = StartPoint.ToPoint();
        Point p2 = EndPoint.ToPoint();

        double xdir = p2.X - p1.X;
        double ydir = p2.Y - p1.Y;
        double angle = Math.Atan2(ydir, xdir) + ArrowCurve;
        double cosa = Math.Cos(angle);
        double sina = Math.Sin(angle);

        var pg = new PathGeometry
        {
            Figures = new PathFigures
            {
                new PathFigure
                {
                    StartPoint = StartPoint.ToPoint(),
                    Segments = new PathSegments
                    {
                        new PolyBezierSegment
                        {
                            Points = new Points
                            {
                                new Point(
                                    (p1.X * 2 + p2.X) / 3 + 30 * cosa,
                                    (p1.Y * 2 + p2.Y) / 3 + 30 * sina),
                                new Point(
                                    (p1.X + p2.X * 2) / 3 + 30 * cosa,
                                    (p1.Y + p2.Y * 2) / 3 + 30 * sina),
                                p2
                            }
                        }
                    },
                    IsClosed = false
                }
            }
        };
        
        ArrowBody.Data = pg;
        ArrowClickArea.Data = pg;

        Point mid = new(
            (p1.X + p2.X) / 2 + 22 * cosa,
            (p1.Y + p2.Y) / 2 + 22 * sina
        );
        Point dir = new(p2.X - p1.X, p2.Y - p1.Y);
        angle = Math.Atan2(dir.Y, dir.X);

        LineGeometry arrowHeadLeft = new(mid, new Point(
            mid.X - Math.Cos(angle + 0.5) * ArrowHeadSize,
            mid.Y - Math.Sin(angle + 0.5) * ArrowHeadSize));
        LineGeometry arrowHeadRight = new(mid, new Point(
            mid.X - Math.Cos(angle - 0.5) * ArrowHeadSize,
            mid.Y - Math.Sin(angle - 0.5) * ArrowHeadSize));
            
        GeometryGroup arrowHeadGeometry = new() { Children = { arrowHeadLeft, arrowHeadRight } };
        ArrowHead.Data = arrowHeadGeometry;
    }

    private void CalcGeometryToSelf()
    {
        if (DataContext is not ConnectionViewModel vm)
            return;
            
        double loopbackAngle = ParentNodeMap?.CalculateLoopbackAngle(StartPoint.ToPoint()) ?? 0;

        double r = vm.from.NodeSize * 0.4;
        double cosa = Math.Cos(loopbackAngle);
        double sina = Math.Sin(loopbackAngle);
        Point emid = StartPoint.ToPoint() + r * new Vector(cosa, sina);
        
        var pg = new EllipseGeometry
        {
            Center = emid,
            RadiusX = r,
            RadiusY = r
        };
        ArrowBody.Data = pg;
        ArrowClickArea.Data = pg;

        Point mid = StartPoint.ToPoint() + 2.0f * r * new Vector(cosa, sina);
        double a = Math.Atan2(sina, cosa) - 3.1415 / 4.0;
        cosa = Math.Cos(a);
        sina = Math.Sin(a);

        LineGeometry arrowHeadLeft = new(mid, new Point(
            mid.X - ArrowHeadSize * cosa,
            mid.Y - ArrowHeadSize * sina));
        LineGeometry arrowHeadRight = new(mid, new Point(
            mid.X - ArrowHeadSize * sina,
            mid.Y + ArrowHeadSize * cosa));
            
        GeometryGroup arrowHeadGeometry = new() { Children = { arrowHeadLeft, arrowHeadRight } };
        ArrowHead.Data = arrowHeadGeometry;
    }
}