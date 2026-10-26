#nullable enable
using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using MacDisplay.Helper;
using MacDisplay.ViewModels;
using Point = Avalonia.Point;
using Vector = Avalonia.Vector;
using System.Linq;
using Avalonia.Controls.Presenters;

namespace MacDisplay.Views;

/// <summary>
/// Interaction logic for Node.xaml
/// </summary>
public partial class Node : UserControl
{
    private IteratorViewModel? Vm => DataContext as IteratorViewModel;
    private Control? _parentContainer;
    private Vector? _t;

    public static readonly StyledProperty<bool> IsMouseDownProperty =
        AvaloniaProperty.Register<Node, bool>(nameof(IsMouseDown), false);

    public bool IsMouseDown
    {
        get => GetValue(IsMouseDownProperty);
        set => SetValue(IsMouseDownProperty, value);
    }

    public static readonly StyledProperty<ICommand?> SelectCommandProperty =
        AvaloniaProperty.Register<Node, ICommand?>(nameof(SelectCommand));

    public ICommand? SelectCommand
    {
        get => GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public static readonly StyledProperty<BindablePoint> PositionProperty =
        AvaloniaProperty.Register<Node, BindablePoint>(
            nameof(Position), 
            new BindablePoint(0, 0),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay,
            validate: v => true,
            coerce: (s, v) => v);

    static Node()
    {
        PositionProperty.Changed.AddClassHandler<Node>((sender, e) =>
        {
            if (sender._parentContainer is not null && e.NewValue is BindablePoint newPoint)
            {
                Canvas.SetLeft(sender._parentContainer, newPoint.X);
                Canvas.SetTop(sender._parentContainer, newPoint.Y);
            }
        });
    }

    public BindablePoint Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public static readonly StyledProperty<Control?> ParentCanvasProperty =
        AvaloniaProperty.Register<Node, Control?>(nameof(ParentCanvas));

    public Control? ParentCanvas
    {
        get => GetValue(ParentCanvasProperty);
        set => SetValue(ParentCanvasProperty, value);
    }

    public Node()
    {
        InitializeComponent();
        
        Loaded += (s, e) =>
        {
            _parentContainer = this.GetVisualParent() as Control;

            if (DataContext is IteratorViewModel vm)
            {
                Position = vm.Position;
                if (_parentContainer is not null)
                {
                    Canvas.SetLeft(_parentContainer, vm.Position.X);
                    Canvas.SetTop(_parentContainer, vm.Position.Y);
                }
                
                vm.PropertyChanged += (s2, e2) =>
                {
                    if (e2.PropertyName == nameof(vm.Position) && _parentContainer is not null)
                    {
                        Canvas.SetLeft(_parentContainer, vm.Position.X);
                        Canvas.SetTop(_parentContainer, vm.Position.Y);
                    }
                };
            }
        };
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // If the click originated from the drag handle border, skip connection logic
        if (e.Source is Visual visual && 
            (visual == dragHandle || visual.FindAncestorOfType<Border>() == dragHandle))
        {
            return; 
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // If the release originated from the drag handle, it was a node drag—NOT a connection drop!
        if (e.Source is Visual visual && (visual == dragHandle || visual.FindAncestorOfType<Border>() == dragHandle))
        {
            base.OnPointerReleased(e);
            e.Handled = true;
            SetValue(IsMouseDownProperty, false);
            _t = null;
            e.Pointer.Capture(null);
            return;
        }

        base.OnPointerReleased(e);
        e.Handled = true;
        SetValue(IsMouseDownProperty, false);
        _t = null;

        // Find the canvas dynamically via the visual tree
        var canvas = this.FindAncestorOfType<Panel>();
        Point canvasPos = canvas != null ? e.GetPosition(canvas) : default;
        
        // Release pointer capture now
        e.Pointer.Capture(null);
        
        var currentPoint = e.GetCurrentPoint(this);
        if (currentPoint.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            if (Vm is not null)
            {
                IteratorViewModel? targetVm = null;

                if (canvas != null)
                {
                    // Scan all nodes on the canvas and check against their ViewModel center position and size
                    foreach (var node in canvas.GetVisualDescendants().OfType<Node>())
                    {
                        if (node.Vm != null)
                        {
                            double size = node.Vm.NodeSize;
                            double centerX = node.Vm.Position.X;
                            double centerY = node.Vm.Position.Y;

                            var nodeRect = new Rect(centerX - (size / 2) - 25, centerY - (size / 2) - 25, size + 50, size + 50);
                            
                            if (nodeRect.Contains(canvasPos))
                            {
                                targetVm = node.Vm;
                                break;
                            }
                        }
                    }
                }

                // Also ensure we aren't connecting a node to itself!
                if (targetVm is not null && targetVm != Vm)
                {
                    Vm.FinishConnecting(targetVm);
                    SelectCommand?.Execute(targetVm);
                }
            }
        }
    }

    private Point GetCanvasPosition() => 
        _parentContainer is not null ? new Point(Canvas.GetLeft(_parentContainer), Canvas.GetTop(_parentContainer)) : new Point(0, 0);

    private void dragHandle_MouseMove(object sender, PointerEventArgs e)
    {
        if (_t is not null)
        {
            var canvas = this.FindAncestorOfType<Panel>();
            if (canvas is not null)
            {
                var pointerProps = e.GetCurrentPoint(this).Properties;
                if (pointerProps.IsLeftButtonPressed)
                {
                    if (sender is IInputElement inputElement)
                    {
                        e.Pointer.Capture(inputElement);
                    }
                    e.Handled = true;

                    // Compute new position and update the ViewModel
                    Point newPos = e.GetPosition(canvas) + _t.Value;
                    
                    // Direct update to ViewModel position
                    if (Vm is not null)
                    {
                        Vm.Position = BindablePoint.FromPoint(newPos);
                    }
                }
                else
                {
                    // Mouse button released or lost
                    e.Pointer.Capture(null);
                    _t = null;
                    e.Handled = true;
                }
            }
        }
    }
    
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        // If we are currently dragging the node via the drag handle, ignore pointer exits!
        if (_t is not null)
        {
            return;
        }

        var pointerProps = e.GetCurrentPoint(this).Properties;
        if (pointerProps.IsLeftButtonPressed)
        {
            e.Handled = true;
            Vm?.StartConnecting();
        }
    }

    private void UserControl_GotFocus(object sender, FocusChangedEventArgs e)
    {
        if (Vm is not null)
        {
            SelectCommand?.Execute(Vm);
        }
    }

    private void Label_MouseDown(object sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed)
        {
            var canvas = this.FindAncestorOfType<Panel>();
            if (canvas is not null)
            {
                _t = GetCanvasPosition() - e.GetPosition(canvas);
            }
        }
    }

    private void ellipseBody_MouseDown(object sender, PointerPressedEventArgs e) => Focus();
}