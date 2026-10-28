#nullable enable
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using MacDisplay.ViewModels;

namespace MacDisplay.Views.Animation;

/// <summary>
/// Interaction logic for DopeButton.axaml.cs
/// </summary>
public partial class DopeButton : Button
{
    public DopeButton()
    {
        InitializeComponent();
    }

    private Point _dragStartPoint;
    private bool _hasDragged = false;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(TopLevel.GetTopLevel(this));
        if (point.Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(TopLevel.GetTopLevel(this));
            _hasDragged = false; // Reset drag state on fresh press

            var animVm = FindAnimationViewModel();
            if (animVm != null && DataContext is KeyframeViewModel kfVm)
            {
                var modifiers = e.KeyModifiers;
                bool isMultiSelect = modifiers.HasFlag(KeyModifiers.Shift) || 
                                    modifiers.HasFlag(KeyModifiers.Meta) || 
                                    modifiers.HasFlag(KeyModifiers.Control);

                if (!isMultiSelect)
                {
                    animVm.ClearSelection();
                }
                animVm.AddToSelection(kfVm);
            }

            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.None);
            e.Handled = true;
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            // Do NOT set e.Handled = true here!
            // Letting it propagate allows the ContextMenu to open automatically.
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (e.Pointer.Captured == this)
        {
            var currentPosition = e.GetPosition(TopLevel.GetTopLevel(this));
            double delta = currentPosition.X - _dragStartPoint.X;

            // Once moved more than 3 pixels, lock into Dragging Mode permanently for this press
            if (!_hasDragged && Math.Abs(delta) > 3.0)
            {
                _hasDragged = true;
            }

            if (_hasDragged)
            {
                var animVm = FindAnimationViewModel();
                if (animVm != null && DataContext is KeyframeViewModel kfVm)
                {
                    animVm.PreviewRepositionSelectedKeyframes(delta);
                }
            }

            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (e.InitialPressMouseButton == MouseButton.Left && e.Pointer.Captured == this)
        {
            Cursor = Cursor.Default;
            e.Pointer.Capture(null);

            var animVm = FindAnimationViewModel();
            if (animVm != null && DataContext is KeyframeViewModel kfVm)
            {
                if (_hasDragged)
                {
                    // We dragged: Commit the new position. DO NOT jump playhead!
                    if (animVm.KeyframeRepositionOffset != 0.0)
                    {
                        animVm.ApplyRepositionOfSelectedKeyframes();
                    }
                }
                else
                {
                    // We did NOT drag (pure click): Flip selection and jump playhead
                    animVm.FlipSelection(kfVm);
                    if (Command?.CanExecute(DataContext) == true)
                    {
                        Command.Execute(DataContext);
                    }
                }
            }

            _hasDragged = false;
            e.Handled = true;
        }
    }

    // Helper to find the AnimationViewModel up the tree
    private AnimationViewModel? FindAnimationViewModel()
    {
        StyledElement? current = this;
        while (current != null)
        {
            // If the parent context or container exposes AnimationViewModel
            if (current.DataContext is AnimationViewModel avm) return avm;
            if (current.DataContext is MainViewModel mvm) return mvm.AnimationViewModel;
            
            current = current.Parent as StyledElement;
        }

        // Fallback to window
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            if (window.DataContext is MainViewModel main) return main.AnimationViewModel;
        }

        return null;
    }
 
}