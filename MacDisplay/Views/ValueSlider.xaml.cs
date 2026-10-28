#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using MacDisplay.ViewModels;

namespace MacDisplay.Views;

/// <summary>
/// Interaction logic for ValueSlider.axaml.cs
/// </summary>
public partial class ValueSlider : UserControl
{
    [LibraryImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int X, int Y);

    private static double IncrementMultiplier
    {
        get
        {
            var modifiers = KeyModifiers.None; 
            bool ctrl = modifiers.HasFlag(KeyModifiers.Control);
            bool shift = modifiers.HasFlag(KeyModifiers.Shift);
            
            double mult = 1.0;
            if (ctrl) mult *= 10.0;
            if (shift) mult *= 0.1;
            return mult;
        }
    }

    private void UpdateLabelFormat() { }

    private MainViewModel? FindMainViewModel()
    {
        // 1. Check direct DataContext first
        if (DataContext is MainViewModel directMain) return directMain;

        // 2. Walk up the StyledElement parent chain
        StyledElement? current = this.Parent as StyledElement;
        while (current != null)
        {
            if (current.DataContext is MainViewModel main)
            {
                return main;
            }
            current = current.Parent as StyledElement;
        }

        // 3. Fallback to Window DataContext
        if (TopLevel.GetTopLevel(this) is Window window && window.DataContext is MainViewModel windowMain)
        {
            return windowMain;
        }

        // 4. Final fallback to desktop lifetime if everything else fails
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
            && desktop.MainWindow?.DataContext is MainViewModel rootMain)
        {
            return rootMain;
        }

        return null;
    }

    private void UpdateAnimatedSymbol()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            char symbol = ' ';
            IBrush symbolColor = Brushes.Gray;

            if (!string.IsNullOrEmpty(AnimationPath))
            {
                // Walk up the tree to find MainViewModel cleanly
                var mainVm = FindMainViewModel();
                if (mainVm?.AnimationViewModel != null)
                {
                    var animatedState = mainVm.AnimationViewModel.GetChannelCurrentState(AnimationPath);
                    symbol = animatedState switch
                    {
                        null => '◇',
                        false => '◆',
                        true => '◈',
                    };

                    if (animatedState is not null)
                    {
                        var isValueModified = mainVm.AnimationViewModel.IsChannelBeingModified(AnimationPath, Value);
                        symbolColor = isValueModified ? Brushes.Orange : Brushes.White;
                    }
                }
            }

            if (symbolButton1 != null)
            {
                symbolButton1.Content = symbol;
                symbolButton1.Foreground = symbolColor;
            }
            if (symbolButton2 != null)
            {
                symbolButton2.Content = symbol;
                symbolButton2.Foreground = symbolColor;
            }
        });
    }

    public static readonly StyledProperty<ValueSliderSettings?> SliderSettingsProperty =
        AvaloniaProperty.Register<ValueSlider, ValueSliderSettings?>(nameof(SliderSettings));

    public ValueSliderSettings? SliderSettings
    {
        get => GetValue(SliderSettingsProperty);
        set => SetValue(SliderSettingsProperty, value);
    }

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<ValueSlider, string?>(nameof(Label));

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly StyledProperty<bool> IsLabelShownProperty =
        AvaloniaProperty.Register<ValueSlider, bool>(nameof(IsLabelShown), true);

    public bool IsLabelShown
    {
        get => GetValue(IsLabelShownProperty);
        set => SetValue(IsLabelShownProperty, value);
    }

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ValueSlider, double>(
            nameof(Value), 
            defaultBindingMode: BindingMode.TwoWay);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly StyledProperty<double> DefaultValueProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(DefaultValue), 0.0);

    public double DefaultValue
    {
        get => GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    public static readonly StyledProperty<double> IncrementProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Increment), 0.1);

    public double Increment
    {
        get => GetValue(IncrementProperty);
        set => SetValue(IncrementProperty, value);
    }

    public static readonly StyledProperty<double?> MinValueProperty =
        AvaloniaProperty.Register<ValueSlider, double?>(nameof(MinValue));

    public double? MinValue
    {
        get => GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    public static readonly StyledProperty<double?> MaxValueProperty =
        AvaloniaProperty.Register<ValueSlider, double?>(nameof(MaxValue));

    public double? MaxValue
    {
        get => GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public static readonly StyledProperty<bool> EditingProperty =
        AvaloniaProperty.Register<ValueSlider, bool>(nameof(Editing));

    public bool Editing
    {
        get => GetValue(EditingProperty);
        set => SetValue(EditingProperty, value);
    }

    public static readonly StyledProperty<string?> AnimationPathProperty =
        AvaloniaProperty.Register<ValueSlider, string?>(nameof(AnimationPath));

    public string? AnimationPath
    {
        get => GetValue(AnimationPathProperty);
        set => SetValue(AnimationPathProperty, value);
    }

    public static readonly StyledProperty<Action?> ValueWillChangeActionProperty =
        AvaloniaProperty.Register<ValueSlider, Action?>(nameof(ValueWillChangeAction));

    public Action? ValueWillChangeAction
    {
        get => GetValue(ValueWillChangeActionProperty);
        set => SetValue(ValueWillChangeActionProperty, value);
    }

    public static readonly StyledProperty<Action<double>?> ValueChangedActionProperty =
        AvaloniaProperty.Register<ValueSlider, Action<double>?>(nameof(ValueChangedAction));

    public Action<double>? ValueChangedAction
    {
        get => GetValue(ValueChangedActionProperty);
        set => SetValue(ValueChangedActionProperty, value);
    }

    public static readonly StyledProperty<Action?> ValueDraggingStartedActionProperty =
        AvaloniaProperty.Register<ValueSlider, Action?>(nameof(ValueDraggingStartedAction));

    public Action? ValueDraggingStartedAction
    {
        get => GetValue(ValueDraggingStartedActionProperty);
        set => SetValue(ValueDraggingStartedActionProperty, value);
    }

    public static readonly StyledProperty<Action<double>?> ValueDraggingCompletedActionProperty =
        AvaloniaProperty.Register<ValueSlider, Action<double>?>(nameof(ValueDraggingCompletedAction));

    public Action<double>? ValueDraggingCompletedAction
    {
        get => GetValue(ValueDraggingCompletedActionProperty);
        set => SetValue(ValueDraggingCompletedActionProperty, value);
    }

    private ICommand? _resetValueCommand;
    public ICommand ResetValueCommand => _resetValueCommand ??= new RelayCommand(() =>
    {
        ValueWillChangeAction?.Invoke();
        Value = (Value == DefaultValue) ? 0.0 : DefaultValue;
        ValueChangedAction?.Invoke(Value);
    });

    public static readonly StyledProperty<ICommand?> AnimateCommandProperty =
        AvaloniaProperty.Register<ValueSlider, ICommand?>(nameof(AnimateCommand));

    public ICommand? AnimateCommand
    {
        get => GetValue(AnimateCommandProperty);
        set => SetValue(AnimateCommandProperty, value);
    }

    private RelayCommand? _internalAnimateCommand;
    public RelayCommand InternalAnimateCommand => _internalAnimateCommand ??= new RelayCommand(() =>
    {
        AnimateCommand?.Execute(new AnimationViewModel.AnimateValueCommandParameters(Label ?? string.Empty, AnimationPath ?? string.Empty, Value));
        UpdateAnimatedSymbol();
    }, () => AnimationPath is not null);

    static ValueSlider()
    {
        SliderSettingsProperty.Changed.AddClassHandler<ValueSlider>((slider, e) =>
        {
            if (e.NewValue is ValueSliderSettings settings)
            {
                if (settings.Label != null) slider.Label = settings.Label;
                if (settings.IsLabelShown.HasValue) slider.IsLabelShown = settings.IsLabelShown.Value;
                if (settings.DefaultValue.HasValue) slider.DefaultValue = settings.DefaultValue.Value;
                if (settings.MinValue.HasValue) slider.MinValue = settings.MinValue.Value;
                if (settings.MaxValue.HasValue) slider.MaxValue = settings.MaxValue.Value;
                if (settings.Increment.HasValue) slider.Increment = settings.Increment.Value;
                if (settings.AnimationPath != null) slider.AnimationPath = settings.AnimationPath;
                if (settings.ValueWillChange != null) slider.ValueWillChangeAction = settings.ValueWillChange;
                if (settings.ValueChanged != null) slider.ValueChangedAction = settings.ValueChanged;
                if (settings.ValueDraggingStarted != null) slider.ValueDraggingStartedAction = settings.ValueDraggingStarted;
                if (settings.ValueDraggingCompleted != null) slider.ValueDraggingCompletedAction = settings.ValueDraggingCompleted;
                if (settings.AnimateCommand != null) slider.AnimateCommand = settings.AnimateCommand;
                if (settings.ToolTip != null) ToolTip.SetTip(slider, settings.ToolTip); // Use attached property
            }
        });

        ValueProperty.Changed.AddClassHandler<ValueSlider>((slider, _) => slider.UpdateAnimatedSymbol());
        IncrementProperty.Changed.AddClassHandler<ValueSlider>((slider, _) => slider.UpdateLabelFormat());
        AnimationPathProperty.Changed.AddClassHandler<ValueSlider>((slider, _) =>
        {
            slider.UpdateAnimatedSymbol();
            slider._internalAnimateCommand?.NotifyCanExecuteChanged(); // Updated method name
        });
    }

    public ValueSlider()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window && window.DataContext is MainViewModel main)
            {
                main.workspace.PropertyChanged += (_, _) => UpdateAnimatedSymbol();
                AnimateCommand = main.AnimationViewModel.AnimateValueCommand;
                UpdateLabelFormat();
            }
        };
    }

    private double ConstrainValue(double value)
    {
        if (MinValue != null)
            value = Math.Max(MinValue.Value, value);
        if (MaxValue != null)
            value = Math.Min(MaxValue.Value, value);
        return value;
    }

    private void Up_Click(object? sender, RoutedEventArgs e)
    {
        ValueWillChangeAction?.Invoke();
        Value = ConstrainValue(Value + Increment * IncrementMultiplier);
        ValueChangedAction?.Invoke(Value);
    }

    private void Down_Click(object? sender, RoutedEventArgs e)
    {
        ValueWillChangeAction?.Invoke();
        Value = ConstrainValue(Value - Increment * IncrementMultiplier);
        ValueChangedAction?.Invoke(Value);
    }

    private void ValueEditor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Return or Key.Enter)
        {
            e.Handled = true;
            ValueWillChangeAction?.Invoke();
            StartEditing();
            ValueChangedAction?.Invoke(Value);
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            Up_Click(sender, e);
        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
            Down_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Value = _lastv;
            Editing = false;
            ValueChangedAction?.Invoke(Value);
        }
    }

    private Point _dragp;
    private double _lastv;

    private void DisplayLabel_MouseMove(object? sender, PointerEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (TopLevel.GetTopLevel(this) is Window window)
            {
                if (e.Pointer.Captured != displayLabel)
                {
                    ValueWillChangeAction?.Invoke();
                    ValueDraggingStartedAction?.Invoke();
                    _dragp = e.GetPosition(window);
                    _lastv = Value;
                    e.Pointer.Capture(displayLabel);
                }

                double delta = e.GetPosition(window).X - _dragp.X;
                Value = ConstrainValue(_lastv + delta * Increment * IncrementMultiplier);
                ValueChangedAction?.Invoke(Value);
            }
        }
    }

    private void DisplayLabel_MouseUp(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            if (e.Pointer.Captured != displayLabel)
            {
                StartEditing();
            }
            else
            {
                ValueDraggingCompletedAction?.Invoke(Value);
            }
        }
        
        if (e.Pointer.Captured == displayLabel)
        {
            e.Pointer.Capture(null);
        }
    }

    private void StartEditing()
    {
        _lastv = Value;
        Editing = true;
        valueEditor.Focus();
        valueEditor.SelectAll();
    }

    private void valueSlider_GotKeyboardFocus(object? sender, FocusChangedEventArgs e)
    {
        StartEditing();
    }

    private void ValueEditor_MouseDoubleClick(object? sender, TappedEventArgs e)
    {
        ResetValueCommand.Execute(null);
        StartEditing();
    }

    private void ValueEditor_LostFocus(object? sender, RoutedEventArgs e) => Editing = false;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 1. Find the MainViewModel via our robust tree walker or fallbacks
        var mainVm = FindMainViewModel();
        if (mainVm != null)
        {
            // Subscribe to workspace property changes safely
            mainVm.workspace.PropertyChanged -= Workspace_PropertyChanged;
            mainVm.workspace.PropertyChanged += Workspace_PropertyChanged;

            // Wire up the animation command if available
            if (mainVm.AnimationViewModel != null)
            {
                AnimateCommand = mainVm.AnimationViewModel.AnimateValueCommand;
            }
        }

        // 3. Ensure the keyframe button updates its visibility and state
        UpdateAnimatedSymbol();
    }

    private void Workspace_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        UpdateAnimatedSymbol();
    }    
}